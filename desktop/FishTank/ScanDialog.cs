using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FishTank;

public sealed class ScanDialog : Form
{
	private readonly Palette colors;

	private ListBox targets;

	private TextBox exclusions;

	private ComboBox profile;

	private NumericUpDown maxFiles;

	private NumericUpDown maxMiB;

	private NumericUpDown delay;

	private CheckBox hashes;

	private Panel configuration;

	private Label progressText;

	private ProgressBar bar;

	private Button start;

	private Button pause;

	private Button cancel;

	private TabControl tabs;

	private ReportView result;

	private CancellationTokenSource cancellation;

	private ManualResetEventSlim resume;

	private bool running;

	private bool paused;

	private DateTime lastProgress = DateTime.MinValue;

	public DetailedReport LastReport { get; private set; }

	public ScanDialog(Palette colors)
	{
		this.colors = colors;
		Text = "FishTank · Detailed custom scan";
		base.Width = 1120;
		base.Height = 850;
		MinimumSize = new Size(900, 750);
		base.StartPosition = FormStartPosition.CenterParent;
		BackColor = colors.Background;
		ForeColor = colors.Text;
		Font = new Font("Segoe UI", 10f);
		base.Padding = new Padding(20);
		Label value = new Label
		{
			Text = "Scan exactly what you choose",
			Dock = DockStyle.Top,
			Height = 50,
			Font = new Font("Segoe UI", 22f, FontStyle.Bold)
		};
		tabs = new TabControl
		{
			Dock = DockStyle.Fill
		};
		TabPage tabPage = new TabPage("Targets & options")
		{
			BackColor = colors.Surface,
			ForeColor = colors.Text,
			Padding = new Padding(14)
		};
		TabPage tabPage2 = new TabPage("Results")
		{
			BackColor = colors.Surface,
			Padding = new Padding(8)
		};
		tabs.TabPages.Add(tabPage);
		tabs.TabPages.Add(tabPage2);
		result = new ReportView(colors)
		{
			Dock = DockStyle.Fill
		};
		tabPage2.Controls.Add(result);
		configuration = new Panel
		{
			Dock = DockStyle.Fill,
			AutoScroll = true
		};
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			AutoSize = true,
			ColumnCount = 2
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 75f));
		configuration.Controls.Add(tableLayoutPanel);
		tabPage.Controls.Add(configuration);
		targets = new ListBox
		{
			BackColor = colors.Surface2,
			ForeColor = colors.Text
		};
		Row(tableLayoutPanel, "Files, folders or drive roots", targets, 150);
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			WrapContents = true
		};
		Control.ControlCollection controls = flowLayoutPanel.Controls;
		Action action = delegate
		{
			using OpenFileDialog openFileDialog = new OpenFileDialog
			{
				Multiselect = true
			};
			if (openFileDialog.ShowDialog(this) == DialogResult.OK)
			{
				string[] fileNames = openFileDialog.FileNames;
				foreach (string path in fileNames)
				{
					AddTarget(path);
				}
			}
		};
		controls.Add(Button("Add files", action));
		flowLayoutPanel.Controls.Add(Button("Add folder / drive", delegate
		{
			using FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog();
			if (folderBrowserDialog.ShowDialog(this) == DialogResult.OK)
			{
				AddTarget(folderBrowserDialog.SelectedPath);
			}
		}));
		flowLayoutPanel.Controls.Add(Button("Remove target", delegate
		{
			if (targets.SelectedIndex >= 0)
			{
				targets.Items.RemoveAt(targets.SelectedIndex);
			}
		}));
		Row(tableLayoutPanel, "", flowLayoutPanel, 50);
		exclusions = new TextBox
		{
			Multiline = true,
			ScrollBars = ScrollBars.Vertical,
			BackColor = colors.Surface2,
			ForeColor = colors.Text
		};
		Row(tableLayoutPanel, "Excluded paths (one per line)", exclusions, 75);
		maxFiles = Number(1m, 100000m, 10000m);
		maxMiB = Number(0m, 1048576m, 0m);
		delay = Number(0m, 10000m, 0m);
		Row(tableLayoutPanel, "Maximum files", maxFiles, 44);
		Row(tableLayoutPanel, "Skip files over MiB (0 = no limit)", maxMiB, 44);
		Row(tableLayoutPanel, "Delay between files (ms)", delay, 44);
		hashes = new CheckBox
		{
			Text = "Calculate SHA-256 before and after each file scan",
			Checked = true,
			ForeColor = colors.Text
		};
		Row(tableLayoutPanel, "File evidence", hashes, 45);
		profile = new ComboBox
		{
			DropDownStyle = ComboBoxStyle.DropDown,
			BackColor = colors.Surface2,
			ForeColor = colors.Text
		};
		profile.Items.AddRange(ScanProfiles.Names());
		profile.Text = "Custom scan";
		Row(tableLayoutPanel, "Profile name", profile, 44);
		Row(tableLayoutPanel, "Reusable settings", new FlowLayoutPanel
		{
			Controls =
			{
				(Control)Button("Save profile", delegate
				{
					ScanProfiles.Save(Plan());
					RefreshNames();
				}),
				(Control)Button("Load profile", delegate
				{
					LoadPlan(ScanProfiles.Load(profile.Text));
				})
			}
		}, 50);
		Row(tableLayoutPanel, "Coverage", new Label
		{
			Text = "Custom scans request no remediation. Defender’s own real-time policy remains active. Reparse points are not followed; access failures, exclusions and limits are recorded. Archives follow Defender policy; FishTank does not unpack them. File-level scanning can be slow.",
			ForeColor = colors.Muted
		}, 82);
		Panel panel = new Panel
		{
			Dock = DockStyle.Bottom,
			Height = 120
		};
		progressText = new Label
		{
			Dock = DockStyle.Top,
			Height = 42,
			Text = "Ready · No scan has started.",
			ForeColor = colors.Muted
		};
		bar = new ProgressBar
		{
			Dock = DockStyle.Top,
			Height = 12
		};
		FlowLayoutPanel flowLayoutPanel2 = new FlowLayoutPanel
		{
			Dock = DockStyle.Bottom,
			Height = 50
		};
		start = Button("Start detailed scan", delegate
		{
			Start();
		});
		pause = Button("Pause queue", delegate
		{
			Pause();
		});
		cancel = Button("Cancel queue", delegate
		{
			if (cancellation != null)
			{
				cancellation.Cancel();
				resume.Set();
				progressText.Text = "Cancel requested. A delegated Defender operation may continue; pending files will not be scanned.";
			}
		});
		pause.Enabled = (cancel.Enabled = false);
		flowLayoutPanel2.Controls.Add(start);
		flowLayoutPanel2.Controls.Add(pause);
		flowLayoutPanel2.Controls.Add(cancel);
		panel.Controls.Add(bar);
		panel.Controls.Add(progressText);
		panel.Controls.Add(flowLayoutPanel2);
		base.Controls.Add(tabs);
		base.Controls.Add(panel);
		base.Controls.Add(value);
		base.FormClosing += delegate(object s, FormClosingEventArgs e)
		{
			if (running)
			{
				e.Cancel = true;
				if (ExplainedMessageBox.Show(this, "Cancel the remaining queue? Wait for it to stop before closing. Windows may continue a delegated scan.", "Scan running", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) == DialogResult.Yes)
				{
					cancellation.Cancel();
					resume.Set();
				}
			}
		};
		base.FormClosed += delegate
		{
			if (cancellation != null)
			{
				cancellation.Dispose();
			}
			if (resume != null)
			{
				resume.Dispose();
			}
		};
	}

	private static NumericUpDown Number(decimal min, decimal max, decimal value)
	{
		NumericUpDown numericUpDown = new NumericUpDown();
		numericUpDown.Minimum = min;
		numericUpDown.Maximum = max;
		numericUpDown.Value = value;
		numericUpDown.ThousandsSeparator = true;
		return numericUpDown;
	}

	private Button Button(string text, Action action)
	{
		Button button = new Button();
		button.Text = text;
		button.AutoSize = true;
		button.Height = 36;
		button.BackColor = colors.Surface2;
		button.ForeColor = colors.Text;
		button.FlatStyle = FlatStyle.Flat;
		button.Margin = new Padding(0, 0, 10, 5);
		Button button2 = button;
		button2.Click += delegate
		{
			try
			{
				action();
			}
			catch (Exception ex)
			{
				ExplainedMessageBox.Show(this, ex.Message, "Scan options");
			}
		};
		return button2;
	}

	private static void Row(TableLayoutPanel table, string label, Control field, int height)
	{
		int row = table.RowCount++;
		table.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
		table.Controls.Add(new Label
		{
			Text = label,
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleLeft
		}, 0, row);
		field.Dock = DockStyle.Fill;
		field.Margin = new Padding(0, 5, 0, 5);
		field.AccessibleName = label;
		table.Controls.Add(field, 1, row);
	}

	private void AddTarget(string path)
	{
		path = ScanPaths.Normalize(path);
		if (!targets.Items.Contains(path))
		{
			targets.Items.Add(path);
		}
	}

	private ScanPlan Plan()
	{
		ScanPlan scanPlan = new ScanPlan();
		scanPlan.Name = profile.Text;
		scanPlan.Targets = targets.Items.Cast<string>().ToArray();
		scanPlan.ExcludedPaths = (from line in exclusions.Lines
			where !string.IsNullOrWhiteSpace(line)
			select line.Trim()).ToArray();
		scanPlan.MaximumFiles = (int)maxFiles.Value;
		scanPlan.MaximumFileBytes = (long)maxMiB.Value * 1024 * 1024;
		scanPlan.DelayMilliseconds = (int)delay.Value;
		scanPlan.HashFiles = hashes.Checked;
		ScanPlan scanPlan2 = scanPlan;
		scanPlan2.Validate();
		return scanPlan2;
	}

	private void LoadPlan(ScanPlan plan)
	{
		targets.Items.Clear();
		targets.Items.AddRange(plan.Targets);
		exclusions.Lines = plan.ExcludedPaths;
		profile.Text = plan.Name;
		maxFiles.Value = plan.MaximumFiles;
		maxMiB.Value = plan.MaximumFileBytes / 1024 / 1024;
		delay.Value = plan.DelayMilliseconds;
		hashes.Checked = plan.HashFiles;
	}

	private void RefreshNames()
	{
		string text = profile.Text;
		profile.Items.Clear();
		profile.Items.AddRange(ScanProfiles.Names());
		profile.Text = text;
	}

	private void Pause()
	{
		if (running)
		{
			paused = !paused;
			if (paused)
			{
				resume.Reset();
				pause.Text = "Resume queue";
				progressText.Text = "Pause requested · The current file finishes before the queue pauses.";
			}
			else
			{
				resume.Set();
				pause.Text = "Pause queue";
			}
		}
	}

	private async void Start()
	{
		if (running)
		{
			return;
		}
		ScanPlan plan;
		try
		{
			plan = Plan();
		}
		catch (Exception ex)
		{
			ExplainedMessageBox.Show(this, ex.Message, "Check targets/options");
			return;
		}
		running = true;
		paused = false;
		configuration.Enabled = false;
		start.Enabled = false;
		Button button = pause;
		bool enabled = (cancel.Enabled = true);
		button.Enabled = enabled;
		pause.Text = "Pause queue";
		if (cancellation != null)
		{
			cancellation.Dispose();
		}
		if (resume != null)
		{
			resume.Dispose();
		}
		cancellation = new CancellationTokenSource();
		resume = new ManualResetEventSlim(initialState: true);
		bar.Style = ProgressBarStyle.Marquee;
		progressText.Text = "Inventorying selected local paths…";
		lastProgress = DateTime.MinValue;
		Progress<ScanProgress> progress = new Progress<ScanProgress>(delegate(ScanProgress p)
		{
			if (!base.IsDisposed && (!((DateTime.UtcNow - lastProgress).TotalMilliseconds < 150.0) || !(p.Phase == "Inventory")))
			{
				lastProgress = DateTime.UtcNow;
				if (p.Total > 0)
				{
					bar.Style = ProgressBarStyle.Blocks;
					bar.Value = Math.Min(100, (int)(100L * (long)p.Completed / p.Total));
				}
				progressText.Text = p.Phase + " · " + p.Completed + ((p.Total > 0) ? (" / " + p.Total) : "") + " · " + p.CurrentPath;
			}
		});
		try
		{
			int num = default(int);
			_ = num;
			_ = 0;
			try
			{
				DetailedReport report = (LastReport = await Task.Run(() => DetailedScanning.Run(plan, cancellation.Token, resume, progress, null)));
				result.SetReport(report);
				tabs.SelectedIndex = 1;
				progressText.Text = report.State + " · Engine calls: " + report.Scanned + " · Review: " + report.Review + " · Gaps/errors: " + (report.Skipped + report.Errors) + ". This is not a clean-device certificate.";
				bar.Style = ProgressBarStyle.Blocks;
				if (report.State.StartsWith("Completed"))
				{
					bar.Value = 100;
				}
			}
			catch (Exception ex2)
			{
				progressText.Text = "Scan failed: " + ex2.Message;
				bar.Style = ProgressBarStyle.Blocks;
			}
		}
		finally
		{
			running = false;
			configuration.Enabled = true;
			start.Enabled = true;
			Button button2 = pause;
			bool enabled2 = (cancel.Enabled = false);
			button2.Enabled = enabled2;
		}
	}
}
