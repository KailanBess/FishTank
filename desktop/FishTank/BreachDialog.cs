using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FishTank;

public sealed class BreachDialog : Form
{
	private readonly Palette colors;

	private TextBox results;

	private TextBox email;

	private TextBox key;

	private TextBox password;

	private bool working;

	public BreachDialog(Palette colors)
	{
		this.colors = colors;
		Text = "FishTank · Breach checks";
		base.Width = 880;
		base.Height = 740;
		MinimumSize = new Size(750, 650);
		base.StartPosition = FormStartPosition.CenterParent;
		Font = new Font("Segoe UI", 10f);
		BackColor = colors.Background;
		ForeColor = colors.Text;
		base.Padding = new Padding(22);
		Label value = new Label
		{
			Text = "Check for known exposure",
			Dock = DockStyle.Top,
			Height = 55,
			Font = new Font("Segoe UI", 22f, FontStyle.Bold)
		};
		TabControl tabControl = new TabControl
		{
			Dock = DockStyle.Top,
			Height = 270
		};
		TabPage tabPage = new TabPage("Email address")
		{
			BackColor = colors.Surface,
			ForeColor = colors.Text,
			Padding = new Padding(14)
		};
		TabPage tabPage2 = new TabPage("Password")
		{
			BackColor = colors.Surface,
			ForeColor = colors.Text,
			Padding = new Padding(14)
		};
		tabControl.TabPages.Add(tabPage);
		tabControl.TabPages.Add(tabPage2);
		TableLayoutPanel tableLayoutPanel = Rows();
		email = Field(masked: false);
		key = Field(masked: true);
		email.MaxLength = 254;
		key.MaxLength = 32;
		Row(tableLayoutPanel, "Your email address", email, 44);
		Row(tableLayoutPanel, "Your HIBP API key", key, 44);
		Label field = new Label
		{
			Text = "Email lookup sends the full address to Have I Been Pwned over HTTPS. A user-provided HIBP API subscription/key is required; it is not saved. Look up your own address or one you are authorized to check.",
			ForeColor = colors.Muted,
			Dock = DockStyle.Fill
		};
		Row(tableLayoutPanel, "Privacy", field, 80);
		Action action = delegate
		{
			CheckEmail();
		};
		Row(tableLayoutPanel, "", Button("Check email", action), 45);
		tabPage.Controls.Add(tableLayoutPanel);
		TableLayoutPanel tableLayoutPanel2 = Rows();
		password = Field(masked: true);
		password.MaxLength = 65536;
		Row(tableLayoutPanel2, "Password to check", password, 44);
		Label field2 = new Label
		{
			Text = "The password is hashed locally. Only the first five SHA-1 hash characters are sent to Pwned Passwords; the full password and hash are never sent. The provider sees your IP and a partial hash. Padded responses are requested. SHA-1 is used only for this lookup protocol, never for vault encryption.",
			ForeColor = colors.Muted,
			Dock = DockStyle.Fill
		};
		Row(tableLayoutPanel2, "Privacy", field2, 115);
		Row(tableLayoutPanel2, "", Button("Check password", delegate
		{
			CheckPassword();
		}), 45);
		tabPage2.Controls.Add(tableLayoutPanel2);
		results = new TextBox
		{
			Dock = DockStyle.Fill,
			ReadOnly = true,
			Multiline = true,
			ScrollBars = ScrollBars.Vertical,
			BackColor = colors.Surface,
			ForeColor = colors.Text,
			Font = Font,
			BorderStyle = BorderStyle.None,
			Text = "No lookup has been made.\r\n\r\nA missing result cannot prove your information is safe. HIBP does not cover every leak or reveal leaked passwords. This feature does not search private messages, identity documents or all dark-web sources."
		};
		LinkLabel linkLabel = new LinkLabel
		{
			Dock = DockStyle.Bottom,
			Height = 42,
			Text = "Breach data: Have I Been Pwned · API and privacy information",
			LinkColor = colors.Accent,
			ActiveLinkColor = colors.Accent,
			TextAlign = ContentAlignment.MiddleLeft
		};
		linkLabel.LinkClicked += delegate
		{
			Process.Start(new ProcessStartInfo("https://haveibeenpwned.com/API/v3")
			{
				UseShellExecute = true
			});
		};
		base.Controls.Add(results);
		base.Controls.Add(linkLabel);
		base.Controls.Add(tabControl);
		base.Controls.Add(value);
		base.FormClosing += delegate(object s, FormClosingEventArgs e)
		{
			if (working)
			{
				e.Cancel = true;
			}
			else
			{
				email.Clear();
				key.Clear();
				password.Clear();
			}
		};
	}

	private TextBox Field(bool masked)
	{
		TextBox textBox = new TextBox();
		textBox.UseSystemPasswordChar = masked;
		textBox.BackColor = colors.Surface2;
		textBox.ForeColor = colors.Text;
		return textBox;
	}

