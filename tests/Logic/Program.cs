using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using FishTank;

int count = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); count++; }
void Reject(Action action, string name) {
    try { action(); } catch (ArgumentException) { count++; return; }
    throw new Exception(name);
}
string path = Path.GetTempFileName();
try {
    File.WriteAllText(path, "abc");
    const string digest = "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD";
    Check(DownloadChecksum.Compute(path) == digest, "SHA-256 known vector");
    Check(DownloadChecksum.Matches(" " + digest.ToLowerInvariant() + "\n", digest), "Publisher paste normalization");
    File.AppendAllText(path, "changed");
    Check(!DownloadChecksum.Matches(digest, DownloadChecksum.Compute(path)), "Altered download must fail");
    File.WriteAllBytes(path, Array.Empty<byte>());
    Check(DownloadChecksum.Compute(path) == "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855", "Empty download vector");
    foreach (var bad in new[] { "", "abc", new string('G', 64), digest + "extra", "file.exe " + digest, digest.Substring(1) })
        Reject(() => DownloadChecksum.Normalize(bad), "Malformed checksum accepted");
    foreach (var page in new[] { "Dashboard", "Antivirus", "Security Center", "Breach Checks", "Password Manager", "Settings" })
        Check(NavigationPolicy.IsVisible(page, false), "Essential page hidden: " + page);
    foreach (var page in new[] { "Real-time monitoring", "Network Security", "System Integrity", "Quarantine", "Tailscale Devices" }) {
        Check(!NavigationPolicy.IsVisible(page, false), "Advanced page exposed: " + page);
        Check(NavigationPolicy.IsVisible(page, true), "Advanced page unreachable: " + page);
    }
    foreach (var bad in new[] { "1.0.0.0", "0.9.0.0", "garbage" })
        Reject(() => UpdateVersionPolicy.RequireNewer(bad, "1.0.0.0", "1.1.0.0"), "Rollback or invalid update accepted");
    Reject(() => UpdateVersionPolicy.RequireNewer("1.0.5.0", "1.0.0.0", "1.1.0.0"), "Below staged floor accepted");
    UpdateVersionPolicy.RequireNewer("1.1.0.0", "1.0.0.0", "1.1.0.0"); count++;
    Check(UpdateUrlPolicy.RequireHttps("https://example.com/releases/latest.json").Host == "example.com", "Direct HTTPS feed");
    foreach (var url in new[] { "http://example.com", "https://user:password@example.com/", "https://example.com:8443/", "https://example.com/?token=x", "https://example.com/#fragment", "file:///tmp/update.exe" })
        Reject(() => UpdateUrlPolicy.RequireHttps(url), "Unsafe release URL accepted");
    using (var source = new MemoryStream(new byte[10]))
    using (var destination = new MemoryStream()) { TransferLimits.CopyBounded(source, destination, 10); Check(destination.Length == 10, "Exact transfer limit"); }
    using (var source = new MemoryStream(new byte[11]))
    using (var destination = new MemoryStream()) {
        bool refused = false; try { TransferLimits.CopyBounded(source, destination, 10); } catch (IOException) { refused = true; }
        Check(refused, "Oversized transfer accepted");
    }
    var before = new EvidenceSnapshot { Category = "test", Items = new List<EvidenceItem> { new EvidenceItem { Name = "A", State = "Observed", Value = "one" } } };
    var after = new EvidenceSnapshot { Category = "test", Items = new List<EvidenceItem> { new EvidenceItem { Name = "A", State = "Observed", Value = "two" }, new EvidenceItem { Name = "B", State = "Observed", Value = "new" } } };
    Check(EvidenceComparison.Compare(before, after).Single(x => x.Name == "A").State == "Changed", "Security change missed");
    Check(EvidenceComparison.Compare(before, after).Single(x => x.Name == "B").State == "Added", "Security addition missed");
    after.Items = new List<EvidenceItem> { new EvidenceItem { Name = "Collector unavailable", State = "Unknown", Value = "No access" } };
    Check(EvidenceComparison.Compare(before, after).Single(x => x.Name == "A").State == "Unknown", "Unavailable collection reported removal");
    after.Items = new List<EvidenceItem>();
    Check(EvidenceComparison.Compare(before, after).Single().State == "Removed", "Known removal missed");
    byte[] secret = Encoding.ASCII.GetBytes("12345678901234567890");
    long[] times = { 59, 1111111109, 1111111111, 1234567890, 2000000000, 20000000000 };
    string[] expectedCodes = { "94287082", "07081804", "14050471", "89005924", "69279037", "65353130" };
    for (int i = 0; i < times.Length; i++) Check(Totp.Code(secret, times[i] / 30, 8) == expectedCodes[i], "RFC 6238 SHA-1 vector " + i);
    string encoded = Totp.Encode(secret);
    var instant = new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc);
    string code = Totp.Code(encoded, instant);
    long accepted = Totp.Match(encoded, code, instant, -1);
    Check(accepted >= 0 && Totp.Match(encoded, code, instant, accepted) == -1, "Authenticator replay accepted");
    Check(Totp.Decode(encoded).SequenceEqual(secret), "Authenticator secret round trip");
    string folder = Path.Combine(Path.GetTempPath(), "FishTank-integrity-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder);
    try {
        File.WriteAllText(Path.Combine(folder, "changed.txt"), "trusted");
        File.WriteAllText(Path.Combine(folder, "missing.txt"), "present");
        Directory.CreateDirectory(Path.Combine(folder, "cache"));
        File.WriteAllText(Path.Combine(folder, "cache", "ignored.txt"), "ignore");
        var baseline = FolderIntegrity.Capture(folder, true, new[] { "cache" });
        Check(baseline.Hashes.Count == 2 && baseline.Gaps.Count == 0, "Excluded folder was inspected");
        File.WriteAllText(Path.Combine(folder, "changed.txt"), "modified");
        File.Delete(Path.Combine(folder, "missing.txt"));
        File.WriteAllText(Path.Combine(folder, "added.txt"), "new");
        var current = FolderIntegrity.Capture(folder, true, new[] { "cache" });
        var changes = FolderIntegrity.Compare(baseline, current);
        Check(changes.Single(x => x.Name == "changed.txt").State == "Changed", "Folder modification missed");
        Check(changes.Single(x => x.Name == "missing.txt").State == "Missing", "Folder removal missed");
        Check(changes.Single(x => x.Name == "added.txt").State == "Added", "Folder addition missed");
        using (var large = new FileStream(Path.Combine(folder, "large.bin"), FileMode.CreateNew)) large.SetLength(FolderIntegrity.MaximumFileBytes + 1);
        var partial = FolderIntegrity.Capture(folder, true, new[] { "cache" });
        Check(partial.Gaps.Count > 0, "Oversized file did not produce unknown coverage");
        Check(FolderIntegrity.Compare(baseline, partial).Single(x => x.Name == "missing.txt").State == "Unknown", "Partial folder scan falsely reported absence");
        Check(!FolderIntegrity.Excluded("cache-other/file", new[] { "cache" }), "Exclusion escaped its boundary");
        foreach (var exclusion in new[] { "../secret", "/absolute", "C:/absolute", "cache/*", "a//b" })
            Reject(() => FolderIntegrity.NormalizeExclusions(new[] { exclusion }), "Unsafe exclusion accepted");
    } finally { Directory.Delete(folder, true); }
    Console.WriteLine($"{count} checks passed.");
} finally { File.Delete(path); }
