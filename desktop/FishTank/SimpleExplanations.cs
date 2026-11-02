using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Forms;

namespace FishTank;

public static class SimpleExplanations
{
	private sealed class TextState
	{
		public string Original;

		public bool Updating;

		public void Apply(Control control)
		{
			if (Updating || control.IsDisposed)
			{
				return;
			}
			Updating = true;
			try
			{
				control.Text = (DefaultSimple ? ExplanationCatalog.Rewrite(Original) : Original);
			}
			finally
			{
				Updating = false;
			}
		}
	}

	private sealed class WindowState
	{
		public FlowLayoutPanel Bar;

		public CheckBox Preference;

		public bool Syncing;
	}

	private const int Limit = 131072;

	private static readonly ConditionalWeakTable<Control, TextState> texts = new ConditionalWeakTable<Control, TextState>();

	private static readonly ConditionalWeakTable<Control, object> wired = new ConditionalWeakTable<Control, object>();

	private static readonly Dictionary<Form, WindowState> windows = new Dictionary<Form, WindowState>();

	private static bool installed;

	private static bool defaultSimple;

	public static bool DefaultSimple
	{
		get
		{
			return defaultSimple;
		}
		set
		{
			defaultSimple = value;
			foreach (KeyValuePair<Form, WindowState> item in new List<KeyValuePair<Form, WindowState>>(windows))
			{
				if (item.Key.IsDisposed)
				{
					continue;
				}
				RefreshText(item.Key);
				WindowState value2 = item.Value;
				if (value2.Preference != null && !value2.Preference.IsDisposed)
				{
					value2.Syncing = true;
					try
					{
						value2.Preference.Checked = value;
					}
					finally
					{
						value2.Syncing = false;
					}
				}
			}
		}
	}

	public static string OriginalText(Control control)
	{
		if (!texts.TryGetValue(control, out var value))
		{
			return control.Text;
		}
		return value.Original;
	}

	public static string Simplify(string text)
	{
		return ExplanationCatalog.Explain(text ?? "");
	}

	public static void Install()
	{
		if (!installed)
		{
			installed = true;
			defaultSimple = AppearanceStore.Load().SimpleExplanationsByDefault;
			Application.Idle += AttachWindows;
		}
	}

	private static void RefreshText(Control root)
	{
		foreach (Control control in root.Controls)
		{
			if (control is SimpleInformation simpleInformation)
			{
				simpleInformation.ApplyDefault();
				continue;
			}
			if (texts.TryGetValue(control, out var value))
			{
				value.Apply(control);
			}
			RefreshText(control);
		}
	}

	private static bool IsSecret(Control control)
	{
		Form form = control.FindForm();
		if (control is TextBoxBase && (form is RecoveryCodesDialog || form is MfaEnrollmentDialog))
		{
			return true;
		}
		if (control is TextBox textBox)
		{
			if (textBox.ReadOnly && !textBox.UseSystemPasswordChar)
			{
				return textBox.PasswordChar != '\0';
			}
			return true;
		}
		return false;
	}

	private static bool IsInformation(Control c)
	{
		if (!(c is Label) && !(c is ButtonBase) && !(c is GroupBox) && !(c is ComboBox) && !(c is ListBox) && !(c is DataGridView))
		{
			if (c is TextBox)
			{
				return ((TextBox)c).ReadOnly;
			}
			return false;
		}
		return true;
	}

	private static bool InsideBar(Control control)
	{
		for (Control control2 = control; control2 != null; control2 = control2.Parent)
		{
			if (control2.Name == "ExplanationOptions")
			{
				return true;
			}
		}
		return false;
	}

	private static void Wire(Control root)
	{
		if (InsideBar(root) || root.IsDisposed)
		{
			return;
		}
		if (!wired.TryGetValue(root, out var _))
		{
			wired.Add(root, new object());
			root.ControlAdded += delegate(object s, ControlEventArgs e)
			{
				Wire(e.Control);
			};
			if (root is Label)
			{
				TextState state = new TextState
				{
					Original = root.Text
				};
				texts.Add(root, state);
				root.TextChanged += delegate
				{
					if (!state.Updating)
					{
						state.Original = root.Text;
						state.Apply(root);
					}
				};
				state.Apply(root);
			}
			if (IsInformation(root) && !IsSecret(root) && root.ContextMenuStrip == null)
			{
				ContextMenuStrip menu = new ContextMenuStrip();
				menu.Items.Add("Explain this simply", null, delegate
				{
					ShowText(root.FindForm(), root.AccessibleName ?? "Information", Describe(root));
				});
				root.ContextMenuStrip = menu;
				root.Disposed += delegate
				{
					menu.Dispose();
				};
			}
		}
		foreach (Control control in root.Controls)
		{
			Wire(control);
		}
	}

	private static void AttachWindows(object sender, EventArgs args)
	{
		List<Form> list = new List<Form>();
		foreach (Form openForm in Application.OpenForms)
		{
			list.Add(openForm);
		}
		foreach (Form item in list)
		{
			Attach(item);
		}
	}

