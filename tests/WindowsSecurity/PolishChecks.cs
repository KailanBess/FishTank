using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using FishTank;
internal static class PolishChecks
{
    internal static void Run(Action<bool,string> check)
    {
        check(NotificationDestination.For("Backup age needs attention") == "Recovery", "Backup alert route");
        check(NotificationDestination.For("Disk health: warning") == "Disk", "Disk alert route");
        check(NotificationDestination.For("Folder integrity: changes") == "Integrity", "Integrity alert route");
        check(NotificationDestination.For("Quick scan finished") == "Scan", "Scan alert route");
        check(NotificationDestination.For("GitHub advertises a release") == "Updates", "Update alert route");
        var start = DateTime.UtcNow.AddMinutes(-1);
        var events = new[] { new ScanEventEvidence { EventId = 1000, ScanId = "one", TimeUtc = start.AddSeconds(1) }, new ScanEventEvidence { EventId = 1002, ScanId = "one", TimeUtc = start.AddSeconds(2) } };
        check(ScanStopPolicy.Assess(events, start).Contains("stopped before"), "Matching stop evidence not recognized");
        events[1].ScanId = "other";
        check(ScanStopPolicy.Assess(events, start).StartsWith("Stop unverified"), "Unrelated stop confirmed cancellation");
        events[1].ScanId = "one"; events[1].EventId = 1001;
        check(ScanStopPolicy.Assess(events, start).Contains("not confirmed cancelled"), "Completion treated as cancellation");
        check(ScanStopPolicy.Assess(events.Concat(new[] { new ScanEventEvidence { EventId = 1000, ScanId = "two", TimeUtc = start.AddSeconds(3) } }), start).StartsWith("Stop unverified"), "Concurrent scans incorrectly correlated");
        check(ScanStopPolicy.Assess(events, DateTime.UtcNow).StartsWith("Stop unverified"), "Old stop event reused");
        int cancellations = 0;
        using (var release = new ManualResetEventSlim())
        {
            var operation = new ScanOperation("Quick", null,
                () => { release.Wait(TimeSpan.FromSeconds(10)); return Core.Json(new { interpretation = "Fixture scan finished", exitCode = 0 }); },
                () => { cancellations++; return new CommandResult { ExitCode = 0 }; },
                _ => "Stop unverified: fixture has no readable event log.");
            var task = operation.RunAsync();
            check(operation.IsRunning && operation.Elapsed >= TimeSpan.Zero, "Running scan has no progress");
            operation.CancelAsync().GetAwaiter().GetResult();
            check(operation.CancelRequested && cancellations == 1 && operation.Cancellation.Contains("Stop unverified"), "Cancel request reported a false stop");
            operation.CancelAsync().GetAwaiter().GetResult();
            check(cancellations == 1, "Repeated cancellation launched twice");
            release.Set(); task.GetAwaiter().GetResult();
            check(!operation.IsRunning && operation.Result == "Fixture scan finished", "Scan completion lost");
        }
        using (var release = new ManualResetEventSlim())
        {
            var custom = new ScanOperation("Custom", "fixture", () => { release.Wait(TimeSpan.FromSeconds(10)); return Core.Json(new { interpretation = "Custom finished" }); }, () => { cancellations++; return new CommandResult(); });
            var task = custom.RunAsync(); custom.CancelAsync().GetAwaiter().GetResult();
            check(!custom.CancelRequested && cancellations == 1, "Custom scan issued quick/full cancellation");
            release.Set(); task.GetAwaiter().GetResult();
        }
        new SetupProgress { Completed = false, DeferredUtc = DateTime.UtcNow }.Save();
        check(!SetupProgress.Load().Completed && SetupProgress.Load().DeferredUtc != DateTime.MinValue, "Deferred setup not retained");
        new SetupProgress { Completed = true }.Save();
        check(SetupProgress.Load().Completed && !ScanScheduleStore.Load().Enabled, "Finishing setup enabled automation");
        var colors = Palette.From(new Appearance { CustomPalette = true, TextColor = "#ff0000" }, true);
        check(colors.Background == System.Drawing.SystemColors.Window && colors.Text == System.Drawing.SystemColors.WindowText && colors.AccentText == System.Drawing.SystemColors.HighlightText, "High contrast did not override custom colors");
    }
}
