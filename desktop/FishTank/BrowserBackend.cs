using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace FishTank;

internal sealed class BrowserBackend : IDisposable
{
    private readonly object gate = new object();
    private readonly Func<string> status;
    private readonly Func<bool> quickScan;
    private PasswordSession vault;
    private DateTime nextUnlock;
    private bool disposed;
    private void RequireOpen() { if (disposed) throw new ObjectDisposedException(nameof(BrowserBackend)); }
    internal BrowserBackend(Func<string> status, Func<bool> quickScan) { this.status = status; this.quickScan = quickScan; }
    internal void Lock() { lock (gate) { vault?.Dispose(); vault = null; } }
    internal void CheckIdle() { lock (gate) if (vault != null && vault.ShouldAutoLock(DateTime.UtcNow)) { vault.Dispose(); vault = null; } }
    internal Task<string> Execute(string path, string body) { lock (gate) RequireOpen(); return Task.Run(() => Run(path, body)); }
    private string Run(string path, string body)
    {
        lock (gate) RequireOpen();
        if (path == "/api/status") { CheckIdle(); return status(); }
        if (path == "/api/check/security") return SecurityReviews.Run("SecurityHealth", false);
        if (path == "/api/check/disk") return DiskHealthChecks.Run();
        if (path == "/api/scan/quick") return Core.Json(new { accepted = quickScan() });
        if (path == "/api/backups") return BackupHistory.Report();
        lock (gate)
        {
            RequireOpen();
            CheckIdle();
            if (path == "/api/vault/state") return Core.Json(new { exists = File.Exists(PasswordSession.DefaultPath), unlocked = vault != null });
            if (path == "/api/vault/lock") { Lock(); return "{}"; }
            if (path == "/api/vault/unlock" || path == "/api/vault/create")
            {
                if (DateTime.UtcNow < nextUnlock) throw new BrowserRequestException(429, "Wait a few seconds before trying another passphrase.");
                if (vault != null) throw new BrowserRequestException(409, "Lock the vault before unlocking or creating it again.");
                nextUnlock = DateTime.UtcNow.AddSeconds(3);
                var command = Core.Parse<UnlockCommand>(body);
                if (command == null || command.Master == null || command.Master.Length > 1024) throw new ArgumentException();
                try
                {
                    if (path == "/api/vault/create" && command.Master != command.Confirm) throw new BrowserRequestException(400, "Master passphrases do not match.");
                    vault = path == "/api/vault/create" ? PasswordSession.Create(PasswordSession.DefaultPath, command.Master) : PasswordSession.Open(PasswordSession.DefaultPath, command.Master);
                    nextUnlock = DateTime.MinValue;
                }
                finally { command.Master = command.Confirm = null; }
                return "{}";
            }
            if (vault == null) throw new BrowserRequestException(423, "Unlock your password vault first.");
            if (path == "/api/vault/list")
            {
                var entries = vault.Snapshot();
                return Core.Json(PasswordOrganization.Ordered(entries).Select(e => new { e.Id, e.Kind, e.Title, e.Email, e.LinkedEmailId, e.UsesLinkedSignIn, e.Username, e.Website, e.UpdatedUtc,
                    ConnectedEmail = PasswordOrganization.EmailFor(e, entries), Review = e.UsesLinkedSignIn ? "Linked sign-in" : e.Password.Length < 14 ? "Short password" : entries.Count(a => a.Password != "" && a.Password == e.Password) > 1 ? "Reused password" : "No length/reuse flag; not a breach check" }));
            }
            if (path == "/api/vault/get")
            {
                var id = Core.Parse<IdCommand>(body)?.Id;
                return Core.Json(vault.Entries.Single(e => e.Id == id));
            }
            if (path == "/api/vault/save") { vault.Put(Core.Parse<PasswordEntry>(body) ?? throw new ArgumentException()); return "{}"; }
            if (path == "/api/vault/delete")
            {
                string id = Core.Parse<IdCommand>(body)?.Id;
                if (vault.Snapshot().Any(e => e.LinkedEmailId == id)) throw new BrowserRequestException(409, "Unlink or move this email account's services before deleting it.");
                vault.Delete(id); return "{}";
            }
            if (path == "/api/vault/generate") return Core.Json(new { Password = PasswordGenerator.Generate(new GeneratorOptions { Length = 24, Lower = true, Upper = true, Digits = true, Symbols = true, SymbolCharacters = "!@#$%^&*()-_=+" }) });
            throw new BrowserRequestException(404, "Unknown dashboard action.");
        }
    }
    public void Dispose() { lock (gate) { disposed = true; vault?.Dispose(); vault = null; } }
    public sealed class UnlockCommand { public string Master { get; set; } public string Confirm { get; set; } }
    public sealed class IdCommand { public string Id { get; set; } }
}
