using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FishTank;

public sealed class SecurityToolsDialog : Form
{
    private readonly Palette colors;
    private readonly Label state;
    private readonly Label backupStatus;
    private readonly TextBox output;
    private readonly CheckBox details;
    private readonly List<Button> buttons = new List<Button>();
    private string report = "Choose a tool above. Checks are read-only unless an action explicitly asks to save a baseline, preference, backup or staged update.";
    private bool working;
    private string folder = "";
    private readonly TextBox exclusions;
    private readonly CheckBox recursive;
    private readonly Label folderName;

    public SecurityToolsDialog(Palette colors, bool advanced, float textScale = 1f, string initialTab = null)
    {
        this.colors = colors;
        Text = "FishTank · Security and integrity tools";
        AutoScaleMode = AutoScaleMode.Dpi;
        Size = new Size(1050, 850); MinimumSize = new Size(760, 620);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = colors.Background; ForeColor = colors.Text; Font = new Font("Segoe UI", 10 * textScale);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(14) };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var tabs = new TabControl { Dock = DockStyle.Fill };
        Shown += delegate { if (initialTab != null) foreach (TabPage tab in tabs.TabPages) if (tab.Text == initialTab) tabs.SelectedTab = tab; };
        layout.Controls.Add(tabs, 0, 0);
        var reviews = Page(tabs, "Security reviews", "Check Windows protection, startup changes, accounts, permissions and listening ports. Unknown or partial results never mean safe. Reviews save up to six encrypted snapshots per category on this Windows profile.");
        Add(reviews, "Protection health", () => Run(() => SecurityReviews.Run("SecurityHealth", false)));
        Add(reviews, "Disk health and free space", () => Run(DiskHealthChecks.Run));
        Add(reviews, "Startup review", () => Run(() => SecurityReviews.Run("StartupInventory", false)));
        Add(reviews, "Account protection", () => Run(() => SecurityReviews.Run("Accounts", false)));
        Add(reviews, "Camera, microphone and location", () => Run(() => SecurityReviews.Run("Permissions", false)));
        Add(reviews, "Browser extensions", () => Run(() => Core.Collect("Browsers")));
        Add(reviews, "Network exposure", () => Run(() => SecurityReviews.Run("Exposure", false)));
        Add(reviews, "Accept startup baseline", () => {
            if (Confirm("Record or replace the trusted startup baseline? Only do this after reviewing the startup configuration and trusting it. Future comparisons use this baseline until you explicitly replace it."))
                Authorize(() => Run(() => SecurityReviews.Run("StartupInventory", true)));
        });
        Add(reviews, "Compare startup baseline", () => Run(SecurityReviews.CompareStartup));
        Add(reviews, "Security change history", () => Run(SecurityReviews.History));
        Add(reviews, "Open Windows Update", () => OpenSettings("ms-settings:windowsupdate"));
        Add(reviews, "Open app privacy settings", () => OpenSettings("ms-settings:privacy"));

        var integrity = Page(tabs, "Folder integrity", "Remember a trusted folder, then compare its contents without changing files. Limits: 5,000 files, 64 MiB per file, 512 MiB total; links and unreadable paths create coverage gaps. One active folder baseline is supported. Scheduled comparisons run only while FishTank is open.");
        folderName = Explain(integrity, "No folder selected.");
        recursive = new CheckBox { Text = "Include subfolders", AutoSize = true, Checked = true, ForeColor = colors.Text };
        integrity.Controls.Add(recursive);
        Explain(integrity, "Optional exclusions: relative file/folder names, one per line. No wildcards. Example: cache");
        exclusions = new TextBox { Multiline = true, Height = 65, Width = 500, ScrollBars = ScrollBars.Vertical, AccessibleName = "Relative integrity exclusions, one per line" };
        integrity.Controls.Add(exclusions);
        Add(integrity, "Choose folder", ChooseFolder);
        Add(integrity, "Accept folder baseline", () => {
            string selected = folder; bool subfolders = recursive.Checked;
            string[] excluded = exclusions.Lines;
            if (selected == "") throw new InvalidOperationException("Choose a folder first.");
            if (Confirm("Record or replace the folder baseline? Only trust files whose current contents you already trust. Excluded paths are not monitored. Incomplete baselines are refused."))
                Authorize(() => Run(() => FolderIntegrityStore.Accept(selected, subfolders, excluded)));
        });
        Add(integrity, "Compare saved folder", () => Run(FolderIntegrityStore.Check));
        Add(integrity, "Folder change history", () => Run(FolderIntegrityStore.History));
        try { var baseline = FolderIntegrityStore.Baseline(); if (baseline != null) { folder = baseline.Root; folderName.Text = folder; recursive.Checked = baseline.Recursive; exclusions.Lines = baseline.Exclusions; } }
        catch { folderName.Text = "Saved baseline could not be read. It has not been replaced."; }

