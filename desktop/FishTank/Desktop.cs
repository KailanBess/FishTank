using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FishTank;

public sealed partial class Desktop : Form
{
	private Appearance appearance;

	private Palette palette;

	private Panel body;

	private Panel nav;

	private FlowLayoutPanel pageFlow;

	private Label status;

	private FolderMonitor monitor = new FolderMonitor();

	private bool busy;
	internal bool HasRunningOperation => busy || everydayWorking || ScanQueueStore.Running || foregroundScan?.IsRunning == true || (securityScheduler?.IsScanning ?? false);

	private bool observing;

	private AppLockController appLock;
	private SecurityScheduler securityScheduler;
	private bool closingWhileLocked;
	private NotifyIcon securityIcon;
	private bool resourcesDisposed;
	private LiveDetectionSession liveDetection;
	private System.Windows.Forms.Timer liveDisplay;
	private Label liveCoverage;
	private ContextMenuStrip trayMenu;
	private bool exitRequested;
	private LocalBrowserServer browser;
	private BrowserBackend browserBackend;

	private string current = "Dashboard";

	private string lastScan = "No scan recorded";

	private string recoveryNotice;

	private List<Dictionary<string, object>> health;

	private Image artwork;

	private Image displayArtwork;

	private Icon ownedIcon;
    private readonly Dictionary<string, Font> appearanceFonts = new Dictionary<string, Font>();

	private string appliedIconKey;

	private readonly ToolTip tips = new ToolTip();

	private static readonly string[] Pages = new string[12]
	{
		"Dashboard", "Antivirus", "Real-time monitoring", "Network Security", "System Integrity", "Security Center", "Quarantine", "Breach Checks", "Protected Files", "Password Manager", "Tailscale Devices",
		"Settings"
	};

	private static readonly string[] Glyphs = new string[12]
	{
		"◈", "◎", "◉", "⇄", "◇", "◆", "▣", "◌", "▤", "⚿", "⊙",
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
        AquariumWorkspace.Register(this);
		appearance = AppearanceStore.Load();
		recoveryNotice = AppearanceStore.Notice;
		Text = "FishTank · Security, clearly.";
		base.Width = 1280;
		base.Height = 880;
		MinimumSize = new Size(1000, 740);
		base.StartPosition = FormStartPosition.CenterScreen;
		base.AutoScaleMode = AutoScaleMode.Dpi;
		DoubleBuffered = true;
        KeyPreview = true;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += SystemPreferencesChanged;
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
		liveDetection = new LiveDetectionSession(NotifyLiveDetection);
		BuildShell();
		appLock = new AppLockController(this, SetStatus);
		securityScheduler = new SecurityScheduler(NotifySecurity, () => HasRunningOperation || Application.OpenForms.Cast<Form>().Any(form => form.Modal), ScheduledScanFinished);
        try { var interrupted=OperationJournal.Pending(); if(interrupted!=null&&!OperationJournal.IsAlive(interrupted))NotifySecurity("Interrupted operation: "+interrupted.Kind+" started "+interrupted.StartedUtc.ToLocalTime().ToString("g")+". Its final application result was not recorded. Review Windows Security before restarting work."); } catch(Exception ex) { SetStatus("Operation recovery history could not be read: "+ex.Message); }
		liveDisplay = new System.Windows.Forms.Timer { Interval = 2000 };
		liveDisplay.Tick += delegate { RefreshLiveDisplay(); DispatchNotification(); browserBackend?.CheckIdle(); if (browser != null && !BrowserAccessAllowed()) LockBrowserSession(); }; liveDisplay.Start();
		bool liveStarted = false;
        Shown += delegate { BeginInvoke((Action)OfferInitialSetup); };
        Shown += delegate { try { if (AppReleaseNotes.Announce()) NotifySecurity("New FishTank release notes are available. Open What's new from All tools to review the installed version's additions."); } catch { SetStatus("Release notes are available in All tools; their read state could not be saved."); } };
		Shown += delegate {
			if (liveStarted) return; liveStarted = true;
			try { if (SecurityPreferences.Load().LiveDetectionEnabled) liveDetection.Start(); }
			catch { SetStatus("Live alert preference could not be read. No listener was silently enabled."); }
			RefreshLiveDisplay();
		};
		if (appearance.StartMaximized)
		{
			base.WindowState = FormWindowState.Maximized;
		}
		base.FormClosing += delegate(object s, FormClosingEventArgs e)
		{
			if (e.CloseReason == CloseReason.UserClosing && !closingWhileLocked && !exitRequested && liveDetection.Enabled)
			{
				try { if (SecurityPreferences.Load().LiveDetectionKeepInTray) { e.Cancel = true; HideForLiveMonitoring(); return; } }
				catch { e.Cancel = true; SetStatus("Background preference is unreadable. Use the explicit tray Exit command to stop FishTank."); return; }
			}
			if (e.CloseReason == CloseReason.UserClosing && !closingWhileLocked && HasRunningOperation && !Confirm("An operation is still running. Closing FishTank may leave the Windows operation running without its final application record. Close anyway?"))
			{
				e.Cancel = true; exitRequested = false;
			}
		};
	}

