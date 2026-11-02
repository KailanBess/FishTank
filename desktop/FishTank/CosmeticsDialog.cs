using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace FishTank;

public sealed class CosmeticsDialog : Form
{
	public Appearance Value;

	private readonly Action<Appearance> preview;

	private Label message;

	public CosmeticsDialog(Appearance value, Action<Appearance> preview)
	{
		Value = value;
		this.preview = preview;
		Text = "FishTank · Cosmetic styles";
		base.Width = 820;
		base.Height = 800;
		MinimumSize = new Size(720, 620);
		base.StartPosition = FormStartPosition.CenterParent;
		Font = new Font("Segoe UI", 10f);
		BackColor = Color.FromArgb(24, 29, 41);
		ForeColor = Color.White;
		TabControl tabControl = new TabControl
		{
			Dock = DockStyle.Fill
		};
		message = new Label
		{
			Dock = DockStyle.Bottom,
			Height = 40,
			Text = "Live preview. Cancel restores your previous appearance."
		};
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Bottom,
			Height = 48,
			FlowDirection = FlowDirection.RightToLeft
		};
		Button button = new Button
		{
			Text = "Save and close",
			AutoSize = true
		};
		EventHandler value2 = delegate
		{
			try
			{
				Value.Validate();
				base.DialogResult = DialogResult.OK;
				Close();
			}
			catch (Exception ex)
			{
				message.Text = ex.Message;
			}
		};
		button.Click += value2;
		flowLayoutPanel.Controls.Add(button);
		flowLayoutPanel.Controls.Add(new Button
		{
			Text = "Cancel",
			DialogResult = DialogResult.Cancel,
			AutoSize = true
		});
		base.Controls.Add(tabControl);
		base.Controls.Add(message);
		base.Controls.Add(flowLayoutPanel);
		TableLayoutPanel rows = Tab(tabControl, "Custom palette");
		Toggle(rows, "Use custom palette", "CustomPalette");
		string[] array = new string[7] { "Main text|TextColor", "Secondary text|MutedColor", "Header background|TopColor", "Page background|BottomColor", "Card background|SurfaceColor", "Accent|AccentColor", "Secondary accent|SecondaryColor" };
		foreach (string spec in array)
		{
			ColorRow(rows, spec);
		}
		TableLayoutPanel rows2 = Tab(tabControl, "Shapes and focus");
		Number(rows2, "Corner radius (-1 = existing shape)", "CornerRadius", -1, 30);
		Choice(rows2, "Navigation icon style", "IconStyle", new string[3] { "Outline", "Filled", "Monochrome" });
		Toggle(rows2, "Icon-only navigation with tooltips", "IconOnlyToolbars");
		ColorRow(rows2, "Selection color|SelectionColor");
		ColorRow(rows2, "Focus outline|FocusColor");
		Number(rows2, "Focus outline width", "FocusWidth", 1, 5);
		TableLayoutPanel rows3 = Tab(tabControl, "Background");
		Choice(rows3, "Background style", "BackgroundStyle", new string[5] { "Plain", "Gradient", "Dots", "Waves", "Wallpaper" });
		FlowLayoutPanel flowLayoutPanel2 = new FlowLayoutPanel();
		TextBox path = new TextBox
		{
			Width = 360,
			Text = Value.WallpaperPath
		};
		path.TextChanged += delegate
		{
			Value.WallpaperPath = path.Text;
			Changed();
		};
		Button button2 = new Button
		{
			Text = "Choose wallpaper",
			AutoSize = true
		};
		button2.Click += delegate
		{
			using OpenFileDialog openFileDialog = new OpenFileDialog
			{
				Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif"
			};
			if (openFileDialog.ShowDialog(this) == DialogResult.OK)
			{
				path.Text = openFileDialog.FileName;
			}
		};
		flowLayoutPanel2.Controls.Add(path);
		flowLayoutPanel2.Controls.Add(button2);
		Row(rows3, "Wallpaper image", flowLayoutPanel2, 70);
		Choice(rows3, "Wallpaper layout", "WallpaperLayout", new string[3] { "Fill", "Fit", "Tile" });
		Number(rows3, "Wallpaper opacity (%)", "WallpaperOpacity", 0, 100);
		Toggle(rows3, "Show aquarium footer", "ShowFooter");
		TableLayoutPanel rows4 = Tab(tabControl, "Artwork and status");
		Choice(rows4, "Artwork frame", "ArtworkFrame", new string[4] { "None", "Thin", "Accent", "Rounded" });
		Toggle(rows4, "Soft artwork shadow", "ArtworkShadow");
		Choice(rows4, "Status badge style", "BadgeStyle", new string[3] { "Text", "Pill", "Square" });
		ColorRow(rows4, "Completed-operation color|ReadyColor");
		ColorRow(rows4, "Working-operation color|RunningColor");
		ColorRow(rows4, "Warning-operation color|WarningColor");
		Row(rows4, "Security meaning", new Label
		{
			Text = "These are presentation colors, not security verdicts. Emulator/platform labels and game-cover controls stay in FishBowl; FishTank adapts them to security cards and evidence panes.",
			Dock = DockStyle.Fill
		}, 110);
	}

	private void Changed()
	{
		try
		{
			Value.Validate();
			preview(Value.Copy());
			message.Text = "Preview applied. Changes affect presentation only.";
		}
		catch (Exception ex)
		{
			message.Text = ex.Message;
		}
	}

	private TableLayoutPanel Tab(TabControl tabs, string text)
	{
		TabPage tabPage = new TabPage(text);
		tabPage.AutoScroll = true;
		tabPage.BackColor = BackColor;
		tabPage.ForeColor = ForeColor;
		tabPage.Padding = new Padding(18);
		TabPage tabPage2 = tabPage;
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel();
		tableLayoutPanel.Dock = DockStyle.Top;
		tableLayoutPanel.AutoSize = true;
		tableLayoutPanel.ColumnCount = 2;
		TableLayoutPanel tableLayoutPanel2 = tableLayoutPanel;
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40f));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60f));
		tabPage2.Controls.Add(tableLayoutPanel2);
		tabs.TabPages.Add(tabPage2);
		return tableLayoutPanel2;
	}

	private void Row(TableLayoutPanel rows, string label, Control control, int height)
	{
		int row = rows.RowCount++;
		rows.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
		rows.Controls.Add(new Label
		{
			Text = label,
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleLeft
		}, 0, row);
		control.Dock = DockStyle.Fill;
		control.Margin = new Padding(0, 8, 0, 8);
		rows.Controls.Add(control, 1, row);
	}

	private void ColorRow(TableLayoutPanel rows, string spec)
	{
		string[] array = spec.Split('|');
		FieldInfo field = typeof(Appearance).GetField(array[1]);
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel();
		TextBox text = new TextBox
		{
			Width = 135,
			Text = (string)field.GetValue(Value)
		};
		text.TextChanged += delegate
		{
			field.SetValue(Value, text.Text);
			Changed();
		};
		Button button = new Button();
		button.Text = "Choose…";
		button.AutoSize = true;
		Button button2 = button;
		button2.Click += delegate
		{
			using ColorDialog colorDialog = new ColorDialog
			{
				FullOpen = true
			};
			if (colorDialog.ShowDialog(this) == DialogResult.OK)
			{
				text.Text = "#" + colorDialog.Color.R.ToString("X2") + colorDialog.Color.G.ToString("X2") + colorDialog.Color.B.ToString("X2");
			}
		};
		flowLayoutPanel.Controls.Add(text);
		flowLayoutPanel.Controls.Add(button2);
		Row(rows, array[0] + " (blank = theme)", flowLayoutPanel, 55);
	}

	private void Toggle(TableLayoutPanel rows, string label, string name)
	{
		FieldInfo field = typeof(Appearance).GetField(name);
		CheckBox box = new CheckBox
		{
			Checked = (bool)field.GetValue(Value),
			Text = "Enabled",
			ForeColor = ForeColor
		};
		box.CheckedChanged += delegate
		{
			field.SetValue(Value, box.Checked);
			Changed();
		};
		Row(rows, label, box, 48);
	}

	private void Number(TableLayoutPanel rows, string label, string name, int min, int max)
	{
		FieldInfo field = typeof(Appearance).GetField(name);
		NumericUpDown number = new NumericUpDown
		{
			Minimum = min,
			Maximum = max,
			Value = (int)field.GetValue(Value)
		};
		number.ValueChanged += delegate
		{
			field.SetValue(Value, (int)number.Value);
			Changed();
		};
		Row(rows, label, number, 50);
	}

	private void Choice(TableLayoutPanel rows, string label, string name, string[] values)
	{
		FieldInfo field = typeof(Appearance).GetField(name);
		ComboBox choice = new ComboBox
		{
			DropDownStyle = ComboBoxStyle.DropDownList
		};
		choice.Items.AddRange(values);
		choice.SelectedItem = (string)field.GetValue(Value);
		choice.SelectedIndexChanged += delegate
		{
			field.SetValue(Value, Convert.ToString(choice.SelectedItem));
			Changed();
		};
		Row(rows, label, choice, 50);
	}
}
