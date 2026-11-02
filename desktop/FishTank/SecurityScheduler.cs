using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FishTank;

internal sealed class SecurityScheduler : IDisposable
{
    private readonly Timer timer = new Timer { Interval = 60000 };
    private readonly Action<string> notify;
    private bool working;
    private bool disposed;
    private bool checkedGitHub;
    internal SecurityScheduler(Action<string> notify)
    {
        this.notify = notify;
        timer.Tick += Tick;
        timer.Start();
    }
    private async void Tick(object sender, EventArgs args)
    {
        if (working || disposed) return;
        working = true;
        try
        {
            var prefs = SecurityPreferences.Load();
            var now = DateTime.UtcNow;
            if (prefs.Reminders && prefs.BackupAgeWarnings && now - prefs.LastBackupAgeNoticeUtc >= TimeSpan.FromDays(prefs.ReminderDays))
            {
                var alerts = BackupHistory.Alerts();
                if (alerts.Count > 0)
                {
                    notify("Backup age needs attention: " + string.Join(", ", alerts.ConvertAll(x => x.Name + " (" + x.State + ")")) + ". Open Backup and recovery. Verification does not refresh a copy's creation date.");
                    SecurityPreferences.Update(p => p.LastBackupAgeNoticeUtc = now);
                }
            }
            if (prefs.CheckGitHubOnStartup && !checkedGitHub)
            {
                checkedGitHub = true;
                string update = await Task.Run(GitHubUpdates.Check);
                if (!disposed) notify(update);
            }
            if (prefs.Reminders && now - prefs.LastReminderUtc >= TimeSpan.FromDays(prefs.ReminderDays)
                && now - prefs.LastReviewUtc >= TimeSpan.FromDays(prefs.ReminderDays))
            {
                notify("Security reminder: review protection in Security Center → Security tools.");
                SecurityPreferences.Update(p => p.LastReminderUtc = now);
            }
            if (prefs.DiskHealthWarnings && now - prefs.LastDiskHealthAttemptUtc >= TimeSpan.FromHours(prefs.DiskCheckHours))
            {
                SecurityPreferences.Update(p => p.LastDiskHealthAttemptUtc = now);
                await Task.Run(DiskHealthChecks.Run);
                if (!disposed) { string message = DiskHealthChecks.Notification(); if (message != null) notify(message); }
            }
            if (prefs.FolderCheckHours > 0 && now - prefs.LastFolderCheckUtc >= TimeSpan.FromHours(prefs.FolderCheckHours))
            {
                SecurityPreferences.Update(p => p.LastFolderCheckUtc = now); // Throttle failed attempts too.
                string report = await Task.Run(FolderIntegrityStore.Check);
                if (!disposed)
                {
                    var summary = Core.Parse<System.Collections.Generic.Dictionary<string, object>>(report);
                    notify("Folder integrity: " + summary["changed"] + " changes or unknown findings. Review saved history in Security tools → Folder integrity.");
                }
            }
        }
        catch (Exception ex) { if (!disposed) notify("Scheduled security work needs attention: " + ex.Message); }
        finally { working = false; }
    }
    public void Dispose() { disposed = true; timer.Dispose(); }
}
