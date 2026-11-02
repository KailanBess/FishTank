using System;
using System.Collections.Generic;
using System.Linq;

namespace FishTank;

internal static class FolderIntegrityStore
{
    private static readonly object Gate = new object();
    internal static FolderSnapshot Baseline() => ProtectedStore.Read<FolderSnapshot>("folder-baseline", () => null);
    internal static string Accept(string folder, bool recursive, string[] excluded)
    {
        var snapshot = FolderIntegrity.Capture(folder, recursive, excluded);
        if (snapshot.Gaps.Count > 0) throw new InvalidOperationException("Baseline was not saved because coverage is incomplete:\n" + string.Join("\n", snapshot.Gaps.Take(20)) + "\nReduce the scope or explicitly exclude paths you do not want to inspect.");
        lock (Gate) ProtectedStore.Write("folder-baseline", snapshot);
        return "Baseline saved for " + snapshot.Hashes.Count + " files at " + snapshot.CapturedUtc.ToString("u") + ". Only trust a baseline captured from files you already trust.";
    }
    internal static string Check()
    {
        lock (Gate)
        {
            var before = Baseline() ?? throw new InvalidOperationException("Choose a folder and accept its baseline first.");
            var after = FolderIntegrity.Capture(before.Root, before.Recursive, before.Exclusions);
            var items = FolderIntegrity.Compare(before, after);
            var history = ProtectedStore.Read("folder-history", () => new List<EvidenceSnapshot>());
            history.Add(new EvidenceSnapshot { Category = "Folder integrity", CapturedUtc = after.CapturedUtc, Items = items });
            ProtectedStore.Write("folder-history", history.Skip(Math.Max(0, history.Count - 5)).ToList());
            SecurityPreferences.Update(p => p.LastFolderCheckUtc = DateTime.UtcNow);
            return Core.Json(new { checkedUtc = after.CapturedUtc, changed = items.Count(x => x.State != "Unchanged"), complete = after.Gaps.Count == 0, findings = items });
        }
    }
    internal static string History() => Core.Json(ProtectedStore.Read("folder-history", () => new List<EvidenceSnapshot>()));
}
