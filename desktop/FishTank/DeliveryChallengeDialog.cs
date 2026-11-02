using System;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FishTank;

public sealed class DeliveryChallengeDialog : Form
{
	private ComboBox channel;

	private TextBox code;

	private Label status;

	private Button send;

	private Button done;

	private string challenge;

	private readonly string password;

	private readonly string purpose;

	private bool working;

	public string FactorValue;

	public string ChallengeId;

	public string Channel;

	public string Code;

	public DeliveryChallengeDialog(string password, string purpose)
	{
		DeliveryChallengeDialog deliveryChallengeDialog = this;
		this.password = password;
		this.purpose = purpose;
		Text = "Email / phone verification";
		base.Width = 600;
		base.Height = 390;
		base.StartPosition = FormStartPosition.CenterParent;
		Font = new Font("Segoe UI", 10f);
		base.Padding = new Padding(22);
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			FlowDirection = FlowDirection.TopDown,
			WrapContents = false
		};
		flowLayoutPanel.Controls.Add(new Label
		{
			Text = "Send a code through your own configured server. It uses only the email/phone pre-provisioned by your server administrator. Delivery may incur gateway charges.",
			Width = 535,
			Height = 65
		});
		channel = new ComboBox
		{
			DropDownStyle = ComboBoxStyle.DropDownList,
			Width = 250
		};
		channel.Items.AddRange(new string[2] { "email", "sms" });
		channel.SelectedIndex = 0;
		flowLayoutPanel.Controls.Add(channel);
		send = new Button
		{
			Text = "Send code",
			Width = 160,
			Height = 35
		};
		Button button = send;
		EventHandler value = async delegate
		{
			await Send();
		};
		button.Click += value;
		flowLayoutPanel.Controls.Add(send);
		code = new TextBox
		{
			MaxLength = 8,
			Width = 250,
			AccessibleName = "Eight-digit delivered code"
		};
		flowLayoutPanel.Controls.Add(code);
		status = new Label
		{
			Width = 535,
			Height = 60,
			Text = "No message has been sent."
		};
		flowLayoutPanel.Controls.Add(status);
		done = new Button
		{
			Text = "Use this code",
			Width = 160,
			Height = 35,
			Enabled = false
		};
		done.Click += delegate
		{
			if (deliveryChallengeDialog.challenge != null && Regex.IsMatch(deliveryChallengeDialog.code.Text, "\\A[0-9]{8}\\z"))
			{
				deliveryChallengeDialog.ChallengeId = deliveryChallengeDialog.challenge;
				deliveryChallengeDialog.Channel = Convert.ToString(deliveryChallengeDialog.channel.SelectedItem);
				deliveryChallengeDialog.Code = deliveryChallengeDialog.code.Text;
				deliveryChallengeDialog.FactorValue = SelfHostedMfa.Factor(deliveryChallengeDialog.challenge, deliveryChallengeDialog.Channel, purpose, deliveryChallengeDialog.Code);
				deliveryChallengeDialog.DialogResult = DialogResult.OK;
				deliveryChallengeDialog.Close();
			}
		};
		flowLayoutPanel.Controls.Add(done);
		base.Controls.Add(flowLayoutPanel);
		base.FormClosing += delegate(object s, FormClosingEventArgs e)
		{
			if (working)
			{
				e.Cancel = true;
			}
		};
		base.FormClosed += delegate
		{
			code.Clear();
		};
	}

	private async Task Send()
	{
		working = true;
		send.Enabled = false;
		done.Enabled = false;
		channel.Enabled = false;
		try
		{
			int num = default(int);
			_ = num;
			_ = 0;
			try
			{
				string selected = Convert.ToString(channel.SelectedItem);
				challenge = await Task.Run(() => SelfHostedMfa.Send(password, selected, purpose));
				status.Text = "Server accepted delivery. Enter its eight-digit code. It expires in five minutes; a resend invalidates the previous code.";
				done.Enabled = true;
			}
			catch (Exception ex)
			{
				status.Text = ex.Message;
				channel.Enabled = true;
			}
		}
		finally
		{
			working = false;
			send.Enabled = true;
		}
	}
}
