using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
namespace FishTank;
public sealed partial class PasswordManagerDialog
{
    private void EntryExtras()
    {
        var entry = Selected();
        using (var dialog = new Form { Text = "Entry history, expiry and encrypted attachments", Width = 740, Height = 610, StartPosition = FormStartPosition.CenterParent })
        {
            var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
            dialog.Controls.Add(panel);
            var history = new ListBox { Width = 690, Height = 110, AccessibleName = "Previous password dates" };
            foreach (var revision in entry.History) history.Items.Add(revision.ChangedUtc);
            var attachments = new ListBox { Width = 690, Height = 100, AccessibleName = "Encrypted attachments" };
            Action refresh = () => { attachments.Items.Clear(); foreach (var a in entry.Attachments) attachments.Items.Add(a.Name); };
            var expiry = new DateTimePicker { ShowCheckBox = true, Checked = DateTime.TryParse(entry.ExpiresUtc, out _), AccessibleName = "Optional password expiry" };
            if (DateTime.TryParse(entry.ExpiresUtc, out var date)) expiry.Value = date.ToLocalTime();
            Action<string, Action> button = (title, action) => { var b = new Button { Text = title, Width = 690, Height = 36 }; b.Click += delegate { try { NeedUnlock(); action(); } catch (Exception ex) { MessageBox.Show(dialog, ex.Message); } }; panel.Controls.Add(b); };
            panel.Controls.Add(new Label { Text = "Up to five previous passwords; attachment limit: five files, 256 KiB each.", AutoSize = true });
            panel.Controls.Add(history);
            button("Copy selected previous password for 30 seconds", () => { if (history.SelectedIndex < 0) return; clipboard.Copy(entry.History[history.SelectedIndex].Password); });
            panel.Controls.Add(expiry);
            button("Save expiry", () => { var current = session.Entries.Single(x => x.Id == entry.Id); current.ExpiresUtc = expiry.Checked ? expiry.Value.ToUniversalTime().ToString("o") : ""; session.Put(current); RefreshEntries(); });
            panel.Controls.Add(attachments);
            button("Attach a file inside the encrypted vault", () => { using (var pick = new OpenFileDialog()) if (pick.ShowDialog(dialog) == DialogResult.OK) { var file = new FileInfo(pick.FileName); if (file.Length > 256 * 1024) throw new IOException("Attachment exceeds 256 KiB."); var bytes = LocalStoreFiles.ReadOptional(file.FullName, 256 * 1024) ?? throw new IOException("Attachment disappeared."); try { var next = entry.Attachments.Select(x => new VaultAttachment { Id = x.Id, Name = x.Name, Content = x.Content }).ToList(); next.Add(new VaultAttachment { Name = file.Name, Content = Convert.ToBase64String(bytes) }); session.SetAttachments(entry.Id, next); entry = session.Entries.Single(x => x.Id == entry.Id); refresh(); } finally { Array.Clear(bytes, 0, bytes.Length); } } });
            button("Export selected attachment (decrypted file)", () => { if (attachments.SelectedIndex < 0) return; var a = entry.Attachments[attachments.SelectedIndex]; using (var save = new SaveFileDialog { FileName = a.Name, Title = "Save decrypted attachment" }) if (save.ShowDialog(dialog) == DialogResult.OK) { var bytes = Convert.FromBase64String(a.Content); try { RestoreFileWriter.WriteNew(save.FileName, bytes); } finally { Array.Clear(bytes, 0, bytes.Length); } } });
            button("Remove selected attachment", () => { if (attachments.SelectedIndex < 0) return; var next = entry.Attachments.ToList(); next.RemoveAt(attachments.SelectedIndex); session.SetAttachments(entry.Id, next); entry = session.Entries.Single(x => x.Id == entry.Id); refresh(); });
            refresh(); secretDialog = dialog; WireActivity(dialog);
            try { dialog.ShowDialog(this); } finally { secretDialog = null; foreach (var h in entry.History) h.Password = ""; foreach (var a in entry.Attachments) a.Content = ""; }
        }
    }
    private void TransferEntries()
    {
        NeedUnlock();
        var choice = MessageBox.Show(this, "Yes: preview and import JSON/CSV.\r\nNo: preview and export plaintext JSON, including history and attachments.\r\nCancel: return.", "Vault transfer", MessageBoxButtons.YesNoCancel);
        if (choice == DialogResult.Cancel) return;
        if (choice == DialogResult.Yes)
        {
            using (var pick = new OpenFileDialog { Filter = "Vault JSON or CSV|*.json;*.csv" })
            if (pick.ShowDialog(this) == DialogResult.OK)
            {
                var bytes = LocalStoreFiles.ReadOptional(pick.FileName, 16 * 1024 * 1024) ?? throw new IOException("Import disappeared.");
                try { var imported = VaultTransfer.Parse(Encoding.UTF8.GetString(bytes), Path.GetExtension(pick.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase));
                    string preview = string.Join("\r\n", imported.Take(30).Select(x => x.Title + " · " + x.Kind));
                    if (MessageBox.Show(this, imported.Count + " entries will be added with new IDs. Existing entries remain.\r\n" + preview, "Import preview (passwords hidden)", MessageBoxButtons.YesNo) == DialogResult.Yes) { NeedUnlock(); session.ImportEntries(imported); RefreshEntries(); }
                } finally { Array.Clear(bytes, 0, bytes.Length); }
            }
        }
        else
        {
            var entries = session.Entries;
            if (MessageBox.Show(this, "Export " + entries.Count + " entries, including passwords, previous passwords and attachments, as readable JSON? The destination will contain unencrypted secrets.", "Plaintext export preview", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            using (var save = new SaveFileDialog { Filter = "Plaintext JSON|*.json", FileName = "FishTank-plaintext-vault.json" })
            if (save.ShowDialog(this) == DialogResult.OK) { NeedUnlock(); byte[] bytes = Encoding.UTF8.GetBytes(Core.Json(new PasswordPayload { Entries = entries.ToList() })); try { RestoreFileWriter.WriteNew(save.FileName, bytes); } finally { Array.Clear(bytes, 0, bytes.Length); } }
        }
    }
}
