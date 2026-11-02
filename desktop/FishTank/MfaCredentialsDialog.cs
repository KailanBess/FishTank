using System.Drawing;
using System.Windows.Forms;

namespace FishTank;

public sealed class MfaCredentialsDialog : Form
{
	private TextBox password;

	private TextBox factor;

	public MfaCredentials Value;

	public MfaCredentialsDialog(bool needFactor)
	{
		MfaCredentialsDialog mfaCredentialsDialog = this;
		Text = "Verify FishTank security change";
		base.Width = 570;
		base.Height = (needFactor ? 330 : 250);
		base.StartPosition = FormStartPosition.CenterParent;
		base.FormBorderStyle = FormBorderStyle.FixedDialog;
		base.MaximizeBox = (base.MinimizeBox = false);
		base.Padding = new Padding(22);
		Font = new Font("Segoe UI", 10f);
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1
		};
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 58f));
		tableLayoutPanel.Controls.Add(new Label
		{
			Text = (needFactor ? "Enter your current app password and a fresh authenticator or unused recovery code. This is required even when sign-in MFA prompts are off." : "Enter your current FishTank app password. This is separate from the password-vault master passphrase."),
			Dock = DockStyle.Fill
		});
		password = new TextBox
		{
			UseSystemPasswordChar = true,
			MaxLength = 1024,
			Dock = DockStyle.Top,
			AccessibleName = "Current app password"
		};
		tableLayoutPanel.Controls.Add(password);
		factor = new TextBox
		{
			UseSystemPasswordChar = true,
			MaxLength = 128,
			Dock = DockStyle.Top,
			AccessibleName = "Current MFA or recovery code"
		};
		if (needFactor)
		{
			tableLayoutPanel.Controls.Add(new Label
			{
				Text = "Authenticator / recovery code",
				Dock = DockStyle.Top,
				Height = 33,
				Padding = new Padding(0, 12, 0, 0)
			});
			tableLayoutPanel.Controls.Add(factor);
			Button button = new Button
			{
				Text = "Use email / phone code…",
				AutoSize = true
			};
			button.Click += delegate
			{
				using DeliveryChallengeDialog deliveryChallengeDialog = new DeliveryChallengeDialog(password.Text, "settings");
				if (deliveryChallengeDialog.ShowDialog(this) == DialogResult.OK)
				{
					factor.Text = deliveryChallengeDialog.FactorValue;
				}
			};
			tableLayoutPanel.Controls.Add(button);
		}
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Bottom,
			Height = 42,
			FlowDirection = FlowDirection.RightToLeft
		};
		Button button2 = new Button
		{
			Text = "Verify and continue",
			AutoSize = true,
			Height = 34
		};
		button2.Click += delegate
		{
			if (!string.IsNullOrEmpty(mfaCredentialsDialog.password.Text) && (!needFactor || !string.IsNullOrWhiteSpace(mfaCredentialsDialog.factor.Text)))
			{
				mfaCredentialsDialog.Value = new MfaCredentials
				{
					Password = mfaCredentialsDialog.password.Text,
					Factor = (needFactor ? mfaCredentialsDialog.factor.Text : null)
				};
				mfaCredentialsDialog.DialogResult = DialogResult.OK;
				mfaCredentialsDialog.Close();
			}
		};
		flowLayoutPanel.Controls.Add(button2);
		flowLayoutPanel.Controls.Add(new Button
		{
			Text = "Cancel",
			DialogResult = DialogResult.Cancel,
			AutoSize = true,
			Height = 34
		});
		base.Controls.Add(tableLayoutPanel);
		base.Controls.Add(flowLayoutPanel);
		base.AcceptButton = button2;
		base.FormClosed += delegate
		{
			password.Clear();
			factor.Clear();
		};
	}

	public static MfaCredentials Prompt(IWin32Window owner, bool factor)
	{
		using MfaCredentialsDialog mfaCredentialsDialog = new MfaCredentialsDialog(factor);
		if (mfaCredentialsDialog.ShowDialog(owner) != DialogResult.OK)
		{
			return null;
		}
		MfaCredentials value = mfaCredentialsDialog.Value;
		mfaCredentialsDialog.Value = null;
		return value;
	}
}
