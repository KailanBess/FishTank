using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
namespace FishTank;
public sealed partial class Desktop
{
    internal void OfferShellScan(string path)
    {
        if (MessageBox.Show(this, "Add this target to the custom scan queue?\r\n" + path, "Right-click scan", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        ScanQueueStore.Add("Custom", path); OpenQueueCenter();
    }
    private void OpenToolsHub()
    {
        using(var dialog=UtilityDialog("All tools, favorites and recent",out var panel))
        {
            var search=new TextBox{AccessibleName="Search all FishTank tools"};var filter=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,AccessibleName="Tool collection"};filter.Items.AddRange(new object[]{"All tools","Favorites","Recent"});filter.SelectedIndex=0;
            var list=new ListBox{Height=320,AccessibleName="Matching tools"};var matches=new List<ToolDefinition>();
            Action refresh=()=>{var prefs=WorkspacePreferences.Load();matches=ToolCatalog.Search(search.Text).Where(x=>filter.SelectedIndex==0||(filter.SelectedIndex==1?prefs.Favorites:prefs.Recent).Contains(x.Id)).ToList();if(filter.SelectedIndex==2)matches=matches.OrderBy(x=>Array.IndexOf(prefs.Recent,x.Id)).ToList();list.Items.Clear();foreach(var item in matches)list.Items.Add((prefs.Favorites.Contains(item.Id)?"★ ":"")+item.Title);if(list.Items.Count>0)list.SelectedIndex=0;};
            Action open=()=>{if(list.SelectedIndex<0)return;var id=matches[list.SelectedIndex].Id;dialog.Close();OpenTool(id);};search.TextChanged+=delegate{refresh();};filter.SelectedIndexChanged+=delegate{refresh();};list.DoubleClick+=delegate{open();};
            Help(panel,"Search by task, or keep your most-used tools in Favorites. Opening a tool does not enable its automatic actions.");panel.Controls.Add(search);panel.Controls.Add(filter);panel.Controls.Add(list);
            panel.Controls.Add(UtilityButton("Open selected tool",open));panel.Controls.Add(UtilityButton("Toggle favorite",()=>{if(list.SelectedIndex<0)return;string id=matches[list.SelectedIndex].Id;WorkspacePreferences.Update(p=>p.Favorites=p.Favorites.Contains(id)?p.Favorites.Where(x=>x!=id).ToArray():p.Favorites.Concat(new[]{id}).ToArray());refresh();}));refresh();dialog.ShowDialog(this);
        }
    }
    private void OpenTool(string id)
    {
        try{ToolCatalog.Record(id);}catch(Exception ex){SetStatus("Recent-tool history could not be saved: "+ex.Message);}
        switch(id)
        {
            case "search":OpenToolsHub();break;case "notices":OpenNotificationCenter();break;case "backups":OpenBackupCenter();break;case "queue":OpenQueueCenter();break;
            case "vault":Navigate("Password Manager");break;case "profiles":OpenProfilesAndLayouts();break;case "devices":OpenDeviceCenter();break;case "passkeys":OpenPasskeyCenter();break;
            case "privacy":OpenPrivacyCenter();break;case "recovery":OpenRecoveryCenter();break;case "help":OpenOfflineHelp();break;case "updates":OpenUpdateChannel();break;
            case "integrations":OpenWindowsIntegration();break;case "accessibility":OpenAccessibilityHelp();break;case "activity":OpenActivityTimeline();break;case "support":OpenSupportReport();break;
            default:throw new ArgumentException("Unknown tool.");
        }
    }
    private void OpenNotificationCenter()
    {
        using(var dialog=UtilityDialog("Notification center",out var panel))
        {
            Help(panel,"Review retained alerts, mark them read, or open the relevant tool. Quiet hours and snooze suppress popups; notices remain available here.");
            var list=new ListBox{Height=250,AccessibleName="Retained notifications",HorizontalScrollbar=true};var items=new List<NoticeRecord>();
            Action refresh=()=>{items=NoticeCenter.Read().OrderByDescending(x=>x.RecordedUtc).ToList();list.Items.Clear();foreach(var n in items)list.Items.Add((n.Read?"":"● ")+n.RecordedUtc.ToLocalTime().ToString("g")+" · "+new[]{"Information","Attention","Urgent"}[Math.Max(0,Math.Min(2,n.Level))]+" · "+n.Summary+(n.Repeats>1?" ("+n.Repeats+" repeats)":""));};
            panel.Controls.Add(list);panel.Controls.Add(UtilityButton("Open selected notice",()=>{if(list.SelectedIndex<0)return;var n=items[list.SelectedIndex];NoticeCenter.Change(n.Id,false);notificationTarget=n.Destination;notificationPending=true;dialog.Close();DispatchNotification();}));
            panel.Controls.Add(UtilityButton("Mark selected read",()=>{if(list.SelectedIndex<0)return;NoticeCenter.Change(items[list.SelectedIndex].Id,false);refresh();}));panel.Controls.Add(UtilityButton("Delete selected notice",()=>{if(list.SelectedIndex<0)return;NoticeCenter.Change(items[list.SelectedIndex].Id,true);refresh();}));
            var prefs=WorkspacePreferences.Load();var quiet=new CheckBox{Text="Enable quiet hours",Checked=prefs.QuietHours,AutoSize=true};var start=new NumericUpDown{Minimum=0,Maximum=23,Value=prefs.QuietStartHour,AccessibleName="Quiet hours start"};var end=new NumericUpDown{Minimum=0,Maximum=23,Value=prefs.QuietEndHour,AccessibleName="Quiet hours end"};var severity=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,AccessibleName="Minimum popup severity"};severity.Items.AddRange(new object[]{"All notices","Attention and urgent","Urgent only"});severity.SelectedIndex=prefs.NoticeLevel;
            panel.Controls.Add(quiet);Help(panel,"Start hour, end hour (local time), and minimum popup severity:");panel.Controls.Add(start);panel.Controls.Add(end);panel.Controls.Add(severity);
            panel.Controls.Add(UtilityButton("Save notification preferences",()=>{WorkspacePreferences.Update(p=>{p.QuietHours=quiet.Checked;p.QuietStartHour=(int)start.Value;p.QuietEndHour=(int)end.Value;p.NoticeLevel=severity.SelectedIndex;});dialog.Close();}));
            panel.Controls.Add(UtilityButton("Snooze popups for one hour",()=>{WorkspacePreferences.Update(p=>p.SnoozeUntilUtc=DateTime.UtcNow.AddHours(1));dialog.Close();}));panel.Controls.Add(UtilityButton("End snooze",()=>WorkspacePreferences.Update(p=>p.SnoozeUntilUtc=DateTime.MinValue)));refresh();dialog.ShowDialog(this);
        }
    }
    private void OpenQueueCenter()
    {
        ScanQueueStore.Recover();
        using(var dialog=UtilityDialog("Scan queue, profiles and comparisons",out var panel))
        {
            Help(panel,"Queue scans to run in order while FishTank is open. Custom targets request no remediation; Defender real-time policy can still act. Interrupted items are retained for review and are not silently restarted.");
            var list=new ListBox{Height=220,AccessibleName="Scan queue",HorizontalScrollbar=true};var items=new List<QueuedScan>();bool stop=false;
            Action refresh=()=>{items=ScanQueueStore.Read();list.Items.Clear();foreach(var x in items)list.Items.Add(x.Kind+" · "+x.State+" · "+(x.Path??"This PC")+" · "+x.Result);};
            panel.Controls.Add(list);panel.Controls.Add(UtilityButton("Queue quick scan",()=>{ScanQueueStore.Add("Quick",null);refresh();}));panel.Controls.Add(UtilityButton("Queue full scan",()=>{ScanQueueStore.Add("Full",null);refresh();}));
            panel.Controls.Add(UtilityButton("Queue a file",()=>{using(var pick=new OpenFileDialog())if(pick.ShowDialog(dialog)==DialogResult.OK){ScanQueueStore.Add("Custom",pick.FileName);refresh();}}));panel.Controls.Add(UtilityButton("Queue a folder or external drive",()=>{using(var pick=new FolderBrowserDialog())if(pick.ShowDialog(dialog)==DialogResult.OK){ScanQueueStore.Add("Custom",pick.SelectedPath);refresh();}}));
            panel.Controls.Add(UtilityAsyncButton("Start pending queue",async ()=>{if(HasRunningOperation && !ScanQueueStore.Running)throw new InvalidOperationException("Finish the active operation first.");if(ScanQueueStore.Running)return;stop=false;await ScanQueueStore.RunAsync(async job=>{var operation=foregroundScan=new ScanOperation(job.Kind,job.Path);var run=operation.RunAsync();ShowScanProgress(operation,dialog);await run;if(operation.Phase.StartsWith("Failed"))throw new IOException(operation.Result);return operation.Result;},()=>stop);if(!dialog.IsDisposed)refresh();}));
            panel.Controls.Add(UtilityButton("Stop after the active item",()=>stop=true));panel.Controls.Add(UtilityButton("Remove selected queue item",()=>{if(list.SelectedIndex>=0){ScanQueueStore.Remove(items[list.SelectedIndex].Id);refresh();}}));
            panel.Controls.Add(UtilityButton("Saved detailed scan profiles",()=>{using(var scans=new ScanDialog(palette))scans.ShowDialog(dialog);}));
            panel.Controls.Add(UtilityButton("Compare recent scan results",()=>{var results=ScanQueueStore.Read().Where(x=>x.State=="Completed").OrderByDescending(x=>x.CompletedUtc).Take(2).ToArray();if(results.Length<2)throw new InvalidOperationException("Complete two queued scans first.");MessageBox.Show(dialog,string.Join("\n\n",results.Select(x=>x.CompletedUtc.ToLocalTime().ToString("g")+" · "+x.Kind+" · "+(x.Path??"This PC")+"\n"+x.Result)),"Result comparison; different scopes can produce different results");}));
            panel.Controls.Add(UtilityButton("Review Defender exclusions and scan policy",()=>{dialog.Close();StartWork(()=>Core.Collect("ScanPolicy"),"ScanPolicy");}));
            var media=new CheckBox{Text="Offer a scan when a removable drive is connected",AutoSize=true,Checked=WorkspacePreferences.Load().ExternalDrivePrompts};media.CheckedChanged+=delegate{WorkspacePreferences.Update(p=>p.ExternalDrivePrompts=media.Checked);};panel.Controls.Add(media);refresh();dialog.FormClosing+=delegate(object sender,FormClosingEventArgs e){if(ScanQueueStore.Running && e.CloseReason==CloseReason.UserClosing){e.Cancel=true;MessageBox.Show(dialog,"Stop the pending queue and wait for its active item, or keep browsing with scan progress.");}};dialog.ShowDialog(this);
        }
    }
    private void OpenBackupCenter()
    {
        using(var dialog=UtilityDialog("Automatic encrypted backups",out var panel))
        {
            var plan=AutomaticBackups.Load();Help(panel,"Automatic backups copy the existing encrypted password vault without storing its master passphrase. A matching file hash is not a passphrase-verified restore. Keep the destination on separate storage when possible. Retention removes only unchanged files recorded by FishTank in that destination.");
            var enabled=new CheckBox{Text="Enable automatic ciphertext backups while FishTank is open",AutoSize=true,Checked=plan.Enabled};var path=new TextBox{Text=plan.Destination,AccessibleName="Encrypted backup destination"};var hours=new NumericUpDown{Minimum=1,Maximum=168,Value=plan.IntervalHours,AccessibleName="Backup interval in hours"};var keep=new NumericUpDown{Minimum=1,Maximum=30,Value=plan.Keep,AccessibleName="Number of managed backups to retain"};
            panel.Controls.Add(enabled);panel.Controls.Add(path);panel.Controls.Add(UtilityButton("Choose backup destination",()=>{using(var pick=new FolderBrowserDialog())if(pick.ShowDialog(dialog)==DialogResult.OK)path.Text=pick.SelectedPath;}));Help(panel,"Interval in hours, then number of copies to retain:");panel.Controls.Add(hours);panel.Controls.Add(keep);Help(panel,"Restore-drill reminder interval in days (requires your passphrase when you run the drill):");var drill=new NumericUpDown{Minimum=1,Maximum=365,Value=plan.DrillDays,AccessibleName="Restore-drill reminder interval in days"};panel.Controls.Add(drill);Help(panel,plan.LastResult);
            Action save=()=>{plan.Enabled=enabled.Checked;plan.Destination=path.Text;plan.IntervalHours=(int)hours.Value;plan.Keep=(int)keep.Value;plan.DrillDays=(int)drill.Value;AutomaticBackups.Save(plan);};
            panel.Controls.Add(UtilityButton("Save backup plan",()=>{save();dialog.Close();}));panel.Controls.Add(UtilityButton("Check destination space",()=>{if(!Directory.Exists(path.Text))throw new DirectoryNotFoundException("Choose an existing destination.");AutomaticBackups.RequireSpace(path.Text,16*1024*1024);MessageBox.Show(dialog,"The destination reports enough free space for a bounded vault copy. Permissions and device availability are checked again during copying.");}));
            panel.Controls.Add(UtilityAsyncButton("Back up encrypted vault now",async ()=>{save();string result=await System.Threading.Tasks.Task.Run(AutomaticBackups.CopyNow);if(!dialog.IsDisposed)MessageBox.Show(dialog,result);}));panel.Controls.Add(UtilityButton("Verify a backup or perform an isolated restore drill",()=>OpenToolsTab("Backup and recovery")));
            panel.Controls.Add(UtilityButton("Compare managed backup copies",()=>{var copies=AutomaticBackups.Manifest().OrderByDescending(x=>x.CreatedUtc).Take(2).ToArray();if(copies.Length<2)throw new InvalidOperationException("Record two managed backups first.");MessageBox.Show(dialog,string.Join("\n",copies.Select(x=>x.CreatedUtc.ToLocalTime().ToString("g")+" · "+x.Bytes+" bytes · SHA-256 "+x.Sha256))+"\nMatching hashes mean identical encrypted bytes; different hashes do not identify changed passwords.","Backup comparison");}));dialog.ShowDialog(this);
        }
    }
}
