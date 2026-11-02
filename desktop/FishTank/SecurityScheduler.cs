using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FishTank;

internal sealed class SecurityScheduler : IDisposable
{
    private readonly Timer timer = new Timer { Interval = 60000 };
    private readonly Action<string> notify;
    private readonly Func<bool> deferScan;
    private readonly Action<string> scanFinished;
    internal bool IsScanning { get; private set; }
    private bool working;
    private bool disposed;
    private bool checkedGitHub;
    internal SecurityScheduler(Action<string> notify, Func<bool> deferScan = null, Action<string> scanFinished = null)
    {
        this.notify = notify;
        this.deferScan = deferScan;
        this.scanFinished = scanFinished;
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
            try { if (!(deferScan?.Invoke() ?? false)) await RunScheduledScan(now); }
            catch { if (!disposed) notify("Scan scheduling could not read or save its state. Review Scan scheduling; no successful scan was inferred."); }
            if (disposed) return;
            if (prefs.Reminders && prefs.BackupAgeWarnings && now - prefs.LastBackupAgeNoticeUtc >= TimeSpan.FromDays(prefs.ReminderDays))
            {
                var alerts = BackupHistory.Alerts();
                var folderAlerts = FolderBackupReminders.Load().FindAll(x => x.NeedsAttention(now));
                if (folderAlerts.Count > 0) notify(folderAlerts.Count + " important folder backup reminders need attention. Open Settings → Folder backup reminders.");
                if (folderAlerts.Count > 0) SecurityPreferences.Update(p => p.LastBackupAgeNoticeUtc = now);
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
    private async Task RunScheduledScan(DateTime now)
    {
        var schedule = ScanScheduleStore.ClaimDue(now);
        if (schedule == null) return;
        IsScanning = true;
        AppActivity.Record("Scans", "Scheduled " + schedule.Kind + " scan started");
        string result;
        try
        {
            string json = await Task.Run(() => Core.Scan(schedule.Kind, null));
            var report = Core.Parse<System.Collections.Generic.Dictionary<string, object>>(json);
            result = schedule.Kind + " scan · " + DateTime.Now.ToString("g") + " · " + Convert.ToString(report["interpretation"]);
        }
        catch (Exception ex)
        {
            result = "Scheduled scan could not complete (" + ex.GetType().Name + "). Review Windows Security and activity history; a delegated operation may still be running.";
        }
        finally { IsScanning = false; }
        ScanScheduleStore.Finish(result);
        AppActivity.Record("Scans", result);
        if (!disposed) { scanFinished?.Invoke(result); notify(result); }
    }
    public void Dispose() { disposed = true; timer.Dispose(); }
}
