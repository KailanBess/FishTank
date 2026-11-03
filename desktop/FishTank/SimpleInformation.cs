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
			BackColor = background,
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false,
			ForeColor = foreground
		};
		toggle.FlatAppearance.BorderColor = SystemInformation.HighContrast ? foreground : ControlPaint.Light(background, 0.2f);
        toggle.FlatAppearance.MouseOverBackColor = ControlPaint.Light(background, 0.1f);
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
