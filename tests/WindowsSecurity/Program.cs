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
            AppInsightsChecks.Run(Check);
            PolishChecks.Run(Check);
            var prefs = SecurityPreferences.Load();
            Check(prefs.IdleMinutes == 5 && prefs.LockWithWindows, "Safe lock defaults");
            SecurityPreferences.Save(prefs);
            SecurityPreferences.Update(p => p.Reminders = true);
            Check(SecurityPreferences.Load().Reminders, "Encrypted preferences round trip");
            Check(!SecurityPreferences.Load().LiveDetectionEnabled && !SecurityPreferences.Load().LiveDetectionKeepInTray, "Live background monitoring was enabled by default");
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
                BackupHistory.Created(BackupHistory.PasswordVault, backup, vault.ContentSha256);
                Reject<IOException>(() => vault.ExportEncrypted(backup), "Backup overwrite accepted");
            }
            string legacyFile = Path.Combine(temp, "legacy.ftvault");
            LegacyPasswordFixture.Write(legacyFile, pass);
            using (var legacy = PasswordSession.Open(legacyFile, pass)) {
                Check(legacy.Entries.Single().Kind == "Service" && legacy.Entries.Single().LinkedEmailId == "", "Native serializer failed legacy organization defaults");
                legacy.Put(new PasswordEntry { Kind = "Email", Email = "fixture@example.test", Title = "Native email", Password = "Email fixture password!" });
            }
            using (var migrated = PasswordSession.Open(legacyFile, pass)) Check(migrated.Entries.Count == 2, "Native organized vault migration failed");
            using (var webVault = new BrowserBackend(() => "{}", () => false)) {
                webVault.Execute("/api/vault/create", Core.Json(new { Master = pass, Confirm = pass })).GetAwaiter().GetResult();
                var email = new PasswordEntry { Kind = "Email", Email = "native@example.test", Title = "Native browser account", Password = "Native browser fixture password" };
                webVault.Execute("/api/vault/save", Core.Json(email)).GetAwaiter().GetResult();
                string details = webVault.Execute("/api/vault/get", Core.Json(new { Id = email.Id })).GetAwaiter().GetResult();
                Check(Core.Parse<PasswordEntry>(details).Email == email.Email, "Native browser command deserialization failed");
                string metadata = webVault.Execute("/api/vault/list", "{}").GetAwaiter().GetResult();
                Check(!metadata.Contains(email.Password), "Native browser list leaked a password");
            }
            string activeHash = DownloadChecksum.Compute(active);
            Check(BackupHistory.Status().Single(x => x.Name == "Password vault backup age").State == "No issue observed", "Newly created backup date was not recorded");
            var oldRecords = ProtectedStore.Read("backup-history", () => new System.Collections.Generic.List<BackupObservation>());
            oldRecords.Single(x => x.Kind == BackupHistory.PasswordVault && x.Path == backup).CreatedUtc = DateTime.UtcNow.AddDays(-8);
            ProtectedStore.Write("backup-history", oldRecords);
            using (var checkedBackup = PasswordSession.Open(backup, pass)) BackupHistory.Verified(BackupHistory.PasswordVault, backup, checkedBackup.ContentSha256, false);
            Check(BackupHistory.Status().Single(x => x.Name == "Password vault backup age").State == "Review", "Reverification made an old backup look newly created");
            Check(SecurityPreferences.RecordedUtc(DateTime.MinValue.AddHours(6)) == DateTime.MinValue, "Unset date did not survive timezone normalization");
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
            Check(health.Items.All(x => new[] { "Unknown", "Review", "No issue observed" }.Contains(x.State)), "Unsupported health status: " + string.Join(" | ", health.Items.Select(x => "[" + (x.State ?? "null") + "]")));
            var disks = SecurityReviews.Capture("DiskHealth");
            Check(disks.Items.Count > 0 && disks.Items.All(x => new[] { "Unknown", "Review", "No issue observed" }.Contains(x.State)), "Disk collector did not return bounded health/unknown evidence");
            string summary = SecurityToolsDialog.Summarize(Core.Json(new[] { new EvidenceItem { Name = "Fixture", State = "Unknown", Value = "Fixture unavailable" } }));
            Check(summary.Contains("unknown") && summary.Contains("full evidence"), "Simple summary hid unknown status or evidence access");
            using (var live = new LiveDetectionSession(null))
            {
                Check(!live.Enabled && live.Status.StartsWith("Off"), "Live listener started without opt-in");
                live.Start(); Check(live.Enabled, "Live session did not record requested start");
                live.Stop(); Check(!live.Enabled && live.Status.StartsWith("Off"), "Stopping live session did not leave it off");
                Check(live.Events().Any(x => x.Kind == "Session started") && live.Events().Any(x => x.Kind == "Session stopped"), "Live start/stop history missing");
            }
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
