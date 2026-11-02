using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace FishTank;

public sealed class ReportView : Panel
{
	private readonly Palette colors;

	private DetailedReport report;

	private Label summary;

	private DataGridView table;

	private TextBox details;

	private ComboBox filter;

	public ReportView(Palette colors)
	{
		this.colors = colors;
		BackColor = colors.Surface;
		base.Padding = new Padding(14);
		summary = new Label
		{
			Dock = DockStyle.Top,
			Height = 95,
			ForeColor = colors.Text
		};
		filter = new ComboBox
		{
			Dock = DockStyle.Top,
			DropDownStyle = ComboBoxStyle.DropDownList,
			BackColor = colors.Surface2,
			ForeColor = colors.Text
		};
		filter.Items.AddRange(new string[3] { "All records", "Needs review / errors", "Skipped / canceled" });
		filter.SelectedIndex = 0;
		ComboBox comboBox = filter;
		EventHandler value = delegate
		{
			Fill();
		};
		comboBox.SelectedIndexChanged += value;
		table = new DataGridView
		{
			Dock = DockStyle.Top,
			Height = 230,
			ReadOnly = true,
			AllowUserToAddRows = false,
			AllowUserToDeleteRows = false,
			RowHeadersVisible = false,
			SelectionMode = DataGridViewSelectionMode.FullRowSelect,
			MultiSelect = false,
			AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
			BackgroundColor = colors.Surface,
			BorderStyle = BorderStyle.None,
			EnableHeadersVisualStyles = false
		};
		table.DefaultCellStyle.BackColor = colors.Surface;
		table.DefaultCellStyle.ForeColor = colors.Text;
		table.DefaultCellStyle.SelectionBackColor = colors.Surface2;
		table.DefaultCellStyle.SelectionForeColor = colors.Text;
		table.ColumnHeadersDefaultCellStyle.BackColor = colors.Surface2;
		table.ColumnHeadersDefaultCellStyle.ForeColor = colors.Text;
		table.Columns.Add("path", "File / path");
		table.Columns.Add("state", "Result");
		table.Columns[0].FillWeight = 75f;
		table.Columns[1].FillWeight = 25f;
		details = new TextBox
		{
			Dock = DockStyle.Fill,
			ReadOnly = true,
			Multiline = true,
			ScrollBars = ScrollBars.Both,
			WordWrap = false,
			BackColor = colors.Surface2,
			ForeColor = colors.Text,
			AccessibleName = "Full selected scan evidence"
		};
		table.SelectionChanged += delegate
		{
			if (table.SelectedRows.Count > 0 && table.SelectedRows[0].Tag is ScanItem scanItem)
			{
				details.Text = "Path: " + scanItem.Path + Environment.NewLine + "State: " + scanItem.State + Environment.NewLine + scanItem.Reason + Environment.NewLine + "Bytes: " + scanItem.Length + Environment.NewLine + "SHA-256: " + scanItem.Sha256 + Environment.NewLine + "Engine code: " + scanItem.EngineExitCode + Environment.NewLine + "Output truncated: " + scanItem.OutputTruncated + Environment.NewLine + scanItem.Output + Environment.NewLine + scanItem.Error;
			}
		};
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Bottom,
			Height = 45
		};
		bool[] array = new bool[2] { false, true };
		foreach (bool flag in array)
		{
			bool format = flag;
			Button button = new Button
			{
				Text = (flag ? "Export CSV" : "Export JSON"),
				AutoSize = true,
				Height = 34,
				BackColor = colors.Surface2,
				ForeColor = colors.Text,
				FlatStyle = FlatStyle.Flat
			};
			button.Click += delegate
			{
				Export(format);
			};
			flowLayoutPanel.Controls.Add(button);
		}
		base.Controls.Add(details);
		base.Controls.Add(flowLayoutPanel);
		base.Controls.Add(table);
		base.Controls.Add(filter);
		base.Controls.Add(summary);
		SetReport(null);
	}

	public void SetReport(DetailedReport value)
	{
		report = value;
		summary.Text = ((value == null) ? "No scan report selected. Reports contain actual engine outcomes and coverage gaps; they do not certify a clean device." : ("Job: " + value.State + " · Engine calls: " + value.Scanned + " · Review: " + value.Review + " · Errors: " + value.Errors + " · Skipped: " + value.Skipped + Environment.NewLine + "Started: " + value.StartedUtc + "   Ended: " + value.EndedUtc + Environment.NewLine + ((value.Failure != "") ? (value.Failure + Environment.NewLine) : "") + "Table shows up to 1,000 filtered records. Exports include every stored record. Review does not mean confirmed malware."));
		Fill();
	}

	private void Fill()
	{
		if (table == null)
		{
			return;
		}
		table.Rows.Clear();
		details.Clear();
		if (report == null)
		{
			return;
		}
		IEnumerable<ScanItem> source = report.Items.AsEnumerable();
		if (filter.SelectedIndex == 1)
		{
			source = source.Where((ScanItem i) => i.State == "ReviewRequired" || i.State == "Error");
		}
		if (filter.SelectedIndex == 2)
		{
			source = source.Where((ScanItem i) => i.State == "Skipped" || i.State == "Canceled");
		}
		foreach (ScanItem item in source.Take(1000))
		{
			int index = table.Rows.Add(item.Path, item.State);
			table.Rows[index].Tag = item;
		}
	}

	private void Export(bool csv)
	{
		if (report == null || ExplainedMessageBox.Show(this, "Export a plaintext scan report? It contains file paths, hashes and scanner output. Review it before sharing. No file content is included by FishTank.", "Export report", MessageBoxButtons.YesNo, MessageBoxIcon.Asterisk) != DialogResult.Yes)
		{
			return;
		}
		SaveFileDialog saveFileDialog = new SaveFileDialog();
		saveFileDialog.Filter = (csv ? "CSV report|*.csv" : "JSON report|*.json");
		saveFileDialog.FileName = "FishTank-scan-" + report.Id + (csv ? ".csv" : ".json");
		using SaveFileDialog saveFileDialog2 = saveFileDialog;
		if (saveFileDialog2.ShowDialog(this) == DialogResult.OK)
		{
			try
			{
				ScanReports.Export(report, saveFileDialog2.FileName, csv);
				return;
			}
			catch (Exception ex)
			{
				ExplainedMessageBox.Show(this, ex.Message, "Export failed (existing files are never overwritten)");
				return;
			}
		}
	}
}
