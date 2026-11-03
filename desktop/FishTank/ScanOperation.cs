using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;

namespace FishTank;

public sealed class ScanEventEvidence
{
    public int EventId;
    public string ScanId;
    public DateTime TimeUtc;
}

public static class ScanStopPolicy
{
    public static string Assess(IEnumerable<ScanEventEvidence> events, DateTime startedUtc)
    {
        var relevant = events.Where(x => x.TimeUtc >= startedUtc).ToArray();
        var starts = relevant.Where(x => x.EventId == 1000).Select(x => x.ScanId).Distinct().ToArray();
        if (starts.Length != 1 || string.IsNullOrEmpty(starts[0])) return "Stop unverified: no unique Defender scan start could be associated with this request.";
        var terminal = relevant.Where(x => x.ScanId == starts[0] && (x.EventId == 1001 || x.EventId == 1002)).OrderByDescending(x => x.TimeUtc).FirstOrDefault();
        return terminal?.EventId == 1002 ? "Defender reported the observed scan stopped before completion (event 1002)."
            : terminal?.EventId == 1001 ? "Defender reported the observed scan completed (event 1001); it was not confirmed cancelled."
            : "Stop unverified: no matching Defender completion or stop event is available. Defender may still be running.";
    }
    internal static string Read(DateTime startedUtc)
    {
        try
        {
            var items = new List<ScanEventEvidence>();
            var query = new EventLogQuery(LiveDetectionSession.Channel, PathType.LogName, "*[System[(EventID=1000 or EventID=1001 or EventID=1002)]]") { ReverseDirection = true };
            using (var reader = new EventLogReader(query))
            {
                for (int i = 0; i < 200; i++)
                using (var record = reader.ReadEvent())
                {
                    if (record == null) break;
                    DateTime time = record.TimeCreated?.ToUniversalTime() ?? DateTime.MinValue;
                    if (time < startedUtc) break;
                    if (record.ProviderName != "Microsoft-Windows-Windows Defender") continue;
                    var doc = new XmlDocument { XmlResolver = null };
                    using (var xml = XmlReader.Create(new StringReader(record.ToXml()), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 65536 })) doc.Load(xml);
                    string id = null;
                    foreach (XmlNode node in doc.SelectNodes("//*[local-name()='EventData']/*[local-name()='Data']"))
                        if ((node.Attributes?["Name"]?.Value ?? "").Replace(" ", "").Replace("_", "").Equals("ScanID", StringComparison.OrdinalIgnoreCase)) id = node.InnerText;
                    items.Add(new ScanEventEvidence { EventId = record.Id, ScanId = id, TimeUtc = time });
                }
            }
            return Assess(items, startedUtc);
        }
        catch { return "Stop unverified: Defender scan events could not be read. Check Windows Security; the engine may still be running."; }
    }
}

internal sealed class ScanOperation
{
    private readonly Func<string> scan;
    private readonly Func<CommandResult> cancel;
    private readonly Func<DateTime, string> evidence;
    private readonly object gate = new object();
    private bool started, running, cancelRequested;
    private string phase = "Ready", result = "", cancellation = "No cancellation requested.";
    internal readonly string Kind;
    internal DateTime StartedUtc { get; private set; }
    internal bool IsRunning { get { lock (gate) return running; } }
    internal bool CancelRequested { get { lock (gate) return cancelRequested; } }
    internal string Phase { get { lock (gate) return phase; } }
    internal string Result { get { lock (gate) return result; } }
    internal string Cancellation { get { lock (gate) return cancellation; } }
    internal string ResultJson { get; private set; }
    internal TimeSpan Elapsed => StartedUtc == DateTime.MinValue ? TimeSpan.Zero : (endedUtc ?? DateTime.UtcNow) - StartedUtc;
    private DateTime? endedUtc;
    internal ScanOperation(string kind, string path, Func<string> scan = null, Func<CommandResult> cancel = null, Func<DateTime, string> evidence = null)
    {
        Kind = kind;
        this.scan = scan ?? (() => Core.Scan(kind, path));
        this.cancel = cancel ?? (() => Core.Run(Core.Defender(), "-Scan -Cancel", 30000));
        this.evidence = evidence ?? ScanStopPolicy.Read;
    }
    internal async Task RunAsync()
    {
        lock (gate) { if (started) throw new InvalidOperationException("This scan has already started."); started = running = true; StartedUtc = DateTime.UtcNow; phase = "Starting Defender"; }
        string journal = null; FileStream profileLock = null;
        try
        {
            profileLock = new FileStream(Path.Combine(Core.Data,"active-operation.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            journal = OperationJournal.Begin(Kind + " scan");
            lock (gate) phase = "Waiting for Defender scan result";
            ResultJson = await Task.Run(scan);
            var report = Core.Parse<Dictionary<string, object>>(ResultJson);
            lock (gate) { result = Convert.ToString(report["interpretation"]); phase = "Scan command finished"; }
        }
        catch (Exception ex) { lock (gate) { result = "Scan could not complete: " + ex.Message; phase = "Failed · engine state may be unknown"; } }
        finally
        {
            if (CancelRequested) await RefreshEvidenceAsync();
            try { if(journal!=null)OperationJournal.Finish(journal); } catch { lock(gate) result += " Operation journal could not be cleared; review this result after restarting."; }
            profileLock?.Dispose();
            lock (gate) { running = false; endedUtc = DateTime.UtcNow; }
        }
    }
    internal async Task RefreshEvidenceAsync()
    {
        if (!CancelRequested) return;
        try { string observed = await Task.Run(() => evidence(StartedUtc)); lock (gate) cancellation = observed; }
        catch { lock (gate) cancellation = "Stop unverified: Defender scan events could not be read."; }
    }
    internal async Task CancelAsync()
    {
        lock (gate)
        {
            if (!running || cancelRequested || (Kind != "Quick" && Kind != "Full")) return;
            cancelRequested = true;
            cancellation = "Cancellation requested; waiting for Defender. The cancel command can affect other active quick/full scans.";
        }
        try
        {
            var response = await Task.Run(cancel);
            string observed = await Task.Run(() => evidence(StartedUtc));
            lock (gate) cancellation = "Cancel command returned " + response.ExitCode + ". " + observed;
        }
        catch { lock (gate) cancellation = "Cancel request failed. Defender may still be running; review Windows Security."; }
        AppActivity.Record("Scans", "Defender cancellation requested; confirmation is shown in scan progress");
    }
}
