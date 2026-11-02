using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace FishTank;

public sealed class RecoveryCodesDialog : Form
{
	private TextBox codes;

	private CheckBox saved;

	private readonly SecretClipboard clipboard = new SecretClipboard();

	public RecoveryCodesDialog(string[] values, Palette colors)
	{
		Text = "Save your FishTank recovery codes";
		base.Width = 690;
		base.Height = 580;
		MinimumSize = new Size(620, 520);
		base.StartPosition = FormStartPosition.CenterParent;
		BackColor = colors.Background;
		ForeColor = colors.Text;
		Font = new Font("Segoe UI", 11f);
		base.Padding = new Padding(22);
		Label value = new Label
		{
			Text = "Keep these codes somewhere safe",
			Dock = DockStyle.Top,
			Height = 48,
			Font = new Font("Segoe UI", 19f, FontStyle.Bold)
		};
		Label value2 = new Label
		{
			Text = "Shown once. Each code replaces the authenticator code for one verification, but still requires your app password. Keep them outside FishTank and separate from your device. New codes invalidate the old set.",
			Dock = DockStyle.Top,
			Height = 80,
			ForeColor = colors.Muted
		};
		codes = new TextBox
		{
			Dock = DockStyle.Fill,
			Multiline = true,
			ReadOnly = true,
			ScrollBars = ScrollBars.Vertical,
			Text = string.Join(Environment.NewLine, values),
			BackColor = colors.Surface2,
			ForeColor = colors.Text,
			Font = new Font("Consolas", 12f),
			AccessibleName = "One-time recovery codes"
		};
		saved = new CheckBox
		{
			Text = "I have saved the recovery codes securely",
			Dock = DockStyle.Bottom,
			Height = 40
		};
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Bottom,
			Height = 48
		};
		Button button = new Button
		{
			Text = "Copy codes (30-second clear)",
			AutoSize = true
		};
		EventHandler value3 = delegate
		{
			try
			{
				clipboard.Copy(codes.Text);
			}
			catch (Exception ex)
			{
				ExplainedMessageBox.Show(this, ex.Message);
			}
		};
		button.Click += value3;
		Button button2 = new Button
		{
			Text = "Save text file…",
			AutoSize = true
		};
		button2.Click += delegate
		{
			if (ExplainedMessageBox.Show(this, "Save these recovery codes as plaintext? Anyone with the file and your app password can use them. Choose a private location outside the app folder.", "Save recovery codes", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) != DialogResult.Yes)
			{
				return;
			}
			using SaveFileDialog saveFileDialog = new SaveFileDialog
			{
				Filter = "Text file|*.txt",
				FileName = "FishTank-recovery-codes.txt"
			};
			if (saveFileDialog.ShowDialog(this) == DialogResult.OK)
			{
				try
				{
					using FileStream stream = new FileStream(saveFileDialog.FileName, FileMode.CreateNew, FileAccess.Write, FileShare.None);
					using StreamWriter streamWriter = new StreamWriter(stream);
					streamWriter.Write("FishTank one-use recovery codes. App password is also required." + Environment.NewLine + codes.Text);
					return;
				}
				catch (Exception ex)
				{
					ExplainedMessageBox.Show(this, ex.Message, "File not saved (existing files are not overwritten)");
					return;
				}
			}
		};
		Button button3 = new Button
		{
			Text = "Done",
			AutoSize = true
		};
		button3.Click += delegate
		{
			Close();
		};
		flowLayoutPanel.Controls.Add(button);
		flowLayoutPanel.Controls.Add(button2);
		flowLayoutPanel.Controls.Add(button3);
		base.Controls.Add(codes);
		base.Controls.Add(saved);
		base.Controls.Add(flowLayoutPanel);
		base.Controls.Add(value2);
		base.Controls.Add(value);
		base.FormClosing += delegate(object s, FormClosingEventArgs e)
		{
			if (!saved.Checked && ExplainedMessageBox.Show(this, "Close without confirming the codes are saved? You can generate a replacement set later using your password and authenticator.", "Keep recovery access", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) != DialogResult.Yes)
			{
				e.Cancel = true;
			}
		};
		base.FormClosed += delegate
		{
			codes.Clear();
			clipboard.Dispose();
		};
	}
}
