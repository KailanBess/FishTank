using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace FishTank;

public sealed class PasswordManagerDialog : Form
{
	private PasswordSession session;

	private readonly Palette colors;

	private DataGridView list;

	private TextBox search;

	private Label state;

	private FlowLayoutPanel actions;

	private readonly Timer idle = new Timer
	{
		Interval = 1000
	};

	private readonly SecretClipboard clipboard = new SecretClipboard();

	private Form secretDialog;

	private bool working;
	internal bool HasRunningOperation => working;

	private int lockGeneration;

	public PasswordManagerDialog(Palette colors)
	{
		this.colors = colors;
		Text = "FishTank · Password manager";
		base.Width = 1030;
		base.Height = 750;
		MinimumSize = new Size(850, 620);
		base.StartPosition = FormStartPosition.CenterParent;
		BackColor = colors.Background;
		ForeColor = colors.Text;
		Font = new Font("Segoe UI", 10f);
		base.Padding = new Padding(22);
		Label value = new Label
		{
			Text = "Your passwords. Your vault.",
			Dock = DockStyle.Top,
			Height = 55,
			Font = new Font("Segoe UI", 23f, FontStyle.Bold)
		};
		state = new Label
		{
			Dock = DockStyle.Bottom,
			Height = 58,
			ForeColor = colors.Muted
		};
		actions = new FlowLayoutPanel
		{
			Dock = DockStyle.Top,
			Height = 115,
			WrapContents = true
		};
		Action action = delegate
		{
			Unlock(create: true);
		};
		Add("Create vault", action);
		Add("Unlock", delegate
		{
			Unlock(create: false);
		});
		Add("Lock now", LockVault);
		Add("Add entry", delegate
		{
			Edit(existing: false);
		});
		Add("Edit selected", delegate
		{
			Edit(existing: true);
		});
		Add("Copy password", CopySelected);
		Add("Delete selected", DeleteSelected);
		Add("Generate password", Generate);
		Add("Encrypted backup", Backup);
		Add("Restore backup", delegate
		{
			Restore();
		});
		search = new TextBox
		{
			Dock = DockStyle.Top,
			AccessibleName = "Search vault by name, username or site",
			BackColor = colors.Surface2,
			ForeColor = colors.Text
		};
		search.TextChanged += delegate
		{
			RefreshEntries();
		};
		list = new DataGridView
		{
			Dock = DockStyle.Fill,
			ReadOnly = true,
			AllowUserToAddRows = false,
			AllowUserToDeleteRows = false,
			RowHeadersVisible = false,
			AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
			SelectionMode = DataGridViewSelectionMode.FullRowSelect,
			MultiSelect = false,
			BackgroundColor = colors.Surface,
			BorderStyle = BorderStyle.None,
			EnableHeadersVisualStyles = false
		};
		list.DefaultCellStyle.BackColor = colors.Surface;
		list.DefaultCellStyle.ForeColor = colors.Text;
		list.DefaultCellStyle.SelectionBackColor = colors.Surface2;
		list.DefaultCellStyle.SelectionForeColor = colors.Text;
		list.ColumnHeadersDefaultCellStyle.BackColor = colors.Surface2;
		list.ColumnHeadersDefaultCellStyle.ForeColor = colors.Text;
		list.Columns.Add("name", "Name");
		list.Columns.Add("username", "Username");
		list.Columns.Add("website", "Website / app");
		list.Columns.Add("review", "Local password review");
		list.CellDoubleClick += delegate(object s, DataGridViewCellEventArgs e)
		{
			if (e.RowIndex >= 0)
			{
				Edit(existing: true);
			}
		};
		base.Controls.Add(list);
		base.Controls.Add(search);
		base.Controls.Add(actions);
		base.Controls.Add(value);
		base.Controls.Add(state);
		RefreshEntries();
		idle.Tick += delegate
		{
			if (session != null && session.ShouldAutoLock(DateTime.UtcNow))
			{
				LockVault();
			}
		};
		idle.Start();
		SystemEvents.SessionSwitch += SessionSwitch;
		WireActivity(this);
		base.FormClosing += delegate(object s, FormClosingEventArgs e)
		{
			if (working)
			{
				e.Cancel = true;
				state.Text = "Wait for the vault operation to complete.";
			}
		};
		base.FormClosed += delegate
		{
			SystemEvents.SessionSwitch -= SessionSwitch;
			idle.Dispose();
			LockVault();
			clipboard.Dispose();
		};
	}

