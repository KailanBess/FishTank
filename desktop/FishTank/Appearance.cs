using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace FishTank;

public sealed class Appearance
{
	public int Version = 1;

	public bool SimpleExplanationsByDefault;

	public string Theme = "Dark";

	public string Accent = "Cyan";

	public string IconColor = "Original";

	public string FontFamily = "Segoe UI";

	public string Density = "Standard";

	public string Blending = "Harmonized";

	public string Contrast = "Soft";

	public string Shape = "Rounded";

	public string Artwork = "Balanced";

	public int TextScale = 100;

	public bool CustomPalette;

	public bool IconOnlyToolbars;

	public bool ShowFooter = true;

	public bool ArtworkShadow;

	public string TextColor = "";

	public string MutedColor = "";

	public string TopColor = "";

	public string BottomColor = "";

	public string SurfaceColor = "";

	public string AccentColor = "";

	public string SecondaryColor = "";

	public string SelectionColor = "";

	public string FocusColor = "";

	public string ReadyColor = "";

	public string RunningColor = "";

	public string WarningColor = "";

	public int CornerRadius = -1;

	public int FocusWidth = 1;

	public int WallpaperOpacity = 20;

	public string IconStyle = "Outline";

	public string BackgroundStyle = "Plain";

	public string WallpaperPath = "";

	public string WallpaperLayout = "Fill";

	public string ArtworkFrame = "None";

	public string BadgeStyle = "Text";

	public bool ShowBanner = true;

	public bool ShowStatus = true;

	public bool ShowIcons = true;

	public bool ShowDetails = true;

	public bool ReducedMotion;

	public bool StrongFocus;

	public bool AlternatingRows = true;

	public bool StartMaximized;

	public string[] CardOrder = new string[4] { "Antivirus", "System integrity", "Network", "Tailscale" };

	public bool[] CardsVisible = new bool[4] { true, true, true, true };

	public static readonly string[] Themes = new string[5] { "Dark", "Light", "Midnight", "Ocean", "Plum" }.Concat(FishBowlColors.Themes.Keys).Distinct().ToArray();

	public static readonly string[] Accents = new string[6] { "Cyan", "Violet", "Blue", "Pink", "Mint", "Sunset" }.Concat(FishBowlColors.Accents.Keys).Distinct().ToArray();

	public static readonly string[] Fonts = new string[11]
	{
		"Bahnschrift", "Segoe UI", "Arial", "Verdana", "Tahoma", "Calibri", "Trebuchet MS", "Consolas", "Georgia", "Palatino Linotype",
		"Courier New"
	};

	private static void Allowed(string value, string[] choices)
	{
		if (Array.IndexOf(choices, value) < 0)
		{
			throw new InvalidDataException("Unsupported appearance value: " + value);
		}
	}

	public void Validate()
	{
		if (Version != 1)
		{
			throw new InvalidDataException("Unsupported appearance settings version");
		}
		if (CornerRadius < -1 || CornerRadius > 30 || FocusWidth < 1 || FocusWidth > 5 || WallpaperOpacity < 0 || WallpaperOpacity > 100)
		{
			throw new InvalidDataException("Invalid cosmetic range");
		}
		string[] array = new string[12]
		{
			TextColor, MutedColor, TopColor, BottomColor, SurfaceColor, AccentColor, SecondaryColor, SelectionColor, FocusColor, ReadyColor,
			RunningColor, WarningColor
		};
		foreach (string text in array)
		{
			if (text == null || (text != "" && !Regex.IsMatch(text, "\\A#[0-9a-fA-F]{6}\\z")))
			{
				throw new InvalidDataException("Use #RRGGBB colors or leave the field blank.");
			}
		}
		Allowed(IconStyle, new string[3] { "Outline", "Filled", "Monochrome" });
		Allowed(BackgroundStyle, new string[5] { "Plain", "Gradient", "Dots", "Waves", "Wallpaper" });
		Allowed(WallpaperLayout, new string[3] { "Fill", "Fit", "Tile" });
		Allowed(ArtworkFrame, new string[4] { "None", "Thin", "Accent", "Rounded" });
		Allowed(BadgeStyle, new string[3] { "Text", "Pill", "Square" });
		if (WallpaperPath == null || WallpaperPath.Length > 2048)
		{
			throw new InvalidDataException("Invalid wallpaper path");
		}
		if (WallpaperPath != "")
		{
			ScanPaths.Normalize(WallpaperPath);
		}
		Allowed(Theme, Themes);
		Allowed(Accent, Accents);
		Allowed(IconColor, new string[2] { "Original", "Match accent" }.Concat(Accents).ToArray());
		Allowed(FontFamily, Fonts);
		Allowed(Density, new string[3] { "Compact", "Standard", "Roomy" });
		Allowed(Blending, new string[2] { "Harmonized", "Original" });
		Allowed(Contrast, new string[2] { "Soft", "Strong" });
		Allowed(Shape, new string[2] { "Rounded", "Square" });
		Allowed(Artwork, new string[3] { "Compact", "Balanced", "Large" });
		if (TextScale < 90 || TextScale > 150 || TextScale % 5 != 0)
		{
			throw new InvalidDataException("Text size must be 90–150%, in steps of five");
		}
		if (CardOrder == null || CardOrder.Length != 4 || new HashSet<string>(CardOrder).Count != 4 || CardsVisible == null || CardsVisible.Length != 4)
		{
			throw new InvalidDataException("Invalid dashboard layout");
		}
		string[] cardOrder = CardOrder;
		foreach (string value in cardOrder)
		{
			Allowed(value, new string[4] { "Antivirus", "System integrity", "Network", "Tailscale" });
		}
	}

	public Appearance Copy()
	{
		return Core.Parse<Appearance>(Core.Json(this));
	}
}
