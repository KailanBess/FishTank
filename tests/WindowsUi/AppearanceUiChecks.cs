using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using FishTank;

internal static class AppearanceUiChecks
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr window);
    private static Control[] All(Control root) => root.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(All(c))).ToArray();
    private static byte[] IconBytes(Icon icon) { using (var bitmap = icon.ToBitmap()) using (var output = new MemoryStream()) { bitmap.Save(output, ImageFormat.Png); return output.ToArray(); } }
    internal static void Run(string output, Action<bool, string> check)
    {
        var personalized = new FishTank.Appearance { Theme = "Light", FontFamily = "Verdana", Density = "Compact", ReducedMotion = true, CustomPalette = true };
        var family = personalized.WithAquariumStyle();
        family.Validate();
        check(personalized.Theme == "Light" && personalized.CustomPalette, "Aquarium preview does not mutate the original appearance");
        check(family.FontFamily == "Verdana" && family.Density == "Compact" && family.ReducedMotion, "Aquarium preset retains accessibility preferences");
        check(family.Theme == "Aquarium Ocean" && family.Accent == "Aquarium Mint" && family.CornerRadius == 16 && !family.CustomPalette, "Aquarium preset uses named aquatic surfaces and rounded cards");
        var original = new FishTank.Appearance { ReducedMotion = true };
        AppearanceStore.Save(original);
        using (var owner = new Desktop())
        using (var timer = new Timer { Interval = 220 })
        {
            owner.Bounds = new Rectangle(-30000, -30000, 1100, 800); owner.ShowInTaskbar = false; owner.Show(); Application.DoEvents();
            typeof(Desktop).GetMethod("Navigate", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(owner, new object[] { "Settings" });
            int tick = 0; string pendingTheme = null; DateTime previewDeadline = DateTime.MinValue; Exception failure = null;
            string[] themes = { "Aquarium Ocean", "Aquarium Midnight", "Aquarium Slate", "Light", "Ocean", "Plum", "Dark" };
            timer.Tick += delegate {
                var dialog = Application.OpenForms.OfType<AppearanceDialog>().FirstOrDefault();
                if (dialog == null) return;
                try {
                    if (pendingTheme != null && owner.CurrentAppearance.Theme != pendingTheme && DateTime.UtcNow < previewDeadline) return;
                    if (pendingTheme != null) {
                        check(owner.CurrentAppearance.Theme == pendingTheme, "Debounced modal preview did not apply latest theme: expected " + pendingTheme + "; actual " + owner.CurrentAppearance.Theme);
                        check(owner.Visible && !IsWindowEnabled(owner.Handle) && owner.Controls.Count > 0, "Repainting changed modal owner state: visible=" + owner.Visible + "; nativeEnabled=" + IsWindowEnabled(owner.Handle) + "; controls=" + owner.Controls.Count);
                        if (tick <= 4) using (var image = new Bitmap(owner.Width, owner.Height)) { owner.DrawToBitmap(image, new Rectangle(Point.Empty, owner.Size)); image.Save(Path.Combine(output, "appearance-preview-" + pendingTheme + ".png")); }
                    }
                    if (tick == 8) {
                        check(IconBytes(dialog.Icon).SequenceEqual(IconBytes(owner.Icon)), "Customize did not inherit the FishTank app icon");
                        check(dialog.Value.WindowFrameMode == "Custom color" && dialog.Value.WindowFrameColor == "#245CC5", "Frame color selection lost during app-theme changes");
                        using (var image = new Bitmap(dialog.Width, dialog.Height)) { dialog.DrawToBitmap(image, new Rectangle(Point.Empty, dialog.Size)); image.Save(Path.Combine(output, "appearance-choices.png")); }
                        timer.Stop(); dialog.DialogResult = DialogResult.Cancel; dialog.Close(); return;
                    }
                    var controls = All(dialog);
                    if (tick == 0) {
                        var options = controls.OfType<RadioButton>().Where(c => (c.AccessibleName ?? "").StartsWith("Window frame:")).ToArray();
                        check(options.Length == 2 && options.Count(x => x.Checked) == 1, "Window frame choices are missing or not exclusive");
                        dialog.Value.WindowFrameColor = "#245CC5";
                        options.Single(c => c.AccessibleName.EndsWith("Custom color")).Checked = true;
                    }
                    pendingTheme = themes[tick % themes.Length];
                    controls.OfType<ComboBox>().Single(c => c.AccessibleName == "Theme").SelectedItem = pendingTheme;
                    controls.OfType<ComboBox>().Single(c => c.AccessibleName == "Interface font").SelectedItem = tick % 2 == 0 ? "Bahnschrift" : "Segoe UI";
                    controls.OfType<NumericUpDown>().Single(c => c.AccessibleName == "Text size (%)").Value = tick % 2 == 0 ? 90 : 150;
                    previewDeadline = DateTime.UtcNow.AddSeconds(4); tick++;
                } catch (Exception ex) { failure = ex; timer.Stop(); dialog.DialogResult = DialogResult.Cancel; dialog.Close(); }
            };
            timer.Start();
            typeof(Desktop).GetMethod("Customize", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(owner, null);
            timer.Stop(); if (failure != null) throw failure;
            check(tick == 8 && owner.CurrentAppearance.Theme == original.Theme && owner.CurrentAppearance.WindowFrameMode == "Follow Windows", "Cancel did not restore app and frame preferences");
            check(AppearanceStore.Load().WindowFrameMode == "Follow Windows", "Preview saved frame changes without confirmation");
            owner.Hide();
        }
    }
}
