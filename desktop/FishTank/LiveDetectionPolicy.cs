using System;
using System.Collections.Generic;
using System.Linq;

namespace FishTank;

public sealed class LiveDetectionEvent
{
    public DateTime RecordedUtc;
    public DateTime EventUtc;
    public long RecordId;
    public int EventId;
    public string Kind;
    public string Summary;
    public string Details;
}
public sealed class LiveProtectionReading
{
    public bool? AntivirusEnabled;
    public bool? RealTimeProtectionEnabled;
    public string Mode;
    public string Error;
}
public static class LiveDetectionPolicy
{
    public static string Kind(int id)
    {
        switch (id)
        {
            case 1116: return "Defender detection";
            case 1117: return "Defender action";
            case 1118: case 1119: return "Defender action failed";
            case 1121: return "ASR block";
            case 1122: return "ASR audit";
            case 1123: return "Protected-folder block";
            case 1124: return "Protected-folder audit";
            case 5001: return "Protection disabled";
            case 5007: return "Protection configuration changed";
            default: return null;
        }
    }
    public static string Summary(int id)
    {
        switch (id)
        {
            case 1116: return "Microsoft Defender reported a threat detection. Review Windows Security; detections can need investigation.";
            case 1117: return "Defender reported a protection action. This event alone does not prove the device is clean.";
            case 1118: case 1119: return "Defender reported a failed protection action. Review Windows Security promptly.";
            case 1121: return "Defender reported an attack-surface-reduction block. Inspect its context in Windows Security.";
            case 1122: return "Defender reported an attack-surface-reduction audit event. Audit mode does not block.";
            case 1123: return "Defender reported a controlled-folder-access block. A legitimate app can also trigger it.";
            case 1124: return "Defender reported a controlled-folder-access audit event. This does not mean access was blocked.";
            case 5001: return "Defender reported real-time protection disabled. Check Windows Security or your managed antivirus policy.";
            case 5007: return "Defender reported a configuration change. Updates and legitimate administration can cause this; it is not proof of intrusion.";
            default: return "Unsupported event; no detection inferred.";
        }
    }
    public static string Coverage(bool enabled, bool subscribed, LiveProtectionReading reading, DateTime checkedUtc, DateTime nowUtc)
    {
        if (!enabled) return "Off · FishTank live alerts are disabled. Windows protection is independent.";
        if (!subscribed) return "Coverage gap · no confirmed Defender event-log subscription is available. Live events may be missed."
            + (reading?.RealTimeProtectionEnabled == false || reading?.AntivirusEnabled == false ? " Defender also reports protection off; another antivirus or managed policy may be in use." : "");
        if (reading == null || checkedUtc == DateTime.MinValue || checkedUtc > nowUtc || nowUtc - checkedUtc > TimeSpan.FromMinutes(3))
            return "Listening, coverage unknown · current Defender protection status is missing or outdated.";
        if (!string.IsNullOrEmpty(reading.Mode) && !string.Equals(reading.Mode, "Normal", StringComparison.OrdinalIgnoreCase))
            return "Listening, limited coverage · Defender reports " + reading.Mode + ". Other antivirus products are not monitored by this feature.";
        if (reading.AntivirusEnabled == false || reading.RealTimeProtectionEnabled == false)
            return "Listening, protection warning · Defender antivirus or real-time protection is reported off. FishTank does not enable it automatically.";
        if (!string.IsNullOrEmpty(reading.Error) || reading.AntivirusEnabled == null || reading.RealTimeProtectionEnabled == null || string.IsNullOrEmpty(reading.Mode))
            return "Listening, coverage unknown · Defender status could not be fully determined.";
        return "Listening to Defender · real-time protection was reported on. Exclusions and Windows policy still apply; complete threat detection is not guaranteed.";
    }
}

public sealed class LiveDetectionBuffer
{
    private readonly object gate = new object();
    private readonly List<LiveDetectionEvent> items = new List<LiveDetectionEvent>();
    private readonly int limit;
    private long lastRecord;
    public LiveDetectionBuffer(int limit = 250) { if (limit < 1 || limit > 1000) throw new ArgumentException("Invalid live history limit."); this.limit = limit; }
    public bool Add(LiveDetectionEvent item)
    {
        lock (gate)
        {
            if (item.RecordId > 0 && items.Any(x => x.RecordId == item.RecordId && x.EventId == item.EventId && x.EventUtc == item.EventUtc)) return false;
            if (item.RecordId > 0 && lastRecord > 0 && item.RecordId < lastRecord)
                Append(new LiveDetectionEvent { RecordedUtc = item.RecordedUtc, EventUtc = item.EventUtc, Kind = "Coverage gap", Summary = "Defender event numbering moved backwards. A log reset/clear or delivery reordering may have occurred; earlier events can be unavailable." });
            if (item.RecordId > 0) lastRecord = item.RecordId;
            Append(item); return true;
        }
    }
    private void Append(LiveDetectionEvent item) { items.Add(item); while (items.Count > limit) items.RemoveAt(0); }
    public void Restore(IEnumerable<LiveDetectionEvent> previous)
    {
        lock (gate) { items.Clear(); lastRecord = 0; foreach (var item in previous) { Append(item); if (item.RecordId > 0) lastRecord = item.RecordId; } }
    }
    public List<LiveDetectionEvent> Snapshot() { lock (gate) return items.Select(x => new LiveDetectionEvent { RecordedUtc = x.RecordedUtc, EventUtc = x.EventUtc, RecordId = x.RecordId, EventId = x.EventId, Kind = x.Kind, Summary = x.Summary, Details = x.Details }).ToList(); }
}
