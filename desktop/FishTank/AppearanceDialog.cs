using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace FishTank;

public sealed class AppearanceDialog : Form
{
	public Appearance Value;

	private readonly Action<Appearance> preview;

	private Appearance original;

	private bool loading;
    private readonly Timer previewDelay = new Timer { Interval = 120 };
    private Appearance pendingPreview;

	private TabControl tabs;

	private Panel sample;

	private Label sampleTitle;

	private Label sampleDetail;

	private CheckedListBox order;

	public AppearanceDialog(Appearance value, Action<Appearance> preview)
	{
		Value = value;
		original = value.Copy();
		this.preview = preview;
        previewDelay.Tick += delegate { previewDelay.Stop(); if (!IsDisposed && pendingPreview != null) this.preview(pendingPreview.Copy()); };
		Text = "Customize FishTank";
		base.Width = 790;
		base.Height = 800;
		MinimumSize = new Size(720, 650);
		base.StartPosition = FormStartPosition.CenterParent;
		Font = new Font("Segoe UI", 10f);
		BackColor = Color.FromArgb(24, 29, 41);
		ForeColor = Color.FromArgb(234, 240, 248);
		Label value2 = new Label
		{
			Text = "Make it yours.",
			Dock = DockStyle.Top,
			Height = 66,
			Font = new Font("Segoe UI", 23f, FontStyle.Bold),
			Padding = new Padding(22, 15, 0, 0)
		};
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Bottom,
			Height = 65,
			Padding = new Padding(16, 10, 0, 0),
			FlowDirection = FlowDirection.RightToLeft
		};
		Control.ControlCollection controls = flowLayoutPanel.Controls;
		Action action = delegate
		{
			try
			{
				Value.Validate();
				base.DialogResult = DialogResult.OK;
				Close();
			}
			catch (Exception ex)
			{
				ExplainedMessageBox.Show(this, ex.Message, "Check settings");
			}
		};
		controls.Add(ActionButton("Save & close", action));
		flowLayoutPanel.Controls.Add(ActionButton("Cancel", delegate
		{
			base.DialogResult = DialogResult.Cancel;
			Close();
		}));
		flowLayoutPanel.Controls.Add(ActionButton("Revert", delegate
		{
			Value = original.Copy();
			BuildTabs();
			Changed();
		}));
		flowLayoutPanel.Controls.Add(ActionButton("Defaults", delegate
		{
			Value = Appearance.Defaults();
			BuildTabs();
			Changed();
		}));
		tabs = new ThemeTabControl
		{
			Dock = DockStyle.Fill, DrawMode = TabDrawMode.OwnerDrawFixed
		};
        tabs.DrawItem += delegate(object sender, DrawItemEventArgs e) {
            if (e.Index < 0 || e.Index >= tabs.TabCount) return;
            var colors = Palette.From(Value);
            using (var brush = new SolidBrush(e.Index == tabs.SelectedIndex ? colors.Surface2 : colors.Background)) e.Graphics.FillRectangle(brush, e.Bounds);
            TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, Font, e.Bounds, colors.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        };
		base.Controls.Add(tabs);
		base.Controls.Add(flowLayoutPanel);
		base.Controls.Add(value2);
		BuildTabs();
	}

	private Button ActionButton(string text, Action action)
	{
		Button button = new Button();
		button.Text = text;
		button.AutoSize = true;
		button.Height = 38;
		button.FlatStyle = FlatStyle.Flat;
		button.BackColor = Color.FromArgb(47, 59, 78);
		button.ForeColor = ForeColor;
		button.Padding = new Padding(8, 3, 8, 3);
		button.Margin = new Padding(5);
		Button button2 = button;
		button2.Click += delegate
		{
			try
			{
				action();
			}
			catch (Exception ex)
			{
				ExplainedMessageBox.Show(this, ex.Message, "Appearance settings");
			}
		};
		return button2;
	}

	private TableLayoutPanel Tab(string name)
	{
		TabPage tabPage = new TabPage(name);
		tabPage.BackColor = BackColor;
		tabPage.ForeColor = ForeColor;
		tabPage.Padding = new Padding(18);
		tabPage.AutoScroll = true;
		TabPage tabPage2 = tabPage;
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel();
		tableLayoutPanel.Dock = DockStyle.Top;
		tableLayoutPanel.AutoSize = true;
		tableLayoutPanel.ColumnCount = 2;
		tableLayoutPanel.BackColor = BackColor;
		tableLayoutPanel.Padding = new Padding(0, 0, 15, 20);
		TableLayoutPanel tableLayoutPanel2 = tableLayoutPanel;
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42f));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58f));
		tabPage2.Controls.Add(tableLayoutPanel2);
		tabs.TabPages.Add(tabPage2);
		return tableLayoutPanel2;
	}

	private void Row(TableLayoutPanel table, string label, Control field, int height)
	{
		int row = table.RowCount++;
		table.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
		Label label2 = new Label();
		label2.Text = label;
		label2.Dock = DockStyle.Fill;
		label2.TextAlign = ContentAlignment.MiddleLeft;
		label2.ForeColor = ForeColor;
		Label control = label2;
		field.Dock = DockStyle.Fill;
		field.Margin = new Padding(0, 9, 0, 9);
		field.AccessibleName = label;
		table.Controls.Add(control, 0, row);
		table.Controls.Add(field, 1, row);
	}

	private void Choice(TableLayoutPanel table, string label, string selected, string[] choices, Action<string> set)
	{
		ComboBox combo = new ComboBox
		{
			DropDownStyle = ComboBoxStyle.DropDownList,
            DrawMode = DrawMode.OwnerDrawFixed,
			BackColor = Color.FromArgb(38, 47, 63),
			ForeColor = ForeColor,
			FlatStyle = FlatStyle.Flat
		};
		combo.DrawItem += delegate(object sender, DrawItemEventArgs e) { var colors = Palette.From(Value); using (var fill = new SolidBrush((e.State & DrawItemState.Selected) != 0 ? colors.Surface2 : colors.Surface)) e.Graphics.FillRectangle(fill, e.Bounds); if (e.Index >= 0) TextRenderer.DrawText(e.Graphics, Convert.ToString(combo.Items[e.Index]), combo.Font, e.Bounds, colors.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix); e.DrawFocusRectangle(); };
        combo.Items.AddRange(choices);
		combo.SelectedItem = selected;
		combo.SelectedIndexChanged += delegate
		{
			set(Convert.ToString(combo.SelectedItem));
			Changed();
		};
		Row(table, label, combo, 52);
	}

	private void Toggle(TableLayoutPanel table, string label, bool value, Action<bool> set)
	{
		CheckBox checkbox = new CheckBox
		{
			Checked = value,
			Text = "Enabled",
			ForeColor = ForeColor
		};
		checkbox.CheckedChanged += delegate
		{
			set(checkbox.Checked);
			Changed();
		};
		Row(table, label, checkbox, 48);
	}

	private void Paragraph(TableLayoutPanel table, string text, int height)
	{
		int row = table.RowCount++;
		table.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
		Label label = new Label();
		label.Text = text;
		label.ForeColor = Color.FromArgb(171, 188, 209);
        label.Tag = "appearance-hint";
		label.Dock = DockStyle.Fill;
		label.Padding = new Padding(0, 12, 0, 0);
		Label control = label;
		table.Controls.Add(control, 0, row);
		table.SetColumnSpan(control, 2);
	}

	private void BuildTabs()
	{
		loading = true;
		foreach (Control item in new List<Control>(GetControls(tabs.Controls)))
		{
			tabs.Controls.Remove(item);
			item.Dispose();
		}
		TableLayoutPanel tableLayoutPanel = Tab("Appearance");
		Paragraph(tableLayoutPanel, "Preview changes on the app behind this window. These controls change presentation only.", 64);
        Row(tableLayoutPanel, "Aquarium family", ActionButton("Use Aquarium style", delegate
        {
            Value = Value.WithAquariumStyle();
            BuildTabs();
            Changed();
        }), 54);
		sample = new Panel
		{
			BackColor = Palette.From(Value).Surface,
			Height = 105,
			Dock = DockStyle.Fill,
			Margin = new Padding(0, 0, 0, 8)
		};
		sampleTitle = new Label
		{
			Text = "FishTank · Your security, clearly.",
			Dock = DockStyle.Top,
			Height = 44,
			Padding = new Padding(12, 10, 0, 0)
		};
		sampleDetail = new Label
		{
			Text = "Independent text size and comfortable spacing.",
			Dock = DockStyle.Fill,
			Padding = new Padding(12, 4, 0, 0)
		};
		sample.Controls.Add(sampleDetail);
		sample.Controls.Add(sampleTitle);
		int row = tableLayoutPanel.RowCount++;
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 105f));
		tableLayoutPanel.Controls.Add(sample, 0, row);
		tableLayoutPanel.SetColumnSpan(sample, 2);
        var frameChoices = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false };
        var followWindows = new RadioButton { Text = "Follow Windows", AutoSize = true, Checked = Value.WindowFrameMode == "Follow Windows", AccessibleName = "Window frame: Follow Windows" };
        var customFrame = new RadioButton { Text = "Custom color", AutoSize = true, Checked = Value.WindowFrameMode == "Custom color", AccessibleName = "Window frame: Custom color" };
        frameChoices.Controls.Add(followWindows); frameChoices.Controls.Add(customFrame);
        var frameColor = ActionButton("Choose color · " + Value.WindowFrameColor, delegate {
            using (var picker = new ColorDialog { Color = ColorTranslator.FromHtml(Value.WindowFrameColor), FullOpen = true })
                if (picker.ShowDialog(this) == DialogResult.OK) { Value.WindowFrameColor = "#" + picker.Color.R.ToString("X2") + picker.Color.G.ToString("X2") + picker.Color.B.ToString("X2"); BuildTabs(); Changed(); }
        });
        frameColor.Enabled = customFrame.Checked;
        followWindows.CheckedChanged += delegate { if (followWindows.Checked) { Value.WindowFrameMode = "Follow Windows"; frameColor.Enabled = false; Changed(); } };
        customFrame.CheckedChanged += delegate { if (customFrame.Checked) { Value.WindowFrameMode = "Custom color"; frameColor.Enabled = true; Changed(); } };
        Row(tableLayoutPanel, "Outside window / title bar", frameChoices, 82);
        Row(tableLayoutPanel, "Custom window color", frameColor, 55);
        Paragraph(tableLayoutPanel, "Window colors are separate from the app theme below. Follow Windows uses your PC's light/dark and title-bar accent settings. Custom title-bar/border colors require Windows 11; Windows 10 keeps its native frame. High Contrast takes priority.", 112);
		Choice(tableLayoutPanel, "Theme", Value.Theme, Appearance.Themes, delegate(string v)
		{
			Value.Theme = v;
		});
		Choice(tableLayoutPanel, "Accent color", Value.Accent, Appearance.Accents, delegate(string v)
		{
			Value.Accent = v;
		});
		Choice(tableLayoutPanel, "App icon color", Value.IconColor, new string[2] { "Original", "Match accent" }.Concat(Appearance.Accents).ToArray(), delegate(string v)
		{
			Value.IconColor = v;
		});
		Choice(tableLayoutPanel, "Interface font", Value.FontFamily, Appearance.Fonts, delegate(string v)
		{
			Value.FontFamily = v;
		});
		NumericUpDown scale = new NumericUpDown
		{
			Minimum = 90m,
			Maximum = 150m,
			Increment = 5m,
			Value = Value.TextScale,
			BackColor = Color.FromArgb(38, 47, 63),
			ForeColor = ForeColor
		};
		scale.ValueChanged += delegate
		{
			Value.TextScale = (int)scale.Value;
			Changed();
		};
		Row(tableLayoutPanel, "Text size (%)", scale, 52);
		Choice(tableLayoutPanel, "Control spacing", Value.Density, new string[3] { "Compact", "Standard", "Roomy" }, delegate(string v)
		{
			Value.Density = v;
		});
		Choice(tableLayoutPanel, "Color blending", Value.Blending, new string[2] { "Harmonized", "Original" }, delegate(string v)
		{
			Value.Blending = v;
		});
		Choice(tableLayoutPanel, "Corner style", Value.Shape, new string[2] { "Rounded", "Square" }, delegate(string v)
		{
			Value.Shape = v;
		});
		Choice(tableLayoutPanel, "Artwork size", Value.Artwork, new string[3] { "Compact", "Balanced", "Large" }, delegate(string v)
		{
			Value.Artwork = v;
		});
		Button field = ActionButton("Cosmetic styles…", delegate
		{
			Appearance value = Value.Copy();
			Appearance value2 = Value.Copy();
			Action<Appearance> action = delegate(Appearance a)
			{
				Value = a.Copy();
				Changed();
			};
			using (CosmeticsDialog cosmeticsDialog = new CosmeticsDialog(value2, action))
			{
				if (cosmeticsDialog.ShowDialog(this) == DialogResult.OK)
				{
					Value = cosmeticsDialog.Value.Copy();
				}
				else
				{
					Value = value;
				}
			}
			BuildTabs();
			Changed();
		});
		Row(tableLayoutPanel, "Full cosmetic controls", field, 55);
		TableLayoutPanel table = Tab("Dashboard & layout");
		Paragraph(table, "Choose which dashboard cards appear and move them into your preferred order. Text size is never changed by spacing presets.", 75);
		Toggle(table, "Show dashboard banner", Value.ShowBanner, delegate(bool v)
		{
			Value.ShowBanner = v;
		});
		Toggle(table, "Show status bar", Value.ShowStatus, delegate(bool v)
		{
			Value.ShowStatus = v;
		});
		Toggle(table, "Show app / navigation icons", Value.ShowIcons, delegate(bool v)
		{
			Value.ShowIcons = v;
		});
		Toggle(table, "Show evidence information pane", Value.ShowDetails, delegate(bool v)
		{
			Value.ShowDetails = v;
		});
		Toggle(table, "Subtle alternating rows", Value.AlternatingRows, delegate(bool v)
		{
			Value.AlternatingRows = v;
		});
		Toggle(table, "Start maximized", Value.StartMaximized, delegate(bool v)
		{
			Value.StartMaximized = v;
		});
		order = new CheckedListBox
		{
			CheckOnClick = true,
			Height = 130,
			BackColor = Color.FromArgb(38, 47, 63),
			ForeColor = ForeColor,
			BorderStyle = BorderStyle.None
		};
		for (int num = 0; num < Value.CardOrder.Length; num++)
		{
			order.Items.Add(Value.CardOrder[num], Value.CardsVisible[num]);
		}
		order.ItemCheck += delegate(object s, ItemCheckEventArgs e)
		{
			if (!loading)
			{
				Value.CardsVisible[e.Index] = e.NewValue == CheckState.Checked;
				Changed();
			}
		};
		Row(table, "Dashboard card order", order, 145);
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel();
		flowLayoutPanel.Controls.Add(ActionButton("Move up", delegate
		{
			MoveCard(-1);
		}));
		flowLayoutPanel.Controls.Add(ActionButton("Move down", delegate
		{
			MoveCard(1);
		}));
		Row(table, "Reorder selected card", flowLayoutPanel, 62);
		TableLayoutPanel table2 = Tab("Accessibility");
		Paragraph(table2, "Settings apply live. Reduced motion removes decorative hover color changes; no decorative animation runs in the background.", 75);
		Toggle(table2, "Reduced motion", Value.ReducedMotion, delegate(bool v)
		{
			Value.ReducedMotion = v;
		});
		Toggle(table2, "Strong keyboard focus outlines", Value.StrongFocus, delegate(bool v)
		{
			Value.StrongFocus = v;
		});
		Choice(table2, "Selection / focus contrast", Value.Contrast, new string[2] { "Soft", "Strong" }, delegate(string v)
		{
			Value.Contrast = v;
		});
		FlowLayoutPanel flowLayoutPanel2 = new FlowLayoutPanel();
		flowLayoutPanel2.WrapContents = true;
		FlowLayoutPanel flowLayoutPanel3 = flowLayoutPanel2;
		flowLayoutPanel3.Controls.Add(ActionButton("Comfortable reading", delegate
		{
			Value.TextScale = 120;
			Value.Density = "Roomy";
			Value.StrongFocus = true;
			Value.Contrast = "Strong";
			BuildTabs();
			Changed();
		}));
		flowLayoutPanel3.Controls.Add(ActionButton("Compact workspace", delegate
		{
			Value.Density = "Compact";
			BuildTabs();
			Changed();
		}));
		Row(table2, "Presets", flowLayoutPanel3, 110);
		Paragraph(table2, "Compact workspace preserves your selected font and text size. Light and dark themes use separate readable text palettes; accents are kept on controls and highlights.", 90);
		TableLayoutPanel table3 = Tab("Import & export");
		Paragraph(table3, "Only FishTank appearance preferences are included. No security policy, VPN credentials, device enrollments, histories or quarantine contents are imported or exported.", 100);
		FlowLayoutPanel flowLayoutPanel4 = new FlowLayoutPanel();
		flowLayoutPanel4.WrapContents = true;
		FlowLayoutPanel flowLayoutPanel5 = flowLayoutPanel4;
		flowLayoutPanel5.Controls.Add(ActionButton("Export appearance", delegate
		{
			using SaveFileDialog saveFileDialog = new SaveFileDialog
			{
				Filter = "FishTank appearance|*.json",
				FileName = "FishTank-appearance.json"
			};
			if (saveFileDialog.ShowDialog(this) == DialogResult.OK)
			{
				AppearanceStore.Export(Value, saveFileDialog.FileName);
			}
		}));
		flowLayoutPanel5.Controls.Add(ActionButton("Import appearance", delegate
		{
			using OpenFileDialog openFileDialog = new OpenFileDialog
			{
				Filter = "FishTank appearance|*.json"
			};
			if (openFileDialog.ShowDialog(this) == DialogResult.OK)
			{
				Value = AppearanceStore.Import(openFileDialog.FileName);
				BuildTabs();
				Changed();
			}
		}));
		Row(table3, "Appearance files", flowLayoutPanel5, 110);
		Paragraph(table3, "Save & close persists your preferences. Cancel restores the appearance from before this window opened. Revert restores that same starting point while keeping this window open. Corrupt settings use a previous valid backup, or safe defaults.", 120);
		loading = false;
		RefreshSample();
	}

	private static List<Control> GetControls(Control.ControlCollection controls)
	{
		List<Control> list = new List<Control>();
		foreach (Control control in controls)
		{
			list.Add(control);
		}
		return list;
	}

	private void MoveCard(int delta)
	{
		int selectedIndex = order.SelectedIndex;
		int num = selectedIndex + delta;
		if (selectedIndex >= 0 && num >= 0 && num < Value.CardOrder.Length)
		{
			string text = Value.CardOrder[selectedIndex];
			bool flag = Value.CardsVisible[selectedIndex];
			Value.CardOrder[selectedIndex] = Value.CardOrder[num];
			Value.CardsVisible[selectedIndex] = Value.CardsVisible[num];
			Value.CardOrder[num] = text;
			Value.CardsVisible[num] = flag;
			int selectedIndex2 = tabs.SelectedIndex;
			BuildTabs();
			tabs.SelectedIndex = selectedIndex2;
			order.SelectedIndex = num;
			Changed();
		}
	}

	private void Changed()
	{
		if (!loading)
		{
			Value.Validate();
			RefreshSample();
			pendingPreview = Value.Copy();
            previewDelay.Stop(); previewDelay.Start();
		}
	}

	protected override void Dispose(bool disposing) { if (disposing) previewDelay.Dispose(); base.Dispose(disposing); }

	private static void ApplyColors(Control root, Palette colors)
    {
        root.BackColor = root is Label ? Color.Transparent : root is ComboBox || root is NumericUpDown || root is ListBox ? colors.Surface : root is Button ? colors.Surface2 : colors.Background;
        if (root is ThemeTabControl themedTabs) { themedTabs.BorderColor = colors.Line; themedTabs.FrameBackground = colors.Background; }
        root.ForeColor = Equals(root.Tag, "appearance-hint") ? colors.Muted : colors.Text;
        if (root is Button button) { button.UseVisualStyleBackColor = false; button.FlatAppearance.BorderColor = colors.Line; button.FlatAppearance.MouseOverBackColor = colors.Surface; }
        foreach (Control child in root.Controls) ApplyColors(child, colors);
    }

	private void RefreshSample()
	{
        ApplyColors(this, Palette.From(Value));
        tabs.Invalidate();
		if (sample != null)
		{
			Palette palette = Palette.From(Value);
			sample.BackColor = palette.Surface;
			sampleTitle.ForeColor = palette.Text;
			sampleTitle.Font = new Font(Value.FontFamily, (float)(12 * Value.TextScale) / 100f, FontStyle.Bold);
			sampleDetail.ForeColor = palette.Muted;
			sampleDetail.Font = new Font(Value.FontFamily, (float)(10 * Value.TextScale) / 100f);
		}
	}
}
