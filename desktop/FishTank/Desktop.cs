using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FishTank;

public sealed class Desktop : Form
{
	private Appearance appearance;

	private Palette palette;

	private Panel body;

	private Panel nav;

	private FlowLayoutPanel pageFlow;

	private Label status;

	private FolderMonitor monitor = new FolderMonitor();

	private bool busy;

	private bool observing;

	private string current = "Dashboard";

	private string lastScan = "No scan recorded";

	private string recoveryNotice;

	private List<Dictionary<string, object>> health;

	private Image artwork;

	private Image displayArtwork;

	private Icon ownedIcon;

	private string appliedIconKey;

	private readonly ToolTip tips = new ToolTip();

	private static readonly string[] Pages = new string[11]
	{
		"Dashboard", "Antivirus", "Real-time monitoring", "Network Security", "System Integrity", "Security Center", "Quarantine", "Breach Checks", "Password Manager", "Tailscale Devices",
		"Settings"
	};

	private static readonly string[] Glyphs = new string[11]
	{
		"◈", "◎", "◉", "⇄", "◇", "◆", "▣", "◌", "⚿", "⊙",
		"⚙"
	};

	public Appearance CurrentAppearance => appearance.Copy();

	public bool ReturnToSignIn { get; private set; }

	private int Gap
	{
		get
		{
			if (!(appearance.Density == "Compact"))
			{
				if (!(appearance.Density == "Roomy"))
				{
					return 16;
				}
				return 22;
			}
			return 10;
		}
	}

	[DllImport("user32.dll")]
	private static extern bool DestroyIcon(IntPtr h);

	public Desktop()
	{
		appearance = AppearanceStore.Load();
		recoveryNotice = AppearanceStore.Notice;
		Text = "FishTank · Security, clearly.";
		base.Width = 1280;
		base.Height = 880;
		MinimumSize = new Size(1000, 740);
		base.StartPosition = FormStartPosition.CenterScreen;
		base.AutoScaleMode = AutoScaleMode.Dpi;
		DoubleBuffered = true;
		using (Stream stream = typeof(Desktop).Assembly.GetManifestResourceStream("FishTank.png"))
		{
			if (stream != null)
			{
				using Image original = Image.FromStream(stream);
				artwork = new Bitmap(original);
			}
		}
		try
		{
			string text = Core.History();
			string[] array = text.Split('\n');
			foreach (string text2 in array)
			{
				if (text2.Contains("scan-complete"))
				{
					Dictionary<string, object> dictionary = Core.Parse<Dictionary<string, object>>(text2.Trim());
					lastScan = "Last scan · " + Convert.ToString(dictionary["time"]);
				}
			}
		}
		catch
		{
		}
		BuildShell();
		if (appearance.StartMaximized)
		{
			base.WindowState = FormWindowState.Maximized;
		}
		base.FormClosed += delegate
		{
			monitor.Dispose();
			tips.Dispose();
			if (artwork != null)
			{
				artwork.Dispose();
			}
			if (displayArtwork != null)
			{
				displayArtwork.Dispose();
			}
		};
		base.FormClosing += delegate(object s, FormClosingEventArgs e)
		{
			if (busy && !Confirm("An operation is still running. Closing FishTank may leave the Windows operation running without its final application record. Close anyway?"))
			{
				e.Cancel = true;
			}
		};
	}

	private float TextSize(float size)
	{
		return size * (float)appearance.TextScale / 100f;
	}

	private Font Face(float size, FontStyle style)
	{
		return new Font(appearance.FontFamily, TextSize(size), style);
	}

	private Label Label(string text, float size, Color color)
	{
		Label label = new Label();
		label.Text = text;
		label.AutoSize = false;
		label.ForeColor = color;
		label.BackColor = Color.Transparent;
		label.Font = Face(size, FontStyle.Regular);
		label.UseMnemonic = false;
		return label;
	}

	private CleanButton Button(string text, Action action, bool primary)
	{
		CleanButton cleanButton = new CleanButton();
		cleanButton.Text = text;
		cleanButton.Font = Face(10f, FontStyle.Bold);
		cleanButton.Width = Math.Max(135, TextRenderer.MeasureText(text, Face(10f, FontStyle.Bold)).Width + 36);
		cleanButton.Height = ((appearance.Density == "Compact") ? 38 : 46);
		cleanButton.Margin = new Padding(0, 0, 10, 8);
		cleanButton.Fill = (primary ? palette.Accent : palette.Surface2);
		cleanButton.Hover = ((!appearance.ReducedMotion) ? (primary ? ControlPaint.Light(palette.Accent) : ControlPaint.Light(palette.Surface2, 0.1f)) : (primary ? palette.Accent : palette.Surface2));
		cleanButton.Ink = (primary ? palette.AccentText : palette.Text);
		cleanButton.Border = (primary ? palette.Accent : palette.Line);
		cleanButton.FocusColor = Palette.Override(appearance.FocusColor, (appearance.Contrast == "Strong") ? palette.Text : palette.Accent);
		cleanButton.StrongFocus = appearance.StrongFocus;
		cleanButton.FocusWidth = appearance.FocusWidth;
		cleanButton.Radius = ((appearance.CornerRadius >= 0) ? appearance.CornerRadius : ((appearance.Shape == "Rounded") ? 10 : 0));
		CleanButton cleanButton2 = cleanButton;
		cleanButton2.AccessibleName = text;
		cleanButton2.Click += delegate
		{
			if (busy)
			{
				SetStatus("Please wait for the current operation.");
				return;
			}
			try
			{
				action();
			}
			catch (Exception ex)
			{
				ShowMessage("Could not finish", ex.Message);
			}
		};
		return cleanButton2;
	}

	private void SetStatus(string text)
	{
		if (status != null && !status.IsDisposed)
		{
			status.Text = text;
			string text2 = text.ToLowerInvariant();
			status.ForeColor = Palette.Override(text2.Contains("failed") ? appearance.WarningColor : (text2.Contains("working") ? appearance.RunningColor : (text2.Contains("completed") ? appearance.ReadyColor : "")), palette.Muted);
		}
	}

