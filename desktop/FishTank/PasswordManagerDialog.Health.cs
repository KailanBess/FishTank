using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
namespace FishTank;
public sealed partial class PasswordManagerDialog
{
    private void ShowVaultHealth()
    {
        NeedUnlock();
        using (var dialog = new Form { Text = "Vault health dashboard", Width = 800, Height = 580, StartPosition = FormStartPosition.CenterParent, Font = Font, BackColor = colors.Background, ForeColor = colors.Text })
        {
            var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(18) }; dialog.Controls.Add(panel);
            var note = new Label { AutoSize = true, MaximumSize = new Size(730, 0), Text = "Local review of expired, soon-expiring, reused, short and repetitive passwords. This is a heuristic review, not a breach check or proof of password strength. Passwords and previous-password values are never displayed here." };
            var filter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Vault health finding filter", Width = 730 }; filter.Items.AddRange(new object[] { "All findings", "Expired", "Expires within 14 days", "Reused", "Short password", "Repeated short password pattern" }); filter.SelectedIndex = 0;
            var list = new ListBox { Height = 290, Width = 730, AccessibleName = "Vault entries needing review", HorizontalScrollbar = true };
            var summary = new Label { AutoSize = true };
            Action refresh = () => { NeedUnlock(); var findings = VaultHealth.Assess(session.Snapshot(), DateTime.UtcNow); string query = filter.SelectedIndex == 0 ? "" : (string)filter.SelectedItem; var shown = findings.Where(x => x.Finding.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToArray(); list.Items.Clear(); foreach (var item in shown) list.Items.Add(item.Title + " · " + item.Finding); summary.Text = findings.Count + " entries need local review; " + shown.Length + " shown. A clear list is not a safety verdict."; };
            var button = new Button { Text = "Refresh vault health", Width = 730, Height = 38 }; button.Click += delegate { try { refresh(); } catch (Exception ex) { MessageBox.Show(dialog, ex.Message); } }; filter.SelectedIndexChanged += delegate { if (session?.IsUnlocked == true) refresh(); };
            panel.Controls.Add(note); panel.Controls.Add(filter); panel.Controls.Add(summary); panel.Controls.Add(list); panel.Controls.Add(button);
            panel.SizeChanged += delegate { foreach (Control control in panel.Controls) { control.Width = Math.Max(200, panel.ClientSize.Width - 48); if (control is Label label) label.MaximumSize = new Size(control.Width, 0); } };
            secretDialog = dialog; WireActivity(dialog);
            try { refresh(); dialog.ShowDialog(this); } finally { secretDialog = null; list.Items.Clear(); }
        }
    }
}
