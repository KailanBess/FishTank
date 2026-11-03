using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FishTank;

public sealed partial class Desktop
{
    private void ScheduledScanFinished(string result)
    {
        lastScan = result;
        if (current == "Dashboard" || current == "Antivirus") RenderPage();
    }

    private void OpenToolsTab(string tab)
    {
        using (var tools = new SecurityToolsDialog(palette, appearance.AdvancedMode, appearance.TextScale / 100f, tab))
        { tools.Icon = Icon; tools.ShowDialog(this); }
    }

    private void AddAttentionOverview()
    {
        EvidenceSnapshot snapshot = null;
        var issues = new List<Tuple<string, Action>>();
        string updateRecovery = UpdateRecoveryStatus.Notice();
        if (updateRecovery != null) issues.Add(Tuple.Create(updateRecovery, (Action)(() => OpenToolsTab("Downloads and updates"))));
        try
        {
            snapshot = SecurityReviews.Latest("SecurityHealth");
            if (snapshot?.Items != null)
                foreach (var item in snapshot.Items.Where(x => x.State == "Review").Take(3))
                    issues.Add(Tuple.Create(item.Name + ": needs attention. Review the evidence in Protection health.", (Action)(() => OpenToolsTab("Security reviews"))));
            if (ProtectionOverview.Status(snapshot, DateTime.UtcNow) == "Couldn’t check")
                issues.Add(Tuple.Create("Protection evidence is missing, incomplete or older than 15 minutes. Run a read-only protection check.", (Action)RefreshProtectionOverview));
        }
        catch { issues.Add(Tuple.Create("Couldn’t read protection evidence. Run a new protection check.", (Action)RefreshProtectionOverview)); }
        try
        {
            int due = FolderBackupReminders.Load().Count(x => x.NeedsAttention(DateTime.UtcNow));
            if (due > 0) issues.Add(Tuple.Create(due + " important folder backup reminders need attention. Record a copy after making your backup.", (Action)OpenFolderReminders));
            if (BackupHistory.Alerts().Count > 0) issues.Add(Tuple.Create("Your FishTank backup age needs review. Create or verify a backup in Backup and recovery.", (Action)(() => OpenToolsTab("Backup and recovery"))));
        }
        catch { issues.Add(Tuple.Create("Couldn’t check backup records. Review Backup and recovery.", (Action)OpenFolderReminders)); }
        try
        {
            if (!AppAccess.Enabled) issues.Add(Tuple.Create("App access has no password. Set an app password in Settings.", (Action)(() => Navigate("Settings"))));
        }
        catch { issues.Add(Tuple.Create("Couldn’t check app access settings. Review Settings.", (Action)(() => Navigate("Settings")))); }
        string state = ProtectionOverview.Status(snapshot, DateTime.UtcNow);
        var top = issues.Take(3).ToArray();
        InsightNote("Protection · " + state,
            "What needs attention?\n" + (top.Length == 0 ? "No issues found in the available checks."
            : string.Join("\n\n", top.Select((x, i) => (i + 1) + ". " + x.Item1)))
            + "\n\n" + (snapshot == null ? "No saved protection check." : "Last protection check: " + snapshot.CapturedUtc.ToLocalTime().ToString("g") + ".")
            + " Protected means the listed Windows checks reported no issue within 15 minutes; it is not a malware-free verdict.");
        var actions = top.Select((issue, i) => (Control)Button("Review item " + (i + 1), issue.Item2, false)).ToList();
        actions.Add(Button("Refresh protection status", RefreshProtectionOverview, true));
        ActionRow(actions.ToArray());
        ActionRow(Button("Activity timeline", OpenActivityTimeline, false), Button("Recovery center", OpenRecoveryCenter, false), Button("Preview support report", OpenSupportReport, false));
    }

