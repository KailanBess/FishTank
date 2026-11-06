using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
namespace FishTank;
public sealed class IncidentCase
{
    public string Id = Guid.NewGuid().ToString("N"), Title, Notes = "";
    public string SourceEventKey;
    public DateTime CreatedUtc = DateTime.UtcNow;
    public bool Closed;
    public List<IncidentEntry> Timeline = new List<IncidentEntry>();
    public List<IncidentEvidence> Evidence = new List<IncidentEvidence>();
}
public sealed class IncidentEntry { public DateTime RecordedUtc = DateTime.UtcNow; public string Action, Detail; }
public sealed class IncidentEvidence { public DateTime PreservedUtc = DateTime.UtcNow; public string Id = Guid.NewGuid().ToString("N"), Label, Sha256, Json; }
internal static class IncidentWorkspace
{
    private static readonly object Gate = new object();
    internal static string[] Ids() => ProtectedStore.Read("incident-case-index", () => new string[0]) ?? throw new IOException("Incident index unreadable.");
    private static string Key(string id) { if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("Invalid incident identity."); return "incident-case-" + id; }
    internal static IncidentCase Load(string id) { var item = ProtectedStore.Read<IncidentCase>(Key(id), () => null) ?? throw new IOException("Incident case missing or unreadable."); Validate(item); if (item.Id != id) throw new IOException("Case identity mismatch."); return item; }
    private static void Validate(IncidentCase item)
    {
        Key(item.Id);
        if (string.IsNullOrWhiteSpace(item.Title) || item.Title.Length > 120 || item.Notes == null || item.Notes.Length > 10000 || item.Timeline == null || item.Timeline.Count > 200 || item.Evidence == null || item.Evidence.Count > 10) throw new IOException("Incident case exceeds its limits.");
        if (Encoding.UTF8.GetByteCount(Core.Json(item)) > 3 * 1024 * 1024) throw new IOException("Case exceeds its 3 MiB limit.");
    }
    internal static IncidentCase Create(string title)
    {
        lock (Gate) { var ids = Ids(); if (ids.Length >= 20) throw new InvalidOperationException("At most 20 local cases are supported. Review or reopen an existing case instead."); var item = new IncidentCase { Title = title }; Validate(item); item.Timeline.Add(new IncidentEntry { Action = "Case opened", Detail = "Local operator investigation; no threat or containment success is inferred." }); ProtectedStore.Write(Key(item.Id), item); ProtectedStore.Write("incident-case-index", ids.Concat(new[] { item.Id }).ToArray()); return item; }
    }
    internal static IncidentCase CreateFromDetection(LiveDetectionEvent item)
    {
        lock (Gate)
        {
            if (item.RecordId <= 0 || item.EventUtc == DateTime.MinValue)
                throw new ArgumentException("A verified Defender event identity is required.", nameof(item));

            var ids = Ids();
            string reference = DetectionReference(item);
            string sourceKey = FormattableString.Invariant($"Defender:{item.EventId}:{item.RecordId}:{item.EventUtc.Ticks}");
            foreach (var id in ids)
            {
                var existing = Load(id);
                if (existing.SourceEventKey == sourceKey ||
                    (existing.SourceEventKey == null && existing.Notes == reference))
                    return existing;
            }
            if (ids.Length >= 20) throw new InvalidOperationException("The local incident limit is reached.");
            var incident = new IncidentCase { Title = item.Kind, Notes = reference, SourceEventKey = sourceKey };
            incident.Timeline.Add(new IncidentEntry
            {
                Action = "Defender alert recorded",
                Detail = "Operator requested local incident tracking. No file or security policy was changed."
            });
            Validate(incident);
            ProtectedStore.Write(Key(incident.Id), incident);
            ProtectedStore.Write("incident-case-index", ids.Concat(new[] { incident.Id }).ToArray());
            return incident;
        }
    }

    private static string DetectionReference(LiveDetectionEvent item) =>
        $"Defender Operational event {item.EventId}, record {item.RecordId}, observed {item.EventUtc:O}.";

    internal static void Update(string id, string notes, bool closed)
    {
        lock (Gate) { var item = Load(id); item.Notes = notes; if (item.Closed != closed) item.Timeline.Add(new IncidentEntry { Action = closed ? "Case closed by operator" : "Case reopened by operator", Detail = "Case workflow state only; not proof of eradication or recovery." }); item.Closed = closed; Validate(item); ProtectedStore.Write(Key(id), item); }
    }
    internal static void ConfirmStep(string id, string step)
    {
        if (!new[] { "Isolation reviewed", "Recovery copy verified", "Credentials rotated on a trusted device", "Recovery and monitoring reviewed" }.Contains(step)) throw new ArgumentException("Unknown response step.");
        lock (Gate) { var item = Load(id); item.Timeline.Add(new IncidentEntry { Action = step, Detail = "Operator-reported confirmation; FishTank did not independently verify completion." }); Validate(item); ProtectedStore.Write(Key(id), item); }
    }
    internal static void Preserve(string id, string label, object evidence)
    {
        string json = Core.Json(evidence); if (string.IsNullOrWhiteSpace(label) || label.Length > 160 || Encoding.UTF8.GetByteCount(json) > 256 * 1024) throw new IOException("Select evidence within the 256 KiB per-item limit.");
        string hash; using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(json))).Replace("-", "");
        lock (Gate) { var item = Load(id); item.Evidence.Add(new IncidentEvidence { Label = label, Json = json, Sha256 = hash }); item.Timeline.Add(new IncidentEntry { Action = "Evidence preserved", Detail = label + "; SHA-256 " + hash }); Validate(item); ProtectedStore.Write(Key(id), item); }
    }
    internal static void PreserveFileHash(string id, string path)
    {
        path = ScanPaths.Normalize(path); ScanPaths.RefuseReparseAncestors(path);
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var sha = SHA256.Create())
        {
            if (held.Length > 128L * 1024 * 1024) throw new IOException("File hashing is limited to 128 MiB.");
            var file = new FileInfo(path); file.Refresh();
            string digest = BitConverter.ToString(sha.ComputeHash(held)).Replace("-", "");
            Preserve(id, "Selected file metadata and hash", new { Path = path, Bytes = held.Length, ModifiedUtc = file.LastWriteTimeUtc, Sha256 = digest, Scope = "Metadata/hash only. Original file bytes were not copied, executed, quarantined or uploaded." });
        }
    }
    internal static string Export(string id)
    {
        var item = Load(id);
        foreach (var evidence in item.Evidence)
        {
            using (var sha = SHA256.Create()) if (!DownloadChecksum.Matches(evidence.Sha256, BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(evidence.Json))).Replace("-", ""))) throw new IOException("Preserved evidence checksum mismatch.");
        }
        return Core.Json(new { Scope = "Local incident notes and selected observations. Checksums detect changes to preserved content; this is not a forensic chain-of-custody certification. Operator steps are not independently verified. Review/redact before sharing; edited exports are separate from originals.", Case = item });
    }
}
