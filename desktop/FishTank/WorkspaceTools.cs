using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace FishTank;

public sealed class WorkspacePreferences
{
    public string[] Favorites = new string[0];
    public string[] Recent = new string[0];
    public int HistoryDays = 30;
    public int NoticeLevel;
    public int QuietStartHour = 22;
    public int QuietEndHour = 8;
    public bool QuietHours;
    public bool ExternalDrivePrompts;
    public bool RemoteStatusEnabled;
    public bool AutoDeleteAuditHistory;
    public DateTime LastHistoryCleanupUtc;
    public DateTime SnoozeUntilUtc;
    public void Validate()
    {
        if (Favorites == null || Recent == null || Favorites.Length > 50 || Recent.Length > 30 || Favorites.Concat(Recent).Any(x => !ToolCatalog.Ids.Contains(x))
            || HistoryDays < 1 || HistoryDays > 365 || NoticeLevel < 0 || NoticeLevel > 2 || QuietStartHour < 0 || QuietStartHour > 23 || QuietEndHour < 0 || QuietEndHour > 23)
            throw new InvalidDataException("Invalid workspace preferences; existing settings were preserved.");
    }
    internal static WorkspacePreferences Load() { var p = ProtectedStore.Read("workspace-preferences", () => new WorkspacePreferences()) ?? throw new InvalidDataException("Workspace settings unreadable."); p.Validate(); return p; }
    internal static void Update(Action<WorkspacePreferences> action) { lock (Gate) { var p=Load(); action(p); p.Validate(); ProtectedStore.Write("workspace-preferences",p); } }
    private static readonly object Gate = new object();
    public bool MayNotify(int level, DateTime nowUtc, DateTime local)
    {
        if (level < NoticeLevel || SnoozeUntilUtc > nowUtc) return false;
        return !QuietHours || QuietStartHour == QuietEndHour || !(QuietStartHour < QuietEndHour ? local.Hour >= QuietStartHour && local.Hour < QuietEndHour : local.Hour >= QuietStartHour || local.Hour < QuietEndHour);
    }
}
public sealed class ToolDefinition
{
    public string Id, Title, Keywords;
    public ToolDefinition(string id,string title,string keywords){Id=id;Title=title;Keywords=keywords;}
}
internal static class ToolCatalog
{
    internal static readonly ToolDefinition[] All = {
        new ToolDefinition("search","Search all tools","global command palette favorites recent"),
        new ToolDefinition("notices","Notification center","alerts severity snooze quiet hours deduplicate"),
        new ToolDefinition("backups","Automatic encrypted backups","retention destination restore drill"),
        new ToolDefinition("queue","Scan queue and profiles","multiple targets compare results exclusions external drive"),
        new ToolDefinition("vault","Password vault","history expiry duplicate attachments import export undo"),
        new ToolDefinition("profiles","Profiles and layouts","multiple device portable migrate appearance shortcuts compact"),
        new ToolDefinition("devices","Devices and signed reports","inventory remote server health signature sharing"),
        new ToolDefinition("passkeys","Passkeys and security keys","server WebAuthn authenticator sessions recovery codes"),
        new ToolDefinition("privacy","Privacy and history","permissions retention selective deletion diagnostics preview"),
        new ToolDefinition("recovery","Recovery center","restore preview compare backup recovery usb"),
        new ToolDefinition("help","Offline help and troubleshooting","search guides bug report diagnostics"),
        new ToolDefinition("updates","Updates and distribution","signed installer version channels staged rollback"),
        new ToolDefinition("integrations","Windows integration","right click scan shell menu uninstall"),
        new ToolDefinition("accessibility","Accessibility","keyboard shortcuts screen reader large controls focus"),
        new ToolDefinition("activity","Activity timeline","scans detections quarantine backups updates"),
        new ToolDefinition("support","Support report","export redact preview diagnostics bug report")
    };
    internal static string[] Ids => All.Select(x=>x.Id).ToArray();
    internal static ToolDefinition[] Search(string query) => All.Where(x => (query??"").Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries).All(word => (x.Title+" "+x.Keywords).IndexOf(word,StringComparison.OrdinalIgnoreCase)>=0)).ToArray();
    internal static void Record(string id) => WorkspacePreferences.Update(p=>p.Recent=new[]{id}.Concat(p.Recent.Where(x=>x!=id)).Take(20).ToArray());
}
public sealed class NoticeRecord
{
    public string Id=Guid.NewGuid().ToString("N");
    public string Summary, Destination;
    public int Level, Repeats=1;
    public DateTime RecordedUtc;
    public bool Read;
}
internal static class NoticeCenter
{
    private static readonly object Gate=new object();
    internal static List<NoticeRecord> Read() => ProtectedStore.Read("notification-center",()=>new List<NoticeRecord>()) ?? throw new InvalidDataException("Notification history unreadable.");
    internal static bool Add(string summary,string destination,int level)
    {
        lock(Gate)
        {
            var prefs=WorkspacePreferences.Load(); var now=DateTime.UtcNow; var items=Read();
            summary=(summary??"").Length>1000 ? summary.Substring(0,1000) : summary;
            var previous=items.LastOrDefault(x=>x.Summary==summary && x.Destination==destination && now-x.RecordedUtc<TimeSpan.FromMinutes(5));
            if(previous!=null){previous.Repeats++;previous.RecordedUtc=now;}else items.Add(new NoticeRecord { Summary=summary,Destination=destination,Level=level,RecordedUtc=now });
            ProtectedStore.Write("notification-center",items.Where(x=>now-x.RecordedUtc<TimeSpan.FromDays(prefs.HistoryDays)).OrderByDescending(x=>x.RecordedUtc).Take(300).OrderBy(x=>x.RecordedUtc).ToList());
            return previous==null && prefs.MayNotify(level,now,DateTime.Now);
        }
    }
    internal static void Change(string id,bool delete) { lock(Gate){var items=Read();var item=items.Single(x=>x.Id==id);if(delete)items.Remove(item);else item.Read=true;ProtectedStore.Write("notification-center",items);} }
}
internal static class OperationJournal
{
    internal static string Begin(string kind) { string id=Guid.NewGuid().ToString("N"); using(var process=System.Diagnostics.Process.GetCurrentProcess())ProtectedStore.Write("active-operation",new OperationRecord{Id=id,Kind=kind,StartedUtc=DateTime.UtcNow,ProcessId=process.Id,ProcessStartedUtc=process.StartTime.ToUniversalTime()});return id; }
    internal static void Finish(string id) { var record=ProtectedStore.Read<OperationRecord>("active-operation",()=>null);if(record?.Id==id)ProtectedStore.Write<OperationRecord>("active-operation",null); }
    internal static OperationRecord Pending()=>ProtectedStore.Read<OperationRecord>("active-operation",()=>null);
    internal static bool IsAlive(OperationRecord record){try{using(var process=System.Diagnostics.Process.GetProcessById(record.ProcessId))return !process.HasExited&&process.StartTime.ToUniversalTime()==record.ProcessStartedUtc;}catch{return false;}}
}
public sealed class OperationRecord { public string Id,Kind; public DateTime StartedUtc,ProcessStartedUtc;public int ProcessId; }
