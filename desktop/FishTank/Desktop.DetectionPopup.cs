using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FishTank;

public sealed partial class Desktop
{
    private DetectionPopup detectionPopup;

    private void ShowDetectionPopup(LiveDetectionEvent item)
    {
        if (detectionPopup != null && !detectionPopup.IsDisposed) return;
        var popup = new DetectionPopup(item.Kind);
        detectionPopup = popup;
        popup.FormClosed += (_, _) => { if (detectionPopup == popup) detectionPopup = null; };
        popup.Details.Click += (_, _) =>
        {
            popup.Close();
            notificationTarget = "Monitoring";
            OpenNotification();
        };
        popup.Action.Click += async (_, _) =>
        {
            if (appLock.IsLocked)
            {
                popup.Status.Text = "Unlock FishTank to save an incident.";
                return;
            }
            popup.Action.Enabled = false;
            try
            {
                await Task.Run(() => IncidentWorkspace.CreateFromDetection(item));
                if (!popup.IsDisposed)
                {
                    popup.Status.Text = "Incident saved. Review it later in FishTank.";
                    popup.StopExpiry();
                }
            }
            catch (Exception)
            {
                if (!popup.IsDisposed)
                {
                    popup.Action.Enabled = true;
                    popup.Status.Text = "Incident could not be saved. The alert is still in Monitoring.";
                }
            }
        };
        popup.ShowAt(Screen.PrimaryScreen.WorkingArea);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        detectionPopup?.Close();
        base.OnFormClosed(e);
    }

    private sealed class DetectionPopup : Form
    {
        internal readonly Button Action = new Button { Text = "Take action", AutoSize = true };
        internal readonly Button Details = new Button { Text = "View details", AutoSize = true };
        internal readonly Label Status = new Label { AutoSize = true, MaximumSize = new Size(364, 0) };
        private readonly Timer expiry = new Timer { Interval = 25000 };

        protected override bool ShowWithoutActivation => true;

        internal DetectionPopup(string kind)
        {
            Text = "FishTank security alert";
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            BackColor = Color.FromArgb(23, 26, 37);
            ForeColor = Color.White;
            var layout = new FlowLayoutPanel
            {
                AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
                WrapContents = false, Padding = new Padding(12)
            };
            var heading = new Label { Text = kind, AutoSize = true };
            Status.Text = "Take action saves a local incident for review. It leaves the file and Defender settings as they are.";
            var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            var ignore = new Button { Text = "Ignore", AutoSize = true };
            ignore.Click += (_, _) => Close();
            foreach (var button in new[] { Action, Details, ignore })
            {
                button.FlatStyle = FlatStyle.Flat;
                actions.Controls.Add(button);
            }
            layout.Controls.Add(heading);
            layout.Controls.Add(Status);
            layout.Controls.Add(actions);
            Controls.Add(layout);
            expiry.Tick += (_, _) => Close();
        }

        internal void ShowAt(Rectangle area)
        {
            PerformLayout();
            Location = new Point(Math.Max(area.Left, area.Right - Width - 16),
                Math.Max(area.Top, area.Bottom - Height - 16));
            Show();
            expiry.Start();
        }

        internal void StopExpiry() => expiry.Stop();

        protected override void Dispose(bool disposing)
        {
            if (disposing) expiry.Dispose();
            base.Dispose(disposing);
        }
    }
}
