using System;
using System.Collections.Generic;
using System.Linq;

namespace FishTank;

public sealed class LiveDetectionEvent
{
    public DateTime RecordedUtc;
    public DateTime EventUtc;
    public long SourceUtcTicks;
    public long RecordId;
    public int EventId;
    public string Kind;
    public string Summary;
    public string Details;
    public bool Recovered;
    public int Severity;
}
public sealed class LiveProtectionReading
{
    public bool? AntivirusEnabled;
    public bool? RealTimeProtectionEnabled;
    public string Mode;
    public string Error;
    public bool? ServiceEnabled, BehaviorMonitorEnabled, IoavProtectionEnabled, NisEnabled, TamperProtected;
    public int? SignatureAgeDays;
    public string SignatureVersion;
}
public static class LiveDetectionPolicy
{
    public static readonly int[] EventIds = { 1006, 1007, 1008, 1015, 1116, 1117, 1118, 1119, 1121, 1122, 1123, 1124, 1127, 5000, 5001, 5007, 5008, 5009, 5010, 5011, 5012, 5013 };
    public static int Severity(int id) => id == 1006 || id == 1008 || id == 1015 || id == 1116 || id == 1118 || id == 1119 || id == 5001 || id == 5008 || id == 5010 || id == 5012 ? 2 : id == 1122 || id == 1124 || id == 5000 || id == 5007 || id == 5009 || id == 5011 ? 0 : 1;
    public static bool InvalidatesStatus(int id) => id >= 5000 && id <= 5013;
    public static string Kind(int id)
    {
        switch (id)
        {
            case 1006: case 1116: return "Defender detection";
            case 1007: case 1117: return "Defender action";
            case 1008: case 1118: case 1119: return "Defender action failed";
            case 1015: return "Defender suspicious behavior";
            case 1121: return "ASR block";
            case 1122: return "ASR audit";
            case 1123: return "Protected-folder block";
            case 1124: return "Protected-folder audit";
            case 1127: return "Protected-sector block";
            case 5001: case 5010: case 5012: return "Protection disabled";
            case 5007: return "Protection configuration changed";
            case 5000: case 5009: case 5011: return "Protection configuration changed";
            case 5008: return "Defender engine failure";
            case 5013: return "Tamper protection blocked change";
            default: return null;
        }
    }
    public static string Summary(int id)
    {
        switch (id)
        {
            case 1006: case 1116: return "Microsoft Defender reported a threat detection. Review Windows Security; detections can need investigation.";
            case 1007: case 1117: return "Defender reported a protection action. This event alone does not prove the device is clean.";
            case 1008: case 1118: case 1119: return "Defender reported a failed protection action. Review Windows Security promptly.";
            case 1015: return "Defender reported suspicious behavior. Review the source evidence; this is not an independent FishTank verdict.";
            case 1121: return "Defender reported an attack-surface-reduction block. Inspect its context in Windows Security.";
            case 1122: return "Defender reported an attack-surface-reduction audit event. Audit mode does not block.";
            case 1123: return "Defender reported a controlled-folder-access block. A legitimate app can also trigger it.";
            case 1124: return "Defender reported a controlled-folder-access audit event. This does not mean access was blocked.";
            case 1127: return "Defender reported a controlled-folder-access disk-sector block. Review its source context and application compatibility.";
            case 5001: return "Defender reported real-time protection disabled. Check Windows Security or your managed antivirus policy.";
            case 5010: case 5012: return "Defender reported a scanning component disabled. Recheck current status and Windows Security; another antivirus or policy may be in use.";
            case 5007: return "Defender reported a configuration change. Updates and legitimate administration can cause this; it is not proof of intrusion.";
            case 5000: case 5009: case 5011: return "Defender reported a protection component enabled. A fresh status observation is still required; this is not proof of complete protection.";
            case 5008: return "Defender reported an engine failure. Protection coverage needs prompt review in Windows Security.";
            case 5013: return "Tamper protection reported blocking a settings change. Review who requested it; a blocked change is not proof of intrusion.";
            default: return "Unsupported event; no detection inferred.";
        }
    }
    public static string Coverage(bool enabled, bool subscribed, LiveProtectionReading reading, DateTime checkedUtc, DateTime nowUtc)
    {
        if (!enabled) return "Off · FishTank live alerts are disabled. Windows protection is independent.";
        if (!subscribed) return "Coverage gap · no confirmed Defender event-log subscription is available. Live events may be missed."
            + (reading?.RealTimeProtectionEnabled == false || reading?.AntivirusEnabled == false ? " Defender also reports protection off; another antivirus or managed policy may be in use." : "");
        if (reading == null || checkedUtc == DateTime.MinValue || checkedUtc > nowUtc || nowUtc - checkedUtc > TimeSpan.FromSeconds(90))
            return "Listening, coverage unknown · current Defender protection status is missing or outdated.";
        if (!string.IsNullOrEmpty(reading.Mode) && !string.Equals(reading.Mode, "Normal", StringComparison.OrdinalIgnoreCase))
            return "Listening, limited coverage · Defender reports " + reading.Mode + (reading.RealTimeProtectionEnabled == false || reading.AntivirusEnabled == false ? "; protection is reported off" : "") + ". Other antivirus products are not monitored by this feature.";
        if (reading.AntivirusEnabled == false || reading.RealTimeProtectionEnabled == false)
            return "Listening, protection warning · Defender antivirus or real-time protection is reported off. FishTank does not enable it automatically.";
        if (!string.IsNullOrEmpty(reading.Error) || reading.AntivirusEnabled == null || reading.RealTimeProtectionEnabled == null || string.IsNullOrEmpty(reading.Mode))
            return "Listening, coverage unknown · Defender status could not be fully determined.";
        if (reading.ServiceEnabled == false || reading.BehaviorMonitorEnabled == false || reading.IoavProtectionEnabled == false || reading.NisEnabled == false || reading.TamperProtected == false)
            return "Listening, protection warning · one or more Defender service/behavior/download/network/tamper protection components were reported off. Review Windows Security and managed policy.";
        if (reading.SignatureAgeDays > 3) return "Listening, protection warning · Defender signatures were reported more than three days old. Review updates and current status.";
        if (reading.SignatureAgeDays < 0) return "Listening, coverage unknown · Defender returned an invalid signature age.";
        if (reading.ServiceEnabled == null || reading.BehaviorMonitorEnabled == null || reading.IoavProtectionEnabled == null || reading.NisEnabled == null || reading.TamperProtected == null || reading.SignatureAgeDays == null)
            return "Listening to Defender · real-time protection was reported on; some component/signature observations are unavailable. Complete threat detection is not guaranteed.";
        return "Listening to Defender · real-time protection was reported on. Exclusions and Windows policy still apply; complete threat detection is not guaranteed.";
    }
    public static string VerifiedCoverage(bool enabled, bool subscribed, bool logHealthy, bool caughtUp, DateTime logCheckedUtc, LiveProtectionReading reading, DateTime checkedUtc, DateTime now)
    {
        if (!enabled) return Coverage(false, false, reading, checkedUtc, now);
        if (!subscribed) return Coverage(true, false, reading, checkedUtc, now);
        if (!logHealthy || logCheckedUtc == DateTime.MinValue || logCheckedUtc > now || now - logCheckedUtc > TimeSpan.FromSeconds(30)) return "Coverage gap · event-log health is unavailable or stale. Subscription alone does not establish delivery.";
        if (!caughtUp) return "Catching up · available log records are still being reconciled; historical delivery is incomplete. " + Coverage(true, true, reading, checkedUtc, now);
        return Coverage(true, true, reading, checkedUtc, now);
    }
}

