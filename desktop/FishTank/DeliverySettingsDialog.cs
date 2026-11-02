using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FishTank;

public sealed class DeliverySettingsDialog : Form
{
	private TextBox origin;

	private TextBox token;

	private Label status;

	private FlowLayoutPanel buttons;

	private bool working;

	public DeliverySettingsDialog()
	{
		Text = "FishTank · Your email/SMS server";
		base.Width = 790;
		base.Height = 530;
		base.StartPosition = FormStartPosition.CenterParent;
		Font = new Font("Segoe UI", 10f);
		base.Padding = new Padding(22);
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			Height = 240,
			ColumnCount = 2
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 75f));
		tableLayoutPanel.Controls.Add(new Label
		{
			Text = "HTTPS server origin",
			Dock = DockStyle.Fill
		}, 0, 0);
		origin = new TextBox
		{
			Dock = DockStyle.Fill,
			Text = "https://mfa.example.com/"
		};
		tableLayoutPanel.Controls.Add(origin, 1, 0);
		tableLayoutPanel.Controls.Add(new Label
		{
			Text = "Private device token",
			Dock = DockStyle.Fill
		}, 0, 1);
		token = new TextBox
		{
			Dock = DockStyle.Fill,
			UseSystemPasswordChar = true,
			MaxLength = 43
		};
		tableLayoutPanel.Controls.Add(token, 1, 1);
		Label control = new Label
		{
			Text = "Host the included Debian verification service, provision a device with its fixed email/phone, then enter the token privately here. SMTP/SMS delivery credentials stay on the server. Keep an offline authenticator/recovery option; it is required before enabling these alternatives.",
			Dock = DockStyle.Fill
		};
		tableLayoutPanel.Controls.Add(control, 0, 2);
		tableLayoutPanel.SetColumnSpan(control, 2);
		DeliveryConfiguration deliveryConfiguration = SelfHostedMfa.Load();
		if (deliveryConfiguration != null)
		{
			origin.Text = deliveryConfiguration.Origin;
			token.Text = deliveryConfiguration.DeviceToken;
		}
		status = new Label
		{
			Dock = DockStyle.Bottom,
			Height = 85
		};
		buttons = new FlowLayoutPanel
		{
			Dock = DockStyle.Bottom,
			Height = 100
		};
		Add("Test and save server", delegate
		{
			Save();
		});
		Add("Verify/enroll email or phone", delegate
		{
			Enroll();
		});
		Add("Close", delegate
		{
			Close();
		});
		base.Controls.Add(tableLayoutPanel);
		base.Controls.Add(status);
		base.Controls.Add(buttons);
		RefreshStatus();
		base.FormClosing += delegate(object s, FormClosingEventArgs e)
		{
			if (working)
			{
				e.Cancel = true;
			}
		};
		base.FormClosed += delegate
		{
			token.Clear();
		};
	}

	private void Add(string label, Action action)
	{
		Button button = new Button();
		button.Text = label;
		button.AutoSize = true;
		button.Height = 36;
		Button button2 = button;
		button2.Click += delegate
		{
			if (working)
			{
				return;
			}
			try
			{
				action();
			}
			catch (Exception ex)
			{
				status.Text = ex.Message;
			}
		};
		buttons.Controls.Add(button2);
	}

	private void RefreshStatus()
	{
		DeliveryConfiguration deliveryConfiguration = SelfHostedMfa.Load();
		status.Text = ((deliveryConfiguration == null) ? "Not configured. No email/SMS messages can be sent." : ("Server saved · Email enrolled: " + deliveryConfiguration.EmailEnrolled + " · SMS enrolled: " + deliveryConfiguration.SmsEnrolled + ". Each channel must prove destination ownership before sign-in use."));
	}

	private async void Save()
	{
		if (ExplainedMessageBox.Show(this, "Connect to the entered HTTPS origin with this device token? Only configure a server you own and trust. Replacing the connection resets channel enrollment.", "Configure verification server", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) != DialogResult.Yes)
		{
			return;
		}
		MfaCredentials credentials = MfaCredentialsDialog.Prompt(this, factor: true);
		try
		{
			if (credentials == null)
			{
				return;
			}
			working = true;
			buttons.Enabled = false;
			string url = origin.Text;
			string key = token.Text;
			try
			{
				int num = default(int);
				_ = num;
				_ = 0;
				try
				{
					await Task.Run(delegate
					{
						SelfHostedMfa.Configure(credentials.Password, credentials.Factor, url, key);
					});
					RefreshStatus();
				}
				catch (Exception ex)
				{
					status.Text = ex.Message;
				}
			}
			finally
			{
				working = false;
				buttons.Enabled = true;
				key = null;
			}
		}
		finally
		{
			if (credentials != null)
			{
				((IDisposable)credentials).Dispose();
			}
		}
	}

	private async void Enroll()
	{
		using MfaCredentials initial = MfaCredentialsDialog.Prompt(this, factor: false);
		if (initial == null)
		{
			return;
		}
		DeliveryChallengeDialog challenge = new DeliveryChallengeDialog(initial.Password, "enroll");
		try
		{
			if (challenge.ShowDialog(this) != DialogResult.OK)
			{
				return;
			}
			MfaCredentials fresh = MfaCredentialsDialog.Prompt(this, factor: true);
			try
			{
				if (fresh == null)
				{
					return;
				}
				working = true;
				buttons.Enabled = false;
				try
				{
					int num = default(int);
					_ = num;
					_ = 0;
					try
					{
						await Task.Run(delegate
						{
							SelfHostedMfa.EnrollChannel(fresh.Password, fresh.Factor, challenge.Channel, challenge.ChallengeId, challenge.Code);
						});
						RefreshStatus();
					}
					catch (Exception ex)
					{
						status.Text = ex.Message;
					}
				}
				finally
				{
					challenge.Code = null;
					challenge.FactorValue = null;
					working = false;
					buttons.Enabled = true;
				}
			}
			finally
			{
				if (fresh != null)
				{
					((IDisposable)fresh).Dispose();
				}
			}
		}
		finally
		{
			if (challenge != null)
			{
				((IDisposable)challenge).Dispose();
			}
		}
	}
}