	internal void CloseWhileLocked() { exitRequested = true; closingWhileLocked = true; ReturnToSignIn = false; Close(); }

	protected override void Dispose(bool disposing)
	{
		if (disposing && !resourcesDisposed)
		{
			resourcesDisposed = true;
            Microsoft.Win32.SystemEvents.UserPreferenceChanged -= SystemPreferencesChanged;
			browser?.Dispose(); browserBackend?.Dispose();
			liveDisplay?.Dispose(); liveDetection?.Dispose(); appLock?.Dispose(); securityScheduler?.Dispose(); securityIcon?.Dispose(); trayMenu?.Dispose();
			scanWindow?.Dispose(); monitor.Dispose(); tips.Dispose(); artwork?.Dispose(); displayArtwork?.Dispose(); ownedIcon?.Dispose();
		}
		base.Dispose(disposing);
        if (disposing) { foreach (var font in appearanceFonts.Values) font.Dispose(); appearanceFonts.Clear(); }
	}

    private T BrowserUi<T>(Func<T> action)
    {
        if (IsDisposed || !IsHandleCreated) throw new ObjectDisposedException(nameof(Desktop));
        return InvokeRequired ? (T)Invoke(action) : action();
    }
    private bool BrowserAccessAllowed() => !IsDisposed && !resourcesDisposed && appLock != null && !appLock.IsLocked && (!AppAccess.Enabled || AppAccess.SessionUnlocked);
    internal void LockBrowserSession() { browser?.Revoke(); }
    private string BrowserStatus() => BrowserUi(() => Core.Json(new {
        liveEnabled = liveDetection.Enabled, liveStatus = liveDetection.Status, lastScan,
        events = liveDetection.Events().TakeLastCompatible(50).Select(e => new { e.Kind, e.Summary, recorded = e.RecordedUtc.ToLocalTime().ToString("g") })
    }));
    private bool BrowserQuickScan() => BrowserUi(() => {
        if (!BrowserAccessAllowed() || HasRunningOperation) return false;
        if (!Confirm("The paired local browser requests a Defender quick scan. Run it?\n\nExisting Defender policy may quarantine or remove detected threats.")) return false;
        if (!BrowserAccessAllowed()) return false;
        StartVisibleScan("Quick", null); return true;
    });
    private void OpenBrowserDashboard()
    {
        if (!BrowserAccessAllowed()) return;
        if (browser == null)
        {
            browserBackend = new BrowserBackend(BrowserStatus, BrowserQuickScan);
            try { browser = new LocalBrowserServer(() => BrowserUi(BrowserAccessAllowed), browserBackend.Execute, browserBackend.Lock); }
            catch (System.Net.HttpListenerException)
            {
                browserBackend.Dispose(); browserBackend = null;
                ShowMessage("Browser listener could not start", "Port 47653 may be in use, or Windows needs a URL reservation for your account. See BROWSER-AND-ACCOUNT-ORGANIZATION.md in the preview package for the one-time administrator setup. Run FishTank normally afterward; no firewall opening is required."); return;
            }
        }
        try { Process.Start(new ProcessStartInfo(browser.PairingUrl()) { UseShellExecute = true }); SetStatus("Browser pairing link opened. Keep it private. A new link revokes the previous browser session and locks its vault."); }
        catch { LockBrowserSession(); SetStatus("The browser could not be opened. The pairing link was revoked. Check your default browser and try again."); }
    }
    private void StopBrowserDashboard()
    {
        browser?.Dispose(); browser = null; browserBackend?.Dispose(); browserBackend = null;
        SetStatus("Browser dashboard stopped. Its sessions and unlocked vault were cleared.");
    }

