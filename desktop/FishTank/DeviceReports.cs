using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
namespace FishTank;
public sealed class DeviceSigningIdentity { public string PrivateKey; public long Sequence; public string Id=Guid.NewGuid().ToString("N"); }
public sealed class SignedDeviceReport { public string Payload,Signature,Modulus,Exponent,Fingerprint; }
public sealed class DeviceHealthSummary { public int Version=1; public string Type="FishTank.HealthReport",DeviceId,RecordedUtc,AppVersion,Protection; public long Sequence; }
internal static class DeviceReports
{
    internal static System.Collections.Generic.Dictionary<string,object> Call(string action,object payload)
    {
        var config=SelfHostedMfa.Load()??throw new InvalidOperationException("Deploy and configure your HTTPS FishTank service first.");
        SelfHostedMfa.Validate(config);return SelfHostedMfa.Post(config,"/v1/devices/"+action,payload);
    }
    internal static void Publish()
    {
        if(!WorkspacePreferences.Load().RemoteStatusEnabled)throw new InvalidOperationException("Enable optional status sharing first.");
        var report=Create();var result=Call("publish",new{report});if(!result.ContainsKey("stored")||!(result["stored"] is bool)||!(bool)result["stored"])throw new IOException("Server did not acknowledge status storage.");
    }
    private static readonly object Gate=new object();
    internal static SignedDeviceReport Create()
    {
        lock(Gate)
        {
            using var lease=new FileStream(Path.Combine(Core.Data,"device-signing-identity.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            var identity=ProtectedStore.Read<DeviceSigningIdentity>("device-signing-identity",()=>null);
            using(var rsa=new RSACryptoServiceProvider(3072){PersistKeyInCsp=false})
            {
                if(identity==null)identity=new DeviceSigningIdentity{PrivateKey=rsa.ToXmlString(true)};else rsa.FromXmlString(identity.PrivateKey);
                identity.Sequence=checked(identity.Sequence+1);ProtectedStore.Write("device-signing-identity",identity);
                var summary=new DeviceHealthSummary{DeviceId=identity.Id,Sequence=identity.Sequence,RecordedUtc=DateTime.UtcNow.ToString("o"),AppVersion=typeof(Program).Assembly.GetName().Version.ToString(),Protection=ProtectionOverview.Status(SecurityReviews.Latest("SecurityHealth"),DateTime.UtcNow)};
                if(summary.Protection.Length>1000)summary.Protection=summary.Protection.Substring(0,1000);
                byte[] bytes=Encoding.UTF8.GetBytes(Core.Json(summary));var key=rsa.ExportParameters(false);
                var report=new SignedDeviceReport{Payload=Convert.ToBase64String(bytes),Signature=Convert.ToBase64String(rsa.SignData(bytes,CryptoConfig.MapNameToOID("SHA256"))),Modulus=Convert.ToBase64String(key.Modulus),Exponent=Convert.ToBase64String(key.Exponent)};
                report.Fingerprint=Fingerprint(report);return report;
            }
        }
    }
    internal static string Fingerprint(SignedDeviceReport report){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.ASCII.GetBytes(report.Modulus+":"+report.Exponent))).Replace("-","");}
    internal static DeviceHealthSummary Verify(SignedDeviceReport report,string trustedFingerprint)
    {
        if(report==null || (report.Payload??"").Length>8192 || (report.Signature??"").Length>1024 || (report.Modulus??"").Length>1024 || (report.Exponent??"").Length>12)throw new InvalidDataException("Report exceeds its size limits.");
        if(!DownloadChecksum.Matches(trustedFingerprint,Fingerprint(report)))throw new UnauthorizedAccessException("Signing key fingerprint does not match the independently trusted device key.");
        byte[] bytes=Convert.FromBase64String(report.Payload);
        using(var rsa=new RSACryptoServiceProvider{PersistKeyInCsp=false}){rsa.ImportParameters(new RSAParameters{Modulus=Convert.FromBase64String(report.Modulus),Exponent=Convert.FromBase64String(report.Exponent)});if(rsa.KeySize<2048||!rsa.VerifyData(bytes,CryptoConfig.MapNameToOID("SHA256"),Convert.FromBase64String(report.Signature)))throw new UnauthorizedAccessException("Invalid report signature.");}
        var result=Core.Parse<DeviceHealthSummary>(Encoding.UTF8.GetString(bytes));
        if(result.Version!=1||result.Type!="FishTank.HealthReport"||!Guid.TryParseExact(result.DeviceId,"N",out _)||result.Sequence<1||!DateTime.TryParse(result.RecordedUtc,out _))throw new InvalidDataException("Unrecognized device report.");return result;
    }
}
