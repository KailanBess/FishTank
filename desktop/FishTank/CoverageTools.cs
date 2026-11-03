using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
namespace FishTank;

public sealed class AsrRule
{
    public string Id, Title, Explanation;
    public AsrRule(string id, string title, string explanation) { Id = id; Title = title; Explanation = explanation; }
}
internal static class CoverageCatalog
{
    internal static readonly string[] Areas = { "Ransomware", "ASR", "AppControl", "Hardware", "Software", "Updates", "Persistence", "Network" };
    // Identifiers from Microsoft's ASR reference; labels/explanations are FishTank summaries.
    internal static readonly AsrRule[] Rules = {
        new AsrRule("d4f940ab-401b-4efc-aadc-ad5f3c50688a","Office child processes","Review Office automation and add-ins in audit before preventing Office from starting other processes."),
        new AsrRule("3b576869-a4ec-4529-8536-b80a7769e899","Office-created executables","Review workflows that create executable content from Office."),
        new AsrRule("9e6c4e1f-7d60-472f-ba1a-a39ef669e4b2","Credential-process access","Reduce credential theft opportunities involving LSASS; review compatibility and existing OS protections."),
        new AsrRule("d3e037e1-3eb8-44c8-a917-57927947596d","Script-downloaded executables","Review JavaScript/VBScript workflows that launch downloaded executable content."),
        new AsrRule("56a863a9-875e-4185-98a7-b882c64b5ce5","Vulnerable signed driver abuse","Reduce writes involving vulnerable signed drivers. This is distinct from the OS driver blocklist."),
        new AsrRule("75668c1f-73b5-4cf0-bb93-3ecf5cb7cc84","Office process injection","Review Office integrations before preventing injection into other processes."),
        new AsrRule("be9ba2d9-53ea-4cdc-84e5-9b1eeee46550","Email-delivered executable content","Review executable content launched through email clients or webmail."),
        new AsrRule("e6db77e5-3df2-4cf1-b95a-636979351e5b","WMI subscription persistence","Review legitimate WMI event subscriptions before enforcing this persistence restriction.")
    };
    internal static void ValidateControl(string control, string rule) { if (control != "CFA" && (control != "ASR" || !Rules.Any(x => x.Id == rule))) throw new ArgumentException("Unsupported protection control."); }
    internal static string Mode(int value) => value == 0 ? "Not configured / disabled" : value == 1 ? "Block" : value == 2 ? "Audit (does not block)" : value == 6 ? "Warn" : "Unknown (" + value + ")";
}
internal static class CoverageCollector
{
    internal static string Execute(object request)
    {
        string script;
        using (var resource = typeof(CoverageCollector).Assembly.GetManifestResourceStream("FishTank.Coverage.ps1"))
        using (var reader = new StreamReader(resource ?? throw new IOException("Coverage collector resource missing."))) script = reader.ReadToEnd();
        string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(Core.Json(request)));
        string bootstrap = "$r=New-Object System.IO.StreamReader([Console]::OpenStandardInput(),[Text.Encoding]::UTF8);$s=$r.ReadToEnd();$r.Dispose();& ([scriptblock]::Create($s))";
        string executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell\\v1.0\\powershell.exe");
        var result = Core.RunCancelable(executable, "-NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(bootstrap)), 120000, CancellationToken.None, "$RequestBase64='" + payload + "'\n" + script);
        if (result.ExitCode != 0) throw new IOException("Windows coverage operation failed; no successful state was inferred.\r\n" + result.Error);
        return result.Output;
    }
    internal static EvidenceSnapshot Capture(string area, Func<object,string> execute = null)
    {
        if (!CoverageCatalog.Areas.Contains(area)) throw new ArgumentException("Unsupported security area.");
        var rows = Core.Parse<List<EvidenceItem>>((execute ?? Execute)(new { Mode = area })) ?? throw new InvalidDataException("No coverage data returned.");
        if (rows.Count == 0 || rows.Count > 2000 || rows.Any(x => x == null || string.IsNullOrEmpty(x.Name) || x.Name.Length > 700 || x.Value == null || x.Value.Length > 150000 || !new[] { "Observed", "Unknown", "Review", "No issue observed" }.Contains(x.State))) throw new InvalidDataException("Invalid or oversized security coverage.");
        foreach (var row in rows) if (row.Value.Contains("PARTIAL:")) row.State = "Unknown";
        rows = rows.GroupBy(x => x.Name).Select(g => g.Select(x => x.State + "\n" + x.Value).Distinct().Count() == 1 ? g.First() : new EvidenceItem { Name = g.Key, State = "Unknown", Value = "Conflicting identities returned; coverage is incomplete." }).ToList();
        var snapshot = new EvidenceSnapshot { Category = area, CapturedUtc = DateTime.UtcNow, Items = rows };
        if (Encoding.UTF8.GetByteCount(Core.Json(snapshot)) > 1024 * 1024) throw new IOException("Security evidence exceeds the bounded snapshot limit. Coverage remains incomplete.");
        return snapshot;
    }
}
internal static class CoverageHistory
{
    private static readonly object Gate = new object();
    internal static List<EvidenceSnapshot> Read(string area)
    {
        if (!CoverageCatalog.Areas.Contains(area)) throw new ArgumentException("Unsupported security area.");
        return ProtectedStore.Read("coverage-history-" + area, () => new List<EvidenceSnapshot>()) ?? throw new InvalidDataException("Coverage history unreadable.");
    }
    internal static EvidenceSnapshot Collect(string area)
    {
        var snapshot = CoverageCollector.Capture(area); Save(snapshot); return snapshot;
    }
    internal static void Save(EvidenceSnapshot snapshot)
    {
        lock (Gate) { var rows = Read(snapshot.Category); rows.Add(snapshot); ProtectedStore.Write("coverage-history-" + snapshot.Category, rows.Skip(Math.Max(0, rows.Count - 3)).ToList()); }
    }
}
public sealed class ProtectionState { public int Value; public bool Ready; public string Reason; }
public sealed class ProtectionPlan
{
    public string Id = Guid.NewGuid().ToString("N"), Control, RuleId, Profile;
    public int Before, Desired;
    public DateTime CreatedUtc = DateTime.UtcNow;
}
public sealed class ProtectionChange { public DateTime RecordedUtc; public string Control, RuleId, Result; public int Before, Desired; }
internal static class ProtectionChanges
{
    internal static ProtectionState Inspect(string control, string rule)
    {
        CoverageCatalog.ValidateControl(control, rule);
        return Core.Parse<ProtectionState>(CoverageCollector.Execute(new { Mode = "ControlState", Control = control, RuleId = rule })) ?? throw new IOException("Protection prerequisites unavailable.");
    }
    internal static ProtectionPlan Prepare(string control, string rule, int desired)
    {
        var state = Inspect(control, rule);
        ValidateTransition(state, desired, true);
        var plan = new ProtectionPlan { Control = control, RuleId = rule, Before = state.Value, Desired = desired, Profile = Path.GetFullPath(Core.Data) };
        ProtectedStore.Write("pending-protection-plan", plan); return plan;
    }
    internal static void ValidateTransition(ProtectionState state, int desired, bool auditReviewed)
    {
        if (state == null || !state.Ready) throw new InvalidOperationException(state?.Reason ?? "Prerequisites unknown.");
        if (!new[] { 0, 1, 2, 6 }.Contains(state.Value) || (desired != 1 && desired != 2)) throw new InvalidOperationException("Unsupported protection mode.");
        if (state.Value == desired) throw new InvalidOperationException("This mode is already configured.");
        if ((state.Value == 1 || state.Value == 6) && desired == 2) throw new InvalidOperationException("FishTank does not weaken an enforced/warn control to audit.");
        if (desired == 1 && (state.Value != 2 || !auditReviewed)) throw new InvalidOperationException("Enable audit first and review its compatibility events before blocking.");
    }
    private static void RequirePlan(ProtectionPlan plan)
    {
        var pending = ProtectedStore.Read<ProtectionPlan>("pending-protection-plan", () => null);
        if (plan == null || pending == null || Core.Json(plan) != Core.Json(pending) || plan.Profile != Path.GetFullPath(Core.Data) || DateTime.UtcNow < plan.CreatedUtc || DateTime.UtcNow - plan.CreatedUtc > TimeSpan.FromMinutes(10)) throw new InvalidOperationException("Protection preview expired or changed. Prepare a fresh plan.");
        CoverageCatalog.ValidateControl(plan.Control, plan.RuleId);
    }
    internal static string Apply(ProtectionPlan plan, string password, string factor, bool auditReviewed)
    {
        if (!Core.Admin) throw new UnauthorizedAccessException("Reopen FishTank as administrator to make this Windows protection change. Review-only tools remain available without elevation.");
        if (!AppAccess.Mfa.Configured) throw new InvalidOperationException("Enroll offline MFA and keep recovery codes before changing Windows protection from FishTank.");
        RequirePlan(plan); string result = null;
        AppAccess.ChangeSecurityConfiguration(password, factor, () => {
            RequirePlan(plan);
            var state = Inspect(plan.Control, plan.RuleId);
            if (state.Value != plan.Before) throw new InvalidOperationException("Current mode changed since preview.");
            ValidateTransition(state, plan.Desired, auditReviewed);
            // Consume before the Windows command: interrupted changes are never replayed automatically.
            ProtectedStore.Write<ProtectionPlan>("pending-protection-plan", null);
            Record(plan, "Attempt started; final state has not been verified.");
            try {
                var reply = Core.Parse<Dictionary<string,object>>(CoverageCollector.Execute(new { Mode = "ApplyControl", Control = plan.Control, RuleId = plan.RuleId, Expected = plan.Before, NewValue = plan.Desired, AuditReviewed = auditReviewed }));
                if (reply == null || !reply.ContainsKey("Verified") || !(reply["Verified"] is bool) || !(bool)reply["Verified"] || Convert.ToInt32(reply["After"]) != plan.Desired) throw new IOException("Windows did not verify the requested preference.");
                result = "Windows preference readback verified: " + CoverageCatalog.Mode(plan.Desired) + ". Review compatibility and effective policy; no attack test was performed.";
                Record(plan, result);
            } catch (Exception ex) { Record(plan, "Change failed or state unknown: " + ex.Message); throw; }
        });
        return result;
    }
    private static void Record(ProtectionPlan plan, string result)
    {
        var history = ProtectedStore.Read("protection-change-history", () => new List<ProtectionChange>()) ?? throw new IOException("Protection change history unreadable.");
        history.Add(new ProtectionChange { RecordedUtc = DateTime.UtcNow, Control = plan.Control, RuleId = plan.RuleId, Before = plan.Before, Desired = plan.Desired, Result = result });
        ProtectedStore.Write("protection-change-history", history.Skip(Math.Max(0, history.Count - 100)).ToList());
    }
}
public sealed class CoverageMonitorPreferences
{
    public bool Persistence, Network;
    public int IntervalMinutes = 30;
    public DateTime LastPersistenceAttemptUtc, LastNetworkAttemptUtc, LastPersistenceGapUtc, LastNetworkGapUtc;
    public string LastResult = "Monitoring is off until enabled.";
    internal static CoverageMonitorPreferences Load() { var p = ProtectedStore.Read("coverage-monitor", () => new CoverageMonitorPreferences()) ?? throw new IOException("Coverage monitor settings unreadable."); if (p.IntervalMinutes < 5 || p.IntervalMinutes > 1440) throw new IOException("Invalid monitoring interval."); return p; }
    internal static void Save(CoverageMonitorPreferences p) { if (p.IntervalMinutes < 5 || p.IntervalMinutes > 1440) throw new ArgumentException("Use 5–1440 minutes."); ProtectedStore.Write("coverage-monitor", p); }
}
internal static class CoverageMonitor
{
    internal static List<string> Tick(Func<string,EvidenceSnapshot> capture = null)
    {
        var result = new List<string>();
        foreach (string area in new[] { "Persistence", "Network" })
        {
            var p = CoverageMonitorPreferences.Load(); var now = DateTime.UtcNow;
            bool enabled = area == "Persistence" ? p.Persistence : p.Network;
            var last = area == "Persistence" ? p.LastPersistenceAttemptUtc : p.LastNetworkAttemptUtc;
            if (!enabled || (now >= last && now - last < TimeSpan.FromMinutes(p.IntervalMinutes))) continue;
            if (area == "Persistence") p.LastPersistenceAttemptUtc = now; else p.LastNetworkAttemptUtc = now;
            CoverageMonitorPreferences.Save(p);
            try {
                var before = CoverageHistory.Read(area).LastOrDefault(); var after = (capture ?? (name => CoverageCollector.Capture(name)))(area); CoverageHistory.Save(after);
                var comparison = before == null ? new List<EvidenceItem>() : EvidenceComparison.Compare(before, after);
                int changed = comparison.Count(x => x.State == "Added" || x.State == "Removed" || x.State == "Changed");
                var oldNames = new HashSet<string>(before?.Items.Select(x => x.Name) ?? Enumerable.Empty<string>());
                var uncertainNames = new HashSet<string>(comparison.Where(x => x.State == "Unknown").Select(x => x.Name));
                int newlyObserved = before == null ? 0 : after.Items.Count(x => x.State != "Unknown" && !oldNames.Contains(x.Name) && uncertainNames.Contains(x.Name));
                int gaps = after.Items.Count(x => x.State == "Unknown");
                string message = area + " monitoring: " + (before == null ? "first inventory recorded" : changed + " recorded changes; " + newlyObserved + " newly observed items under partial coverage") + "; " + gaps + " coverage gaps. Newly observed does not prove newly created. Review the saved evidence; changes are not a malware verdict.";
                p = CoverageMonitorPreferences.Load(); p.LastResult = message;
                var gapNotice = area == "Persistence" ? p.LastPersistenceGapUtc : p.LastNetworkGapUtc;
                if (changed > 0 || newlyObserved > 0 || (gaps > 0 && now - gapNotice >= TimeSpan.FromDays(1))) { result.Add(message); if (area == "Persistence") p.LastPersistenceGapUtc = now; else p.LastNetworkGapUtc = now; }
                CoverageMonitorPreferences.Save(p);
            } catch (Exception ex) { string failure = area + " monitoring failed; coverage remains unknown: " + ex.Message; var latest = CoverageMonitorPreferences.Load(); latest.LastResult = failure; CoverageMonitorPreferences.Save(latest); result.Add(failure); }
        }
        return result;
    }
}
