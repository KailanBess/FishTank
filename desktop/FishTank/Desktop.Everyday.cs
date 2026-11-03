using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace FishTank;
public sealed partial class Desktop
{
    private bool everydayWorking;
    private async Task EverydayWork(Func<Task> work)
    {
        if (HasRunningOperation) throw new InvalidOperationException("Finish the active operation first.");
        everydayWorking = true; try { await work(); } finally { everydayWorking = false; }
    }
    private void OpenSecurityChanges()
    {
        using (var dialog = UtilityDialog("Recorded security changes", out var panel))
        {
            Help(panel, "Compare two recorded checks of the same category. Unknown coverage stays unknown. A change needs review; it is not automatically a threat or an approved baseline.");
            var category = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Security check category" };
            category.Items.AddRange(SecurityReviews.Categories); category.SelectedIndex = 0;
            var before = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Earlier recorded check" };
            var after = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Later recorded check" };
            var list = new ListBox { Height = 150, AccessibleName = "Changed and unknown findings" };
            var detail = new TextBox { Multiline = true, ReadOnly = true, Height = 210, ScrollBars = ScrollBars.Vertical, AccessibleName = "Before and after evidence" };
            var snapshots = new List<EvidenceSnapshot>(); var findings = new List<EvidenceItem>();
            Action refresh = () => { snapshots = SecurityReviews.Recorded((string)category.SelectedItem); before.Items.Clear(); after.Items.Clear(); list.Items.Clear(); detail.Clear(); foreach (var snapshot in snapshots) { string title = snapshot.CapturedUtc.ToLocalTime().ToString("G"); before.Items.Add(title); after.Items.Add(title); } if (snapshots.Count > 1) { before.SelectedIndex = snapshots.Count - 2; after.SelectedIndex = snapshots.Count - 1; } else detail.Text = "Record two checks in this category before comparing them."; };
            category.SelectedIndexChanged += delegate { refresh(); };
            list.SelectedIndexChanged += delegate { if (list.SelectedIndex >= 0) detail.Text = findings[list.SelectedIndex].Value; };
            panel.Controls.Add(category); Help(panel, "Earlier check, then later check:"); panel.Controls.Add(before); panel.Controls.Add(after);
            panel.Controls.Add(UtilityButton("Compare selected checks", () => { if (before.SelectedIndex < 0 || after.SelectedIndex <= before.SelectedIndex) throw new InvalidOperationException("Choose an earlier and a later recorded check."); findings = EvidenceComparison.Compare(snapshots[before.SelectedIndex], snapshots[after.SelectedIndex]).Where(x => x.State != "Unchanged").ToList(); list.Items.Clear(); foreach (var item in findings) list.Items.Add(item.State + " · " + item.Name); detail.Text = findings.Count == 0 ? "No recorded differences. This does not prove safety or complete coverage." : "Select a finding for both observations."; if (findings.Count > 0) list.SelectedIndex = 0; }));
            panel.Controls.Add(list); panel.Controls.Add(detail);
            panel.Controls.Add(UtilityButton("Refresh recorded checks", refresh));
            panel.Controls.Add(UtilityButton("Capture a current read-only check", () => { string selected = (string)category.SelectedItem; dialog.Close(); StartWork(() => SecurityReviews.Run(selected, false), "Message"); }));
            refresh(); dialog.ShowDialog(this);
        }
    }
    private void OpenStorageCleanup()
    {
        using (var dialog = UtilityDialog("Storage cleanup preview", out var panel))
        {
            Help(panel, "Preview recognized FishTank download remnants and settings temporary files older than seven days in this profile. Vaults, backups, rollback copies, verified updates and arbitrary folders are excluded. Nothing is selected or removed automatically.");
            var list = new CheckedListBox { Height = 280, CheckOnClick = true, AccessibleName = "Old FishTank files to select for removal", HorizontalScrollbar = true };
            var summary = new Label { AutoSize = true, Text = "Choose Preview to inspect eligible files." }; var items = new List<CleanupCandidate>();
            panel.Controls.Add(list); panel.Controls.Add(summary);
            panel.Controls.Add(UtilityAsyncButton("Preview eligible files", async () => await EverydayWork(async () => { var found = await Task.Run(() => StorageCleanup.Preview(DateTime.UtcNow)); if (dialog.IsDisposed) return; items = found; list.Items.Clear(); foreach (var file in items) list.Items.Add(file.Name + " · " + file.Bytes + " bytes · " + file.ModifiedUtc.ToLocalTime().ToString("g"), false); summary.Text = items.Count + " eligible files · " + items.Sum(x => x.Bytes) + " bytes. Select only files you want removed."; })));
            panel.Controls.Add(UtilityAsyncButton("Remove selected previewed files", async () => await EverydayWork(async () => { var selected = list.CheckedIndices.Cast<int>().Select(i => items[i]).ToArray(); if (selected.Length == 0) return; if (MessageBox.Show(dialog, "Permanently remove " + selected.Length + " selected temporary/download files (" + selected.Sum(x => x.Bytes) + " bytes)?", "Confirm selective cleanup", MessageBoxButtons.YesNo) != DialogResult.Yes) return; int removed = 0; try { await Task.Run(() => { foreach (var item in selected) { StorageCleanup.Delete(item, DateTime.UtcNow); removed++; } }); } finally { if (!dialog.IsDisposed) { list.Items.Clear(); items.Clear(); summary.Text = removed + " selected files removed. Preview again before another cleanup."; } } })));
            dialog.FormClosing += delegate(object sender, FormClosingEventArgs e) { if (everydayWorking) e.Cancel = true; }; dialog.ShowDialog(this);
        }
    }
    private void OpenPortableTransfer()
    {
        using (var dialog = UtilityDialog("Portable profile export and import", out var panel))
        {
            Help(panel, PortableProfileTransfer.Scope);
            Help(panel, "The transfer file contains encrypted vault bytes and readable appearance settings. Its checksum detects accidental damage; the vault master passphrase authenticates the encrypted content when opened.");
            panel.Controls.Add(UtilityAsyncButton("Preview and export this profile", async () => await EverydayWork(async () => { var copy = appearance.Copy(); var bundle = await Task.Run(() => PortableProfileTransfer.Capture(copy)); if (dialog.IsDisposed) return; if (MessageBox.Show(dialog, PortableProfileTransfer.Scope + "\r\n\r\nEncrypted vault SHA-256: " + bundle.VaultSha256, "Export preview", MessageBoxButtons.YesNo) != DialogResult.Yes) return; using (var save = new SaveFileDialog { Filter = "FishTank portable profile|*.ftprofile", FileName = "FishTank-profile.ftprofile" }) if (save.ShowDialog(dialog) == DialogResult.OK) { string destination = save.FileName; await Task.Run(() => RestoreFileWriter.WriteNew(destination, Encoding.UTF8.GetBytes(Core.Json(bundle)))); if (!dialog.IsDisposed) MessageBox.Show(dialog, "Transfer file saved. Keep the master passphrase separately."); } })));
            Help(panel, "New destination profile name (1–32 letters, digits, underscores or hyphens):"); var name = new TextBox { MaxLength = 32, AccessibleName = "New imported profile name" }; panel.Controls.Add(name);
            panel.Controls.Add(UtilityAsyncButton("Preview and import into a new profile", async () => await EverydayWork(async () => { string profileName = name.Text, root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FishTank"); ProfilePaths.Resolve(root, profileName); using (var pick = new OpenFileDialog { Filter = "FishTank portable profile|*.ftprofile" }) if (pick.ShowDialog(dialog) == DialogResult.OK) { string source = pick.FileName; var bundle = await Task.Run(() => PortableProfileTransfer.Parse(LocalStoreFiles.ReadOptional(source, 24 * 1024 * 1024))); if (dialog.IsDisposed) return; if (MessageBox.Show(dialog, PortableProfileTransfer.Scope + "\r\n\r\nNew profile: " + profileName + "\r\nTheme: " + bundle.Appearance.Theme + "\r\nEncrypted vault SHA-256: " + bundle.VaultSha256, "Import preview", MessageBoxButtons.YesNo) != DialogResult.Yes) return; await Task.Run(() => PortableProfileTransfer.Import(bundle, root, profileName)); if (!dialog.IsDisposed) MessageBox.Show(dialog, "New profile imported. Open it from Profiles and layouts, configure app access and verify the vault with its master passphrase. The original profile is retained."); } })));
            panel.Controls.Add(UtilityButton("Open profiles and layouts", OpenProfilesAndLayouts)); dialog.FormClosing += delegate(object sender, FormClosingEventArgs e) { if (everydayWorking) e.Cancel = true; }; dialog.ShowDialog(this);
        }
    }
    private void OpenWhatsNew()
    {
        using (var dialog = UtilityDialog("What's new in FishTank", out var panel))
        {
            Help(panel, AppReleaseNotes.IsUnread() ? "New release notes for this profile." : "Release notes for the installed version.");
            var search = new TextBox { AccessibleName = "Search release notes" };
            var notes = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Height = 380, AccessibleName = "Installed release notes", Text = AppReleaseNotes.Text };
            search.TextChanged += delegate { notes.Text = string.IsNullOrWhiteSpace(search.Text) ? AppReleaseNotes.Text : string.Join("\r\n", AppReleaseNotes.Text.Split(new[] { "\r\n" }, StringSplitOptions.None).Where(line => line.IndexOf(search.Text, StringComparison.OrdinalIgnoreCase) >= 0)); };
            panel.Controls.Add(search); panel.Controls.Add(notes); panel.Controls.Add(UtilityButton("Mark these release notes read", () => { AppReleaseNotes.MarkRead(); dialog.Close(); })); dialog.ShowDialog(this);
        }
    }
    private void OpenDemo() => Process.Start(new ProcessStartInfo(Path.Combine(Core.Base, "FishTank.exe"), "--demo") { UseShellExecute = false });
}
