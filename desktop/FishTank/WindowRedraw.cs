using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FishTank;

// Layout suspension does not suspend native child-window painting.
internal sealed class WindowRedraw : IDisposable
{
    private readonly Control control;
    private readonly IntPtr pausedHandle;
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr value, IntPtr data);
    [DllImport("user32.dll")] private static extern bool RedrawWindow(IntPtr window, IntPtr rectangle, IntPtr region, uint flags);
    internal WindowRedraw(Control control)
    {
        this.control = control;
        // Enabling redraw on an originally hidden window would unexpectedly show it.
        if (control.IsHandleCreated && control.Visible) { pausedHandle = control.Handle; SendMessage(pausedHandle, 0xB, IntPtr.Zero, IntPtr.Zero); }
    }
    public void Dispose()
    {
        if (control.IsDisposed || !control.IsHandleCreated) return;
        if (pausedHandle != IntPtr.Zero && control.Handle == pausedHandle) SendMessage(pausedHandle, 0xB, new IntPtr(1), IntPtr.Zero);
        control.CreateControl();
        control.Invalidate(true);
        if (control.Visible) RedrawWindow(control.Handle, IntPtr.Zero, IntPtr.Zero, 0x1 | 0x4 | 0x80 | 0x100 | 0x400);
    }
}
