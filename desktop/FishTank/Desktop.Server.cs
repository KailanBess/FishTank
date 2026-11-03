using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace FishTank;
public sealed partial class Desktop
{
    private void OpenPasskeyCenter()
    {
        using(var dialog=UtilityDialog("Server passkeys, security keys and session history",out var panel))
        {
            Help(panel,"Install the FishTank service on your existing HTTPS server before using these controls. Passkeys provide an optional online second factor alongside the app password. Vault decryption still requires its master passphrase. Keep offline MFA and recovery codes.");
            Help(panel,"Current app password (used locally):");var password=new TextBox{UseSystemPasswordChar=true,MaxLength=1024,AccessibleName="Current app password"};panel.Controls.Add(password);
            panel.Controls.Add(UtilityButton("Review local app authentication history",()=>MessageBox.Show(dialog,string.Join("\r\n",AuthenticationHistory.Read().OrderByDescending(x=>x.RecordedUtc).Select(x=>x.RecordedUtc.ToLocalTime().ToString("g")+" · "+x.Context+" · "+(x.Approved?"Approved":"Rejected"))))));
            panel.Controls.Add(UtilityButton("Enroll a passkey or security key",()=>{using(var prompt=new PasskeyDialog(password.Text,"settings",true))if(prompt.ShowDialog(dialog)==DialogResult.OK)MessageBox.Show(dialog,"Server confirmed enrollment. Test it at your next app sign-in while retaining offline recovery access.");}));
            panel.Controls.Add(UtilityButton("Test existing passkey",()=>{using(var prompt=new PasskeyDialog(password.Text,"settings"))if(prompt.ShowDialog(dialog)==DialogResult.OK){bool ok=AppAccess.AuthenticateAt(password.Text,prompt.FactorValue,DateTime.UtcNow,true);MessageBox.Show(dialog,ok?"App password and server passkey verified.":"Verification failed.");}}));
            Help(panel,"Credential ID from the server key list, for revocation:");var key=new TextBox{AccessibleName="Passkey credential ID to revoke"};panel.Controls.Add(key);
            panel.Controls.Add(UtilityAsyncButton("List registered keys and recent server authentications",async()=>{string result=await Task.Run(()=>Core.Json(ServerPasskeys.Call("list",new{})));if(!dialog.IsDisposed)MessageBox.Show(dialog,result,"Server key and authentication history");}));
            panel.Controls.Add(UtilityAsyncButton("Revoke specified passkey (requires settings code)",async()=>{AppAccess.AuthorizeCodeDelivery(password.Text);using(var delivery=new DeliveryChallengeDialog(password.Text,"settings")){if(delivery.ShowDialog(dialog)!=DialogResult.OK)return;string id=key.Text,challenge=delivery.ChallengeId,code=delivery.Code;await Task.Run(()=>ServerPasskeys.Call("remove",new{id,challenge,code}));if(!dialog.IsDisposed)MessageBox.Show(dialog,"Server confirmed revocation.");}}));
            panel.Controls.Add(UtilityButton("Offline authenticator and recovery-code regeneration",()=>{using(var mfa=new MfaSettingsDialog(palette))mfa.ShowDialog(dialog);}));dialog.ShowDialog(this);password.Clear();
        }
    }
    private void OpenDeviceCenter()
    {
        using(var dialog=UtilityDialog("Device inventory and signed health reports",out var panel))
        {
            Help(panel,"Export a signed summary without vault contents, hostnames, user names, raw logs or file paths. A valid signature identifies a key; it does not certify that Windows is secure. Verify its fingerprint through a separate trusted channel. Sequence and recorded time help identify stale reports.");
            panel.Controls.Add(UtilityAsyncButton("Preview and export this device's signed health report",async()=>{var report=await Task.Run(DeviceReports.Create);if(dialog.IsDisposed)return;var summary=DeviceReports.Verify(report,report.Fingerprint);if(MessageBox.Show(dialog,Core.Json(summary)+"\r\nSigning fingerprint: "+report.Fingerprint,"Signed report preview",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;using(var save=new SaveFileDialog{Filter="Signed report JSON|*.json",FileName="FishTank-device-report.json"})if(save.ShowDialog(dialog)==DialogResult.OK)RestoreFileWriter.WriteNew(save.FileName,Encoding.UTF8.GetBytes(Core.Json(report)));}));
            Help(panel,"Independently trusted signing key fingerprint (64 hexadecimal characters):");var fingerprint=new TextBox{AccessibleName="Independently trusted signing key fingerprint"};panel.Controls.Add(fingerprint);
            panel.Controls.Add(UtilityButton("Verify an imported signed report",()=>{using(var pick=new OpenFileDialog{Filter="Signed report JSON|*.json"})if(pick.ShowDialog(dialog)==DialogResult.OK){byte[] bytes=LocalStoreFiles.ReadOptional(pick.FileName,16384)??throw new IOException("Report unavailable.");var report=Core.Parse<SignedDeviceReport>(Encoding.UTF8.GetString(bytes));var summary=DeviceReports.Verify(report,fingerprint.Text.Trim());MessageBox.Show(dialog,Core.Json(summary)+"\r\nSignature matches the supplied fingerprint. Compare sequence and time with your previous report before trusting freshness.");}}));
            Help(panel,"Remote status requires deployment of the server extension and explicit key enrollment using a server-delivered settings code. Only manually published summaries are uploaded. Peer groups are assigned by the server administrator; independently verify peer key fingerprints.");
            Help(panel,"App password for reporting-key enrollment or revocation (used locally):");var password=new TextBox{UseSystemPasswordChar=true,MaxLength=1024,AccessibleName="App password for reporting key enrollment"};panel.Controls.Add(password);
            panel.Controls.Add(UtilityAsyncButton("Enroll this device's reporting key",async()=>{AppAccess.AuthorizeCodeDelivery(password.Text);using(var delivery=new DeliveryChallengeDialog(password.Text,"settings")){if(delivery.ShowDialog(dialog)!=DialogResult.OK)return;var report=await Task.Run(DeviceReports.Create);string challenge=delivery.ChallengeId,code=delivery.Code;await Task.Run(()=>DeviceReports.Call("enroll",new{modulus=report.Modulus,exponent=report.Exponent,challenge,code}));if(!dialog.IsDisposed)MessageBox.Show(dialog,"Reporting key enrolled. Fingerprint: "+report.Fingerprint);}}));
            var sharing=new CheckBox{Text="Allow manual status publishing to my configured server",AutoSize=true,Checked=WorkspacePreferences.Load().RemoteStatusEnabled};sharing.CheckedChanged+=delegate{WorkspacePreferences.Update(p=>p.RemoteStatusEnabled=sharing.Checked);};panel.Controls.Add(sharing);
            panel.Controls.Add(UtilityAsyncButton("Publish a signed summary now",async()=>{await Task.Run(DeviceReports.Publish);if(!dialog.IsDisposed)MessageBox.Show(dialog,"Server acknowledged this signed summary.");}));
            panel.Controls.Add(UtilityAsyncButton("Download available device summaries for review",async()=>{var result=await Task.Run(()=>Core.Json(DeviceReports.Call("list",new{})));if(dialog.IsDisposed)return;using(var save=new SaveFileDialog{Filter="Device summaries JSON|*.json",FileName="FishTank-remote-status.json"})if(save.ShowDialog(dialog)==DialogResult.OK)RestoreFileWriter.WriteNew(save.FileName,Encoding.UTF8.GetBytes(result));}));
            panel.Controls.Add(UtilityAsyncButton("Revoke reporting key and delete its server summary",async()=>{AppAccess.AuthorizeCodeDelivery(password.Text);using(var delivery=new DeliveryChallengeDialog(password.Text,"settings")){if(delivery.ShowDialog(dialog)!=DialogResult.OK)return;string challenge=delivery.ChallengeId,code=delivery.Code;await Task.Run(()=>DeviceReports.Call("revoke",new{challenge,code}));WorkspacePreferences.Update(p=>p.RemoteStatusEnabled=false);if(!dialog.IsDisposed){sharing.Checked=false;MessageBox.Show(dialog,"Server acknowledged reporting-key revocation and summary deletion.");}}}));
            panel.Controls.Add(UtilityButton("Profiles and local inventory",OpenProfilesAndLayouts));dialog.ShowDialog(this);
        }
    }
}
