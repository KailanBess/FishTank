using System.Drawing;
using System.Windows.Forms;

namespace FishTank;

public sealed class MasterPrompt : Form
{
	private TextBox master;

	private TextBox repeat;

	public string Secret;

	public MasterPrompt(bool create, bool appAccess = false)
	{
		MasterPrompt masterPrompt = this;
		Text = (create ? "Create your master passphrase" : "Unlock password vault");
		base.Width = 560;
		base.Height = (create ? 320 : 260);
		base.StartPosition = FormStartPosition.CenterParent;
		base.FormBorderStyle = FormBorderStyle.FixedDialog;
		base.MaximizeBox = false;
		base.MinimizeBox = false;
		Font = new Font("Segoe UI", 10f);
		base.Padding = new Padding(20);
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = (create ? 5 : 4)
		};
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 65f));
		tableLayoutPanel.Controls.Add(new Label
		{
			Text = ((!appAccess) ? (create ? "Use a unique passphrase of at least 12 characters. It cannot be reset or recovered if forgotten." : "Your master passphrase decrypts this local vault. It is never sent to a service.") : (create ? "Choose a unique app passphrase of at least 12 characters. It is separate from your password-vault master passphrase." : "Enter your current FishTank app password. It is never sent to a service.")),
			Dock = DockStyle.Fill
		}, 0, 0);
		master = new TextBox
		{
			UseSystemPasswordChar = true,
			MaxLength = 1024,
			Dock = DockStyle.Top,
			AccessibleName = "Master passphrase"
		};
		tableLayoutPanel.Controls.Add(master, 0, 1);
		if (create)
		{
			repeat = new TextBox
			{
				UseSystemPasswordChar = true,
				MaxLength = 1024,
				Dock = DockStyle.Top,
				AccessibleName = "Repeat master passphrase"
			};
			tableLayoutPanel.Controls.Add(new Label
			{
				Text = "Repeat passphrase",
				Height = 28,
				Dock = DockStyle.Top
			}, 0, 2);
			tableLayoutPanel.Controls.Add(repeat, 0, 3);
		}
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Bottom,
			Height = 42,
			FlowDirection = FlowDirection.RightToLeft
		};
		Button button = new Button
		{
			Text = (create ? "Create vault" : "Unlock"),
			AutoSize = true,
			Height = 34
		};
		button.Click += delegate
		{
			if (create && (masterPrompt.master.Text.Length < 12 || masterPrompt.master.Text != masterPrompt.repeat.Text))
			{
				ExplainedMessageBox.Show(masterPrompt, "Use at least 12 characters and enter the same passphrase twice.");
			}
			else if (masterPrompt.master.Text.Length != 0)
			{
				masterPrompt.Secret = masterPrompt.master.Text;
				masterPrompt.DialogResult = DialogResult.OK;
				masterPrompt.Close();
			}
		};
		flowLayoutPanel.Controls.Add(button);
		flowLayoutPanel.Controls.Add(new Button
		{
			Text = "Cancel",
			DialogResult = DialogResult.Cancel,
			AutoSize = true,
			Height = 34
		});
		base.AcceptButton = button;
		base.Controls.Add(tableLayoutPanel);
		base.Controls.Add(flowLayoutPanel);
		base.FormClosed += delegate
		{
			master.Clear();
			if (repeat != null)
			{
				repeat.Clear();
			}
		};
	}
}