    private void InsightNote(string title, string detail)
    {
        var card = Card(160);
        var heading = Label(title, 12f, palette.Text); heading.Font = Face(12f, FontStyle.Bold); heading.Dock = DockStyle.Top;
        var description = Label(detail, 10f, palette.Muted); description.Dock = DockStyle.Top;
        card.Controls.Add(description); card.Controls.Add(heading);
        card.SizeChanged += delegate {
            int width = Math.Max(100, card.ClientSize.Width - card.Padding.Horizontal);
            heading.Height = TextRenderer.MeasureText(heading.Text, heading.Font, new Size(width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height + 12;
            description.Height = TextRenderer.MeasureText(description.Text, description.Font, new Size(width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height + 8;
            int height = heading.Height + description.Height + card.Padding.Vertical;
            if (card.Height != height) card.Height = height;
        };
        AddBlock(card, 160);
    }

    private void RefreshProtectionOverview() => StartWork(() => SecurityReviews.Run("SecurityHealth", false), "Message");

    private void AddScanScheduleSummary()
    {
        try
        {
            var schedule = ScanScheduleStore.Load();
            InsightNote("Scheduled scans", (schedule.Enabled ? schedule.Kind + " scan · " + (schedule.IntervalDays == 1 ? "Daily" : "Weekly")
                + " · Next due: " + schedule.NextRunUtc.ToLocalTime().ToString("g") : "Automatic scans are off.")
                + "\n" + schedule.LastResult + "\nRuns while FishTank is open; a missed occurrence runs once when available. Defender’s existing remediation policy applies.");
        }
        catch { Note("Scan schedule · couldn’t check", "Saved scheduling data could not be read. It has not been replaced."); }
        ActionRow(Button("Configure scan schedule", OpenScanSchedule, false), Button("View scan progress", OpenCurrentScan, false), Button("Activity timeline", OpenActivityTimeline, false));
    }

    private void AddAppSettings()
    {
        ActionRow(Button("All tools · Ctrl+K", OpenToolsHub, true), Button("Search settings", OpenSettingsSearch, false));
        InsightNote("Appearance and app access", "Choose readable colors and text, or manage your app password, authenticator and locking preferences.");
        ActionRow(Button("Appearance & accessibility", Customize, false), Button("App access & MFA", OpenAccessSettings, false), Button("Locking & notifications", () => OpenToolsTab("Locking and reminders"), false));
        InsightNote("Automation and connections", "Choose scheduled scans, track backup reminders, or manage the local browser dashboard.");
        ActionRow(Button("Scan scheduling", OpenScanSchedule, false), Button("Backup reminders", OpenFolderReminders, false), Button("Browser dashboard", OpenBrowserControls, false));
        ActionRow(Button("More tools", OpenMoreTools, false), Button("Recovery center", OpenRecoveryCenter, false));
    }

    private Form UtilityDialog(string title, out FlowLayoutPanel content)
    {
        var dialog = new Form { Text = "FishTank · " + title, StartPosition = FormStartPosition.CenterParent,
            Size = new Size(820, 650), MinimumSize = new Size(620, 480), AutoScaleMode = AutoScaleMode.Dpi,
            BackColor = palette.Background, ForeColor = palette.Text, Font = Face(10, FontStyle.Regular), Icon = Icon };
        content = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Padding = new Padding(18) };
        var panel = content;
        panel.SizeChanged += delegate { foreach (Control control in panel.Controls) { int width = Math.Max(200, panel.ClientSize.Width - 48); if (control is Label label) label.MaximumSize = new Size(width, 0); control.Width = width; } };
        dialog.Controls.Add(panel);
        return dialog;
    }
    private static void Help(FlowLayoutPanel panel, string text) => panel.Controls.Add(new Label { Text = text, AutoSize = true,
        MaximumSize = new Size(700, 0), Width = 700, Margin = new Padding(3, 8, 3, 10), UseMnemonic = false });
    private static Button UtilityButton(string title, Action action)
    {
        var button = new Button { Text = title, Height = 40, Width = 680, UseMnemonic = false };
        button.Click += delegate { try { action(); } catch (Exception ex) { MessageBox.Show(button.FindForm(), ex.Message, "Could not complete action", MessageBoxButtons.OK, MessageBoxIcon.Information); } };
        return button;
    }

    private void OpenScanSchedule()
    {
        try
        {
            var schedule = ScanScheduleStore.Load();
            using (var dialog = UtilityDialog("Scan schedule", out var panel))
            {
                Help(panel, "Run Defender automatically while FishTank is open, including tray mode. A missed scan runs once when the app is available. Scans wait for foreground app operations and dialogs to finish. Quick/full scans follow Defender’s existing policy, which may quarantine or remove detections. Saving an enabled schedule authorizes these scans.");
                var enabled = new CheckBox { Text = "Enable scheduled Defender scans", Checked = schedule.Enabled, AutoSize = true };
                var kind = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Scheduled scan type" };
                kind.Items.AddRange(new object[] { "Quick", "Full" }); kind.SelectedItem = schedule.Kind;
                var interval = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Scan frequency" };
                interval.Items.AddRange(new object[] { "Daily", "Weekly" }); interval.SelectedIndex = schedule.IntervalDays == 1 ? 0 : 1;
                var time = new DateTimePicker { Format = DateTimePickerFormat.Time, ShowUpDown = true, CustomFormat = "HH:mm", AccessibleName = "Scheduled scan local time", Value = DateTime.Today.AddMinutes(schedule.MinuteOfDay) };
                panel.Controls.Add(enabled); Help(panel, "Scan type and frequency:"); panel.Controls.Add(kind); panel.Controls.Add(interval);
                Help(panel, "Local time (weekly scans start at the next selected time, then repeat every seven days):"); panel.Controls.Add(time);
                Help(panel, "Last attempt: " + (schedule.LastAttemptUtc == DateTime.MinValue ? "None" : schedule.LastAttemptUtc.ToLocalTime().ToString("g")) + "\n" + schedule.LastResult);
                panel.Controls.Add(UtilityButton("Save schedule", () => {
                    var currentSchedule = ScanScheduleStore.Load();
                    currentSchedule.Enabled = enabled.Checked; currentSchedule.Kind = (string)kind.SelectedItem;
                    currentSchedule.IntervalDays = interval.SelectedIndex == 0 ? 1 : 7; currentSchedule.MinuteOfDay = time.Value.Hour * 60 + time.Value.Minute;
                    currentSchedule.NextRunUtc = enabled.Checked ? currentSchedule.NextAfter(DateTime.UtcNow, TimeZoneInfo.Local) : DateTime.MinValue;
                    ScanScheduleStore.Save(currentSchedule); AppActivity.Record("Scans", currentSchedule.Enabled ? "Automatic scan schedule enabled or changed" : "Automatic scan schedule disabled"); dialog.Close();
                }));
                dialog.ShowDialog(this);
            }
            RenderPage();
        }
        catch (Exception ex) { ShowMessage("Scan scheduling unavailable", ex.Message); }
    }

    private void OpenFolderReminders()
    {
        try
        {
            using (var dialog = UtilityDialog("Important folder backup reminders", out var panel))
            {
                Help(panel, "Track when you last copied important folders using your own backup tool. FishTank does not copy these folders or verify their backups. “Record backup made now” records your declaration only. Enable in-app reminders in Locking and reminders for periodic notices.");
                var list = new ListBox { Height = 180, AccessibleName = "Tracked backup folders", HorizontalScrollbar = true };
                var days = new NumericUpDown { Minimum = 1, Maximum = 365, Value = 7, AccessibleName = "New folder reminder interval in days" };
                var items = new List<FolderBackupReminder>();
                Action refresh = () => { items = FolderBackupReminders.Load(); list.Items.Clear(); foreach (var item in items) list.Items.Add(item.Path + " · " + item.Status(DateTime.UtcNow) + " · every " + item.Days + " days · last recorded: " + (item.LastCopiedUtc == DateTime.MinValue ? "never" : item.LastCopiedUtc.ToLocalTime().ToString("g"))); };
                panel.Controls.Add(list); Help(panel, "Remind me after this many days (for a new folder):"); panel.Controls.Add(days);
                panel.Controls.Add(UtilityButton("Add important folder", () => { using (var picker = new FolderBrowserDialog()) if (picker.ShowDialog(dialog) == DialogResult.OK) { FolderBackupReminders.Add(picker.SelectedPath, (int)days.Value); refresh(); } }));
                panel.Controls.Add(UtilityButton("Record backup made now", () => { if (list.SelectedIndex < 0) throw new InvalidOperationException("Select a folder first."); FolderBackupReminders.Update(items[list.SelectedIndex].Id, false); AppActivity.Record("Backups", "Important folder backup declared by user; contents not verified"); refresh(); }));
                panel.Controls.Add(UtilityButton("Remove selected reminder", () => { if (list.SelectedIndex < 0) throw new InvalidOperationException("Select a folder first."); FolderBackupReminders.Update(items[list.SelectedIndex].Id, true); refresh(); }));
                panel.Controls.Add(UtilityButton("Configure reminder notifications", () => OpenToolsTab("Locking and reminders")));
                refresh(); dialog.ShowDialog(this);
            }
            RenderPage();
        }
        catch (Exception ex) { ShowMessage("Folder reminders unavailable", ex.Message); }
    }

    private void OpenActivityTimeline()
    {
        using (var dialog = UtilityDialog("Activity timeline", out var panel))
        {
            Help(panel, "Recent scans, observed Defender detections, quarantine actions, backups and updates. Up to 1,000 entries from the latest 14 audit files and retained histories. Events while monitoring was off can be missing. Raw paths and event payloads are omitted.");
            var search = new TextBox { AccessibleName = "Search activity summaries" };
            var category = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Activity category" };
            category.Items.AddRange(new object[] { "All", "Scans", "Detections", "Quarantine", "Backups", "Updates", "App", "Coverage" }); category.SelectedIndex = 0;
            var grid = new DataGridView { Height = 320, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AccessibleName = "Recent activity" };
            grid.Columns.Add("time", "Local time"); grid.Columns.Add("category", "Category"); grid.Columns.Add("summary", "Activity");
            var entries = new List<ActivityEntry>();
            Action filter = () => { grid.Rows.Clear(); foreach (var item in entries.Where(x => (category.SelectedIndex == 0 || x.Category == (string)category.SelectedItem) && (x.Summary ?? "").IndexOf(search.Text, StringComparison.OrdinalIgnoreCase) >= 0)) grid.Rows.Add(item.TimeUtc.ToLocalTime().ToString("g"), item.Category, item.Summary); };
            Action refresh = async () => { try { var live = liveDetection?.Events(); var loaded = await Task.Run(() => ActivityTimeline.Read(live)); if (!dialog.IsDisposed) { entries = loaded; filter(); } } catch (Exception ex) { if (!dialog.IsDisposed) MessageBox.Show(dialog, "Activity could not be read: " + ex.Message); } };
            search.TextChanged += delegate { filter(); }; category.SelectedIndexChanged += delegate { filter(); };
            panel.Controls.Add(search); panel.Controls.Add(category); panel.Controls.Add(grid); panel.Controls.Add(UtilityButton("Refresh activity", refresh));
            refresh(); dialog.ShowDialog(this);
        }
    }

    private void OpenSupportReport()
    {
        using (var dialog = UtilityDialog("Preview and redact support report", out var panel))
        {
            Help(panel, "Review the report below and delete any text you do not want to share. Save writes exactly the edited preview to a local text file. Nothing is uploaded or sent.");
            var preview = new TextBox { Multiline = true, Height = 400, ScrollBars = ScrollBars.Both, WordWrap = false,
                Text = SupportReport.Build(), AccessibleName = "Editable support report preview" };
            panel.Controls.Add(preview);
            panel.Controls.Add(UtilityButton("Save edited report", () => {
                using (var picker = new SaveFileDialog { Filter = "Text report (*.txt)|*.txt", FileName = "FishTank-support-report.txt", OverwritePrompt = true })
                    if (picker.ShowDialog(dialog) == DialogResult.OK) { File.WriteAllText(picker.FileName, preview.Text, new UTF8Encoding(false)); MessageBox.Show(dialog, "Edited support report saved locally."); }
            }));
            dialog.ShowDialog(this);
        }
    }

    private void OpenSettingsSearch()
    {
        using (var dialog = UtilityDialog("Search settings", out var panel))
        {
            Help(panel, "Type a setting or feature name, select a result, and open its controls.");
            var query = new TextBox { AccessibleName = "Search settings" };
            var list = new ListBox { Height = 320, AccessibleName = "Matching settings" };
            var destinations = new[] {
                Tuple.Create("Appearance and accessibility · theme accent font text size spacing reduced motion", (Action)Customize),
                Tuple.Create("App password and sign-in · change remove lock", (Action)OpenAccessSettings),
                Tuple.Create("Multifactor authentication · MFA authenticator recovery codes email SMS", (Action)(() => { using (var mfa = new MfaSettingsDialog(palette)) mfa.ShowDialog(this); })),
                Tuple.Create("Scan scheduling · automatic daily weekly quick full antivirus", (Action)OpenScanSchedule),
                Tuple.Create("Important folder backup reminders · folders copy overdue", (Action)OpenFolderReminders),
                Tuple.Create("Locking and reminders · idle notifications security-review interval", (Action)(() => OpenToolsTab("Locking and reminders"))),
                Tuple.Create("Backup and recovery · encrypted vault verification restore", (Action)(() => OpenToolsTab("Backup and recovery"))),
                Tuple.Create("Downloads and updates · GitHub publisher signature checksum", (Action)(() => OpenToolsTab("Downloads and updates"))),
                Tuple.Create("Folder integrity · baseline exclusions scheduled comparisons", (Action)(() => OpenToolsTab("Folder integrity"))),
                Tuple.Create("Protection health · firewall disk startup privacy permissions", (Action)(() => OpenToolsTab("Security reviews"))),
                Tuple.Create("Local browser dashboard · pairing session browser access", (Action)OpenBrowserControls),
                Tuple.Create("Live monitoring · Defender detections background alerts tray", (Action)(() => Navigate("Real-time monitoring"))),
                Tuple.Create("Activity timeline · scans quarantine backups updates history", (Action)OpenActivityTimeline),
                Tuple.Create("First-run setup · onboarding password protection reminders", (Action)OpenInitialSetup),
                Tuple.Create("Recovery center · quarantine restore backup passphrase", (Action)OpenRecoveryCenter),
                Tuple.Create("Accessibility keyboard shortcuts · high contrast screen reader focus", (Action)OpenAccessibilityHelp),
                Tuple.Create("Signed release readiness · certificate publisher signing install", (Action)OpenReleaseReadiness),
                Tuple.Create("Support report · export preview redact diagnostics", (Action)OpenSupportReport)
            };
            var matches = destinations.ToList();
            Action filter = () => { matches = destinations.Where(x => query.Text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).All(word => x.Item1.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)).ToList(); list.Items.Clear(); foreach (var item in matches) list.Items.Add(item.Item1); if (list.Items.Count > 0) list.SelectedIndex = 0; };
            Action open = () => { if (list.SelectedIndex < 0) return; var action = matches[list.SelectedIndex].Item2; dialog.Close(); action(); };
            query.TextChanged += delegate { filter(); }; list.DoubleClick += delegate { open(); };
            panel.Controls.Add(query); panel.Controls.Add(list); panel.Controls.Add(UtilityButton("Open selected setting", open)); filter();
            dialog.ShowDialog(this);
        }
    }
}
