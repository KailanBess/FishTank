using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FishTank;

public sealed class CleanButton : Button
{
	public Color Fill;

	public Color Hover;

	public Color Ink;

	public Color Border;

	public Color FocusColor;

	public bool StrongFocus;

	public int FocusWidth = 1;

	public int Radius = 10;

	private bool hover;

	public CleanButton()
	{
		base.FlatStyle = FlatStyle.Flat;
		base.FlatAppearance.BorderSize = 0;
		Cursor = Cursors.Hand;
		base.UseVisualStyleBackColor = false;
		SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
	}

	protected override void OnMouseEnter(EventArgs e)
	{
		hover = true;
		Invalidate();
		base.OnMouseEnter(e);
	}

	protected override void OnMouseLeave(EventArgs e)
	{
		hover = false;
		Invalidate();
		base.OnMouseLeave(e);
	}

	protected override void OnGotFocus(EventArgs e)
	{
		Invalidate();
		base.OnGotFocus(e);
	}

	protected override void OnLostFocus(EventArgs e)
	{
		Invalidate();
		base.OnLostFocus(e);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		using GraphicsPath path = Surface.Rounded(new Rectangle(1, 1, base.Width - 3, base.Height - 3), Radius);
		using (SolidBrush brush = new SolidBrush(hover ? Hover : Fill))
		{
			e.Graphics.FillPath(brush, path);
		}
		using (Pen pen = new Pen(Focused ? FocusColor : Border, (!Focused) ? 1 : Math.Max(FocusWidth, (!StrongFocus) ? 1 : 3)))
		{
			e.Graphics.DrawPath(pen, path);
		}
		TextFormatFlags flags = (TextFormatFlags)(0x8824 | ((TextAlign != ContentAlignment.MiddleLeft) ? 1 : 0));
		TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(base.Padding.Left + 10, 0, base.Width - base.Padding.Horizontal - 20, base.Height), base.Enabled ? Ink : SystemColors.GrayText, flags);
	}
}
