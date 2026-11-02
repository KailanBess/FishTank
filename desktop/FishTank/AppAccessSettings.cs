using System.Windows.Forms;

namespace FishTank;

public static class AppAccessSettings
{
	public static void Change(IWin32Window owner)
	{
		bool enabled = AppAccess.Enabled;
		string replacement = null;
		using (MasterPrompt masterPrompt = new MasterPrompt(create: true, appAccess: true))
		{
			masterPrompt.Text = (enabled ? "New FishTank app password (separate from vault)" : "Set FishTank app password (separate from vault)");
			if (masterPrompt.ShowDialog(owner) != DialogResult.OK)
			{
				return;
			}
			replacement = masterPrompt.Secret;
			masterPrompt.Secret = null;
		}
		try
		{
			using MfaCredentials mfaCredentials = (enabled ? MfaCredentialsDialog.Prompt(owner, AppAccess.Mfa.Configured) : new MfaCredentials());
			if (mfaCredentials != null)
			{
				AppAccess.SetPassword(mfaCredentials.Password, replacement, mfaCredentials.Factor);
				ExplainedMessageBox.Show(owner, "App password saved. Existing MFA enrollment and prompt settings are preserved. Your password vault still uses its separate master passphrase.", "App access");
			}
		}
		finally
		{
			replacement = null;
		}
	}

	public static void Remove(IWin32Window owner)
	{
		if (!AppAccess.Enabled || ExplainedMessageBox.Show(owner, "Remove the app access password and any MFA enrollment/recovery codes? The password-manager master passphrase remains required.", "Confirm removal", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) != DialogResult.Yes)
		{
			return;
		}
		using MfaCredentials mfaCredentials = MfaCredentialsDialog.Prompt(owner, AppAccess.Mfa.Configured);
		if (mfaCredentials != null)
		{
			AppAccess.RemovePassword(mfaCredentials.Password, mfaCredentials.Factor);
		}
	}
}
