using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FishTank;

public sealed class DependencyReviewDialog : Form
{
    private readonly Func<string, CancellationToken, Task<string>> transport;
    private readonly Button choose = new Button { Text = "Choose snapshot...", AutoSize = true };
    private readonly Button query = new Button { Text = "Query OSV (online)...", AutoSize = true, Enabled = false };
    private readonly Button cancel = new Button { Text = "Cancel query", AutoSize = true, Enabled = false };
    private readonly Button export = new Button { Text = "Save new JSON report...", AutoSize = true, Enabled = false };
    private readonly Label status = new Label { Dock = DockStyle.Top, AutoSize = true, Text = "No snapshot selected. Nothing has been sent." };
    private readonly DataGridView table = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, RowHeadersVisible = false, AllowUserToAddRows = false,
        AllowUserToDeleteRows = false, AutoGenerateColumns = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
    private DependencySnapshot snapshot;
    private DependencyAdvisoryReport report;
    private CancellationTokenSource pending;
    internal DependencySnapshot SelectedSnapshot { get { return snapshot; } }
    internal DependencyAdvisoryReport CurrentReport { get { return report; } }
    public DependencyReviewDialog(Palette colors, float textScale = 1f, Func<string, CancellationToken, Task<string>> transport = null)
    {
        this.transport = transport;
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "FishTank · Dependency advisory review"; AccessibleName = Text;
        Size = new Size(1100, 760); MinimumSize = new Size(760, 560); Font = new Font("Segoe UI", 10 * textScale);
        BackColor = colors.Background; ForeColor = colors.Text; StartPosition = FormStartPosition.CenterParent;
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        actions.Controls.AddRange(new Control[] { choose, query, cancel, export });
        var note = new TextBox { Dock = DockStyle.Bottom, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Height = Math.Min(180, Font.Height * 5 + 12),
            BackColor = colors.Surface, ForeColor = colors.Text, AccessibleName = "Dependency review coverage and network disclosure",
            Text = "Choose a NuGet project.assets.json schema-3 or CycloneDX 1.5 component snapshot (up to 4 MiB, 256 package versions). Parsing is local and does not execute build scripts. Query OSV sends supported package names, ecosystems and versions to api.osv.dev only after confirmation; private package names may identify software. Raw files, paths and credentials are not sent. OSV/provider coverage and snapshot freshness are not verified. No matches is not proof of safety. Partial/unavailable results stay explicit. Up to four provider pages and 2,000 advisory-ID entries are retained; aliases may describe the same issue. No software is updated, installed or removed." };
        foreach (var title in new[] { "Ecosystem", "Package", "Version", "Observation", "Advisory IDs" })
            table.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = title, AutoSizeMode = title == "Advisory IDs" ? DataGridViewAutoSizeColumnMode.None : DataGridViewAutoSizeColumnMode.AllCells, Width = 500, SortMode = DataGridViewColumnSortMode.NotSortable });
        table.AccessibleName = "Reviewed package metadata and advisory observations"; table.BackgroundColor = colors.Surface;
        table.EnableHeadersVisualStyles = false; table.DefaultCellStyle.BackColor = table.ColumnHeadersDefaultCellStyle.BackColor = colors.Surface;
        table.DefaultCellStyle.ForeColor = table.ColumnHeadersDefaultCellStyle.ForeColor = colors.Text;
        table.DefaultCellStyle.SelectionBackColor = colors.Accent; table.DefaultCellStyle.SelectionForeColor = colors.AccentText;
        table.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        table.RowTemplate.Height = Font.Height + 8;
        Controls.Add(table); Controls.Add(status); Controls.Add(actions); Controls.Add(note);
        Action wrap = () => status.MaximumSize = new Size(Math.Max(100, ClientSize.Width - Padding.Horizontal), 0);
        Resize += delegate { wrap(); }; wrap();
        foreach (var button in new[] { choose, query, cancel, export })
        { button.BackColor = colors.Surface; button.ForeColor = colors.Text; button.FlatStyle = FlatStyle.Flat; button.Padding = new Padding(8, 4, 8, 4); }
        choose.Click += async delegate { await ChooseAsync(); };
        query.Click += async delegate
        {
            if (snapshot == null || pending != null) return;
            if (MessageBox.Show(this, "Send the " + snapshot.Packages.Count + " reviewed package names, ecosystems and versions to https://api.osv.dev? The provider sees your IP address. Private package names may identify software. No raw files, paths or credentials are sent. Up to four requests may be made. No software changes occur.",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) await QueryConfirmedAsync();
        };
        cancel.Click += delegate { if (pending != null) pending.Cancel(); };
        export.Click += delegate { Export(); };
        FormClosed += delegate { if (pending != null) pending.Cancel(); };
    }
    private async Task ChooseAsync()
    {
        if (pending != null) return;
        using (var picker = new OpenFileDialog { Filter = "JSON snapshot|*.json", CheckFileExists = true })
        {
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            choose.Enabled = query.Enabled = export.Enabled = false;
            try { var next = await Task.Run(() => DependencySnapshots.Read(picker.FileName)); if (!IsDisposed) LoadSnapshot(next); }
            catch { if (!IsDisposed) status.Text = "Snapshot could not be read or is unsupported. No data was sent; existing files are unchanged."; }
            finally { if (!IsDisposed) { choose.Enabled = true; query.Enabled = snapshot != null; export.Enabled = report != null; } }
        }
    }
    internal void LoadSnapshot(DependencySnapshot value)
    {
        if (pending != null) throw new InvalidOperationException("Finish or cancel the active query first.");
        snapshot = value; report = null; export.Enabled = false; query.Enabled = value != null;
        status.Text = value == null ? "No snapshot selected." : value.Packages.Count + " supported package versions · " + value.SkippedItems + " skipped items. Review all metadata before querying. Nothing sent yet.";
        Render();
    }
    internal async Task QueryConfirmedAsync()
    {
        if (snapshot == null || pending != null) return;
        var input = snapshot; report = null; Render(); pending = new CancellationTokenSource();
        choose.Enabled = query.Enabled = export.Enabled = false; cancel.Enabled = true; status.Text = "Querying reviewed metadata; no protection verdict is inferred...";
        try
        {
            var next = await DependencyAdvisories.CheckAsync(input, transport, pending.Token);
            if (IsDisposed) return;
            report = next; Render(); export.Enabled = true;
            int incomplete = next.Findings.Count(f => f.State == "Unknown" || f.State == "Unavailable" || f.State == "Partial");
            status.Text = "Recorded " + next.RecordedAtUtc.ToString("u") + " · " + incomplete + " partial/unknown/unavailable packages · " + input.SkippedItems + " skipped input items. No matches is not proof of safety.";
        }
        catch (OperationCanceledException) { if (!IsDisposed) status.Text = "Canceled. No completed report is presented; prior results were cleared."; }
        catch { if (!IsDisposed) status.Text = "Query unavailable. No completed report or safe result is inferred."; }
        finally { pending.Dispose(); pending = null; if (!IsDisposed) { choose.Enabled = query.Enabled = true; cancel.Enabled = false; export.Enabled = report != null; } }
    }
    private void Render()
    {
        table.Rows.Clear(); if (snapshot == null) return;
        for (int i = 0; i < snapshot.Packages.Count; i++)
        {
            var package = snapshot.Packages[i]; var finding = report == null ? null : report.Findings[i];
            string ids = finding == null ? "" : string.Join(", ", finding.AdvisoryIds.Take(20)) + (finding.AdvisoryIds.Length > 20 ? " (more IDs in JSON report)" : "");
            table.Rows.Add(package.Ecosystem, package.Name, package.Version, finding == null ? "Not queried" : finding.State + " (" + finding.Code + ")", ids);
        }
    }
    private void Export()
    {
        if (report == null || pending != null) return;
        using (var picker = new SaveFileDialog { Filter = "JSON report|*.json", DefaultExt = "json", OverwritePrompt = false, FileName = "FishTank-dependency-advisories-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".json" })
        {
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                DependencyAdvisoryExport.SaveNew(picker.FileName, report, new[] { AppDomain.CurrentDomain.BaseDirectory, Core.Data });
            }
            catch { MessageBox.Show(this, "Choose a new .json filename outside FishTank's profile/program folders and linked paths. Existing files are never overwritten.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information); }
        }
    }
}
