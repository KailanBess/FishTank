using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using FishTank;

internal static class InsightsUiChecks
{
    private static Control[] All(Control control) => control.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(All(c))).ToArray();
    internal static void Run(string output, Action<bool,string> check)
    {
        Core.Audit("scan-complete", new { exitCode = 0 });
        using (var owner = new Desktop())
        {
            owner.StartPosition = FormStartPosition.Manual; owner.Location = new Point(-30000, -30000); owner.ShowInTaskbar = false; owner.Show();
            foreach (string method in new[] { "OpenRansomwareCenter", "OpenAsrCenter", "OpenApplicationControl", "OpenHardwareSecurity", "OpenSoftwareInventory", "OpenPersistenceMonitor", "OpenExposureMonitor", "OpenIncidentWorkspace", "OpenCfaPreview", "OpenAsrPreview", "OpenSecurityChanges", "OpenStorageCleanup", "OpenPortableTransfer", "OpenWhatsNew", "OpenServerWizard", "OpenToolsHub", "OpenNotificationCenter", "OpenQueueCenter", "OpenBackupCenter", "OpenPrivacyCenter", "OpenProfilesAndLayouts", "OpenOfflineHelp", "OpenWindowsIntegration", "OpenDeviceCenter", "OpenPasskeyCenter", "OpenGitHubUpdates", "OpenUpdateChannel", "OpenScanSchedule", "OpenFolderReminders", "OpenSettingsSearch", "OpenSupportReport", "OpenActivityTimeline", "OpenInitialSetup", "OpenRecoveryCenter", "OpenAccessibilityHelp", "OpenReleaseReadiness", "OpenAccessSettings", "OpenBrowserControls", "OpenMoreTools" })
            {
                Exception failure = null; bool visited = false;
                using (var timer = new Timer { Interval = 250 })
                {
                    timer.Tick += delegate {
                        var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f != owner && f.Modal);
                        if (dialog == null || visited) return;
                        visited = true;
                        try
                        {
                            dialog.Size = new Size(620, 650); dialog.PerformLayout();
                            var controls = All(dialog);
                            check(controls.OfType<Button>().Any(), method + " has no controls");
                            check(controls.Where(c => c.Parent is FlowLayoutPanel).All(c => c.Width <= c.Parent.ClientSize.Width), method + " requires horizontal scrolling");
                            if (method == "OpenSettingsSearch")
                            {
                                controls.OfType<TextBox>().Single(x => x.AccessibleName == "Search settings").Text = "MFA";
                                check(controls.OfType<ListBox>().Single().Items.Count == 1, "Settings keyword search failed");
                            }
                            if (method == "OpenServerWizard")
                            {
                                var steps = controls.OfType<TabControl>().Single();
                                check(steps.TabPages.Count == 4, "Server wizard lacks four setup steps");
                                check(controls.OfType<TextBox>().Single(x => x.AccessibleName == "Private server device token").UseSystemPasswordChar, "Server token is visible");
                                for (int step = 0; step < steps.TabPages.Count; step++)
                                {
                                    steps.SelectedIndex = step; dialog.PerformLayout();
                                    using (var bitmap = new Bitmap(dialog.Width, dialog.Height)) { dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size)); bitmap.Save(Path.Combine(output, "ServerWizard-step" + step + ".png")); }
                                }
                                steps.SelectedIndex = 0;
                            }
                            if (method == "OpenGitHubUpdates") {
                                check(controls.OfType<Button>().Any(x => x.Text == "Check GitHub release"), "GitHub release checking is not discoverable");
                                check(!controls.OfType<Button>().Single(x => x.Text == "Apply update").Enabled, "Apply enabled without a verified update");
                                check(controls.OfType<TextBox>().Single(x => x.AccessibleName == "GitHub update result").Text.StartsWith("Choose Check"), "Opening updates silently contacted GitHub");
                                check(controls.OfType<Button>().Any(x => x.Text == "Use preview updates (no certificate)"), "Certificate-free setup option missing");
                                UpdateChannelPreferences.Save(new UpdateChannelPreferences { Channel = "Preview", AllowUnsignedPreviewUpdates = true });
                                dialog.GetType().GetMethod("RefreshReadiness", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(dialog, null);
                                check(controls.OfType<Button>().Single(x => x.Text == "Download and verify").Enabled, "Preview still requires a publisher certificate");
                                check(!controls.OfType<Button>().Single(x => x.Text == "Apply update").Enabled, "Preview Apply enabled without staged files");
                                check(controls.OfType<Label>().Single(x => x.AccessibleName == "GitHub update readiness").Text.Contains("signatures are not"), "Unsigned preview assurance was hidden");
                                ProtectedStore.Write("pending-github-update", new PendingGitHubUpdate { Version = typeof(Desktop).Assembly.GetName().Version.ToString(), TrustMode = GitHubPreviewUpdates.Mode, Files = new[] { new StagedExecutable(), new StagedExecutable() } });
                                dialog.GetType().GetMethod("RefreshReadiness", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(dialog, null);
                                check(!controls.OfType<Button>().Single(x => x.Text == "Apply update").Enabled, "Already installed update offered for reapplication");
                                ProtectedStore.Write<PendingGitHubUpdate>("pending-github-update", null);
                                using (var bitmap = new Bitmap(dialog.Width, dialog.Height)) { dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size)); bitmap.Save(Path.Combine(output, "GitHubUpdates-preview-enabled.png")); }
                                UpdateChannelPreferences.Save(new UpdateChannelPreferences());
                                dialog.GetType().GetMethod("RefreshReadiness", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(dialog, null);
                                check(!controls.OfType<Button>().Single(x => x.Text == "Download and verify").Enabled, "Turning preview off did not restore signed-only readiness");
                            }
                            if (method == "OpenScanSchedule") check(!controls.OfType<CheckBox>().Single(x => x.Text == "Enable scheduled Defender scans").Checked, "Automatic scanning defaults on");
                            if (method == "OpenSupportReport")
                            {
                                var preview = controls.OfType<TextBox>().Single(x => x.AccessibleName == "Editable support report preview");
                                check(!preview.ReadOnly && preview.Text.Contains("support report"), "Support preview cannot be edited");
                                preview.Text = preview.Text.Replace("Windows:", "Redacted Windows:");
                            }
                            using (var bitmap = new Bitmap(dialog.Width, dialog.Height)) { dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size)); bitmap.Save(Path.Combine(output, method + ".png")); }
                        }
                        catch(Exception ex) { failure = ex; }
                        finally { dialog.Close(); }
                    };
                    timer.Start();
                    typeof(Desktop).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(owner, null);
                    timer.Stop();
                }
                if (failure != null) throw failure;
                check(visited, method + " did not open");
            }
            string realProfile = Core.Data;
            using (var demo = new DemoDialog())
            {
                demo.Show(owner);
                var result = All(demo).OfType<TextBox>().Single();
                foreach (var button in All(demo).OfType<Button>()) { button.PerformClick(); check(result.Text.StartsWith("SIMULATED"), "Demo action did not identify its sample result"); }
                check(Core.Data == realProfile, "Demo changed the real profile");
                using (var bitmap = new Bitmap(demo.Width, demo.Height)) { demo.DrawToBitmap(bitmap, new Rectangle(Point.Empty, demo.Size)); bitmap.Save(Path.Combine(output, "Demo.png")); }
                demo.Close();
            }
            using (var release = new System.Threading.ManualResetEventSlim())
            {
                var operation = new ScanOperation("Quick", null, () => { release.Wait(TimeSpan.FromSeconds(10)); return Core.Json(new { interpretation = "Fixture completed" }); });
                var task = operation.RunAsync();
                using (var progress = new ScanRunDialog(operation, Palette.From(new FishTank.Appearance())))
                {
                    progress.StartPosition = FormStartPosition.Manual; progress.Location = new Point(-30000, -30000); progress.ShowInTaskbar = false;
                    progress.Show(owner); Application.DoEvents();
                    check(All(progress).OfType<Label>().Any(x => x.AccessibleName == "Scan stage" && x.Text.Length > 0), "Scan stage lacks an accessible label");
                    check(All(progress).OfType<Button>().Any(x => x.Text.Contains("cancellation")), "Scan cancellation control missing");
                    using (var bitmap = new Bitmap(progress.Width, progress.Height)) { progress.DrawToBitmap(bitmap, new Rectangle(Point.Empty, progress.Size)); bitmap.Save(Path.Combine(output, "ScanRunDialog.png")); }
                    progress.Close();
                    check(!progress.IsDisposed && !progress.Visible && operation.IsRunning, "Closing progress incorrectly stops a running scan");
                    release.Set();
                    var until = DateTime.UtcNow.AddSeconds(5); while (!task.IsCompleted && DateTime.UtcNow < until) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
                    check(task.IsCompleted && !operation.IsRunning, "Completed scan did not clear running state");
                }
            }
            owner.Hide();
        }
    }
}
