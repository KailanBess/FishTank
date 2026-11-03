using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FishTank;

public sealed class AppUnlockDialog : Form
{
	private TextBox password;

	private TextBox factor;

	private Label message;

	private Label factorLabel;

	private Button unlock;

	private Button delivery;

	private bool working;

	public AppUnlockDialog()
	{
		Text = "Unlock FishTank";
		base.Width = 540;
		base.Height = 310;
		base.StartPosition = FormStartPosition.CenterScreen;
		base.FormBorderStyle = FormBorderStyle.FixedDialog;
		base.MaximizeBox = false;
		base.MinimizeBox = false;
		BackColor = Color.FromArgb(19, 23, 33);
		ForeColor = Color.White;
		Font = new Font("Segoe UI", 11f);
		base.Padding = new Padding(24);
		Label value = new Label
		{
			Text = "Welcome back to FishTank",
			Dock = DockStyle.Top,
			Height = 48,
			Font = new Font("Segoe UI", 20f, FontStyle.Bold)
		};
		message = new Label
		{
			Text = "Enter your app password. Your password vault uses its own master passphrase.",
			Dock = DockStyle.Top,
			Height = 64
		};
		password = new TextBox
		{
			UseSystemPasswordChar = true,
			MaxLength = 1024,
			Dock = DockStyle.Top,
			AccessibleName = "App password"
		};
		factorLabel = new Label
		{
			Text = "Authenticator code or one-use recovery code",
			Dock = DockStyle.Top,
			Height = 38,
			Padding = new Padding(0, 12, 0, 0)
		};
		factor = new TextBox
		{
			UseSystemPasswordChar = true,
			MaxLength = 256,
			Dock = DockStyle.Top,
			AccessibleName = "Authenticator or recovery code"
		};
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Bottom,
			Height = 44,
			FlowDirection = FlowDirection.RightToLeft
		};
		unlock = new Button
		{
			Text = "Unlock",
			Width = 110,
			Height = 36
		};
		Button button = unlock;
		EventHandler value2 = async delegate
		{
			await Unlock();
		};
		button.Click += value2;
		Button value3 = new Button
		{
			Text = "Exit",
			Width = 100,
			Height = 36,
			DialogResult = DialogResult.Cancel
		};
		delivery = new Button
		{
			Text = "Email / phone code…",
			AutoSize = true,
			Height = 36
		};
		delivery.Click += delegate
		{
			if (working)
			{
				return;
			}
			using DeliveryChallengeDialog deliveryChallengeDialog = new DeliveryChallengeDialog(password.Text, "signin");
			if (deliveryChallengeDialog.ShowDialog(this) == DialogResult.OK)
			{
				factor.Text = deliveryChallengeDialog.FactorValue;
			}
		};
		flowLayoutPanel.Controls.Add(delivery);
        var passkey = new Button { Text = "Passkey…", AutoSize = true, Height = 36 };
        passkey.Click += delegate { if (working) return; using (var prompt = new PasskeyDialog(password.Text, "signin")) if (prompt.ShowDialog(this) == DialogResult.OK) factor.Text = prompt.FactorValue; };
        flowLayoutPanel.Controls.Add(passkey);
        flowLayoutPanel.Height = 85;
        Height += 45;
		flowLayoutPanel.Controls.Add(unlock);
		flowLayoutPanel.Controls.Add(value3);
		base.Controls.Add(factor);
		base.Controls.Add(factorLabel);
		base.Controls.Add(password);
		base.Controls.Add(message);
		base.Controls.Add(value);
		base.Controls.Add(flowLayoutPanel);
		base.AcceptButton = unlock;
		RefreshMfa();
		base.FormClosing += delegate(object s, FormClosingEventArgs e)
		{
			if (working)
			{
				e.Cancel = true;
			}
			else
			{
				password.Clear();
				factor.Clear();
			}
		};
	}

	private void RefreshMfa()
	{
		bool askOnSignIn = AppAccess.Mfa.AskOnSignIn;
		TextBox textBox = factor;
		Label label = factorLabel;
		bool flag = (delivery.Visible = askOnSignIn);
		bool visible = (label.Visible = flag);
		textBox.Visible = visible;
		base.Height = (askOnSignIn ? 385 : 310);
	}

	private async Task Unlock()
	{
		if (working)
		{
			return;
		}
		string value = password.Text;
		string code = factor.Text;
		password.Clear();
		factor.Clear();
		working = true;
		unlock.Enabled = false;
		message.Text = "Verifying app access…";
		try
		{
			int num = default(int);
			_ = num;
			_ = 0;
			try
			{
				if (await Task.Run(() => AppAccess.Authenticate(value, code)))
				{
					base.DialogResult = DialogResult.OK;
					working = false;
					Close();
				}
				else
				{
					RefreshMfa();
					message.Text = "Password or MFA/recovery code is incorrect, expired or used. Repeated attempts are throttled.";
				}
			}
			catch (Exception ex)
			{
				message.Text = ex.Message;
			}
		}
		finally
		{
			value = null;
			code = null;
			working = false;
			unlock.Enabled = true;
		}
	}

	public static bool Prompt()
	{
		if (!AppAccess.Enabled)
		{
			return true;
		}
		try
		{
			using AppUnlockDialog appUnlockDialog = new AppUnlockDialog();
			return appUnlockDialog.ShowDialog() == DialogResult.OK;
		}
		catch (Exception ex)
		{
			ExplainedMessageBox.Show("FishTank access remains locked.\n\n" + ex.Message, "Cannot read app access settings", MessageBoxButtons.OK, MessageBoxIcon.Hand);
			return false;
		}
	}
}
