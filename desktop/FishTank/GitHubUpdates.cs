using System;
using System.IO;
using System.Net;
using System.Text;

namespace FishTank;

public sealed class StagedExecutable
{
    public string Name;
    public string Path;
    public string Version;
    public string Sha256;
    public string PublisherPin;
}
public sealed class PendingGitHubUpdate
{
    public long ReleaseId;
    public string Version;
    public StagedExecutable[] Files;
}
public static class GitHubUpdates
{
    private static readonly object Gate = new object();
    private static HttpWebResponse Open(Uri url)
    {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        var request = (HttpWebRequest)WebRequest.Create(url);
        request.AllowAutoRedirect = false; request.UseDefaultCredentials = false;
        request.UserAgent = "FishTank/" + typeof(GitHubUpdates).Assembly.GetName().Version;
        request.Accept = url.Host == "api.github.com" ? "application/vnd.github+json" : "application/octet-stream";
        if (url.Host == "api.github.com") request.Headers["X-GitHub-Api-Version"] = "2022-11-28";
        request.Timeout = 20000; request.ReadWriteTimeout = 20000;
        try { return (HttpWebResponse)request.GetResponse(); }
        catch (WebException error)
        {
            var response = error.Response as HttpWebResponse; int status = (int)(response?.StatusCode ?? 0); response?.Dispose();
            throw new IOException(status == 404 ? "No public stable release was found. The repository may be private or have no published release. Source commits and preview ZIPs are not signed releases."
                : status == 403 || status == 429 ? "GitHub access or rate limits prevented this update check. Nothing was installed." : "GitHub could not be reached securely. Nothing was installed.");
        }
    }
    public static GitHubRelease Latest()
    {
        using (var response = Open(new Uri(GitHubReleasePolicy.LatestUrl)))
        using (var data = new MemoryStream())
        {
            if (response.StatusCode != HttpStatusCode.OK) throw new IOException("GitHub release metadata did not return a direct successful response.");
            using (var stream = response.GetResponseStream()) TransferLimits.CopyBounded(stream, data, 1024 * 1024);
            var release = Core.Parse<GitHubRelease>(Encoding.UTF8.GetString(data.ToArray()));
            GitHubReleasePolicy.Asset(release, "FishTank.exe"); GitHubReleasePolicy.Asset(release, "FishTank.Cli.exe");
            return release;
        }
    }
    public static string Check()
    {
        var release = Latest(); string version = GitHubReleasePolicy.Version(release.tag_name);
        var installed = typeof(GitHubUpdates).Assembly.GetName().Version;
        if (System.Version.Parse(version) <= installed) return "GitHub's latest public stable release is " + version + ". No newer stable release was found for installed version " + installed + ".";
        return "GitHub advertises stable release " + version + " from " + GitHubReleasePolicy.Repository + ". It has NOT been verified yet. Download and verify checks both executables against GitHub digests and your trusted publisher pin before an installation can be offered.";
    }
    private static void Download(GitHubAsset asset, string destination)
    {
        var url = new Uri(asset.browser_download_url);
        for (int redirects = 0; redirects <= 5; redirects++)
        {
            using (var response = Open(url))
            {
                int status = (int)response.StatusCode;
                if (status >= 300 && status <= 399) { url = GitHubReleasePolicy.Redirect(url, response.Headers["Location"]); continue; }
                if (response.StatusCode != HttpStatusCode.OK) throw new IOException("GitHub did not return the selected release asset.");
                using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var input = response.GetResponseStream()) { TransferLimits.CopyBounded(input, output, asset.size); output.Flush(true); if (output.Length != asset.size) throw new IOException("GitHub asset length mismatch."); }
                return;
            }
        }
        throw new IOException("GitHub asset exceeded the permitted redirect count.");
    }
    public static string DownloadAndStage()
    {
        lock (Gate)
        {
            var preferences = SecurityPreferences.Load();
            if (string.IsNullOrEmpty(preferences.PublisherCertificateSha256)) throw new InvalidOperationException("Set an independently verified publisher certificate SHA-256 pin in security preferences first. Unsigned previews cannot be installed as trusted updates.");
            var release = Latest(); string version = GitHubReleasePolicy.Version(release.tag_name);
            UpdateVersionPolicy.RequireNewer(version, typeof(GitHubUpdates).Assembly.GetName().Version.ToString(), preferences.HighestStagedVersion);
            var staged = new System.Collections.Generic.List<StagedExecutable>();
            foreach (string name in new[] { "FishTank.exe", "FishTank.Cli.exe" })
            {
                var asset = GitHubReleasePolicy.Asset(release, name);
                string temp = System.IO.Path.Combine(Core.Data, "github-download-" + Guid.NewGuid().ToString("N") + ".exe");
                try { Download(asset, temp); staged.Add(TrustedDownloads.StageCore(temp, asset.digest.Substring(7), version, name)); }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
            ProtectedStore.Write("pending-github-update", new PendingGitHubUpdate { ReleaseId = release.id, Version = version, Files = staged.ToArray() });
            return "Both GitHub release executables passed checksum, publisher and version verification for " + version + ". They are staged; use Install verified GitHub update to close FishTank, replace only its executables, keep rollback copies and restart. User data is not replaced.";
        }
    }
    internal static PendingGitHubUpdate Pending() => ProtectedStore.Read<PendingGitHubUpdate>("pending-github-update", () => null);
}
