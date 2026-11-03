using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace FishTank;

internal sealed class LiveDetectionDialog : Form
{
    private readonly LiveDetectionSession live;
    private readonly Label coverage;
    private readonly DataGridView list;
    private readonly TextBox detail;
    private readonly CheckBox advanced;
    private readonly Timer refresh = new Timer { Interval = 2000 };
    private string signature;
    internal LiveDetectionDialog(LiveDetectionSession live, Palette colors, bool showDetails)
    {
        this.live = live;
        Text = "FishTank · Live Defender alerts"; AutoScaleMode = AutoScaleMode.Dpi;
        Size = new Size(1050, 800); MinimumSize = new Size(800, 620); StartPosition = FormStartPosition.CenterParent;
        BackColor = colors.Background; ForeColor = colors.Text; Font = new Font("Segoe UI", 10);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(14) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 60)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        coverage = new Label { AutoSize = true, MaximumSize = new Size(950, 0), UseMnemonic = false }; layout.Controls.Add(coverage, 0, 0);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        advanced = new CheckBox { Text = "Show selected event details (may include private paths)", Checked = showDetails, AutoSize = true };
        advanced.CheckedChanged += delegate { ShowSelected(); }; actions.Controls.Add(advanced);
        var export = new Button { Text = "Export retained history", AutoSize = true };
        export.Click += delegate {
            try {
                if (ExplainedMessageBox.Show(this, "Export retained Defender event metadata? It can contain private file/process paths and threat details. Keep the report private.", "Export live history", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                using (var picker = new SaveFileDialog { Filter = "JSON report|*.json", FileName = "FishTank-live-alerts.json" })
                    if (picker.ShowDialog(this) == DialogResult.OK) using (var file = new StreamWriter(new FileStream(picker.FileName, FileMode.CreateNew, FileAccess.Write, FileShare.None))) file.Write(live.Report());
            } catch { detail.Text = "Export did not complete. Existing files were not overwritten; a partial selected report may remain."; }
        };
        actions.Controls.Add(export);
        var checkNow = new Button { Text = "Check now", AutoSize = true };
        checkNow.Click += async delegate { checkNow.Enabled = false; try { await live.CheckNowAsync(); if (!IsDisposed) RefreshEvents(); } finally { if (!checkNow.IsDisposed) checkNow.Enabled = true; } };
        actions.Controls.Add(checkNow); layout.Controls.Add(actions, 0, 1);
        list = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
            MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = colors.Surface, EnableHeadersVisualStyles = false, AccessibleName = "Defender-reported live events and coverage gaps" };
        list.DefaultCellStyle.BackColor = colors.Surface; list.DefaultCellStyle.ForeColor = colors.Text;
        list.DefaultCellStyle.SelectionBackColor = colors.Surface2; list.DefaultCellStyle.SelectionForeColor = colors.Text;
        list.ColumnHeadersDefaultCellStyle.BackColor = colors.Surface2; list.ColumnHeadersDefaultCellStyle.ForeColor = colors.Text;
        list.Columns.Add("time", "Source time (local)"); list.Columns.Add("kind", "Source / event type"); list.Columns.Add("summary", "Explanation");
        list.Columns[0].FillWeight = 22; list.Columns[1].FillWeight = 26; list.Columns[2].FillWeight = 52;
        list.SelectionChanged += delegate { ShowSelected(); }; layout.Controls.Add(list, 0, 2);
        detail = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = colors.Surface, ForeColor = colors.Text, AccessibleName = "Selected live event explanation and optional technical details" };
        layout.Controls.Add(detail, 0, 3); Controls.Add(layout);
        layout.SizeChanged += delegate { coverage.MaximumSize = new Size(Math.Max(200, layout.Width - 32), 0); };
        refresh.Tick += delegate { RefreshEvents(); }; refresh.Start(); RefreshEvents();
        Shown += delegate { signature = null; RefreshEvents(); };
    }
    private static string Key(LiveDetectionEvent item) => item.RecordedUtc.Ticks + ":" + item.RecordId + ":" + item.EventId + ":" + item.Kind;
    private void RefreshEvents()
    {
        if (IsDisposed) return;
        coverage.Text = live.Status + "\nUp to 1,000 entries / 4 MiB retained across sessions; trimmed counts are reported. Missed records are reconciled while available in Windows. No events does not mean no threats. This is a Defender event listener, not a second antivirus engine.";
        var items = live.Events(); string current = items.Count + ":" + (items.Count == 0 ? "" : Key(items.Last()));
        if (signature == current) return;
        string selected = list.SelectedRows.Count == 0 ? null : Key((LiveDetectionEvent)list.SelectedRows[0].Tag);
        bool follow = list.SelectedRows.Count == 0 || list.SelectedRows[0].Index == list.Rows.Count - 1;
        signature = current; list.Rows.Clear();
        foreach (var item in items) list.Rows[list.Rows.Add(item.EventUtc.ToLocalTime().ToString("g"), (item.Recovered ? "Recovered · " : "") + item.Kind, item.Summary)].Tag = item;
        list.ClearSelection();
        var target = follow ? list.Rows.Cast<DataGridViewRow>().LastOrDefault() : list.Rows.Cast<DataGridViewRow>().FirstOrDefault(x => Key((LiveDetectionEvent)x.Tag) == selected);
        if (target != null) { target.Selected = true; if (follow && list.IsHandleCreated && list.DisplayedRowCount(false) > 0) list.FirstDisplayedScrollingRowIndex = target.Index; }
        ShowSelected();
    }
    private void ShowSelected()
    {
        if (detail == null) return;
        if (list.SelectedRows.Count == 0 || !(list.SelectedRows[0].Tag is LiveDetectionEvent item)) { detail.Text = "Select an event. Background/offline gaps and missing telemetry are not treated as clean results."; return; }
        detail.Text = item.Summary + "\r\n\r\n" + (item.Recovered ? "Recovered historical record" : item.RecordId > 0 ? "Live source record" : "FishTank monitoring observation") + "\r\nReceived (UTC): " + item.RecordedUtc.ToString("u") + (advanced.Checked ? "\r\n\r\nEvent ID: " + item.EventId + " · Record ID: " + item.RecordId + "\r\nSource event time (UTC): " + item.EventUtc.ToUniversalTime().ToString("u") + "\r\n" + item.Details : "\r\n\r\nEnable details for the retained source metadata. FishTank has not opened or inspected the reported files.");
    }
    protected override void Dispose(bool disposing) { if (disposing) refresh.Dispose(); base.Dispose(disposing); }
}