        var downloads = Page(tabs, "Downloads and updates", "Inspect a file's Windows publisher signature. Valid signatures are not malware verdicts. Update staging additionally requires a trusted SHA-256, a configured publisher certificate pin, and a newer FishTank version. Current unsigned development builds cannot pass signed-update verification. Only the separate confirmed GitHub installation action replaces and restarts the app.");
        Explain(downloads, "GitHub updates: only public stable releases from KailanBess/FishTank. Checking and downloading require consent; installation is a separate confirmed action. Signed desktop and CLI assets are required. Your data and existing runtime configuration are preserved.");
        Add(downloads, "Check GitHub for updates", () => { if (Confirm("Contact GitHub's API to check KailanBess/FishTank public stable releases? GitHub can see your IP address; no app credentials are sent.")) Run(GitHubUpdates.Check); });
        Add(downloads, "Download and verify GitHub update", () => { if (Confirm("Download both release executables from GitHub and verify their digests, publisher pin and versions? No installation occurs in this step.")) Run(GitHubUpdates.DownloadAndStage); });
        Add(downloads, "Apply verified GitHub update", () => {
            if (Confirm("Close FishTank, reverify and install both staged GitHub executables, retain rollback copies, and restart? Finish open work first. Your private data is not replaced. Installation needs write access to the app folder; no administrator elevation is requested automatically.")) Authorize(GitHubUpdateInstaller.Start);
        });
        Add(downloads, "Last GitHub installation result", () => SetReport(GitHubUpdateInstaller.LastResult()));
        Add(downloads, "Inspect publisher signature", () => { string file = PickFile(); if (file != null) Run(() => TrustedDownloads.Inspect(file)); });
        Add(downloads, "Check configured release feed", () => { if (Confirm("Contact the configured HTTPS release feed? Its host can see your IP address. No app credentials are sent. Advertised releases are not trusted until package verification succeeds.")) Run(VerifiedUpdateFeed.Check); });
        Add(downloads, "Download and verify advertised update", () => { if (Confirm("Contact the configured release host and download its advertised package (maximum 128 MiB)? FishTank verifies publisher, checksum and version before staging it. It will not execute or install the package.")) Run(VerifiedUpdateFeed.DownloadAndStage); });
        Explain(downloads, "Expected update SHA-256, obtained from the authenticated publisher:");
        var checksum = new TextBox { Width = 560, MaxLength = 66, AccessibleName = "Trusted update SHA-256 checksum" }; downloads.Controls.Add(checksum);
        Add(downloads, "Verify and stage update", () => {
            string expected = DownloadChecksum.Normalize(checksum.Text);
            string file = PickFile();
            if (file != null && Confirm("Verify and stage this FishTank update without running it? Verification requires a valid matching publisher signature, checksum and newer version.")) Run(() => TrustedDownloads.StageUpdate(file, expected));
        });
        var settings = Page(tabs, "Locking and reminders", "App locking requires an app password. The password vault keeps its independent five-minute lock. Reminders and comparisons run locally while FishTank is open; no scheduled service or online notification is created. Disabling app locking or changing publisher trust requires your app password and enrolled MFA.");
        SecurityPreferences preferences;
        try { preferences = SecurityPreferences.Load(); }
        catch { preferences = null; Explain(settings, "Saved preferences are unreadable. Saving is disabled so defaults cannot silently weaken protection."); }
        Explain(settings, "App inactivity timeout in minutes (0 disables idle locking):");
        var idle = new NumericUpDown { Minimum = 0, Maximum = 120, Value = preferences?.IdleMinutes ?? 5, Width = 100, AccessibleName = "App inactivity minutes" }; settings.Controls.Add(idle);
        var windowsLock = new CheckBox { Text = "Lock FishTank when Windows locks or disconnects", AutoSize = true, Checked = preferences?.LockWithWindows ?? true }; settings.Controls.Add(windowsLock);
        var reminders = new CheckBox { Text = "Show in-app security and backup reminders", AutoSize = true, Checked = preferences?.Reminders ?? false }; settings.Controls.Add(reminders);
        var desktopNotifications = new CheckBox { Text = "Also show Windows desktop notifications (no file names or secrets)", AutoSize = true, Checked = preferences?.DesktopNotifications ?? false }; settings.Controls.Add(desktopNotifications);
        var checkGitHub = new CheckBox { Text = "Check public GitHub releases once after startup (contacts GitHub)", AutoSize = true, Checked = preferences?.CheckGitHubOnStartup ?? false }; settings.Controls.Add(checkGitHub);
        Explain(settings, "Security-review age and reminder repeat interval (days). Backup copy age is configured separately:");
        var days = new NumericUpDown { Minimum = 1, Maximum = 90, Value = preferences?.ReminderDays ?? 7, Width = 100, AccessibleName = "Security reminder interval in days" }; settings.Controls.Add(days);
        var backupWarnings = new CheckBox { Text = "Include backup-age warnings in enabled reminders", AutoSize = true, Checked = preferences?.BackupAgeWarnings ?? true }; settings.Controls.Add(backupWarnings);
        Explain(settings, "Warn when a recorded app-created backup copy reaches this age (days). Verifying an old copy does not make it fresh:");
        var backupDays = new NumericUpDown { Minimum = 1, Maximum = 365, Value = preferences?.BackupAgeDays ?? 7, Width = 100, AccessibleName = "Maximum recorded backup copy age in days" }; settings.Controls.Add(backupDays);
        var diskWarnings = new CheckBox { Text = "Check disk health automatically while FishTank is open (read only)", AutoSize = true, Checked = preferences?.DiskHealthWarnings ?? true }; settings.Controls.Add(diskWarnings);
        Explain(settings, "Disk-check interval. Hardware may hide telemetry; unsupported checks stay unknown:");
        var diskInterval = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240, AccessibleName = "Automatic disk health check interval" };
        diskInterval.Items.AddRange(new object[] { "Hourly", "Daily", "Weekly" }); diskInterval.SelectedIndex = Array.IndexOf(new[] { 1, 24, 168 }, preferences?.DiskCheckHours ?? 24); settings.Controls.Add(diskInterval);
        Explain(settings, "Folder comparison schedule (while app is open):");
        var schedule = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240, AccessibleName = "Folder integrity comparison schedule" };
        schedule.Items.AddRange(new object[] { "Off", "Hourly", "Daily", "Weekly" });
        schedule.SelectedIndex = Array.IndexOf(new[] { 0, 1, 24, 168 }, preferences?.FolderCheckHours ?? 0); settings.Controls.Add(schedule);
        Explain(settings, "Trusted publisher certificate SHA-256 (leave blank until you have an authenticated publisher pin):");
        var pin = new TextBox { Width = 560, MaxLength = 66, Text = preferences?.PublisherCertificateSha256 ?? "", AccessibleName = "Trusted publisher certificate SHA-256" }; settings.Controls.Add(pin);
        Explain(settings, "Optional direct HTTPS release-manifest URL (same host as its package; no redirects). Leave blank until a signed release channel exists:");
        var feed = new TextBox { Width = 560, MaxLength = 2048, Text = preferences?.UpdateFeedUrl ?? "", AccessibleName = "Trusted release manifest HTTPS URL" }; settings.Controls.Add(feed);
        Add(settings, "Save security preferences", () => {
            if (preferences == null) throw new InvalidOperationException("Repair or restore security preferences before changing them.");
            string publisher = pin.Text.Trim(); if (publisher != "") publisher = DownloadChecksum.Normalize(publisher);
            string feedUrl = feed.Text.Trim(); if (feedUrl != "") VerifiedUpdateFeed.RequireHttps(feedUrl);
            Action save = () => {
                SecurityPreferences.Update(p => { p.IdleMinutes = (int)idle.Value; p.LockWithWindows = windowsLock.Checked; p.Reminders = reminders.Checked; p.DesktopNotifications = desktopNotifications.Checked; p.CheckGitHubOnStartup = checkGitHub.Checked; p.ReminderDays = (int)days.Value; p.FolderCheckHours = new[] { 0, 1, 24, 168 }[schedule.SelectedIndex]; p.PublisherCertificateSha256 = publisher; p.UpdateFeedUrl = feedUrl; p.BackupAgeDays = (int)backupDays.Value; p.BackupAgeWarnings = backupWarnings.Checked; p.DiskHealthWarnings = diskWarnings.Checked; p.DiskCheckHours = new[] { 1, 24, 168 }[diskInterval.SelectedIndex]; });
                SetReport("Security preferences saved. App locking works only when an app password is configured. Schedules run only while FishTank is open.");
                RefreshBackupAge();
            };
            bool changedTrust = publisher != SecurityPreferences.Load().PublisherCertificateSha256 || feedUrl != SecurityPreferences.Load().UpdateFeedUrl;
            if (changedTrust && !AppAccess.Enabled) throw new InvalidOperationException("Set an app password before configuring trusted update publishers.");
            if (AppAccess.Enabled) Authorize(save); else save();
        });

        var backups = Page(tabs, "Backup and recovery", "Test an encrypted FishTank password-vault backup using its master passphrase. Verification decrypts it in memory. The restore drill uses a temporary isolated folder and does not replace your active vault. This does not back up Windows or certify disk recovery.");
        backupStatus = Explain(backups, "Loading recorded backup ages…");
        RefreshBackupAge();
        Add(backups, "Show backup age and verification dates", () => { RefreshBackupAge(); SetReport(BackupHistory.Summary()); });
        Add(backups, "Verify encrypted backup", () => CheckBackup(false));
        Add(backups, "Test restore in isolation", () => CheckBackup(true));
        Add(backups, "Open Windows backup settings", () => OpenSettings("ms-settings:backup"));
        Add(backups, "Open recovery settings", () => OpenSettings("ms-settings:recovery"));
        Explain(backups, "Keep the vault master passphrase and Windows/BitLocker recovery information separately from the backup. An old backup may restore old passwords; verify again after major changes.");

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        details = new CheckBox { Text = "Show full evidence (Advanced)", AutoSize = true, Checked = advanced };
        details.CheckedChanged += delegate { RefreshReport(); }; toolbar.Controls.Add(details);
        var export = new Button { Text = "Export displayed report", AutoSize = true }; export.Click += delegate { try { Export(); } catch (Exception ex) { SetReport("Export failed: " + ex.Message); } }; toolbar.Controls.Add(export);
        layout.Controls.Add(toolbar, 0, 1);
        output = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = true, BackColor = colors.Surface, ForeColor = colors.Text, AccessibleName = "Security review findings" };
        layout.Controls.Add(output, 0, 2);
        state = new Label { AutoSize = true, Dock = DockStyle.Fill, Text = "Ready · Ctrl+Shift+L locks the app when an app password is set." }; layout.Controls.Add(state, 0, 3);
        Controls.Add(layout); RefreshReport();
        FormClosing += delegate(object sender, FormClosingEventArgs e) { if (working) { e.Cancel = true; state.Text = "Wait for the current bounded check to finish before closing."; } };
    }
    private FlowLayoutPanel Page(TabControl tabs, string title, string explanation)
    {
        var tab = new TabPage(title) { BackColor = colors.Background, ForeColor = colors.Text };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(10) };
        flow.SizeChanged += delegate {
            int width = Math.Max(200, flow.ClientSize.Width - 50);
            foreach (Control control in flow.Controls)
            {
                if (control is Label || control is CheckBox || control is Button) control.MaximumSize = new Size(width, 0);
                if (control is TextBox) control.Width = Math.Min(560, width);
            }
        };
        tab.Controls.Add(flow); tabs.TabPages.Add(tab); Explain(flow, explanation); return flow;
    }
    private Label Explain(FlowLayoutPanel panel, string value)
    {
        var label = new Label { Text = value, AutoSize = true, MaximumSize = new Size(650, 0), Margin = new Padding(0, 3, 0, 9), UseMnemonic = false };
        panel.Controls.Add(label); return label;
    }
    private void Add(FlowLayoutPanel panel, string label, Action action)
    {
        var button = new Button { Text = label, AutoSize = true, Padding = new Padding(8, 4, 8, 4), BackColor = colors.Surface2, ForeColor = colors.Text, FlatStyle = FlatStyle.Flat, AccessibleName = label };
        button.Click += delegate { try { if (!working) action(); } catch (Exception ex) { SetReport("Action did not complete: " + ex.Message); } };
        panel.Controls.Add(button); buttons.Add(button);
    }
    private void OpenSettings(string uri) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true });
    private bool Confirm(string text) => ExplainedMessageBox.Show(this, text, "Confirm security action", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
    private void Authorize(Action action)
    {
        if (!AppAccess.Enabled) { action(); return; }
        using (var credentials = MfaCredentialsDialog.Prompt(this, AppAccess.Mfa.Configured))
            if (credentials != null) AppAccess.ChangeSecurityConfiguration(credentials.Password, credentials.Factor, action);
    }
    private void ChooseFolder()
    {
        using (var picker = new FolderBrowserDialog()) if (picker.ShowDialog(this) == DialogResult.OK) { folder = picker.SelectedPath; folderName.Text = folder; }
    }
    private string PickFile()
    {
        using (var picker = new OpenFileDialog { CheckFileExists = true }) return picker.ShowDialog(this) == DialogResult.OK ? picker.FileName : null;
    }
    private async void Run(Func<string> action)
    {
        if (working) return;
        working = true; buttons.ForEach(b => b.Enabled = false); state.Text = "Working · no security verdict until checks finish.";
        try { string result = await Task.Run(action); if (!IsDisposed) { SetReport(result); RefreshBackupAge(); } }
        catch (Exception ex) { if (!IsDisposed) SetReport("Check did not complete. Status remains unknown.\n" + ex.Message); }
        finally { working = false; if (!IsDisposed) { buttons.ForEach(b => b.Enabled = true); state.Text = "Finished · review findings and coverage gaps."; } }
    }
    private void SetReport(string value) { if (IsDisposed) return; report = value; RefreshReport(); }
    private void RefreshBackupAge()
    {
        if (IsDisposed || backupStatus == null) return;
        try { backupStatus.Text = string.Join("\n", BackupHistory.Status().Select(x => x.Name + ": " + x.State)) + "\nUse Show backup age and verification dates for recorded copy dates and limits."; }
        catch { backupStatus.Text = "Backup age is unknown because tracking data could not be read. Existing files have not been changed."; }
    }
    private void RefreshReport() { if (output != null) output.Text = details.Checked ? report : Summarize(report); }
    internal static string Summarize(string value)
    {
        try
        {
            var rows = new List<Dictionary<string, object>>();
            Action<object> visit = null;
            visit = item => {
                if (item is Dictionary<string, object> map) { if (map.ContainsKey("Name") && map.ContainsKey("State")) rows.Add(map); else foreach (var child in map.Values) visit(child); }
                else if (item is IEnumerable sequence && !(item is string)) foreach (var child in sequence) visit(child);
            };
            object parsed = Core.Parse<object>(value);
            if (parsed is Dictionary<string, object> root && root.TryGetValue("signature", out object proof) && proof is Dictionary<string, object> signature)
            {
                string status = Convert.ToString(signature["Status"]);
                return "Publisher signature: " + status + ".\r\n" + (status == "Valid"
                    ? "Windows accepted this signature under its current trust policy. This does not prove the file is harmless."
                    : "The publisher signature was not accepted. Do not treat this file as a verified update.")
                    + "\r\nPublisher: " + signature["Subject"] + "\r\nShow full evidence for certificate details.";
            }
            visit(parsed);
            if (rows.Count == 0) return parsed is object[] array && array.Length == 0 ? "No saved observations yet. Run a review or folder comparison first." : value;
            var text = new StringBuilder("Review summary · this is evidence, not a guarantee of safety.\r\n");
            foreach (var group in rows.GroupBy(x => Convert.ToString(x["State"]))) text.AppendLine(group.Key + ": " + group.Count());
            foreach (var row in rows.Where(x => !new[] { "Unchanged", "No issue observed" }.Contains(Convert.ToString(x["State"]))).Take(30))
            {
                string state = Convert.ToString(row["State"]);
                string explanation = state == "Unknown" ? "This check was unavailable or incomplete. Its result is unknown; review full evidence for the error or coverage gap."
                    : state == "Changed" ? "This differs from the previous observation. It may be an expected update or an unwanted change; compare the evidence before acting."
                    : state == "Added" ? "This was not in the earlier observation. Review whether you expected it."
                    : state == "Removed" || state == "Missing" ? "This was recorded before and was not found in the completed comparison. Review whether removal was expected."
                    : state == "Review" ? Convert.ToString(row["Value"]).Split('\n').Last()
                    : "Inventory was collected locally. Show full evidence for the recorded settings and values.";
                text.AppendLine("\r\n" + row["Name"] + " — " + state + "\r\n" + explanation);
            }
            text.AppendLine("\r\nShow full evidence for all observations, before/after values and timestamps. Unknown or partial checks require follow-up.");
            return text.ToString();
        }
        catch { return value; }
    }
    private void Export()
    {
        if (!Confirm("Export the currently displayed report? It can include usernames, file paths, startup metadata and network settings. Keep it private.")) return;
        using (var picker = new SaveFileDialog { FileName = "FishTank-security-review.txt", Filter = "Text report|*.txt" })
            if (picker.ShowDialog(this) == DialogResult.OK) using (var file = new StreamWriter(new FileStream(picker.FileName, FileMode.CreateNew, FileAccess.Write, FileShare.None))) file.Write(output.Text);
    }
    private void CheckBackup(bool drill)
    {
        string backup = PickFile(); if (backup == null) return;
        string secret;
        using (var prompt = new MasterPrompt(false))
        {
            prompt.Text = "Backup master passphrase · validation only";
            if (prompt.ShowDialog(this) != DialogResult.OK) return;
            secret = prompt.Secret; prompt.Secret = null;
        }
        Run(() => {
            try
            {
                string fingerprint;
                using (var verified = PasswordSession.Open(backup, secret)) { fingerprint = verified.ContentSha256; }
                if (drill)
                {
                    string temp = Path.Combine(Core.Data, "restore-test-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(temp);
                    try { using (var restored = PasswordSession.RestoreEncrypted(backup, Path.Combine(temp, "test.ftvault"), secret)) { } }
                    finally { Directory.Delete(temp, true); }
                }
                try { BackupHistory.Verified(BackupHistory.PasswordVault, backup, fingerprint, drill); }
                catch { return "Backup validation succeeded, but recording its verification/age failed. Creation age remains unknown; no fresh backup is inferred."; }
                return drill ? "Backup authenticated, decrypted, restored to an isolated folder, and reopened successfully. Temporary files were removed; the active vault was not replaced." : "Backup authenticated and decrypted successfully. The active vault was not changed. A real restore drill is still recommended.";
            }
            finally { secret = null; }
        });
    }
}
