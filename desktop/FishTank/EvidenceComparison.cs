using System;
using System.Collections.Generic;
using System.Linq;

namespace FishTank;

public sealed class EvidenceItem
{
    public string Name;
    public string State;
    public string Value;
}
public sealed class EvidenceSnapshot
{
    public DateTime CapturedUtc;
    public string Category;
    public List<EvidenceItem> Items = new List<EvidenceItem>();
}
public static class EvidenceComparison
{
    public static List<EvidenceItem> Compare(EvidenceSnapshot before, EvidenceSnapshot after)
    {
        if (before == null || after == null || before.Category != after.Category) throw new ArgumentException("Choose snapshots of the same category.");
        var old = before.Items.ToDictionary(x => x.Name, StringComparer.Ordinal);
        var now = after.Items.ToDictionary(x => x.Name, StringComparer.Ordinal);
        var result = new List<EvidenceItem>();
        bool partial = before.Items.Any(x => x.State == "Unknown") || after.Items.Any(x => x.State == "Unknown");
        foreach (string key in old.Keys.Union(now.Keys).OrderBy(x => x, StringComparer.Ordinal))
        {
            old.TryGetValue(key, out var a); now.TryGetValue(key, out var b);
            bool unknown = a?.State == "Unknown" || b?.State == "Unknown" || (partial && (a == null || b == null));
            result.Add(new EvidenceItem { Name = key,
                State = unknown ? "Unknown" : a == null ? "Added" : b == null ? "Removed" : a.State == b.State && a.Value == b.Value ? "Unchanged" : "Changed",
                Value = "Before (" + before.CapturedUtc.ToString("u") + "): " + (a == null ? "Not recorded" : a.State + "\n" + a.Value)
                    + "\nAfter (" + after.CapturedUtc.ToString("u") + "): " + (b == null ? "Not recorded" : b.State + "\n" + b.Value)
                    + "\nChanges require review; an unchanged observation is not proof of safety." });
        }
        return result;
    }
}
