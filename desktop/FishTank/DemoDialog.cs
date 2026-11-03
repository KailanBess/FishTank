using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
namespace FishTank;
internal static class DemoSamples
{
    internal static PasswordEntry[] Entries(DateTime now) => new[] {
        new PasswordEntry { Title = "Sample mail account", Password = "demo-only", ExpiresUtc = now.AddDays(-1).ToString("o") },
        new PasswordEntry { Title = "Sample shopping account", Password = "demo-only", ExpiresUtc = now.AddDays(7).ToString("o") },
        new PasswordEntry { Title = "Sample long unique password", Password = "Sample-only unique passphrase 2026!" }
    };
    internal static string Simulate(string action)
    {
        switch (action)
        {
            case "scan": return "SIMULATED scan completed: one sample finding needs review. No Windows scanner was started.";
            case "backup": return "SIMULATED backup failed: the example USB destination is disconnected. Reconnect it and review the last successful copy time.";
            case "restore": return "SIMULATED restore drill: a sample encrypted backup was selected, authenticated and reopened. No real files were read or restored.";
            default: throw new ArgumentException("Unknown demonstration action.");
        }
    }
}
internal sealed class DemoDialog : Form
{
    internal DemoDialog()
    {
        Text = "FishTank · SAMPLE demonstration"; Width = 860; Height = 670; MinimumSize = new Size(600, 480); StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 11); BackColor = Color.FromArgb(19, 23, 33); ForeColor = Color.White;
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(20) };
        Controls.Add(panel);
        panel.SizeChanged += delegate { foreach (Control control in panel.Controls) { control.Width = Math.Max(200, panel.ClientSize.Width - 50); if (control is Label label) label.MaximumSize = new Size(control.Width, 0); } };
        panel.Controls.Add(new Label { Text = "SAMPLE DATA · Isolated demonstration", Font = new Font(Font, FontStyle.Bold), AutoSize = true });
        panel.Controls.Add(new Label { Text = "This process uses in-memory examples. It opens no real profile or vault, starts no collectors or background tasks, and makes no server requests. Close this window to finish.", AutoSize = true, MaximumSize = new Size(760, 0) });
        var list = new ListBox { Height = 170, AccessibleName = "Sample vault health findings", HorizontalScrollbar = true };
        foreach (var finding in VaultHealth.Assess(DemoSamples.Entries(DateTime.UtcNow), DateTime.UtcNow)) list.Items.Add(finding.Title + " · " + finding.Finding);
        panel.Controls.Add(list);
        var result = new TextBox { Multiline = true, ReadOnly = true, Height = 160, ScrollBars = ScrollBars.Vertical, AccessibleName = "Simulated action result", Text = "Choose a sample action. Every result is explicitly simulated." };
        foreach (var item in new[] { Tuple.Create("Simulate a scan result", "scan"), Tuple.Create("Simulate a failed backup", "backup"), Tuple.Create("Simulate a restore drill", "restore") })
        {
            string action = item.Item2; var button = new Button { Text = item.Item1, Height = 42, Width = 760 };
            button.Click += delegate { result.Text = DemoSamples.Simulate(action); }; panel.Controls.Add(button);
        }
        panel.Controls.Add(result);
    }
}
