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
    public int ReminderDays = 7;
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
            || (FolderCheckHours != 0 && FolderCheckHours != 1 && FolderCheckHours != 24 && FolderCheckHours != 168))
            throw new ArgumentException("Use 0–120 idle minutes, 1–90 reminder days, and hourly/daily/weekly folder checks.");
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
        value.LastReviewUtc = value.LastReviewUtc.ToUniversalTime();
        value.LastBackupVerifiedUtc = value.LastBackupVerifiedUtc.ToUniversalTime();
        value.LastReminderUtc = value.LastReminderUtc.ToUniversalTime();
        value.LastFolderCheckUtc = value.LastFolderCheckUtc.ToUniversalTime();
        return value;
        }
    }
    internal static void Save(SecurityPreferences value) { lock (Gate) { value.Validate(); ProtectedStore.Write("security-preferences", value); } }
    internal static void Update(Action<SecurityPreferences> change) { lock (Gate) { var value = Load(); change(value); Save(value); } }
}
