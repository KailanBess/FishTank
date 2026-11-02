using System;
using System.IO;
using System.Net;
using System.Text;

namespace FishTank;

public sealed class ReleaseManifest
{
    public string Version;
    public string Sha256;
    public string PackageUrl;
}
public static class VerifiedUpdateFeed
{
    public static Uri RequireHttps(string url)
    {
        return UpdateUrlPolicy.RequireHttps(url);
    }
    private static HttpWebResponse Open(Uri uri)
    {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        var request = (HttpWebRequest)WebRequest.Create(uri);
        request.AllowAutoRedirect = false; request.UseDefaultCredentials = false;
        request.Timeout = 20000; request.ReadWriteTimeout = 20000;
        var response = (HttpWebResponse)request.GetResponse();
        if (response.StatusCode != HttpStatusCode.OK) { response.Dispose(); throw new IOException("The release host did not return a direct successful response."); }
        return response;
    }
    public static ReleaseManifest Read()
    {
        var prefs = SecurityPreferences.Load();
        var feed = RequireHttps(prefs.UpdateFeedUrl);
        using (var response = Open(feed))
        using (var stream = response.GetResponseStream())
        using (var memory = new MemoryStream())
        {
            CopyBounded(stream, memory, 65536);
            var manifest = Core.Parse<ReleaseManifest>(Encoding.UTF8.GetString(memory.ToArray()));
            if (manifest == null) throw new IOException("Release manifest is empty.");
            DownloadChecksum.Normalize(manifest.Sha256);
            var package = RequireHttps(manifest.PackageUrl);
            if (!string.Equals(package.Host, feed.Host, StringComparison.OrdinalIgnoreCase)) throw new IOException("Release packages must use the same HTTPS host as the configured feed.");
            UpdateVersionPolicy.RequireNewer(manifest.Version, typeof(VerifiedUpdateFeed).Assembly.GetName().Version.ToString(), prefs.HighestStagedVersion);
            return manifest;
        }
    }
    public static string Check()
    {
        var manifest = Read();
        AppActivity.Record("Updates", "Configured release feed checked; installation is separate.");
        return "Release feed advertises version " + manifest.Version + " at " + RequireHttps(manifest.PackageUrl).Host + ". The package is NOT verified yet. Use Download and verify to check its checksum, publisher signature and version before staging.";
    }
    public static string DownloadAndStage()
    {
        var manifest = Read();
        AppActivity.Record("Updates", "Configured release feed checked; installation is separate.");
        if (string.IsNullOrEmpty(SecurityPreferences.Load().PublisherCertificateSha256)) throw new InvalidOperationException("Configure the trusted publisher pin first; no package was downloaded.");
        string temp = Path.Combine(Core.Data, "release-download-" + Guid.NewGuid().ToString("N") + ".exe");
        try
        {
            using (var response = Open(RequireHttps(manifest.PackageUrl)))
            using (var stream = response.GetResponseStream())
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { CopyBounded(stream, file, 128L * 1024 * 1024); file.Flush(true); }
            return TrustedDownloads.StageUpdate(temp, manifest.Sha256, manifest.Version);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static void CopyBounded(Stream source, Stream destination, long maximum)
    {
        TransferLimits.CopyBounded(source, destination, maximum);
    }
}
