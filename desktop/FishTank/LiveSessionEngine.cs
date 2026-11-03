using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace FishTank;
public sealed class LiveCheckpoint
{
    public int Version = 1;
    public LiveEventCursor Cursor = new LiveEventCursor();
    public List<LiveDetectionEvent> Events = new List<LiveDetectionEvent>();
    public long Captured, Trimmed;
    public DateTime SavedUtc;
}
internal interface ILiveCheckpointStore { LiveCheckpoint Read(); void Write(LiveCheckpoint checkpoint); }
internal sealed class ProtectedLiveCheckpointStore : ILiveCheckpointStore
{
    public LiveCheckpoint Read()
    {
        var saved = ProtectedStore.Read<LiveCheckpoint>("live-checkpoint", () => null);
        if (saved != null) return saved;
        // Migration reads the old store; corrupt/unreadable data is never replaced with defaults.
        var legacy = ProtectedStore.Read("live-detection-history", () => new List<LiveDetectionEvent>()) ?? throw new IOException("Legacy live history is unreadable.");
        return new LiveCheckpoint { Events = legacy };
    }
    public void Write(LiveCheckpoint checkpoint) => ProtectedStore.Write("live-checkpoint", checkpoint);
}
internal sealed class LiveSessionEngine : IDisposable
{
    private readonly object gate = new object(), persistence = new object();
    private readonly ILiveEventSource source;
    private readonly ILiveCheckpointStore store;
    private readonly Func<DateTime> clock;
    private readonly Func<LiveProtectionReading> read;
    private readonly Action<LiveDetectionEvent> notify;
    private readonly bool useTimer;
    private readonly LiveDetectionBuffer history = new LiveDetectionBuffer(1000);
    private IDisposable subscription;
    private Timer timer;
    private bool enabled, disposed, subscribed, logHealthy, caughtUp, storageBlocked;
    private int generation, subscribing, cycling;
    private long subscriptionToken, revision, savedRevision, protectionRevision, captured, previousTrimmed;
    private DateTime checkedUtc, completedUtc, logCheckedUtc, lastPollAttempt, lastSubscribeAttempt, lastReconcileAttempt;
    private string storageError = "", deliveryError = "Not checked yet.";
    private LiveEventCursor cursor = new LiveEventCursor();
    private LiveProtectionReading reading;
    private readonly Dictionary<string, DateTime> lastNotice = new Dictionary<string, DateTime>();
    private readonly Dictionary<string, int> pendingNotices = new Dictionary<string, int>();
    private readonly HashSet<string> activeGaps = new HashSet<string>();
    internal bool Enabled { get { lock (gate) return enabled; } }
    internal LiveSessionEngine(Action<LiveDetectionEvent> notify, ILiveEventSource source = null, Func<LiveProtectionReading> read = null, Func<DateTime> clock = null, ILiveCheckpointStore store = null, bool useTimer = true)
    {
        this.notify = notify; this.source = source ?? new WindowsLiveEventSource(); this.clock = clock ?? (() => DateTime.UtcNow); this.store = store ?? new ProtectedLiveCheckpointStore(); this.useTimer = useTimer;
        this.read = read ?? (() => Core.Parse<LiveProtectionReading[]>(Core.Collect("LiveProtection", 15000)).Single());
        try {
            var checkpoint = this.store.Read();
            if (checkpoint == null || checkpoint.Version != 1 || checkpoint.Cursor == null || checkpoint.Events == null || checkpoint.Events.Count > 5000) throw new IOException("Invalid live checkpoint.");
            ValidateCursor(checkpoint.Cursor);
            if (checkpoint.Captured < 0 || checkpoint.Trimmed < 0) throw new IOException("Invalid checkpoint counters.");
            foreach (var item in checkpoint.Events) Validate(item);
            history.Restore(checkpoint.Events); cursor = Clone(checkpoint.Cursor); captured = checkpoint.Captured; previousTrimmed = checkpoint.Trimmed;
        } catch {
            storageBlocked = true; storageError = "Checkpoint/history could not be read. Existing data was preserved; saving is blocked until storage is repaired and monitoring restarted.";
            Add(new LiveDetectionEvent { Kind = "Coverage gap", Severity = 1, Summary = storageError }, false);
        }
    }
    private static LiveEventCursor Clone(LiveEventCursor value) => new LiveEventCursor { RecordId = value.RecordId, EventId = value.EventId, EventUtc = value.EventUtc };
    private static void ValidateCursor(LiveEventCursor value)
    {
        if (value == null || value.RecordId < 0 || (value.RecordId > 0 && (value.EventId <= 0 || value.EventUtc == DateTime.MinValue))) throw new IOException("Invalid log anchor.");
    }
    private static void ValidateBatch(LiveReplayBatch batch, LiveEventCursor from)
    {
        if (batch == null || batch.Events == null || batch.Events.Count > 500) throw new IOException("Invalid reconciliation batch.");
        ValidateCursor(batch.Cursor);
        if (batch.Gap == null && batch.Cursor.RecordId < from.RecordId) throw new IOException("Unexplained checkpoint rewind.");
        long previous = batch.Gap == null ? from.RecordId : 0;
        foreach (var item in batch.Events) {
            Validate(item);
            if (item.RecordId <= previous || item.RecordId > batch.Cursor.RecordId || item.EventUtc == DateTime.MinValue || LiveDetectionPolicy.Kind(item.EventId) == null) throw new IOException("Invalid reconciliation event order or identity.");
            previous = item.RecordId;
        }
        if (!batch.CaughtUp && batch.Cursor.RecordId <= from.RecordId && batch.Gap == null) throw new IOException("Backfill made no progress.");
    }
    private static void Validate(LiveDetectionEvent item)
    {
        if (item == null || (item.Kind ?? "").Length > 160 || (item.Summary ?? "").Length > 4000 || (item.Details ?? "").Length > 10000) throw new IOException("Invalid live event size.");
    }
    internal string Status
    {
        get { lock (gate) {
            string result = LiveDetectionPolicy.VerifiedCoverage(enabled, subscribed, logHealthy, caughtUp, logCheckedUtc, reading, checkedUtc, clock());
            if (enabled && deliveryError != "") result += " " + deliveryError;
            if (storageError != "") result += " History warning: " + storageError;
            return result + " Retained " + history.Count + "/1000; trimmed " + (previousTrimmed + history.Trimmed) + ".";
        } }
    }
    internal void Start()
    {
        lock (gate) {
            if (disposed) throw new ObjectDisposedException(nameof(LiveSessionEngine));
            if (enabled) return;
            enabled = true; generation++; subscribed = logHealthy = caughtUp = false; reading = null; checkedUtc = logCheckedUtc = DateTime.MinValue;
            lastPollAttempt = lastSubscribeAttempt = lastReconcileAttempt = DateTime.MinValue; deliveryError = "Waiting for log validation and checkpoint reconciliation.";
            Add(new LiveDetectionEvent { Kind = "Session started", Summary = "Live monitoring started. Available records will be reconciled from the saved checkpoint; missing/overwritten records and initial recovery limits are reported." }, false);
            if (useTimer) timer = new Timer(state => { var ignored = CycleAsync(false); }, null, 0, 5000);
        }
    }
    private static bool Due(DateTime previous, DateTime now, int seconds) => previous == DateTime.MinValue || now < previous || now - previous >= TimeSpan.FromSeconds(seconds);
    private void Attach(int request)
    {
        if (Interlocked.Exchange(ref subscribing, 1) != 0) return;
        IDisposable old = null, candidate = null; long token = 0;
        try {
            lock (gate) {
                if (!enabled || disposed || generation != request || subscribed || !Due(lastSubscribeAttempt, clock(), 10)) return;
                lastSubscribeAttempt = clock(); old = subscription; subscription = null; token = ++subscriptionToken;
            }
            Release(old);
            bool candidateFailed = false;
            candidate = source.Subscribe(item => {
                lock (gate) { if (!enabled || disposed || generation != request || subscriptionToken != token) return; Accept(item, false); }
            }, message => {
                lock (gate) { if (!enabled || disposed || generation != request || subscriptionToken != token) return; candidateFailed = true; subscribed = false; logHealthy = false; deliveryError = message; Add(new LiveDetectionEvent { Kind = "Coverage gap", Severity = 1, Summary = message }, true); }
            });
            bool stale;
            lock (gate) {
                stale = !enabled || disposed || generation != request || subscriptionToken != token;
                if (!stale) { subscription = candidate; candidate = null; subscribed = !candidateFailed; }
            }
            if (stale) return;
        } catch {
            lock (gate) if (enabled && !disposed && generation == request) { subscribed = false; deliveryError = "Event subscription failed. Available-log reconciliation will continue; subscription will retry."; Add(new LiveDetectionEvent { Kind = "Coverage gap", Severity = 1, Summary = deliveryError }, true); }
        } finally { Release(candidate); Interlocked.Exchange(ref subscribing, 0); }
    }
    private bool Accept(LiveDetectionEvent input, bool recovered)
    {
        Validate(input);
        if (LiveDetectionPolicy.Kind(input.EventId) == null) return false;
        if (input.RecordId <= 0 || input.EventUtc == DateTime.MinValue) throw new IOException("Source event identity is unavailable.");
        var item = new LiveDetectionEvent { RecordedUtc = clock(), EventUtc = input.EventUtc, RecordId = input.RecordId, EventId = input.EventId,
            Kind = LiveDetectionPolicy.Kind(input.EventId), Summary = LiveDetectionPolicy.Summary(input.EventId), Details = input.Details, Recovered = recovered, Severity = LiveDetectionPolicy.Severity(input.EventId) };
        if (!history.Add(item)) return false;
        captured++; revision++;
        if (LiveDetectionPolicy.InvalidatesStatus(item.EventId) && (!recovered || item.EventUtc >= checkedUtc)) { protectionRevision++; reading = null; checkedUtc = DateTime.MinValue; lastPollAttempt = DateTime.MinValue; }
        if (!recovered) Notice(item);
        return true;
    }
    private void Add(LiveDetectionEvent item, bool alert)
    {
        if (item.Kind == "Coverage gap" && !activeGaps.Add(item.Summary ?? "")) return;
        item.RecordedUtc = clock(); if (item.EventUtc == DateTime.MinValue) item.EventUtc = item.RecordedUtc;
        history.Add(item); revision++; if (alert) Notice(item);
    }
    private void Notice(LiveDetectionEvent item)
    {
        string group = item.Severity == 2 ? "urgent" : item.Severity == 0 ? "information" : "attention";
        int delay = item.Severity == 2 ? 2 : item.Severity == 0 ? 30 : 10;
        if (lastNotice.TryGetValue(group, out var last) && !Due(last, clock(), delay)) { pendingNotices[group] = pendingNotices.TryGetValue(group, out var count) ? count + 1 : 1; return; }
        lastNotice[group] = clock(); try { notify?.Invoke(item); } catch { }
    }
    private void DrainNotices()
    {
        foreach (string group in pendingNotices.Keys.ToArray())
        {
            int delay = group == "urgent" ? 2 : group == "information" ? 30 : 10;
            if (!Due(lastNotice[group], clock(), delay)) continue;
            int count = pendingNotices[group]; pendingNotices.Remove(group);
            var summary = new LiveDetectionEvent { Kind = "Alert batch", RecordedUtc = clock(), EventUtc = clock(), Severity = group == "urgent" ? 2 : group == "information" ? 0 : 1,
                Summary = count + " additional " + group + " live notices were coalesced. Review individual retained source events; this is a notification summary, not an extra threat detection." };
            Notice(summary);
        }
    }
    internal async Task CycleAsync(bool force)
    {
        if (Interlocked.Exchange(ref cycling, 1) != 0) return;
        int request;
        try {
            lock (gate) { if (!enabled || disposed) return; request = generation; }
            Attach(request);
            bool reconcile; LiveEventCursor from;
            lock (gate) { if (!enabled || disposed || generation != request) return; reconcile = force || Due(lastReconcileAttempt, clock(), 10); from = Clone(cursor); if (reconcile) lastReconcileAttempt = clock(); }
            if (reconcile)
            {
                try {
                    var batch = await Task.Run(() => source.Read(from, 500));
                    lock (gate) {
                        if (!enabled || disposed || generation != request) return;
                        ValidateBatch(batch, from);
                        if (batch.Gap != null) Add(new LiveDetectionEvent { Kind = "Coverage gap", Severity = 1, Summary = batch.Gap }, true);
                        int recovered = 0, urgent = 0;
                        foreach (var item in batch.Events) if (Accept(item, true)) { recovered++; if (LiveDetectionPolicy.Severity(item.EventId) == 2) urgent++; }
                        if (cursor.RecordId != batch.Cursor.RecordId || cursor.EventId != batch.Cursor.EventId || cursor.EventUtc != batch.Cursor.EventUtc) revision++;
                        cursor = Clone(batch.Cursor); logHealthy = true; caughtUp = batch.CaughtUp; logCheckedUtc = clock();
                        if (subscribed && caughtUp) activeGaps.Clear();
                        deliveryError = batch.CaughtUp ? "Available-log reconciliation checked. Historical gaps and retention limits still apply." : "Backfill continues in bounded batches; no complete-history claim is made.";
                        if (recovered > 0) Add(new LiveDetectionEvent { Kind = "Recovered events", Severity = urgent > 0 ? 2 : 1, Summary = recovered + " available source events recovered; " + urgent + " require urgent review. Source timestamps can precede receipt. These are not all new detections." }, true);
                    }
                } catch {
                    lock (gate) if (enabled && !disposed && generation == request) { logHealthy = caughtUp = false; logCheckedUtc = clock(); deliveryError = "Event-log validation/reconciliation failed. Disabled logs, access loss, resets or retention can leave gaps. Check Windows Event Viewer."; Add(new LiveDetectionEvent { Kind = "Coverage gap", Severity = 1, Summary = deliveryError }, true); }
                }
            }
            bool poll; long observedRevision; DateTime started;
            lock (gate) { if (!enabled || disposed || generation != request) return; poll = force || Due(lastPollAttempt, clock(), 30); started = clock(); observedRevision = protectionRevision; if (poll) lastPollAttempt = started; }
            if (poll)
            {
                LiveProtectionReading result;
                try { result = await Task.Run(read) ?? new LiveProtectionReading { Error = "No Defender reading returned." }; }
                catch { result = new LiveProtectionReading { Error = "Current Defender status could not be read within its bounded query." }; }
                lock (gate) {
                    if (!enabled || disposed || generation != request) return;
                    if (observedRevision != protectionRevision) { reading = null; checkedUtc = DateTime.MinValue; lastPollAttempt = DateTime.MinValue; }
                    else {
                        string before = LiveDetectionPolicy.Coverage(true, true, reading, checkedUtc, clock()); reading = result; checkedUtc = started; completedUtc = clock();
                        string after = LiveDetectionPolicy.Coverage(true, true, reading, checkedUtc, clock());
                        if (before != after) Add(new LiveDetectionEvent { Kind = after.Contains("warning") ? "Protection status warning" : "Coverage status", Severity = after.Contains("warning") ? 2 : 1, Summary = after, Details = "Read-only Defender observation. Freshness is measured from query start; results crossing a newer protection-change event are discarded." }, after.Contains("warning") || after.Contains("unknown"));
                    }
                }
            }
            lock (gate) if (enabled && !disposed && generation == request) DrainNotices();
            Flush();
        } catch { lock (gate) storageError = "Live worker failed; inspect coverage diagnostics before relying on it."; }
        finally { Interlocked.Exchange(ref cycling, 0); }
    }
    private void Flush()
    {
        lock (persistence)
        {
            LiveCheckpoint checkpoint; long version;
            lock (gate) {
                if (storageBlocked || revision == savedRevision) return;
                checkpoint = SnapshotCheckpoint();
                while (Encoding.UTF8.GetByteCount(Core.Json(checkpoint)) > 4 * 1024 * 1024 && checkpoint.Events.Count > 1) { history.TrimOldest(Math.Max(1, checkpoint.Events.Count / 8)); revision++; checkpoint = SnapshotCheckpoint(); }
                version = revision;
            }
            try { store.Write(checkpoint); lock (gate) { savedRevision = version; storageError = ""; } }
            catch { lock (gate) storageError = "Checkpoint/history could not be saved. Unpersisted events can be recovered only while their Windows log records remain available."; }
        }
    }
    private LiveCheckpoint SnapshotCheckpoint() => new LiveCheckpoint { Cursor = Clone(cursor), Events = history.Snapshot(), Captured = captured, Trimmed = previousTrimmed + history.Trimmed, SavedUtc = clock() };
    internal List<LiveDetectionEvent> Events() => history.Snapshot();
    internal string Report() { lock (gate) return Core.Json(new { status = Status, enabled, subscribed, logHealthy, caughtUp, statusQueryStartedUtc = checkedUtc, statusQueryCompletedUtc = completedUtc, logCheckedUtc, checkpoint = cursor, reading, captured, trimmed = previousTrimmed + history.Trimmed, storageError, scope = "Defender-reported metadata only. Reconciliation is bounded to available log records; initial/reset recovery starts within 24 hours. No guarantee of complete threat detection. Paths in selected details are private.", events = history.Snapshot() }); }
    internal void Stop() => Stop(false);
    private void Stop(bool disposing)
    {
        IDisposable old;
        lock (gate) {
            if (disposing) disposed = true;
            if (!enabled) return;
            enabled = false; generation++; subscriptionToken++; subscribed = false; timer?.Dispose(); timer = null; old = subscription; subscription = null;
            Add(new LiveDetectionEvent { Kind = "Session stopped", Summary = "FishTank monitoring stopped. Windows Defender continues independently; available missed records will be reconciled on next start." }, false);
        }
        Release(old); Flush();
    }
    private static void Release(IDisposable value) { try { value?.Dispose(); } catch { } }
    public void Dispose() => Stop(true);
}
