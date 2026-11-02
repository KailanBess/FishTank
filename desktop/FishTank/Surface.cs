using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FishTank;

public sealed class Surface : Panel
{
	public int Radius = 16;

	public Color BorderColor = Color.Transparent;

	public Surface()
	{
		DoubleBuffered = true;
		SetStyle(ControlStyles.ResizeRedraw, value: true);
	}

	public static GraphicsPath Rounded(Rectangle r, int radius)
	{
		GraphicsPath graphicsPath = new GraphicsPath();
		int num = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
		if (num < 2)
		{
			graphicsPath.AddRectangle(r);
			return graphicsPath;
		}
		graphicsPath.AddArc(r.X, r.Y, num, num, 180f, 90f);
		graphicsPath.AddArc(r.Right - num, r.Y, num, num, 270f, 90f);
		graphicsPath.AddArc(r.Right - num, r.Bottom - num, num, num, 0f, 90f);
		graphicsPath.AddArc(r.X, r.Bottom - num, num, num, 90f, 90f);
		graphicsPath.CloseFigure();
		return graphicsPath;
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		using (GraphicsPath path = Rounded(new Rectangle(0, 0, base.Width - 1, base.Height - 1), Radius))
		{
			using (SolidBrush brush = new SolidBrush(BackColor))
			{
				e.Graphics.FillPath(brush, path);
			}
			if (BorderColor != Color.Transparent)
			{
				using Pen pen = new Pen(BorderColor);
				e.Graphics.DrawPath(pen, path);
			}
		}
		base.OnPaint(e);
	}

	protected override void OnPaintBackground(PaintEventArgs e)
	{
		if (base.Parent != null)
		{
			using (SolidBrush brush = new SolidBrush((base.Parent.BackColor.A == 0) ? FindForm().BackColor : base.Parent.BackColor))
			{
				e.Graphics.FillRectangle(brush, base.ClientRectangle);
				return;
			}
		}
		base.OnPaintBackground(e);
	}
}
