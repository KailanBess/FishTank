using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace FishTank;

public sealed class ProtectedFilesDialog : Form
{
    private FileSafeSession session;
    private readonly Palette colors;
    private readonly DataGridView files;
    private readonly Label state;
    private readonly Timer timer = new Timer { Interval = 1000 };
    private DateTime activity = DateTime.UtcNow;
    private int generation;
    private bool working;
    internal bool HasRunningOperation => working;
    private bool disposed;
    public ProtectedFilesDialog(Palette colors)
    {
        this.colors = colors;
        Text = "FishTank · Protected files"; AutoScaleMode = AutoScaleMode.Dpi;
        Size = new Size(1000, 750); MinimumSize = new Size(760, 600); StartPosition = FormStartPosition.CenterParent;
        BackColor = colors.Background; ForeColor = colors.Text; Font = new Font("Segoe UI", 10);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(16) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var help = new Label { AutoSize = true, MaximumSize = new Size(850, 0), Text = "Encrypted contents and internal names stay unreadable without the separate container passphrase. The .ftsafe file itself remains visible. Limits: 256 files / 64 MiB total. Copies leave readable originals; moving removes selected originals only after both encrypted copies verify. Deleted originals may remain in backups or disk remnants. Use BitLocker/device encryption for stolen-drive protection. Malware controlling your unlocked account can capture passwords and plaintext." };
        layout.Controls.Add(help, 0, 0);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        Add(actions, "Create encrypted file copies", () => Protect(false, false));
        Add(actions, "Encrypt and move personal files", () => Protect(true, false));
        Add(actions, "Create encrypted folder copy", () => Protect(false, true));
        Add(actions, "Encrypt and move folder files", () => Protect(true, true));
        Add(actions, "Unlock container", Unlock);
        Add(actions, "Export permitted plaintext", Export);
        Add(actions, "Lock now", LockForAppLock, true);
        Add(actions, "Windows drive encryption", () => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:deviceencryption") { UseShellExecute = true }));
        layout.Controls.Add(actions, 0, 1);
        files = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
            BackgroundColor = colors.Surface, ForeColor = colors.Text, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AccessibleName = "Unlocked protected file names and sizes" };
        files.DefaultCellStyle.BackColor = colors.Surface; files.DefaultCellStyle.ForeColor = colors.Text;
        files.EnableHeadersVisualStyles = false; files.ColumnHeadersDefaultCellStyle.BackColor = colors.Surface2; files.ColumnHeadersDefaultCellStyle.ForeColor = colors.Text;
        files.DefaultCellStyle.SelectionBackColor = colors.Surface2; files.DefaultCellStyle.SelectionForeColor = colors.Text;
        files.Columns.Add("name", "Protected name (hidden while locked)"); files.Columns.Add("size", "Bytes"); layout.Controls.Add(files, 0, 2);
        state = new Label { Text = "Locked · Separate passphrase required. There is no passphrase reset.", AutoSize = true, MaximumSize = new Size(850, 0), Dock = DockStyle.Fill };
        layout.Controls.Add(state, 0, 3); Controls.Add(layout);
        layout.SizeChanged += delegate { int width = Math.Max(300, layout.ClientSize.Width - 40); help.MaximumSize = state.MaximumSize = new Size(width, 0); };
        WireActivity(this);
        timer.Tick += delegate { if (session != null && DateTime.UtcNow - activity >= TimeSpan.FromMinutes(5)) LockForAppLock(); }; timer.Start();
        SystemEvents.SessionSwitch += SessionChanged;
        FormClosing += delegate(object sender, FormClosingEventArgs e) { if (working) { e.Cancel = true; state.Text = "Wait for the authorized file operation to finish. Lock now can hide and lock open container contents."; } };
    }
    private void WireActivity(Control parent)
    {
        parent.KeyDown += delegate { activity = DateTime.UtcNow; }; parent.MouseDown += delegate { activity = DateTime.UtcNow; };
        foreach (Control child in parent.Controls) WireActivity(child);
    }
    private void Add(FlowLayoutPanel panel, string text, Action action, bool duringWork = false)
    {
        var button = new Button { Text = text, AutoSize = true, Padding = new Padding(6), AccessibleName = text, FlatStyle = FlatStyle.Flat, BackColor = colors.Surface2, ForeColor = colors.Text, UseVisualStyleBackColor = false };
        button.Click += delegate { if (working && !duringWork) return; activity = DateTime.UtcNow; try { action(); } catch (Exception ex) { state.Text = "Operation did not complete: " + ex.Message; } };
        panel.Controls.Add(button);
    }
    private bool Confirm(string text) => ExplainedMessageBox.Show(this, text, "Confirm protected-file action", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
    private string Password(bool create)
    {
        using (var prompt = new MasterPrompt(create))
        {
            prompt.Text = create ? "New protected-files passphrase · no reset" : "Protected-files passphrase";
            if (prompt.ShowDialog(this) != DialogResult.OK) return null;
            string value = prompt.Secret; prompt.Secret = null; return value;
        }
    }
    private void Protect(bool move, bool folder)
    {
        string root = null; string[] selected = null;
        if (folder) { using (var picker = new FolderBrowserDialog()) { if (picker.ShowDialog(this) != DialogResult.OK) return; root = picker.SelectedPath; } }
        else { using (var picker = new OpenFileDialog { Multiselect = true, CheckFileExists = true }) { if (picker.ShowDialog(this) != DialogResult.OK) return; selected = picker.FileNames; } }
        string scope = folder ? root : string.Join("\n", selected.Take(10)) + (selected.Length > 10 ? "\n… and " + (selected.Length - 10) + " more selected files" : "");
        if (!Confirm((move ? "Encrypt and MOVE these selected personal files? Originals will be removed only after the container and recovery copy verify. Removal is not secure erasure.\n" : "Create encrypted COPIES? Readable originals will remain outside FishTank.\n")
            + "\n" + scope + "\n\nThe passphrase cannot be reset. Keep the recovery copy and passphrase safely; losing them can permanently lose access. No system/account files are moved.")) return;
        string destination;
        using (var picker = new SaveFileDialog { Filter = "FishTank protected container|*.ftsafe", FileName = "Protected-files.ftsafe", AddExtension = true })
        { if (picker.ShowDialog(this) != DialogResult.OK) return; destination = picker.FileName; }
        string password = Password(true); if (password == null) return;
        Run(() => { try { var paths = folder ? ProtectedFileService.CollectFolder(root) : selected; return ProtectedFileService.Create(paths, root, destination, password, move); } finally { password = null; } });
    }
    private async void Unlock()
    {
        string path;
        using (var picker = new OpenFileDialog { Filter = "FishTank protected container|*.ftsafe", CheckFileExists = true }) { if (picker.ShowDialog(this) != DialogResult.OK) return; path = picker.FileName; }
        string password = Password(false); if (password == null) return;
        LockForAppLock(); int request = generation; working = true; state.Text = "Verifying and unlocking…";
        try
        {
            FileSafeSession opened = await Task.Run(() => ProtectedFileService.Open(path, password));
            if (IsDisposed || generation != request) { opened.Dispose(); return; }
            session = opened; foreach (var file in opened.Files) files.Rows.Add(file.Name, file.Length);
            activity = DateTime.UtcNow; state.Text = opened.Files.Count + " files verified and unlocked in memory. No plaintext was automatically written or opened. Locks after five minutes of inactivity.";
        }
        catch (Exception ex) { if (!IsDisposed && generation == request) state.Text = "Still locked: " + ex.Message; }
        finally { password = null; working = false; }
    }
    private void Export()
    {
        var opened = session ?? throw new InvalidOperationException("Unlock a container first.");
        if (!Confirm("Permit ALL files in this container to be exported as ordinary plaintext files? They will remain readable outside FishTank after you lock the container. No files are automatically opened. On interruption, partial plaintext exports may remain.")) return;
        using (var picker = new FolderBrowserDialog()) if (picker.ShowDialog(this) == DialogResult.OK) { string parent = picker.SelectedPath; Run(() => ProtectedFileService.Export(opened, parent)); }
    }
    private async void Run(Func<string> action)
    {
        int request = generation; working = true; state.Text = "Working on the explicitly permitted file operation…";
        try { string result = await Task.Run(action); if (!IsDisposed) state.Text = generation == request ? result : "A permitted operation finished while locked. Reopen and verify the encrypted container; originals or permitted plaintext exports may remain. Check them before assuming protection."; }
        catch (Exception ex) { if (!IsDisposed) state.Text = generation == request ? "Operation did not complete: " + ex.Message + " Check original and partial output files before relying on protection." : "An operation ended while locked. Originals or partial permitted exports may remain; review them before relying on protection."; }
        finally { working = false; }
    }
    internal void LockForAppLock()
    {
        generation++; session?.Dispose(); session = null; files.Rows.Clear(); state.Text = "Locked · File contents and internal names require the separate passphrase. Permitted plaintext exports remain readable outside the app.";
    }
    private void SessionChanged(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock || e.Reason == SessionSwitchReason.RemoteDisconnect || e.Reason == SessionSwitchReason.ConsoleDisconnect)
            if (!disposed && IsHandleCreated) try { BeginInvoke((Action)LockForAppLock); } catch (InvalidOperationException) { }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed) { disposed = true; generation++; session?.Dispose(); session = null; timer.Dispose(); SystemEvents.SessionSwitch -= SessionChanged; }
        base.Dispose(disposing);
    }
}
