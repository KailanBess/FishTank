using System.Windows.Forms;

namespace FishTank;

public static class ExplainedMessageBox
{
	public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
	{
		return Show(null, text, caption, buttons, icon);
	}

	public static DialogResult Show(IWin32Window owner, string text)
	{
		return Show(owner, text, "FishTank", MessageBoxButtons.OK, MessageBoxIcon.None);
	}

	public static DialogResult Show(IWin32Window owner, string text, string caption)
	{
		return Show(owner, text, caption, MessageBoxButtons.OK, MessageBoxIcon.None);
	}

	public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
	{
		using ExplanationMessage explanationMessage = new ExplanationMessage(text, caption, buttons, icon);
		DialogResult dialogResult = ((owner == null) ? explanationMessage.ShowDialog() : explanationMessage.ShowDialog(owner));
		return (buttons == MessageBoxButtons.YesNo && dialogResult != DialogResult.Yes) ? DialogResult.No : dialogResult;
	}
}
