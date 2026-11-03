using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FishTank;

internal static class LiveReliabilityChecks
{
    private sealed class Store : ILiveCheckpointStore
    {
        internal LiveCheckpoint Value = new LiveCheckpoint();
        internal bool ReadFails, WriteFails;
        internal int Writes;
        internal Action Writing;
        public LiveCheckpoint Read() { if (ReadFails) throw new IOException("fixture"); return Copy(Value); }
        public void Write(LiveCheckpoint value) { Writes++; Writing?.Invoke(); if (WriteFails) throw new IOException("fixture"); Value = Copy(value); }
        private static LiveCheckpoint Copy(LiveCheckpoint value) => Core.Parse<LiveCheckpoint>(Core.Json(value));
    }
    private sealed class Source : ILiveEventSource
    {
        internal Action<LiveDetectionEvent> Receive, OldReceive;
        internal Action<string> Fail;
        internal Func<LiveEventCursor, LiveReplayBatch> Replay = cursor => new LiveReplayBatch { Cursor = cursor, CaughtUp = true };
        internal bool SubscribeFails, ReadFails, FailDuringSubscribe;
        internal int Subscriptions, Disposals;
        private sealed class Lease : IDisposable { private readonly Action release; internal Lease(Action release) { this.release = release; } public void Dispose() => release(); }
        public IDisposable Subscribe(Action<LiveDetectionEvent> receive, Action<string> failed) { Subscriptions++; if (SubscribeFails) throw new IOException("fixture"); OldReceive = Receive; Receive = receive; Fail = failed; if (FailDuringSubscribe) failed("fixture activation failure"); return new Lease(() => Disposals++); }
        public LiveReplayBatch Read(LiveEventCursor cursor, int limit) { if (ReadFails) throw new IOException("fixture"); return Replay(cursor); }
    }
    private static readonly DateTime Epoch = new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);
    private static LiveDetectionEvent Event(long record, int id = 1116) => new LiveDetectionEvent { RecordId = record, EventId = id, EventUtc = Epoch.AddSeconds(record).AddTicks(1234), Details = "Fixture metadata" };
    private static LiveEventCursor Cursor(LiveDetectionEvent item) => new LiveEventCursor { RecordId = item.RecordId, EventId = item.EventId, EventUtc = item.EventUtc };
    private static LiveProtectionReading Good() => new LiveProtectionReading { AntivirusEnabled = true, RealTimeProtectionEnabled = true, Mode = "Normal", ServiceEnabled = true, BehaviorMonitorEnabled = true, IoavProtectionEnabled = true, NisEnabled = true, TamperProtected = true, SignatureAgeDays = 0 };
    private static void Cycle(LiveDetectionSession session) { if (!session.CheckNowAsync().Wait(5000)) throw new Exception("Live cycle fixture timed out"); }
    internal static void Run(Action<bool, string> check)
    {
        var now = Epoch.AddHours(1); var source = new Source(); var store = new Store(); var notices = new List<LiveDetectionEvent>();
        source.Replay = cursor => new LiveReplayBatch { Cursor = Cursor(Event(2, 1117)), Events = new List<LiveDetectionEvent> { Event(1), Event(2, 1117) }, CaughtUp = true, Gap = "Initial bounded recovery fixture" };
        using (var session = new LiveDetectionSession(notices.Add, source, Good, () => now, store)) {
            session.Start(); session.Start(); Cycle(session);
            check(source.Subscriptions == 1, "Duplicate Start subscribed twice");
            check(session.Status.Contains("reported on") && session.Events().Count(x => x.Recovered) == 2, "Initial backfill or verified status failed");
            check(notices.Count(x => x.Kind == "Recovered events") == 1 && !notices.Any(x => x.RecordId > 0), "Historical events notified as new detections");
            source.Receive(Event(2, 1117)); check(session.Events().Count(x => x.RecordId == 2) == 1, "Replay/watcher duplicate retained");
            source.Receive(Event(4)); source.Receive(Event(3));
            check(!session.Events().Any(x => x.Summary != null && x.Summary.Contains("reset/reordering")), "Out-of-order delivery invented reset");
            source.Replay = cursor => new LiveReplayBatch { Cursor = Cursor(Event(4)), Events = new List<LiveDetectionEvent> { Event(3), Event(4) }, CaughtUp = true };
            Cycle(session); check(store.Value.Cursor.RecordId == 4 && store.Value.Events.Count(x => x.RecordId > 0) == 4, "Checkpoint lost delivered or replayed events");
            now = now.AddSeconds(31); check(session.Status.Contains("stale"), "Stale log validation reported healthy");
            source.ReadFails = true; Cycle(session); check(session.Status.Contains("Coverage gap"), "Failed source read reported healthy");
            source.ReadFails = false; source.Replay = cursor => new LiveReplayBatch { Cursor = cursor, CaughtUp = true }; Cycle(session);
            check(session.Status.Contains("reported on"), "Reconciliation recovery remained falsely broken");
            source.Fail("fixture delivery failure"); check(session.Status.Contains("Coverage gap"), "Watcher fault was ignored");
            now = now.AddSeconds(11); Cycle(session); check(source.Subscriptions == 2 && source.Disposals == 1, "Failed watcher not replaced");
            source.OldReceive(Event(99)); check(!session.Events().Any(x => x.RecordId == 99), "Disposed subscription callback accepted");
            source.Receive(Event(5, 1122)); check(notices.Last().Severity != 2, "Audit notice became urgent threat");
            session.Stop(); source.Receive(Event(98)); check(!session.Events().Any(x => x.RecordId == 98), "Stopped session accepted callback");
        }
        var resumedSource = new Source(); long resumedFrom = -1, resumedTicks = -1;
        resumedSource.Replay = cursor => { resumedFrom = cursor.RecordId; resumedTicks = cursor.EventUtc.Ticks; return new LiveReplayBatch { Cursor = Cursor(Event(6)), Events = new List<LiveDetectionEvent> { Event(5, 1122), Event(6) }, CaughtUp = true }; };
        using (var session = new LiveDetectionSession(null, resumedSource, Good, () => now, store)) { session.Start(); Cycle(session); check(resumedTicks == Event(4).EventUtc.Ticks && store.Value.Cursor.EventUtcTicks == Event(6).EventUtc.Ticks, "Submillisecond checkpoint identity lost in JSON round trip"); check(session.Events().Single(x => x.RecordId == 5).EventUtc.Ticks == Event(5).EventUtc.Ticks, "Source timestamp precision lost on restart"); check(resumedFrom == 4 && session.Events().Count(x => x.RecordId == 5) == 1 && session.Events().Any(x => x.RecordId == 6 && x.Recovered), "Restart checkpoint recovery/dedup failed"); }

        source = new Source { FailDuringSubscribe = true }; store = new Store();
        using (var session = new LiveDetectionSession(null, source, Good, () => now, store)) { session.Start(); Cycle(session); check(session.Status.Contains("Coverage gap"), "Synchronous subscription failure overwritten by success"); }
        source = new Source { SubscribeFails = true };
        using (var session = new LiveDetectionSession(null, source, Good, () => now, new Store())) { session.Start(); Cycle(session); check(session.Status.Contains("Coverage gap"), "Subscription denial reported healthy"); }
        source = new Source(); store = new Store { ReadFails = true };
        using (var session = new LiveDetectionSession(null, source, Good, () => now, store)) { session.Start(); Cycle(session); check(store.Writes == 0 && session.Status.Contains("saving is blocked"), "Corrupt history overwritten with defaults"); }
        source = new Source(); store = new Store { WriteFails = true };
        using (var session = new LiveDetectionSession(null, source, Good, () => now, store)) {
            session.Start(); Cycle(session); check(session.Status.Contains("could not be saved"), "Save failure concealed");
            source.Receive(Event(1)); store.WriteFails = false; Cycle(session); check(store.Value.Events.Any(x => x.RecordId == 1) && !session.Status.Contains("could not be saved"), "Failed checkpoint not retried");
            bool once = false; store.Writing = () => { if (!once) { once = true; source.Receive(Event(2)); } };
            source.Receive(Event(3)); Cycle(session); store.Writing = null; Cycle(session);
            check(store.Value.Events.Any(x => x.RecordId == 2), "Event during save lost dirty revision");
        }
        source = new Source(); store = new Store();
        using (var session = new LiveDetectionSession(null, source, Good, () => now, store)) {
            session.Start(); Cycle(session);
            source.Replay = cursor => new LiveReplayBatch { Cursor = Cursor(Event(10)), Events = new List<LiveDetectionEvent> { Event(10) }, CaughtUp = false }; Cycle(session);
            check(session.Status.Contains("Catching up"), "Partial backfill reported complete");
            source.Replay = cursor => new LiveReplayBatch { Cursor = Cursor(Event(2)), Events = new List<LiveDetectionEvent> { Event(2) }, CaughtUp = true }; Cycle(session);
            check(session.Status.Contains("Coverage gap") && store.Value.Cursor.RecordId == 10, "Unexplained cursor rewind trusted");
            source.Replay = cursor => new LiveReplayBatch { Cursor = Cursor(Event(2)), Events = new List<LiveDetectionEvent> { Event(2) }, CaughtUp = true, Gap = "Fixture reset: old anchor no longer matches" }; Cycle(session);
            check(store.Value.Cursor.RecordId == 2 && session.Events().Any(x => x.Summary == "Fixture reset: old anchor no longer matches"), "Explicit log reset not recovered/reported");
            source.Replay = cursor => new LiveReplayBatch { Cursor = Cursor(Event(4)), Events = new List<LiveDetectionEvent> { Event(4), Event(3) }, CaughtUp = true }; Cycle(session);
            check(store.Value.Cursor.RecordId == 2 && !session.Events().Any(x => x.RecordId == 4), "Malformed batch partially committed");
        }
        using (var entered = new ManualResetEventSlim()) using (var release = new ManualResetEventSlim()) {
            source = new Source(); store = new Store(); bool block = true;
            Func<LiveProtectionReading> read = () => { if (block) { entered.Set(); if (!release.Wait(4000)) throw new TimeoutException(); } return Good(); };
            using (var session = new LiveDetectionSession(null, source, read, () => now, store)) {
                session.Start(); var cycle = session.CheckNowAsync(); check(entered.Wait(3000), "Status query did not enter fixture");
                source.Receive(Event(1, 5001)); release.Set(); check(cycle.Wait(4000) && session.Status.Contains("unknown"), "Old in-flight healthy status overwrote new protection-off event");
                block = false; Cycle(session); check(session.Status.Contains("reported on"), "Fresh status after invalidation not accepted");
                entered.Reset(); release.Reset(); block = true; cycle = session.CheckNowAsync(); check(entered.Wait(3000), "Restart fixture did not enter");
                session.Stop(); session.Start(); release.Set(); check(cycle.Wait(4000) && session.Status.Contains("Coverage gap"), "Previous generation result contaminated restarted session");
                block = false; Cycle(session); check(session.Status.Contains("reported on"), "Restart could not recover");
            }
        }
        source = new Source(); store = new Store();
        using (var session = new LiveDetectionSession(null, source, () => { throw new IOException("fixture"); }, () => now, store)) { session.Start(); Cycle(session); check(session.Status.Contains("unknown"), "Failed current status query reported healthy"); }
        source = new Source(); store = new Store();
        using (var session = new LiveDetectionSession(null, source, Good, () => now, store)) {
            session.Start(); Cycle(session);
            for (int i = 1; i <= 1200; i++) { var item = Event(i); item.Details = new string('X', 9900); source.Receive(item); }
            Cycle(session);
            check(session.Events().Count <= 1000 && System.Text.Encoding.UTF8.GetByteCount(Core.Json(store.Value)) <= 4 * 1024 * 1024 && store.Value.Trimmed > 200, "Retention/byte cap failed or trimming hidden");
            var original = Event(5000); var buffer = new LiveDetectionBuffer(2); buffer.Add(original); original.Summary = "external mutation";
            check(buffer.Snapshot()[0].Summary != original.Summary, "Buffer stores external mutable reference");
        }
        foreach (int id in LiveDetectionPolicy.EventIds) check(LiveDetectionPolicy.Kind(id) != null, "Subscribed event has no classification: " + id);
        check(LiveDetectionPolicy.Severity(1122) == 0 && LiveDetectionPolicy.Severity(1124) == 0 && LiveDetectionPolicy.Severity(5008) == 2 && LiveDetectionPolicy.Kind(1127) == "Protected-sector block", "Expanded event severity/classification failed");
        var reading = Good(); reading.BehaviorMonitorEnabled = false;
        check(LiveDetectionPolicy.Coverage(true, true, reading, now, now).Contains("warning"), "Disabled behavior monitor missed");
        reading = Good(); reading.SignatureAgeDays = 4; check(LiveDetectionPolicy.Coverage(true, true, reading, now, now).Contains("warning"), "Stale signatures missed");
        check(LiveDetectionPolicy.Coverage(true, true, Good(), now.AddSeconds(-91), now).Contains("unknown"), "91-second stale observation trusted");
        reading = Good(); reading.SignatureAgeDays = -1; check(LiveDetectionPolicy.Coverage(true, true, reading, now, now).Contains("unknown"), "Invalid signature age trusted");
        reading = Good(); reading.TamperProtected = false; check(LiveDetectionPolicy.Coverage(true, true, reading, now, now).Contains("warning"), "Tamper protection off concealed");
        reading = Good(); reading.NisEnabled = null; check(LiveDetectionPolicy.Coverage(true, true, reading, now, now).Contains("unavailable"), "Missing component observation hidden");
        source = new Source(); store = new Store(); notices.Clear();
        using (var session = new LiveDetectionSession(notices.Add, source, Good, () => now, store)) {
            session.Start(); Cycle(session); notices.Clear();
            source.Receive(Event(1)); source.Receive(Event(2)); source.Receive(Event(3));
            check(notices.Count == 1 && session.Events().Count(x => x.RecordId > 0) == 3, "Throttle discarded source events");
            now = now.AddSeconds(3); Cycle(session);
            check(notices.Any(x => x.Kind == "Alert batch" && x.Summary.StartsWith("2 additional")), "Coalesced notifications silently disappeared");
            session.Stop(); session.Dispose(); bool rejected = false; try { session.Start(); } catch (ObjectDisposedException) { rejected = true; }
            check(rejected, "Disposed monitor restarted");
        }
        var bounded = new LiveDetectionBuffer(2); bounded.Add(Event(1)); bounded.Add(Event(2)); bounded.Add(Event(3));
        check(!bounded.Add(Event(1)) && bounded.Count == 2 && bounded.Trimmed == 1, "Recently evicted record reinserted by replay");
        source = new Source(); store = new Store();
        bool slow = true;
        using (var session = new LiveDetectionSession(null, source, () => { if (slow) now = now.AddSeconds(91); return Good(); }, () => now, store)) {
            session.Start(); Cycle(session); check(session.Status.Contains("stale") || session.Status.Contains("unknown"), "Slow query freshness measured from completion");
            slow = false; Cycle(session); check(session.Status.Contains("reported on"), "Fresh bounded query not restored");
            now = now.AddMinutes(-1); check(session.Status.Contains("Coverage gap"), "Clock rollback extended log health lifetime");
        }
    }
}
