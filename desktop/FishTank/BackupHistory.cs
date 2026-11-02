using System;
using System.Collections.Generic;
using System.Linq;

namespace FishTank;

public sealed class BackupObservation
{
    public string Kind;
    public string Path;
    public string Sha256;
    public DateTime CreatedUtc;
    public DateTime VerifiedUtc;
    public DateTime RestoreTestUtc;
}
internal static class BackupHistory
{
    internal const string PasswordVault = "Password vault";
    internal const string ProtectedFiles = "Protected files";
    private static readonly object Gate = new object();
    private static List<BackupObservation> Read()
    {
        var records = ProtectedStore.Read("backup-history", () => new List<BackupObservation>()) ?? throw new InvalidOperationException("Backup tracking data could not be read.");
        foreach (var record in records) { record.CreatedUtc = SecurityPreferences.RecordedUtc(record.CreatedUtc); record.VerifiedUtc = SecurityPreferences.RecordedUtc(record.VerifiedUtc); record.RestoreTestUtc = SecurityPreferences.RecordedUtc(record.RestoreTestUtc); }
        return records;
    }
    private static void Record(string kind, string path, string digest, bool created, bool verified, bool restored)
    {
        if (kind != PasswordVault && kind != ProtectedFiles) throw new ArgumentException("Unknown backup type.");
        digest = DownloadChecksum.Normalize(digest);
        lock (Gate)
        {
            var records = Read();
            var record = records.LastOrDefault(x => x.Kind == kind && x.Path == path && x.Sha256 == digest);
            if (record == null) { record = new BackupObservation { Kind = kind, Path = path, Sha256 = digest }; records.Add(record); }
            if (created) { record.CreatedUtc = DateTime.UtcNow; record.VerifiedUtc = DateTime.MinValue; record.RestoreTestUtc = DateTime.MinValue; }
            if (verified) record.VerifiedUtc = DateTime.UtcNow;
            if (restored) record.RestoreTestUtc = DateTime.UtcNow;
            records = records.GroupBy(x => x.Kind).SelectMany(group => group.OrderByDescending(x => x.CreatedUtc > x.VerifiedUtc ? x.CreatedUtc : x.VerifiedUtc).Take(20)).ToList();
            ProtectedStore.Write("backup-history", records);
            if (kind == PasswordVault && verified) SecurityPreferences.Update(p => p.LastBackupVerifiedUtc = DateTime.UtcNow);
        }
    }
    internal static void Created(string kind, string path, string digest, bool verified = false) => Record(kind, path, digest, true, verified, false);
    internal static void Verified(string kind, string path, string digest, bool restored) => Record(kind, path, digest, false, true, restored);
    internal static List<EvidenceItem> Status()
    {
        lock (Gate)
        {
            var records = Read(); var prefs = SecurityPreferences.Load(); var result = new List<EvidenceItem>();
            foreach (string kind in new[] { PasswordVault, ProtectedFiles })
            {
                var own = records.Where(x => x.Kind == kind).ToArray();
                DateTime created = own.Select(x => x.CreatedUtc).DefaultIfEmpty(DateTime.MinValue).Max();
                DateTime verified = own.Select(x => x.VerifiedUtc).Concat(new[] { kind == PasswordVault ? prefs.LastBackupVerifiedUtc : DateTime.MinValue }).Max();
                result.Add(BackupAgePolicy.Assess(kind, created, verified, DateTime.UtcNow, prefs.BackupAgeDays));
            }
            return result;
        }
    }
    internal static bool HasActivity()
    {
        lock (Gate) return Read().Count > 0 || System.IO.File.Exists(PasswordSession.DefaultPath);
    }
    internal static string Report() => Core.Json(new { scope = "FishTank-created copies and successful verification events only. Other backup applications are not inspected. Dates are recorded locally; availability and freshness of current vault contents are not inferred.", findings = Status() });
    internal static string Summary() => string.Join("\n\n", Status().Select(x => x.Name + " — " + x.State + "\n" + x.Value));
    internal static List<EvidenceItem> Alerts()
    {
        lock (Gate)
        {
            var records = Read();
            bool vault = records.Any(x => x.Kind == PasswordVault) || System.IO.File.Exists(PasswordSession.DefaultPath) || SecurityPreferences.Load().LastBackupVerifiedUtc != DateTime.MinValue;
            bool files = records.Any(x => x.Kind == ProtectedFiles);
            return Status().Where(x => x.State != "No issue observed" && ((vault && x.Name == PasswordVault + " backup age") || (files && x.Name == ProtectedFiles + " backup age"))).ToList();
        }
    }
}
