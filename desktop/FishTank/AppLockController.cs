using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace FishTank;

internal sealed class AppLockController : IMessageFilter, IDisposable
{
    private readonly Desktop owner;
    private readonly Action<string> status;
    private readonly Timer timer = new Timer { Interval = 1000 };
    private DateTime lastActivity = DateTime.UtcNow;
    private bool locking;
    private bool disposed;
    internal bool IsLocked => locking;

    internal AppLockController(Desktop owner, Action<string> status)
    {
        this.owner = owner; this.status = status;
        Application.AddMessageFilter(this);
        SystemEvents.SessionSwitch += SessionChanged;
        timer.Tick += delegate {
            if (locking || !AppAccess.Enabled) return;
            if (!AppAccess.SessionUnlocked) { Lock(); return; }
            int minutes = 5;
            try { minutes = SecurityPreferences.Load().IdleMinutes; }
            catch { status("Security preferences unreadable; default 5-minute app locking remains active."); }
            if (minutes > 0 && DateTime.UtcNow - lastActivity >= TimeSpan.FromMinutes(minutes)) Lock();
        };
        timer.Start();
    }
    public bool PreFilterMessage(ref Message message)
    {
        if (!locking && message.Msg == 0x100 && message.WParam.ToInt32() == (int)Keys.L
            && (Control.ModifierKeys & (Keys.Control | Keys.Shift)) == (Keys.Control | Keys.Shift) && owner.IsHandleCreated)
        {
            owner.BeginInvoke((Action)Lock);
            return true;
        }
        if (!locking && ((message.Msg >= 0x100 && message.Msg <= 0x109) || (message.Msg >= 0x200 && message.Msg <= 0x20E))) lastActivity = DateTime.UtcNow;
        return false;
    }
    private void SessionChanged(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason != SessionSwitchReason.SessionLock && e.Reason != SessionSwitchReason.RemoteDisconnect && e.Reason != SessionSwitchReason.ConsoleDisconnect) return;
        bool enabled = true;
        try { enabled = SecurityPreferences.Load().LockWithWindows; } catch { }
        if (enabled && !disposed && owner.IsHandleCreated)
        {
            try { owner.BeginInvoke((Action)Lock); } catch (InvalidOperationException) { }
        }
    }
    internal void Lock()
    {
        if (locking || disposed) return;
        if (!AppAccess.Enabled)
        {
            foreach (var vault in Application.OpenForms.OfType<PasswordManagerDialog>().ToArray()) vault.LockForAppLock();
            foreach (var safe in Application.OpenForms.OfType<ProtectedFilesDialog>().ToArray()) safe.LockForAppLock();
            status("Open vaults are locked. Set an app password in Settings to lock access to the whole app."); return;
        }
        locking = true;
        var windows = Application.OpenForms.Cast<Form>().Where(f => !f.IsDisposed).ToList();
        var visible = windows.Where(f => f.Visible).ToList();
        foreach (var vault in windows.OfType<PasswordManagerDialog>()) vault.LockForAppLock();
        foreach (var safe in windows.OfType<ProtectedFilesDialog>()) safe.LockForAppLock();
        AppAccess.Lock();
        foreach (var form in visible) form.Hide();
        bool unlocked = false;
        try
        {
            while (!disposed)
            {
                using (var prompt = new AppUnlockDialog())
                {
                    prompt.Text = "FishTank locked · sign in to continue";
                    if (prompt.ShowDialog() == DialogResult.OK) { unlocked = true; break; }
                }
                if (MessageBox.Show("Exit FishTank? Already-started Windows operations may continue without a final app record.", "FishTank remains locked", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    foreach (var form in windows.Where(f => f != owner && !f.IsDisposed)) form.Dispose();
                    owner.CloseWhileLocked();
                    return;
                }
            }
        }
        catch
        {
            MessageBox.Show("Access remains locked. FishTank will close because its access settings could not be read or verified. Restore the private access configuration before trying again.", "FishTank locked", MessageBoxButtons.OK, MessageBoxIcon.Error);
            foreach (var form in windows.Where(f => f != owner && !f.IsDisposed)) form.Dispose();
            owner.CloseWhileLocked();
        }
        finally
        {
            if (unlocked)
            {
                foreach (var form in visible.Where(f => !f.IsDisposed)) form.Show();
                lastActivity = DateTime.UtcNow;
            }
            locking = false;
        }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; timer.Dispose();
        SystemEvents.SessionSwitch -= SessionChanged;
        Application.RemoveMessageFilter(this);
    }
}
