using System;
using System.IO;
namespace FishTank;
public sealed class UpdateChannelPreferences
{
    public string Channel="Stable";
    public DateTime StageNotBeforeUtc;
    internal static UpdateChannelPreferences Load(){var p=ProtectedStore.Read("update-channel",()=>new UpdateChannelPreferences())??throw new InvalidDataException("Update channel unreadable.");if(p.Channel!="Stable"&&p.Channel!="Preview")throw new InvalidDataException("Unsupported update channel.");return p;}
    internal static void Save(UpdateChannelPreferences value){if(value.Channel!="Stable"&&value.Channel!="Preview")throw new ArgumentException("Choose Stable or Preview.");ProtectedStore.Write("update-channel",value);}
}
public sealed partial class Desktop
{
    private void OpenUpdateChannel()
    {
        using(var dialog=UtilityDialog("Update channels and staging window",out var panel))
        {
            Help(panel,"Stable uses the latest published stable release. Preview also considers published prereleases with numeric version tags. Every installation still requires both newer executables, exact SHA-256 digests and the configured publisher certificate pin. Source ZIPs do not qualify. Staging never installs automatically.");
            var prefs=UpdateChannelPreferences.Load();var channel=new System.Windows.Forms.ComboBox{DropDownStyle=System.Windows.Forms.ComboBoxStyle.DropDownList,AccessibleName="Release channel"};channel.Items.AddRange(new object[]{"Stable","Preview"});channel.SelectedItem=prefs.Channel;
            var after=new System.Windows.Forms.DateTimePicker{ShowCheckBox=true,Checked=prefs.StageNotBeforeUtc>DateTime.UtcNow,AccessibleName="Earliest allowed update staging date"};if(after.Checked)after.Value=prefs.StageNotBeforeUtc.ToLocalTime();
            panel.Controls.Add(channel);panel.Controls.Add(after);panel.Controls.Add(UtilityButton("Save channel and staging window",()=>{UpdateChannelPreferences.Save(new UpdateChannelPreferences{Channel=(string)channel.SelectedItem,StageNotBeforeUtc=after.Checked?after.Value.ToUniversalTime():DateTime.MinValue});dialog.Close();}));panel.Controls.Add(UtilityButton("Open verified updates and rollback evidence",()=>OpenToolsTab("Downloads and updates")));dialog.ShowDialog(this);
        }
    }
}
