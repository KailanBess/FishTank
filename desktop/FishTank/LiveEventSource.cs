using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
namespace FishTank;

public sealed class LiveEventCursor
{
    public long RecordId;
    public int EventId;
    public DateTime EventUtc;
    public long EventUtcTicks;
}
internal sealed class LiveReplayBatch
{
    internal List<LiveDetectionEvent> Events = new List<LiveDetectionEvent>();
    internal LiveEventCursor Cursor;
    internal bool CaughtUp;
    internal string Gap;
}
internal interface ILiveEventSource
{
    IDisposable Subscribe(Action<LiveDetectionEvent> receive, Action<string> failed);
    LiveReplayBatch Read(LiveEventCursor cursor, int limit);
}
internal sealed class WindowsLiveEventSource : ILiveEventSource
{
    internal const string Channel = "Microsoft-Windows-Windows Defender/Operational";
    internal static string Predicate => string.Join(" or ", LiveDetectionPolicy.EventIds.Select(id => "EventID=" + id));
    private static LiveEventCursor Identity(EventRecord record)
    {
        if (record.RecordId == null || record.RecordId <= 0 || record.TimeCreated == null) throw new InvalidOperationException("Source event identity is unavailable.");
        var utc = record.TimeCreated.Value.ToUniversalTime();
        return new LiveEventCursor { RecordId = record.RecordId.Value, EventId = record.Id, EventUtc = utc, EventUtcTicks = utc.Ticks };
    }
    private static LiveEventCursor Edge(bool reverse)
    {
        using (var reader = new EventLogReader(new EventLogQuery(Channel, PathType.LogName, "*") { ReverseDirection = reverse }))
        using (var record = reader.ReadEvent()) return record == null ? new LiveEventCursor() : Identity(record);
    }
    private static bool AnchorMatches(LiveEventCursor cursor)
    {
        if (cursor == null || cursor.RecordId <= 0) return true;
        using (var reader = new EventLogReader(new EventLogQuery(Channel, PathType.LogName, "*[System[EventRecordID=" + cursor.RecordId + "]]")))
        using (var record = reader.ReadEvent()) return record != null && record.Id == cursor.EventId && record.TimeCreated?.ToUniversalTime().Ticks == (cursor.EventUtcTicks > 0 ? cursor.EventUtcTicks : cursor.EventUtc.Ticks);
    }
    private static LiveDetectionEvent Convert(EventRecord record, bool recovered)
    {
        if (record.ProviderName != "Microsoft-Windows-Windows Defender") return null;
        string kind = LiveDetectionPolicy.Kind(record.Id); if (kind == null) return null;
        var identity = Identity(record);
        return new LiveDetectionEvent { EventId = record.Id, RecordId = identity.RecordId, EventUtc = identity.EventUtc, SourceUtcTicks = identity.EventUtcTicks,
            Kind = kind, Summary = LiveDetectionPolicy.Summary(record.Id), Details = LiveDetectionDetails.Read(record.ToXml()), Recovered = recovered };
    }
    public IDisposable Subscribe(Action<LiveDetectionEvent> receive, Action<string> failed)
    {
        var watcher = new EventLogWatcher(new EventLogQuery(Channel, PathType.LogName, "*[System[(" + Predicate + ")]]"), null, false);
        watcher.EventRecordWritten += delegate(object sender, EventRecordWrittenEventArgs args) {
            using (var record = args.EventRecord)
            {
                if (args.EventException != null) { failed("Windows event delivery failed; reconciliation will retry available records."); return; }
                if (record == null) return;
                try { var item = Convert(record, false); if (item != null) receive(item); }
                catch { failed("A source event could not be read safely; source coverage needs review."); }
            }
        };
        try { watcher.Enabled = true; return watcher; } catch { watcher.Dispose(); throw; }
    }
    public LiveReplayBatch Read(LiveEventCursor cursor, int limit)
    {
        if (limit < 1 || limit > 1000) throw new ArgumentException("Invalid reconciliation limit.");
        using (var configuration = new EventLogConfiguration(Channel)) if (!configuration.IsEnabled) throw new InvalidOperationException("The Defender Operational log is disabled. Event coverage is unavailable.");
        var head = Edge(true); var oldest = Edge(false); var result = new LiveReplayBatch();
        if (head.RecordId == 0)
        {
            if (Edge(true).RecordId != 0) throw new InvalidOperationException("The empty log changed during validation.");
            result.Cursor = new LiveEventCursor(); result.CaughtUp = true;
            if (cursor?.RecordId > 0) result.Gap = "The previously populated Defender log is empty. A clear/reset may have removed records.";
            return result;
        }
        bool resume = cursor != null && cursor.RecordId > 0;
        if (resume && (cursor.RecordId < oldest.RecordId || cursor.RecordId > head.RecordId || !AnchorMatches(cursor)))
        {
            result.Gap = "The saved log anchor is no longer available or no longer matches. Records may have rolled over or the log was reset/cleared. Recovery is limited to available records from the last 24 hours.";
            resume = false;
        }
        if (!resume && result.Gap == null) result.Gap = "Initial reconciliation covers available matching records from the last 24 hours; older or deleted records are outside this checkpoint.";
        string lower = resume ? "EventRecordID>" + cursor.RecordId : "TimeCreated[timediff(@SystemTime)<=86400000]";
        string query = "*[System[(" + Predicate + ") and " + lower + " and EventRecordID<=" + head.RecordId + "]]";
        using (var reader = new EventLogReader(new EventLogQuery(Channel, PathType.LogName, query)))
        {
            for (int i = 0; i <= limit; i++)
            using (var record = reader.ReadEvent())
            {
                if (record == null) { result.CaughtUp = true; break; }
                if (i == limit) break;
                var item = Convert(record, true); if (item != null) result.Events.Add(item);
                result.Cursor = Identity(record);
            }
        }
        // A clear/rollover during the query must never advance a trusted checkpoint.
        if (!AnchorMatches(head)) throw new InvalidOperationException("The event log changed generation during reconciliation. Its checkpoint was not advanced.");
        if (result.CaughtUp) result.Cursor = head;
        if (result.Cursor == null) throw new InvalidOperationException("No consistent reconciliation cursor was returned.");
        return result;
    }
}
