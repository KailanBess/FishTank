using System;
using System.Collections.Generic;
using System.Linq;

namespace FishTank;

public static class SecurityReviews
{
    private static readonly object Gate = new object();
    public static readonly string[] Categories = { "SecurityHealth", "StartupInventory", "Accounts", "Permissions", "Exposure" };
    private static List<EvidenceSnapshot> Read() => ProtectedStore.Read("security-history", () => new List<EvidenceSnapshot>()) ?? throw new InvalidOperationException("Invalid security history.");

    public static EvidenceSnapshot Capture(string category)
    {
        if (!Categories.Contains(category)) throw new ArgumentException("Unknown security review.");
        var items = Core.Parse<List<EvidenceItem>>(Core.Collect(category)) ?? throw new InvalidOperationException("Collector returned no evidence.");
        foreach (var item in items) if ((item.Value ?? "").Contains("PARTIAL:")) item.State = "Unknown";
        items = items.GroupBy(x => x.Name).Select(group => group.Select(x => x.State + "\n" + x.Value).Distinct().Count() == 1
            ? group.First() : new EvidenceItem { Name = group.Key, State = "Unknown", Value = "Conflicting duplicate identities were returned; inspect the underlying Windows inventory." }).ToList();
        return new EvidenceSnapshot { Category = category, CapturedUtc = DateTime.UtcNow, Items = items };
    }
    public static string Run(string category, bool acceptBaseline)
    {
        var snapshot = Capture(category);
        if (acceptBaseline && snapshot.Items.Any(x => x.State == "Unknown")) throw new InvalidOperationException("A trusted startup baseline was not saved because coverage is incomplete. Review unknown checks and permissions first.");
        lock (Gate)
        {
            var history = Read();
            var previous = history.LastOrDefault(x => x.Category == category);
            var output = previous == null ? snapshot.Items : EvidenceComparison.Compare(previous, snapshot);
            history.Add(snapshot);
            history = history.GroupBy(x => x.Category).SelectMany(x => x.Skip(Math.Max(0, x.Count() - 6))).ToList();
            ProtectedStore.Write("security-history", history);
            if (acceptBaseline) ProtectedStore.Write("baseline-" + category, snapshot);
            SecurityPreferences.Update(p => p.LastReviewUtc = DateTime.UtcNow);
            return Core.Json(new { capturedUtc = snapshot.CapturedUtc, note = previous == null ? "First review recorded; not a trusted baseline unless explicitly accepted." : "Compared with the previous recorded review. Unknown checks remain unknown.", findings = output });
        }
    }
    public static string CompareStartup()
    {
        var before = ProtectedStore.Read<EvidenceSnapshot>("baseline-StartupInventory", () => null);
        if (before == null) throw new InvalidOperationException("Record a trusted startup baseline first.");
        var after = Capture("StartupInventory");
        return Core.Json(EvidenceComparison.Compare(before, after));
    }
    public static string History() { lock (Gate) return Core.Json(Read()); }
}
