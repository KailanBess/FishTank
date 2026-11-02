using System;
using System.Windows.Forms;
namespace FishTank;
public sealed partial class Desktop
{
    private void OpenAccessSettings()
    {
        using (var dialog = UtilityDialog("App access and authentication", out var panel))
        {
            try
            {
                var mfa = AppAccess.Mfa;
                Help(panel, (AppAccess.Enabled ? "App password is configured." : "No app password is configured.") + " Vaults keep their own master passphrases.\nAuthenticator: " + (mfa.Configured ? "enrolled; " + mfa.RecoveryCodesRemaining + " recovery codes remain." : "not enrolled."));
            }
            catch { Help(panel, "App access settings could not be read. Access remains protected; existing records have not been replaced."); }
            panel.Controls.Add(UtilityButton("Set or change app password", () => AppAccessSettings.Change(dialog)));
            panel.Controls.Add(UtilityButton("Remove app password", () => AppAccessSettings.Remove(dialog)));
            panel.Controls.Add(UtilityButton("Authenticator and recovery codes", () => { using (var mfa = new MfaSettingsDialog(palette)) mfa.ShowDialog(dialog); }));
            panel.Controls.Add(UtilityButton("Lock app access now", () => { dialog.Close(); appLock.Lock(); }));
            panel.Controls.Add(UtilityButton("Idle locking and notifications", () => OpenToolsTab("Locking and reminders")));
            dialog.ShowDialog(this);
        }
        RenderPage();
    }
    private void OpenBrowserControls()
    {
        using (var dialog = UtilityDialog("Local browser dashboard", out var panel))
        {
            Help(panel, "Pair a browser on this PC. Links expire after one minute and work once. Browser access locks with the app and after inactivity. No website is exposed to your network.");
            panel.Controls.Add(UtilityButton("Open and pair browser dashboard", OpenBrowserDashboard));
            panel.Controls.Add(UtilityButton("Stop browser dashboard and revoke sessions", StopBrowserDashboard));
            dialog.ShowDialog(this);
        }
    }
    private void OpenMoreTools()
    {
        using (var dialog = UtilityDialog("More tools", out var panel))
        {
            Help(panel, "Occasional setup, review and support tasks. Opening a tool does not enable optional protection or automation.");
            panel.Controls.Add(UtilityButton("First-run setup", OpenInitialSetup));
            panel.Controls.Add(UtilityButton("Activity timeline", OpenActivityTimeline));
            panel.Controls.Add(UtilityButton("Security and integrity tools", OpenSecurityTools));
            panel.Controls.Add(UtilityButton("Signed release readiness", OpenReleaseReadiness));
            panel.Controls.Add(UtilityButton("Preview and redact support report", OpenSupportReport));
            panel.Controls.Add(UtilityButton("Accessibility and keyboard help", OpenAccessibilityHelp));
            dialog.ShowDialog(this);
        }
    }
}
