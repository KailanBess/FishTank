using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace FishTank;

public sealed class ScenePanel : Panel
{
	public Appearance Appearance;

	public Palette Colors;

	private Image wallpaper;

	public ScenePanel(Appearance appearance, Palette colors)
	{
		Appearance = appearance;
		Colors = colors;
		DoubleBuffered = true;
		SetStyle(ControlStyles.ResizeRedraw, value: true);
		BackColor = colors.Background;
		if (SystemInformation.HighContrast || !(appearance.BackgroundStyle == "Wallpaper") || !(appearance.WallpaperPath != ""))
		{
			return;
		}
		try
		{
			ScanPaths.RefuseReparseAncestors(appearance.WallpaperPath);
			if (new FileInfo(appearance.WallpaperPath).Length > 20971520)
			{
				return;
			}
			using Image image = Image.FromFile(appearance.WallpaperPath);
			if ((long)image.Width * (long)image.Height <= 40000000)
			{
				wallpaper = new Bitmap(image);
			}
		}
		catch
		{
		}
	}

	protected override void OnPaintBackground(PaintEventArgs e)
	{
		base.OnPaintBackground(e);
        if (SystemInformation.HighContrast) return;
		if (base.Width < 1 || base.Height < 1)
		{
			return;
		}
		Color color = Palette.Override(Appearance.SecondaryColor, Colors.Sidebar);
		if (Appearance.BackgroundStyle == "Gradient")
		{
			using LinearGradientBrush brush = new LinearGradientBrush(base.ClientRectangle, Colors.Background, color, 45f);
			e.Graphics.FillRectangle(brush, base.ClientRectangle);
		}
		if (Appearance.BackgroundStyle == "Dots")
		{
			using SolidBrush brush2 = new SolidBrush(Color.FromArgb(45, Colors.Accent));
			for (int i = 12; i < base.Width; i += 24)
			{
				for (int j = 12; j < base.Height; j += 24)
				{
					e.Graphics.FillEllipse(brush2, i, j, 2, 2);
				}
			}
		}
		if (Appearance.BackgroundStyle == "Waves")
		{
			using Pen pen = new Pen(Color.FromArgb(40, Colors.Accent), 1f);
			for (int k = 0; k < base.Height; k += 36)
			{
				List<PointF> list = new List<PointF>();
				for (int l = 0; l < base.Width; l += 8)
				{
					list.Add(new PointF(l, (float)k + (float)Math.Sin((double)l / 60.0) * 12f));
				}
				if (list.Count > 1)
				{
					e.Graphics.DrawLines(pen, list.ToArray());
				}
			}
		}
		if (wallpaper == null)
		{
			return;
		}
		using ImageAttributes imageAttributes = new ImageAttributes();
		ColorMatrix colorMatrix = new ColorMatrix();
		colorMatrix.Matrix33 = (float)Appearance.WallpaperOpacity / 100f;
		imageAttributes.SetColorMatrix(colorMatrix);
		if (Appearance.WallpaperLayout == "Tile")
		{
			for (int m = 0; m < base.Width; m += wallpaper.Width)
			{
				for (int n = 0; n < base.Height; n += wallpaper.Height)
				{
					e.Graphics.DrawImage(wallpaper, new Rectangle(m, n, wallpaper.Width, wallpaper.Height), 0, 0, wallpaper.Width, wallpaper.Height, GraphicsUnit.Pixel, imageAttributes);
				}
			}
		}
		else
		{
			float num = ((Appearance.WallpaperLayout == "Fit") ? Math.Min((float)base.Width / (float)wallpaper.Width, (float)base.Height / (float)wallpaper.Height) : Math.Max((float)base.Width / (float)wallpaper.Width, (float)base.Height / (float)wallpaper.Height));
			int num2 = (int)((float)wallpaper.Width * num);
			int num3 = (int)((float)wallpaper.Height * num);
			e.Graphics.DrawImage(wallpaper, new Rectangle((base.Width - num2) / 2, (base.Height - num3) / 2, num2, num3), 0, 0, wallpaper.Width, wallpaper.Height, GraphicsUnit.Pixel, imageAttributes);
		}
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing && wallpaper != null)
		{
			wallpaper.Dispose();
		}
		base.Dispose(disposing);
	}
}
