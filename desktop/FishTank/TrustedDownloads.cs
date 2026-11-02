using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace FishTank;

public sealed class PublisherSignature
{
    public string Status;
    public string Message;
    public string Subject;
    public string CertificateSha256;
    public bool Timestamped;
}
public static class TrustedDownloads
{
    private static readonly object Gate = new object();
    public static PublisherSignature Signature(string path)
    {
        path = ScanPaths.Normalize(path); ScanPaths.RefuseReparseAncestors(path);
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            string script;
            using (var resource = typeof(TrustedDownloads).Assembly.GetManifestResourceStream("FishTank.Trust.ps1"))
            using (var reader = new StreamReader(resource)) script = reader.ReadToEnd();
            string request = Convert.ToBase64String(Encoding.UTF8.GetBytes(Core.Json(new { Path = path })));
            string command = Convert.ToBase64String(Encoding.Unicode.GetBytes("& {" + script + "\n} -RequestBase64 " + request));
            string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell\\v1.0\\powershell.exe");
            var result = Core.Run(powershell, "-NoProfile -NonInteractive -EncodedCommand " + command, 90000);
            if (result.ExitCode != 0) throw new IOException("Windows signature verification failed. Publisher trust remains unknown.");
            return Core.Parse<PublisherSignature>(result.Output) ?? throw new IOException("No signature evidence returned.");
        }
    }
    public static string Inspect(string path)
    {
        var signature = Signature(path);
        return Core.Json(new { signature, note = "Valid means Windows accepted the Authenticode signature under its current trust policy; it is not a malware verdict. Windows may consult certificate trust/revocation services. Unsigned files have no verified publisher." });
    }
    public static string StageUpdate(string path, string checksum, string expectedVersion = null)
    {
        var staged = StageCore(path, checksum, expectedVersion);
        return "Verified update staged at " + staged.Path + ". It has NOT been run or installed. GitHub pair updates can be installed separately from the GitHub update controls.";
    }
    internal static StagedExecutable StageCore(string path, string checksum, string expectedVersion = null, string expectedName = null)
    {
        lock (Gate)
        {
            checksum = DownloadChecksum.Normalize(checksum);
            var prefs = SecurityPreferences.Load();
            if (string.IsNullOrEmpty(prefs.PublisherCertificateSha256)) throw new InvalidOperationException("Configure a publisher certificate pin from an authenticated release source before staging updates. Current unsigned previews cannot pass this check.");
            path = ScanPaths.Normalize(path); ScanPaths.RefuseReparseAncestors(path);
            using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (!DownloadChecksum.Matches(checksum, DownloadChecksum.Compute(path))) throw new IOException("Update checksum mismatch. Nothing was staged.");
                var signature = Signature(path);
                if (signature.Status != "Valid" || !DownloadChecksum.Matches(prefs.PublisherCertificateSha256, signature.CertificateSha256)) throw new IOException("Update publisher signature or certificate pin was not accepted.");
                var assembly = AssemblyName.GetAssemblyName(path); // Metadata only; never loads executable code.
                if (assembly.Name != "FishTank" && assembly.Name != "FishTank.Cli") throw new IOException("This is not a recognized FishTank assembly.");
                if (expectedName != null && assembly.Name + ".exe" != expectedName) throw new IOException("GitHub update component identity mismatch.");
                string version = assembly.Version.ToString();
                if (expectedVersion != null && version != expectedVersion) throw new IOException("Signed package version differs from the advertised version.");
                UpdateVersionPolicy.RequireNewer(version, typeof(TrustedDownloads).Assembly.GetName().Version.ToString(), prefs.HighestStagedVersion);
                string directory = Path.Combine(Core.Data, "verified-updates", version);
                Directory.CreateDirectory(directory);
                string destination = Path.Combine(directory, assembly.Name + ".exe");
                bool created = false;
                try
                {
                    if (!File.Exists(destination))
                        using (var copy = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { created = true; held.CopyTo(copy); copy.Flush(true); }
                    if (!DownloadChecksum.Matches(checksum, DownloadChecksum.Compute(destination))) throw new IOException("Staged copy did not match; it was discarded.");
                    SecurityPreferences.Update(p => p.HighestStagedVersion = version);
                    return new StagedExecutable { Name = assembly.Name + ".exe", Path = destination, Sha256 = checksum, PublisherPin = signature.CertificateSha256, Version = version };
                }
                catch { if (created && File.Exists(destination)) File.Delete(destination); throw; }
            }
        }
    }
}
