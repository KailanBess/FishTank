using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
namespace FishTank;
internal static class ServerPasskeys
{
    internal static Dictionary<string,object> Call(string action,object payload)
    {
        var config=SelfHostedMfa.Load()??throw new InvalidOperationException("Install the FishTank verification service on your HTTPS server, then configure its origin and device token in App access → Authenticator and recovery codes.");
        SelfHostedMfa.Validate(config);return SelfHostedMfa.Post(config,"/v1/passkeys/"+action,payload);
    }
    internal static Dictionary<string,object> Start(string password,string mode,string purpose,string challenge=null,string code=null)
    {
        AppAccess.AuthorizeCodeDelivery(password);var config=SelfHostedMfa.Load()??throw new InvalidOperationException("Configure your HTTPS verification server first.");
        var result=Call("start",new{mode,purpose,binding=config.Binding,challenge,code});
        if(!result.ContainsKey("flow") || !Regex.IsMatch(Convert.ToString(result["flow"]),"\\A[A-Za-z0-9_-]{43}\\z") || Convert.ToString(result["binding"])!=config.Binding || Convert.ToString(result["purpose"])!=purpose)throw new IOException("Server ceremony context mismatch.");
        var url=new Uri(Convert.ToString(result["url"]));
        if(url.GetLeftPart(UriPartial.Authority)!=new Uri(config.Origin).GetLeftPart(UriPartial.Authority)||url.AbsolutePath!="/passkeys"||url.Query!=""||!Regex.IsMatch(url.Fragment,"\\A#[A-Za-z0-9_-]{43}\\z"))throw new IOException("Server returned an unexpected ceremony URL.");return result;
    }
    internal static bool Consume(string factor,bool settings)
    {
        var parts=(factor??"").Split(':');string purpose=settings?"settings":"signin";
        if(parts.Length!=3||parts[0]!="passkey"||parts[1]!=purpose||!Regex.IsMatch(parts[2],"\\A[A-Za-z0-9_-]{43}\\z"))return false;
        var config=SelfHostedMfa.Load();if(config==null)return false;
        var result=Call("consume",new{flow=parts[2],purpose});return Convert.ToString(result["state"])=="approved"&&Convert.ToString(result["mode"])=="authenticate"&&Convert.ToString(result["binding"])==config.Binding&&Convert.ToString(result["purpose"])==purpose;
    }
}