	private void NotifySecurity(string message)
	{
		if (IsDisposed) return;
		SetStatus(message);
		try
		{
			bool popup = NoticeCenter.Add(message, NotificationDestination.For(message), 1);
			if (!popup) return;
			if (!SecurityPreferences.Load().DesktopNotifications) return;
			EnsureTray();
			notificationTarget = NotificationDestination.For(message);
            securityIcon.ShowBalloonTip(8000, "FishTank security", "A security reminder or integrity review needs attention. Open FishTank to review it.", ToolTipIcon.Info);
		}
		catch { /* The saved report/status remains available if Windows notifications are unavailable. */ }
	}

	private void EnsureTray()
	{
		if (securityIcon == null)
		{
			trayMenu = new ContextMenuStrip();
			trayMenu.Items.Add("Show FishTank", null, delegate { RestoreFromTray(); });
			trayMenu.Items.Add("Exit FishTank (stops live alerts)", null, delegate { RequestTrayExit(); });
			securityIcon = new NotifyIcon { Icon = SystemIcons.Shield, Text = "FishTank", ContextMenuStrip = trayMenu };
			securityIcon.DoubleClick += delegate { RestoreFromTray(); };
            securityIcon.BalloonTipClicked += delegate { OpenNotification(); };
		}
		securityIcon.Visible = true;
	}

	private void RefreshLiveDisplay()
	{
		if (IsDisposed || liveDetection == null) return;
		if (liveCoverage != null && !liveCoverage.IsDisposed) liveCoverage.Text = liveDetection.Status;
		if (liveDetection.Enabled || appLock?.IsParked == true)
		{
			EnsureTray();
			string text = liveDetection.Enabled ? "FishTank · " + liveDetection.Status : "FishTank · live alerts off";
			securityIcon.Text = text.Length > 63 ? text.Substring(0, 63) : text;
		}
		else if (securityIcon != null)
		{
			try { securityIcon.Visible = SecurityPreferences.Load().DesktopNotifications; }
			catch { securityIcon.Visible = false; }
		}
	}

	private void NotifyLiveDetection(LiveDetectionEvent item)
	{
		if (IsDisposed || !IsHandleCreated) return;
		try { BeginInvoke((Action)delegate {
			if (IsDisposed || !liveDetection.Enabled) return;
			SetStatus("Live Defender alert: " + item.Kind + ". Open Monitoring to review the source evidence.");
			RefreshLiveDisplay();
			try { if (NoticeCenter.Add("Defender activity: " + item.Kind + " · " + item.EventId + "/" + item.RecordId + " · " + item.RecordedUtc.ToString("O"), "Monitoring", item.Severity) && SecurityPreferences.Load().LiveDetectionNotifications) { EnsureTray(); notificationTarget = "Monitoring"; securityIcon.ShowBalloonTip(8000, "FishTank · Defender activity", "Defender activity or a monitoring coverage gap needs review. Open FishTank → Monitoring. No file paths are shown in notifications.", item.Severity == 2 ? ToolTipIcon.Warning : ToolTipIcon.Info); } }
			catch { /* Event history and coverage status remain available. */ }
		}); } catch (InvalidOperationException) { }
	}

