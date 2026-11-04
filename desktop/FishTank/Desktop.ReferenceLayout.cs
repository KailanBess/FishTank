using System;
using System.Drawing;
using System.Windows.Forms;

namespace FishTank;

public sealed partial class Desktop
{
    private void AddReferenceSecurityOverview()
    {
        var layout = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, BackColor = palette.Background, Padding = new Padding(0), Margin = new Padding(0) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
        var hero = Card(250); hero.Dock = DockStyle.Fill; hero.Margin = new Padding(0, 0, 12, 0);
        var content = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = palette.Surface };
        var eyebrow = Label("PROTECTION OVERVIEW", 9f, palette.Accent); eyebrow.AutoSize = true;
        var title = Label(health == null ? "Ready when you are." : "Your latest checks.", 25f, palette.Text); title.Font = Face(25f, FontStyle.Bold); title.AutoSize = true;
        var detail = Label(health == null ? "Review Windows protection and device health. No assessment has run in this session." : "Review the collected evidence below. Unavailable checks remain unknown.", 11f, palette.Muted); detail.AutoSize = true;
        var inspect = Button(health == null ? "Check this device" : "Refresh checks", () => StartWork(() => Core.Collect("Health"), "Health"), true);
        inspect.AutoSize = true; inspect.Margin = new Padding(0, 18, 0, 0);
        content.Controls.Add(eyebrow); content.Controls.Add(title); content.Controls.Add(detail); content.Controls.Add(inspect);
        var schedule = Button("Scan schedule", OpenScanSchedule, false); schedule.AutoSize = true; content.Controls.Add(schedule);
        var evidence = Button("Finding details", () => OpenToolsTab("Security reviews"), false); evidence.AutoSize = true; content.Controls.Add(evidence);
        content.SizeChanged += delegate { var width = Math.Max(160, content.ClientSize.Width - 20); title.MaximumSize = new Size(width, 0); detail.MaximumSize = new Size(width, 0); };
        hero.Controls.Add(content); layout.Controls.Add(hero, 0, 0);
        var side = Card(250); side.Dock = DockStyle.Fill; side.Margin = new Padding(0);
        var metrics = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, BackColor = palette.Surface };
        metrics.RowStyles.Add(new RowStyle(SizeType.Percent, 27)); metrics.RowStyles.Add(new RowStyle(SizeType.Percent, 27)); metrics.RowStyles.Add(new RowStyle(SizeType.Percent, 27)); metrics.RowStyles.Add(new RowStyle(SizeType.Percent, 19));
        Label Metric(string heading, string value) { var label = Label(heading + "\n" + value, 11f, palette.Text); label.Dock = DockStyle.Fill; return label; }
        metrics.Controls.Add(Metric("DEVICE CHECKS", health == null ? "Not assessed" : health.Count + " collected"), 0, 0);
        metrics.Controls.Add(Metric("LIVE DEFENDER ALERTS", liveDetection != null && liveDetection.Enabled ? "Listening" : "Off"), 0, 1);
        metrics.Controls.Add(Metric("LATEST SCAN", string.IsNullOrEmpty(lastScan) ? "Not run" : lastScan), 0, 2);
        var review = Button("Security center", () => Navigate("Security Center"), false); review.Dock = DockStyle.Fill; metrics.Controls.Add(review, 0, 3);
        side.Controls.Add(metrics); layout.Controls.Add(side, 1, 0);
        AddBlock(layout, (int)TextSize(280f));
    }
}

