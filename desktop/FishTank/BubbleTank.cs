using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FishTank;

public sealed class BubbleTank : PictureBox
{
	private sealed class Bubble
	{
		public float X;

		public float Y;

		public float VX;

		public float VY;

		public float Radius;

		public float Life;
	}

	private readonly List<Bubble> bubbles = new List<Bubble>();

	private readonly Timer animation = new Timer
	{
		Interval = 30
	};

	private int serial;

	public bool ReducedMotion;

	public bool Shadow;

	public bool Filled;

	public string Frame = "None";

	public Color BubbleColor = Color.Cyan;

	public int BubbleCount => bubbles.Count;

	public BubbleTank()
	{
		DoubleBuffered = true;
		Cursor = Cursors.Hand;
		base.TabStop = true;
		base.AccessibleName = "FishTank mascot. Click or press Space to shoot bubbles.";
		Timer timer = animation;
		EventHandler value = delegate
		{
			Advance();
		};
		timer.Tick += value;
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
		if (ReducedMotion)
		{
			return;
		}
		for (int i = 0; i < 8; i++)
		{
			if (bubbles.Count >= 40)
			{
				break;
			}
			float num = (float)(serial++ % 7 - 3) * 0.1f;
			bubbles.Add(new Bubble
			{
				X = (float)base.Width * 0.88f,
				Y = (float)base.Height * 0.48f,
				VX = 1.4f + (float)i * 0.13f,
				VY = -0.5f + num,
				Radius = 2 + i % 4,
				Life = 1f
			});
		}
		animation.Start();
		Invalidate();
	}

	public void Advance()
	{
		for (int num = bubbles.Count - 1; num >= 0; num--)
		{
			Bubble bubble = bubbles[num];
			bubble.X += bubble.VX;
			bubble.Y += bubble.VY;
			bubble.VY -= 0.025f;
			bubble.Life -= 0.025f;
			if (bubble.Life <= 0f || bubble.X > (float)base.Width + bubble.Radius)
			{
				bubbles.RemoveAt(num);
			}
		}
		if (bubbles.Count == 0)
		{
			animation.Stop();
		}
		Invalidate();
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
		foreach (Bubble bubble in bubbles)
		{
			using Pen pen2 = new Pen(Color.FromArgb((int)(bubble.Life * 220f), BubbleColor), 1.3f);
			e.Graphics.DrawEllipse(pen2, bubble.X - bubble.Radius, bubble.Y - bubble.Radius, bubble.Radius * 2f, bubble.Radius * 2f);
		}
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			animation.Stop();
			animation.Dispose();
			bubbles.Clear();
		}
		base.Dispose(disposing);
	}
}