	private void HideForLiveMonitoring()
	{
		if (Application.OpenForms.Cast<Form>().Any(form => form != this && form.Visible)) { SetStatus("Close the current dialog before hiding FishTank to the tray. Authorized main-window work can continue in the background."); return; }
		EnsureTray(); appLock.ParkInTray(); Hide();
	}

	private void RestoreFromTray()
	{
		if (IsDisposed) return;
		if (appLock.IsParked) { if (!appLock.RestoreFromTray() || IsDisposed) return; }
		else if (appLock.IsLocked)
		{
			Application.OpenForms.Cast<Form>().OfType<AppUnlockDialog>().FirstOrDefault()?.Activate(); return;
		}
		WindowState = FormWindowState.Normal; Show(); Activate(); RefreshLiveDisplay();
	}

	private void RequestTrayExit()
	{
		if (IsDisposed) return;
		if (Application.OpenForms.Cast<Form>().Any(form => form != this && form.Visible)) { SetStatus("Close the current dialog before exiting FishTank."); return; }
		if (!busy && liveDetection.Enabled && !Confirm("Exit FishTank and stop its live alert listener? Microsoft's existing protection remains independent. Events while FishTank is off may be missing from its history.")) return;
		exitRequested = true; Close();
	}

	private void SetLiveEnabled(bool enabled)
	{
		SecurityPreferences.Update(p => p.LiveDetectionEnabled = enabled);
		try { if (enabled) liveDetection.Start(); else liveDetection.Stop(); }
		finally { RefreshLiveDisplay(); }
		RenderPage();
	}

	private void LiveMonitoringControls()
	{
		Surface coverage = Card((int)TextSize(140f));
		liveCoverage = Label(liveDetection?.Status ?? "Off · live alerts are disabled.", 11f, palette.Text); liveCoverage.Dock = DockStyle.Fill;
		coverage.Controls.Add(liveCoverage); AddBlock(coverage, (int)TextSize(140f));
		Note("Live alerts are optional", "FishTank listens for Microsoft Defender detections, actions, ASR/controlled-folder events and protection changes. It does not detect every threat, scan encrypted/offline contents, or monitor every antivirus product. Defender's own exclusions and policy still apply. Nothing here enables/disables Windows protection or automatically deletes files. Closing FishTank normally stops this listener; optional tray mode keeps the process visibly running after closing its window.");
		ActionRow(Button("Start live Defender alerts", () => SetLiveEnabled(true), true, true), Button("Stop live Defender alerts", () => SetLiveEnabled(false), false, true),
			Button("Review live alert history", delegate { using (var history = new LiveDetectionDialog(liveDetection, palette, appearance.AdvancedMode)) { history.Icon = Icon; history.ShowDialog(this); } }, false, true),
			Button("Open Windows Security", () => Process.Start(new ProcessStartInfo("windowsdefender:") { UseShellExecute = true }), false, true));
		try
		{
			var preferences = SecurityPreferences.Load();
			var keep = new CheckBox { Text = "Keep running in tray when window closes", Checked = preferences.LiveDetectionKeepInTray, AutoSize = true, ForeColor = palette.Text, AccessibleName = "Keep optional live alerts in the tray when closing the window" };
			var notifications = new CheckBox { Text = "Show live alert Windows notifications", Checked = preferences.LiveDetectionNotifications, AutoSize = true, ForeColor = palette.Text, AccessibleName = "Enable generic live alert notifications" };
			bool sync = false; bool previousKeep = keep.Checked, previousNotifications = notifications.Checked;
			keep.CheckedChanged += delegate { if (sync) return; try { SecurityPreferences.Update(p => p.LiveDetectionKeepInTray = keep.Checked); previousKeep = keep.Checked; } catch { sync = true; keep.Checked = previousKeep; sync = false; SetStatus("Background preference could not be saved; previous setting remains in use."); } };
			notifications.CheckedChanged += delegate { if (sync) return; try { SecurityPreferences.Update(p => p.LiveDetectionNotifications = notifications.Checked); previousNotifications = notifications.Checked; } catch { sync = true; notifications.Checked = previousNotifications; sync = false; SetStatus("Live notification preference could not be saved."); } };
			ActionRow(keep); ActionRow(notifications);
		}
		catch { Note("Monitoring preferences unknown", "Preferences could not be read. No background setting was silently enabled."); }
	}

