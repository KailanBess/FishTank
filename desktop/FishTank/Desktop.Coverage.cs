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
    private void OpenRansomwareCenter() => OpenCoverageArea("Ransomware");
    private void OpenAsrCenter() => OpenCoverageArea("ASR");
    private void OpenApplicationControl() => OpenCoverageArea("AppControl");
    private void OpenHardwareSecurity() => OpenCoverageArea("Hardware");
    private void OpenSoftwareInventory() => OpenCoverageArea("Software");
    private void OpenPersistenceMonitor() => OpenCoverageArea("Persistence");
    private void OpenExposureMonitor() => OpenCoverageArea("Network");
    private void OpenCfaPreview() => OpenProtectionManager("CFA");
    private void OpenAsrPreview() => OpenProtectionManager("ASR");
    private void OpenCoverageArea(string area)
    {
        if (!CoverageCatalog.Areas.Contains(area)) throw new ArgumentException("Unknown security area.");
        using (var dialog = UtilityDialog(area + " · security coverage", out var panel))
        {
            string explanation = area == "Ransomware" ? "Review Controlled Folder Access, write-block/audit events and backup readiness. Audit records observations and does not prevent writes. Existing folder/app exceptions remain unchanged."
                : area == "ASR" ? "Review Defender ASR rules, exclusions and audit/block events. The manager offers eight documented rules with audit-first setup. No rule is silently enabled or disabled."
                : area == "AppControl" ? "Review Smart App Control, code-integrity/AppLocker observations and publisher signatures. FishTank does not rewrite boot/app-control policy or silently trust unsigned programs."
                : area == "Hardware" ? "Review Secure Boot, TPM, VBS, memory-integrity runtime and driver-blocklist evidence. Missing or unsupported readings remain unknown. Configure firmware/drivers in their supported tools after checking compatibility."
                : area == "Software" ? "Inventory registered and packaged apps with observation times. Optional WinGet queries contact configured sources for candidate updates; they do not install software, accept source agreements or prove vulnerability coverage."
                : area == "Persistence" ? "Compare startup entries, service configurations, scheduled tasks and WMI subscriptions. Command definitions are hashed rather than retained. Legitimate changes and coverage gaps require review."
                : "Compare listening TCP/UDP endpoints, effective firewall rules and remote-access configuration. No remote probes are made. A listener or allow rule is not proof of Internet exposure or malware.";
            Help(panel, explanation);
            var status = new Label { AutoSize = true, Text = "No check has been requested from this window." };
            var search = new TextBox { AccessibleName = "Filter security observations" };
            var list = new ListBox { Height = 190, HorizontalScrollbar = true, AccessibleName = "Security observations" };
            var details = new TextBox { Multiline = true, ReadOnly = true, Height = 200, ScrollBars = ScrollBars.Vertical, AccessibleName = "Selected security evidence and limitations" };
            var all = new List<EvidenceItem>(); var visible = new List<EvidenceItem>(); string current = area;
            Action filter = () => { visible = all.Where(x => (x.Name + " " + x.State + " " + x.Value).IndexOf(search.Text, StringComparison.OrdinalIgnoreCase) >= 0).ToList(); list.Items.Clear(); foreach (var item in visible) list.Items.Add(item.State + " · " + item.Name); if (visible.Count > 0) list.SelectedIndex = 0; else details.Text = "No matching recorded observations. This is not a safety verdict."; };
            Action<string> load = category => { current = category; var snapshot = CoverageHistory.Read(category).LastOrDefault(); all = snapshot?.Items ?? new List<EvidenceItem>(); status.Text = snapshot == null ? "No saved " + category + " observation. Refresh to collect evidence." : category + " observed " + snapshot.CapturedUtc.ToLocalTime().ToString("G") + " · " + Math.Max(0, (DateTime.UtcNow - snapshot.CapturedUtc).TotalHours).ToString("0.0") + " hours old · " + all.Count(x => x.State == "Unknown") + " coverage gaps. Refresh before acting on stale evidence."; filter(); };
            search.TextChanged += delegate { filter(); }; list.SelectedIndexChanged += delegate { if (list.SelectedIndex >= 0) details.Text = visible[list.SelectedIndex].Value; };
            panel.Controls.Add(status); panel.Controls.Add(search); panel.Controls.Add(list); panel.Controls.Add(details);
            var collect = UtilityAsyncButton("Collect current " + area + " evidence", async () => await EverydayWork(async () => { await Task.Run(() => CoverageHistory.Collect(area)); if (!dialog.IsDisposed) load(area); })); panel.Controls.Add(collect); panel.Controls.SetChildIndex(collect, 1);
            panel.Controls.Add(UtilityButton("Compare the last two recorded checks", () => { var snapshots = CoverageHistory.Read(current); if (snapshots.Count < 2) throw new InvalidOperationException("Record two checks of this category first."); all = EvidenceComparison.Compare(snapshots[snapshots.Count - 2], snapshots.Last()).Where(x => x.State != "Unchanged").ToList(); status.Text = "Before/after comparison. Unknown coverage is not interpreted as removal or safety."; filter(); }));
            if (area == "Ransomware" || area == "ASR")
                panel.Controls.Add(UtilityButton("Preview an audit-first protection change", () => { dialog.Close(); if (area == "Ransomware") OpenCfaPreview(); else OpenAsrPreview(); }));
            if (area == "Ransomware") panel.Controls.Add(UtilityButton("Review backups and restore-drill readiness", () => { dialog.Close(); OpenRecoveryCenter(); }));
            if (area == "AppControl") panel.Controls.Add(UtilityAsyncButton("Inspect a selected application's publisher signature", async () => await EverydayWork(async () => { using (var pick = new OpenFileDialog { Title = "Inspect signature; selected file is never executed" }) if (pick.ShowDialog(dialog) == DialogResult.OK) { string path = pick.FileName; if (MessageBox.Show(dialog, "Check this file's Windows signature? Windows may consult certificate trust/revocation services. The file will not be executed or uploaded by FishTank.", "Publisher review", MessageBoxButtons.YesNo) != DialogResult.Yes) return; string result = await Task.Run(() => TrustedDownloads.Inspect(path)); if (!dialog.IsDisposed) details.Text = result; } })));
            if (area == "Software")
            {
                Help(panel, "Available-version queries use the verified Microsoft-signed packaged WinGet executable. If it or source consent is unavailable, the result stays unknown. Vendor installers remain under your control.");
                panel.Controls.Add(UtilityAsyncButton("Query available updates (no installs)", async () => await EverydayWork(async () => { if (MessageBox.Show(dialog, "Contact configured WinGet sources to list candidate updates? No source agreement will be accepted and no installer will be started.", "Available update query", MessageBoxButtons.YesNo) != DialogResult.Yes) return; await Task.Run(() => CoverageHistory.Collect("Updates")); if (!dialog.IsDisposed) load("Updates"); })));
                panel.Controls.Add(UtilityButton("Review Windows Update", () => Process.Start(new ProcessStartInfo("ms-settings:windowsupdate") { UseShellExecute = true })));
            }
            if (area == "Persistence" || area == "Network")
            {
                var prefs = CoverageMonitorPreferences.Load(); var enabled = new CheckBox { Text = "Enable periodic " + area.ToLowerInvariant() + " comparisons while FishTank is open", AutoSize = true, Checked = area == "Persistence" ? prefs.Persistence : prefs.Network };
                var minutes = new NumericUpDown { Minimum = 5, Maximum = 1440, Value = prefs.IntervalMinutes, AccessibleName = "Security monitoring interval in minutes" };
                panel.Controls.Add(enabled); Help(panel, "Shared monitoring interval in minutes (5–1440). First capture is an inventory, not a trusted baseline."); panel.Controls.Add(minutes);
                panel.Controls.Add(UtilityButton("Save monitoring preferences", () => { var latest = CoverageMonitorPreferences.Load(); if (area == "Persistence") latest.Persistence = enabled.Checked; else latest.Network = enabled.Checked; latest.IntervalMinutes = (int)minutes.Value; CoverageMonitorPreferences.Save(latest); status.Text = "Monitoring preferences saved. " + latest.LastResult; }));
            }
            panel.Controls.Add(UtilityButton("Open Windows Security for supported settings", () => Process.Start(new ProcessStartInfo("windowsdefender:") { UseShellExecute = true })));
            panel.Controls.Add(UtilityButton("Open incident response workspace", () => { dialog.Close(); OpenIncidentWorkspace(); }));
            load(area); dialog.FormClosing += delegate(object sender, FormClosingEventArgs e) { if (everydayWorking) e.Cancel = true; }; dialog.ShowDialog(this);
        }
    }
    private void OpenProtectionManager(string control)
    {
        using (var dialog = UtilityDialog(control + " · audit-first change preview", out var panel))
        {
            Help(panel, "This tool changes one Windows preference only after preview, confirmation and app-password/MFA authorization. Changes require administrator execution, active Defender and supported Windows; managed policy and unknown prerequisites are refused. No UAC, cloud, tamper, exclusion or firmware settings are silently weakened. Audit does not block. Blocking requires an existing audit mode and your compatibility review.");
            var rule = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "ASR rule to review" };
            if (control == "ASR") foreach (var entry in CoverageCatalog.Rules) rule.Items.Add(entry.Title); else rule.Items.Add("Controlled Folder Access"); rule.SelectedIndex = 0;
            var desired = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Desired protection mode" }; desired.Items.AddRange(new object[] { "Audit (observes; no blocking)", "Block (requires prior audit review)" }); desired.SelectedIndex = 0;
            var reviewed = new CheckBox { Text = "I reviewed audit events and compatibility before choosing Block", AutoSize = true };
            var preview = new TextBox { Multiline = true, ReadOnly = true, Height = 220, ScrollBars = ScrollBars.Vertical, AccessibleName = "Protection change preview", Text = "Choose a control and request a fresh preview. This window has not requested a Windows change." };
            Help(panel, "Control:"); panel.Controls.Add(rule); Help(panel, "Requested mode:"); panel.Controls.Add(desired); panel.Controls.Add(reviewed); panel.Controls.Add(preview);
            var password = new TextBox { UseSystemPasswordChar = true, MaxLength = 1024, AccessibleName = "App password authorizing Windows protection change" }; var factor = new TextBox { UseSystemPasswordChar = true, MaxLength = 256, AccessibleName = "Fresh MFA or recovery factor for protection change" };
            Help(panel, "Current app password, then fresh MFA/recovery factor:"); panel.Controls.Add(password); panel.Controls.Add(factor); ProtectionPlan plan = null;
            rule.SelectedIndexChanged += delegate { plan = null; }; desired.SelectedIndexChanged += delegate { plan = null; };
            panel.Controls.Add(UtilityAsyncButton("Read prerequisites and prepare preview", async () => await EverydayWork(async () => { string id = control == "ASR" ? CoverageCatalog.Rules[rule.SelectedIndex].Id : ""; int mode = desired.SelectedIndex == 0 ? 2 : 1; plan = null; var prepared = await Task.Run(() => ProtectionChanges.Prepare(control, id, mode)); if (dialog.IsDisposed) return; if ((control == "ASR" && CoverageCatalog.Rules[rule.SelectedIndex].Id != id) || (desired.SelectedIndex == 0 ? 2 : 1) != mode) { preview.Text = "Selection changed while prerequisites were being read. Prepare another preview."; return; } plan = prepared; string explanation = control == "ASR" ? CoverageCatalog.Rules.Single(x => x.Id == id).Explanation : "Audit CFA compatibility before blocking writes. Existing protected-folder and application exception lists are preserved."; preview.Text = explanation + "\r\n\r\nControl: " + control + " " + id + "\r\nBefore: " + CoverageCatalog.Mode(plan.Before) + "\r\nRequested: " + CoverageCatalog.Mode(plan.Desired) + "\r\nPreview expires in ten minutes. Administrator: " + Core.Admin + "\r\nRollback reference: previous numeric preference " + plan.Before + ". Changes remain subject to Windows/administrator policy. Review Windows Security or your administrator if compatibility requires recovery; FishTank never automatically disables protection."; })));
            panel.Controls.Add(UtilityButton("Review prior change attempts and outcomes", () => { var history = ProtectedStore.Read("protection-change-history", () => new List<ProtectionChange>()); preview.Text = Core.Json(history); plan = null; }));
            var prepareButton = panel.Controls.OfType<Button>().Single(x => x.Text == "Read prerequisites and prepare preview"); panel.Controls.SetChildIndex(prepareButton, 6);
            panel.Controls.Add(UtilityButton("Export this preview for review", () => { if (plan == null) throw new InvalidOperationException("Prepare a preview first."); using (var save = new SaveFileDialog { Filter = "Text file|*.txt", FileName = "FishTank-protection-preview.txt" }) if (save.ShowDialog(dialog) == DialogResult.OK) RestoreFileWriter.WriteNew(save.FileName, Encoding.UTF8.GetBytes(preview.Text)); }));
            panel.Controls.Add(UtilityAsyncButton("Confirm and apply this one protection change", async () => await EverydayWork(async () => { if (plan == null) throw new InvalidOperationException("Prepare a fresh preview first."); var selected = plan; bool audit = reviewed.Checked; if (MessageBox.Show(dialog, preview.Text + "\r\n\r\nApply only this requested preference now? Blocking can affect legitimate applications. Readback is checked, but protection effectiveness is not attack-tested.", "Confirm Windows protection change", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return; string pwd = password.Text, code = factor.Text; plan = null; try { string result = await Task.Run(() => ProtectionChanges.Apply(selected, pwd, code, audit)); if (!dialog.IsDisposed) preview.Text = result; } finally { if (!dialog.IsDisposed) factor.Clear(); } })));
            dialog.FormClosing += delegate(object sender, FormClosingEventArgs e) { if (everydayWorking) e.Cancel = true; }; try { dialog.ShowDialog(this); } finally { password.Clear(); factor.Clear(); }
        }
    }
}
