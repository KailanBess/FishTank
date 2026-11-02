using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace FishTank;

public sealed class FolderSnapshot
{
    public int Version = 1;
    public string Root;
    public bool Recursive;
    public string[] Exclusions = new string[0];
    public DateTime CapturedUtc;
    public Dictionary<string, string> Hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public List<string> Gaps = new List<string>();
}

public static class FolderIntegrity
{
    public const int MaximumFiles = 5000;
    public const long MaximumFileBytes = 64L * 1024 * 1024;
    public const long MaximumTotalBytes = 512L * 1024 * 1024;

    public static string[] NormalizeExclusions(string[] exclusions)
    {
        if (exclusions == null || exclusions.Length > 100) throw new ArgumentException("Use at most 100 relative exclusions.");
        return exclusions.Select(x => (x ?? "").Trim().Replace('\\', '/').TrimEnd('/')).Where(x => x != "").Select(x => {
            if (x.StartsWith("/") || x.Contains(":") || x.Split('/').Any(p => p == ".." || p == "." || p == "") || x.IndexOfAny(new[] { '*', '?', '\0', '\r', '\n' }) >= 0)
                throw new ArgumentException("Use relative file/folder names, without wildcards or .. segments.");
            return x;
        }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public static bool Excluded(string relative, string[] exclusions) => exclusions.Any(x =>
        string.Equals(x, relative, StringComparison.OrdinalIgnoreCase) || relative.StartsWith(x + "/", StringComparison.OrdinalIgnoreCase));

    public static FolderSnapshot Capture(string root, bool recursive, string[] exclusions)
    {
        root = Path.GetFullPath(root);
        if (Environment.OSVersion.Platform == PlatformID.Win32NT) ScanPaths.RefuseReparseAncestors(root);
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked folders are refused.");
        if (!Directory.Exists(root)) throw new IOException("Choose an existing folder.");
        var snapshot = new FolderSnapshot { Root = root, Recursive = recursive, Exclusions = NormalizeExclusions(exclusions), CapturedUtc = DateTime.UtcNow };
        var pending = new Stack<string>(); pending.Push(root);
        long bytes = 0;
        int examined = 0;
        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            try
            {
                foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    if (++examined > 20000) { snapshot.Gaps.Add("Entry limit reached; remaining paths were not inspected."); return snapshot; }
                    string relative = entry.Substring(root.TrimEnd(Path.DirectorySeparatorChar).Length).TrimStart(Path.DirectorySeparatorChar).Replace('\\', '/');
                    if (Excluded(relative, snapshot.Exclusions)) continue;
                    try
                    {
                        var attributes = File.GetAttributes(entry);
                        if ((attributes & FileAttributes.ReparsePoint) != 0) { snapshot.Gaps.Add(relative + ": link/reparse point skipped."); continue; }
                        if ((attributes & FileAttributes.Directory) != 0) { if (recursive) pending.Push(entry); continue; }
                        if (snapshot.Hashes.Count >= MaximumFiles) { snapshot.Gaps.Add("File limit reached; comparison is partial."); return snapshot; }
                        using (var file = new FileStream(entry, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            if (file.Length > MaximumFileBytes || bytes + file.Length > MaximumTotalBytes) { snapshot.Gaps.Add(relative + ": byte limit reached; file not hashed."); continue; }
                            bytes += file.Length;
                            using (var sha = SHA256.Create()) snapshot.Hashes.Add(relative, BitConverter.ToString(sha.ComputeHash(file)).Replace("-", ""));
                        }
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { snapshot.Gaps.Add(relative + ": unreadable (" + ex.GetType().Name + ")."); }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { snapshot.Gaps.Add(directory + ": folder unreadable (" + ex.GetType().Name + ")."); }
        }
        return snapshot;
    }
    public static List<EvidenceItem> Compare(FolderSnapshot before, FolderSnapshot after)
    {
        if (!string.Equals(before.Root, after.Root, StringComparison.OrdinalIgnoreCase) || before.Recursive != after.Recursive
            || !before.Exclusions.SequenceEqual(after.Exclusions, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("Folder scope changed. Accept a separate baseline deliberately.");
        var result = new List<EvidenceItem>();
        foreach (string name in before.Hashes.Keys.Union(after.Hashes.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            before.Hashes.TryGetValue(name, out string a); after.Hashes.TryGetValue(name, out string b);
            result.Add(new EvidenceItem { Name = name, State = a == null ? (before.Gaps.Count == 0 ? "Added" : "Unknown") : b == null ? (after.Gaps.Count == 0 ? "Missing" : "Unknown") : a == b ? "Unchanged" : "Changed",
                Value = "Baseline SHA-256: " + (a ?? "Not recorded") + "\nCurrent SHA-256: " + (b ?? "Not read") + "\nA change can be legitimate; no file was modified or blocked." });
        }
        result.AddRange(after.Gaps.Select(x => new EvidenceItem { Name = "Coverage gap", State = "Unknown", Value = x }));
        return result;
    }
}
