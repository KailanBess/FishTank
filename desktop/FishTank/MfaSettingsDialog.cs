using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FishTank;

public sealed class MfaSettingsDialog : Form
{
	private readonly Palette colors;

	private Label status;

	private CheckBox require;

	private FlowLayoutPanel actions;

	private bool working;

	public MfaSettingsDialog(Palette colors)
	{
		this.colors = colors;
		Text = "FishTank · Multifactor authentication";
		base.Width = 800;
		base.Height = 540;
		MinimumSize = new Size(740, 500);
		base.StartPosition = FormStartPosition.CenterParent;
		BackColor = colors.Background;
		ForeColor = colors.Text;
		Font = new Font("Segoe UI", 11f);
		base.Padding = new Padding(24);
		Label value = new Label
		{
			Text = "Choose how you sign in",
			Dock = DockStyle.Top,
			Height = 58,
			Font = new Font("Segoe UI", 22f, FontStyle.Bold)
		};
		status = new Label
		{
			Dock = DockStyle.Top,
			Height = 74,
			ForeColor = colors.Muted
		};
		require = new CheckBox
		{
			Text = "Require MFA when opening FishTank",
			Dock = DockStyle.Top,
			Height = 46,
			AccessibleName = "Require MFA on app sign-in"
		};
		Label value2 = new Label
		{
			Dock = DockStyle.Fill,
			ForeColor = colors.Muted,
			Text = "Authenticator-app codes (TOTP) and single-use recovery codes work offline. Your app password is still required.\r\n\r\nTurn the sign-in requirement off to stop MFA prompts at startup; enrollment remains saved for turning it back on. Sensitive security changes still require the password and an existing MFA/recovery code.\r\n\r\nThis local app lock does not protect against someone controlling the same Windows account. Your password vault keeps its own master passphrase."
		};
		actions = new FlowLayoutPanel
		{
			Dock = DockStyle.Bottom,
			Height = 106,
			WrapContents = true
		};
		Action action = delegate
		{
			Setup();
		};
		Add("Set up / replace authenticator", action);
		Add("Apply sign-in prompt setting", delegate
		{
			ApplyPrompt();
		});
		Add("Generate new recovery codes", delegate
		{
			Regenerate();
		});
		Add("Remove MFA enrollment", delegate
		{
			Forget();
		});
		Add("Email / phone server", delegate
		{
			using DeliverySettingsDialog deliverySettingsDialog = new DeliverySettingsDialog();
			deliverySettingsDialog.ShowDialog(this);
		});
		Add("Close", delegate
		{
			Close();
		});
		base.Controls.Add(value2);
		base.Controls.Add(require);
		base.Controls.Add(status);
		base.Controls.Add(actions);
		base.Controls.Add(value);
		RefreshState();
		base.FormClosing += delegate(object s, FormClosingEventArgs e)
		{
			if (working)
			{
				e.Cancel = true;
			}
		};
	}

	private void Add(string title, Action action)
	{
		Button button = new Button();
		button.Text = title;
		button.AutoSize = true;
		button.Height = 36;
		button.FlatStyle = FlatStyle.Flat;
		button.BackColor = colors.Surface2;
		button.ForeColor = colors.Text;
		button.Padding = new Padding(7, 2, 7, 2);
		button.Margin = new Padding(0, 0, 8, 8);
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
		actions.Controls.Add(button2);
	}

	private void RefreshState()
	{
		MfaStatus mfa = AppAccess.Mfa;
		require.Checked = mfa.AskOnSignIn;
		require.Enabled = AppAccess.Enabled && mfa.Configured;
		status.Text = ((!AppAccess.Enabled) ? "Set an app password in Settings before adding optional MFA." : ((!mfa.Configured) ? "MFA is not enrolled. Sign-in uses your app password only." : ("Authenticator enrolled · Sign-in MFA " + (mfa.AskOnSignIn ? "ON" : "OFF (password only)") + " · Recovery codes remaining: " + mfa.RecoveryCodesRemaining + "\r\nEach authenticator time step and recovery code can be accepted only once. Use a fresh code for another verification.")));
	}

	private void Setup()
	{
		if (!AppAccess.Enabled)
		{
			throw new InvalidOperationException("Set an app password first.");
		}
		using MfaEnrollmentDialog mfaEnrollmentDialog = new MfaEnrollmentDialog(AppAccess.Mfa.Configured, colors);
		if (mfaEnrollmentDialog.ShowDialog(this) == DialogResult.OK)
		{
			RefreshState();
			ShowCodes(mfaEnrollmentDialog.RecoveryCodes);
			Array.Clear(mfaEnrollmentDialog.RecoveryCodes, 0, mfaEnrollmentDialog.RecoveryCodes.Length);
		}
	}

	private void ShowCodes(string[] codes)
	{
		using RecoveryCodesDialog recoveryCodesDialog = new RecoveryCodesDialog(codes, colors);
		recoveryCodesDialog.ShowDialog(this);
	}

	private async void ApplyPrompt()
	{
		if (!AppAccess.Mfa.Configured)
		{
			status.Text = "Set up an authenticator first.";
			return;
		}
		bool ask = require.Checked;
		if (ask || ExplainedMessageBox.Show(this, "Stop requiring MFA at sign-in? Future sign-ins will need only the app password. Enrollment stays saved. Verification is required to apply this change.", "Turn off sign-in MFA", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) == DialogResult.Yes)
		{
			await Run(delegate(MfaCredentials credentials)
			{
				AppAccess.SetMfaPrompt(credentials.Password, credentials.Factor, ask, DateTime.UtcNow);
				return (string[])null;
			});
		}
		else
		{
			RefreshState();
		}
	}

	private async void Regenerate()
	{
		if (!AppAccess.Mfa.Configured)
		{
			status.Text = "Set up an authenticator first.";
		}
		else if (ExplainedMessageBox.Show(this, "Generate ten new recovery codes and invalidate the previous set?", "Replace recovery codes", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) == DialogResult.Yes)
		{
			await Run((MfaCredentials credentials) => AppAccess.RegenerateRecovery(credentials.Password, credentials.Factor, DateTime.UtcNow));
		}
	}

	private async void Forget()
	{
		if (AppAccess.Mfa.Configured && ExplainedMessageBox.Show(this, "Remove the authenticator and all recovery codes? The app password will remain enabled.", "Remove MFA", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) == DialogResult.Yes)
		{
			await Run(delegate(MfaCredentials credentials)
			{
				AppAccess.ForgetMfa(credentials.Password, credentials.Factor, DateTime.UtcNow);
				return (string[])null;
			});
		}
	}

	private async Task Run(Func<MfaCredentials, string[]> action)
	{
		MfaCredentials credentials = MfaCredentialsDialog.Prompt(this, factor: true);
		try
		{
			if (credentials == null)
			{
				return;
			}
			working = true;
			actions.Enabled = false;
			require.Enabled = false;
			status.Text = "Verifying current access and MFA…";
			try
			{
				int num = default(int);
				_ = num;
				_ = 0;
				try
				{
					string[] codes = await Task.Run(() => action(credentials));
					RefreshState();
					if (codes != null)
					{
						ShowCodes(codes);
						Array.Clear(codes, 0, codes.Length);
					}
				}
				catch (Exception ex)
				{
					status.Text = ex.Message;
				}
			}
			finally
			{
				working = false;
				actions.Enabled = true;
				require.Enabled = AppAccess.Mfa.Configured;
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
}
