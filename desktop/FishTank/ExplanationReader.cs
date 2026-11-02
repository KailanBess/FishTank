using System;
using System.Drawing;
using System.Windows.Forms;

namespace FishTank;

public sealed class ExplanationReader : Form
{
	public ExplanationReader(string title, string original)
	{
		Text = "Explain simply — " + title;
		base.Size = new Size(800, 620);
		MinimumSize = new Size(480, 340);
		base.StartPosition = FormStartPosition.CenterParent;
		base.AutoScaleMode = AutoScaleMode.Dpi;
		Font = new Font("Segoe UI", 11f);
		if (string.IsNullOrWhiteSpace(original))
		{
			original = "No explanatory text is visible here. Passwords, setup keys, recovery codes and editable fields are excluded.";
		}
		TabControl tabs = new TabControl
		{
			Dock = DockStyle.Fill,
			AccessibleName = "Simple explanation and original information"
		};
		string[] array = new string[2] { "Simple explanation", "Full original details" };
		foreach (string text in array)
		{
			TabPage value = new TabPage(text)
			{
				Controls = { (Control)new TextBox
				{
					Multiline = true,
					ReadOnly = true,
					ScrollBars = ScrollBars.Both,
					WordWrap = true,
					Dock = DockStyle.Fill,
					Font = Font,
					Text = ((text == "Simple explanation") ? SimpleExplanations.Simplify(original) : original),
					AccessibleName = text
				} }
			};
			tabs.TabPages.Add(value);
		}
		Label note = new Label
		{
			Text = "Technical values stay unchanged. Switch tabs for the exact original. No information is sent online.",
			Dock = DockStyle.Top,
			AutoSize = true,
			MaximumSize = new Size(base.ClientSize.Width, 0),
			Padding = new Padding(8),
			UseMnemonic = false
		};
		base.SizeChanged += delegate
		{
			note.MaximumSize = new Size(Math.Max(1, base.ClientSize.Width), 0);
		};
		Button button = new Button
		{
			Text = "Close",
			Dock = DockStyle.Bottom,
			Height = 38,
			DialogResult = DialogResult.Cancel
		};
		base.Controls.Add(tabs);
		base.Controls.Add(note);
		base.Controls.Add(button);
		base.CancelButton = button;
		base.KeyPreview = true;
		base.KeyDown += delegate(object s, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.F1)
			{
				tabs.SelectedIndex = 0;
				e.Handled = true;
			}
		};
	}
}
