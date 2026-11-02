using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace FishTank;

public sealed class ScanSchedule
{
    public bool Enabled;
    public string Kind = "Quick";
    public int IntervalDays = 1;
    public int MinuteOfDay = 12 * 60;
    public DateTime NextRunUtc = DateTime.MinValue;
    public DateTime LastAttemptUtc = DateTime.MinValue;
    public string LastResult = "No scheduled scan recorded.";
    public void Validate()
    {
        if ((Kind != "Quick" && Kind != "Full") || (IntervalDays != 1 && IntervalDays != 7)
            || MinuteOfDay < 0 || MinuteOfDay >= 1440) throw new ArgumentException("Choose quick/full, daily/weekly, and a valid time.");
    }
    public bool IsDue(DateTime nowUtc) => Enabled && NextRunUtc != DateTime.MinValue && nowUtc >= NextRunUtc;
    public DateTime NextAfter(DateTime nowUtc, TimeZoneInfo zone)
    {
        Validate();
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), zone);
        var next = local.Date.AddMinutes(MinuteOfDay);
        if (next <= local) next = next.AddDays(IntervalDays);
        // Spring-forward gaps move to the next valid minute; ambiguous times run once.
        while (zone.IsInvalidTime(next)) next = next.AddMinutes(1);
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(next, DateTimeKind.Unspecified), zone);
    }
}

internal static class ScanScheduleStore
{
    private static readonly object Gate = new object();
    internal static ScanSchedule Load()
    {
        lock (Gate)
        {
            var schedule = ProtectedStore.Read("scan-schedule", () => new ScanSchedule()) ?? throw new InvalidDataException("Scan schedule unreadable.");
            schedule.Validate();
            schedule.NextRunUtc = SecurityPreferences.RecordedUtc(schedule.NextRunUtc);
            schedule.LastAttemptUtc = SecurityPreferences.RecordedUtc(schedule.LastAttemptUtc);
            return schedule;
        }
    }
    internal static void Save(ScanSchedule schedule) { lock (Gate) { schedule.Validate(); ProtectedStore.Write("scan-schedule", schedule); } }
    internal static ScanSchedule ClaimDue(DateTime nowUtc)
    {
        lock (Gate)
        {
            var schedule = Load();
            if (!schedule.IsDue(nowUtc)) return null;
            schedule.LastAttemptUtc = nowUtc;
            schedule.NextRunUtc = schedule.NextAfter(nowUtc, TimeZoneInfo.Local);
            schedule.LastResult = "Started; completion has not been recorded. A scan may still be running if FishTank was closed.";
            Save(schedule); // Persist before launching, so a restart never replays the same occurrence.
            return schedule;
        }
    }
    internal static void Finish(string result)
    {
        lock (Gate) { var current = Load(); current.LastResult = result; Save(current); }
    }
}

public sealed class FolderBackupReminder
{
    public string Id = Guid.NewGuid().ToString("N");
    public string Path;
    public int Days = 7;
    public DateTime LastCopiedUtc;
    public string Status(DateTime nowUtc) => LastCopiedUtc == DateTime.MinValue ? "No backup recorded"
        : LastCopiedUtc > nowUtc ? "Couldn’t check: recorded date is in the future"
        : nowUtc - LastCopiedUtc >= TimeSpan.FromDays(Days) ? "Backup overdue" : "Within reminder interval";
    public bool NeedsAttention(DateTime nowUtc) => LastCopiedUtc == DateTime.MinValue || LastCopiedUtc > nowUtc || nowUtc - LastCopiedUtc >= TimeSpan.FromDays(Days);
}