	private float TextSize(float size)
	{
		return size * (float)appearance.TextScale / 100f;
	}

	private Font Face(float size, FontStyle style)
	{
		string key = appearance.FontFamily + ":" + TextSize(size).ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + style;
        if (!appearanceFonts.TryGetValue(key, out var font)) { font = new Font(appearance.FontFamily, TextSize(size), style); appearanceFonts.Add(key, font); }
        return font;
	}

	private Label Label(string text, float size, Color color)
	{
		Label label = new Label();
		label.Text = text;
        label.AccessibleName = text;
		label.AutoSize = false;
		label.ForeColor = color;
		label.BackColor = Color.Transparent;
		label.Font = Face(size, FontStyle.Regular);
		label.UseMnemonic = false;
		label.AutoEllipsis = true;
		return label;
	}

	private CleanButton Button(string text, Action action, bool primary, bool allowWhileBusy = false)
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
        cleanButton2.AccessibleRole = AccessibleRole.PushButton;
        cleanButton2.AccessibleDescription = text + ". Press Enter or Space to activate.";
		cleanButton2.Click += delegate
		{
			if (busy && !allowWhileBusy)
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
            status.AccessibleName = "Application status";
            status.AccessibleDescription = text;
            status.AccessibleRole = AccessibleRole.StatusBar;
			string text2 = text.ToLowerInvariant();
			status.ForeColor = Palette.Override(text2.Contains("failed") ? appearance.WarningColor : (text2.Contains("working") ? appearance.RunningColor : (text2.Contains("completed") ? appearance.ReadyColor : "")), palette.Muted);
		}
	}

	private void BuildShell()
	{
        WindowFrame.SetAppearance(appearance);
        WindowFrame.Attach(this);
        using (var redraw = new WindowRedraw(this))
        {
		SuspendLayout();
        try {
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
			Width = (int)(224 * appearance.TextScale / 100f),
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
			if (!NavigationPolicy.IsVisible(page, appearance.AdvancedMode)) continue;
			string text = ((page == "Real-time monitoring") ? "Monitoring" : ((page == "Network Security") ? "Network" : ((page == "System Integrity") ? "System integrity" : ((page == "Tailscale Devices") ? "Devices" : ((page == "Password Manager") ? "Passwords" : page)))));
			CleanButton cleanButton = Button((appearance.ShowIcons ? (Glyphs[i] + "   ") : "") + text, delegate
			{
				Navigate(page);
			}, primary: false, allowWhileBusy: true);
			cleanButton.Tag = page;
			if (appearance.IconOnlyToolbars)
			{
				cleanButton.Text = Glyphs[i];
				cleanButton.AccessibleName = page;
				tips.SetToolTip(cleanButton, page);
			}
			cleanButton.TextAlign = ContentAlignment.MiddleLeft;
			cleanButton.SetBounds(0, num, nav.Width - 52, (int)TextSize(44f));
			cleanButton.BackColor = palette.Sidebar;
			cleanButton.AccessibleName = page;
			cleanButton.AccessibleDescription = "Open " + page;
			tips.SetToolTip(cleanButton, page);
			cleanButton.Fill = ((page == current) ? Palette.Override(appearance.SelectionColor, palette.Surface2) : palette.Sidebar);
			cleanButton.Border = ((page == current) ? palette.Line : palette.Sidebar);
			cleanButton.Ink = ((page == current) ? palette.Accent : palette.Muted);
			panel4.Controls.Add(cleanButton);
			num += (int)TextSize((appearance.Density == "Roomy") ? 50 : 46);
		}
		panel4.AutoScrollMinSize = new Size(0, num);
		UpdateNavigation(panel4);
		Label label3 = Label("WINDOWS SECURITY COMPANION\nDevelopment preview · " + typeof(Program).Assembly.GetName().Version.ToString(3), 8f, palette.Muted);
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
		if (!SystemInformation.HighContrast && appearance.BackgroundStyle != "Plain")
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
        SimpleExplanations.Attach(this);
        } finally { ResumeLayout(performLayout: true); FitPage(); }
        }
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
            WindowFrame.SetIcon(ownedIcon);
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
		int width = Math.Max(280, pageFlow.ClientSize.Width - 24);
		foreach (Control control in pageFlow.Controls)
		{
			control.Width = width;
		}
	}

	private void Navigate(string page)
	{
		if (!NavigationPolicy.IsVisible(page, appearance.AdvancedMode)) page = "Dashboard";
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
				cleanButton.Hover = cleanButton.Fill;
				cleanButton.BackColor = palette.Sidebar;
				cleanButton.AccessibleDescription = flag ? "Current page: " + current : "Open " + cleanButton.Tag;
				cleanButton.Border = (flag ? palette.Accent : palette.Sidebar);
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
		c.Width = Math.Max(280, pageFlow.ClientSize.Width - 24);
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
		panel2.SizeChanged += delegate
		{
			int width = Math.Max(100, panel2.ClientSize.Width);
			label.Height = TextRenderer.MeasureText(label.Text, label.Font, new Size(width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height + 8;
			label2.Height = TextRenderer.MeasureText(label2.Text, label2.Font, new Size(width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height + 8;
			int required = label.Height + label2.Height + 10;
			if (panel2.Height != required) panel2.Height = required;
		};
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
            AddScanScheduleSummary();
			Surface surface2 = Card(210);
			Label label3 = Label("Choose your scan", 18f, palette.Text);
			label3.Font = Face(18f, FontStyle.Bold);
			label3.Dock = DockStyle.Top;
			label3.Height = (int)TextSize(43f);
			Label label4 = Label("Start with a quick scan, or choose exactly what to inspect.", 11f, palette.Muted);
			label4.Dock = DockStyle.Top;
			label4.Height = (int)TextSize(43f);
			surface2.Controls.Add(label4);
			surface2.Controls.Add(label3);
			AddBlock(surface2, (int)TextSize(140f));
			ActionRow(Button("Quick scan", () => RequestScan("Quick"), true),
				Button("Full scan", () => RequestScan("Full"), false),
				Button("Choose file", () => CustomScan(false), false),
				Button("Choose folder", () => CustomScan(true), false));
			Note("How scanning works", "Quick and full scans follow Defender’s existing remediation policy. Custom scans request no remediation; independent real-time protection can still act.");
			if (appearance.AdvancedMode)
			{
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
			}

			break;
		}
		case "Real-time monitoring":
			Header("Monitoring", "Optional live Defender alerts, with visible background controls.");
			LiveMonitoringControls();
			if (appearance.AdvancedMode)
			{
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
			}

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
				ShowMessage("Remote setup", "Install and sign in to Tailscale on each device using your own account. Discover devices to inspect your existing network. Discovery does not enable remote control. Remote commands need separately configured access; the email/SMS verification server needs its own HTTPS connection and device token in Settings → Multifactor authentication. Never share those credentials.");
			}, primary: false));
			break;
		case "Security Center":
			Header("Security center", "Review protection and check downloaded files before opening them.");
			ShowSecurityChecklist();
			ActionRow(Button("Security and integrity tools", OpenSecurityTools, true));
			ActionRow(Button("Verify a download checksum", VerifyDownload, true),
				Button("Open Windows Security", () => Process.Start(new ProcessStartInfo("windowsdefender:") { UseShellExecute = true }), false));
			if (appearance.AdvancedMode)
			{
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
			}

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
		case "Protected Files":
			Header("Protected files", "Encrypt selected personal files, including their internal names, with a separate passphrase.");
			Note("Protection outside the app", "Encrypted .ftsafe containers stay unreadable without their passphrase. Ordinary copies and permitted exports remain readable. This does not hide the container file or block malware controlling your unlocked account. Enable Windows drive encryption for stolen-device protection.");
			ActionRow(Button("Open protected files", delegate { using (var safe = new ProtectedFilesDialog(palette)) { safe.Icon = Icon; safe.ShowDialog(this); } }, true));
			break;
		case "Password Manager":
			Header("Passwords", "Organize email accounts, their connected services, and each service’s own credentials.");
			Note("A master-passphrase vault", "The encrypted vault groups email accounts with linked services, separate service usernames/passwords, linked sign-in methods, sites and notes. It locks after five minutes of inactivity or when Windows locks. The master passphrase cannot be reset if forgotten.");
			ActionRow(Button("Open password manager", delegate
			{
				using PasswordManagerDialog passwordManagerDialog = new PasswordManagerDialog(palette);
				passwordManagerDialog.Icon = base.Icon;
				passwordManagerDialog.ShowDialog(this);
			}, primary: true));
			Note("Choose your password length", "Generate 1–65,536 characters, subject to the selected character sets. Choose uppercase, lowercase, digits, symbols and lookalike exclusions. Generation works offline; websites may impose smaller limits.");
			break;
		case "Settings":
            Header("Settings", "Find everyday preferences here. Occasional tools are in More tools.");
            AddAppSettings();
            break;
		}
		FitPage();
	}

	private void OpenSecurityTools()
	{
		using (var tools = new SecurityToolsDialog(palette, appearance.AdvancedMode, appearance.TextScale / 100f)) { tools.Icon = Icon; tools.ShowDialog(this); }
	}

	private void AddViewControls()
	{
		ActionRow(Button(appearance.AdvancedMode ? "Switch to Simple view" : "Show Advanced tools", delegate
		{
			Appearance saved = AppearanceStore.Load();
			saved.AdvancedMode = !appearance.AdvancedMode;
			saved.SimpleExplanationsByDefault = SimpleExplanations.DefaultSimple;
			AppearanceStore.Save(saved);
			appearance = saved;
			if (!NavigationPolicy.IsVisible(current, appearance.AdvancedMode)) current = "Dashboard";
			BuildShell();
		}, false));
	}

	private void ShowSecurityChecklist()
	{
		StringBuilder text = new StringBuilder();
		try
		{
			text.AppendLine(AppAccess.Enabled ? "App password: enabled." : "App password: not set. Set one in Settings to protect app access.");
			MfaStatus mfa = AppAccess.Mfa;
			text.AppendLine(!mfa.Configured ? "Authenticator: not enrolled. Add one in Settings; it works without your server."
				: "Authenticator: enrolled. Sign-in verification is " + (mfa.AskOnSignIn ? "on." : "off; sign-in uses only the password."));
			if (mfa.Configured) text.AppendLine("Recovery codes remaining: " + mfa.RecoveryCodesRemaining + ". Keep them privately outside this PC.");
			DeliveryConfiguration delivery = SelfHostedMfa.Load();
			text.AppendLine(delivery == null ? "Email/SMS: server not connected. Offline authentication remains available."
				: "Email: " + (delivery.EmailEnrolled ? "enrolled" : "not enrolled") + "; SMS: " + (delivery.SmsEnrolled ? "enrolled" : "not enrolled") + ". Delivery has not been tested by this checklist.");
		}
		catch
		{
			text.AppendLine("Some access settings could not be read. Their protection status is unknown; review Settings before relying on them.");
		}
		text.AppendLine("Windows protection: use Check this device or open Windows Security for current evidence. This checklist is not a malware verdict.");
		Note("Your local security checklist", text.ToString());
		ActionRow(Button("Review access settings", () => Navigate("Settings"), false));
	}

	private void VerifyDownload()
	{
		string path = PickFile();
		if (path == null) return;
		using (Form prompt = new Form())
		{
			prompt.Text = "Verify download · SHA-256";
			prompt.StartPosition = FormStartPosition.CenterParent;
			prompt.AutoScaleMode = AutoScaleMode.Dpi;
			prompt.ClientSize = new Size(600, 280);
			prompt.MinimumSize = new Size(480, 320);
			prompt.BackColor = palette.Background;
			prompt.ForeColor = palette.Text;
			prompt.Font = Face(10f, FontStyle.Regular);
			prompt.Padding = new Padding(18);
			Label help = new Label { Dock = DockStyle.Top, Height = 110,
				Text = "Paste the publisher's SHA-256 checksum from a trusted source. FishTank reads the file locally; it does not open, run or upload it. A match confirms the file matches that checksum, not that it is safe.", UseMnemonic = false };
			TextBox input = new TextBox { Dock = DockStyle.Top, MaxLength = 66, AccessibleName = "Expected publisher SHA-256 checksum" };
			Button verify = new Button { Text = "Compare checksum", AutoSize = true, Dock = DockStyle.Bottom, Height = 42 };
			verify.Click += delegate
			{
				try { input.Text = DownloadChecksum.Normalize(input.Text); prompt.DialogResult = DialogResult.OK; }
				catch (ArgumentException ex) { ExplainedMessageBox.Show(prompt, ex.Message, "Check the checksum"); }
			};
			prompt.Controls.Add(input);
			prompt.Controls.Add(help);
			prompt.Controls.Add(verify);
			prompt.AcceptButton = verify;
			if (prompt.ShowDialog(this) != DialogResult.OK) return;
			string expected = input.Text;
			StartWork(delegate
			{
				string actual = DownloadChecksum.Compute(path);
				return (DownloadChecksum.Matches(expected, actual) ? "MATCH: the file matches the supplied checksum. This does not prove it is safe." : "MISMATCH: the file differs from the supplied checksum. Do not run it until you resolve the difference with its publisher.")
					+ "\nExpected SHA-256: " + expected + "\nActual SHA-256: " + actual;
			}, "Message");
		}
	}

    private void Dashboard()
    {
        if (appearance.ShowBanner) AddReferenceSecurityOverview();
        else { Header("FishTank", "Review protection and choose a scan."); ActionRow(Button("Quick scan", () => RequestScan("Quick"), true), Button("Full scan", () => RequestScan("Full"), false)); }
        AddAttentionOverview();
        AddViewControls();
        if (observing) { Note("Folder observer is running", "Your optional folder observer continues in the background. It reports activity and does not block threats."); ActionRow(Button("Stop folder observer", delegate { monitor.Dispose(); observing = false; RenderPage(); }, false)); }
        if (!string.IsNullOrEmpty(lastScan)) Note("Latest scan", lastScan);
        if (!string.IsNullOrEmpty(recoveryNotice)) Note("Appearance settings", recoveryNotice);
    }
	private async void StartWork(Func<string> action, string kind)
	{
		if (HasRunningOperation)
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
			StartVisibleScan(kind, null);
		}
	}

	private void CustomScan(bool folder)
	{
		string path = (folder ? PickFolder() : PickFile());
		if (path != null)
		{
			StartVisibleScan("Custom", path);
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
					appearanceDialog.Value.AdvancedMode = this.appearance.AdvancedMode;
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

