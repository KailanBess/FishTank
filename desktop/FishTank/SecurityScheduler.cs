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
    internal ScanOperation Scan { get; private set; }
    internal bool IsScanning => Scan?.IsRunning == true;
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
            if (!(deferScan?.Invoke() ?? false))
                foreach (var coverage in await Task.Run(() => CoverageMonitor.Tick()))
                    if (!disposed) notify(coverage);
            var workspace=WorkspacePreferences.Load();
            if(workspace.AutoDeleteAuditHistory&&now-workspace.LastHistoryCleanupUtc>=TimeSpan.FromDays(1))
            {
                await Task.Run(()=>{foreach(var file in HistoryPrivacy.Candidates(workspace.HistoryDays))HistoryPrivacy.DeleteSelected(file.Name);});
                WorkspacePreferences.Update(p=>p.LastHistoryCleanupUtc=now);
            }
            string backup = await Task.Run(AutomaticBackups.Tick);
            if (!disposed && backup != null) notify(backup);
            string destination = await Task.Run(BackupDestinationWatch.Tick);
            if (!disposed && destination != null) notify(destination);
            string drill=AutomaticBackups.DrillReminder();if(!disposed&&drill!=null)notify(drill);
            if (WorkspacePreferences.Load().ExternalDrivePrompts)
                foreach (var drive in RemovableDriveOffers.Poll())
                    if (!disposed) notify("External drive " + drive + " is available. Review its contents and add a custom scan in the scan queue; nothing was scanned automatically.");
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
        Scan = new ScanOperation(schedule.Kind, null);
        AppActivity.Record("Scans", "Scheduled " + schedule.Kind + " scan started");
        await Scan.RunAsync();
        string result = schedule.Kind + " scan · " + DateTime.Now.ToString("g") + " · " + Scan.Result + (Scan.CancelRequested ? " " + Scan.Cancellation : "");
        ScanScheduleStore.Finish(result);
        AppActivity.Record("Scans", result);
        if (!disposed) { scanFinished?.Invoke(result); notify(result); }
    }
    public void Dispose() { disposed = true; timer.Dispose(); }
}
