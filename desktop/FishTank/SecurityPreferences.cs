using System;

namespace FishTank;

public sealed class SecurityPreferences
{
    private static readonly object Gate = new object();
    public int Version = 1;
    public int IdleMinutes = 5;
    public bool LockWithWindows = true;
    public bool Reminders;
    public bool DesktopNotifications;
    public bool CheckGitHubOnStartup;
    public int ReminderDays = 7;
    public int BackupAgeDays = 7;
    public bool BackupAgeWarnings = true;
    public bool DiskHealthWarnings = true;
    public int DiskCheckHours = 24;
    public DateTime LastBackupAgeNoticeUtc = DateTime.MinValue;
    public DateTime LastDiskHealthAttemptUtc = DateTime.MinValue;
    public DateTime LastDiskHealthCheckUtc = DateTime.MinValue;
    public int FolderCheckHours; // Zero disables automatic comparisons; only runs while FishTank is open.
    public DateTime LastReviewUtc = DateTime.UtcNow;
    public DateTime LastBackupVerifiedUtc = DateTime.MinValue;
    public DateTime LastReminderUtc = DateTime.MinValue;
    public DateTime LastFolderCheckUtc = DateTime.MinValue;
    public string PublisherCertificateSha256 = "";
    public string UpdateFeedUrl = "";
    public string HighestStagedVersion = "0.0.0.0";

    public void Validate()
    {
        if (Version != 1 || IdleMinutes < 0 || IdleMinutes > 120 || ReminderDays < 1 || ReminderDays > 90
            || BackupAgeDays < 1 || BackupAgeDays > 365 || (DiskCheckHours != 1 && DiskCheckHours != 24 && DiskCheckHours != 168)
            || (FolderCheckHours != 0 && FolderCheckHours != 1 && FolderCheckHours != 24 && FolderCheckHours != 168))
            throw new ArgumentException("Use 0–120 idle minutes, 1–90 reminder days, 1–365 backup-age days, and supported hourly/daily/weekly check intervals.");
        if (PublisherCertificateSha256 != "") DownloadChecksum.Normalize(PublisherCertificateSha256);
        if (UpdateFeedUrl != "") VerifiedUpdateFeed.RequireHttps(UpdateFeedUrl);
        if (!System.Version.TryParse(HighestStagedVersion, out _)) throw new ArgumentException("Invalid staged update version.");
    }

    public static SecurityPreferences Load()
    {
        lock (Gate)
        {
        var value = ProtectedStore.Read("security-preferences", () => new SecurityPreferences());
        if (value == null) throw new InvalidOperationException("Security preferences could not be read; defaults were not silently substituted.");
        value.Validate();
        value.LastReviewUtc = RecordedUtc(value.LastReviewUtc);
        value.LastBackupVerifiedUtc = RecordedUtc(value.LastBackupVerifiedUtc);
        value.LastReminderUtc = RecordedUtc(value.LastReminderUtc);
        value.LastFolderCheckUtc = RecordedUtc(value.LastFolderCheckUtc);
        value.LastBackupAgeNoticeUtc = RecordedUtc(value.LastBackupAgeNoticeUtc);
        value.LastDiskHealthAttemptUtc = RecordedUtc(value.LastDiskHealthAttemptUtc);
        value.LastDiskHealthCheckUtc = RecordedUtc(value.LastDiskHealthCheckUtc);
        return value;
        }
    }
    internal static DateTime RecordedUtc(DateTime value) => value.Year == 1 ? DateTime.MinValue : value.ToUniversalTime();
    internal static void Save(SecurityPreferences value) { lock (Gate) { value.Validate(); ProtectedStore.Write("security-preferences", value); } }
    internal static void Update(Action<SecurityPreferences> change) { lock (Gate) { var value = Load(); change(value); Save(value); } }
}
