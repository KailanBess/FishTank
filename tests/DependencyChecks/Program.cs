using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using FishTank;

internal static class DependencyChecks
{
    private static int checks;
    private static void Check(bool value, string text) { if (!value) throw new Exception(text); checks++; }
    private static async Task Reject<T>(Func<Task> action) where T : Exception
    { try { await action(); } catch (T) { checks++; return; } throw new Exception("Expected " + typeof(T).Name); }
    private static DependencySnapshot Snapshot(string json) { return DependencySnapshots.Parse(Encoding.UTF8.GetBytes(json)); }
    [STAThread] private static int Main(string[] args)
    {
        string root = Path.Combine(Path.GetTempPath(), "FishTank-dependency-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); Core.Data = root;
        try
        {
            ModelChecks(root).GetAwaiter().GetResult();
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            foreach (int scale in new[] { 100, 150, 200 })
            {
                int calls = 0;
                var input = Snapshot("{\"version\":3,\"libraries\":{\"Fixture.Package/1.0.0\":{\"type\":\"package\"}}}");
                var colors = Palette.From(new Appearance { Theme = "Dark", TextScale = scale });
                using (var form = new DependencyReviewDialog(colors, scale / 100f, (json, token) => { calls++; return Task.FromResult("{\"results\":[{}]}"); }))
                {
                    form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-4000, -4000);
                    form.Show(); Application.DoEvents();
                    Check(calls == 0 && form.SelectedSnapshot == null, "Opening advisory dialog sent metadata");
                    form.LoadSnapshot(input); Application.DoEvents();
                    Check(calls == 0 && form.CurrentReport == null, "Selecting local metadata queried the provider");
                    var task = form.QueryConfirmedAsync();
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    while (!task.IsCompleted && watch.Elapsed < TimeSpan.FromSeconds(10)) { Application.DoEvents(); Thread.Sleep(5); }
                    task.GetAwaiter().GetResult(); Application.DoEvents();
                    Check(calls == 1 && form.CurrentReport.Findings[0].State == "No published matches", "Reviewed fixture query did not complete");
                    var grid = Descendants(form).OfType<DataGridView>().Single();
                    Check(grid.Rows.Count == 1 && grid.Rows[0].Cells[1].Value.ToString() == "Fixture.Package", "Package/result table lost metadata");
                    Check(grid.Parent.ClientRectangle.Contains(grid.Bounds) && grid.Height > 120, "Advisory table clipped at enlarged text");
                    Check(grid.Rows[0].Height >= grid.Font.Height + 6, "Advisory row text clipped");
                    if (args.Length == 1)
                    {
                        Directory.CreateDirectory(args[0]); using (var image = new Bitmap(form.Width, form.Height))
                        { form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(Path.Combine(args[0], "dependencies-" + scale + ".png")); }
                    }
                    form.Close(); Application.DoEvents();
                }
            }
            Console.WriteLine("Dependency review: " + checks + " import/provider/privacy/pagination/export/native-UI checks passed. No real provider was contacted.");
            return 0;
        }
        finally
        {
            string full = Path.GetFullPath(root), temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(full), @"\AFishTank-dependency-[a-f0-9]{32}\z")) throw new IOException("Unsafe fixture cleanup.");
            Directory.Delete(full, true);
        }
    }
    private static IEnumerable<Control> Descendants(Control parent) { return parent.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c))); }
    private static async Task ModelChecks(string root)
    {
        string raw = "{\"version\":3,\"libraries\":{\"Fixture.Package/1.0.0\":{\"type\":\"package\"},\"fixture.package/1.0.0\":{\"type\":\"package\"},\"Project/1.0\":{\"type\":\"project\"}},\"packageFolders\":{\"Private credential/path/token\":\"ignored\"}}";
        var input = Snapshot(raw);
        Check(input.Packages.Count == 1 && input.SkippedItems == 1 && input.SourceSha256.Length == 64, "Import counts/hash/deduplication incorrect");
        var cyclone = Snapshot("{\"bomFormat\":\"CycloneDX\",\"specVersion\":\"1.5\",\"components\":[{\"purl\":\"pkg:pypi/Some_Name@1.2.3\",\"version\":\"1.2.3\"},{\"purl\":\"pkg:npm/unsupported@1.0.0\"}]}");
        Check(cyclone.Packages.Single().Name == "some-name" && cyclone.SkippedItems == 1, "Supported ecosystem normalization/unsupported item coverage incorrect");
        foreach (var bad in new[] {
            "{\"version\":4,\"libraries\":{\"A/1.0\":{\"type\":\"package\"}}}",
            "{\"version\":3,\"version\":3,\"libraries\":{}}",
            "{\"version\":3,\"vers\\u0069on\":3,\"libraries\":{}}",
            "{\"bomFormat\":\"CycloneDX\",\"specVersion\":\"9.0\",\"components\":[]}",
            "{\"version\":3,\"libraries\":{\"Bad/../../secret\":{\"type\":\"package\"}}}", "{}" })
            await Reject<InvalidDataException>(() => { Snapshot(bad); return Task.CompletedTask; });
        var libraries = string.Join(",", Enumerable.Range(0, 257).Select(i => "\"Package" + i + "/1.0\":{\"type\":\"package\"}"));
        await Reject<InvalidDataException>(() => { Snapshot("{\"version\":3,\"libraries\":{" + libraries + "}}"); return Task.CompletedTask; });
        await Reject<InvalidDataException>(() => { DependencySnapshots.Parse(new byte[4 * 1024 * 1024 + 1]); return Task.CompletedTask; });
        string path = Path.Combine(root, "source.json"); File.WriteAllText(path, raw, new UTF8Encoding(false));
        var before = File.ReadAllBytes(path); Check(DependencySnapshots.Read(path).SourceSha256 == input.SourceSha256 && File.ReadAllBytes(path).SequenceEqual(before), "Read modified input or changed content attribution");
        int calls = 0;
        var empty = await DependencyAdvisories.CheckAsync(input, (json, token) => {
            calls++; Check(!json.Contains("Private") && !json.Contains("packageFolders") && json.Contains("Fixture.Package"), "Query transmitted raw/private source fields");
            return Task.FromResult("{\"results\":[{}]}"); });
        Check(calls == 1 && empty.Findings[0].State == "No published matches" && empty.Input.SkippedItems == 1, "Valid empty result/coverage was mislabeled");
        foreach (string response in new[] { "{}", "{\"results\":[]}", "{\"results\":[{\"error\":\"Private provider details\"}]}", "{\"results\":[{\"vulns\":[{\"id\":\"https://untrusted\"}]}]}", "{\"results\":[{}],\"results\":[{}]}" })
        {
            var invalid = await DependencyAdvisories.CheckAsync(input, (json, token) => Task.FromResult(response));
            Check(invalid.Findings[0].State == "Unknown" && !DependencyAdvisoryExport.Serialize(invalid).Contains("Private provider"), "Invalid provider result inferred clean coverage or leaked raw details");
        }
        var unavailable = await DependencyAdvisories.CheckAsync(input, (json, token) => Task.FromException<string>(new IOException("Private path/credentials")));
        Check(unavailable.Findings[0].State == "Unavailable" && !DependencyAdvisoryExport.Serialize(unavailable).Contains("Private"), "Unavailable provider became no-match or leaked exception");
        int page = 0;
        var paged = await DependencyAdvisories.CheckAsync(input, (json, token) => {
            page++; if (page == 2) Check(json.Contains("page_token") && json.Contains("next2"), "Pagination token was not attributed");
            return Task.FromResult(page == 1 ? "{\"results\":[{\"vulns\":[{\"id\":\"CVE-fixture-1\"}],\"next_page_token\":\"next2\"}]}" : "{\"results\":[{\"vulns\":[{\"id\":\"CVE-fixture-1\"},{\"id\":\"GHSA-fixture-2\"}]}]}"); });
        Check(page == 2 && paged.Findings[0].AdvisoryIds.Length == 2 && paged.Findings[0].State == "Advisory IDs reported", "Paginated IDs/deduplication incorrect");
        page = 0; var limited = await DependencyAdvisories.CheckAsync(input, (json, token) => Task.FromResult("{\"results\":[{\"next_page_token\":\"page" + (++page) + "\"}]}"));
        Check(page == 4 && limited.Findings[0].State == "Partial" && limited.Findings[0].Code == "provider-page-limit", "Page cap was hidden or not bounded");
        page = 0; var cycle = await DependencyAdvisories.CheckAsync(input, (json, token) => { page++; return Task.FromResult("{\"results\":[{\"next_page_token\":\"again\"}]}"); });
        Check(page == 2 && cycle.Findings[0].State == "Unknown", "Pagination cycle inferred complete results");
        string ids = string.Join(",", Enumerable.Range(0, 2001).Select(i => "{\"id\":\"CVE-fixture-" + i + "\"}"));
        var capped = await DependencyAdvisories.CheckAsync(input, (json, token) => Task.FromResult("{\"results\":[{\"vulns\":[" + ids + "]}]}"));
        Check(capped.Findings[0].State == "Partial" && capped.Findings[0].AdvisoryIds.Length == 2000, "Advisory ID resource cap was hidden");
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel(); int sent = 0;
            await Reject<OperationCanceledException>(() => DependencyAdvisories.CheckAsync(input, (json, token) => { sent++; return Task.FromResult("{}"); }, canceled.Token));
            Check(sent == 0, "Canceled query transmitted metadata");
        }
        using (var active = new CancellationTokenSource())
            await Reject<OperationCanceledException>(() => DependencyAdvisories.CheckAsync(input, (json, token) => { active.Cancel(); token.ThrowIfCancellationRequested(); return Task.FromResult("{}"); }, active.Token));
        string report = Path.Combine(root, "advisories.json"); DependencyAdvisoryExport.SaveNew(report, paged, new string[0]);
        var saved = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(report));
        Check(saved["recordedAtUtc"].ToString().EndsWith("Z") && File.ReadAllText(report).Contains("https://osv.dev/vulnerability/"), "Export lost UTC/source links");
        byte[] prior = File.ReadAllBytes(report);
        await Reject<IOException>(() => { DependencyAdvisoryExport.SaveNew(report, empty, new string[0]); return Task.CompletedTask; });
        Check(File.ReadAllBytes(report).SequenceEqual(prior), "Export overwrote prior report");
        await Reject<IOException>(() => { DependencyAdvisoryExport.SaveNew(Path.Combine(root, "new.json"), empty, new[] { root }); return Task.CompletedTask; });
        await Reject<IOException>(() => { DependencyAdvisoryExport.SaveNew(Path.Combine(root, "wrong.txt"), empty, new string[0]); return Task.CompletedTask; });
        Check(Directory.GetFiles(root, "*.tmp").Length == 0 && File.ReadAllBytes(path).SequenceEqual(before), "Export leaked temporary output or modified input");
    }
}
