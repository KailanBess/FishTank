using System;
using System.IO;
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
    Console.WriteLine($"{count} checks passed.");
} finally { File.Delete(path); }
