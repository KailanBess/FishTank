using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using FishTank;
internal static class Program
{
    private static int checks;
    private static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;}
    private static void Reject<T>(Action action,string name) where T:Exception{try{action();}catch(T){checks++;return;}throw new Exception(name);}
    [STAThread] private static int Main()
    {
        string parent=Path.GetFullPath(Path.GetTempPath()),root=Path.Combine(parent,"FishTank-expansion-"+Guid.NewGuid().ToString("N")),previous=Core.Data;
        Directory.CreateDirectory(root);Core.Data=root;
        try
        {
            var prefs=WorkspacePreferences.Load();Check(!prefs.RemoteStatusEnabled&&!prefs.ExternalDrivePrompts&&!prefs.QuietHours,"Optional sharing or prompts enabled by default");
            prefs.QuietHours=true;prefs.QuietStartHour=22;prefs.QuietEndHour=8;Check(!prefs.MayNotify(2,DateTime.UtcNow,new DateTime(2026,1,1,23,0,0)),"Overnight quiet hours failed");Check(prefs.MayNotify(2,DateTime.UtcNow,new DateTime(2026,1,1,12,0,0)),"Daytime quiet filter failed");
            Check(NoticeCenter.Add("Fixture reminder","Disk",1),"First notice suppressed");Check(!NoticeCenter.Add("Fixture reminder","Disk",1)&&NoticeCenter.Read().Single().Repeats==2,"Duplicate popup not suppressed");
            Check(ToolCatalog.Search("encrypted retention").Any(x=>x.Id=="backups"),"Search keywords missing");ToolCatalog.Record("queue");ToolCatalog.Record("queue");Check(WorkspacePreferences.Load().Recent.Length==1,"Recent tools duplicated");
            Reject<ArgumentException>(()=>ProfilePaths.Resolve(root,"../escape"),"Profile escaped root");
            var imported=VaultTransfer.Parse("Title,Password,Notes\r\nFixture,abc123,\"quoted, notes\"",true);Check(imported.Count==1&&imported[0].Notes=="quoted, notes","Quoted CSV damaged");
            Reject<InvalidDataException>(()=>VaultTransfer.Parse("Title,Password\nFixture,\"unterminated",true),"Malformed CSV accepted");
            using(var vault=PasswordSession.Create(PasswordSession.DefaultPath,"Fixture-only vault passphrase 2026!"))
            {
                var entry=new PasswordEntry{Title="Fixture",Password="first fixture secret"};vault.Put(entry);entry.Password="second fixture secret";vault.Put(entry);
                Check(vault.Entries.Single().History.Single().Password=="first fixture secret","Password history missing");vault.UndoLastMutation();Check(vault.Entries.Single().Password=="first fixture secret","Undo failed");
                vault.ImportEntries(imported);Check(vault.Entries.Count==2&&vault.Entries.All(e=>e.Id!=imported[0].Id),"Import IDs not remapped");vault.UndoLastMutation();Check(vault.Entries.Count==1,"Import undo failed");
                vault.SetAttachments(entry.Id,new System.Collections.Generic.List<VaultAttachment>{new VaultAttachment{Name="fixture.txt",Content=Convert.ToBase64String(new byte[]{1,2,3})}});Check(vault.Entries.Single().Attachments.Count==1,"Attachment not persisted");
            }
            using(var reopened=PasswordSession.Open(PasswordSession.DefaultPath,"Fixture-only vault passphrase 2026!"))Check(reopened.Entries.Single().Attachments.Count==1,"Version 3 vault did not reopen");
            string dest=Path.Combine(root,"copies");Directory.CreateDirectory(dest);AutomaticBackups.Save(new CipherBackupPlan{Destination=dest,Keep=1});AutomaticBackups.CopyNow();AutomaticBackups.CopyNow();Check(Directory.GetFiles(dest).Length==1&&AutomaticBackups.Manifest().Count==1,"Managed retention failed");
            var report=DeviceReports.Create();var verified=DeviceReports.Verify(report,report.Fingerprint);Check(verified.Sequence==1,"Signed report failed");var newer=DeviceReports.Create();Check(DeviceReports.Verify(newer,newer.Fingerprint).Sequence==2,"Sequence did not advance");
            Reject<UnauthorizedAccessException>(()=>DeviceReports.Verify(report,new string('0',64)),"Untrusted key accepted");report.Payload=Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("tampered"));Reject<UnauthorizedAccessException>(()=>DeviceReports.Verify(report,report.Fingerprint),"Tampered signature accepted");
            ScanQueueStore.Add("Quick",null);ScanQueueStore.Add("Full",null);int scans=0;ScanQueueStore.RunAsync(job=>{scans++;return Task.FromResult("fixture result");},()=>false).GetAwaiter().GetResult();Check(scans==2&&ScanQueueStore.Read().All(x=>x.State=="Completed"),"Queue did not run sequentially");
            var random=new Random(5347);for(int i=0;i<200;i++){string input=new string(Enumerable.Range(0,random.Next(80)).Select(_=>"a,\"\r\n"[random.Next(5)]).ToArray());try{var parsed=VaultTransfer.Parse(input,true);foreach(var e in parsed)e.Validate();}catch(InvalidDataException){}catch(FormatException){}}Check(true,"CSV fuzz cases");
            Console.WriteLine(checks+" expansion checks passed, including 200 CSV fuzz inputs.");return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        finally{Core.Data=previous;string resolved=Path.GetFullPath(root);if(resolved.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(resolved).StartsWith("FishTank-expansion-"))Directory.Delete(resolved,true);}
    }
}
