using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FishTank;
internal static class Program
{
    static int checks;
    static void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; }
    static void Reject<T>(Action action, string name) where T:Exception { try { action(); } catch(T) { checks++; return; } throw new Exception(name); }
    [STAThread] static int Main()
    {
        string parent = Path.GetFullPath(Path.GetTempPath());
        string root = Path.Combine(parent, "FishTank-resilience-" + Guid.NewGuid().ToString("N"));
        string previous = Core.Data; Directory.CreateDirectory(root); Core.Data = root;
        try
        {
            Check(!AppAccess.Enabled && !ScanScheduleStore.Load().Enabled, "Fresh profile enables access or automation");
            Check(LocalStoreFiles.ReadOptional(Path.Combine(root, "absent"), 10) == null, "Missing file not distinguished");
            SecurityPreferences.Save(new SecurityPreferences { DiskHealthWarnings = false });
            string prefs = Path.Combine(root, "security-preferences.dpapi"); byte[] original = File.ReadAllBytes(prefs); bool fallback = false;
            using (var held = new FileStream(prefs, FileMode.Open, FileAccess.Read, FileShare.None))
                Reject<IOException>(() => ProtectedStore.Read("security-preferences", () => { fallback = true; return new SecurityPreferences(); }), "Busy settings defaulted");
            Check(!fallback, "I/O failure invoked defaults");
            var damaged = (byte[])original.Clone(); damaged[damaged.Length / 2] ^= 1; File.WriteAllBytes(prefs, damaged);
            Reject<CryptographicException>(() => SecurityPreferences.Update(p => p.IdleMinutes = 0), "Corrupt settings overwritten");
            Check(File.ReadAllBytes(prefs).SequenceEqual(damaged), "Failed update changed corrupt original"); File.WriteAllBytes(prefs, original);
            Directory.CreateDirectory(AppAccess.Store); Check(AppAccess.Enabled, "Invalid access record treated as password disabled");
            Reject<UnauthorizedAccessException>(() => AppAccess.Authenticate("fixture", null), "Invalid access record unlocked app");
            Check(!AppAccess.SessionUnlocked, "Unreadable access remained unlocked"); Directory.Delete(AppAccess.Store);
            AppAccess.SetPassword(null, "Fixture-only app password 2026!"); AppAccess.Lock();
            Check(!AppAccess.Authenticate("incorrect fixture", null) && !AppAccess.SessionUnlocked, "Wrong password unlocked profile");
            Check(AppAccess.Authenticate("Fixture-only app password 2026!", null), "Correct password failed"); AppAccess.Lock();
            string vault = Path.Combine(root, "fixture.ftvault"); const string master = "Fixture-only vault master 2026!";
            using (var session = PasswordSession.Create(vault, master))
            {
                session.Put(new PasswordEntry { Title = "Fixture", Password = "Private fixture content 2026!" });
                Check(session.ShouldAutoLock(session.LastActivityUtc.AddSeconds(-1)), "Clock rollback extended unlocked vault lifetime");
                session.ExportEncrypted(Path.Combine(root, "backup.ftvault"));
            }
            byte[] encrypted = File.ReadAllBytes(vault);
            Reject<CryptographicException>(() => PasswordSession.Open(vault, "incorrect fixture"), "Wrong vault master accepted");
            Check(File.ReadAllBytes(vault).SequenceEqual(encrypted), "Wrong master changed vault bytes");
            var corrupt = (byte[])encrypted.Clone(); corrupt[corrupt.Length - 1] ^= 1; File.WriteAllBytes(vault, corrupt);
            Reject<CryptographicException>(() => PasswordSession.Open(vault, master), "Tampered vault accepted"); File.WriteAllBytes(vault, encrypted);
            using (var restored = PasswordSession.RestoreEncrypted(Path.Combine(root, "backup.ftvault"), Path.Combine(root, "restore.ftvault"), master))
                Check(restored.Entries.Single().Title == "Fixture", "Isolated restore lost contents");
            var backend = new BrowserBackend(() => "{}", () => false);
            var gate = typeof(BrowserBackend).GetField("gate", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(backend); Task<string> queued;
            Monitor.Enter(gate);
            try { queued = backend.Execute("/api/vault/create", Core.Json(new { Master = master, Confirm = master })); backend.Dispose(); }
            finally { Monitor.Exit(gate); }
            Reject<ObjectDisposedException>(() => queued.GetAwaiter().GetResult(), "Queued browser action survived disposal");
            Reject<ObjectDisposedException>(() => backend.Execute("/api/status", "{}").GetAwaiter().GetResult(), "Disposed browser backend responds");
            Check(!File.Exists(PasswordSession.DefaultPath), "Disposed backend created a vault");
            var policy = new BrowserSessionPolicy(); var now = DateTime.UtcNow; var pair = policy.Pair(policy.BeginPair(now), now);
            policy.Revoke(); Check(!policy.Check(pair[0], pair[1], now, false), "Revoked browser token retained access");
            string destination = Path.Combine(root, "restored-document.txt"); byte[] payload = Encoding.UTF8.GetBytes("Fixture recovery bytes");
            Reject<IOException>(() => RestoreFileWriter.WriteNew(destination, payload, (stream, data) => { stream.Write(data, 0, 2); throw new IOException("Injected write interruption"); }), "Interrupted restore reported success");
            Check(!File.Exists(destination) && Directory.GetFiles(root, ".fishtank-restore-*.tmp").Length == 0, "Interrupted restore left partial plaintext");
            File.WriteAllText(destination, "Existing user fixture"); Reject<IOException>(() => RestoreFileWriter.WriteNew(destination, payload), "Restore overwrote destination");
            Check(File.ReadAllText(destination) == "Existing user fixture", "Restore changed existing file"); File.Delete(destination);
            RestoreFileWriter.WriteNew(destination, payload); Check(File.ReadAllBytes(destination).SequenceEqual(payload), "Atomic restore changed bytes");
            var unavailable = new ScanOperation("Quick", null, () => throw new FileNotFoundException("Defender unavailable fixture")); unavailable.RunAsync().GetAwaiter().GetResult();
            Check(!unavailable.IsRunning && unavailable.Phase.StartsWith("Failed") && unavailable.Result.Contains("unavailable"), "Missing Defender reported success");
            TestOfflineUpdates();
            var signature = TrustedDownloads.Signature(typeof(Program).Assembly.Location);
            Check(signature.Status == "NotSigned", "Built-in publisher verifier did not inspect the unsigned fixture");
            var health = Core.Parse<System.Collections.Generic.List<EvidenceItem>>(Core.Collect("SecurityHealth"));
            Check(health.Count == 7 && health.Any(x => x.Name == "Defender real-time protection") && health.All(x => new[] { "Unknown", "Review", "No issue observed" }.Contains(x.State)), "Large collector resource did not execute the requested health mode with bounded unknown states");
            var liveReading = Core.Parse<LiveProtectionReading[]>(Core.Collect("LiveProtection"));
            Check(liveReading.Length == 1, "Collector silently fell back to the default inventory mode");
            try {
                var source = new WindowsLiveEventSource();
                using (source.Subscribe(item => { }, message => { })) {
                    var batch = source.Read(new LiveEventCursor(), 10);
                    Check(batch.Cursor != null && batch.Events.Count <= 10 && batch.Events.All(x => x.RecordId > 0 && x.EventUtc != DateTime.MinValue && LiveDetectionPolicy.Kind(x.EventId) != null), "Native Defender log returned invalid source identities");
                    Console.WriteLine("Read-only native Defender subscription/log query exercised; " + batch.Events.Count + " available records observed. This does not establish detection efficacy.");
                }
            } catch (System.Diagnostics.Eventing.Reader.EventLogException) { Console.WriteLine("Native Defender log unavailable on this runner: coverage unknown. Deterministic failure tests run separately."); }
              catch (UnauthorizedAccessException) { Console.WriteLine("Native Defender log access denied: coverage unknown. No Windows access policy changed."); }
              catch (InvalidOperationException ex) { Console.WriteLine("Native Defender log could not be validated: coverage unknown. " + ex.Message); }
            File.WriteAllText(Path.Combine(root, "github-update-result.json"), "{\"Status\":\"Pending\"}"); Check(UpdateRecoveryStatus.Notice() != null, "Interrupted update hidden");
            File.WriteAllText(Path.Combine(root, "github-update-result.json"), "broken result"); Check(UpdateRecoveryStatus.Notice().Contains("could not be read"), "Damaged update result treated as success");
            Console.WriteLine(checks + " isolated-profile resilience checks passed. No live scan, user profile, certificate trust or installation was changed."); return 0;
        }
        catch(Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            AppAccess.Lock(); Core.Data = previous; string full = Path.GetFullPath(root);
            if (full.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(full).StartsWith("FishTank-resilience-", StringComparison.Ordinal)) Directory.Delete(full, true);
        }
    }
    static void TestOfflineUpdates()
    {
        var blocked = new TcpListener(IPAddress.Loopback, 0); blocked.Start(); var old = WebRequest.DefaultWebProxy;
        var server = Task.Run(() => { using (var client = blocked.AcceptTcpClient()) using (var stream = client.GetStream()) { client.ReceiveTimeout = 5000; byte[] input = new byte[4096]; stream.Read(input, 0, input.Length); byte[] response = Encoding.ASCII.GetBytes("HTTP/1.1 503 Service Unavailable\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"); stream.Write(response, 0, response.Length); } });
        try { WebRequest.DefaultWebProxy = new WebProxy("http://127.0.0.1:" + ((IPEndPoint)blocked.LocalEndpoint).Port); Reject<IOException>(() => GitHubUpdates.Check(), "Offline update reported success"); Check(GitHubUpdates.Pending() == null, "Offline update created install plan"); }
        finally { WebRequest.DefaultWebProxy = old; blocked.Stop(); }
        if (!server.Wait(TimeSpan.FromSeconds(5))) throw new Exception("Offline proxy fixture did not finish");
    }
}
