using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace FishTank;
internal sealed class PasskeyDialog:Form
{
    internal string FactorValue;
    internal PasskeyDialog(string password,string purpose,bool register=false)
    {
        Text=register?"Enroll a server passkey":"Verify with a passkey or security key";Width=660;Height=370;StartPosition=FormStartPosition.CenterParent;
        var panel=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true};Controls.Add(panel);
        var info=new Label{AutoSize=true,MaximumSize=new System.Drawing.Size(610,0),Text="The system browser opens your configured HTTPS server. Keep your app password and vault passphrase in FishTank. Enrollment also requires an existing server-delivered settings code. Offline MFA remains available."};panel.Controls.Add(info);
        var start=new Button{Text="Open server verification",Width=600,Height=38};var check=new Button{Text="Check completed verification",Width=600,Height=38,Enabled=false};panel.Controls.Add(start);panel.Controls.Add(check);string flow=null;
        start.Click+=async delegate{
            start.Enabled=false;
            try {
                string challenge=null,code=null;
                if(register){using(var delivery=new DeliveryChallengeDialog(password,"settings")){if(delivery.ShowDialog(this)!=DialogResult.OK){start.Enabled=true;return;}challenge=delivery.ChallengeId;code=delivery.Code;}}
                var result=await Task.Run(()=>ServerPasskeys.Start(password,register?"register":"authenticate",purpose,challenge,code));
                if(IsDisposed)return;flow=Convert.ToString(result["flow"]);Process.Start(new ProcessStartInfo(Convert.ToString(result["url"])){UseShellExecute=true});check.Enabled=true;info.Text="Finish the authenticator prompt in your browser, then check here. This request expires in three minutes.";
            }catch(Exception ex){if(!IsDisposed){info.Text=ex.Message;start.Enabled=true;}}
        };
        check.Click+=async delegate{check.Enabled=false;try{var result=await Task.Run(()=>ServerPasskeys.Call("poll",new{flow,purpose}));if(IsDisposed)return;string state=Convert.ToString(result["state"]);if(state=="approved"){if(!register)FactorValue="passkey:"+purpose+":"+flow;DialogResult=DialogResult.OK;Close();}else info.Text="Server verification: "+state+". No authentication has been granted here.";}catch(Exception ex){if(!IsDisposed)info.Text=ex.Message;}finally{if(!IsDisposed)check.Enabled=true;}};
    }
}
