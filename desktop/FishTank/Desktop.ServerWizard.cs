using System;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace FishTank;
public sealed partial class Desktop
{
    private void OpenServerWizard()
    {
        using (var dialog = UtilityDialog("Server connection wizard", out var panel))
        {
            var tabs = new TabControl { Height = 350, AccessibleName = "Server setup steps" };
            FlowLayoutPanel Page(string title)
            {
                var page = new TabPage(title) { BackColor = palette.Background, ForeColor = palette.Text };
                var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(8), BackColor = palette.Background, ForeColor = palette.Text };
                flow.SizeChanged += delegate { foreach (Control control in flow.Controls) { int width = Math.Max(180, flow.ClientSize.Width - 30); control.Width = width; if (control is Label label) label.MaximumSize = new System.Drawing.Size(width, 0); } };
                page.Controls.Add(flow); tabs.TabPages.Add(page); return flow;
            }
            var installation = Page("1 Install"); var connection = Page("2 Connect"); var enrollment = Page("3 Verify delivery"); var next = Page("4 Optional features");
            Help(installation, "1. Install the FishTank service on your existing server. Configure real email/SMS delivery and stable HTTPS routes; keep the raw listener on loopback. Follow SERVER-EXPANSION.md in the download. This wizard connects to the service after installation.");
            var status = new TextBox { Multiline = true, ReadOnly = true, Height = 100, ScrollBars = ScrollBars.Vertical, AccessibleName = "Server connection status", Text = "No connection has been tested in this wizard." };
            Help(connection, "2. Enter the exact HTTPS origin and private device token provisioned on that server. The token is sent only to this origin over verified HTTPS; inspect the address before testing.");
            var origin = new TextBox { AccessibleName = "HTTPS server origin", MaxLength = 2048 };
            var token = new TextBox { AccessibleName = "Private server device token", UseSystemPasswordChar = true, MaxLength = 43 };
            try { var config = SelfHostedMfa.Load(); if (config != null) { origin.Text = config.Origin; status.Text = "A saved connection exists. Its token is not displayed. Test the saved connection below, or enter a replacement token deliberately."; } } catch { status.Text = "Saved connection could not be read. It has not been replaced. Review the existing configuration before changing it."; }
            Help(connection, "HTTPS origin:"); connection.Controls.Add(origin); Help(connection, "Private device token:"); connection.Controls.Add(token);
            Help(enrollment, "3. Saving or enrolling requires your app password and a fresh offline MFA/recovery factor for each action. Configure offline MFA in App access first. These credentials are checked locally.");
            var password = new TextBox { AccessibleName = "Current app password for server setup", UseSystemPasswordChar = true, MaxLength = 1024 };
            var factor = new TextBox { AccessibleName = "Fresh MFA or recovery factor for server setup", UseSystemPasswordChar = true, MaxLength = 256 };
            Help(enrollment, "App password:"); enrollment.Controls.Add(password); Help(enrollment, "Fresh MFA or recovery factor:"); enrollment.Controls.Add(factor);
            bool working = false;
            async Task Work(Func<Task> action)
            {
                if (working) throw new InvalidOperationException("Wait for the current connection step.");
                working = true; origin.Enabled = token.Enabled = password.Enabled = factor.Enabled = false;
                try { await action(); }
                catch (Exception ex) { if (!dialog.IsDisposed) status.Text = ServerConnectionAdvice.Explain(ex); }
                finally { if (!dialog.IsDisposed) origin.Enabled = token.Enabled = password.Enabled = factor.Enabled = true; working = false; }
            }
            connection.Controls.Add(UtilityAsyncButton("Test entered HTTPS origin and token without saving", async () => await Work(async () => { string address = origin.Text.Trim(), secret = token.Text; await Task.Run(() => SelfHostedMfa.Discover(address, secret)); if (!dialog.IsDisposed) status.Text = "HTTPS and authenticated device status succeeded. No settings were saved. Actual delivery and channel enrollment still need testing."; })));
            connection.Controls.Add(UtilityAsyncButton("Test the saved connection", async () => await Work(async () => { var saved = SelfHostedMfa.Load() ?? throw new InvalidOperationException("No server connection has been saved."); var discovered = await Task.Run(() => SelfHostedMfa.Discover(saved.Origin, saved.DeviceToken)); if (!dialog.IsDisposed) status.Text = discovered.Binding == saved.Binding ? "Saved HTTPS/device status verified. This does not prove delivery, passkey enrollment or remote sharing." : "Server destination binding changed. Reconfigure this device and reenroll delivery channels before using it for verification."; })));
            enrollment.Controls.Add(UtilityAsyncButton("Authenticate and save this connection", async () => await Work(async () => { string address = origin.Text.Trim(), secret = token.Text, pwd = password.Text, code = factor.Text; try { await Task.Run(() => SelfHostedMfa.Configure(pwd, code, address, secret)); if (!dialog.IsDisposed) { token.Clear(); status.Text = "Connection saved with Windows protection. Enter a fresh offline factor before channel enrollment. Existing channel enrollment was reset; verify the destination below."; } } finally { if (!dialog.IsDisposed) factor.Clear(); } })));
            enrollment.Controls.Add(UtilityAsyncButton("Test actual delivery and enroll its destination", async () => await Work(async () => { string pwd = password.Text, currentFactor = factor.Text; using (var delivery = new DeliveryChallengeDialog(pwd, "enroll")) { if (delivery.ShowDialog(dialog) != DialogResult.OK) return; string channel = delivery.Channel, challenge = delivery.ChallengeId, code = delivery.Code; try { await Task.Run(() => SelfHostedMfa.EnrollChannel(pwd, currentFactor, channel, challenge, code)); if (!dialog.IsDisposed) status.Text = "The delivered code was verified and this channel was enrolled. Keep offline recovery access. Passkeys and reporting keys require their own explicit enrollment."; } finally { if (!dialog.IsDisposed) factor.Clear(); } } })));
            enrollment.Controls.Add(UtilityButton("Open offline MFA and recovery settings", () => { using (var mfa = new MfaSettingsDialog(palette)) mfa.ShowDialog(dialog); }));
            next.Controls.Add(UtilityButton("Continue to passkeys and security keys", OpenPasskeyCenter));
            next.Controls.Add(UtilityButton("Continue to signed device status", OpenDeviceCenter));
            Help(connection, "Troubleshooting: a missing API route usually means the service or proxy route is absent; an unauthorized response needs the correct device token. DNS/VPN, HTTPS certificate and timeout failures have separate guidance above. Never disable certificate verification to make a connection pass.");
            Help(installation, "Install the prepared service files, edit the private environment on the server, configure your existing HTTPS proxy routes, start the service, and provision a unique private device token. The server kit contains SERVER-EXPANSION.md with the full steps. No server installation is performed from this wizard.");
            installation.Controls.Add(UtilityButton("Next: check the connection", () => tabs.SelectedIndex = 1));
            connection.Controls.Add(UtilityButton("Next: save and verify delivery", () => tabs.SelectedIndex = 2));
            enrollment.Controls.Add(UtilityButton("Next: optional server features", () => tabs.SelectedIndex = 3));
            Help(next, "After real delivery is verified, enroll passkeys/security keys or a signed-reporting key separately. Keep offline MFA and recovery access. Connection success does not enable these optional features.");
            panel.Controls.Add(tabs); panel.Controls.Add(status);
            dialog.SizeChanged += delegate { tabs.Height = Math.Max(220, dialog.ClientSize.Height - 210); };
            dialog.FormClosing += delegate(object sender, FormClosingEventArgs e) { if (working) e.Cancel = true; };
            try { dialog.ShowDialog(this); } finally { token.Clear(); password.Clear(); factor.Clear(); }
        }
    }
}
