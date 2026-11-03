using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
namespace FishTank;
public sealed partial class Desktop
{
    private static Button UtilityAsyncButton(string title,Func<Task> action)
    {
        var button=new Button{Text=title,Height=40,Width=680};button.Click+=async delegate{button.Enabled=false;try{await action();}catch(Exception ex){if(!button.IsDisposed)MessageBox.Show(button.FindForm(),ex.Message,"Could not complete action");}finally{if(!button.IsDisposed)button.Enabled=true;}};return button;
    }
    private void OpenPrivacyCenter()
    {
        using(var dialog=UtilityDialog("Privacy, permissions and history",out var panel))
        {
            Help(panel,"Choose how long to retain notification history. Older application audit days can be previewed and deleted selectively below; today's active audit and all vault/backup files are excluded.");
            var prefs=WorkspacePreferences.Load();var days=new NumericUpDown{Minimum=1,Maximum=365,Value=prefs.HistoryDays,AccessibleName="History retention in days"};var list=new ListBox{Height=200,AccessibleName="Older application audit files"};
            Action refresh=()=>{list.Items.Clear();foreach(var file in HistoryPrivacy.Candidates((int)days.Value))list.Items.Add(file.Name);};panel.Controls.Add(days);panel.Controls.Add(list);panel.Controls.Add(UtilityButton("Preview older history days",refresh));
            panel.Controls.Add(UtilityButton("Save notification retention",()=>WorkspacePreferences.Update(p=>p.HistoryDays=(int)days.Value)));
            var prune=new CheckBox{Text="Automatically delete older audit days using this age (off by default)",AutoSize=true,Checked=prefs.AutoDeleteAuditHistory};panel.Controls.Add(prune);
            panel.Controls.Add(UtilityButton("Save automatic audit-history retention",()=>{WorkspacePreferences.Update(p=>{p.HistoryDays=(int)days.Value;p.AutoDeleteAuditHistory=prune.Checked;});dialog.Close();}));
            panel.Controls.Add(UtilityButton("Clear local authentication history",()=>{if(MessageBox.Show(dialog,"Delete the local authentication history and its previous local store copy? This does not revoke access or delete server authentication events.","Clear authentication history",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;ProtectedStore.Write("authentication-history",new System.Collections.Generic.List<AuthenticationEvent>());string backup=Path.Combine(Core.Data,"authentication-history.dpapi.bak");if(File.Exists(backup))File.Delete(backup);}));
            panel.Controls.Add(UtilityButton("Delete selected older audit day",()=>{if(list.SelectedItem==null)return;if(MessageBox.Show(dialog,"Permanently delete this older application audit day? Vaults and backups are not deleted.","Delete selected history",MessageBoxButtons.YesNo)==DialogResult.Yes){HistoryPrivacy.DeleteSelected((string)list.SelectedItem);refresh();}}));
            panel.Controls.Add(UtilityButton("Review Windows camera, microphone and location permissions",()=>{dialog.Close();StartWork(()=>SecurityReviews.Run("Permissions",false),"Message");}));panel.Controls.Add(UtilityButton("Preview diagnostic/support data",OpenSupportReport));panel.Controls.Add(UtilityButton("Notification history and quiet hours",OpenNotificationCenter));refresh();dialog.ShowDialog(this);
        }
    }
    private Appearance appearanceUndo;
    private void ApplyWorkspaceAppearance(Appearance next){next.Validate();appearanceUndo=appearance.Copy();AppearanceStore.Save(next);appearance=next;BuildShell();}
    private void OpenProfilesAndLayouts()
    {
        using(var dialog=UtilityDialog("Profiles, saved layouts and appearance transfer",out var panel))
        {
            Help(panel,"Named profiles use separate data folders and app authentication. Switching starts a new window; finish active work first. Migration copies the encrypted vault only, leaving existing MFA, tokens and Windows-bound records in the original profile.");
            var name=new TextBox{AccessibleName="Profile or layout name"};var layouts=new ListBox{Height=140,AccessibleName="Saved layouts"};Action refresh=()=>{layouts.Items.Clear();foreach(var row in LayoutStore.Read())layouts.Items.Add(row.Name);};panel.Controls.Add(name);panel.Controls.Add(layouts);
            panel.Controls.Add(UtilityButton("Save current layout",()=>{LayoutStore.Save(name.Text,appearance);refresh();}));panel.Controls.Add(UtilityButton("Apply selected layout",()=>{if(layouts.SelectedItem==null)return;var next=LayoutStore.Read().Single(x=>x.Name==(string)layouts.SelectedItem).Appearance;dialog.Close();ApplyWorkspaceAppearance(next);}));
            panel.Controls.Add(UtilityButton("Undo last layout change",()=>{if(appearanceUndo==null)throw new InvalidOperationException("No layout change to undo in this session.");var previous=appearanceUndo;appearanceUndo=null;dialog.Close();AppearanceStore.Save(previous);appearance=previous;BuildShell();}));
            panel.Controls.Add(UtilityButton("Use compact layout",()=>{var next=appearance.Copy();next.Density="Compact";dialog.Close();ApplyWorkspaceAppearance(next);}));
            panel.Controls.Add(UtilityButton("Use large controls and strong keyboard focus",()=>{var next=appearance.Copy();next.Density="Roomy";next.TextScale=150;next.StrongFocus=true;next.FocusWidth=3;dialog.Close();ApplyWorkspaceAppearance(next);}));
            var shortcutLetter=new TextBox{MaxLength=1,AccessibleName="Custom shortcut letter, Control plus Alt plus A to Z"};var shortcutTool=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,AccessibleName="Tool opened by shortcut"};foreach(var tool in ToolCatalog.All)shortcutTool.Items.Add(tool.Title);shortcutTool.SelectedIndex=0;
            Help(panel,"Custom shortcuts use Ctrl+Alt plus a letter. Existing lock and navigation shortcuts remain available.");panel.Controls.Add(shortcutLetter);panel.Controls.Add(shortcutTool);panel.Controls.Add(UtilityButton("Assign shortcut",()=>ShortcutStore.Set(shortcutLetter.Text,ToolCatalog.All[shortcutTool.SelectedIndex].Id)));panel.Controls.Add(UtilityButton("Review assigned shortcuts",()=>MessageBox.Show(dialog,string.Join("\r\n",ShortcutStore.Read().Select(x=>"Ctrl+Alt+"+x.Keys+" → "+x.Tool)))));panel.Controls.Add(UtilityButton("Reset custom shortcuts",()=>ProtectedStore.Write("keyboard-shortcuts",new System.Collections.Generic.List<ShortcutBinding>())));
            panel.Controls.Add(UtilityButton("Export appearance settings",()=>{var next=appearance.Copy();next.WallpaperPath="";using(var pick=new SaveFileDialog{Filter="Appearance JSON|*.json",FileName="FishTank-appearance.json"})if(pick.ShowDialog(dialog)==DialogResult.OK)File.WriteAllText(pick.FileName,Core.Json(next),new UTF8Encoding(false));}));
            panel.Controls.Add(UtilityButton("Preview and import appearance",()=>{using(var pick=new OpenFileDialog{Filter="Appearance JSON|*.json"})if(pick.ShowDialog(dialog)==DialogResult.OK){var next=LayoutStore.Import(File.ReadAllText(pick.FileName));if(MessageBox.Show(dialog,"Apply theme "+next.Theme+", text size "+next.TextScale+"% and density "+next.Density+"? Imported wallpaper paths are removed.","Appearance preview",MessageBoxButtons.YesNo)==DialogResult.Yes){dialog.Close();ApplyWorkspaceAppearance(next);}}}));
            panel.Controls.Add(UtilityButton("Open named profile",()=>{if(HasRunningOperation)throw new InvalidOperationException("Finish active work first.");string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"FishTank");ProfilePaths.Resolve(root,name.Text);System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(Core.Base,"FishTank.exe"),"--profile "+Core.Quote(name.Text)){UseShellExecute=false});dialog.Close();}));
            panel.Controls.Add(UtilityButton("Copy encrypted vault to a new named profile",()=>{string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"FishTank");string target=ProfilePaths.Resolve(root,name.Text);ProfilePaths.CopyEncryptedVault(Core.Data,target);MessageBox.Show(dialog,"Encrypted vault copied without overwriting an existing vault. Test it with the same master passphrase; the original profile is unchanged.");}));refresh();dialog.ShowDialog(this);
        }
    }
    private void OpenOfflineHelp()
    {
        using(var dialog=UtilityDialog("Offline help and guided troubleshooting",out var panel))
        {
            var search=new TextBox{AccessibleName="Search offline help"};var list=new ListBox{Height=180,AccessibleName="Help articles"};var text=new TextBox{Multiline=true,ReadOnly=true,Height=220,ScrollBars=ScrollBars.Vertical,AccessibleName="Help article text"};
            Action refresh=()=>{list.Items.Clear();foreach(var item in OfflineHelp.Articles.Where(x=>(x.Key+" "+x.Value).IndexOf(search.Text,StringComparison.OrdinalIgnoreCase)>=0))list.Items.Add(item.Key);if(list.Items.Count>0)list.SelectedIndex=0;};search.TextChanged+=delegate{refresh();};list.SelectedIndexChanged+=delegate{if(list.SelectedItem!=null)text.Text=OfflineHelp.Articles[(string)list.SelectedItem];};
            panel.Controls.Add(search);panel.Controls.Add(list);panel.Controls.Add(text);panel.Controls.Add(UtilityButton("Draft a bug report",()=>{using(var draft=UtilityDialog("Bug-report draft; nothing is sent",out var body)){var editor=new TextBox{Multiline=true,Height=360,ScrollBars=ScrollBars.Vertical,AccessibleName="Editable bug report draft",Text="Expected behavior:\r\nActual behavior:\r\nSteps to reproduce:\r\n\r\n"+SupportReport.Build()};body.Controls.Add(editor);body.Controls.Add(UtilityButton("Save reviewed draft",()=>{using(var save=new SaveFileDialog{Filter="Text file|*.txt",FileName="FishTank-bug-report.txt"})if(save.ShowDialog(draft)==DialogResult.OK)File.WriteAllText(save.FileName,editor.Text,new UTF8Encoding(false));}));draft.ShowDialog(dialog);}}));
            panel.Controls.Add(UtilityButton("Create a recovery USB folder",()=>{using(var pick=new FolderBrowserDialog())if(pick.ShowDialog(dialog)==DialogResult.OK){string target=Path.Combine(pick.SelectedPath,"FishTank-recovery-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(target);File.WriteAllText(Path.Combine(target,"RECOVERY.txt"),OfflineHelp.Articles["Recovery USB folder"]+"\r\n\r\n"+OfflineHelp.Articles["Backups and recovery"]);if(File.Exists(PasswordSession.DefaultPath)){byte[] bytes=LocalStoreFiles.ReadOptional(PasswordSession.DefaultPath,16*1024*1024);RestoreFileWriter.WriteNew(Path.Combine(target,"passwords.ftvault"),bytes);}MessageBox.Show(dialog,"Recovery instructions and an available encrypted vault copy were saved. This is not a bootable USB. Keep the master passphrase on separate storage and test recovery.");}}));refresh();dialog.ShowDialog(this);
        }
    }
    private void OpenWindowsIntegration()
    {
        using(var dialog=UtilityDialog("Windows right-click scanning",out var panel))
        {
            Help(panel,"Add an optional per-user right-click entry for files and folders. It opens a confirmed custom scan in FishTank; it does not silently scan or request administrator rights. Remove these entries here before moving a portable installation.");
            panel.Controls.Add(UtilityButton("Add right-click scan entries for this installation",()=>{foreach(string root in new[]{"Software\\Classes\\*\\shell\\FishTank.Scan","Software\\Classes\\Directory\\shell\\FishTank.Scan"}){using(var existing=Registry.CurrentUser.OpenSubKey(root)){if(existing!=null&&!Equals(existing.GetValue("Owner"),"FishTank"))throw new InvalidOperationException("An unowned existing shell entry requires manual review.");}using(var key=Registry.CurrentUser.CreateSubKey(root)){key.SetValue("Owner","FishTank");key.SetValue("","Scan with FishTank");using(var command=key.CreateSubKey("command"))command.SetValue("",Core.Quote(Path.Combine(Core.Base,"FishTank.exe"))+" --scan-file \"%1\"");}}MessageBox.Show(dialog,"Per-user right-click entries added.");}));
            panel.Controls.Add(UtilityButton("Remove FishTank right-click entries",()=>{foreach(string root in new[]{"Software\\Classes\\*\\shell\\FishTank.Scan","Software\\Classes\\Directory\\shell\\FishTank.Scan"}){using(var key=Registry.CurrentUser.OpenSubKey(root)){if(key==null)continue;if(!Equals(key.GetValue("Owner"),"FishTank"))throw new InvalidOperationException("Shell entry ownership did not match.");}Registry.CurrentUser.DeleteSubKeyTree(root,false);}MessageBox.Show(dialog,"FishTank right-click entries removed. Profiles and vaults were not deleted.");}));dialog.ShowDialog(this);
        }
    }
}
