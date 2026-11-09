using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace FishTank;
public sealed partial class Desktop
{
    private void OpenIncidentWorkspace()
    {
        using (var dialog = UtilityDialog("Incident inbox", out var panel))
        {
            Help(panel, "Keep a local timeline, notes and selected evidence. No automatic isolation, process termination, file execution, credential rotation or remote sharing occurs. Operator confirmations are recorded separately from observed evidence. If compromise is suspected, use a separate trusted device for account recovery.");
            var filter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Filter incident inbox" };
            filter.Items.AddRange(new[] { "Needs review", "Operator closed", "All cases" });
            filter.SelectedIndex = 0;
            var counts = new Label { AutoSize = true, AccessibleName = "Incident counts" };
            var source = new Label { AutoSize = true, AccessibleName = "Incident source and time" };
            var cases = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Local incident case" }; var ids = new List<string>();
            var title = new TextBox { MaxLength = 120, AccessibleName = "New incident title" };
            var notes = new TextBox { Multiline = true, Height = 130, MaxLength = 10000, ScrollBars = ScrollBars.Vertical, AccessibleName = "Private incident notes" };
            var closed = new CheckBox { Text = "Operator closed this case (does not certify eradication)", AutoSize = true };
            var timeline = new ListBox { Height = 160, HorizontalScrollbar = true, AccessibleName = "Incident timeline" };
            var area = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Recorded evidence category" }; area.Items.AddRange(CoverageCatalog.Areas); area.SelectedIndex = 0;
            var evidence = new ListBox { Height = 130, HorizontalScrollbar = true, AccessibleName = "Recorded observations to preserve" }; var observations = new List<EvidenceItem>(); EvidenceSnapshot snapshot = null;
            string Selected() { if (cases.SelectedIndex < 0) throw new InvalidOperationException("Create or select a case first."); return ids[cases.SelectedIndex]; }
            Action loadCase = () =>
            {
                timeline.Items.Clear();
                if (cases.SelectedIndex < 0)
                {
                    notes.Clear(); closed.Checked = false; source.Text = "No cases in this view.";
                    notes.Enabled = closed.Enabled = false;
                    return;
                }
                var item = IncidentWorkspace.Load(Selected());
                notes.Enabled = closed.Enabled = true;
                notes.Text = item.Notes; closed.Checked = item.Closed;
                source.Text = (string.IsNullOrEmpty(item.SourceEventKey) ? "Local case" : "Microsoft Defender event")
                    + " · Case recorded " + item.CreatedUtc.ToLocalTime().ToString("g");
                foreach (var row in item.Timeline)
                    timeline.Items.Add(row.RecordedUtc.ToLocalTime().ToString("g") + " · " + row.Action + " · " + row.Detail);
            };
            Action<string> refresh = select =>
            {
                var all = IncidentWorkspace.Ids().Select(IncidentWorkspace.Load)
                    .OrderByDescending(item => item.CreatedUtc).ToList();
                var shown = all.Where(item => filter.SelectedIndex == 2 || item.Closed == (filter.SelectedIndex == 1)).ToList();
                counts.Text = all.Count(item => !item.Closed) + " needs review · " + all.Count(item => item.Closed) + " operator closed";
                ids = shown.Select(item => item.Id).ToList();
                cases.Items.Clear();
                foreach (var item in shown) cases.Items.Add(item.Title + (item.Closed ? " · closed" : " · needs review"));
                if (ids.Count > 0) cases.SelectedIndex = Math.Max(0, ids.IndexOf(select));
                else loadCase();
            };
            Action loadEvidence = () => { snapshot = CoverageHistory.Read((string)area.SelectedItem).LastOrDefault(); observations = snapshot?.Items ?? new List<EvidenceItem>(); evidence.Items.Clear(); foreach (var item in observations) evidence.Items.Add(item.State + " · " + item.Name); };
            cases.SelectedIndexChanged += delegate { loadCase(); }; area.SelectedIndexChanged += delegate { loadEvidence(); };
            filter.SelectedIndexChanged += delegate { refresh(cases.SelectedIndex >= 0 ? Selected() : null); };
            panel.Controls.Add(counts); panel.Controls.Add(filter); panel.Controls.Add(cases); panel.Controls.Add(source);
            Help(panel, "Title for a new case:"); panel.Controls.Add(title);
            panel.Controls.Add(UtilityButton("Create a local case", () => { var item = IncidentWorkspace.Create(title.Text); if (filter.SelectedIndex == 1) filter.SelectedIndex = 0; refresh(item.Id); }));
            Help(panel, "Private notes (save deliberately before switching cases):"); panel.Controls.Add(notes); panel.Controls.Add(closed);
            panel.Controls.Add(UtilityButton("Save notes and workflow state", () => { string id = Selected(); IncidentWorkspace.Update(id, notes.Text, closed.Checked); refresh(id); }));
            panel.Controls.Add(timeline); Help(panel, "Choose already-recorded evidence, then select one observation to preserve:"); panel.Controls.Add(area); panel.Controls.Add(evidence);
            panel.Controls.Add(UtilityButton("Refresh recorded evidence list", loadEvidence));
            panel.Controls.Add(UtilityAsyncButton("Collect current evidence for this category", async () => await EverydayWork(async () => { string category = (string)area.SelectedItem; await Task.Run(() => CoverageHistory.Collect(category)); if (!dialog.IsDisposed) loadEvidence(); })));
            panel.Controls.Add(UtilityButton("Preserve the selected recorded observation", () => { string id = Selected(); if (snapshot == null || evidence.SelectedIndex < 0) throw new InvalidOperationException("Select an observation first."); var observation = observations[evidence.SelectedIndex]; if (MessageBox.Show(dialog, "Preserve this observation in the encrypted local case? It can contain usernames, paths or event details.\r\n" + observation.Name, "Preserve selected evidence", MessageBoxButtons.YesNo) != DialogResult.Yes) return; IncidentWorkspace.Preserve(id, snapshot.Category + " observation", new { snapshot.CapturedUtc, snapshot.Category, Observation = observation }); loadCase(); }));
            panel.Controls.Add(UtilityButton("Preserve recent app activity summaries", () => { string id = Selected(); var recent = AppActivity.Read().OrderByDescending(x => x.TimeUtc).Take(30).ToArray(); if (MessageBox.Show(dialog, "Preserve the latest " + recent.Length + " local app activity summaries? These are app records, not a complete Windows event timeline.", "Preserve activity", MessageBoxButtons.YesNo) != DialogResult.Yes) return; IncidentWorkspace.Preserve(id, "Recent app activity summaries", recent); loadCase(); }));
            panel.Controls.Add(UtilityAsyncButton("Preserve a selected file's metadata and SHA-256", async () => await EverydayWork(async () => { string id = Selected(); using (var pick = new OpenFileDialog { Title = "Hash metadata only; file is never executed or uploaded" }) if (pick.ShowDialog(dialog) == DialogResult.OK) { string path = pick.FileName; if (MessageBox.Show(dialog, "Record this file's local path, size, modification time and hash? File bytes will not be copied or executed.\r\n" + path, "Preserve file metadata", MessageBoxButtons.YesNo) != DialogResult.Yes) return; await Task.Run(() => IncidentWorkspace.PreserveFileHash(id, path)); if (!dialog.IsDisposed) loadCase(); } })));
            Help(panel, "Containment and recovery checklist: review isolation; preserve evidence; verify an offline recovery copy; rotate affected credentials from a trusted device; review recovery and monitoring. A checkbox or operator note does not prove a step succeeded.");
            panel.Controls.Add(UtilityButton("Open network settings to review manual isolation", () => { if (MessageBox.Show(dialog, "Open Windows network settings for manual isolation review? Disconnecting can interrupt remote access or support. FishTank will not disconnect adapters or change firewall rules.", "Containment review", MessageBoxButtons.YesNo) == DialogResult.Yes) Process.Start(new ProcessStartInfo("ms-settings:network-status") { UseShellExecute = true }); }));
            foreach (string item in new[] { "Isolation reviewed", "Recovery copy verified", "Credentials rotated on a trusted device", "Recovery and monitoring reviewed" }) { string step = item; panel.Controls.Add(UtilityButton("Record operator confirmation: " + step, () => { string id = Selected(); if (MessageBox.Show(dialog, "Record your confirmation of this step? It will be labeled operator-reported, not independently verified.\r\n" + step, "Confirm response step", MessageBoxButtons.YesNo) != DialogResult.Yes) return; IncidentWorkspace.ConfirmStep(id, step); loadCase(); })); }
            panel.Controls.Add(UtilityButton("Preview, redact and export this case", () => { string json = IncidentWorkspace.Export(Selected()); using (var review = UtilityDialog("Incident export review; nothing is sent", out var body)) { var editor = new TextBox { Multiline = true, ScrollBars = ScrollBars.Both, Height = 380, AccessibleName = "Editable incident export preview", Text = json }; Help(body, "This is a readable export containing notes and selected evidence, potentially including names and paths. Redact before sharing. Edited exports do not replace protected originals; edits can invalidate embedded evidence checksums."); body.Controls.Add(editor); body.Controls.Add(UtilityButton("Save this reviewed text as a new file", () => { using (var save = new SaveFileDialog { Filter = "Reviewed incident text|*.txt", FileName = "FishTank-incident-reviewed.txt" }) if (save.ShowDialog(review) == DialogResult.OK) RestoreFileWriter.WriteNew(save.FileName, Encoding.UTF8.GetBytes(editor.Text)); })); review.ShowDialog(dialog); } }));
            refresh(null); loadEvidence(); dialog.FormClosing += delegate(object sender, FormClosingEventArgs e) { if (everydayWorking) e.Cancel = true; }; dialog.ShowDialog(this);
        }
    }
}
