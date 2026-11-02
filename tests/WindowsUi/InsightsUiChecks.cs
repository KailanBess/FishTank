using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using FishTank;

internal static class InsightsUiChecks
{
    private static Control[] All(Control control) => control.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(All(c))).ToArray();
    internal static void Run(string output, Action<bool,string> check)
    {
        Core.Audit("scan-complete", new { exitCode = 0 });
        using (var owner = new Desktop())
        {
            owner.StartPosition = FormStartPosition.Manual; owner.Location = new Point(-30000, -30000); owner.ShowInTaskbar = false; owner.Show();
            foreach (string method in new[] { "OpenScanSchedule", "OpenFolderReminders", "OpenSettingsSearch", "OpenSupportReport", "OpenActivityTimeline" })
            {
                Exception failure = null; bool visited = false;
                using (var timer = new Timer { Interval = 250 })
                {
                    timer.Tick += delegate {
                        var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f != owner && f.Modal);
                        if (dialog == null || visited) return;
                        visited = true;
                        try
                        {
                            dialog.Size = new Size(620, 650); dialog.PerformLayout();
                            var controls = All(dialog);
                            check(controls.OfType<Button>().Any(), method + " has no controls");
                            check(controls.Where(c => c.Parent is FlowLayoutPanel).All(c => c.Width <= c.Parent.ClientSize.Width), method + " requires horizontal scrolling");
                            if (method == "OpenSettingsSearch")
                            {
                                controls.OfType<TextBox>().Single(x => x.AccessibleName == "Search settings").Text = "MFA";
                                check(controls.OfType<ListBox>().Single().Items.Count == 1, "Settings keyword search failed");
                            }
                            if (method == "OpenScanSchedule") check(!controls.OfType<CheckBox>().Single(x => x.Text == "Enable scheduled Defender scans").Checked, "Automatic scanning defaults on");
                            if (method == "OpenSupportReport")
                            {
                                var preview = controls.OfType<TextBox>().Single(x => x.AccessibleName == "Editable support report preview");
                                check(!preview.ReadOnly && preview.Text.Contains("support report"), "Support preview cannot be edited");
                                preview.Text = preview.Text.Replace("Windows:", "Redacted Windows:");
                            }
                            using (var bitmap = new Bitmap(dialog.Width, dialog.Height)) { dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size)); bitmap.Save(Path.Combine(output, method + ".png")); }
                        }
                        catch(Exception ex) { failure = ex; }
                        finally { dialog.Close(); }
                    };
                    timer.Start();
                    typeof(Desktop).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(owner, null);
                    timer.Stop();
                }
                if (failure != null) throw failure;
                check(visited, method + " did not open");
            }
            owner.Hide();
        }
    }
}
