using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FishTank;

public sealed class BubbleTank : PictureBox
{
	private BubbleOverlay overlay;

	public bool ReducedMotion;

	public bool Shadow;

	public bool Filled;

	public string Frame = "None";

	public Color BubbleColor = Color.Cyan;

	public int BubbleCount => overlay == null || overlay.IsDisposed ? 0 : overlay.BubbleCount;

	public BubbleTank()
	{
		DoubleBuffered = true;
		Cursor = Cursors.Hand;
		base.TabStop = true;
		base.AccessibleName = "FishTank mascot. Click or press Space to shoot bubbles.";
		base.Click += delegate
		{
			Shoot();
		};
		base.KeyDown += delegate(object s, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Return)
			{
				Shoot();
				e.Handled = true;
			}
		};
	}

	public void Shoot()
	{
		if (ReducedMotion || !Visible || FindForm() == null) return;
		if (overlay == null || overlay.IsDisposed) overlay = new BubbleOverlay(() => !ReducedMotion && Visible && !IsDisposed);
		Point origin = PointToScreen(BarrelPoint());
		overlay.Shoot(FindForm(), origin, BubbleColor);
	}

    internal Point BarrelPoint()
    {
        var area = new RectangleF(Padding.Left, Padding.Top, Math.Max(1, ClientSize.Width - Padding.Horizontal), Math.Max(1, ClientSize.Height - Padding.Vertical));
        if (Image != null) {
            if (SizeMode == PictureBoxSizeMode.Zoom) {
                float scale = Math.Min(area.Width / Image.Width, area.Height / Image.Height);
                float width = Image.Width * scale, height = Image.Height * scale;
                area = new RectangleF(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height);
            } else if (SizeMode == PictureBoxSizeMode.CenterImage) area = new RectangleF(area.X + (area.Width - Image.Width) / 2, area.Y + (area.Height - Image.Height) / 2, Image.Width, Image.Height);
            else if (SizeMode != PictureBoxSizeMode.StretchImage) area = new RectangleF(area.Location, Image.Size);
        }
        // Opening of the right-facing barrel in the original artwork, including its transparent padding.
        return Point.Round(new PointF(area.X + area.Width * 0.925f, area.Y + area.Height * 0.48f));
    }

	public void Advance()
	{
		if (ReducedMotion) overlay?.HideAndClear();
		else if (overlay != null && !overlay.IsDisposed) overlay.Advance();
	}

	protected override void OnVisibleChanged(EventArgs e)
	{
		base.OnVisibleChanged(e);
		if (!Visible && overlay != null && !overlay.IsDisposed) overlay.HideAndClear();
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		if (Filled)
		{
			using SolidBrush brush = new SolidBrush(Color.FromArgb(32, BubbleColor));
			using GraphicsPath path = Surface.Rounded(new Rectangle(1, 1, base.Width - 3, base.Height - 3), 14);
			e.Graphics.FillPath(brush, path);
		}
		if (Shadow)
		{
			using SolidBrush brush2 = new SolidBrush(Color.FromArgb(30, Color.Black));
			e.Graphics.FillEllipse(brush2, (float)base.Width * 0.15f, (float)base.Height * 0.72f, (float)base.Width * 0.7f, (float)base.Height * 0.12f);
		}
		base.OnPaint(e);
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		if (Frame != "None")
		{
			using Pen pen = new Pen((Frame == "Accent") ? BubbleColor : Color.FromArgb(90, BubbleColor), 1f);
			using GraphicsPath path2 = Surface.Rounded(new Rectangle(1, 1, base.Width - 3, base.Height - 3), (Frame == "Rounded") ? 14 : 0);
			e.Graphics.DrawPath(pen, path2);
		}

	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			overlay?.Dispose();
		}
		base.Dispose(disposing);
	}
}
