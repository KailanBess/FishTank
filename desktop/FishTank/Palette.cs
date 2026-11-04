using System.Drawing;
using System.Windows.Forms;

namespace FishTank;

public sealed class Palette
{
	public Color Background;

	public Color Sidebar;

	public Color Surface;

	public Color Surface2;

	public Color Text;

	public Color Muted;

	public Color Line;

	public Color Accent;

	public Color AccentText;

	public static Color AccentColor(string name)
	{
        if (name == "Aquarium Mint") return Color.FromArgb(103, 232, 211);
        if (name == "Aquarium Blue") return Color.FromArgb(113, 190, 255);
        if (name == "Aquarium Coral") return Color.FromArgb(255, 170, 150);
		if (FishBowlColors.Accents.TryGetValue(name, out var value))
		{
			return Color.FromArgb(value[0]);
		}
		return name switch
		{
			"Violet" => Color.FromArgb(157, 126, 247),
			"Blue" => Color.FromArgb(77, 148, 255),
			"Pink" => Color.FromArgb(232, 117, 181),
			"Mint" => Color.FromArgb(58, 193, 159),
			"Sunset" => Color.FromArgb(238, 151, 87),
			_ => Color.FromArgb(43, 184, 215),
		};
	}

	private static Color Mix(Color a, Color b, double t)
	{
		return Color.FromArgb((int)((double)(int)a.R * (1.0 - t) + (double)(int)b.R * t), (int)((double)(int)a.G * (1.0 - t) + (double)(int)b.G * t), (int)((double)(int)a.B * (1.0 - t) + (double)(int)b.B * t));
	}

	public static Color Override(string hex, Color fallback)
	{
		if (!string.IsNullOrEmpty(hex))
		{
			return ColorTranslator.FromHtml(hex);
		}
		return fallback;
	}

	public static Palette From(Appearance a) => From(a, SystemInformation.HighContrast);

    internal static Palette From(Appearance a, bool highContrast)
	{
        if (highContrast) return new Palette {
            Background = SystemColors.Window, Sidebar = SystemColors.Window,
            Surface = SystemColors.Window, Surface2 = SystemColors.Highlight,
            Text = SystemColors.WindowText, Muted = SystemColors.WindowText,
            Line = SystemColors.WindowText, Accent = SystemColors.Highlight,
            AccentText = SystemColors.HighlightText
        };
		Palette palette = new Palette();
		bool flag = a.Theme == "Light";
		Color color = ((a.Theme == "Midnight") ? Color.FromArgb(10, 13, 21) : ((a.Theme == "Ocean") ? Color.FromArgb(12, 29, 40) : ((a.Theme == "Plum") ? Color.FromArgb(28, 21, 39) : Color.FromArgb(19, 23, 33))));
		palette.Background = (flag ? Color.FromArgb(245, 247, 251) : color);
		palette.Sidebar = (flag ? Color.White : Mix(color, Color.Black, 0.2));
		palette.Surface = (flag ? Color.White : Mix(color, Color.White, 0.045));
		palette.Surface2 = (flag ? Color.FromArgb(236, 240, 247) : Mix(color, Color.White, 0.085));
		palette.Accent = AccentColor(a.Accent);
		if (a.Blending == "Harmonized")
		{
			palette.Surface = Mix(palette.Surface, palette.Accent, 0.025);
			palette.Surface2 = Mix(palette.Surface2, palette.Accent, 0.035);
		}
		palette.Text = (flag ? Color.FromArgb(24, 36, 56) : Color.FromArgb(235, 240, 248));
		palette.Muted = (flag ? Color.FromArgb(84, 99, 118) : Color.FromArgb(157, 172, 194));
		palette.Line = (flag ? Color.FromArgb(216, 223, 234) : Color.FromArgb(52, 62, 78));
		if (FishBowlColors.Themes.TryGetValue(a.Theme, out var value))
		{
			palette.Text = Color.FromArgb(value[0]);
			palette.Sidebar = Color.FromArgb(value[1]);
			palette.Background = Color.FromArgb(value[2]);
			palette.Surface = Color.FromArgb(value[3]);
			palette.Muted = Color.FromArgb(value[4]);
			palette.Surface2 = Mix(palette.Surface, palette.Text, 0.055);
			palette.Line = Mix(palette.Surface, palette.Text, 0.18);
		}
        if (a.Theme.StartsWith("Aquarium ", System.StringComparison.Ordinal))
        {
            palette.Background = a.Theme == "Aquarium Midnight" ? Color.FromArgb(12, 14, 22) : a.Theme == "Aquarium Slate" ? Color.FromArgb(27, 35, 43) : Color.FromArgb(9, 21, 29);
            palette.Sidebar = Mix(palette.Background, Color.Black, .18);
            palette.Surface = Color.FromArgb(16, 36, 47);
            palette.Surface2 = Color.FromArgb(25, 47, 61);
            palette.Text = Color.FromArgb(241, 247, 255);
            palette.Muted = Color.FromArgb(160, 184, 195);
            palette.Line = Color.FromArgb(34, 65, 78);
        }
		if (a.CustomPalette)
		{
			palette.Text = Override(a.TextColor, palette.Text);
			palette.Muted = Override(a.MutedColor, palette.Muted);
			palette.Sidebar = Override(a.TopColor, palette.Sidebar);
			palette.Background = Override(a.BottomColor, palette.Background);
			palette.Surface = Override(a.SurfaceColor, palette.Surface);
			palette.Accent = Override(a.AccentColor, palette.Accent);
			palette.Surface2 = Mix(palette.Surface, palette.Text, 0.055);
			palette.Line = Mix(palette.Surface, palette.Text, 0.18);
		}
		palette.AccentText = ((palette.Accent.GetBrightness() < 0.45f) ? Color.White : Color.FromArgb(6, 22, 32));
		return palette;
	}
}
