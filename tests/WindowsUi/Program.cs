using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using FishTank;

internal static class Program
{
    static int checks;
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
    static Control[] Descendants(Control control) => control.Controls.Cast<Control>()
        .SelectMany(c => new[] { c }.Concat(Descendants(c))).ToArray();

    [STAThread]
    static int Main(string[] args)
    {
        string temp = Path.Combine(Path.GetTempPath(), "FishTank-ui-" + Guid.NewGuid().ToString("N"));
        string output = Path.GetFullPath(args.Length == 0 ? "ui-artifacts" : args[0]);
        Directory.CreateDirectory(temp);
        Directory.CreateDirectory(output);
        Core.Data = temp;
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        SimpleExplanations.Install();
        try
        {
            string[] pages = { "Dashboard", "Antivirus", "Real-time monitoring", "Network Security", "System Integrity", "Security Center", "Quarantine", "Breach Checks", "Password Manager", "Tailscale Devices", "Settings" };
            foreach (string theme in new[] { "Dark", "Light" })
            foreach (int scale in new[] { 100, 150 })
            foreach (bool advanced in new[] { false, true })
            {
                var appearance = new FishTank.Appearance { Theme = theme, TextScale = scale, AdvancedMode = advanced, ReducedMotion = true };
                AppearanceStore.Save(appearance);
                using (var form = new Desktop())
                {
                    form.Size = new Size(1000, 740);
                    foreach (string page in pages.Where(p => NavigationPolicy.IsVisible(p, advanced)))
                    {
                        typeof(Desktop).GetMethod("Navigate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, new object[] { page });
                        form.RenderPreview(Path.Combine(output, $"{theme}-{scale}-{advanced}-{page.Replace(' ', '-')}.png"), page, appearance);
                        // Exercise live transitions too, rather than only the initial shell colors.
                        var navigate = typeof(Desktop).GetMethod("Navigate", BindingFlags.Instance | BindingFlags.NonPublic);
                        navigate.Invoke(form, new object[] { "Dashboard" });
                        navigate.Invoke(form, new object[] { page });
                        var navigation = Descendants(form).OfType<CleanButton>().Where(b => b.Tag is string).ToArray();
                        Check(navigation.Length == (advanced ? 11 : 6), "Incorrect navigation count");
                        var colors = Palette.From(appearance);
                        foreach (var button in navigation)
                        {
                            Check(button.Hover == button.Fill, "Sidebar hover changes fill: " + button.Tag);
                            Check(button.BackColor == button.Parent.BackColor, "Sidebar corner background mismatch");
                            Check(button.AccessibleName == (string)button.Tag, "Missing navigation accessible name");
                            Check(button.Border == ((string)button.Tag == page ? colors.Accent : colors.Sidebar), "Selection border did not follow navigation");
                        }
                        var flow = Descendants(form).OfType<FlowLayoutPanel>().First(p => p.FlowDirection == FlowDirection.TopDown);
                        Check(flow.Controls.Cast<Control>().All(c => c.Width <= flow.ClientSize.Width), "Page requires horizontal scrolling");
                    }
                }
            }
            Console.WriteLine(checks + " Windows UI checks passed; inspect PNGs for text clipping, focus, and spacing.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { Directory.Delete(temp, true); }
    }
}
