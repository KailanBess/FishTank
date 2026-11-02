using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FishTank;

public sealed class ScanHistoryDialog : Form
{
	private sealed class HistoryItem
	{
		public string Id;

		public DateTime Written;

		public override string ToString()
		{
			return Written.ToString("g") + " · " + Id.Substring(0, 8);
		}
	}

	public ScanHistoryDialog(Palette colors)
	{
		Text = "FishTank · Detailed scan history";
		base.Width = 1150;
		base.Height = 800;
		MinimumSize = new Size(900, 680);
		base.StartPosition = FormStartPosition.CenterParent;
		Font = new Font("Segoe UI", 10f);
		BackColor = colors.Background;
		ForeColor = colors.Text;
		base.Padding = new Padding(18);
		ListBox ids = new ListBox
		{
			Dock = DockStyle.Left,
			Width = 250,
			BackColor = colors.Surface2,
			ForeColor = colors.Text
		};
		ReportView view = new ReportView(colors)
		{
			Dock = DockStyle.Fill
		};
		Label value = new Label
		{
			Dock = DockStyle.Top,
			Height = 45,
			Text = "Select one of the 50 most recently written local encrypted reports (local write time shown). A Running checkpoint can indicate an interrupted job."
		};
		base.Controls.Add(view);
		base.Controls.Add(ids);
		base.Controls.Add(value);
		string[] array = ScanReports.RecentIds();
		foreach (string text in array)
		{
			ids.Items.Add(new HistoryItem
			{
				Id = text,
				Written = File.GetLastWriteTime(Path.Combine(Core.Data, "scan-reports", text + ".dpapi"))
			});
		}
		ids.SelectedIndexChanged += async delegate
		{
			if (ids.SelectedItem == null)
			{
				return;
			}
			string id = ((HistoryItem)ids.SelectedItem).Id;
			ids.Enabled = false;
			try
			{
				int num = default(int);
				_ = num;
				_ = 0;
				try
				{
					DetailedReport report = await Task.Run(() => ScanReports.Read(id));
					if (!base.IsDisposed)
					{
						view.SetReport(report);
					}
				}
				catch (Exception ex)
				{
					ExplainedMessageBox.Show(this, ex.Message, "Report unavailable");
				}
			}
			finally
			{
				if (!base.IsDisposed)
				{
					ids.Enabled = true;
				}
			}
		};
	}
}