	public static void Attach(Form form)
	{
		if (form == null || form.IsDisposed || form is ExplanationReader || form is ExplanationMessage)
		{
			return;
		}
		if (!windows.TryGetValue(form, out var state))
		{
			state = new WindowState();
			windows.Add(form, state);
			form.FormClosed += delegate
			{
				windows.Remove(form);
			};
			form.KeyPreview = true;
			form.KeyDown += delegate(object s, KeyEventArgs e)
			{
				if (e.KeyCode == Keys.F1)
				{
					e.Handled = true;
					e.SuppressKeyPress = true;
					Show(form);
				}
			};
		}
		Wire(form);
		if (state.Bar != null && !state.Bar.IsDisposed && state.Bar.Parent == form)
		{
			return;
		}
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel();
		flowLayoutPanel.Name = "ExplanationOptions";
		flowLayoutPanel.Dock = DockStyle.Bottom;
		flowLayoutPanel.AutoSize = true;
		flowLayoutPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
		flowLayoutPanel.WrapContents = true;
		flowLayoutPanel.MinimumSize = new Size(0, 38);
		flowLayoutPanel.Padding = new Padding(6, 3, 6, 3);
		flowLayoutPanel.BackColor = SystemColors.Control;
		flowLayoutPanel.ForeColor = SystemColors.ControlText;
		flowLayoutPanel.AccessibleName = "Explanation options";
		FlowLayoutPanel flowLayoutPanel2 = flowLayoutPanel;
		state.Bar = flowLayoutPanel2;
		Button button = new Button();
		button.Text = "Explain simply (F1)";
		button.AutoSize = true;
		button.AccessibleName = "Explain information in this window simply";
		Button button2 = button;
		button2.Click += delegate
		{
			Show(form);
		};
		flowLayoutPanel2.Controls.Add(button2);
		CheckBox preference = new CheckBox
		{
			Text = "Use simple explanations",
			AutoSize = true,
			Checked = DefaultSimple,
			Padding = new Padding(4, 5, 0, 0),
			AccessibleName = "Use simple explanations by default"
		};
		state.Preference = preference;
		preference.CheckedChanged += delegate
		{
			if (state.Syncing)
			{
				return;
			}
			try
			{
				Appearance appearance = AppearanceStore.Load();
				appearance.SimpleExplanationsByDefault = preference.Checked;
				AppearanceStore.Save(appearance);
				DefaultSimple = preference.Checked;
			}
			catch (Exception)
			{
				state.Syncing = true;
				preference.Checked = DefaultSimple;
				state.Syncing = false;
				ExplainedMessageBox.Show(form, "The explanation preference could not be saved. Your previous preference is still in use.", "FishTank");
			}
		};
		flowLayoutPanel2.Controls.Add(preference);
		form.Controls.Add(flowLayoutPanel2);
		flowLayoutPanel2.SendToBack();
	}

	private static string Describe(Control control)
	{
		StringBuilder stringBuilder = new StringBuilder();
		Collect(control, stringBuilder, includeSelf: true);
		return stringBuilder.ToString();
	}

	private static void Append(StringBuilder output, string text)
	{
		if (output.Length < 131072 && !string.IsNullOrWhiteSpace(text))
		{
			int num = 131072 - output.Length;
			output.Append((text.Length <= num) ? text : text.Substring(0, num));
			output.AppendLine().AppendLine();
		}
	}

	private static void Collect(Control control, StringBuilder output, bool includeSelf)
	{
		if (!control.Visible || InsideBar(control) || IsSecret(control) || output.Length >= 131072)
		{
			return;
		}
		if (control is SimpleInformation simpleInformation)
		{
			Append(output, simpleInformation.OriginalInformation);
			return;
		}
		if (includeSelf && IsInformation(control))
		{
			DataGridView dataGridView = control as DataGridView;
			ComboBox comboBox = control as ComboBox;
			ListBox listBox = control as ListBox;
			if (dataGridView != null)
			{
				Append(output, dataGridView.AccessibleName ?? "Results");
				foreach (DataGridViewRow item in (IEnumerable)dataGridView.Rows)
				{
					if (!item.Visible || item.IsNewRow)
					{
						continue;
					}
					StringBuilder stringBuilder = new StringBuilder();
					foreach (DataGridViewCell cell in item.Cells)
					{
						if (cell.Visible)
						{
							stringBuilder.Append(dataGridView.Columns[cell.ColumnIndex].HeaderText).Append(": ").Append(Convert.ToString(cell.FormattedValue))
								.Append("   ");
						}
					}
					Append(output, stringBuilder.ToString());
					if (output.Length >= 131072)
					{
						break;
					}
				}
			}
			else if (comboBox != null)
			{
				Append(output, (comboBox.AccessibleName ?? "Selected option") + ": " + comboBox.Text);
			}
			else if (listBox != null)
			{
				Append(output, listBox.AccessibleName ?? "List");
				foreach (object item2 in listBox.Items)
				{
					Append(output, Convert.ToString(item2));
					if (output.Length >= 131072)
					{
						break;
					}
				}
			}
			else
			{
				Append(output, OriginalText(control));
			}
		}
		if (control is TextBoxBase || control is DataGridView || control is ComboBox || control is ListBox)
		{
			return;
		}
		foreach (Control control2 in control.Controls)
		{
			Collect(control2, output, includeSelf: true);
		}
	}

	public static string ReadInformation(Form form)
	{
		StringBuilder stringBuilder = new StringBuilder();
		Collect(form, stringBuilder, includeSelf: false);
		if (stringBuilder.Length >= 131072)
		{
			stringBuilder.AppendLine("This view reached its size limit. Use a result's own explanation menu to inspect it separately.");
		}
		return stringBuilder.ToString();
	}

	public static void Show(Form owner)
	{
		ShowText(owner, owner.Text, ReadInformation(owner));
	}

	public static void ShowText(IWin32Window owner, string title, string original)
	{
		using ExplanationReader explanationReader = new ExplanationReader(title, original);
		explanationReader.ShowDialog(owner);
	}
}