	private static TableLayoutPanel Rows()
	{
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel();
		tableLayoutPanel.Dock = DockStyle.Fill;
		tableLayoutPanel.ColumnCount = 2;
		TableLayoutPanel tableLayoutPanel2 = tableLayoutPanel;
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 75f));
		return tableLayoutPanel2;
	}

	private static void Row(TableLayoutPanel rows, string text, Control field, int height)
	{
		int row = rows.RowCount++;
		rows.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
		rows.Controls.Add(new Label
		{
			Text = text,
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleLeft
		}, 0, row);
		field.Dock = DockStyle.Fill;
		field.Margin = new Padding(0, 5, 0, 5);
		field.AccessibleName = text;
		rows.Controls.Add(field, 1, row);
	}

	private Button Button(string text, Action action)
	{
		Button button = new Button();
		button.Text = text;
		button.AutoSize = true;
		button.BackColor = colors.Surface2;
		button.ForeColor = colors.Text;
		button.FlatStyle = FlatStyle.Flat;
		Button button2 = button;
		button2.Click += delegate
		{
			if (!working)
			{
				action();
			}
		};
		return button2;
	}

	private async void CheckPassword()
	{
		string secret = password.Text;
		try
		{
			BreachChecks.PasswordHash(secret);
		}
		catch (Exception ex)
		{
			results.Text = ex.Message;
			return;
		}
		if (ExplainedMessageBox.Show(this, "Send only the first five hash characters to api.pwnedpasswords.com to check this password?\n\nThe service sees your IP address and partial hash. The full password and hash remain local. No results or secrets are saved by FishTank.", "Approve this online lookup", MessageBoxButtons.YesNo, MessageBoxIcon.Asterisk) != DialogResult.Yes)
		{
			return;
		}
		working = true;
		results.Text = "Checking Pwned Passwords…";
		try
		{
			int num = default(int);
			_ = num;
			_ = 0;
			try
			{
				long count = await Task.Run(() => BreachChecks.CheckPassword(secret, consent: true, null));
				results.Text = ((count > 0) ? ("This password appears in the Pwned Passwords dataset (reported occurrences: " + count + ").\n\nDo not reuse it. Change it on the relevant website and enable multifactor authentication where available. FishTank has not changed any account password.") : "No match was returned in the checked Pwned Passwords range.\n\nThis is not proof the password is secret, strong or safe to reuse.");
			}
			catch (Exception ex2)
			{
				results.Text = ex2.Message;
			}
		}
		finally
		{
			password.Clear();
			secret = null;
			working = false;
		}
	}

	private async void CheckEmail()
	{
		string apiKey = key.Text;
		string address;
		try
		{
			address = BreachChecks.NormalizeEmail(email.Text);
		}
		catch (Exception ex)
		{
			results.Text = ex.Message;
			return;
		}
		if (ExplainedMessageBox.Show(this, "Send the full email address below and your HIBP API key to haveibeenpwned.com over HTTPS?\n\n" + address + "\n\nThe provider sees the address, API key and your IP. Use only an address you own or are authorized to check. FishTank will not save the query, key or results.", "Approve this online lookup", MessageBoxButtons.YesNo, MessageBoxIcon.Asterisk) != DialogResult.Yes)
		{
			return;
		}
		working = true;
		results.Text = "Checking Have I Been Pwned…";
		try
		{
			int num = default(int);
			_ = num;
			_ = 0;
			try
			{
				List<Dictionary<string, object>> breaches = await Task.Run(() => BreachChecks.CheckEmail(address, apiKey, consent: true, null));
				StringBuilder text = new StringBuilder();
				if (breaches.Count == 0)
				{
					text.AppendLine("No known breach was returned for this address. This does not prove it has never leaked.");
				}
				else
				{
					text.AppendLine("Have I Been Pwned returned " + breaches.Count + " breach records:\n");
					foreach (Dictionary<string, object> item in breaches)
					{
						text.AppendLine(Value(item, "Name") + " · " + Value(item, "BreachDate"));
						text.AppendLine("Exposed data categories: " + Value(item, "DataClasses"));
						text.AppendLine("Verified record: " + Value(item, "IsVerified"));
						text.AppendLine();
					}
					text.AppendLine("Review the affected accounts, use distinct passwords and enable multifactor authentication. A record does not prove the current password is compromised.");
				}
				text.AppendLine("\nSource: Have I Been Pwned. Coverage is incomplete; sensitive/retired breaches may be omitted by the API.");
				results.Text = text.ToString();
			}
			catch (Exception ex2)
			{
				results.Text = ex2.Message;
			}
		}
		finally
		{
			key.Clear();
			apiKey = null;
			working = false;
		}
	}

	private static string Value(Dictionary<string, object> entry, string key)
	{
		if (!entry.TryGetValue(key, out var value) || value == null)
		{
			return "Unknown";
		}
		if (value is IEnumerable enumerable && !(value is string))
		{
			List<string> list = new List<string>();
			foreach (object item in enumerable)
			{
				list.Add(Convert.ToString(item));
			}
			return string.Join(", ", list.ToArray());
		}
		return Convert.ToString(value);
	}
}
