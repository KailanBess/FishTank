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
    foreach (var page in new[] { "Dashboard", "Antivirus", "Real-time monitoring", "Security Center", "Breach Checks", "Protected Files", "Password Manager", "Settings" })
        Check(NavigationPolicy.IsVisible(page, false), "Essential page hidden: " + page);
    foreach (var page in new[] { "Network Security", "System Integrity", "Quarantine", "Tailscale Devices" }) {
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
    string safeRoot = Path.Combine(Path.GetTempPath(), "FishTank-safe-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(safeRoot);
    try {
        const string passphrase = "Test-only protected file passphrase!";
        string container = Path.Combine(safeRoot, "protected.ftsafe");
        byte[] contents = Encoding.UTF8.GetBytes("PRIVATE_CONTENT_TEST_12345");
        using (var input = new MemoryStream(contents))
            FileSafe.Create(container, passphrase, new[] { new FileSafeSource { Name = "private-folder/private-name.txt", Content = input } });
        byte[] encrypted = File.ReadAllBytes(container);
        string encodedBytes = Encoding.UTF8.GetString(encrypted);
        Check(!encodedBytes.Contains("PRIVATE_CONTENT_TEST_12345") && !encodedBytes.Contains("private-name.txt"), "Protected file leaked name or content");
        bool rejected = false; try { using var ignored = FileSafe.Open(container, "incorrect"); } catch (System.Security.Cryptography.CryptographicException) { rejected = true; }
        Check(rejected, "Protected file accepted wrong passphrase");
        string export = Path.Combine(safeRoot, "permitted-export");
        using (var opened = FileSafe.Open(container, passphrase)) {
            Check(opened.Files.Single().Name == "private-folder/private-name.txt", "Protected name did not round trip");
            opened.ExtractAll(export);
            Check(File.ReadAllBytes(Path.Combine(export, "private-folder", "private-name.txt")).SequenceEqual(contents), "Protected bytes did not round trip");
            bool overwrite = false; try { opened.ExtractAll(export); } catch (IOException) { overwrite = true; }
            Check(overwrite, "Protected export overwrote an existing folder");
            opened.Dispose();
            bool locked = false; try { var ignored = opened.Files; } catch (InvalidOperationException) { locked = true; }
            Check(locked, "Protected contents accessible after lock");
        }
        foreach (int position in new[] { 12, 28, 60, encrypted.Length - 1 }) {
            byte[] changed = (byte[])encrypted.Clone(); changed[position] ^= 1;
            string damaged = Path.Combine(safeRoot, "damaged.ftsafe"); File.WriteAllBytes(damaged, changed);
            bool failed = false; try { using var ignored = FileSafe.Open(damaged, passphrase); } catch (System.Security.Cryptography.CryptographicException) { failed = true; }
            Check(failed, "Tampered protected container accepted at " + position);
        }
        foreach (var name in new[] { "../escape", "/absolute", "C:/escape", "a/../escape", "file.txt:stream", "CON.txt", "a//b", "folder\\file", "COM¹.txt", "NUL .txt", "CONOUT$", "bad\u0001name" }) {
            bool failed = false; try { FileSafe.SafeName(name); } catch (InvalidDataException) { failed = true; }
            Check(failed, "Unsafe protected file name accepted: " + name);
        }
        using (var input = new MemoryStream(contents)) {
            bool overwrite = false; try { FileSafe.Create(container, passphrase, new[] { new FileSafeSource { Name = "copy", Content = input } }); } catch (IOException) { overwrite = true; }
            Check(overwrite && File.ReadAllBytes(container).SequenceEqual(encrypted), "Existing protected container replaced");
        }
    } finally { Directory.Delete(safeRoot, true); }
    var release = new GitHubRelease { id = 1, tag_name = "v1.2.3", assets = new[] { new GitHubAsset { id = 2, name = "FishTank.exe", size = 123,
        digest = "sha256:" + new string('A', 64), browser_download_url = "https://github.com/KailanBess/FishTank/releases/download/v1.2.3/FishTank.exe" } } };
    Check(GitHubReleasePolicy.Version(release.tag_name) == "1.2.3.0", "GitHub version normalization failed");
    Check(GitHubReleasePolicy.Asset(release, "FishTank.exe").id == 2, "Valid GitHub asset rejected");
    release.prerelease = true; Reject(() => GitHubReleasePolicy.Asset(release, "FishTank.exe"), "Prerelease auto-update accepted"); release.prerelease = false;
    release.draft = true; Reject(() => GitHubReleasePolicy.Asset(release, "FishTank.exe"), "Draft update accepted"); release.draft = false;
    release.assets[0].digest = null; Reject(() => GitHubReleasePolicy.Asset(release, "FishTank.exe"), "Missing GitHub digest accepted"); release.assets[0].digest = "sha256:" + new string('A', 64);
    release.assets[0].browser_download_url = "https://github.com/other/repo/releases/download/v1.2.3/FishTank.exe";
    Reject(() => GitHubReleasePolicy.Asset(release, "FishTank.exe"), "Wrong GitHub repository accepted");
    var origin = new Uri("https://github.com/KailanBess/FishTank/releases/download/v1.2.3/FishTank.exe");
    Check(GitHubReleasePolicy.Redirect(origin, "https://release-assets.githubusercontent.com/asset?signature=fixture").Scheme == "https", "GitHub signed asset redirect refused");
    foreach (var redirect in new[] { "http://release-assets.githubusercontent.com/asset", "https://evil.example/asset", "https://user:password@release-assets.githubusercontent.com/asset", "https://github.com/other/repo/releases/download/v1.2.3/FishTank.exe", "https://release-assets.githubusercontent.com:8443/asset" })
        Reject(() => GitHubReleasePolicy.Redirect(origin, redirect), "Unsafe GitHub redirect accepted");
    var backupNow = new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);
    Check(BackupAgePolicy.Assess("Vault", DateTime.MinValue, DateTime.MinValue, backupNow, 7).State == "Unknown", "Missing backup date reported fresh");
    Check(BackupAgePolicy.Assess("Vault", DateTime.MinValue, backupNow, backupNow, 7).State == "Unknown", "Verification invented backup creation age");
    Check(BackupAgePolicy.Assess("Vault", backupNow.AddDays(-8), backupNow, backupNow, 7).State == "Review", "Verifying old backup reset its age");
    Check(BackupAgePolicy.Assess("Vault", backupNow.AddDays(-7), backupNow, backupNow, 7).State == "Review", "Backup age threshold boundary missed");
    Check(BackupAgePolicy.Assess("Vault", backupNow.AddDays(-7).AddSeconds(1), DateTime.MinValue, backupNow, 7).State == "No issue observed", "Backup marked overdue before threshold");
    Check(BackupAgePolicy.Assess("Vault", backupNow.AddDays(1), backupNow, backupNow, 7).State == "Unknown", "Future creation date accepted");
    Check(BackupAgePolicy.Assess("Vault", backupNow.AddDays(-1), backupNow.AddDays(1), backupNow, 7).State == "Unknown", "Future verification date accepted");
    Check(BackupAgePolicy.Assess("Vault", DateTime.MinValue.AddHours(6), DateTime.MinValue.AddHours(6), backupNow, 7).State == "Unknown", "Timezone-adjusted empty date treated as a real backup");
    Reject(() => BackupAgePolicy.Assess("Vault", backupNow, backupNow, backupNow, 0), "Zero backup age allowed");
    Reject(() => BackupAgePolicy.Assess("Vault", backupNow, backupNow, backupNow, 366), "Oversized backup age allowed");
    var goodProtection = new LiveProtectionReading { AntivirusEnabled = true, RealTimeProtectionEnabled = true, Mode = "Normal" };
    Check(LiveDetectionPolicy.Coverage(false, true, goodProtection, backupNow, backupNow).StartsWith("Off"), "Optional live monitoring enabled by status alone");
    Check(LiveDetectionPolicy.Coverage(true, false, goodProtection, backupNow, backupNow).Contains("Coverage gap"), "Missing event subscription reported protection");
    Check(LiveDetectionPolicy.Coverage(true, true, null, backupNow, backupNow).Contains("unknown"), "Missing protection data reported on");
    Check(LiveDetectionPolicy.Coverage(true, true, goodProtection, backupNow.AddMinutes(-4), backupNow).Contains("unknown"), "Stale protection reading trusted");
    Check(LiveDetectionPolicy.Coverage(true, true, goodProtection, backupNow.AddSeconds(1), backupNow).Contains("unknown"), "Future protection reading trusted");
    Check(LiveDetectionPolicy.Coverage(true, true, goodProtection, backupNow, backupNow).Contains("not guaranteed"), "Live coverage promised complete detection");
    goodProtection.RealTimeProtectionEnabled = false;
    Check(LiveDetectionPolicy.Coverage(true, true, goodProtection, backupNow, backupNow).Contains("warning"), "Real-time protection off was missed");
    goodProtection.Mode = "Passive Mode";
    Check(LiveDetectionPolicy.Coverage(true, true, goodProtection, backupNow, backupNow).Contains("limited"), "Passive Defender mode reported full coverage");
    goodProtection.Mode = "Normal"; goodProtection.RealTimeProtectionEnabled = null;
    Check(LiveDetectionPolicy.Coverage(true, true, goodProtection, backupNow, backupNow).Contains("unknown"), "Missing real-time status inferred healthy");
    Check(LiveDetectionPolicy.Kind(1116) == "Defender detection", "Defender detection classification wrong");
    Check(LiveDetectionPolicy.Kind(1118) == "Defender action failed", "Failed remediation was missed");
    Check(LiveDetectionPolicy.Kind(5001) == "Protection disabled", "Disabled-protection event missed");
    Check(LiveDetectionPolicy.Kind(9999) == null, "Unknown event invented a threat");
    Check(LiveDetectionPolicy.Summary(1122).Contains("does not block") && LiveDetectionPolicy.Summary(1124).Contains("does not mean access was blocked"), "Audit events were described as blocks");
    Check(LiveDetectionPolicy.Summary(5007).Contains("not proof of intrusion"), "Configuration changes called intrusions");
    var liveBuffer = new LiveDetectionBuffer(3);
    var reported = new LiveDetectionEvent { RecordId = 10, EventId = 1116, RecordedUtc = backupNow, EventUtc = backupNow, Kind = "Defender detection", Summary = "Fixture" };
    Check(liveBuffer.Add(reported) && !liveBuffer.Add(reported), "Duplicate live records were not suppressed");
    liveBuffer.Add(new LiveDetectionEvent { RecordId = 11, EventId = 1117, RecordedUtc = backupNow.AddSeconds(1), EventUtc = backupNow.AddSeconds(1), Kind = "Defender action" });
    liveBuffer.Add(new LiveDetectionEvent { RecordId = 2, EventId = 1116, RecordedUtc = backupNow.AddSeconds(2), EventUtc = backupNow.AddSeconds(2), Kind = "Defender detection" });
    Check(liveBuffer.Snapshot().Count == 3 && !liveBuffer.Snapshot().Any(x => x.Kind == "Coverage gap"), "Out-of-order delivery invented a log reset or history unbounded");
    var retained = liveBuffer.Snapshot(); liveBuffer.Restore(retained);
    Check(liveBuffer.Snapshot().Count == retained.Count, "Restoring history invented extra gaps");
    liveBuffer.Snapshot()[0].Summary = "mutated external copy";
    Check(liveBuffer.Snapshot()[0].Summary != "mutated external copy", "History snapshot exposed mutable internal records");
    string selectedDetails = LiveDetectionDetails.Read("<Event><EventData><Data Name='Threat Name'>FixtureThreat</Data><Data Name='Path'>C:\\Fixture.txt</Data><Data Name='User'>PRIVATE_FIXTURE_USERNAME</Data></EventData></Event>");
    Check(selectedDetails.Contains("FixtureThreat") && selectedDetails.Contains("Fixture.txt") && !selectedDetails.Contains("PRIVATE_FIXTURE_USERNAME"), "Event detail allowlist failed");
    Check(LiveDetectionDetails.Read("<!DOCTYPE Event [<!ENTITY sample SYSTEM 'file:///never-read-fixture'>]><Event><EventData><Data Name='Path'>&sample;</Data></EventData></Event>").Contains("safely parsed"), "External XML entity was allowed");
    Check(LiveDetectionDetails.Read(new string('X', 65537)).Contains("size limit"), "Oversized event XML accepted");
    Check(LiveDetectionDetails.Read("<broken").Contains("safely parsed"), "Malformed event XML was not handled");
    Console.WriteLine($"{count} checks passed.");
} finally { File.Delete(path); }
