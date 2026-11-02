using System;
using System.Drawing;
using System.Windows.Forms;

namespace FishTank;

public sealed class ExplanationMessage : Form
{
	public ExplanationMessage(string original, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
	{
		if (buttons != MessageBoxButtons.OK && buttons != MessageBoxButtons.YesNo)
		{
			throw new ArgumentException("Unsupported confirmation button set");
		}
		Text = caption;
		base.Size = new Size(740, 480);
		MinimumSize = new Size(460, 300);
		base.StartPosition = FormStartPosition.CenterParent;
		base.AutoScaleMode = AutoScaleMode.Dpi;
		Font = new Font("Segoe UI", 11f);
		base.MinimizeBox = (base.MaximizeBox = false);
		CheckBox choice = new CheckBox
		{
			Text = "Explain simply (F1)",
			Checked = SimpleExplanations.DefaultSimple,
			Dock = DockStyle.Top,
			AutoSize = true,
			Padding = new Padding(10),
			AccessibleName = "Switch this message between simple and original wording"
		};
		TextBox body = new TextBox
		{
			Text = (choice.Checked ? SimpleExplanations.Simplify(original) : original),
			Multiline = true,
			ReadOnly = true,
			WordWrap = true,
			ScrollBars = ScrollBars.Vertical,
			Dock = DockStyle.Fill,
			Font = Font,
			AccessibleName = "Message details"
		};
		choice.CheckedChanged += delegate
		{
			body.Text = (choice.Checked ? SimpleExplanations.Simplify(original) : original);
		};
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Bottom,
			AutoSize = true,
			Padding = new Padding(8),
			FlowDirection = FlowDirection.RightToLeft
		};
		if (buttons == MessageBoxButtons.YesNo)
		{
			Button no = new Button
			{
				Text = "No",
				AutoSize = true,
				DialogResult = DialogResult.No
			};
			Button value = new Button
			{
				Text = "Yes",
				AutoSize = true,
				DialogResult = DialogResult.Yes
			};
			flowLayoutPanel.Controls.Add(no);
			flowLayoutPanel.Controls.Add(value);
			base.CancelButton = no;
			base.AcceptButton = no;
			base.Shown += delegate
			{
				no.Focus();
			};
		}
		else
		{
			Button button = new Button
			{
				Text = "OK",
				AutoSize = true,
				DialogResult = DialogResult.OK
			};
			flowLayoutPanel.Controls.Add(button);
			base.CancelButton = button;
			base.AcceptButton = button;
		}
		if (icon != MessageBoxIcon.None)
		{
			PictureBox symbol = new PictureBox
			{
				Dock = DockStyle.Left,
				Width = 50,
				SizeMode = PictureBoxSizeMode.CenterImage,
				AccessibleName = ((icon == MessageBoxIcon.Exclamation) ? "Warning" : "Information")
			};
			Icon icon2 = icon switch
			{
				MessageBoxIcon.Exclamation => SystemIcons.Warning,
				MessageBoxIcon.Hand => SystemIcons.Error,
				_ => SystemIcons.Information,
			};
			symbol.Image = icon2.ToBitmap();
			symbol.Disposed += delegate
			{
				symbol.Image.Dispose();
			};
			base.Controls.Add(body);
			base.Controls.Add(symbol);
		}
		else
		{
			base.Controls.Add(body);
		}
		base.Controls.Add(choice);
		base.Controls.Add(flowLayoutPanel);
		base.KeyPreview = true;
		base.KeyDown += delegate(object s, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.F1)
			{
				choice.Checked = true;
				e.Handled = true;
				e.SuppressKeyPress = true;
			}
		};
	}
}
