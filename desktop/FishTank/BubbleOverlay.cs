using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FishTank;

// An owned, non-activating window lets particles leave nested layout containers.
internal sealed class BubbleOverlay : Form
{
    private sealed class Bubble
    {
        public float X, Y, VX, VY, Radius, Phase, Sway;
        public Color Color;
    }

    private readonly List<Bubble> bubbles = new List<Bubble>();
    private readonly Func<bool> canAnimate;
    private readonly Random random = new Random();
    private readonly Timer animation = new Timer { Interval = 30 };
    public int BubbleCount => bubbles.Count;
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            // Layered + transparent + no-activate: clicks and focus stay with the app below.
            parameters.ExStyle |= 0x80000 | 0x20 | 0x08000000 | 0x80;
            return parameters;
        }
    }

    public BubbleOverlay(Func<bool> canAnimate)
    {
        this.canAnimate = canAnimate;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = TransparencyKey = Color.Magenta;
        DoubleBuffered = true;
        animation.Tick += delegate { Advance(); };
    }

    private float Between(float min, float max) => min + (float)random.NextDouble() * (max - min);

    public void Shoot(Form owner, Point origin, Color color)
    {
        Rectangle screen = Screen.FromPoint(origin).Bounds;
        if (Bounds != screen) HideAndClear();
        Bounds = screen;
        int count = random.Next(3, 7);
        for (int i = 0; i < count && bubbles.Count < 40; i++)
        {
            bubbles.Add(new Bubble
            {
                X = origin.X - Left + Between(-9, 9),
                Y = origin.Y - Top + Between(-7, 7),
                VX = (random.Next(2) == 0 ? -1 : 1) * Between(1.4f, 3.2f), VY = Between(-3.8f, -1.8f),
                Radius = Between(2.5f, 5), Phase = Between(0, (float)Math.PI * 2),
                Sway = Between(0.25f, 0.8f), Color = color
            });
        }
        if (!Visible) Show(owner);
        animation.Start();
        Invalidate();
    }

    public void Advance()
    {
        if (!canAnimate() || Owner == null || !Owner.Visible || Owner.WindowState == FormWindowState.Minimized)
        {
            HideAndClear();
            return;
        }
        for (int i = bubbles.Count - 1; i >= 0; i--)
        {
            var bubble = bubbles[i];
            bubble.Phase += 0.08f;
            bubble.X += bubble.VX + (float)Math.Sin(bubble.Phase) * bubble.Sway;
            bubble.Y += bubble.VY;
            bubble.VX *= 0.985f;
            bubble.VY = Math.Max(-5.5f, bubble.VY - 0.015f);
            // Keep the entire circle visible at the sides until it passes the screen top.
            bubble.X = Math.Max(bubble.Radius + 3, Math.Min(Width - bubble.Radius - 3, bubble.X));
            if (bubble.Y + bubble.Radius + 3 < 0) bubbles.RemoveAt(i);
        }
        if (bubbles.Count == 0) HideAndClear();
        else Invalidate();
    }

    public void HideAndClear()
    {
        animation.Stop();
        bubbles.Clear();
        Hide();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        foreach (var bubble in bubbles)
        {
            var circle = new RectangleF(bubble.X - bubble.Radius, bubble.Y - bubble.Radius,
                bubble.Radius * 2, bubble.Radius * 2);
            using var contrast = new Pen(Color.FromArgb(35, 65, 85), 3f);
            using var rim = new Pen(bubble.Color, 1.6f);
            using var highlight = new Pen(Color.White, 1.2f);
            e.Graphics.DrawEllipse(contrast, circle);
            e.Graphics.DrawEllipse(rim, circle);
            e.Graphics.DrawArc(highlight, circle.X + 1.5f, circle.Y + 1.5f,
                circle.Width - 3, circle.Height - 3, 200, 75);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            animation.Dispose();
            bubbles.Clear();
        }
        base.Dispose(disposing);
    }
}
