using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyMetadata("AquariumWorkspace", "1")]

namespace FishTank;

internal static class AquariumWorkspace
{
    internal static bool Enabled => Environment.GetEnvironmentVariable("AQUARIUM_HOSTED") == "1";
    [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetProp(IntPtr window, string name, IntPtr value);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int show);
    internal static void Prepare() { if (Enabled) SetProcessDpiAwarenessContext(new IntPtr(-4)); }
    internal static void Register(Form form)
    {
        if (!Enabled) return;
        form.HandleCreated += delegate { SetProp(form.Handle, "Aquarium.Workspace.v1", new IntPtr(1)); };
        form.Shown += delegate { form.MinimumSize = new System.Drawing.Size(640, 400); form.WindowState = FormWindowState.Normal; if (GetParent(form.Handle) == IntPtr.Zero) ShowWindow(form.Handle, 0); };
    }
}
