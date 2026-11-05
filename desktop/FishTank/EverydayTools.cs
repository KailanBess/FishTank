using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
namespace FishTank;

public sealed class VaultHealthFinding
{
    public string Id, Title, Finding;
}
internal static class VaultHealth
{
    internal static List<VaultHealthFinding> Assess(IEnumerable<PasswordEntry> source, DateTime nowUtc)
    {
        var entries = source.ToArray();
        var reused = new HashSet<string>(entries.Where(e => !e.UsesLinkedSignIn && e.Password != "").GroupBy(e => e.Password).Where(g => g.Count() > 1).Select(g => g.Key));
        var result = new List<VaultHealthFinding>();
        foreach (var entry in entries)
        {
            var flags = new List<string>();
            if (!entry.UsesLinkedSignIn && entry.Password.Length < 14) flags.Add("Short password (under 14 characters)");
            if (!entry.UsesLinkedSignIn && RepeatedPattern(entry.Password)) flags.Add("Repeated short password pattern");
            if (!entry.UsesLinkedSignIn && reused.Contains(entry.Password)) flags.Add("Reused in this vault");
            if (DateTime.TryParse(entry.ExpiresUtc, out var expiry))
            {
                expiry = expiry.ToUniversalTime();
                if (expiry <= nowUtc) flags.Add("Expired");
                else if (expiry <= nowUtc.AddDays(14)) flags.Add("Expires within 14 days");
            }
            if (flags.Count > 0) result.Add(new VaultHealthFinding { Id = entry.Id, Title = entry.Title, Finding = string.Join("; ", flags) });
        }
        return result;
    }
    private static bool RepeatedPattern(string password)
    {
        if (password.Length < 14) return false;
        for (int size = 1; size <= 8 && size <= password.Length / 2; size++)
            if (password.Length % size == 0 && Enumerable.Range(size, password.Length - size).All(i => password[i] == password[i % size])) return true;
        return false;
    }
}

public sealed class CleanupCandidate
{
    public string Name, Sha256;
    public long Bytes;
    public DateTime ModifiedUtc;
}
internal static class StorageCleanup
{
    private static bool Recognized(string name) => Regex.IsMatch(name ?? "", @"\A(?:(?:github|release)-download-[a-f0-9]{32}\.exe|(?:appearance\.json|[a-z][a-z0-9-]{0,60}\.dpapi)\.[a-f0-9]{32}\.tmp)\z");
    internal static List<CleanupCandidate> Preview(DateTime nowUtc)
    {
        ScanPaths.RefuseReparseAncestors(Core.Data);
        var result = new List<CleanupCandidate>();
        foreach (var file in new DirectoryInfo(Core.Data).GetFiles().Where(f => Recognized(f.Name)))
        {
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0 || file.LastWriteTimeUtc > nowUtc.AddDays(-7) || file.Length > 128L * 1024 * 1024) continue;
            result.Add(new CleanupCandidate { Name = file.Name, Bytes = file.Length, ModifiedUtc = file.LastWriteTimeUtc, Sha256 = DownloadChecksum.Compute(file.FullName) });
        }
        return result.OrderBy(x => x.ModifiedUtc).ToList();
    }
    internal static void Delete(CleanupCandidate item, DateTime nowUtc)
    {
        if (item == null || !Recognized(item.Name)) throw new ArgumentException("Choose a previewed FishTank temporary file.");
        string path = Path.Combine(Core.Data, item.Name);
        ScanPaths.RefuseReparseAncestors(path);
        var file = new FileInfo(path);
        if (file.LastWriteTimeUtc > nowUtc.AddDays(-7) || file.LastWriteTimeUtc != item.ModifiedUtc || file.Length != item.Bytes) throw new IOException("File changed or is too recent. Refresh the preview.");
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var sha = SHA256.Create())
        {
            string actual = BitConverter.ToString(sha.ComputeHash(held)).Replace("-", "");
            if (!DownloadChecksum.Matches(item.Sha256, actual)) throw new IOException("File contents changed. Refresh the preview.");
        }
        File.Delete(path);
    }
}

