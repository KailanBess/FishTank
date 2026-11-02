using System;
using System.Drawing;
using System.Windows.Forms;

namespace FishTank;

public sealed class PasswordEditor : Form
{
	private TextBox title;

	private TextBox username;

	private TextBox password;

	private TextBox website;

	private TextBox notes;

	private TextBox symbols;

	private NumericUpDown length;

	private CheckBox lower;

	private CheckBox upper;

	private CheckBox digits;

	private CheckBox punctuation;

	private CheckBox ambiguous;

	public PasswordEntry Value;

	private bool generator;

	public PasswordEditor(PasswordEntry entry, bool generator, Palette colors)
	{
		PasswordEditor passwordEditor = this;
		this.generator = generator;
		Value = entry.Copy();
		Text = (generator ? "Password generator" : "Password entry");
		base.Width = 690;
		base.Height = (generator ? 560 : 820);
		MinimumSize = new Size(630, generator ? 520 : 700);
		base.StartPosition = FormStartPosition.CenterParent;
		Font = new Font("Segoe UI", 10f);
		BackColor = colors.Background;
		ForeColor = colors.Text;
		base.Padding = new Padding(18);
		Panel panel = new Panel
		{
			Dock = DockStyle.Fill,
			AutoScroll = true
		};
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			AutoSize = true,
			ColumnCount = 2
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 75f));
		panel.Controls.Add(tableLayoutPanel);
		title = Field(Value.Title, 256, colors);
		username = Field(Value.Username, 1024, colors);
		website = Field(Value.Website, 2048, colors);
		notes = Field(Value.Notes, 10000, colors);
		notes.Multiline = true;
		if (!generator)
		{
			Row(tableLayoutPanel, "Name", title, 50);
			Row(tableLayoutPanel, "Username", username, 50);
			Row(tableLayoutPanel, "Website / app", website, 50);
		}
		password = Field(Value.Password, 65536, colors);
		password.UseSystemPasswordChar = true;
		Row(tableLayoutPanel, "Password", password, 50);
		CheckBox reveal = new CheckBox
		{
			Text = "Show password",
			ForeColor = colors.Muted,
			AutoSize = true
		};
		reveal.CheckedChanged += delegate
		{
			passwordEditor.password.UseSystemPasswordChar = !reveal.Checked;
		};
		Row(tableLayoutPanel, "Visibility", reveal, 40);
		length = new NumericUpDown
		{
			Minimum = 1m,
			Maximum = 65536m,
			Value = 24m,
			ThousandsSeparator = true
		};
		Row(tableLayoutPanel, "Generated length", length, 50);
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			WrapContents = true
		};
		lower = Box("a–z", check: true);
		upper = Box("A–Z", check: true);
		digits = Box("0–9", check: true);
		punctuation = Box("Symbols", check: true);
		CheckBox[] array = new CheckBox[4] { lower, upper, digits, punctuation };
		foreach (CheckBox value in array)
		{
			flowLayoutPanel.Controls.Add(value);
		}
		Row(tableLayoutPanel, "Characters", flowLayoutPanel, 50);
		symbols = Field("!@#$%^&*()-_=+[]{}:,.?", 94, colors);
		Row(tableLayoutPanel, "Allowed symbols", symbols, 50);
		ambiguous = Box("Exclude lookalikes (I, l, 1, O, 0…)", check: false);
		Row(tableLayoutPanel, "Readability", ambiguous, 44);
		Button button = new Button
		{
			Text = "Generate new password",
			AutoSize = true,
			Height = 34
		};
		button.Click += delegate
		{
			try
			{
				password.Text = PasswordGenerator.Generate(new GeneratorOptions
				{
					Length = (int)length.Value,
					Lower = lower.Checked,
					Upper = upper.Checked,
					Digits = digits.Checked,
					Symbols = punctuation.Checked,
					ExcludeAmbiguous = ambiguous.Checked,
					SymbolCharacters = symbols.Text
				});
			}
			catch (Exception ex)
			{
				ExplainedMessageBox.Show(this, ex.Message, "Generator options");
			}
		};
		Row(tableLayoutPanel, "", button, 48);
		if (!generator)
		{
			Row(tableLayoutPanel, "Notes", notes, 100);
		}
		Label field = new Label
		{
			Text = "Generation is local and uses cryptographic randomness. Sites may reject long passwords or certain symbols. No automatic website password change or autofill is performed.",
			Dock = DockStyle.Top,
			Height = 65,
			ForeColor = colors.Muted
		};
		Row(tableLayoutPanel, "", field, 75);
		FlowLayoutPanel flowLayoutPanel2 = new FlowLayoutPanel
		{
			Dock = DockStyle.Bottom,
			Height = 48,
			FlowDirection = FlowDirection.RightToLeft
		};
		Button button2 = new Button
		{
			Text = (generator ? "Copy generated password" : "Save entry"),
			AutoSize = true,
			Height = 36
		};
		button2.Click += delegate
		{
			try
			{
				passwordEditor.Value.Title = (generator ? "Generated password" : passwordEditor.title.Text);
				passwordEditor.Value.Username = passwordEditor.username.Text;
				passwordEditor.Value.Password = passwordEditor.password.Text;
				passwordEditor.Value.Website = passwordEditor.website.Text;
				passwordEditor.Value.Notes = passwordEditor.notes.Text;
				passwordEditor.Value.Validate();
				passwordEditor.DialogResult = DialogResult.OK;
				passwordEditor.Close();
			}
			catch (Exception ex)
			{
				ExplainedMessageBox.Show(passwordEditor, ex.Message, "Check entry");
			}
		};
		flowLayoutPanel2.Controls.Add(button2);
		flowLayoutPanel2.Controls.Add(new Button
		{
			Text = "Cancel",
			DialogResult = DialogResult.Cancel,
			AutoSize = true,
			Height = 36
		});
		base.Controls.Add(panel);
		base.Controls.Add(flowLayoutPanel2);
		base.AcceptButton = button2;
		base.FormClosed += delegate
		{
			password.Clear();
			notes.Clear();
			username.Clear();
		};
	}

	private CheckBox Box(string text, bool check)
	{
		CheckBox checkBox = new CheckBox();
		checkBox.Text = text;
		checkBox.Checked = check;
		checkBox.AutoSize = true;
		checkBox.ForeColor = ForeColor;
		checkBox.Margin = new Padding(0, 7, 14, 0);
		return checkBox;
	}

	private static TextBox Field(string text, int max, Palette colors)
	{
		TextBox textBox = new TextBox();
		textBox.Text = text;
		textBox.MaxLength = max;
		textBox.BackColor = colors.Surface2;
		textBox.ForeColor = colors.Text;
		textBox.BorderStyle = BorderStyle.FixedSingle;
		return textBox;
	}

	private static void Row(TableLayoutPanel rows, string name, Control field, int height)
	{
		int row = rows.RowCount++;
		rows.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
		rows.Controls.Add(new Label
		{
			Text = name,
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleLeft
		}, 0, row);
		field.Dock = DockStyle.Fill;
		field.Margin = new Padding(0, 8, 0, 8);
		field.AccessibleName = name;
		rows.Controls.Add(field, 1, row);
	}
}
