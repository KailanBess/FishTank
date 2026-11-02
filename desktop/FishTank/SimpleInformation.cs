using System;
using System.Drawing;
using System.Windows.Forms;

namespace FishTank;

public sealed class SimpleInformation : Panel
{
	public readonly string OriginalInformation;

	private readonly TextBox content;

	private readonly Button toggle;

	private bool simple;

	public SimpleInformation(string text, Color background, Color foreground, Font font)
	{
		OriginalInformation = text;
		BackColor = background;
		content = new TextBox
		{
			Dock = DockStyle.Fill,
			Multiline = true,
			ReadOnly = true,
			WordWrap = true,
			ScrollBars = ScrollBars.Vertical,
			BorderStyle = BorderStyle.None,
			BackColor = background,
			ForeColor = foreground,
			Font = font,
			AccessibleName = "Information text"
		};
		toggle = new Button
		{
			Dock = DockStyle.Bottom,
			Height = 34,
			AccessibleName = "Switch between simple explanation and full original details",
			BackColor = SystemColors.Control,
			ForeColor = SystemColors.ControlText
		};
		Button button = toggle;
		EventHandler value = delegate
		{
			simple = !simple;
			UpdateText();
		};
		button.Click += value;
		base.Controls.Add(content);
		base.Controls.Add(toggle);
		ApplyDefault();
	}

	public void ApplyDefault()
	{
		simple = SimpleExplanations.DefaultSimple;
		UpdateText();
	}

	private void UpdateText()
	{
		content.Text = (simple ? SimpleExplanations.Simplify(OriginalInformation) : OriginalInformation);
		toggle.Text = (simple ? "Show full details" : "Explain simply");
	}
}
