using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FishTank;

public sealed class MfaEnrollmentDialog : Form
{
	private readonly Palette colors;

	private readonly string secret;

	private TextBox currentPassword;

	private TextBox oldFactor;

	private TextBox newCode;

	private Label status;

	private Button enroll;

	private readonly SecretClipboard clipboard = new SecretClipboard();

	private bool working;

	public string[] RecoveryCodes { get; private set; }

	public MfaEnrollmentDialog(bool replacing, Palette colors)
	{
		this.colors = colors;
		secret = Totp.NewSecret();
		Text = (replacing ? "Replace FishTank authenticator" : "Set up FishTank authenticator");
		base.Width = 780;
		base.Height = (replacing ? 740 : 675);
		MinimumSize = new Size(700, 650);
		base.StartPosition = FormStartPosition.CenterParent;
		BackColor = colors.Background;
		ForeColor = colors.Text;
		Font = new Font("Segoe UI", 10f);
		base.Padding = new Padding(22);
		Label value = new Label
		{
			Text = "Connect your authenticator",
			Dock = DockStyle.Top,
			Height = 52,
			Font = new Font("Segoe UI", 22f, FontStyle.Bold)
		};
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
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 73f));
		panel.Controls.Add(tableLayoutPanel);
		Row(tableLayoutPanel, "1 · Add an account", new Label
		{
			Text = "In an external authenticator app, add a time-based account manually. Use the setup key below, SHA-1, 6 digits and a 30-second period. Prefer another device.",
			ForeColor = colors.Muted
		}, 80);
		TextBox textBox = Field(mask: false);
		textBox.ReadOnly = true;
		textBox.Text = secret;
		Row(tableLayoutPanel, "Secret setup key", textBox, 50);
		Button button = new Button
		{
			Text = "Copy setup key",
			AutoSize = true
		};
		button.Click += delegate
		{
			try
			{
				clipboard.Copy(secret);
			}
			catch (Exception ex)
			{
				status.Text = ex.Message;
			}
		};
		Row(tableLayoutPanel, "", button, 42);
		TextBox textBox2 = Field(mask: false);
		textBox2.ReadOnly = true;
		textBox2.Text = Totp.EnrollmentUri(secret, Environment.UserName);
		Row(tableLayoutPanel, "Local otpauth URI", textBox2, 50);
		Row(tableLayoutPanel, "Keep the key private", new Label
		{
			Text = "The setup key generates every code for this enrollment. FishTank does not send it to a service or online QR generator. No QR image is required; manual key entry is supported.",
			ForeColor = colors.Muted
		}, 75);
		newCode = Field(mask: false);
		newCode.MaxLength = 6;
		Row(tableLayoutPanel, "2 · Code from new account", newCode, 50);
		currentPassword = Field(mask: true);
		currentPassword.MaxLength = 1024;
		Row(tableLayoutPanel, "3 · Current app password", currentPassword, 50);
		oldFactor = Field(mask: true);
		oldFactor.MaxLength = 128;
		if (replacing)
		{
			Row(tableLayoutPanel, "Existing MFA / recovery code", oldFactor, 55);
		}
		status = new Label
		{
			Dock = DockStyle.Bottom,
			Height = 65,
			ForeColor = colors.Muted,
			Text = "Enrollment is saved only after both the new authenticator and current access credentials verify. Cancel leaves the old enrollment unchanged."
		};
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Bottom,
			Height = 45,
			FlowDirection = FlowDirection.RightToLeft
		};
		enroll = new Button
		{
			Text = "Confirm and enable MFA",
			AutoSize = true,
			Height = 35
		};
		enroll.Click += async delegate
		{
			await Enroll();
		};
		flowLayoutPanel.Controls.Add(enroll);
		flowLayoutPanel.Controls.Add(new Button
		{
			Text = "Cancel",
			DialogResult = DialogResult.Cancel,
			AutoSize = true,
			Height = 35
		});
		base.Controls.Add(panel);
		base.Controls.Add(status);
		base.Controls.Add(flowLayoutPanel);
		base.Controls.Add(value);
		base.AcceptButton = enroll;
		base.FormClosing += delegate(object s, FormClosingEventArgs e)
		{
			if (working)
			{
				e.Cancel = true;
			}
		};
		base.FormClosed += delegate
		{
			currentPassword.Clear();
			oldFactor.Clear();
			newCode.Clear();
			clipboard.Dispose();
		};
	}

	private TextBox Field(bool mask)
	{
		TextBox textBox = new TextBox();
		textBox.UseSystemPasswordChar = mask;
		textBox.BackColor = colors.Surface2;
		textBox.ForeColor = colors.Text;
		return textBox;
	}

	private static void Row(TableLayoutPanel table, string label, Control field, int height)
	{
		int row = table.RowCount++;
		table.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
		table.Controls.Add(new Label
		{
			Text = label,
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleLeft
		}, 0, row);
		field.Dock = DockStyle.Fill;
		field.Margin = new Padding(0, 6, 0, 6);
		field.AccessibleName = label;
		table.Controls.Add(field, 1, row);
	}

	private async Task Enroll()
	{
		if (working)
		{
			return;
		}
		string password = currentPassword.Text;
		string old = oldFactor.Text;
		string code = newCode.Text;
		currentPassword.Clear();
		oldFactor.Clear();
		newCode.Clear();
		working = true;
		enroll.Enabled = false;
		status.Text = "Verifying enrollment…";
		try
		{
			int num = default(int);
			_ = num;
			_ = 0;
			try
			{
				RecoveryCodes = await Task.Run(() => AppAccess.EnrollMfa(password, old, secret, code, DateTime.UtcNow));
				working = false;
				base.DialogResult = DialogResult.OK;
				Close();
			}
			catch (Exception ex)
			{
				status.Text = ex.Message;
			}
		}
		finally
		{
			password = null;
			old = null;
			code = null;
			working = false;
			enroll.Enabled = true;
		}
	}
}
