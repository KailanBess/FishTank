using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FishTank;

internal sealed class ScanRunDialog : Form
{
    private readonly ScanOperation operation;
    private readonly Timer timer = new Timer { Interval = 500 };
    private readonly Label stage, elapsed, result;
    private readonly Button cancel, close;
    private readonly ProgressBar bar;
    internal ScanRunDialog(ScanOperation operation, Palette colors)
    {
        this.operation = operation;
        Text = "FishTank · " + operation.Kind + " scan progress"; StartPosition = FormStartPosition.CenterParent;
        Size = new Size(750, 540); MinimumSize = new Size(620, 480); AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = colors.Background; ForeColor = colors.Text; Font = new Font("Segoe UI", 10);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 6 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        stage = new Label { AutoSize = true, Dock = DockStyle.Fill, AccessibleName = "Scan stage", AccessibleRole = AccessibleRole.StatusBar };
        elapsed = new Label { AutoSize = true, Dock = DockStyle.Fill, AccessibleName = "Elapsed scan time" };
        bar = new ProgressBar { Dock = DockStyle.Fill, Style = ProgressBarStyle.Marquee, AccessibleName = "Scan activity; Defender does not supply a reliable percentage" };
        result = new Label { Dock = DockStyle.Fill, UseMnemonic = false, AccessibleName = "Scan outcome and cancellation evidence" };
        cancel = new Button { Dock = DockStyle.Fill, Text = "Request Defender cancellation", AccessibleName = "Request cancellation of quick or full Defender scans" };
        close = new Button { Dock = DockStyle.Fill, Text = "Keep scanning in background", AccessibleName = "Hide scan progress; scan continues" };
        cancel.Click += async delegate { if (operation.CancelRequested) { await operation.RefreshEvidenceAsync(); RefreshState(); return; } if (MessageBox.Show(this, "Ask Defender to cancel active quick/full scans? This can also affect scans started elsewhere. A successful request alone does not confirm a stop.", "Cancel Defender scan", MessageBoxButtons.YesNo) == DialogResult.Yes) await operation.CancelAsync(); RefreshState(); };
        close.Click += delegate { if (operation.IsRunning) Hide(); else Close(); };
        layout.Controls.Add(stage, 0, 0); layout.Controls.Add(elapsed, 0, 1); layout.Controls.Add(bar, 0, 2); layout.Controls.Add(result, 0, 3); layout.Controls.Add(cancel, 0, 4); layout.Controls.Add(close, 0, 5);
        Controls.Add(layout);
        timer.Tick += delegate { RefreshState(); }; timer.Start();
        FormClosing += delegate(object sender, FormClosingEventArgs e) { if (operation.IsRunning && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
        RefreshState();
    }
    private void RefreshState()
    {
        if (IsDisposed) return;
        stage.Text = operation.Phase;
        elapsed.Text = "Elapsed: " + operation.Elapsed.ToString(@"hh\:mm\:ss");
        result.Text = (operation.Result == "" ? "Defender does not provide a reliable percentage or current filename for this scan. Windows policy controls remediation." : operation.Result)
            + "\n\n" + operation.Cancellation + (operation.Kind == "Custom" ? "\nCustom scans do not support the quick/full cancel command. Wait for the command result or review Windows Security." : "");
        cancel.Text = operation.CancelRequested ? "Refresh cancellation evidence" : "Request Defender cancellation";
        cancel.Enabled = (operation.IsRunning || operation.CancelRequested) && (operation.Kind == "Quick" || operation.Kind == "Full");
        bar.Style = operation.IsRunning ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
        close.Text = operation.IsRunning ? "Keep scanning in background" : "Close scan result";
        close.AccessibleName = operation.IsRunning ? "Hide scan progress; scan continues" : "Close scan result";
    }
    protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
}