public sealed class LiveDetectionBuffer
{
    private readonly object gate = new object();
    private readonly List<LiveDetectionEvent> items = new List<LiveDetectionEvent>();
    private readonly int limit;
    private readonly HashSet<string> seen = new HashSet<string>();
    private readonly Queue<string> seenOrder = new Queue<string>();
    public long Trimmed { get; private set; }
    public int Count { get { lock (gate) return items.Count; } }
    public LiveDetectionBuffer(int limit = 1000) { if (limit < 1 || limit > 5000) throw new ArgumentException("Invalid live history limit."); this.limit = limit; }
    private static string Identity(LiveDetectionEvent item) => item.RecordId + ":" + item.EventId + ":" + (item.SourceUtcTicks > 0 ? item.SourceUtcTicks : item.EventUtc.Ticks);
    public bool Add(LiveDetectionEvent item)
    {
        lock (gate)
        {
            if (item.RecordId > 0)
            {
                string identity = Identity(item); if (!seen.Add(identity)) return false;
                seenOrder.Enqueue(identity); while (seenOrder.Count > 10000) seen.Remove(seenOrder.Dequeue());
            }
            Append(item); return true;
        }
    }
    private void Append(LiveDetectionEvent item) { items.Add(new LiveDetectionEvent { RecordedUtc = item.RecordedUtc, EventUtc = item.EventUtc, SourceUtcTicks = item.SourceUtcTicks > 0 ? item.SourceUtcTicks : item.EventUtc.Ticks, RecordId = item.RecordId, EventId = item.EventId, Kind = item.Kind, Summary = item.Summary, Details = item.Details, Recovered = item.Recovered, Severity = item.Severity }); while (items.Count > limit) { items.RemoveAt(0); Trimmed++; } }
    public void TrimOldest(int count = 1) { lock (gate) { int remove = Math.Min(Math.Max(0, count), Math.Max(0, items.Count - 1)); items.RemoveRange(0, remove); Trimmed += remove; } }
    public void Restore(IEnumerable<LiveDetectionEvent> previous)
    {
        lock (gate) { items.Clear(); seen.Clear(); seenOrder.Clear(); foreach (var item in previous) Add(item); }
    }
    public List<LiveDetectionEvent> Snapshot() { lock (gate) return items.Select(x => new LiveDetectionEvent { RecordedUtc = x.RecordedUtc, EventUtc = x.EventUtc, SourceUtcTicks = x.SourceUtcTicks, RecordId = x.RecordId, EventId = x.EventId, Kind = x.Kind, Summary = x.Summary, Details = x.Details, Recovered = x.Recovered, Severity = x.Severity }).ToList(); }
}