	private void WireActivity(Control control)
	{
		control.KeyDown += delegate
		{
			if (session != null)
			{
				session.Touch();
			}
		};
		control.MouseDown += delegate
		{
			if (session != null)
			{
				session.Touch();
			}
		};
		foreach (Control control2 in control.Controls)
		{
			WireActivity(control2);
		}
	}

	private void Add(string text, Action action)
	{
		Button button = new Button();
		button.Text = text;
		button.AutoSize = true;
		button.Height = 36;
		button.BackColor = colors.Surface2;
		button.ForeColor = colors.Text;
		button.FlatStyle = FlatStyle.Flat;
		button.Padding = new Padding(8, 3, 8, 3);
		button.Margin = new Padding(0, 0, 9, 8);
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
				ExplainedMessageBox.Show(this, ex.Message, "Password manager");
			}
		};
		actions.Controls.Add(button2);
	}

	private void SessionSwitch(object sender, SessionSwitchEventArgs e)
	{
		if (e.Reason == SessionSwitchReason.SessionLock && base.IsHandleCreated && !base.IsDisposed)
		{
			BeginInvoke((Action)delegate
			{
				LockVault();
			});
		}
	}

	private bool Confirm(string text)
	{
		return ExplainedMessageBox.Show(this, text, "Confirm password vault action", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) == DialogResult.Yes;
	}

	private async void Unlock(bool create)
	{
		if (session != null)
		{
			state.Text = "Vault is already unlocked. Lock it before switching vault operations.";
			return;
		}
		string master = null;
		using (MasterPrompt masterPrompt = new MasterPrompt(create))
		{
			secretDialog = masterPrompt;
			if (masterPrompt.ShowDialog(this) != DialogResult.OK)
			{
				secretDialog = null;
				return;
			}
			master = masterPrompt.Secret;
			masterPrompt.Secret = null;
			secretDialog = null;
		}
		working = true;
		int generation = lockGeneration;
		state.Text = "Deriving encryption keys…";
		try
		{
			int num = default(int);
			_ = num;
			_ = 0;
			try
			{
				PasswordSession unlocked = await Task.Run(() => (!create) ? PasswordSession.Open(PasswordSession.DefaultPath, master) : PasswordSession.Create(PasswordSession.DefaultPath, master));
				if (generation != lockGeneration || base.IsDisposed)
				{
					unlocked.Dispose();
					return;
				}
				session = unlocked;
				RefreshEntries();
			}
			catch (Exception ex)
			{
				state.Text = ex.Message;
			}
		}
		finally
		{
			master = null;
			working = false;
		}
	}

	private void NeedUnlock()
	{
		if (session == null || !session.IsUnlocked)
		{
			throw new InvalidOperationException("Unlock or create your password vault first.");
		}
	}

	private void LockVault()
	{
		lockGeneration++;
		if (secretDialog != null && !secretDialog.IsDisposed)
		{
			secretDialog.DialogResult = DialogResult.Cancel;
			secretDialog.Close();
		}
		if (session != null)
		{
			session.Dispose();
			session = null;
		}
		clipboard.Clear();
		search.Clear();
		list.Rows.Clear();
		state.Text = "Locked · Master passphrase required. Generator works without a vault. Auto-lock after 5 minutes of inactivity or when Windows locks.";
	}

	internal void LockForAppLock() { LockVault(); }

	private void RefreshEntries()
	{
		if (list == null)
		{
			return;
		}
		list.Rows.Clear();
		if (session == null)
		{
			state.Text = "Locked · Create or unlock your vault. No password data is visible. Auto-lock: 5 minutes.";
			return;
		}
		IList<PasswordEntry> entries = session.Entries;
		string text = search.Text;
		foreach (PasswordEntry e in entries)
		{
			if ((e.Title + " " + e.Username + " " + e.Website).IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
			{
				string text2 = ((e.Password.Length < 14) ? "Shorter than 14 characters" : "");
				if (entries.Count((PasswordEntry other) => other.Password == e.Password) > 1)
				{
					text2 = text2 + ((text2 == "") ? "" : " · ") + "Reused password";
				}
				if (text2 == "")
				{
					text2 = "No length/reuse flag (not a breach check)";
				}
				int index = list.Rows.Add(e.Title, e.Username, e.Website, text2);
				list.Rows[index].Tag = e.Id;
			}
		}
		state.Text = "Unlocked · " + entries.Count + " entries · Passwords are not displayed in this list. Clipboard clears after 30 seconds if unchanged.";
	}

	private PasswordEntry Selected()
	{
		NeedUnlock();
		if (list.SelectedRows.Count == 0)
		{
			throw new InvalidOperationException("Select an entry first.");
		}
		string id = Convert.ToString(list.SelectedRows[0].Tag);
		return session.Entries.First((PasswordEntry e) => e.Id == id);
	}

	private void Edit(bool existing)
	{
		NeedUnlock();
		PasswordEntry passwordEntry = (existing ? Selected() : new PasswordEntry());
		using PasswordEditor passwordEditor = new PasswordEditor(passwordEntry, generator: false, colors);
		secretDialog = passwordEditor;
		WireActivity(passwordEditor);
		try
		{
			if (passwordEditor.ShowDialog(this) == DialogResult.OK)
			{
				NeedUnlock();
				session.Put(passwordEditor.Value);
				RefreshEntries();
			}
		}
		finally
		{
			passwordEditor.Value.Password = "";
			passwordEntry.Password = "";
			secretDialog = null;
		}
	}

	private void CopySelected()
	{
		PasswordEntry passwordEntry = Selected();
		try
		{
			clipboard.Copy(passwordEntry.Password);
			state.Text = "Copied · Clipboard will be cleared after 30 seconds if it has not changed. Other apps can read a copied password.";
		}
		finally
		{
			passwordEntry.Password = "";
		}
	}

	private void DeleteSelected()
	{
		PasswordEntry passwordEntry = Selected();
		try
		{
			if (Confirm("Remove this entry from the active vault? Encrypted backups may still contain previous versions."))
			{
				session.Delete(passwordEntry.Id);
				RefreshEntries();
			}
		}
		finally
		{
			passwordEntry.Password = "";
		}
	}

	private void Generate()
	{
		using PasswordEditor passwordEditor = new PasswordEditor(new PasswordEntry(), generator: true, colors);
		secretDialog = passwordEditor;
		WireActivity(passwordEditor);
		try
		{
			if (passwordEditor.ShowDialog(this) == DialogResult.OK)
			{
				clipboard.Copy(passwordEditor.Value.Password);
				state.Text = "Generated password copied. It was not saved; add an entry to store it. Clipboard clear timer: 30 seconds.";
			}
		}
		finally
		{
			passwordEditor.Value.Password = "";
			secretDialog = null;
		}
	}

	private void Backup()
	{
		NeedUnlock();
		SaveFileDialog saveFileDialog = new SaveFileDialog();
		saveFileDialog.Filter = "Encrypted FishTank vault|*.ftvault";
		saveFileDialog.FileName = "FishTank-password-backup.ftvault";
		using SaveFileDialog saveFileDialog2 = saveFileDialog;
		if (saveFileDialog2.ShowDialog(this) == DialogResult.OK)
		{
			session.ExportEncrypted(saveFileDialog2.FileName);
			state.Text = "Encrypted backup saved. Keep its master passphrase separately; losing it prevents recovery.";
		}
	}

	private async void Restore()
	{
		if (!Confirm("Restore an encrypted backup? After its master passphrase is verified, it replaces the active vault. The previous encrypted file is retained as a .bak file."))
		{
			return;
		}
		OpenFileDialog openFileDialog = new OpenFileDialog();
		openFileDialog.Filter = "Encrypted FishTank vault|*.ftvault";
		string backup;
		using (OpenFileDialog openFileDialog2 = openFileDialog)
		{
			if (openFileDialog2.ShowDialog(this) != DialogResult.OK)
			{
				return;
			}
			backup = openFileDialog2.FileName;
		}
		string master;
		using (MasterPrompt masterPrompt = new MasterPrompt(create: false))
		{
			secretDialog = masterPrompt;
			if (masterPrompt.ShowDialog(this) != DialogResult.OK)
			{
				secretDialog = null;
				return;
			}
			master = masterPrompt.Secret;
			masterPrompt.Secret = null;
			secretDialog = null;
		}
		LockVault();
		working = true;
		int generation = lockGeneration;
		state.Text = "Verifying encrypted backup…";
		try
		{
			int num = default(int);
			_ = num;
			_ = 0;
			try
			{
				PasswordSession unlocked = await Task.Run(() => PasswordSession.RestoreEncrypted(backup, PasswordSession.DefaultPath, master));
				if (generation != lockGeneration || base.IsDisposed)
				{
					unlocked.Dispose();
					return;
				}
				session = unlocked;
				RefreshEntries();
			}
			catch (Exception ex)
			{
				state.Text = ex.Message;
			}
		}
		finally
		{
			master = null;
			working = false;
		}
	}
}
