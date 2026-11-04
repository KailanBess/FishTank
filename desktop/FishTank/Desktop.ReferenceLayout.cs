using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace FishTank;

public sealed partial class Desktop
{
    private void AddReferenceSecurityOverview()
    {
        var hero = new Panel { BackColor = palette.Background, Margin = new Padding(0), Height = (int)TextSize(340) };
        var welcome = Label("Welcome to FishTank", 27f, palette.Text); welcome.Font = Face(27f, FontStyle.Bold); welcome.TextAlign = ContentAlignment.MiddleCenter;
        var description = Label("Review your protection, scan local files, and keep recovery close.", 11f, palette.Muted); description.TextAlign = ContentAlignment.TopCenter;
        var start = new ReferenceScanButton { Accent = palette.Accent, BackColor = palette.Background, ForeColor = palette.Text, AccessibleName = "Start full scan", AccessibleDescription = "Request a Microsoft Defender full scan. FishTank asks for confirmation before running.", TabIndex = 0 };
        start.Click += delegate { if (HasRunningOperation) { SetStatus("An operation is already running. Review its progress first."); return; } RequestScan("Full"); };
        var caption = Label("START FULL SCAN", 10f, palette.Text); caption.TextAlign = ContentAlignment.MiddleCenter;
        hero.Controls.Add(welcome); hero.Controls.Add(description); hero.Controls.Add(start); hero.Controls.Add(caption);
        Action arrange = () => { int width = hero.ClientSize.Width; int ring = (int)TextSize(154); welcome.SetBounds(0, 8, width, (int)TextSize(48)); description.SetBounds(Math.Max(0,(width-600)/2),(int)TextSize(61),Math.Min(600,width),(int)TextSize(42)); start.SetBounds((width-ring)/2,(int)TextSize(111),ring,ring); caption.SetBounds(0,(int)TextSize(276),width,(int)TextSize(30)); };
        hero.SizeChanged += delegate { arrange(); }; arrange(); AddBlock(hero, (int)TextSize(330));

        EvidenceSnapshot snapshot = null; try { snapshot = SecurityReviews.Latest("SecurityHealth"); } catch { }
        string state = ProtectionOverview.Status(snapshot, DateTime.UtcNow);
        string observed = snapshot == null ? "No saved assessment. Run a protection check to collect evidence." : "Last checked " + snapshot.CapturedUtc.ToLocalTime().ToString("g");
        var cards = new TableLayoutPanel { ColumnCount = 3, RowCount = 1, BackColor = palette.Background, Margin = new Padding(0), Padding = new Padding(0) };
        for (int i=0;i<3;i++) cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f/3));
        Surface Tile(string title, string value, string detail, string action, Action run)
        {
            var card = Card((int)TextSize(170)); card.Dock = DockStyle.Fill; card.Margin = new Padding(0,0,10,0); card.Padding = new Padding(16);
            var heading = Label(title, 11f, palette.Muted); heading.Dock=DockStyle.Top; heading.Height=(int)TextSize(26);
            var result = Label(value,18f,palette.Text); result.Font=Face(18f,FontStyle.Bold); result.Dock=DockStyle.Top; result.Height=(int)TextSize(36);
            var note = Label(detail,9f,palette.Muted); note.Dock=DockStyle.Fill;
            var button=Button(action,run,false);button.Dock=DockStyle.Bottom;button.Height=(int)TextSize(32);button.Margin=new Padding(0);
            card.Controls.Add(note);card.Controls.Add(button);card.Controls.Add(result);card.Controls.Add(heading);return card;
        }
        cards.Controls.Add(Tile("PROTECTION STATUS", state, observed, "Review protection", () => OpenToolsTab("Security reviews")),0,0);
        cards.Controls.Add(Tile("LIVE DEFENDER ALERTS", liveDetection != null && liveDetection.Enabled ? "Listening" : "Off", "Optional activity alerts from Microsoft Defender.", "Monitoring", () => Navigate("Real-time monitoring")),1,0);
        cards.Controls.Add(Tile("RECOVERY & REPORTS", "Your safety net", "Verify a backup, review recovery guidance or export an edited report.", "Recovery center", OpenRecoveryCenter),2,0);
        cards.SizeChanged += delegate {
            int columns = cards.ClientSize.Width < 700 ? 1 : 3;
            if(cards.ColumnCount==columns)return;
            cards.SuspendLayout(); cards.ColumnCount=columns;cards.RowCount=columns==1?3:1; cards.ColumnStyles.Clear();cards.RowStyles.Clear();
            for(int i=0;i<columns;i++)cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f/columns));
            for(int i=0;i<cards.RowCount;i++)cards.RowStyles.Add(new RowStyle(SizeType.Percent,100f/cards.RowCount));
            var controls=cards.Controls.Cast<Control>().ToArray();for(int i=0;i<controls.Length;i++)cards.SetCellPosition(controls[i],new TableLayoutPanelCellPosition(i%columns,i/columns));
            cards.Height=(int)TextSize(columns==1?490:170); cards.ResumeLayout(true);
        };
        AddBlock(cards,(int)TextSize(170));
        ActionRow(Button("Quick scan", () => RequestScan("Quick"), false), Button("Scan schedule", OpenScanSchedule, false), Button("Finding details", () => OpenToolsTab("Security reviews"), false));
    }
}

public sealed class ReferenceScanButton : Button
{
    public Color Accent = Color.MediumPurple;
    public ReferenceScanButton() { SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true); FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;AccessibleRole=AccessibleRole.PushButton;Cursor=Cursors.Hand;Text="Start full scan"; }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(BackColor);
        float size=Math.Min(Width,Height),cx=Width/2f,cy=Height/2f;
        for(int i=12;i>=1;i--)using(var glow=new Pen(Color.FromArgb(Math.Max(2,18-i),Accent),i+1))g.DrawEllipse(glow,cx-size*.39f,cy-size*.39f,size*.78f,size*.78f);
        using(var ring=new Pen(Accent,2.4f))g.DrawEllipse(ring,cx-size*.39f,cy-size*.39f,size*.78f,size*.78f);
        using(var inner=new Pen(Color.FromArgb(100,Accent),1))g.DrawEllipse(inner,cx-size*.31f,cy-size*.31f,size*.62f,size*.62f);
        using(var power=new Pen(Enabled?ForeColor:Color.Gray,3.2f){StartCap=LineCap.Round,EndCap=LineCap.Round}) {g.DrawArc(power,cx-size*.15f,cy-size*.15f,size*.30f,size*.30f,-50,280);g.DrawLine(power,cx,cy-size*.22f,cx,cy-size*.015f);}
        if(Focused)using(var focus=new Pen(ForeColor,1){DashStyle=DashStyle.Dot})g.DrawEllipse(focus,cx-size*.46f,cy-size*.46f,size*.92f,size*.92f);
    }
}

