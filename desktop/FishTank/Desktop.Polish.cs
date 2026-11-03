using System;
using System.Linq;
using System.Windows.Forms;
namespace FishTank;
internal sealed class SetupProgress
{
    public bool Completed;
    public DateTime DeferredUtc;
    internal static SetupProgress Load() => ProtectedStore.Read("setup-progress", () => new SetupProgress()) ?? throw new InvalidOperationException("Setup progress unreadable.");
    internal void Save() => ProtectedStore.Write("setup-progress", this);
}
public static class NotificationDestination
{
    public static string For(string message)
    {
        message = (message ?? "").ToLowerInvariant();
        return message.Contains("backup") ? "Recovery" : message.Contains("disk health") ? "Disk" : message.Contains("folder integrity") ? "Integrity" : message.Contains("scan") ? "Scan" : message.Contains("github") ? "Updates" : "Security";
    }
}
public sealed partial class Desktop
{
    private ScanOperation foregroundScan;
    private ScanRunDialog scanWindow;
    private string notificationTarget = "Security";
    private bool notificationPending;
    private ScanOperation ActiveScan => foregroundScan?.IsRunning == true ? foregroundScan : securityScheduler?.Scan?.IsRunning == true ? securityScheduler.Scan : new[] { foregroundScan, securityScheduler?.Scan }.Where(x => x != null).OrderByDescending(x => x.StartedUtc).FirstOrDefault();
    private async void StartVisibleScan(string kind, string path)
    {
        if (HasRunningOperation) { SetStatus("Finish the active operation before starting another scan."); return; }
        var operation = foregroundScan = new ScanOperation(kind, path);
        var task = operation.RunAsync();
        ShowScanProgress(operation);
        SetStatus(kind + " scan running. Open scan progress for elapsed time and cancellation evidence.");
        await task;
        if (IsDisposed) return;
        lastScan = kind + " scan · " + operation.Result; SetStatus(lastScan);
        if (current == "Antivirus") RenderPage();
    }
    private void ShowScanProgress(ScanOperation operation, Form owner = null)
    {
        if (operation == null) { SetStatus("No active or recent scan in this session."); return; }
        if (scanWindow != null && !scanWindow.IsDisposed) scanWindow.Dispose();
        scanWindow = new ScanRunDialog(operation, palette) { Icon = Icon }; scanWindow.Show(owner ?? this);
    }
    private void OpenCurrentScan() => ShowScanProgress(ActiveScan ?? foregroundScan);
    private void OpenNotification() { notificationPending = true; RestoreFromTray(); DispatchNotification(); }
    private void DispatchNotification()
    {
        if (!notificationPending || !Visible || appLock == null || appLock.IsLocked || Application.OpenForms.Cast<Form>().Any(f => f.Modal)) return;
        notificationPending = false;
        switch (notificationTarget)
        {
            case "Monitoring": Navigate("Real-time monitoring"); using (var history = new LiveDetectionDialog(liveDetection, palette, appearance.AdvancedMode)) history.ShowDialog(this); break;
            case "Recovery": OpenRecoveryCenter(); break;
            case "Disk": OpenToolsTab("Security reviews"); break;
            case "Integrity": OpenToolsTab("Folder integrity"); break;
            case "Updates": OpenToolsTab("Downloads and updates"); break;
            case "Scan": Navigate("Antivirus"); OpenCurrentScan(); break;
            default: OpenToolsTab("Security reviews"); break;
        }
    }
    private void OfferInitialSetup()
    {
        try { var progress = SetupProgress.Load(); if (!progress.Completed && progress.DeferredUtc == DateTime.MinValue) OpenInitialSetup(); }
        catch { SetStatus("Setup progress could not be read. Open Settings → First-run setup; existing settings were preserved."); }
    }
    private void OpenInitialSetup()
    {
        try
        {
            var progress = SetupProgress.Load();
            using (var dialog = UtilityDialog("First-run setup", out var panel))
            {
                Help(panel, "Set up FishTank at your own pace. These steps do not change Windows protection policy. Optional scans, notifications and reminders remain off until you choose them.");
                Help(panel, "1. Check protection: review Windows protection and any missing evidence.");
                panel.Controls.Add(UtilityButton("Run read-only protection check", async () => {
                    try { await System.Threading.Tasks.Task.Run(() => SecurityReviews.Run("SecurityHealth", false)); if (!dialog.IsDisposed) MessageBox.Show(dialog, ProtectionOverview.Status(SecurityReviews.Latest("SecurityHealth"), DateTime.UtcNow) + "\nOpen Dashboard for findings.", "Protection check finished"); }
                    catch (Exception ex) { if (!dialog.IsDisposed) MessageBox.Show(dialog, "Couldn’t check: " + ex.Message); }
                }));
                Help(panel, "2. Protect app access: choose a password, then optionally enroll an authenticator. Your vault keeps its own passphrase.");
                panel.Controls.Add(UtilityButton("Configure app password", () => AppAccessSettings.Change(dialog)));
                panel.Controls.Add(UtilityButton("Configure authenticator and recovery codes", () => { using (var mfa = new MfaSettingsDialog(palette)) mfa.ShowDialog(dialog); }));
                Help(panel, "3. Choose optional automation and reminders. Backups are made with your own backup tool.");
                panel.Controls.Add(UtilityButton("Choose scan schedule", OpenScanSchedule));
                panel.Controls.Add(UtilityButton("Choose backup reminders", OpenFolderReminders));
                panel.Controls.Add(UtilityButton("Configure notifications and locking", () => OpenToolsTab("Locking and reminders")));
                Help(panel, "4. Review recovery and keyboard accessibility before finishing.");
                panel.Controls.Add(UtilityButton("Open recovery center", OpenRecoveryCenter));
                panel.Controls.Add(UtilityButton("Accessibility and keyboard shortcuts", OpenAccessibilityHelp));
                panel.Controls.Add(UtilityButton("Finish setup", () => { progress.Completed = true; progress.Save(); dialog.Close(); }));
                panel.Controls.Add(UtilityButton("Do this later", () => dialog.Close()));
                dialog.FormClosing += delegate { if (!progress.Completed) { progress.DeferredUtc = DateTime.UtcNow; progress.Save(); } };
                dialog.ShowDialog(this);
            }
            RenderPage();
        }
        catch (Exception ex) { ShowMessage("Setup unavailable", ex.Message); }
    }
    private void OpenRecoveryCenter()
    {
        using (var dialog = UtilityDialog("Recovery center", out var panel))
        {
            Help(panel, "Choose a recovery task. Restoring quarantine can reintroduce a threat. Backup verification and isolated restore tests do not replace your active vault.");
            panel.Controls.Add(UtilityButton("Open quarantine in Advanced view", () => { dialog.Close(); if (!appearance.AdvancedMode) { var saved = appearance.Copy(); saved.AdvancedMode = true; AppearanceStore.Save(saved); appearance = saved; current = "Quarantine"; BuildShell(); } else Navigate("Quarantine"); StartWork(Vault.List, "Vault"); }));
            panel.Controls.Add(UtilityButton("Verify backup or test restore in isolation", () => OpenToolsTab("Backup and recovery")));
            panel.Controls.Add(UtilityButton("Review important folder backup reminders", OpenFolderReminders));
            panel.Controls.Add(UtilityButton("Review installation and rollback result", () => OpenToolsTab("Downloads and updates")));
            panel.Controls.Add(UtilityButton("Review authenticator recovery-code status", () => { using (var mfa = new MfaSettingsDialog(palette)) mfa.ShowDialog(dialog); }));
            panel.Controls.Add(UtilityButton("Open Windows recovery options", () => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:recovery") { UseShellExecute = true })));
            Help(panel, "Keep your vault master passphrase and BitLocker recovery information on separate storage. An older backup can contain older passwords. FishTank cannot recover a lost vault passphrase. Use Windows Security for Defender-managed quarantine; FishTank’s manual quarantine is separate.");
            panel.Controls.Add(UtilityButton("Preview a support report", OpenSupportReport)); dialog.ShowDialog(this);
        }
    }
    private void SystemPreferencesChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (IsDisposed || !IsHandleCreated) return;
        if (e.Category == Microsoft.Win32.UserPreferenceCategory.Accessibility || e.Category == Microsoft.Win32.UserPreferenceCategory.Color)
            try { BeginInvoke((Action)(() => { if (!IsDisposed) BuildShell(); })); } catch (InvalidOperationException) { }
    }
    private void OpenAccessibilityHelp()
    {
        using (var dialog = UtilityDialog("Accessibility and keyboard shortcuts", out var panel))
        {
            Help(panel, "Tab / Shift+Tab: move between controls. Enter or Space: activate the focused button. Alt+1: Dashboard. Alt+2: Antivirus. Ctrl+F: search settings. Ctrl+Shift+R: recovery center. Ctrl+Shift+L: lock app access. F1: explanations. Text fields support selecting and copying text.");
            Help(panel, "Windows High Contrast takes precedence over custom colors. Appearance settings offer text scaling, reduced motion and stronger focus indicators. Text labels accompany protection status.");
            panel.Controls.Add(UtilityButton("Customize text size, motion and focus", Customize)); dialog.ShowDialog(this);
        }
    }
    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Shift | Keys.L)) { appLock?.Lock(); return true; }
        if (appLock?.IsLocked == true) return base.ProcessCmdKey(ref message, keyData);
        try { string shortcut=ShortcutStore.Find(keyData); if(shortcut!=null){OpenTool(shortcut);return true;} } catch(Exception ex) { SetStatus("Custom shortcut could not be read: "+ex.Message); }
        if (keyData == (Keys.Control | Keys.K)) { OpenToolsHub(); return true; }
        if (keyData == (Keys.Control | Keys.F)) { OpenSettingsSearch(); return true; }
        if (keyData == (Keys.Control | Keys.Shift | Keys.R)) { OpenRecoveryCenter(); return true; }
        if (keyData == (Keys.Alt | Keys.D1)) { Navigate("Dashboard"); return true; }
        if (keyData == (Keys.Alt | Keys.D2)) { Navigate("Antivirus"); return true; }
        return base.ProcessCmdKey(ref message, keyData);
    }
}