public sealed class PortableProfileBundle
{
    public int Version = 1;
    public string VaultBase64, VaultSha256;
    public Appearance Appearance;
}
internal static class PortableProfileTransfer
{
    internal const string Scope = "Included: encrypted password vault and appearance without wallpaper paths. Reenroll on the destination: app password, offline MFA/recovery codes, server token/channels/passkeys, reporting key and browser pairing. Schedules, backups, history and integration settings stay in the original profile. Keep the original and test the imported vault with its master passphrase.";
    private static string Digest(byte[] data) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", ""); }
    internal static PortableProfileBundle Capture(Appearance appearance)
    {
        var bytes = LocalStoreFiles.ReadOptional(PasswordSession.DefaultPath, 16 * 1024 * 1024) ?? throw new IOException("Create a password vault before exporting a profile.");
        var copy = appearance.Copy(); copy.WallpaperPath = ""; copy.Validate();
        var bundle = new PortableProfileBundle { VaultBase64 = Convert.ToBase64String(bytes), VaultSha256 = Digest(bytes), Appearance = copy };
        Validate(bundle); return bundle;
    }
    internal static byte[] Validate(PortableProfileBundle bundle)
    {
        if (bundle == null || bundle.Version != 1 || bundle.Appearance == null || bundle.VaultBase64 == null || bundle.VaultBase64.Length > 22369624) throw new InvalidDataException("Unsupported or oversized profile transfer.");
        var bytes = Convert.FromBase64String(bundle.VaultBase64);
        if (bytes.Length < 96 || bytes.Length > 16 * 1024 * 1024 || Encoding.ASCII.GetString(bytes, 0, 8) != "FTPV0001" || !DownloadChecksum.Matches(bundle.VaultSha256, Digest(bytes))) throw new InvalidDataException("Encrypted vault bytes or checksum are invalid.");
        bundle.Appearance.WallpaperPath = ""; bundle.Appearance.Validate(); return bytes;
    }
    internal static PortableProfileBundle Parse(byte[] bytes)
    {
        if (bytes == null || bytes.Length > 24 * 1024 * 1024) throw new InvalidDataException("Profile transfer exceeds 24 MiB.");
        var bundle = Core.Parse<PortableProfileBundle>(Encoding.UTF8.GetString(bytes)); Validate(bundle); return bundle;
    }
    internal static string Import(PortableProfileBundle bundle, string root, string name)
    {
        var bytes = Validate(bundle); string target = ProfilePaths.Resolve(root, name);
        string parent = Path.GetFullPath(Path.GetDirectoryName(target));
        for (string current = parent; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            try { if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Profile imports do not follow links."); }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        Directory.CreateDirectory(parent); ScanPaths.RefuseReparseAncestors(parent);
        if (Directory.Exists(target) || File.Exists(target)) throw new IOException("Use a new profile name; existing profiles are never replaced.");
        string staging = Path.Combine(parent, ".profile-import-" + Guid.NewGuid().ToString("N"));
        if (!Path.GetFullPath(target).StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !Path.GetFullPath(staging).StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Unsafe profile destination.");
        Directory.CreateDirectory(staging);
        try
        {
            AutomaticBackups.RequireSpace(staging, bytes.Length);
            RestoreFileWriter.WriteNew(Path.Combine(staging, "passwords.ftvault"), bytes);
            RestoreFileWriter.WriteNew(Path.Combine(staging, "appearance.json"), Encoding.UTF8.GetBytes(Core.Json(bundle.Appearance)));
            Directory.Move(staging, target); return target;
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                foreach (string file in new[] { "passwords.ftvault", "appearance.json" }) { string own = Path.Combine(staging, file); if (File.Exists(own)) File.Delete(own); }
                if (!Directory.EnumerateFileSystemEntries(staging).Any()) Directory.Delete(staging);
            }
        }
    }
}

public sealed class BackupWatchState { public string Destination, Condition; public DateTime NoticedUtc; }
internal static class BackupDestinationWatch
{
    internal static string Assess(CipherBackupPlan plan, Func<string, bool> exists, Action<string, long> space)
    {
        if (!plan.Enabled) return "Disabled";
        try { if (!exists(plan.Destination)) return "Destination unavailable"; space(plan.Destination, 16 * 1024 * 1024); return "Available"; }
        catch (Exception) { return "Destination space or access could not be checked"; }
    }
    internal static string Tick()
    {
        var plan = AutomaticBackups.Load();
        string condition = Assess(plan, Directory.Exists, AutomaticBackups.RequireSpace);
        if (condition == "Disabled") return null;
        var previous = ProtectedStore.Read("backup-destination-watch", () => new BackupWatchState());
        var now = DateTime.UtcNow;
        bool changed = previous.Destination != plan.Destination || previous.Condition != condition;
        bool remind = condition != "Available" && now - previous.NoticedUtc >= TimeSpan.FromDays(1);
        if (!changed && !remind) return null;
        ProtectedStore.Write("backup-destination-watch", new BackupWatchState { Destination = plan.Destination, Condition = condition, NoticedUtc = now });
        if (condition == "Available") return previous.Condition != null && previous.Condition != "Available" ? "Backup destination is available again. Review the last copy time; no successful backup was inferred." : null;
        return "Backup destination needs attention: " + condition + ". Reconnect it or choose another destination in Automatic encrypted backups. Scheduled copy failures remain in notification history.";
    }
}

public sealed class ReleaseNotesState { public string SeenVersion, AnnouncedVersion; }
internal static class AppReleaseNotes
{
    internal static string Version => typeof(Program).Assembly.GetName().Version.ToString();
    internal static bool IsUnread() => ProtectedStore.Read("release-notes-state", () => new ReleaseNotesState()).SeenVersion != Version;
    internal static void MarkRead() => ProtectedStore.Write("release-notes-state", new ReleaseNotesState { SeenVersion = Version, AnnouncedVersion = Version });
    internal static bool Announce()
    {
        var state = ProtectedStore.Read("release-notes-state", () => new ReleaseNotesState());
        if (state.AnnouncedVersion == Version) return false;
        state.AnnouncedVersion = Version; ProtectedStore.Write("release-notes-state", state); return true;
    }
    internal static string Text => "FishTank " + Version + "\r\n\r\nNew in 0.16.0:\r\n• Complete Aquarium family release packaging and a checksummed Debian server kit.\r\n\r\nNew in 0.15.6:\r\n• Aquarium themes, accents and a one-click family appearance preset.\r\n• Consistent defaults, saved-profile preservation and high-contrast backgrounds.\r\n\r\nNew in 0.15.5:\r\n• Clarified certificate-free preview installation guidance.\r\n\r\nNew in 0.15.4:\r\n• Certificate-free, opt-in GitHub preview updates.\r\n• Automatic preview publishing after all CI checks pass.\r\n• Digest/provenance rechecks, rollback and named-profile restart.\r\n\r\nNew in 0.15.3:\r\n• Forward-facing muzzle bubbles with image-aware positioning.\r\n• Settings → GitHub updates: check, download/verify and apply.\r\n\r\nNew in 0.15.2:\r\n• Repaint-safe, debounced appearance preview and reusable fonts.\r\n• Theme-matched explanation controls and Customize tabs/choices.\r\n• Modal preview, app-icon inheritance and Cancel regression checks.\r\n\r\nNew in 0.15.1:\r\n• Separate Follow Windows and Custom color window-frame options.\r\n• Smaller, fewer mascot bubbles with a wider outward burst.\r\n\r\nNew in 0.15:\r\n• Durable live-event checkpoints and missed-record recovery.\r\n• Verified log health, fresh status reads and race-safe protection warnings.\r\n• Expanded event types, accurate audit severity and burst summaries.\r\n• Source/receipt timestamps, manual recheck and explicit history limits.\r\n\r\nNew in 0.14:\r\n• Ransomware protection and audit-first ASR/CFA previews.\r\n• Application-control and hardware-security reviews.\r\n• Installed software and optional available-update inventory.\r\n• Opt-in persistence and network-exposure comparisons.\r\n• Incident cases, selected evidence and guided recovery.\r\n\r\nNew in 0.13:\r\n• Backup destination failure/reconnection alerts.\r\n• Vault health dashboard for expired, reused and short passwords.\r\n• Side-by-side recorded security changes.\r\n• Preview and selectively remove old FishTank download/temp files.\r\n• Portable encrypted-vault/appearance transfer with reenrollment preview.\r\n• Isolated --demo mode with sample data and simulated actions.\r\n• Searchable in-app release notes and a server connection wizard.\r\n\r\n0.12 introduced Ctrl+K tools/favorites, scan queues, encrypted backups, history/attachments/import/undo, profiles/layouts, optional server passkeys and signed device status.\r\n\r\nOptional automation remains opt-in. Server features require deployment and real enrollment. This development preview is unsigned.";
}

internal static class ServerConnectionAdvice
{
    internal static string Explain(Exception error)
    {
        for (Exception current = error; current != null; current = current.InnerException)
            if (current is WebException web)
            {
                if (web.Status == WebExceptionStatus.NameResolutionFailure) return "The server name could not be resolved. Check the hostname and your VPN/DNS connection.";
                if (web.Status == WebExceptionStatus.TrustFailure || web.Status == WebExceptionStatus.SecureChannelFailure) return "HTTPS certificate or TLS verification failed. Repair the hostname/certificate on the server; certificate checks remain enabled.";
                if (web.Status == WebExceptionStatus.Timeout) return "The server timed out. Check that the service is running and HTTPS forwards to its loopback listener.";
                if (web.Status == WebExceptionStatus.ConnectFailure) return "The server could not be reached. Check connectivity, VPN, service status and HTTPS routing.";
            }
        string message = error.Message ?? "";
        if (message.Contains("HTTP 401")) return "The server rejected the device token. Provision a private token for this device and paste it locally.";
        if (message.Contains("HTTP 404")) return "The FishTank API route was not found. Install the service and configure its routes without replacing another service.";
        if (message.Contains("HTTP 503")) return "The service is unavailable. Check its configuration and status on the server.";
        return message + "\r\nReview the configured origin, token and server setup instructions. No verification was granted.";
    }
}
