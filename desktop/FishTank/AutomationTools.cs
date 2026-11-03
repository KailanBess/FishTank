using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
namespace FishTank;
public sealed class QueuedScan
{
    public string Id=Guid.NewGuid().ToString("N"),Kind,Path,State="Pending",Result="";
    public DateTime CreatedUtc=DateTime.UtcNow,CompletedUtc;
}
internal static class ScanQueueStore
{
    private static readonly object Gate=new object();
    internal static bool Running { get; private set; }
    private static FileStream Lease()=>new FileStream(System.IO.Path.Combine(Core.Data,"scan-queue.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
    internal static List<QueuedScan> Read()=>ProtectedStore.Read("scan-queue",()=>new List<QueuedScan>()) ?? throw new InvalidDataException("Queue unreadable.");
    internal static void Add(string kind,string path)
    {
        using var lease=Lease();
        if(kind!="Quick" && kind!="Full" && kind!="Custom")throw new ArgumentException("Unsupported scan type.");
        if(kind=="Custom"){path=ScanPaths.Normalize(path);ScanPaths.RefuseReparseAncestors(path);if(!File.Exists(path)&&!Directory.Exists(path))throw new FileNotFoundException("Scan target is unavailable.");}
        lock(Gate){var jobs=Read();if(jobs.Count>=200)throw new InvalidOperationException("Clear completed entries before adding more than 200 queue items.");jobs.Add(new QueuedScan{Kind=kind,Path=path});ProtectedStore.Write("scan-queue",jobs);}
    }
    internal static void Remove(string id){using var lease=Lease();lock(Gate){if(Running)throw new InvalidOperationException("Wait for the queue to finish.");var jobs=Read();jobs.RemoveAll(x=>x.Id==id);ProtectedStore.Write("scan-queue",jobs);}}
    internal static void Recover(){using var lease=Lease();lock(Gate){if(Running)return;var jobs=Read();foreach(var job in jobs.Where(x=>x.State=="Running")){job.State="Interrupted";job.Result="Application closed before the result was recorded. Review Defender before queuing another scan.";}ProtectedStore.Write("scan-queue",jobs);}}
    internal static async Task RunAsync(Func<QueuedScan,Task<string>> scan,Func<bool> stop)
    {
        using var lease=Lease();
        lock(Gate){if(Running)throw new InvalidOperationException("The scan queue is already running.");Running=true;}
        try
        {
            while(!stop())
            {
                QueuedScan job;
                lock(Gate){var jobs=Read();job=jobs.FirstOrDefault(x=>x.State=="Pending");if(job==null)break;job.State="Running";ProtectedStore.Write("scan-queue",jobs);}
                string state="Completed",result;
                try{result=await scan(job);}catch(Exception ex){state="Failed";result=ex.Message;}
                lock(Gate){var jobs=Read();var stored=jobs.Single(x=>x.Id==job.Id);stored.State=state;stored.Result=result;stored.CompletedUtc=DateTime.UtcNow;ProtectedStore.Write("scan-queue",jobs);}
            }
        }
        finally{lock(Gate)Running=false;}
    }
}
public sealed class CipherBackupPlan
{
    public bool Enabled;
    public string Destination="";
    public int IntervalHours=24,Keep=7,DrillDays=30;
    public DateTime LastAttemptUtc,LastCopyUtc,LastDrillUtc;
    public DateTime LastDrillNoticeUtc;
    public string LastResult="No automatic backup recorded.";
    public void Validate(){if(IntervalHours<1||IntervalHours>168||Keep<1||Keep>30||DrillDays<1||DrillDays>365||(Enabled && string.IsNullOrWhiteSpace(Destination)))throw new InvalidDataException("Use a backup destination, 1–168 hours, 1–30 retained copies and 1–365 drill days.");}
}
public sealed class ManagedBackup
{
    public string Name,Sha256,Root;
    public DateTime CreatedUtc;
    public long Bytes;
}
internal static class AutomaticBackups
{
    private static readonly object Gate=new object();
    internal static CipherBackupPlan Load(){var p=ProtectedStore.Read("automatic-backup-plan",()=>new CipherBackupPlan())??throw new InvalidDataException("Backup plan unreadable.");p.Validate();return p;}
    internal static void Save(CipherBackupPlan plan){plan.Validate();if(plan.Enabled){plan.Destination=Path.GetFullPath(plan.Destination);if(!Directory.Exists(plan.Destination))throw new DirectoryNotFoundException("Choose an existing backup destination.");}ProtectedStore.Write("automatic-backup-plan",plan);}
    private static void SaveRecordedState(CipherBackupPlan plan){plan.Validate();ProtectedStore.Write("automatic-backup-plan",plan);}
    internal static List<ManagedBackup> Manifest()=>ProtectedStore.Read("managed-backups",()=>new List<ManagedBackup>())??throw new InvalidDataException("Backup manifest unreadable.");
    internal static void RequireSpace(string destination,long bytes)
    {
        var drive=new DriveInfo(Path.GetPathRoot(Path.GetFullPath(destination)));
        if(!drive.IsReady || drive.AvailableFreeSpace < bytes+16*1024*1024)throw new IOException("Insufficient or unknown destination space. No original was replaced.");
    }
    internal static string CopyNow()
    {
        lock(Gate)
        {
            using var lease=new FileStream(Path.Combine(Core.Data,"managed-backups.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            var plan=Load();if(string.IsNullOrWhiteSpace(plan.Destination))throw new InvalidOperationException("Choose a backup destination first.");
            plan.LastAttemptUtc=DateTime.UtcNow;Save(plan);
            byte[] bytes=LocalStoreFiles.ReadOptional(PasswordSession.DefaultPath,16*1024*1024)??throw new FileNotFoundException("Create a password vault before backing it up.");
            if(bytes.Length<96 || System.Text.Encoding.ASCII.GetString(bytes,0,8)!="FTPV0001")throw new InvalidDataException("Source is not a recognized encrypted vault.");
            RequireSpace(plan.Destination,bytes.Length);
            var manifest=Manifest();var item=new ManagedBackup{Name="FishTank-vault-"+Guid.NewGuid().ToString("N")+".ftvault",Root=Path.GetFullPath(plan.Destination),CreatedUtc=DateTime.UtcNow,Bytes=bytes.Length};
            string destination=Path.Combine(plan.Destination,item.Name);string expected;using(var sha=System.Security.Cryptography.SHA256.Create())expected=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","");RestoreFileWriter.WriteNew(destination,bytes);item.Sha256=DownloadChecksum.Compute(destination);if(!DownloadChecksum.Matches(expected,item.Sha256))throw new IOException("Backup bytes did not match the source. Retention was not performed.");
            manifest.Add(item);ProtectedStore.Write("managed-backups",manifest);plan.LastCopyUtc=item.CreatedUtc;plan.LastResult="Encrypted bytes copied and SHA-256 recorded. Passphrase verification/restore testing is separate.";Save(plan);
            BackupHistory.Created(BackupHistory.PasswordVault,destination,item.Sha256);
            // Delete only older, unchanged files recorded by FishTank in this exact destination.
            foreach(var old in manifest.Where(x=>string.Equals(x.Root,item.Root,StringComparison.OrdinalIgnoreCase)).OrderByDescending(x=>x.CreatedUtc).Skip(plan.Keep).ToArray())
            {
                if(!System.Text.RegularExpressions.Regex.IsMatch(old.Name??"","\\AFishTank-vault-[a-f0-9]{32}\\.ftvault\\z"))throw new InvalidDataException("Invalid managed backup filename; retention stopped.");
                string path=Path.Combine(plan.Destination,old.Name);if(File.Exists(path)){ScanPaths.RefuseReparseAncestors(path);if(!DownloadChecksum.Matches(old.Sha256,DownloadChecksum.Compute(path)))continue;File.Delete(path);}manifest.Remove(old);
            }
            ProtectedStore.Write("managed-backups",manifest);return plan.LastResult;
        }
    }
    internal static string Tick()
    {
        var p=Load();if(!p.Enabled || DateTime.UtcNow-p.LastAttemptUtc<TimeSpan.FromHours(p.IntervalHours))return null;
        try{return CopyNow();}catch(Exception ex){p=Load();p.LastAttemptUtc=DateTime.UtcNow;p.LastResult="Automatic backup needs attention: "+ex.Message;SaveRecordedState(p);return p.LastResult;}
    }
    internal static string CopyWithStatus()
    {
        try { return CopyNow(); }
        catch (Exception ex)
        {
            try { var p = Load(); p.LastAttemptUtc = DateTime.UtcNow; p.LastResult = "Manual backup failed: " + ex.Message; SaveRecordedState(p); } catch { }
            throw;
        }
    }
    internal static string DrillReminder()
    {
        var p=Load();if(!p.Enabled)return null;var now=DateTime.UtcNow;
        var last=BackupHistory.Observations().Where(x=>x.Kind==BackupHistory.PasswordVault).Select(x=>x.RestoreTestUtc).DefaultIfEmpty(DateTime.MinValue).Max();
        if(now-last<TimeSpan.FromDays(p.DrillDays)||now-p.LastDrillNoticeUtc<TimeSpan.FromDays(1))return null;
        p.LastDrillNoticeUtc=now;SaveRecordedState(p);return "A password-vault restore drill is due. Open Recovery center to choose a backup and enter its passphrase. No plaintext restore or verification runs automatically.";
    }
}