internal static class FolderBackupReminders
{
    private static readonly object Gate = new object();
    internal static List<FolderBackupReminder> Load()
    {
        lock (Gate)
        {
            var items = ProtectedStore.Read("folder-backup-reminders", () => new List<FolderBackupReminder>()) ?? throw new InvalidDataException("Folder reminders unreadable.");
            if (items.Count > 50 || items.Any(x => x == null || !Guid.TryParseExact(x.Id, "N", out _) || string.IsNullOrEmpty(x.Path) || x.Days < 1 || x.Days > 365))
                throw new InvalidDataException("Folder reminders are invalid.");
            foreach (var item in items) item.LastCopiedUtc = SecurityPreferences.RecordedUtc(item.LastCopiedUtc);
            return items;
        }
    }
    internal static void Add(string path, int days)
    {
        lock (Gate)
        {
            path = System.IO.Path.GetFullPath(path);
            if (!Directory.Exists(path)) throw new DirectoryNotFoundException("Choose an existing folder.");
            var items = Load();
            if (days < 1 || days > 365 || items.Count >= 50) throw new ArgumentException("Use 1–365 days and at most 50 folders.");
            if (items.Any(x => string.Equals(x.Path.TrimEnd('\\'), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("That folder already has a reminder.");
            items.Add(new FolderBackupReminder { Path = path, Days = days });
            ProtectedStore.Write("folder-backup-reminders", items);
        }
    }
    internal static void Update(string id, bool remove)
    {
        lock (Gate)
        {
            var items = Load(); var item = items.Single(x => x.Id == id);
            if (remove) items.Remove(item); else item.LastCopiedUtc = DateTime.UtcNow;
            ProtectedStore.Write("folder-backup-reminders", items);
        }
    }
}

public sealed class ActivityEntry
{
    public DateTime TimeUtc;
    public string Category;
    public string Summary;
}

internal static class AppActivity
{
    private static readonly object Gate = new object();
    internal static void Record(string category, string summary)
    {
        try
        {
            lock (Gate)
            {
                var records = Read();
                records.Add(new ActivityEntry { TimeUtc = DateTime.UtcNow, Category = category, Summary = summary });
                ProtectedStore.Write("app-activity", records.Skip(Math.Max(0, records.Count - 500)).ToList());
            }
        }
        catch { /* History failures never change the outcome of a completed operation. */ }
    }
    internal static List<ActivityEntry> Read() { lock (Gate) return ProtectedStore.Read("app-activity", () => new List<ActivityEntry>()) ?? throw new InvalidDataException("Activity unreadable."); }
}

internal static class ActivityTimeline
{
    internal static List<ActivityEntry> Read(IEnumerable<LiveDetectionEvent> live = null)
    {
        var result = new List<ActivityEntry>();
        // Read bounded recent files, newest first. Raw payloads never enter the unified timeline.
        if (Directory.Exists(Core.Data))
        foreach (var file in new DirectoryInfo(Core.Data).GetFiles("events-*.jsonl").OrderByDescending(x => x.Name).Take(14))
        {
            if (file.Length > 2 * 1024 * 1024) { result.Add(Gap("An oversized activity file was skipped.")); continue; }
            try
            {
                foreach (string line in File.ReadLines(file.FullName))
                {
                    try
                    {
                        var row = Core.Parse<Dictionary<string, object>>(line);
                        if (!DateTime.TryParse(Convert.ToString(row["time"]), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time)) throw new InvalidDataException();
                        string action = Convert.ToString(row["action"]);
                        string category = action.StartsWith("scan-") || action.StartsWith("scheduled-scan") ? "Scans"
                            : action.StartsWith("quarantine-") || action.StartsWith("restore-") || action.StartsWith("vault-delete") ? "Quarantine"
                            : action.StartsWith("backup-") ? "Backups"
                            : action.Contains("update") || action.StartsWith("signature-") ? "Updates" : "App";
                        string summary = action.Replace('-', ' ');
                        if (action == "scan-complete" && row.TryGetValue("detail", out var value) && value is Dictionary<string, object> detail && detail.TryGetValue("exitCode", out var code))
                            summary += ": Defender exit " + Convert.ToString(code);
                        result.Add(new ActivityEntry { TimeUtc = time.ToUniversalTime(), Category = category, Summary = summary });
                    }
                    catch { result.Add(Gap("An activity record could not be read.")); }
                    if (result.Count > 1500) result = result.OrderByDescending(x => x.TimeUtc).Take(1000).ToList();
                }
            }
            catch { result.Add(Gap("An activity file could not be read.")); }
        }
        try
        {
            foreach (var record in BackupHistory.Observations())
            {
                if (record.CreatedUtc != DateTime.MinValue) result.Add(new ActivityEntry { TimeUtc = record.CreatedUtc, Category = "Backups", Summary = record.Kind + " copy created" });
                if (record.VerifiedUtc != DateTime.MinValue) result.Add(new ActivityEntry { TimeUtc = record.VerifiedUtc, Category = "Backups", Summary = record.Kind + " backup verified" });
                if (record.RestoreTestUtc != DateTime.MinValue) result.Add(new ActivityEntry { TimeUtc = record.RestoreTestUtc, Category = "Backups", Summary = record.Kind + " restore tested" });
            }
        }
        catch { result.Add(Gap("Backup history could not be read.")); }
        try { result.AddRange(AppActivity.Read()); }
        catch { result.Add(Gap("App activity history could not be read.")); }
        if (live == null)
        {
            try { live = ProtectedStore.Read("live-detection-history", () => new List<LiveDetectionEvent>()); }
            catch { result.Add(Gap("Detection history could not be read.")); }
        }
        foreach (var item in live ?? Enumerable.Empty<LiveDetectionEvent>())
            result.Add(new ActivityEntry { TimeUtc = item.RecordedUtc.ToUniversalTime(), Category = "Detections", Summary = LiveDetectionPolicy.Kind(item.EventId) ?? item.Kind });
        return result.OrderByDescending(x => x.TimeUtc).Take(1000).ToList();
    }
    private static ActivityEntry Gap(string message) => new ActivityEntry { TimeUtc = DateTime.UtcNow, Category = "Coverage", Summary = message };
}

public static class ProtectionOverview
{
    private static readonly string[] Required = { "Defender real-time protection", "Defender signatures", "Windows Firewall", "Secure Boot", "System-drive encryption", "Windows Update service", "Pending restart indicators" };
    public static string Status(EvidenceSnapshot snapshot, DateTime nowUtc)
    {
        if (snapshot?.Items == null || snapshot.CapturedUtc.ToUniversalTime() > nowUtc || nowUtc - snapshot.CapturedUtc.ToUniversalTime() > TimeSpan.FromMinutes(15)) return "Couldn’t check";
        if (snapshot.Items.Any(x => x.State == "Review")) return "Needs attention";
        return Required.All(name => snapshot.Items.Count(x => x.Name == name && x.State == "No issue observed") == 1)
            && snapshot.Items.All(x => x.State == "No issue observed") ? "Protected" : "Couldn’t check";
    }
}

internal static class SupportReport
{
    internal static string Build()
    {
        var text = new StringBuilder();
        text.AppendLine("FishTank support report · " + DateTime.UtcNow.ToString("u"));
        text.AppendLine("App: " + typeof(Core).Assembly.GetName().Version);
        text.AppendLine("Windows: " + Environment.OSVersion.Version + "; 64-bit process: " + Environment.Is64BitProcess);
        text.AppendLine("This report contains selected status summaries. Review and remove anything you do not want to share before saving.");
        try
        {
            var snapshot = SecurityReviews.Latest("SecurityHealth");
            text.AppendLine("Protection: " + ProtectionOverview.Status(snapshot, DateTime.UtcNow));
            text.AppendLine("Last protection check: " + (snapshot == null ? "Not recorded" : snapshot.CapturedUtc.ToUniversalTime().ToString("u")));
            text.AppendLine("Review findings: " + (snapshot?.Items.Count(x => x.State == "Review") ?? 0) + "; unknown findings: " + (snapshot?.Items.Count(x => x.State == "Unknown") ?? 0));
        }
        catch { text.AppendLine("Protection: couldn’t read saved evidence."); }
        try
        {
            var schedule = ScanScheduleStore.Load();
            text.AppendLine("Scheduled scans: " + (schedule.Enabled ? schedule.Kind + " every " + schedule.IntervalDays + " day(s)" : "Off"));
            text.AppendLine("Last scheduled attempt: " + (schedule.LastAttemptUtc == DateTime.MinValue ? "None" : schedule.LastAttemptUtc.ToString("u")));
        }
        catch { text.AppendLine("Scan schedule: unreadable."); }
        try { var folders = FolderBackupReminders.Load(); text.AppendLine("Folder backup reminders: " + folders.Count + "; needing attention: " + folders.Count(x => x.NeedsAttention(DateTime.UtcNow))); }
        catch { text.AppendLine("Folder reminders: unreadable."); }
        try { text.AppendLine("Recent activity counts: " + string.Join(", ", ActivityTimeline.Read().GroupBy(x => x.Category).Select(x => x.Key + "=" + x.Count()))); }
        catch { text.AppendLine("Activity: unreadable."); }
        text.AppendLine("Passwords, tokens, account names, file paths, raw command output and vault contents are excluded.");
        return text.ToString();
    }
}
