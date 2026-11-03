using System;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace FishTank;

// Retain the native caption, resizing, system menu and accessibility behavior.
internal static class WindowFrame
{
    private static Appearance current = new Appearance();
    private static bool installed;
    private static Icon appIcon;
    private static readonly ConditionalWeakTable<Form, FrameState> states = new ConditionalWeakTable<Form, FrameState>();
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    internal static void SetAppearance(Appearance value)
    {
        current = value.Copy();
        SimpleExplanations.ApplyTheme(Palette.From(current));
        if (!installed) { installed = true; Application.Idle += AttachOpenWindows; Application.ApplicationExit += delegate { appIcon?.Dispose(); appIcon = null; }; }
        foreach (Form form in Application.OpenForms.Cast<Form>().ToArray()) { Attach(form); Apply(form); }
    }
    private static void AttachOpenWindows(object sender, EventArgs args)
    {
        foreach (Form form in Application.OpenForms.Cast<Form>().ToArray()) Attach(form);
    }
    internal static void Attach(Form form)
    {
        if (form.IsDisposed || form.FormBorderStyle == FormBorderStyle.None || states.TryGetValue(form, out _)) return;
        states.Add(form, new FrameState(form));
    }
    internal static void SetIcon(Icon value)
    {
        var previous = appIcon; appIcon = (Icon)value.Clone();
        foreach (Form form in Application.OpenForms.Cast<Form>().ToArray()) { Attach(form); if (states.TryGetValue(form, out var state)) state.UpdateIcon(); }
        previous?.Dispose();
    }
    internal static int ColorRef(Color color) => color.R | color.G << 8 | color.B << 16;
    internal static Color TextFor(Color background)
    {
        Func<byte, double> linear = channel => { double v = channel / 255.0; return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); };
        double luminance = 0.2126 * linear(background.R) + 0.7152 * linear(background.G) + 0.0722 * linear(background.B);
        return (luminance + 0.05) / 0.05 >= 1.05 / (luminance + 0.05) ? Color.Black : Color.White;
    }
    private static bool WindowsDark()
    {
        try { using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")) return key?.GetValue("AppsUseLightTheme") is int light && light == 0; }
        catch { return false; }
    }
    private static void Set(Form form, int attribute, int value) { DwmSetWindowAttribute(form.Handle, attribute, ref value, sizeof(int)); }
    private static void Apply(Form form)
    {
        if (!form.IsHandleCreated || form.IsDisposed || form.FormBorderStyle == FormBorderStyle.None) return;
        try {
            bool custom = current.WindowFrameMode == "Custom color" && !SystemInformation.HighContrast;
            Color color = ColorTranslator.FromHtml(current.WindowFrameColor);
            Set(form, 20, (custom ? TextFor(color) == Color.White : !SystemInformation.HighContrast && WindowsDark()) ? 1 : 0);
            // DEFAULT removes overrides when switching back to Windows or High Contrast.
            Set(form, 34, custom ? ColorRef(color) : -1);
            Set(form, 35, custom ? ColorRef(color) : -1);
            Set(form, 36, custom ? ColorRef(TextFor(color)) : -1);
            // Unsupported attributes are deliberately left to the OS (Windows 10).
        } catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
    }
    private sealed class FrameState : NativeWindow
    {
        private readonly Form form;
        private Icon ownedIcon;
        internal FrameState(Form form)
        {
            this.form = form;
            UpdateIcon();
            form.Disposed += delegate { ownedIcon?.Dispose(); ownedIcon = null; };
            form.HandleCreated += delegate { AssignHandle(form.Handle); Apply(form); };
            form.HandleDestroyed += delegate { ReleaseHandle(); };
            form.Activated += delegate { Apply(form); };
            form.Deactivate += delegate { Apply(form); };
            if (form.IsHandleCreated) { AssignHandle(form.Handle); Apply(form); }
        }
        internal void UpdateIcon()
        {
            if (form.IsDisposed) return;
            if (appIcon == null) {
                try { appIcon = Icon.ExtractAssociatedIcon(typeof(Program).Assembly.Location); } catch { return; }
            }
            if (appIcon == null) return;
            var previous = ownedIcon; ownedIcon = (Icon)appIcon.Clone(); form.Icon = ownedIcon; previous?.Dispose();
        }
        protected override void WndProc(ref Message message)
        {
            int id = message.Msg;
            base.WndProc(ref message);
            if (id == 0x31A || id == 0x1A || id == 0x320) Apply(form);
        }
    }
}
