using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FishTank;

internal static class AppInsightsChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var hardware = CoverageCollector.Capture("Hardware");
        check(hardware.Category == "Hardware" && hardware.Items.Any(x => x.Name == "Secure Boot runtime") && hardware.Items.Any(x => x.Name == "TPM readiness"), "Embedded coverage resource did not bind its category");
        var now = new DateTime(2026, 11, 2, 16, 0, 0, DateTimeKind.Utc);
        var schedule = new ScanSchedule { Enabled = true, MinuteOfDay = 15 * 60, IntervalDays = 1 };
        check(schedule.NextAfter(now, TimeZoneInfo.Utc) == now.Date.AddDays(1).AddHours(15), "Daily schedule next occurrence");
        schedule.IntervalDays = 7;
        check(schedule.NextAfter(now, TimeZoneInfo.Utc) == now.Date.AddDays(7).AddHours(15), "Weekly schedule next occurrence");
        schedule.NextRunUtc = now.AddDays(-3);
        check(schedule.IsDue(now), "Missed schedule catches up");
        schedule.Enabled = false;
        check(!schedule.IsDue(now), "Disabled schedule stays off");
        schedule.Enabled = true;
        ScanScheduleStore.Save(schedule);
        check(ScanScheduleStore.ClaimDue(now) != null, "Due occurrence claimed");
        check(ScanScheduleStore.ClaimDue(now) == null, "Same occurrence replayed");
        ScanScheduleStore.Finish("Fixture completed");
        check(ScanScheduleStore.Load().LastResult == "Fixture completed", "Last scan result lost");
        ScanScheduleStore.Save(new ScanSchedule());
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");
        var gap = new ScanSchedule { MinuteOfDay = 150 };
        var next = gap.NextAfter(new DateTime(2026, 3, 8, 6, 0, 0, DateTimeKind.Utc), zone);
        check(TimeZoneInfo.ConvertTimeFromUtc(next, zone).Hour == 3, "Spring-forward invalid time not resolved");
        bool rejected = false; try { new ScanSchedule { Kind = "Custom" }.Validate(); } catch (ArgumentException) { rejected = true; }
        check(rejected, "Unsupported scheduled scan type accepted");

        var snapshot = new EvidenceSnapshot { CapturedUtc = now, Items = new[] { "Defender real-time protection", "Defender signatures", "Windows Firewall", "Secure Boot", "System-drive encryption", "Windows Update service", "Pending restart indicators" }.Select(name => new EvidenceItem { Name = name, State = "No issue observed" }).ToList() };
        check(ProtectionOverview.Status(snapshot, now) == "Protected", "Complete fresh evidence not recognized");
        snapshot.Items[0].State = "Unknown";
        check(ProtectionOverview.Status(snapshot, now) == "Couldn’t check", "Unknown evidence reported protected");
        snapshot.Items[1].State = "Review";
        check(ProtectionOverview.Status(snapshot, now) == "Needs attention", "Review priority lost");
        check(ProtectionOverview.Status(snapshot, now.AddHours(1)) == "Couldn’t check", "Stale evidence reported current");
        check(ProtectionOverview.Status(null, now) == "Couldn’t check", "Missing evidence reported protected");
        snapshot.Items.ForEach(x => x.State = "No issue observed"); snapshot.Items.RemoveAt(0);
        check(ProtectionOverview.Status(snapshot, now) == "Couldn’t check", "Partial evidence reported protected");

        string folder = Path.Combine(Core.Data, "private-folder-fixture"); Directory.CreateDirectory(folder);
        FolderBackupReminders.Add(folder, 7);
        var reminder = FolderBackupReminders.Load().Single();
        check(reminder.NeedsAttention(now), "Never-backed-up folder lacks reminder");
        FolderBackupReminders.Update(reminder.Id, false);
        reminder = FolderBackupReminders.Load().Single();
        check(!reminder.NeedsAttention(DateTime.UtcNow), "Recorded folder backup still overdue");
        check(reminder.NeedsAttention(DateTime.UtcNow.AddDays(8)), "Old folder copy lacks reminder");
        reminder.LastCopiedUtc = now.AddDays(1);
        check(reminder.NeedsAttention(now), "Future copy date considered fresh");
        check(!System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(Core.Data, "folder-backup-reminders.dpapi"))).Contains(folder), "Folder path stored unencrypted");

        const string secret = "SUPPORT-EXPORT-MUST-NOT-INCLUDE-THIS";
        Core.Audit("scan-complete", new { exitCode = 2, path = secret, output = secret });
        Core.Audit("quarantine-staged", new { path = secret });
        AppActivity.Record("Updates", "Update verified and staged; not installed");
        BackupHistory.Created(BackupHistory.ProtectedFiles, secret, new string('a', 64));
        var timeline = ActivityTimeline.Read(new[] { new LiveDetectionEvent { EventId = 1116, RecordedUtc = DateTime.UtcNow, Details = secret } });
        check(timeline.Any(x => x.Category == "Scans" && x.Summary.Contains("exit 2")), "Scan outcome absent from unified timeline");
        check(timeline.Any(x => x.Category == "Detections") && timeline.Any(x => x.Category == "Quarantine") && timeline.Any(x => x.Category == "Updates"), "Activity categories not combined");
        check(timeline.Any(x => x.Category == "Backups"), "Backup events absent from timeline");
        check(!Core.Json(timeline).Contains(secret), "Timeline exposed raw private payloads");
        check(timeline.Zip(timeline.Skip(1), (a,b) => a.TimeUtc >= b.TimeUtc).All(x => x), "Timeline not newest first");
        string report = SupportReport.Build();
        check(!report.Contains(secret) && !report.Contains(folder), "Support report leaked a path or raw event payload");
        File.AppendAllText(Path.Combine(Core.Data, "events-" + DateTime.UtcNow.ToString("yyyy-MM-dd") + ".jsonl"), "broken record\n");
        check(ActivityTimeline.Read().Any(x => x.Category == "Coverage"), "Malformed activity silently hidden");
        FolderBackupReminders.Update(reminder.Id, true);
        check(FolderBackupReminders.Load().Count == 0, "Removed folder reminder retained");
    }
}
