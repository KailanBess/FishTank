using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FishTank;

internal sealed class GitHubUpdatesDialog : Form
{
    private readonly Label readiness;
    private readonly TextBox result;
    private readonly Button check, download, apply, preferences, channel;
    private bool working;
    internal GitHubUpdatesDialog(Palette colors, Action openPreferences, Action openChannel)
    {
        Text = "FishTank · GitHub updates"; AutoScaleMode = AutoScaleMode.Dpi;
        Size = new Size(820, 680); MinimumSize = new Size(620, 560); StartPosition = FormStartPosition.CenterParent;
        BackColor = colors.Background; ForeColor = colors.Text; Font = new Font("Segoe UI", 10);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(18) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var heading = new Label { Text = "GitHub updates", AutoSize = true, Font = new Font("Segoe UI", 18, FontStyle.Bold), Margin = new Padding(0, 0, 0, 12) };
        readiness = new Label { AutoSize = true, UseMnemonic = false, AccessibleName = "GitHub update readiness", Margin = new Padding(0, 0, 0, 12) };
        var notice = new Label { Text = "Check releases from KailanBess/FishTank, then download and verify before applying. Clicking Check or Download contacts GitHub, which can see your IP address. Opening this window makes no network request. Unsigned preview ZIPs cannot be applied as trusted updates.", AutoSize = true, UseMnemonic = false, Margin = new Padding(0, 0, 0, 12) };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = Padding.Empty };
        Func<string, Button> button = title => { var b = new CleanButton { Text = title, Width = TextRenderer.MeasureText(title, Font).Width + 40, Height = 42, Fill = colors.Surface2, Hover = colors.Surface, Ink = colors.Text, Border = colors.Line, FocusColor = colors.Accent, Radius = 8, Margin = new Padding(0, 0, 8, 8), UseMnemonic = false, AccessibleName = title, FlatStyle = FlatStyle.Flat, BackColor = colors.Surface2, ForeColor = colors.Text }; b.FlatAppearance.BorderColor = colors.Line; actions.Controls.Add(b); return b; };
        check = button("Check GitHub release"); download = button("Download and verify"); apply = button("Apply update");
        preferences = button("Publisher trust settings"); channel = button("Release channel");
        var releases = button("View GitHub releases"); var last = button("Last update result");
        check.Click += async delegate { await RunAsync(GitHubUpdates.Check); };
        download.Click += async delegate { await RunAsync(GitHubUpdates.DownloadAndStage); };
        apply.Click += delegate {
            if (working) return;
            if (ExplainedMessageBox.Show(this, "Apply the verified GitHub update? FishTank will reverify both executables, close, replace them, retain rollback copies and restart. Finish open work first. Your private data is retained.", "Apply GitHub update", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try { GitHubUpdateInstaller.Start(); } catch (Exception ex) { result.Text = "Update was not applied.\r\n" + ex.Message; RefreshReadiness(); }
        };
        preferences.Click += delegate { openPreferences(); RefreshReadiness(); };
        channel.Click += delegate { openChannel(); RefreshReadiness(); };
        releases.Click += delegate { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/" + GitHubReleasePolicy.Repository + "/releases") { UseShellExecute = true }); } catch (Exception ex) { result.Text = ex.Message; } };
        last.Click += delegate { try { result.Text = GitHubUpdateInstaller.LastResult(); } catch (Exception ex) { result.Text = ex.Message; } };
        result = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = colors.Surface, ForeColor = colors.Text, AccessibleName = "GitHub update result", Text = "Choose Check GitHub release to look for a newer published release. Checking never installs an update." };
        layout.Controls.Add(heading, 0, 0); layout.Controls.Add(readiness, 0, 1); layout.Controls.Add(notice, 0, 2); layout.Controls.Add(actions, 0, 3); layout.Controls.Add(result, 0, 4);
        layout.SizeChanged += delegate { int width = Math.Max(200, layout.ClientSize.Width - layout.Padding.Horizontal); readiness.MaximumSize = notice.MaximumSize = new Size(width, 0); };
        Controls.Add(layout); RefreshReadiness();
    }
    private void RefreshReadiness()
    {
        if (IsDisposed) return;
        check.Enabled = preferences.Enabled = channel.Enabled = !working;
        download.Enabled = apply.Enabled = false;
        try {
            var prefs = SecurityPreferences.Load(); var selected = UpdateChannelPreferences.Load(); var pending = GitHubUpdates.Pending();
            bool trusted = !string.IsNullOrEmpty(prefs.PublisherCertificateSha256);
            readiness.Text = "Installed: " + typeof(Program).Assembly.GetName().Version + " · Channel: " + selected.Channel
                + "\r\n" + (trusted ? "Publisher trust configured." : "Publisher trust is not configured. Set an independently verified publisher certificate pin before downloading an installable update.")
                + "\r\n" + (pending == null ? "No verified update is ready to apply." : "Staged version: " + pending.Version + ". Apply rechecks signatures, digests and version before replacing files.");
            download.Enabled = !working && trusted;
            apply.Enabled = !working && trusted && pending?.Files != null && pending.Files.Length == 2;
        } catch { readiness.Text = "Update preferences or staging history could not be read. Applying updates is unavailable until this is resolved; existing data has been preserved."; }
    }
    private async Task RunAsync(Func<string> operation)
    {
        if (working) return;
        working = true; RefreshReadiness(); result.Text = "Contacting GitHub… No update has been applied.";
        try { string text = await Task.Run(operation); if (!IsDisposed) result.Text = text; }
        catch (Exception ex) { if (!IsDisposed) result.Text = "The update step did not complete. No update was applied.\r\n" + ex.Message; }
        finally { working = false; if (!IsDisposed) RefreshReadiness(); }
    }
}

public sealed partial class Desktop
{
    private void OpenGitHubUpdates()
    {
        using (var dialog = new GitHubUpdatesDialog(palette, () => OpenToolsTab("Locking and reminders"), OpenUpdateChannel)) dialog.ShowDialog(this);
    }
}
