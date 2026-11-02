using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FishTank;

internal sealed class LiveDetectionSession : IDisposable
{
    internal const string Channel = "Microsoft-Windows-Windows Defender/Operational";
    private const string Query = "*[System[(EventID=1116 or EventID=1117 or EventID=1118 or EventID=1119 or EventID=1121 or EventID=1122 or EventID=1123 or EventID=1124 or EventID=5001 or EventID=5007)]]";
    private readonly object gate = new object();
    private readonly object persistence = new object();
    private readonly LiveDetectionBuffer history = new LiveDetectionBuffer();
    private readonly Action<LiveDetectionEvent> notify;
    private EventLogWatcher watcher;
    private Timer timer;
    private bool enabled, disposed, subscribed, dirty;
    private int generation, polling;
    private int watcherFailures;
    private bool reportedDisabled;
    private LiveProtectionReading reading;
    private DateTime checkedUtc;
    private DateTime lastPollAttempt;
    private DateTime lastSubscribeAttempt;
    private DateTime lastSave = DateTime.MinValue;
    private string storageError = "";
    private readonly Dictionary<string, DateTime> notices = new Dictionary<string, DateTime>();
    internal bool Enabled { get { lock (gate) return enabled; } }
    internal string Status
    {
        get { lock (gate) return (enabled && reportedDisabled ? "Protection warning · Defender reported real-time protection disabled; current status needs review. " : "") + LiveDetectionPolicy.Coverage(enabled, subscribed, reading, checkedUtc, DateTime.UtcNow) + (storageError == "" ? "" : " History storage is unavailable; some alerts may not be saved."); }
    }
    internal LiveDetectionSession(Action<LiveDetectionEvent> notify)
    {
        this.notify = notify;
        try { var previous = ProtectedStore.Read("live-detection-history", () => new List<LiveDetectionEvent>()).TakeLastCompatible(250).ToList(); foreach (var item in previous) { item.RecordedUtc = item.RecordedUtc.ToUniversalTime(); item.EventUtc = item.EventUtc.ToUniversalTime(); } history.Restore(previous); }
        catch
        {
            storageError = "Unable to read previous history.";
            history.Add(new LiveDetectionEvent { RecordedUtc = DateTime.UtcNow, EventUtc = DateTime.UtcNow, Kind = "Coverage gap", Summary = "Previous live history could not be read. Historical coverage is unknown; the original encrypted store may need recovery." });
        }
    }
    internal void Start()
    {
        lock (gate)
        {
            if (disposed || enabled) return;
            enabled = true; generation++; reading = null; checkedUtc = DateTime.MinValue; lastPollAttempt = DateTime.MinValue; lastSubscribeAttempt = DateTime.MinValue; reportedDisabled = false;
            Add(new LiveDetectionEvent { Kind = "Session started", Summary = "FishTank live alerts started. Events while it was off or disconnected may be missing; existing history is not proof of complete coverage." });
        }
        lock (gate) if (enabled && !disposed) timer = new Timer(Tick, null, 0, 10000);
    }
    private void Attach()
    {
        EventLogWatcher previous; int request, failures;
        lock (gate) { if (!enabled || disposed || subscribed || (DateTime.UtcNow >= lastSubscribeAttempt && DateTime.UtcNow - lastSubscribeAttempt < TimeSpan.FromMinutes(1))) return; lastSubscribeAttempt = DateTime.UtcNow; request = generation; failures = watcherFailures; previous = watcher; watcher = null; }
        Release(previous);
        EventLogWatcher candidate = null;
        try
        {
            candidate = new EventLogWatcher(new EventLogQuery(Channel, PathType.LogName, Query), null, false);
            candidate.EventRecordWritten += EventWritten;
            lock (gate) { if (!enabled || disposed || generation != request) { candidate.Dispose(); return; } watcher = candidate; }
            candidate.Enabled = true;
            lock (gate) if (enabled && !disposed && generation == request && watcher == candidate) subscribed = watcherFailures == failures;
        }
        catch
        {
            lock (gate)
            {
                if (watcher == candidate) watcher = null;
                if (enabled && !disposed && generation == request) { subscribed = false; Add(new LiveDetectionEvent { Kind = "Coverage gap", Summary = "Defender event subscription is unavailable. Permissions, disabled/absent logs, or a different antivirus can limit coverage; no elevation or settings change was attempted." }); }
            }
            Release(candidate);
        }
    }
    private void EventWritten(object sender, EventRecordWrittenEventArgs args)
    {
        try { using (var record = args.EventRecord)
        {
            lock (gate)
            {
                if (!enabled || disposed || sender != watcher) return;
                if (args.EventException != null)
                {
                    subscribed = false; watcherFailures++;
                    Add(new LiveDetectionEvent { Kind = "Coverage gap", Summary = "Defender log delivery failed. Live events may be missed until a subscription can be restored." }); return;
                }
                if (record == null || record.ProviderName != "Microsoft-Windows-Windows Defender") return;
                string kind = LiveDetectionPolicy.Kind(record.Id); if (kind == null) return;
                if (record.Id == 5001 || record.Id == 5007) { reading = null; checkedUtc = DateTime.MinValue; }
                if (record.Id == 5001) reportedDisabled = true;
                string details;
                try { details = LiveDetectionDetails.Read(record.ToXml()); } catch { details = "Event details could not be read; review the original event in Windows Security."; }
                Add(new LiveDetectionEvent { EventId = record.Id, RecordId = record.RecordId ?? 0, EventUtc = record.TimeCreated?.ToUniversalTime() ?? DateTime.UtcNow,
                    Kind = kind, Summary = LiveDetectionPolicy.Summary(record.Id), Details = details });
            }
        } }
        catch { lock (gate) if (enabled && !disposed && sender == watcher) Add(new LiveDetectionEvent { Kind = "Coverage gap", Summary = "A Defender record could not be safely read. Its details may be missing; inspect the Windows protection history." }); }
    }
    private void Add(LiveDetectionEvent item)
    {
        item.RecordedUtc = DateTime.UtcNow; if (item.EventUtc == DateTime.MinValue) item.EventUtc = item.RecordedUtc;
        if (!history.Add(item)) return;
        dirty = true;
        if (item.Kind.StartsWith("Session", StringComparison.Ordinal) || item.Kind == "Coverage status") return;
        string group = item.Kind == "Coverage gap" || item.Kind == "Coverage warning" ? "gap" : item.Kind == "Protection configuration changed" ? "configuration" : item.Kind == "Defender action failed" || item.Kind == "Protection disabled" || item.Kind == "Protection status warning" ? "urgent" : "activity";
        TimeSpan interval = group == "gap" || group == "configuration" ? TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(15);
        if (notices.TryGetValue(group, out DateTime previous) && item.RecordedUtc >= previous && item.RecordedUtc - previous < interval) return;
        notices[group] = item.RecordedUtc;
        try { notify?.Invoke(item); } catch { /* History/status remain available if the UI is closing. */ }
    }
    private async void Tick(object state)
    {
        try { Flush(); Attach(); } catch { lock (gate) storageError = "Unable to flush live history or refresh its subscription."; }
        if (Interlocked.Exchange(ref polling, 1) != 0) return;
        int request;
        try
        {
            lock (gate) { if (!enabled || disposed) return; request = generation; }
            bool poll;
            lock (gate) { if (!enabled || disposed || generation != request) return; poll = lastPollAttempt == DateTime.MinValue || DateTime.UtcNow < lastPollAttempt || DateTime.UtcNow - lastPollAttempt >= TimeSpan.FromMinutes(1); if (poll) lastPollAttempt = DateTime.UtcNow; }
            if (poll)
            {
                LiveProtectionReading result;
                try { result = (await Task.Run(() => Core.Parse<LiveProtectionReading[]>(Core.Collect("LiveProtection")))).Single(); }
                catch { result = new LiveProtectionReading { Error = "Current Defender status could not be read." }; }
                lock (gate)
                {
                    if (!enabled || disposed || generation != request) return;
                    string before = LiveDetectionPolicy.Coverage(enabled, subscribed, reading, checkedUtc, DateTime.UtcNow);
                    reading = result; checkedUtc = DateTime.UtcNow;
                    if (string.IsNullOrEmpty(result.Error) && result.RealTimeProtectionEnabled != null && result.AntivirusEnabled != null) reportedDisabled = false;
                    string after = LiveDetectionPolicy.Coverage(enabled, subscribed, reading, checkedUtc, DateTime.UtcNow);
                    if (before != after)
                    {
                        string kind = result.RealTimeProtectionEnabled == false || result.AntivirusEnabled == false ? "Protection status warning" : after.StartsWith("Listening to Defender", StringComparison.Ordinal) ? "Coverage status" : "Coverage warning";
                        Add(new LiveDetectionEvent { Kind = kind, Summary = after, Details = "Read-only Get-MpComputerStatus observation; no protection settings were changed." });
                    }
                }
            }
            Flush();
        }
        catch { lock (gate) { if (enabled && !disposed) storageError = "Live status/history work failed."; } }
        finally { Interlocked.Exchange(ref polling, 0); }
    }
    private void Flush()
    {
        lock (persistence)
        {
            List<LiveDetectionEvent> snapshot;
            lock (gate) { if (!dirty || (DateTime.UtcNow >= lastSave && DateTime.UtcNow - lastSave < TimeSpan.FromSeconds(2))) return; snapshot = history.Snapshot(); dirty = false; lastSave = DateTime.UtcNow; }
            try { ProtectedStore.Write("live-detection-history", snapshot); lock (gate) storageError = ""; }
            catch { lock (gate) { dirty = true; storageError = "Unable to save live history."; } }
        }
    }
    internal List<LiveDetectionEvent> Events() => history.Snapshot();
    internal string Report() => Core.Json(new { status = Status, scope = "Defender-reported events only; retained history is limited to 250 records. No guarantee of complete detection. Event details can contain private paths.", events = Events() });
    internal void Stop()
    {
        EventLogWatcher previous;
        lock (gate)
        {
            if (!enabled) return;
            enabled = false; generation++; subscribed = false; timer?.Dispose(); timer = null;
            previous = watcher; watcher = null;
            Add(new LiveDetectionEvent { Kind = "Session stopped", Summary = "FishTank live alerts stopped. Defender's own protection was not changed; subsequent events are not captured by this session." });
            lastSave = DateTime.MinValue;
        }
        Release(previous); Flush();
    }
    private static void Release(EventLogWatcher value) { try { value?.Dispose(); } catch { /* Stale callbacks are rejected by session identity/generation. */ } }
    public void Dispose() { Stop(); lock (gate) disposed = true; }
}
internal static class LiveHistoryExtensions
{
    internal static IEnumerable<T> TakeLastCompatible<T>(this IEnumerable<T> values, int count) { var array = values.ToArray(); return array.Skip(Math.Max(0, array.Length - count)); }
}
