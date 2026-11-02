using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using FishTank;

internal static class Program
{
    static int checks;
    static void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; }
    static void Reject<T>(Action action, string name) where T : Exception
    { try { action(); } catch (T) { checks++; return; } throw new Exception(name); }

    static int Main()
    {
        string temp = Path.Combine(Path.GetTempPath(), "FishTank-security-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp); Core.Data = temp; Core.Init();
        try
        {
            var prefs = SecurityPreferences.Load();
            Check(prefs.IdleMinutes == 5 && prefs.LockWithWindows, "Safe lock defaults");
            SecurityPreferences.Save(prefs);
            SecurityPreferences.Update(p => p.Reminders = true);
            Check(SecurityPreferences.Load().Reminders, "Encrypted preferences round trip");
            var stored = File.ReadAllBytes(Path.Combine(temp, "security-preferences.dpapi"));
            Check(!System.Text.Encoding.UTF8.GetString(stored).Contains("PublisherCertificateSha256"), "Preferences stored as plaintext");
            byte[] corrupt = (byte[])stored.Clone(); corrupt[corrupt.Length / 2] ^= 1;
            File.WriteAllBytes(Path.Combine(temp, "security-preferences.dpapi"), corrupt);
            Reject<CryptographicException>(() => SecurityPreferences.Load(), "Corrupt preferences silently accepted");
            File.WriteAllBytes(Path.Combine(temp, "security-preferences.dpapi"), stored);

            const string pass = "Test-only vault master passphrase 2026!";
            string active = Path.Combine(temp, "active.ftvault"), backup = Path.Combine(temp, "backup.ftvault");
            using (var vault = PasswordSession.Create(active, pass))
            {
                vault.Put(new PasswordEntry { Title = "Fixture", Password = "Test-only entry value!" });
                vault.ExportEncrypted(backup);
                Reject<IOException>(() => vault.ExportEncrypted(backup), "Backup overwrite accepted");
            }
            string activeHash = DownloadChecksum.Compute(active);
            using (var verified = PasswordSession.Open(backup, pass)) Check(verified.Entries.Count == 1, "Backup contents invalid");
            Reject<CryptographicException>(() => PasswordSession.Open(backup, "incorrect passphrase"), "Wrong backup passphrase accepted");
            string isolated = Path.Combine(temp, "isolated.ftvault");
            using (var restored = PasswordSession.RestoreEncrypted(backup, isolated, pass)) Check(restored.Entries.Single().Title == "Fixture", "Isolated restore failed");
            Check(DownloadChecksum.Compute(active) == activeHash, "Restore drill changed active vault");
            byte[] tampered = File.ReadAllBytes(backup); tampered[60] ^= 1;
            string damaged = Path.Combine(temp, "damaged.ftvault"); File.WriteAllBytes(damaged, tampered);
            Reject<CryptographicException>(() => PasswordSession.Open(damaged, pass), "Tampered backup accepted");
            using (var first = PasswordSession.Open(active, pass))
            using (var second = PasswordSession.Open(active, pass))
            {
                first.Put(new PasswordEntry { Title = "First writer", Password = "Test-only write!" });
                Reject<IOException>(() => second.Put(new PasswordEntry { Title = "Stale writer", Password = "Test-only write!" }), "Concurrent vault change overwritten");
                Check(first.ShouldAutoLock(DateTime.UtcNow.AddMinutes(6)), "Vault inactivity locking failed");
            }

            AppAccess.SetPassword(null, pass);
            Check(!AppAccess.Verify("wrong password"), "Wrong app password accepted");
            Check(AppAccess.Verify(pass), "Correct app password rejected");
            string secret = Totp.NewSecret(); var time = DateTime.UtcNow.AddMinutes(-10);
            string[] recovery = AppAccess.EnrollMfa(pass, null, secret, Totp.Code(secret, time), time);
            Check(recovery.Length == 10 && AppAccess.Mfa.AskOnSignIn, "MFA enrollment failed");
            time = time.AddSeconds(30); string code = Totp.Code(secret, time);
            Check(AppAccess.AuthenticateAt(pass, code, time, true), "Authenticator sign-in failed");
            Check(!AppAccess.AuthenticateAt(pass, code, time, true), "Authenticator replay accepted");
            Check(AppAccess.AuthenticateAt(pass, recovery[0], time.AddSeconds(30), true), "Recovery sign-in failed");
            Check(!AppAccess.AuthenticateAt(pass, recovery[0], time.AddSeconds(30), true), "Recovery replay accepted");
            AppAccess.SetMfaPrompt(pass, recovery[1], false, DateTime.UtcNow);
            Check(AppAccess.Authenticate(pass, null), "Optional sign-in MFA setting ignored");
            Reject<UnauthorizedAccessException>(() => AppAccess.ChangeSecurityConfiguration(pass, null, () => SecurityPreferences.Update(p => p.IdleMinutes = 0)), "Sensitive lock change bypassed enrolled MFA");
            Check(SecurityPreferences.Load().IdleMinutes == 5, "Unauthorized lock preference changed");
            AppAccess.ChangeSecurityConfiguration(pass, recovery[2], () => SecurityPreferences.Update(p => p.IdleMinutes = 10));
            Check(SecurityPreferences.Load().IdleMinutes == 10, "Authenticated security preference failed");
            AppAccess.Lock(); Check(!AppAccess.SessionUnlocked, "App lock left session unlocked");

            var signature = TrustedDownloads.Signature(typeof(Program).Assembly.Location);
            Check(signature.Status != "Valid", "Unsigned fixture trusted as publisher");
            Reject<InvalidOperationException>(() => TrustedDownloads.StageUpdate(typeof(Program).Assembly.Location, DownloadChecksum.Compute(typeof(Program).Assembly.Location)), "Update staged without publisher pin");
            Reject<ArgumentException>(() => VerifiedUpdateFeed.Read(), "Unconfigured release feed used network");
            var health = SecurityReviews.Capture("SecurityHealth");
            Check(health.Items.Count >= 7 && health.Items.Any(x => x.Name == "Secure Boot"), "Windows health collector did not execute");
            Check(health.Items.All(x => new[] { "Unknown", "Review", "No issue observed" }.Contains(x.State)), "Unsupported health status");
            string summary = SecurityToolsDialog.Summarize(Core.Json(new[] { new EvidenceItem { Name = "Fixture", State = "Unknown", Value = "Fixture unavailable" } }));
            Check(summary.Contains("unknown") && summary.Contains("full evidence"), "Simple summary hid unknown status or evidence access");
            string personal = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "FishTank-safe-fixture-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(personal);
            try
            {
                string document = Path.Combine(personal, "fixture-private.txt"), container = Path.Combine(personal, "fixture.ftsafe");
                File.WriteAllText(document, "Fixture plaintext for verified move only");
                ProtectedFileService.Create(new[] { document }, null, container, pass, true);
                Check(!File.Exists(document), "Verified source move left original plaintext");
                Check(File.Exists(container) && File.Exists(container + ".recovery.ftsafe"), "Verified move lacks two encrypted copies");
                using (var recoveryCopy = ProtectedFileService.Open(container + ".recovery.ftsafe", pass)) Check(recoveryCopy.Files.Count == 1, "Recovery container could not be verified");
                using (var permitted = ProtectedFileService.Open(container, pass)) ProtectedFileService.Export(permitted, personal);
                string exported = Directory.GetDirectories(personal, "FishTank-restored-*").Single();
                Check(File.ReadAllText(Path.Combine(exported, "fixture-private.txt")) == "Fixture plaintext for verified move only", "Permitted plaintext export was corrupted");
                Check(NamedStreams.Read(Path.Combine(exported, "fixture-private.txt"), "Zone.Identifier").Contains("ZoneId=3"), "Export lost Windows internet-origin warnings");
                Check(Directory.GetAccessControl(exported).AreAccessRulesProtected, "Plaintext export inherited broad parent access");
                File.WriteAllText(document, "Still readable fixture");
                string blocked = Path.Combine(personal, "blocked.ftsafe"); File.WriteAllText(blocked + ".recovery.ftsafe", "existing user file fixture");
                Reject<IOException>(() => ProtectedFileService.Create(new[] { document }, null, blocked, pass, true), "Existing recovery copy was overwritten");
                Check(File.ReadAllText(document) == "Still readable fixture", "Failed protection deleted original plaintext");
                NamedStreams.Write(document, "fixture-metadata", "Preserve this alternate stream");
                Reject<IOException>(() => ProtectedFileService.Create(new[] { document }, null, Path.Combine(personal, "metadata.ftsafe"), pass, true), "Move silently discarded alternate stream data");
                Check(NamedStreams.Read(document, "fixture-metadata") == "Preserve this alternate stream", "Refused move damaged alternate streams");
                Reject<IOException>(() => new ProtectedSource(Path.Combine(Core.Base, "WindowsSecurity.exe"), true), "Application executable was movable into protected storage");
            }
            finally { Directory.Delete(personal, true); }
            Console.WriteLine(checks + " Windows security checks passed.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { Directory.Delete(temp, true); }
    }
}
