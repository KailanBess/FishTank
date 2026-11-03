using System;
using System.IO;
using System.Linq;
using System.Net;
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
            LiveReliabilityChecks.Run(Check);
            PreviewUpdateChecks.Run(Check);
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
            var now=DateTime.UtcNow;
            var health=VaultHealth.Assess(DemoSamples.Entries(now),now);Check(health.Count==2&&health.All(x=>x.Finding.Contains("Reused")),"Vault health did not group reused passwords");Check(health.Any(x=>x.Finding.Contains("Expired"))&&health.Any(x=>x.Finding.Contains("within 14")),"Expiry findings missing");Check(!Core.Json(health).Contains("demo-only"),"Health dashboard exposed passwords");
            Check(VaultHealth.Assess(new[]{new PasswordEntry{Title="Pattern fixture",Password="passwordpassword"}},now).Single().Finding.Contains("Repeated"),"Repeated long password pattern was missed");
            Check(DemoSamples.Simulate("scan").StartsWith("SIMULATED")&&DemoSamples.Simulate("restore").Contains("No real files"),"Demo actions not explicitly simulated");
            Reject<ArgumentException>(()=>DemoSamples.Simulate("real-scan"),"Unknown demo action accepted");
            string tempName="github-download-"+Guid.NewGuid().ToString("N")+".exe",tempPath=Path.Combine(root,tempName);File.WriteAllText(tempPath,"fixture download");File.SetLastWriteTimeUtc(tempPath,now.AddDays(-10));
            string unrelated=Path.Combine(root,"keep-me.tmp");File.WriteAllText(unrelated,"unrelated fixture");File.SetLastWriteTimeUtc(unrelated,now.AddDays(-10));
            var cleanup=StorageCleanup.Preview(now);Check(cleanup.Count==1&&cleanup[0].Name==tempName,"Cleanup included unrecognized or vault files");
            File.WriteAllText(tempPath,"modified fixture");File.SetLastWriteTimeUtc(tempPath,cleanup[0].ModifiedUtc);Reject<IOException>(()=>StorageCleanup.Delete(cleanup[0],now),"Changed cleanup file deleted");
            cleanup=StorageCleanup.Preview(now);StorageCleanup.Delete(cleanup.Single(),now);Check(!File.Exists(tempPath)&&File.Exists(unrelated)&&File.Exists(PasswordSession.DefaultPath),"Cleanup removed unrelated files or vault");
            Reject<ArgumentException>(()=>StorageCleanup.Delete(new CleanupCandidate{Name="../passwords.ftvault"},now),"Cleanup accepted traversal");
            var bundle=PortableProfileTransfer.Capture(new Appearance());var transferred=PortableProfileTransfer.Parse(System.Text.Encoding.UTF8.GetBytes(Core.Json(bundle)));string importedRoot=Path.Combine(root,"portable-target");string importedProfile=PortableProfileTransfer.Import(transferred,importedRoot,"sample");
            using(var opened=PasswordSession.Open(Path.Combine(importedProfile,"passwords.ftvault"),"Fixture-only vault passphrase 2026!"))Check(opened.Entries.Count==1,"Transferred encrypted vault failed authentication");
            Check(!Directory.GetFiles(importedProfile).Any(x=>x.EndsWith(".dpapi"))&&PortableProfileTransfer.Scope.Contains("Reenroll"),"Profile transfer included Windows-bound secrets");
            Reject<IOException>(()=>PortableProfileTransfer.Import(transferred,importedRoot,"sample"),"Import replaced an existing profile");
            transferred.VaultSha256=new string('0',64);Reject<InvalidDataException>(()=>PortableProfileTransfer.Validate(transferred),"Corrupt transfer checksum accepted");
            string watch=Path.Combine(root,"watch");Directory.CreateDirectory(watch);AutomaticBackups.Save(new CipherBackupPlan{Enabled=true,Destination=watch});Check(BackupDestinationWatch.Tick()==null,"Initial available destination alerted as failure");Directory.Delete(watch);Check(BackupDestinationWatch.Tick().Contains("needs attention"),"Disconnected destination did not alert");Check(BackupDestinationWatch.Tick()==null,"Disconnected destination repeated immediately");
            string failure=AutomaticBackups.Tick();Check(failure.Contains("needs attention")&&AutomaticBackups.Load().LastAttemptUtc>DateTime.MinValue,"Unavailable backup failed to persist/throttle its failure");Check(AutomaticBackups.Tick()==null,"Failed scheduled copy retried immediately");Directory.CreateDirectory(watch);Check(BackupDestinationWatch.Tick().Contains("available again"),"Reconnected destination did not report recovery");
            Check(BackupDestinationWatch.Assess(new CipherBackupPlan(),_=>throw new Exception(),(_,__)=>throw new Exception())=="Disabled","Disabled backup watcher accessed destination");
            Check(AppReleaseNotes.IsUnread()&&AppReleaseNotes.Announce()&&!AppReleaseNotes.Announce(),"Release announcements repeat or disappeared");AppReleaseNotes.MarkRead();Check(!AppReleaseNotes.IsUnread(),"Release-note read state not saved");
            Check(ServerConnectionAdvice.Explain(new IOException("fixture",new WebException("fixture",WebExceptionStatus.TrustFailure))).Contains("certificate"),"TLS setup guidance missing");
            Reject<InvalidOperationException>(()=>ProtectionChanges.ValidateTransition(new ProtectionState{Ready=true,Value=0},1,true),"Block bypassed audit-first transition");
            Reject<InvalidOperationException>(()=>ProtectionChanges.ValidateTransition(new ProtectionState{Ready=true,Value=2},1,false),"Unreviewed audit allowed block");
            Reject<InvalidOperationException>(()=>ProtectionChanges.ValidateTransition(new ProtectionState{Ready=false,Value=2,Reason="Fixture unsupported"},1,true),"Unknown prerequisites allowed change");
            Reject<InvalidOperationException>(()=>ProtectionChanges.ValidateTransition(new ProtectionState{Ready=true,Value=6},2,true),"Warn enforcement weakened to audit");
            ProtectionChanges.ValidateTransition(new ProtectionState{Ready=true,Value=2},1,true);Check(true,"Reviewed audit transition rejected");
            Reject<ArgumentException>(()=>CoverageCatalog.ValidateControl("ASR","invalid"),"Unknown ASR identifier accepted");
            var partial=CoverageCollector.Capture("Hardware",_=>Core.Json(new[]{new EvidenceItem{Name="Fixture",State="Observed",Value="PARTIAL: fixture"}}));Check(partial.Items.Single().State=="Unknown","Partial coverage lost unknown state");
            Reject<InvalidDataException>(()=>CoverageCollector.Capture("Hardware",_=>"[]"),"Empty collector output treated as complete");
            var monitoring=new CoverageMonitorPreferences();CoverageMonitorPreferences.Save(monitoring);int captures=0;Check(CoverageMonitor.Tick(_=>{captures++;throw new Exception();}).Count==0&&captures==0,"Disabled monitoring collected data");
            monitoring.Persistence=true;monitoring.IntervalMinutes=5;CoverageMonitorPreferences.Save(monitoring);
            Func<string,string,EvidenceSnapshot> sample=(category,value)=>new EvidenceSnapshot{Category=category,CapturedUtc=DateTime.UtcNow,Items=new System.Collections.Generic.List<EvidenceItem>{new EvidenceItem{Name="FixtureService",State="Observed",Value=value}}};
            Check(CoverageMonitor.Tick(area=>sample(area,"old")).Count==0,"First inventory treated as an established change");
            monitoring=CoverageMonitorPreferences.Load();monitoring.LastPersistenceAttemptUtc=DateTime.MinValue;CoverageMonitorPreferences.Save(monitoring);Check(CoverageMonitor.Tick(area=>sample(area,"new")).Single().Contains("1 recorded changes"),"Persistence change not reported");
            monitoring=CoverageMonitorPreferences.Load();monitoring.LastPersistenceAttemptUtc=DateTime.MinValue;CoverageMonitorPreferences.Save(monitoring);Check(CoverageMonitor.Tick(_=>throw new IOException("Fixture collector denied")).Single().Contains("unknown")&&CoverageMonitorPreferences.Load().LastResult.Contains("failed"),"Failed monitor did not retain unknown status");
            Check(CoverageMonitor.Tick(_=>throw new Exception()).Count==0,"Failed monitoring retried immediately");
            monitoring=CoverageMonitorPreferences.Load();monitoring.LastPersistenceAttemptUtc=DateTime.MinValue;CoverageMonitorPreferences.Save(monitoring);CoverageMonitor.Tick(area=>new EvidenceSnapshot{Category=area,CapturedUtc=DateTime.UtcNow,Items=new System.Collections.Generic.List<EvidenceItem>{new EvidenceItem{Name="Gap",State="Unknown",Value="Fixture partial"}}});
            monitoring=CoverageMonitorPreferences.Load();monitoring.LastPersistenceAttemptUtc=DateTime.MinValue;CoverageMonitorPreferences.Save(monitoring);var partialNotice=CoverageMonitor.Tick(area=>new EvidenceSnapshot{Category=area,CapturedUtc=DateTime.UtcNow,Items=new System.Collections.Generic.List<EvidenceItem>{new EvidenceItem{Name="Gap",State="Unknown",Value="Fixture partial"},new EvidenceItem{Name="New observation",State="Observed",Value="Fixture"}}});Check(partialNotice.Single().Contains("1 newly observed"),"Partial coverage suppressed a newly observed persistence item");
            var incident=IncidentWorkspace.Create("Fixture incident");IncidentWorkspace.Preserve(incident.Id,"Fixture snapshot",partial);IncidentWorkspace.ConfirmStep(incident.Id,"Isolation reviewed");var saved=IncidentWorkspace.Load(incident.Id);Check(saved.Evidence.Count==1&&saved.Timeline.Last().Detail.Contains("Operator-reported") && saved.Timeline.Last().Detail.Contains("not independently"),"Incident confirmations presented as observed facts");
            IncidentWorkspace.Update(incident.Id,"Fixture notes",true);Check(IncidentWorkspace.Load(incident.Id).Closed&&IncidentWorkspace.Export(incident.Id).Contains("Fixture notes"),"Case workflow/export failed");
            Reject<ArgumentException>(()=>IncidentWorkspace.Load("../escape"),"Case path traversal accepted");
            saved=IncidentWorkspace.Load(incident.Id);saved.Evidence[0].Json="tampered fixture";ProtectedStore.Write("incident-case-"+saved.Id,saved);Reject<IOException>(()=>IncidentWorkspace.Export(incident.Id),"Changed preserved evidence exported without detection");
            Console.WriteLine(checks+" expansion checks passed, including 200 CSV fuzz inputs.");return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        finally{Core.Data=previous;string resolved=Path.GetFullPath(root);if(resolved.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(resolved).StartsWith("FishTank-expansion-"))Directory.Delete(resolved,true);}
    }
}
