using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;
using FishTank;

internal static class Program
{
    static int checks;
    static void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; }
    static void Refuse<T>(Action action, string name) where T : Exception { try { action(); } catch (T) { checks++; return; } throw new Exception(name); }
    static async Task Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--serve-ui") Core.Data = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1])), "profile");
        Directory.CreateDirectory(Core.Data);
        try
        {
            if (args.Length == 2 && args[0] == "--serve-ui")
            {
                using var uiBackend = new BrowserBackend(() => "{\"liveEnabled\":false,\"liveStatus\":\"Off · optional Defender alerts are disabled.\",\"lastScan\":\"No scan recorded\",\"events\":[]}", () => false);
                using var uiHost = new LocalBrowserServer(() => true, uiBackend.Execute, uiBackend.Lock, 0);
                File.WriteAllText(args[1], uiHost.PairingUrl());
                Console.WriteLine("Loopback UI fixture ready.");
                await Task.Delay(TimeSpan.FromMinutes(10)); return;
            }
            var now = new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);
            var policy = new BrowserSessionPolicy();
            string pair = policy.BeginPair(now);
            Check(policy.Pair(new string('0',64), now) == null, "Guessed pairing accepted");
            var first = policy.Pair(pair, now); Check(first != null, "Valid pairing failed");
            Check(policy.Pair(pair, now) == null, "Pairing replay accepted");
            Check(!policy.Check(first[0], new string('0',64), now, true), "Wrong CSRF accepted");
            Check(policy.Check(first[0], first[1], now.AddMinutes(4), false), "Session prematurely expired");
            Check(!policy.Check(first[0], first[1], now.AddMinutes(5), false), "Polling extended idle timeout");
            var second = policy.Pair(policy.BeginPair(now), now);
            Check(!policy.Check(first[0], first[1], now, false), "Old session survived new pairing");
            Check(policy.Check(second[0], second[1], now.AddMinutes(4), true), "User action rejected");
            Check(policy.Check(second[0], second[1], now.AddMinutes(8), false), "User action did not extend session");
            Check(policy.Expire(now.AddMinutes(9)), "Idle expiry did not revoke credentials");
            Check(policy.Pair(policy.BeginPair(now), now.AddMinutes(1)) == null, "Expired link accepted");
            pair = policy.BeginPair(now); for(int i=0;i<20;i++) policy.Pair("wrong", now);
            Check(policy.Pair(pair, now) == null, "Pairing guesses not rate limited");
            var absolutePolicy = new BrowserSessionPolicy(); var absolute = absolutePolicy.Pair(absolutePolicy.BeginPair(now), now);
            for (int i=1;i<120;i++) Check(absolutePolicy.Check(absolute[0], absolute[1], now.AddMinutes(i*4), true), "Active session ended early");
            Check(!absolutePolicy.Check(absolute[0], absolute[1], now.AddHours(8), true), "Absolute expiry missing");
            var rollback = new BrowserSessionPolicy(); var rollbackSession = rollback.Pair(rollback.BeginPair(now), now);
            Check(!rollback.Check(rollbackSession[0], rollbackSession[1], now.AddSeconds(-1), false), "Clock rollback extended session");

            const string migrationPass = "Legacy fixture master passphrase!";
            string legacyPath = Path.Combine(Core.Data, "legacy.ftvault");
            LegacyPasswordFixture.Write(legacyPath, migrationPass);
            using (var legacy = PasswordSession.Open(legacyPath, migrationPass)) {
                var old = legacy.Entries.Single();
                Check(old.Kind == "Service" && old.LinkedEmailId == "" && old.Email == "" && old.Notes == "Original notes", "Original vault did not migrate with intact fields");
                legacy.Put(new PasswordEntry { Kind = "Email", Email = "new@example.test", Title = "New account", Password = "New fixture password" });
            }
            using (var migrated = PasswordSession.Open(legacyPath, migrationPass)) Check(migrated.Entries.Count == 2 && migrated.Entries.Single(e => e.Title == "Legacy standalone").Password == "Legacy fixture password!", "Migration lost old credentials");
            using (var original = PasswordSession.Open(legacyPath + ".bak", migrationPass)) Check(original.Entries.Count == 1, "Original encrypted migration backup was not preserved");
            bool allowed = true; int calls = 0, locks = 0;
            using var backend = new BrowserBackend(() => "{\"liveEnabled\":false,\"events\":[]}", () => true);
            using var host = new LocalBrowserServer(() => allowed, async (path, body) => { calls++; return await backend.Execute(path, body); }, () => { locks++; backend.Lock(); }, 0);
            using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(host.Origin), Timeout = TimeSpan.FromSeconds(20) };
            string bearer = "", csrf = "";
            async Task<HttpResponseMessage> Send(string path, string body = "{}", string origin = null, string nonce = null, string auth = null, string type = "application/json")
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, path);
                request.Headers.TryAddWithoutValidation("Origin", origin ?? host.Origin);
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + (auth ?? bearer));
                request.Headers.TryAddWithoutValidation("X-FishTank-CSRF", nonce ?? csrf);
                request.Content = new StringContent(body, Encoding.UTF8, type);
                return await client.SendAsync(request);
            }
            async Task<string> Success(string path, object body = null) { using var result = await Send(path, Core.Json(body ?? new {})); var text = await result.Content.ReadAsStringAsync(); Check(result.StatusCode == HttpStatusCode.OK, path + " failed: " + text); return text; }
            async Task Pair()
            {
                string url = host.PairingUrl(); string token = url.Split(new[] { "#pair=" }, StringSplitOptions.None)[1];
                using var response = await Send("/api/pair", Core.Json(token)); Check(response.IsSuccessStatusCode, "HTTP pairing failed");
                var value = Core.Parse<System.Collections.Generic.Dictionary<string,string>>(await response.Content.ReadAsStringAsync()); bearer = value["session"]; csrf = value["csrf"];
                using var replay = await Send("/api/pair", Core.Json(token)); Check(replay.StatusCode == HttpStatusCode.Unauthorized, "HTTP pairing replay accepted");
            }
            using (var page = await client.GetAsync("/")) {
                var html = await page.Content.ReadAsStringAsync(); Check(page.IsSuccessStatusCode && html.Contains("Email connection"), "Browser page missing");
                Check(page.Headers.CacheControl.NoStore && page.Headers.GetValues("Content-Security-Policy").Single().Contains("frame-ancestors 'none'"), "Sensitive page cache/framing policy missing");
                Check(!page.Headers.Contains("Access-Control-Allow-Origin"), "Unexpected CORS");
            }
            using (var noAuth = await Send("/api/status")) Check(noAuth.StatusCode == HttpStatusCode.Unauthorized && calls == 0, "Unpaired backend access");
            await Pair();
            using (var crossSite = await Send("/api/status", origin: "https://evil.example")) Check(crossSite.StatusCode == HttpStatusCode.Forbidden && calls == 0, "Cross-origin API access");
            using (var form = await Send("/api/status", type: "text/plain")) Check(form.StatusCode == HttpStatusCode.Forbidden, "Simple cross-site request accepted");
            using (var query = await Send("/api/status?secret=value")) Check(query.StatusCode == HttpStatusCode.Forbidden, "API query accepted");
            using (var get = await client.GetAsync("/api/status")) Check(get.StatusCode == HttpStatusCode.Forbidden, "State endpoint allowed GET");
            using (var over = await Send("/api/status", new string('x', 131073))) Check(over.StatusCode == HttpStatusCode.RequestEntityTooLarge, "Oversized request accepted");
            await Success("/api/status");
            using (var wrongCsrf = await Send("/api/vault/state", nonce: new string('0',64))) Check(wrongCsrf.StatusCode == HttpStatusCode.Unauthorized, "Missing CSRF accepted");
            await Pair();
            using (var locked = await Send("/api/vault/list")) Check((int)locked.StatusCode == 423, "Locked vault exposed metadata");
            const string master = "Browser fixture master passphrase!";
            await Success("/api/vault/create", new { Master = master, Confirm = master });
            var email = new PasswordEntry { Kind = "Email", Title = "Personal email", Email = "fixture@example.test", Username = "mail-user", Password = "Email-secret-fixture-456!" };
            var ea = new PasswordEntry { Title = "EA <script>alert(1)</script>", Username = "DistinctEAUser", Password = "Service-secret-fixture-789!", LinkedEmailId = email.Id };
            await Success("/api/vault/save", email); await Success("/api/vault/save", ea);
            string list = await Success("/api/vault/list");
            Check(list.Contains("DistinctEAUser") && list.Contains("fixture@example.test"), "Email-service connection lost");
            Check(!list.Contains("Email-secret") && !list.Contains("Service-secret") && !list.Contains("Notes"), "List leaked secret fields");
            string single = await Success("/api/vault/get", new { Id = ea.Id });
            Check(Core.Parse<PasswordEntry>(single).Password == ea.Password, "Explicit credential retrieval failed");
            var linked = new PasswordEntry { Title = "Federated service", Password = "", UsesLinkedSignIn = true, LinkedEmailId = email.Id };
            await Success("/api/vault/save", linked);
            using (var parentDelete = await Send("/api/vault/delete", Core.Json(new { Id = email.Id }))) Check(parentDelete.StatusCode == HttpStatusCode.Conflict, "Parent deletion orphaned services");
            var wrongLink = new PasswordEntry { Title = "Wrong parent", Password = "fixture", LinkedEmailId = ea.Id };
            using (var rejected = await Send("/api/vault/save", Core.Json(wrongLink))) Check(rejected.StatusCode == HttpStatusCode.BadRequest, "Non-email parent accepted");
            var bytes = File.ReadAllBytes(PasswordSession.DefaultPath);
            Check(!Encoding.UTF8.GetString(bytes).Contains(email.Email) && !Encoding.UTF8.GetString(bytes).Contains(ea.Password), "Vault stored plaintext");
            await Success("/api/vault/lock");
            using (var hidden = await Send("/api/vault/get", Core.Json(new { Id = ea.Id }))) Check((int)hidden.StatusCode == 423, "Locked vault exposed password");
            using (var wrong = await Send("/api/vault/unlock", Core.Json(new { Master = "Incorrect test-only master" }))) Check(wrong.StatusCode == HttpStatusCode.BadRequest, "Wrong vault master accepted");
            using (var throttled = await Send("/api/vault/unlock", Core.Json(new { Master = "Incorrect test-only master" }))) Check((int)throttled.StatusCode == 429, "Failed unlock guesses not throttled");
            await Task.Delay(3100);
            await Success("/api/vault/unlock", new { Master = master });
            var after = Core.Parse<PasswordEntry>(await Success("/api/vault/get", new { Id = ea.Id }));
            Check(after.LinkedEmailId == email.Id && after.Username == "DistinctEAUser", "Encrypted organized fields did not round-trip");
            using (var desktop = PasswordSession.Open(PasswordSession.DefaultPath, master)) desktop.Put(new PasswordEntry { Title = "Other window", Password = "Concurrent fixture" });
            using (var stale = await Send("/api/vault/save", Core.Json(ea))) Check(stale.StatusCode == HttpStatusCode.BadRequest, "Stale browser overwrote desktop edit");
            await Success("/api/lock");
            using (var revoked = await Send("/api/status")) Check(revoked.StatusCode == HttpStatusCode.Unauthorized, "Explicit lock left browser access");
            await Pair(); allowed = false;
            using (var appLock = await Send("/api/status")) Check(appLock.StatusCode == HttpStatusCode.Unauthorized, "Desktop lock bypass");
            allowed = true;
            using (var old = await Send("/api/status")) Check(old.StatusCode == HttpStatusCode.Unauthorized, "App unlock revived old browser session");
            Check(locks > 0, "Revoke never cleared backend vault");
            using (var direct = PasswordSession.Open(PasswordSession.DefaultPath, master)) {
                Refuse<InvalidOperationException>(() => direct.Delete(email.Id), "Native deletion orphaned services");
                email.Kind = "Service";
                Refuse<InvalidDataException>(() => direct.Put(email), "Parent conversion orphaned services");
                var ordered = PasswordOrganization.Ordered(direct.Entries).ToList();
                Check(ordered[0].Kind == "Email" && ordered[1].LinkedEmailId == ordered[0].Id, "Email grouping order failed");
            }
            // A revocation while a slow authorized operation runs must suppress its response.
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var finish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (var slow = new LocalBrowserServer(() => true, async (p, b) => { entered.SetResult(true); await finish.Task; return "{\"secret\":\"must-not-return\"}"; }, () => {}, 0))
            using (var slowClient = new HttpClient(new HttpClientHandler { UseProxy = false }))
            {
                async Task<HttpResponseMessage> SlowRequest(string p, string b, string auth = "", string nonce = "") {
                    using var request = new HttpRequestMessage(HttpMethod.Post, slow.Origin + p);
                    request.Headers.Add("Origin", slow.Origin); request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + auth); request.Headers.Add("X-FishTank-CSRF", nonce);
                    request.Content = new StringContent(b, Encoding.UTF8, "application/json"); return await slowClient.SendAsync(request);
                }
                using var pairing = await SlowRequest("/api/pair", Core.Json(slow.PairingUrl().Split(new[] { "#pair=" }, StringSplitOptions.None)[1]));
                var credentials = Core.Parse<System.Collections.Generic.Dictionary<string,string>>(await pairing.Content.ReadAsStringAsync());
                var pending = SlowRequest("/api/status", "{}", credentials["session"], credentials["csrf"]);
                await entered.Task; slow.Revoke(); finish.SetResult(true);
                using var revokedResponse = await pending;
                Check(revokedResponse.StatusCode == HttpStatusCode.Unauthorized && !(await revokedResponse.Content.ReadAsStringAsync()).Contains("must-not-return"), "In-flight result escaped revocation");
            }
            Console.WriteLine(checks + " browser/session/vault checks passed. Collectors use fixtures; Windows UI is not executed.");
        }
        finally { Directory.Delete(Core.Data, true); }
    }
}