	private void BuildShell()
	{
		SuspendLayout();
		foreach (Control item in new List<Control>(ControlsToList(base.Controls)))
		{
			base.Controls.Remove(item);
			item.Dispose();
		}
		palette = Palette.From(appearance);
		UpdateArtwork();
		BackColor = palette.Background;
		ForeColor = palette.Text;
		Font = Face(10f, FontStyle.Regular);
		nav = new Panel
		{
			Dock = DockStyle.Left,
			Width = ((appearance.TextScale >= 130) ? 250 : 220),
			BackColor = palette.Sidebar,
			Padding = new Padding(16, 22, 14, 16)
		};
		Label label = Label("FishTank", 21f, palette.Text);
		label.Font = Face(21f, FontStyle.Bold);
		label.SetBounds(20, 78, 175, 36);
		label.Height = (int)TextSize(36f);
		label.Width = nav.Width - 40;
		nav.Controls.Add(label);
		Label label2 = Label("SECURITY, CLEARLY.", 8f, palette.Muted);
		label2.SetBounds(22, 116, 180, 25);
		label2.Top = label.Bottom + 3;
		label2.Width = nav.Width - 40;
		nav.Controls.Add(label2);
		if (artwork != null && appearance.ShowIcons)
		{
			BubbleTank bubbleTank = new BubbleTank();
			bubbleTank.ReducedMotion = appearance.ReducedMotion;
			bubbleTank.BubbleColor = palette.Accent;
			bubbleTank.Filled = appearance.IconStyle == "Filled";
			bubbleTank.Frame = appearance.ArtworkFrame;
			bubbleTank.Shadow = appearance.ArtworkShadow;
			bubbleTank.Image = displayArtwork;
			bubbleTank.SizeMode = PictureBoxSizeMode.Zoom;
			bubbleTank.Left = 18;
			bubbleTank.Top = 12;
			bubbleTank.Width = 76;
			bubbleTank.Height = 64;
			bubbleTank.AccessibleName = "FishTank glass aquarium tank";
			BubbleTank value = bubbleTank;
			nav.Controls.Add(value);
		}
		int height = Math.Max(150, label2.Bottom + 18);
		Panel panel = new Panel();
		panel.Dock = DockStyle.Top;
		panel.Height = height;
		panel.BackColor = palette.Sidebar;
		Panel panel2 = panel;
		foreach (Control item2 in ControlsToList(nav.Controls))
		{
			nav.Controls.Remove(item2);
			panel2.Controls.Add(item2);
		}
		nav.Controls.Add(panel2);
		Panel panel3 = new Panel();
		panel3.Dock = DockStyle.Fill;
		panel3.AutoScroll = true;
		panel3.BackColor = palette.Sidebar;
		Panel panel4 = panel3;
		nav.Controls.Add(panel4);
		panel4.BringToFront();
		int num = 0;
		for (int i = 0; i < Pages.Length; i++)
		{
			string page = Pages[i];
			string text = ((page == "Real-time monitoring") ? "Monitoring" : ((page == "Network Security") ? "Network" : ((page == "System Integrity") ? "System integrity" : ((page == "Tailscale Devices") ? "Devices" : ((page == "Password Manager") ? "Passwords" : page)))));
			CleanButton cleanButton = Button((appearance.ShowIcons ? (Glyphs[i] + "   ") : "") + text, delegate
			{
				Navigate(page);
			}, primary: false);
			cleanButton.Tag = page;
			if (appearance.IconOnlyToolbars)
			{
				cleanButton.Text = Glyphs[i];
				cleanButton.AccessibleName = page;
				tips.SetToolTip(cleanButton, page);
			}
			cleanButton.TextAlign = ContentAlignment.MiddleLeft;
			cleanButton.SetBounds(0, num, nav.Width - 52, 44);
			cleanButton.Fill = ((page == current) ? Palette.Override(appearance.SelectionColor, palette.Surface2) : palette.Sidebar);
			cleanButton.Border = ((page == current) ? palette.Line : palette.Sidebar);
			cleanButton.Ink = ((page == current) ? palette.Accent : palette.Muted);
			panel4.Controls.Add(cleanButton);
			num += ((appearance.Density == "Roomy") ? 50 : 46);
		}
		panel4.AutoScrollMinSize = new Size(0, num);
		Label label3 = Label("WINDOWS SECURITY COMPANION\nDevelopment preview · 0.6", 8f, palette.Muted);
		label3.Dock = DockStyle.Bottom;
		label3.Height = (int)TextSize(52f);
		label3.Visible = appearance.ShowFooter;
		nav.Controls.Add(label3);
		body = new ScenePanel(appearance, palette)
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(30, 24, 26, 12),
			BackColor = palette.Background
		};
		status = Label(busy ? "Working…" : "Local by default · Online checks require your consent", 9f, palette.Muted);
		status.Dock = DockStyle.Bottom;
		status.Height = 32;
		status.Visible = appearance.ShowStatus;
		pageFlow = new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			AutoScroll = true,
			FlowDirection = FlowDirection.TopDown,
			WrapContents = false,
			BackColor = palette.Background,
			Padding = new Padding(0, 0, 0, 12)
		};
		if (appearance.BackgroundStyle != "Plain")
		{
			pageFlow.BackColor = Color.Transparent;
		}
		pageFlow.SizeChanged += delegate
		{
			FitPage();
		};
		body.Controls.Add(pageFlow);
		body.Controls.Add(status);
		base.Controls.Add(body);
		base.Controls.Add(nav);
		ApplyAppIcon();
		RenderPage();
		ResumeLayout(performLayout: true);
		FitPage();
	}

	private static List<Control> ControlsToList(Control.ControlCollection controls)
	{
		List<Control> list = new List<Control>();
		foreach (Control control in controls)
		{
			list.Add(control);
		}
		return list;
	}

	private void UpdateArtwork()
	{
		if (displayArtwork != null)
		{
			displayArtwork.Dispose();
			displayArtwork = null;
		}
		if (artwork == null)
		{
			return;
		}
		Bitmap image = new Bitmap(256, 256);
		using (Graphics graphics = Graphics.FromImage(image))
		{
			if (appearance.IconColor == "Original" && appearance.IconStyle != "Monochrome")
			{
				graphics.DrawImage(artwork, 0, 0, 256, 256);
			}
			else
			{
				Color color = ((appearance.IconStyle == "Monochrome") ? palette.Text : Palette.AccentColor((appearance.IconColor == "Match accent") ? appearance.Accent : appearance.IconColor));
				ColorMatrix colorMatrix = new ColorMatrix(new float[5][]
				{
					new float[5]
					{
						0.3f * (float)(int)color.R / 128f,
						0.3f * (float)(int)color.G / 128f,
						0.3f * (float)(int)color.B / 128f,
						0f,
						0f
					},
					new float[5]
					{
						0.59f * (float)(int)color.R / 128f,
						0.59f * (float)(int)color.G / 128f,
						0.59f * (float)(int)color.B / 128f,
						0f,
						0f
					},
					new float[5]
					{
						0.11f * (float)(int)color.R / 128f,
						0.11f * (float)(int)color.G / 128f,
						0.11f * (float)(int)color.B / 128f,
						0f,
						0f
					},
					new float[5] { 0f, 0f, 0f, 1f, 0f },
					new float[5] { 0f, 0f, 0f, 0f, 1f }
				});
				using ImageAttributes imageAttributes = new ImageAttributes();
				imageAttributes.SetColorMatrix(colorMatrix);
				graphics.DrawImage(artwork, new Rectangle(0, 0, 256, 256), 0, 0, artwork.Width, artwork.Height, GraphicsUnit.Pixel, imageAttributes);
			}
		}
		displayArtwork = image;
	}

	private void ApplyAppIcon()
	{
		if (artwork == null)
		{
			return;
		}
		string text = appearance.IconColor + "|" + appearance.IconStyle + "|" + appearance.Accent + "|" + palette.Text.ToArgb();
		if (appliedIconKey == text)
		{
			return;
		}
		appliedIconKey = text;
		using Bitmap bitmap = new Bitmap(64, 64);
		using (Graphics graphics = Graphics.FromImage(bitmap))
		{
			if (appearance.IconColor == "Original" && appearance.IconStyle != "Monochrome")
			{
				graphics.DrawImage(artwork, 0, 0, 64, 64);
			}
			else
			{
				Color color = ((appearance.IconStyle == "Monochrome") ? palette.Text : Palette.AccentColor((appearance.IconColor == "Match accent") ? appearance.Accent : appearance.IconColor));
				ColorMatrix colorMatrix = new ColorMatrix(new float[5][]
				{
					new float[5]
					{
						0.3f * (float)(int)color.R / 128f,
						0.3f * (float)(int)color.G / 128f,
						0.3f * (float)(int)color.B / 128f,
						0f,
						0f
					},
					new float[5]
					{
						0.59f * (float)(int)color.R / 128f,
						0.59f * (float)(int)color.G / 128f,
						0.59f * (float)(int)color.B / 128f,
						0f,
						0f
					},
					new float[5]
					{
						0.11f * (float)(int)color.R / 128f,
						0.11f * (float)(int)color.G / 128f,
						0.11f * (float)(int)color.B / 128f,
						0f,
						0f
					},
					new float[5] { 0f, 0f, 0f, 1f, 0f },
					new float[5] { 0f, 0f, 0f, 0f, 1f }
				});
				using ImageAttributes imageAttributes = new ImageAttributes();
				imageAttributes.SetColorMatrix(colorMatrix);
				graphics.DrawImage(artwork, new Rectangle(0, 0, 64, 64), 0, 0, artwork.Width, artwork.Height, GraphicsUnit.Pixel, imageAttributes);
			}
		}
		IntPtr hicon = bitmap.GetHicon();
		try
		{
			Icon icon = ownedIcon;
			using (Icon icon2 = Icon.FromHandle(hicon))
			{
				ownedIcon = (Icon)icon2.Clone();
			}
			base.Icon = ownedIcon;
			icon?.Dispose();
		}
		finally
		{
			DestroyIcon(hicon);
		}
	}

	private void FitPage()
	{
		if (pageFlow == null)
		{
			return;
		}
		int width = Math.Max(650, pageFlow.ClientSize.Width - 24);
		foreach (Control control in pageFlow.Controls)
		{
			control.Width = width;
		}
	}

	private void Navigate(string page)
	{
		current = page;
		UpdateNavigation(nav);
		pageFlow.SuspendLayout();
		try
		{
			pageFlow.Visible = false;
			RenderPage();
			pageFlow.AutoScrollPosition = Point.Empty;
		}
		finally
		{
			pageFlow.Visible = true;
			pageFlow.ResumeLayout(performLayout: true);
		}
		pageFlow.Invalidate(invalidateChildren: true);
	}

	private void UpdateNavigation(Control parent)
	{
		foreach (Control control in parent.Controls)
		{
			if (control is CleanButton cleanButton && cleanButton.Tag is string)
			{
				bool flag = (string)cleanButton.Tag == current;
				cleanButton.Fill = (flag ? Palette.Override(appearance.SelectionColor, palette.Surface2) : palette.Sidebar);
				cleanButton.Border = (flag ? palette.Line : palette.Sidebar);
				cleanButton.Ink = (flag ? palette.Accent : palette.Muted);
				cleanButton.Invalidate();
			}
			if (control.HasChildren)
			{
				UpdateNavigation(control);
			}
		}
	}

	private void AddBlock(Control c, int height)
	{
		c.Height = height;
		c.Width = Math.Max(650, pageFlow.ClientSize.Width - 24);
		c.Margin = new Padding(0, 0, 0, Gap);
		pageFlow.Controls.Add(c);
	}

	private Surface Card(int height)
	{
		Surface surface = new Surface();
		surface.Height = height;
		surface.BackColor = palette.Surface;
		surface.BorderColor = palette.Line;
		surface.Radius = ((appearance.CornerRadius >= 0) ? appearance.CornerRadius : ((appearance.Shape == "Rounded") ? 16 : 0));
		surface.Padding = new Padding(22);
		return surface;
	}

	private void Header(string title, string subtitle)
	{
		Panel panel = new Panel();
		panel.BackColor = palette.Background;
		Panel panel2 = panel;
		Label label = Label(title, 25f, palette.Text);
		label.Font = Face(25f, FontStyle.Bold);
		label.Dock = DockStyle.Top;
		label.Height = (int)TextSize(43f);
		Label label2 = Label(subtitle, 10f, palette.Muted);
		label2.Dock = DockStyle.Bottom;
		label2.Height = (int)TextSize(30f);
		panel2.Controls.Add(label2);
		panel2.Controls.Add(label);
		AddBlock(panel2, (int)TextSize(85f));
	}

	private void Note(string title, string detail)
	{
		Surface surface = Card((int)TextSize(220f));
		Label label = Label(title, 12f, palette.Text);
		label.Font = Face(12f, FontStyle.Bold);
		label.Dock = DockStyle.Top;
		label.Height = (int)TextSize(34f);
		SimpleInformation simpleInformation = new SimpleInformation(detail, palette.Surface, palette.Muted, Face(10f, FontStyle.Regular));
		simpleInformation.Dock = DockStyle.Fill;
		SimpleInformation value = simpleInformation;
		surface.Controls.Add(value);
		surface.Controls.Add(label);
		AddBlock(surface, (int)TextSize(230f));
	}

	private void ActionRow(params Control[] buttons)
	{
		FlowLayoutPanel row = new FlowLayoutPanel
		{
			BackColor = palette.Background,
			AutoSize = false,
			WrapContents = true
		};
		foreach (Control value in buttons)
		{
			row.Controls.Add(value);
		}
		row.SizeChanged += delegate
		{
			int num = 0;
			int num2 = 1;
			int num3 = 0;
			foreach (Control control in row.Controls)
			{
				int num4 = control.Width + control.Margin.Horizontal;
				if (num > 0 && num + num4 > row.ClientSize.Width)
				{
					num2++;
					num = 0;
				}
				num += num4;
				num3 = Math.Max(num3, control.Height + control.Margin.Vertical);
			}
			int num5 = num2 * num3 + 2;
			if (row.Height != num5)
			{
				row.Height = num5;
			}
		};
		AddBlock(row, 58);
	}

	private void CollectButton(string text, string mode)
	{
		ActionRow(Button(text, delegate
		{
			StartWork(() => Core.Collect(mode), mode);
		}, primary: true));
	}

	private void RenderPage()
	{
		foreach (Control item in ControlsToList(pageFlow.Controls))
		{
			pageFlow.Controls.Remove(item);
			item.Dispose();
		}
		switch (current)
		{
		case "Dashboard":
			Dashboard();
			break;
		case "Antivirus":
		{
			Header("Antivirus", "Real scans, powered by the Microsoft Defender installed on this PC.");
			Surface surface2 = Card(210);
			Label label3 = Label("Choose your scan", 18f, palette.Text);
			label3.Font = Face(18f, FontStyle.Bold);
			label3.Dock = DockStyle.Top;
			label3.Height = 43;
			Label label4 = Label("Start with a quick scan, or choose exactly what to inspect.", 11f, palette.Muted);
			label4.Dock = DockStyle.Top;
			label4.Height = 43;
			FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel();
			flowLayoutPanel.Dock = DockStyle.Bottom;
			flowLayoutPanel.Height = 65;
			flowLayoutPanel.BackColor = palette.Surface;
			FlowLayoutPanel flowLayoutPanel2 = flowLayoutPanel;
			flowLayoutPanel2.Controls.Add(Button("Quick scan", delegate
			{
				RequestScan("Quick");
			}, primary: true));
			flowLayoutPanel2.Controls.Add(Button("Full scan", delegate
			{
				RequestScan("Full");
			}, primary: false));
			flowLayoutPanel2.Controls.Add(Button("Choose file", delegate
			{
				CustomScan(folder: false);
			}, primary: false));
			flowLayoutPanel2.Controls.Add(Button("Choose folder", delegate
			{
				CustomScan(folder: true);
			}, primary: false));
			surface2.Controls.Add(flowLayoutPanel2);
			surface2.Controls.Add(label4);
			surface2.Controls.Add(label3);
			AddBlock(surface2, 210);
			Note("How scanning works", "Quick and full scans follow Defender’s existing remediation policy. Custom scans request no remediation; independent real-time protection can still act.");
			ActionRow(Button("Detailed multi-target scan", delegate
			{
				using ScanDialog scanDialog = new ScanDialog(palette);
				scanDialog.Icon = base.Icon;
				scanDialog.ShowDialog(this);
				if (scanDialog.LastReport != null)
				{
					lastScan = "Last detailed scan · " + scanDialog.LastReport.EndedUtc + " · " + scanDialog.LastReport.State;
				}
			}, primary: true), Button("Detailed reports", delegate
			{
				using ScanHistoryDialog scanHistoryDialog = new ScanHistoryDialog(palette);
				scanHistoryDialog.Icon = base.Icon;
				scanHistoryDialog.ShowDialog(this);
			}, primary: false));
			ActionRow(Button("Scanner readiness / policy", delegate
			{
				StartWork(() => Core.Collect("ScanPolicy"), "ScanPolicy");
			}, primary: false), Button("Scan activity history", delegate
			{
				StartWork(Core.History, "History");
			}, primary: false), Button("Defender events", delegate
			{
				StartWork(() => Core.Collect("Events"), "Events");
			}, primary: false));
			break;
		}
		case "Real-time monitoring":
			Header("Monitoring", "Keep Windows protection visible. Observe a folder when you need more context.");
			Note(observing ? "Folder observer is running" : "Folder observer is off", "The optional observer watches one folder without subfolders. File bursts create low-confidence alerts; this does not block ransomware or replace Defender.");
			ActionRow(Button("Observe a folder", delegate
			{
				string p = PickFolder();
				if (p != null)
				{
					StartWork(delegate
					{
						monitor.Start(p);
						observing = true;
						return "Observing " + p;
					}, "Message");
				}
			}, primary: true), Button("Stop observer", delegate
			{
				monitor.Dispose();
				observing = false;
				RenderPage();
			}, primary: false), Button("Recent events", delegate
			{
				StartWork(() => Core.Collect("Events"), "Events");
			}, primary: false), Button("Activity history", delegate
			{
				StartWork(Core.History, "History");
			}, primary: false));
			break;
		case "Network Security":
			Header("Network", "Understand this PC’s connections, firewall profiles and DNS configuration.");
			Note("Visibility without connection changes", "Read-only inventory. FishTank does not label connections malicious without evidence or add firewall rules automatically.");
			CollectButton("Inspect network", "Network");
			break;
		case "System Integrity":
			Header("System integrity", "Review Windows security configuration and supported repair tools.");
			ActionRow(Button("Check Windows health", delegate
			{
				StartWork(() => Core.Collect("Health"), "Health");
			}, primary: true), Button("Startup & scheduled tasks", delegate
			{
				StartWork(() => Core.Collect("Persistence"), "Persistence");
			}, primary: false), Button("Service health (admin)", delegate
			{
				StartWork(() => GuardService.Query("health"), "Health");
			}, primary: false));
			Note("Repairs stay under your control", "SFC and DISM can modify Windows files. They require your confirmation and Windows administrator approval.");
			ActionRow(Button("Run SFC repair", delegate
			{
				Repair(dism: false);
			}, primary: false), Button("Run DISM repair", delegate
			{
				Repair(dism: true);
			}, primary: false));
			break;
		case "Quarantine":
			Header("Quarantine", "Encrypted recovery copies. Every removal and restoration stays in your control.");
			Note("Manual review, safe recovery", "Files are encrypted with your Windows identity before source removal. Restoring can reintroduce a threat. Existing files are never overwritten.");
			ActionRow(Button("Review vault", delegate
			{
				StartWork(Vault.List, "Vault");
			}, primary: true), Button("Quarantine a file", delegate
			{
				string p = PickFile();
				if (p != null && Confirm("Encrypt a recovery copy and remove this source file?\n\n" + p + "\n\nThis is your manual decision, not a malware verdict."))
				{
					StartWork(() => Core.Json(Vault.Put(p, "Explicit FishTank user request")), "Message");
				}
			}, primary: false), Button("Restore entry", RestoreEntry, primary: false), Button("Delete recovery copy", DeleteEntry, primary: false));
			break;
		case "Tailscale Devices":
			Header("Devices", "Your own devices. Your existing Tailscale network.");
			Note("Remote management is opt-in", "Discovery uses Tailscale’s supported status interface. FishTank does not enroll devices, change VPN or DNS settings, or store Tailscale login keys.");
			ActionRow(Button("Discover devices", delegate
			{
				StartWork(() => Core.Collect("Tailscale"), "Tailscale");
			}, primary: true), Button("Remote setup guide", delegate
			{
				Process.Start(new ProcessStartInfo(Path.Combine(Core.Base, "docs", "REMOTE.md"))
				{
					UseShellExecute = true
				});
			}, primary: false));
			break;
		case "Security Center":
			Header("Security center", "Review hardening, account exposure, browser permissions and file changes.");
			Note("Review first, change deliberately", "These local checks can identify settings that deserve attention. Unavailable checks remain unknown. FishTank does not silently enable, disable or repair Windows security settings.");
			ActionRow(Button("Windows hardening", delegate
			{
				StartWork(() => Core.Collect("Hardening"), "Hardening");
			}, primary: true), Button("Accounts & remote access", delegate
			{
				StartWork(() => Core.Collect("Accounts"), "Accounts");
			}, primary: false), Button("Browser permissions", delegate
			{
				StartWork(() => Core.Collect("Browsers"), "Browsers");
			}, primary: false));
			Note("File integrity baselines", "Choose a known-good file to remember its SHA-256, then compare it later. A changed file may be a legitimate update; an unchanged file is not proof of safety.");
			ActionRow(Button("Remember a file", delegate
			{
				string file = PickFile();
				if (file != null && Confirm("Record or replace the local integrity baseline for this file?\n\n" + file + "\n\nOnly baseline a file you currently trust."))
				{
					StartWork(delegate
					{
						IntegrityBaseline.Capture(file);
						return "Baseline captured locally. No file was uploaded or modified.";
					}, "Message");
				}
			}, primary: false), Button("Compare remembered files", delegate
			{
				StartWork(IntegrityBaseline.Compare, "Baseline");
			}, primary: false));
			break;
		case "Breach Checks":
			Header("Breach checks", "Find known email and password exposure, with consent for each lookup.");
			Note("Know what leaves this PC", "Email checks send the full address and your API key to Have I Been Pwned. Password checks send only a five-character hash prefix. Queries, keys and results are not saved by FishTank.");
			ActionRow(Button("Open breach checks", delegate
			{
				using BreachDialog breachDialog = new BreachDialog(palette);
				breachDialog.Icon = base.Icon;
				breachDialog.ShowDialog(this);
			}, primary: true));
			Note("Coverage has limits", "A result can help you act on known breaches. No service can prove information has never leaked or search every private breach. Email lookup requires your own HIBP API key.");
			break;
		case "Password Manager":
			Header("Passwords", "Store credentials locally and generate distinct passwords for each account.");
			Note("A master-passphrase vault", "The encrypted vault stores names, usernames, passwords, sites and notes. It locks after five minutes of inactivity or when Windows locks. The master passphrase cannot be reset if forgotten.");
			ActionRow(Button("Open password manager", delegate
			{
				using PasswordManagerDialog passwordManagerDialog = new PasswordManagerDialog(palette);
				passwordManagerDialog.Icon = base.Icon;
				passwordManagerDialog.ShowDialog(this);
			}, primary: true));
			Note("Choose your password length", "Generate 1–65,536 characters, subject to the selected character sets. Choose uppercase, lowercase, digits, symbols and lookalike exclusions. Generation works offline; websites may impose smaller limits.");
			break;
		case "Settings":
		{
			Header("Settings", "Make FishTank feel like yours.");
			Surface surface = Card(195);
			Label label = Label("Appearance & accessibility", 18f, palette.Text);
			label.Font = Face(18f, FontStyle.Bold);
			label.Dock = DockStyle.Top;
			label.Height = 42;
			Label label2 = Label("Themes, accents, icon colors, fonts, spacing and dashboard layout.\nPreview changes live, then save or revert.", 11f, palette.Muted);
			label2.Dock = DockStyle.Top;
			label2.Height = 70;
			CleanButton cleanButton = Button("Customize FishTank", Customize, primary: true);
			cleanButton.Dock = DockStyle.Bottom;
			surface.Controls.Add(cleanButton);
			surface.Controls.Add(label2);
			surface.Controls.Add(label);
			AddBlock(surface, 210);
			Note(AppAccess.Enabled ? "App password is enabled" : "App password is off", "Require a separate password to open this data profile through the desktop or interactive CLI. The password vault still needs its own master passphrase. The background service stays independent.");
			ActionRow(Button(AppAccess.Enabled ? "Change app password" : "Set app password", delegate
			{
				AppAccessSettings.Change(this);
				RenderPage();
			}, primary: true), Button("Remove app password", delegate
			{
				AppAccessSettings.Remove(this);
				RenderPage();
			}, primary: false), Button("Lock and return to sign in", delegate
			{
				if (!AppAccess.Enabled)
				{
					ShowMessage("Set a password first", "Enable an app password before locking access.");
				}
				else
				{
					AppAccess.Lock();
					ReturnToSignIn = true;
					Close();
				}
			}, primary: false));
			MfaStatus mfa = AppAccess.Mfa;
			Note("Optional multifactor authentication", (!mfa.Configured) ? "Authenticator MFA is not enrolled. Add an external authenticator and one-use recovery codes, then choose whether to require them at sign-in." : ("Authenticator enrolled · Sign-in MFA " + (mfa.AskOnSignIn ? "on" : "off (password only)") + " · Recovery codes remaining: " + mfa.RecoveryCodesRemaining + ". Security changes still require MFA verification."));
			ActionRow(Button("Multifactor authentication", delegate
			{
				using (MfaSettingsDialog mfaSettingsDialog = new MfaSettingsDialog(palette))
				{
					mfaSettingsDialog.Icon = base.Icon;
					mfaSettingsDialog.ShowDialog(this);
				}
				RenderPage();
			}, primary: true));
			Note("Protection preferences", "Defender controls antivirus exclusions, scan schedules and engine behavior. FishTank appearance settings never change Windows security settings.");
			ActionRow(Button("Update Defender signatures", delegate
			{
				if (Confirm("Ask Defender to download signature updates using its current policy?"))
				{
					StartWork(Core.UpdateSignatures, "Message");
				}
			}, primary: false));
			Note("Local storage", Core.Data + "\nBreach checks contact external services only with consent. Password vaults are local and excluded from remote management. Service installation remains opt-in.");
			break;
		}
		}
		FitPage();
	}

	private void Dashboard()
	{
		Header("Your security, at a glance", "A calm place to check this PC and decide what needs attention.");
		if (appearance.ShowBanner)
		{
			Surface surface = Card(220);
			Panel panel = new Panel();
			panel.Dock = DockStyle.Fill;
			panel.BackColor = palette.Surface;
			Panel panel2 = panel;
			Label label = Label("YOUR WINDOWS COMPANION", 8f, palette.Accent);
			label.Dock = DockStyle.Top;
			label.Height = 24;
			Label label2 = Label((health == null) ? "Let’s check your device." : "Your latest checks are ready.", 23f, palette.Text);
			label2.Font = Face(23f, FontStyle.Bold);
			label2.Dock = DockStyle.Top;
			label2.Height = (int)TextSize(50f);
			Label label3 = Label((health == null) ? "Get a clear view of Windows protection, network settings and device health." : "Review collected evidence below. Unavailable checks remain unknown.", 11f, palette.Muted);
			label3.Dock = DockStyle.Top;
			label3.Height = (int)TextSize(48f);
			CleanButton cleanButton = Button((health == null) ? "Check this device" : "Refresh checks", delegate
			{
				StartWork(() => Core.Collect("Health"), "Health");
			}, primary: true);
			cleanButton.Dock = DockStyle.Bottom;
			cleanButton.Height = 46;
			Panel panel3 = new Panel();
			panel3.Dock = DockStyle.Bottom;
			panel3.Height = 48;
			panel3.BackColor = palette.Surface;
			Panel panel4 = panel3;
			panel4.Controls.Add(cleanButton);
			cleanButton.Dock = DockStyle.Left;
			cleanButton.Width = Math.Max(190, TextRenderer.MeasureText(cleanButton.Text, cleanButton.Font).Width + 40);
			panel2.Controls.Add(panel4);
			panel2.Controls.Add(label3);
			panel2.Controls.Add(label2);
			panel2.Controls.Add(label);
			surface.Controls.Add(panel2);
			if (artwork != null && appearance.ShowIcons)
			{
				BubbleTank bubbleTank = new BubbleTank();
				bubbleTank.ReducedMotion = appearance.ReducedMotion;
				bubbleTank.BubbleColor = palette.Accent;
				bubbleTank.Filled = appearance.IconStyle == "Filled";
				bubbleTank.Frame = appearance.ArtworkFrame;
				bubbleTank.Shadow = appearance.ArtworkShadow;
				bubbleTank.Image = displayArtwork;
				bubbleTank.SizeMode = PictureBoxSizeMode.Zoom;
				bubbleTank.Dock = DockStyle.Right;
				bubbleTank.Width = ((appearance.Artwork == "Compact") ? 150 : ((appearance.Artwork == "Large") ? 280 : 220));
				bubbleTank.BackColor = palette.Surface;
				bubbleTank.AccessibleName = "FishTank aquarium hull and glass barrel";
				BubbleTank value = bubbleTank;
				surface.Controls.Add(value);
				panel2.BringToFront();
			}
			AddBlock(surface, (int)TextSize(235f));
		}
		else
		{
			ActionRow(Button("Check this device", delegate
			{
				StartWork(() => Core.Collect("Health"), "Health");
			}, primary: true));
		}
		int num = 0;
		if (health != null)
		{
			foreach (Dictionary<string, object> item in health)
			{
				if (Convert.ToString(item["State"]) == "Unknown")
				{
					num++;
				}
			}
		}
		Label label4 = Label((health == null) ? "PROTECTION OVERVIEW · NOT YET ASSESSED" : ("LATEST CHECKS · " + num + " UNAVAILABLE OR ACCESS-RESTRICTED"), 9f, palette.Muted);
		if (appearance.BadgeStyle != "Text")
		{
			label4.BackColor = palette.Surface2;
			label4.Padding = new Padding(8, 3, 0, 0);
			label4.BorderStyle = ((appearance.BadgeStyle == "Square") ? BorderStyle.FixedSingle : BorderStyle.None);
		}
		AddBlock(label4, 25);
		int num2 = 0;
		bool[] cardsVisible = appearance.CardsVisible;
		for (int num3 = 0; num3 < cardsVisible.Length; num3++)
		{
			if (cardsVisible[num3])
			{
				num2++;
			}
		}
		int num4 = 0;
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel();
		tableLayoutPanel.ColumnCount = 2;
		tableLayoutPanel.RowCount = Math.Max(1, (num2 + 1) / 2);
		tableLayoutPanel.BackColor = palette.Background;
		tableLayoutPanel.Margin = new Padding(0);
		TableLayoutPanel tableLayoutPanel2 = tableLayoutPanel;
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		for (int num5 = 0; num5 < tableLayoutPanel2.RowCount; num5++)
		{
			tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / (float)tableLayoutPanel2.RowCount));
		}
		for (int num6 = 0; num6 < appearance.CardOrder.Length; num6++)
		{
			if (appearance.CardsVisible[num6])
			{
				string name = appearance.CardOrder[num6];
				Surface surface2 = Card(150);
				surface2.Dock = DockStyle.Fill;
				surface2.Margin = new Padding((num4 % 2 != 0) ? (Gap / 2) : 0, 0, (num4 % 2 == 0) ? (Gap / 2) : 0, Gap);
				surface2.Padding = new Padding(18);
				Label label5 = Label(name, 14f, palette.Text);
				label5.Font = Face(14f, FontStyle.Bold);
				label5.Dock = DockStyle.Top;
				label5.Height = 33;
				Label label6 = Label((name == "Antivirus") ? "Defender scans, with results you can review." : ((!(name == "System integrity")) ? ((name == "Network") ? "Inspect connections, DNS and firewall." : "Discover your existing Tailscale devices.") : ((health == null) ? "Windows health has not been checked." : ("Evidence collected · " + num + " unknown checks."))), 10f, palette.Muted);
				label6.Dock = DockStyle.Fill;
				CleanButton cleanButton2 = Button((name == "Antivirus") ? "Open scans" : "Review", delegate
				{
					Navigate((name == "Antivirus") ? "Antivirus" : ((name == "System integrity") ? "System Integrity" : ((name == "Network") ? "Network Security" : "Tailscale Devices")));
				}, primary: false);
				cleanButton2.Dock = DockStyle.Bottom;
				cleanButton2.Height = 36;
				surface2.Controls.Add(label6);
				surface2.Controls.Add(cleanButton2);
				surface2.Controls.Add(label5);
				tableLayoutPanel2.Controls.Add(surface2, num4 % 2, num4 / 2);
				num4++;
			}
		}
		if (num2 > 0)
		{
			AddBlock(tableLayoutPanel2, (int)TextSize((num2 <= 2) ? 140 : 280));
		}
		else
		{
			tableLayoutPanel2.Dispose();
		}
		Label c = Label(lastScan, 10f, palette.Muted);
		AddBlock(c, (int)TextSize(25f));
		if (!string.IsNullOrEmpty(recoveryNotice))
		{
			Note("Appearance settings", recoveryNotice);
		}
	}

	private async void StartWork(Func<string> action, string kind)
	{
		if (busy)
		{
			return;
		}
		busy = true;
		SetStatus("Working… You can keep browsing. Some Windows checks require administrator access.");
		try
		{
			int num = default(int);
			_ = num;
			_ = 0;
			try
			{
				string value = await Task.Run(action);
				if (base.IsDisposed)
				{
					return;
				}
				if (kind == "Health")
				{
					try
					{
						health = Core.Parse<List<Dictionary<string, object>>>(value);
					}
					catch
					{
					}
				}
				if (kind == "Scan")
				{
					lastScan = "Last scan · " + DateTime.Now.ToString("g");
				}
				RenderPage();
				RenderResult(value, kind);
				SetStatus("Completed · Review results and any unknown checks.");
			}
			catch (Exception ex)
			{
				if (!base.IsDisposed)
				{
					ShowMessage("Could not complete the operation", ex.Message);
					SetStatus("Operation failed. No security verdict inferred.");
				}
			}
		}
		finally
		{
			busy = false;
		}
	}

	private void ShowMessage(string title, string text)
	{
		Note(title, text);
		FitPage();
	}

	private static string Get(Dictionary<string, object> item, string key)
	{
		if (!item.ContainsKey(key) || item[key] == null)
		{
			return "";
		}
		return Convert.ToString(item[key]);
	}

	private void RenderResult(string value, string kind)
	{
		if (kind == "Message")
		{
			try
			{
				Dictionary<string, object> value2 = Core.Parse<Dictionary<string, object>>(value);
				value = Pretty(value2);
			}
			catch
			{
			}
			Note("Result", value);
			return;
		}
		Label c = Label((kind == "Scan") ? "SCAN RESULT" : "COLLECTED EVIDENCE", 10f, palette.Accent);
		AddBlock(c, 28);
		if (kind == "Scan")
		{
			Dictionary<string, object> item = Core.Parse<Dictionary<string, object>>(value);
			Note("Defender returned code " + Get(item, "exitCode"), Get(item, "interpretation"));
			Detail(Get(item, "output") + "\n" + Get(item, "error"));
			return;
		}
		if (kind == "Tailscale")
		{
			RenderTailscale(value);
			return;
		}
		List<string[]> list = new List<string[]>();
		try
		{
			if (kind == "History")
			{
				string[] array = value.Split('\n');
				foreach (string text in array)
				{
					if (!string.IsNullOrWhiteSpace(text))
					{
						try
						{
							Dictionary<string, object> dictionary = Core.Parse<Dictionary<string, object>>(text.Trim());
							list.Add(new string[3]
							{
								Get(dictionary, "time"),
								Get(dictionary, "action"),
								Pretty(dictionary.ContainsKey("detail") ? dictionary["detail"] : null)
							});
						}
						catch
						{
						}
					}
				}
			}
			else if (kind == "Vault")
			{
				foreach (Dictionary<string, object> item4 in Core.Parse<List<Dictionary<string, object>>>(value))
				{
					list.Add(new string[3]
					{
						(Get(item4, "Id") != "") ? Get(item4, "Id") : Get(item4, "id"),
						(Get(item4, "Reason") != "") ? Get(item4, "Reason") : Get(item4, "error"),
						Get(item4, "Original") + "\nSHA-256: " + Get(item4, "Sha256") + "\nCreated: " + Get(item4, "Created")
					});
				}
			}
			else
			{
				foreach (Dictionary<string, object> item5 in Core.Parse<List<Dictionary<string, object>>>(value))
				{
					list.Add(new string[3]
					{
						Get(item5, "Name"),
						Get(item5, "State"),
						Get(item5, "Value")
					});
				}
			}
		}
		catch
		{
			Detail(value);
			return;
		}
		if (list.Count == 0)
		{
			Note("Nothing recorded yet", "No matching records are available. This does not indicate a clean device.");
		}
		else
		{
			EvidenceTable(list, (kind == "History") ? "Time" : ((kind == "Vault") ? "Entry ID" : "Check"), (kind == "History") ? "Activity" : ((kind == "Vault") ? "Reason" : "Result"));
		}
	}

	private static string Pretty(object value)
	{
		if (value == null)
		{
			return "";
		}
		if (value is Dictionary<string, object> dictionary)
		{
			StringBuilder stringBuilder = new StringBuilder();
			foreach (KeyValuePair<string, object> item in dictionary)
			{
				stringBuilder.AppendLine(item.Key + ": " + Pretty(item.Value));
			}
			return stringBuilder.ToString();
		}
		if (value is IEnumerable enumerable && !(value is string))
		{
			List<string> list = new List<string>();
			foreach (object item2 in enumerable)
			{
				list.Add(Pretty(item2));
			}
			return string.Join(", ", list.ToArray());
		}
		return Convert.ToString(value);
	}

	private void Detail(string text)
	{
		TextBox textBox = new TextBox();
		textBox.Multiline = true;
		textBox.ReadOnly = true;
		textBox.ScrollBars = ScrollBars.Both;
		textBox.WordWrap = false;
		textBox.Text = text;
		textBox.BackColor = palette.Surface;
		textBox.ForeColor = palette.Text;
		textBox.Font = Face(10f, FontStyle.Regular);
		textBox.BorderStyle = BorderStyle.FixedSingle;
		textBox.AccessibleName = "Full evidence details";
		TextBox c = textBox;
		AddBlock(c, 240);
	}

	private void EvidenceTable(List<string[]> rows, string first, string second)
	{
		Surface surface = Card(390);
		surface.Padding = new Padding(12);
		DataGridView table = new DataGridView
		{
			Dock = DockStyle.Top,
			Height = 210,
			ReadOnly = true,
			AllowUserToAddRows = false,
			AllowUserToDeleteRows = false,
			AllowUserToResizeRows = false,
			RowHeadersVisible = false,
			SelectionMode = DataGridViewSelectionMode.FullRowSelect,
			MultiSelect = false,
			BackgroundColor = palette.Surface,
			BorderStyle = BorderStyle.None,
			GridColor = palette.Line,
			EnableHeadersVisualStyles = false,
			AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
			AccessibleName = "Evidence records"
		};
		table.ColumnHeadersDefaultCellStyle.BackColor = palette.Surface2;
		table.ColumnHeadersDefaultCellStyle.ForeColor = palette.Text;
		table.ColumnHeadersDefaultCellStyle.Font = Face(10f, FontStyle.Bold);
		table.ColumnHeadersHeight = 40;
		table.DefaultCellStyle.BackColor = palette.Surface;
		table.DefaultCellStyle.ForeColor = palette.Text;
		table.DefaultCellStyle.Font = Face(10f, FontStyle.Regular);
		table.DefaultCellStyle.SelectionBackColor = palette.Surface2;
		table.DefaultCellStyle.SelectionForeColor = palette.Text;
		table.RowTemplate.Height = ((appearance.Density == "Compact") ? 32 : ((appearance.Density == "Roomy") ? 48 : 40));
		if (appearance.AlternatingRows)
		{
			table.AlternatingRowsDefaultCellStyle.BackColor = palette.Surface2;
		}
		table.Columns.Add("item", first);
		table.Columns.Add("state", second);
		table.Columns[0].FillWeight = 65f;
		table.Columns[1].FillWeight = 35f;
		TextBox details = new TextBox
		{
			Dock = DockStyle.Fill,
			ReadOnly = true,
			Multiline = true,
			ScrollBars = ScrollBars.Vertical,
			BackColor = palette.Surface,
			ForeColor = palette.Muted,
			BorderStyle = BorderStyle.None,
			Font = Face(10f, FontStyle.Regular),
			AccessibleName = "Selected evidence details",
			Visible = appearance.ShowDetails
		};
		foreach (string[] row in rows)
		{
			int index = table.Rows.Add(row[0], row[1]);
			table.Rows[index].Tag = row[2];
		}
		table.SelectionChanged += delegate
		{
			if (table.SelectedRows.Count > 0)
			{
				details.Text = Convert.ToString(table.SelectedRows[0].Tag);
			}
		};
		table.CellDoubleClick += delegate(object s, DataGridViewCellEventArgs e)
		{
			if (e.RowIndex >= 0)
			{
				ExplainedMessageBox.Show(this, Convert.ToString(table.Rows[e.RowIndex].Tag), "Evidence details");
			}
		};
		details.Text = rows[0][2];
		surface.Controls.Add(details);
		surface.Controls.Add(table);
		AddBlock(surface, appearance.ShowDetails ? 410 : 250);
	}

	private void RenderTailscale(string value)
	{
		List<Dictionary<string, object>> list = Core.Parse<List<Dictionary<string, object>>>(value);
		List<string[]> list2 = new List<string[]>();
		foreach (Dictionary<string, object> item in list)
		{
			string text = Get(item, "Value");
			try
			{
				Dictionary<string, object> dictionary = Core.Parse<Dictionary<string, object>>(text);
				string text2 = Get(dictionary, "BackendState");
				Note("Tailscale · " + text2, "Connection state comes from the installed Tailscale client. Peer visibility does not grant FishTank management permission.");
				Dictionary<string, object> dictionary2 = (dictionary.ContainsKey("Self") ? (dictionary["Self"] as Dictionary<string, object>) : null);
				if (dictionary2 != null)
				{
					list2.Add(new string[3]
					{
						Get(dictionary2, "HostName") + " (this device)",
						text2,
						Pretty(dictionary2)
					});
				}
				Dictionary<string, object> dictionary3 = (dictionary.ContainsKey("Peer") ? (dictionary["Peer"] as Dictionary<string, object>) : null);
				if (dictionary3 == null)
				{
					continue;
				}
				foreach (KeyValuePair<string, object> item2 in dictionary3)
				{
					if (item2.Value is Dictionary<string, object> dictionary4)
					{
						list2.Add(new string[3]
						{
							Get(dictionary4, "HostName"),
							(Get(dictionary4, "Online") == "True") ? "Online" : "Offline / unknown",
							Pretty(dictionary4)
						});
					}
				}
			}
			catch
			{
				list2.Add(new string[3]
				{
					Get(item, "Name"),
					Get(item, "State"),
					text
				});
			}
		}
		EvidenceTable(list2, "Device / check", "Status");
	}

	private bool Confirm(string text)
	{
		return ExplainedMessageBox.Show(this, text, "Confirm FishTank action", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) == DialogResult.Yes;
	}

	private string PickFile()
	{
		using OpenFileDialog openFileDialog = new OpenFileDialog();
		return (openFileDialog.ShowDialog(this) == DialogResult.OK) ? openFileDialog.FileName : null;
	}

	private string PickFolder()
	{
		using FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog();
		return (folderBrowserDialog.ShowDialog(this) == DialogResult.OK) ? folderBrowserDialog.SelectedPath : null;
	}

	private void RequestScan(string kind)
	{
		if (Confirm("Run a " + kind.ToLowerInvariant() + " Defender scan?\n\nExisting Defender policy may quarantine or remove detected threats."))
		{
			StartWork(() => Core.Scan(kind, null), "Scan");
		}
	}

	private void CustomScan(bool folder)
	{
		string path = (folder ? PickFolder() : PickFile());
		if (path != null)
		{
			StartWork(() => Core.Scan("Custom", path), "Scan");
		}
	}

	private void Repair(bool dism)
	{
		if (Confirm(dism ? "Run DISM RestoreHealth? This modifies the Windows image and may contact Windows Update." : "Run SFC /scannow? This may repair Windows system files."))
		{
			ProcessStartInfo processStartInfo = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), dism ? "dism.exe" : "sfc.exe"), dism ? "/Online /Cleanup-Image /RestoreHealth" : "/scannow");
			processStartInfo.UseShellExecute = true;
			processStartInfo.Verb = "runas";
			Process.Start(processStartInfo);
		}
	}

	private string AskId()
	{
		Form d = new Form
		{
			Text = "Vault entry ID",
			Width = 450,
			Height = 180,
			StartPosition = FormStartPosition.CenterParent,
			BackColor = palette.Background,
			ForeColor = palette.Text,
			Padding = new Padding(20)
		};
		try
		{
			TextBox textBox = new TextBox();
			textBox.Dock = DockStyle.Top;
			textBox.Font = Face(11f, FontStyle.Regular);
			textBox.AccessibleName = "Vault entry ID";
			TextBox textBox2 = textBox;
			CleanButton cleanButton = Button("Continue", delegate
			{
				d.DialogResult = DialogResult.OK;
				d.Close();
			}, primary: true);
			cleanButton.Dock = DockStyle.Bottom;
			d.Controls.Add(textBox2);
			d.Controls.Add(cleanButton);
			d.AcceptButton = cleanButton;
			return (d.ShowDialog(this) == DialogResult.OK) ? textBox2.Text.Trim() : null;
		}
		finally
		{
			if (d != null)
			{
				((IDisposable)d).Dispose();
			}
		}
	}

	private void RestoreEntry()
	{
		string id = AskId();
		if (id == null)
		{
			return;
		}
		SaveFileDialog saveFileDialog = new SaveFileDialog();
		saveFileDialog.Title = "Restore into a new file inside your profile";
		using SaveFileDialog saveFileDialog2 = saveFileDialog;
		if (saveFileDialog2.ShowDialog(this) == DialogResult.OK && Confirm("Restore this entry? This can reintroduce a threat. A recovery copy will be retained."))
		{
			string path = saveFileDialog2.FileName;
			StartWork(delegate
			{
				Vault.Restore(id, path);
				return "Restored. Encrypted recovery copy retained.";
			}, "Message");
		}
	}

	private void DeleteEntry()
	{
		string id = AskId();
		if (id != null && Confirm("Permanently delete the encrypted recovery copy for " + id + "?"))
		{
			StartWork(delegate
			{
				Vault.Delete(id);
				return "Recovery copy deleted.";
			}, "Message");
		}
	}

	private void Customize()
	{
		Appearance appearance = this.appearance.Copy();
		Appearance value = this.appearance.Copy();
		Action<Appearance> preview = delegate(Appearance appearance2)
		{
			this.appearance = appearance2.Copy();
			BuildShell();
		};
		using (AppearanceDialog appearanceDialog = new AppearanceDialog(value, preview))
		{
			if (appearanceDialog.ShowDialog(this) == DialogResult.OK)
			{
				try
				{
					appearanceDialog.Value.SimpleExplanationsByDefault = SimpleExplanations.DefaultSimple;
					AppearanceStore.Save(appearanceDialog.Value);
					this.appearance = appearanceDialog.Value.Copy();
					recoveryNotice = "";
				}
				catch
				{
					this.appearance = appearance;
					BuildShell();
					throw;
				}
			}
			else
			{
				this.appearance = appearance;
			}
		}
		BuildShell();
	}

	public void RenderPreview(string path, string page, Appearance theme)
	{
		if (theme != null)
		{
			theme.Validate();
			appearance = theme.Copy();
		}
		current = page;
		BuildShell();
		bool visible = base.Visible;
		Point location = base.Location;
		bool showInTaskbar = base.ShowInTaskbar;
		try
		{
			if (!visible)
			{
				base.ShowInTaskbar = false;
				base.StartPosition = FormStartPosition.Manual;
				base.Location = new Point(-30000, -30000);
				Show();
			}
			PerformLayout();
			Application.DoEvents();
			FitPage();
			Application.DoEvents();
			using Bitmap bitmap = new Bitmap(base.Width, base.Height);
			DrawToBitmap(bitmap, new Rectangle(0, 0, base.Width, base.Height));
			bitmap.Save(path, ImageFormat.Png);
		}
		finally
		{
			if (!visible)
			{
				Hide();
			}
			base.Location = location;
			base.ShowInTaskbar = showInTaskbar;
		}
	}
}
