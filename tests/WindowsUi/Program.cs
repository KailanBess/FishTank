using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using FishTank;

internal static class Program
{
    static int checks;
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
    static Control[] Descendants(Control control) => control.Controls.Cast<Control>()
        .SelectMany(c => new[] { c }.Concat(Descendants(c))).ToArray();

    [STAThread]
    static int Main(string[] args)
    {
        string temp = Path.Combine(Path.GetTempPath(), "FishTank-ui-" + Guid.NewGuid().ToString("N"));
        string output = Path.GetFullPath(args.Length == 0 ? "ui-artifacts" : args[0]);
        Directory.CreateDirectory(temp);
        Directory.CreateDirectory(output);
        Core.Data = temp;
        SecurityPreferences.Save(new SecurityPreferences { DiskHealthWarnings = false }); // UI fixtures never launch automatic hardware jobs.
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        SimpleExplanations.Install();
        try
        {
            using (var host = new Form { Bounds = new Rectangle(100, 300, 400, 300), ShowInTaskbar = false })
            using (var tank = new BubbleTank { Bounds = new Rectangle(50, 100, 76, 64) })
            {
                host.Controls.Add(tank);
                host.Show();
                tank.Shoot();
                Check(tank.BubbleCount >= 8 && tank.BubbleCount <= 14, "Click burst count outside randomized range");
                var overlay = (Form)typeof(BubbleTank).GetField("overlay", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(tank);
                Check(overlay.Bounds == Screen.FromControl(tank).Bounds, "Bubbles remain clipped to tank or parent");
                Check(overlay.Owner == host && !overlay.ShowInTaskbar, "Bubble overlay ownership is incorrect");
                for (int tick = 0; tick < 45; tick++) tank.Advance();
                Check(tank.BubbleCount > 0, "Bubbles disappeared before reaching the screen top");
                for (int tick = 0; tick < 1500; tick++) tank.Advance();
                Check(tank.BubbleCount == 0 && !overlay.Visible, "Finished animation did not clean up");
                tank.ReducedMotion = true;
                tank.Shoot();
                Check(tank.BubbleCount == 0, "Reduced motion still emits bubbles");
                tank.ReducedMotion = false;
                tank.Shoot();
                tank.Dispose();
                Check(overlay.IsDisposed, "Disposing the tank leaked its overlay");
                host.Hide();
            }
            InsightsUiChecks.Run(output, Check);
            string[] pages = { "Dashboard", "Antivirus", "Real-time monitoring", "Network Security", "System Integrity", "Security Center", "Quarantine", "Breach Checks", "Protected Files", "Password Manager", "Tailscale Devices", "Settings" };
            foreach (string theme in new[] { "Dark", "Light" })
            foreach (int scale in new[] { 100, 150 })
            foreach (bool advanced in new[] { false, true })
            {
                var appearance = new FishTank.Appearance { Theme = theme, TextScale = scale, AdvancedMode = advanced, ReducedMotion = true };
                AppearanceStore.Save(appearance);
                using (var form = new Desktop())
                {
                    form.Size = new Size(1000, 740);
                    foreach (string page in pages.Where(p => NavigationPolicy.IsVisible(p, advanced)))
                    {
                        typeof(Desktop).GetMethod("Navigate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, new object[] { page });
                        form.RenderPreview(Path.Combine(output, $"{theme}-{scale}-{advanced}-{page.Replace(' ', '-')}.png"), page, appearance);
                        // Exercise live transitions too, rather than only the initial shell colors.
                        var navigate = typeof(Desktop).GetMethod("Navigate", BindingFlags.Instance | BindingFlags.NonPublic);
                        navigate.Invoke(form, new object[] { "Dashboard" });
                        navigate.Invoke(form, new object[] { page });
                        var navigation = Descendants(form).OfType<CleanButton>().Where(b => b.Tag is string).ToArray();
                        Check(navigation.Length == (advanced ? 12 : 8), "Incorrect navigation count");
                        var colors = Palette.From(appearance);
                        foreach (var button in navigation)
                        {
                            Check(button.Hover == button.Fill, "Sidebar hover changes fill: " + button.Tag);
                            Check(button.BackColor == button.Parent.BackColor, "Sidebar corner background mismatch");
                            Check(button.AccessibleName == (string)button.Tag, "Missing navigation accessible name");
                            Check(button.Border == ((string)button.Tag == page ? colors.Accent : colors.Sidebar), "Selection border did not follow navigation");
                        }
                        var flow = Descendants(form).OfType<FlowLayoutPanel>().First(p => p.FlowDirection == FlowDirection.TopDown);
                        Check(flow.Controls.Cast<Control>().All(c => c.Width <= flow.ClientSize.Width), "Page requires horizontal scrolling");
                    }
                }
                using (var tools = new SecurityToolsDialog(Palette.From(appearance), advanced, scale / 100f))
                {
                    tools.Size = new Size(760, 620);
                    tools.StartPosition = FormStartPosition.Manual;
                    tools.Location = new Point(-30000, -30000);
                    tools.ShowInTaskbar = false;
                    tools.Show();
                    var tabs = Descendants(tools).OfType<TabControl>().Single();
                    foreach (TabPage tab in tabs.TabPages)
                    {
                        tabs.SelectedTab = tab;
                        tools.PerformLayout(); Application.DoEvents();
                        Check(Descendants(tab).OfType<Button>().Any(), "Security tab has no actions");
                        foreach (var input in Descendants(tab).OfType<TextBox>().Where(t => t.Parent is FlowLayoutPanel))
                            Check(!string.IsNullOrEmpty(input.AccessibleName), "Security input lacks accessible name");
                        using (var bitmap = new Bitmap(tools.Width, tools.Height))
                        {
                            tools.DrawToBitmap(bitmap, new Rectangle(Point.Empty, tools.Size));
                            bitmap.Save(Path.Combine(output, $"tools-{theme}-{scale}-{advanced}-{tab.Text.Replace(' ', '-')}.png"));
                        }
                    }
                    tools.Hide();
                }
                using (var live = new LiveDetectionSession(null))
                using (var history = new LiveDetectionDialog(live, Palette.From(appearance), advanced))
                {
                    history.Size = new Size(800, 620); history.StartPosition = FormStartPosition.Manual;
                    history.Location = new Point(-30000, -30000); history.ShowInTaskbar = false; history.Show(); Application.DoEvents();
                    Check(!live.Enabled, "Opening history silently enabled detection");
                    using (var bitmap = new Bitmap(history.Width, history.Height)) { history.DrawToBitmap(bitmap, new Rectangle(Point.Empty, history.Size)); bitmap.Save(Path.Combine(output, $"live-{theme}-{scale}-{advanced}.png")); }
                    history.Hide();
                }
                using (var safe = new ProtectedFilesDialog(Palette.From(appearance)))
                {
                    safe.Size = new Size(760, 600); safe.StartPosition = FormStartPosition.Manual;
                    safe.Location = new Point(-30000, -30000); safe.ShowInTaskbar = false; safe.Show(); Application.DoEvents();
                    Check(Descendants(safe).OfType<DataGridView>().Single().Rows.Count == 0, "Locked file container exposed names");
                    using (var bitmap = new Bitmap(safe.Width, safe.Height)) { safe.DrawToBitmap(bitmap, new Rectangle(Point.Empty, safe.Size)); bitmap.Save(Path.Combine(output, $"protected-{theme}-{scale}-{advanced}.png")); }
                    safe.Hide();
                }
            }
            var account = new PasswordEntry { Kind = "Email", Title = "Personal email", Email = "fixture@example.test", Password = "Email fixture secret!" };
            var connected = new PasswordEntry { Title = "EA", LinkedEmailId = account.Id, Username = "EAUsername", Password = "Service fixture secret!" };
            using (var editor = new PasswordEditor(connected, false, Palette.From(new FishTank.Appearance()), new[] { account }))
            {
                editor.Size = new Size(630, 700); editor.StartPosition = FormStartPosition.Manual;
                editor.Location = new Point(-30000, -30000); editor.ShowInTaskbar = false; editor.Show(); Application.DoEvents();
                var controls = Descendants(editor);
                var connection = controls.OfType<ComboBox>().Single(c => c.AccessibleName == "Email connection");
                Check(connection.SelectedIndex == 1, "Service did not retain its email connection");
                var serviceUser = controls.OfType<TextBox>().Single(c => c.AccessibleName == "Username");
                Check(serviceUser.Text == "EAUsername", "Separate service username was lost");
                var linked = controls.OfType<CheckBox>().Single(c => c.Text.StartsWith("Sign in through"));
                linked.Checked = true;
                Check(!controls.OfType<TextBox>().Single(c => c.AccessibleName == "Password").Enabled, "Linked sign-in still required a separate password");
                using (var bitmap = new Bitmap(editor.Width, editor.Height)) { editor.DrawToBitmap(bitmap, new Rectangle(Point.Empty, editor.Size)); bitmap.Save(Path.Combine(output, "organized-password-editor.png")); }
                ((Button)editor.AcceptButton).PerformClick();
                Check(editor.Value.LinkedEmailId == account.Id && editor.Value.UsesLinkedSignIn && editor.Value.Password == "", "Editor did not save organized sign-in data");
            }
            const string password = "Test-only UI lock passphrase 2026!";
            AppAccess.SetPassword(null, password);
            using (var owner = new Desktop())
            using (var vault = new PasswordManagerDialog(Palette.From(new FishTank.Appearance())))
            using (var responder = new Timer { Interval = 50 })
            {
                owner.StartPosition = FormStartPosition.Manual; owner.Location = new Point(-30000, -30000); owner.ShowInTaskbar = false;
                vault.StartPosition = FormStartPosition.Manual; vault.Location = new Point(-30000, -30000); vault.ShowInTaskbar = false;
                owner.Show(); vault.Show(owner);
                var sessionField = typeof(PasswordManagerDialog).GetField("session", BindingFlags.Instance | BindingFlags.NonPublic);
                sessionField.SetValue(vault, PasswordSession.Create(Path.Combine(temp, "lock-test.ftvault"), password));
                Exception failure = null; bool submitted = false;
                responder.Tick += delegate {
                    if (submitted) return;
                    var prompt = Application.OpenForms.Cast<Form>().OfType<AppUnlockDialog>().FirstOrDefault();
                    if (prompt == null) return;
                    submitted = true;
                    try {
                        Check(!owner.Visible && !vault.Visible, "Sensitive windows remained visible while app locked");
                        Check(sessionField.GetValue(vault) == null, "App locking left password vault unlocked");
                        Check(!AppAccess.SessionUnlocked, "App session not locked before sign-in");
                    } catch (Exception ex) { failure = ex; }
                    ((TextBox)typeof(AppUnlockDialog).GetField("password", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(prompt)).Text = password;
                    ((Button)typeof(AppUnlockDialog).GetField("unlock", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(prompt)).PerformClick();
                };
                responder.Start();
                var controller = (AppLockController)typeof(Desktop).GetField("appLock", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
                controller.Lock();
                responder.Stop();
                if (failure != null) throw failure;
                Check(submitted && AppAccess.SessionUnlocked && owner.Visible && vault.Visible, "App did not restore its UI after verified sign-in");
                controller.ParkInTray(); owner.Hide(); vault.Hide();
                Check(controller.IsParked && controller.IsLocked && !AppAccess.SessionUnlocked, "Background parking left app access unlocked");
                submitted = false; failure = null; responder.Start();
                bool restored = controller.RestoreFromTray(); responder.Stop();
                if (failure != null) throw failure;
                Check(restored && submitted && AppAccess.SessionUnlocked && !controller.IsParked, "Tray restore skipped or failed sign-in");
            }
            Console.WriteLine(checks + " Windows UI checks passed; inspect PNGs for text clipping, focus, and spacing.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { Directory.Delete(temp, true); }
    }
}
