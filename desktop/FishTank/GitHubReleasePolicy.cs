using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace FishTank;

public sealed class GitHubAsset
{
    public long id;
    public string name;
    public long size;
    public string digest;
    public string browser_download_url;
}
public sealed class GitHubRelease
{
    public long id;
    public string tag_name;
    public bool draft;
    public bool prerelease;
    public GitHubAsset[] assets;
}
public static class GitHubReleasePolicy
{
    public const string Repository = "KailanBess/FishTank";
    public const string LatestUrl = "https://api.github.com/repos/" + Repository + "/releases/latest";
    public static string Version(string tag)
    {
        if (!Regex.IsMatch(tag ?? "", "\\Av?[0-9]+\\.[0-9]+(?:\\.[0-9]+){0,2}\\z") || !System.Version.TryParse((tag ?? "").TrimStart('v'), out var version))
            throw new ArgumentException("GitHub release tag must be a stable vMajor.Minor.Build.Revision version.");
        return new System.Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision)).ToString();
    }
    public static GitHubAsset Asset(GitHubRelease release, string name)
    {
        if (release == null || release.id <= 0 || release.draft || release.prerelease || release.assets == null || release.assets.Length > 100)
            throw new ArgumentException("No supported public stable GitHub release was returned.");
        Version(release.tag_name);
        if (name != "FishTank.exe" && name != "FishTank.Cli.exe") throw new ArgumentException("Unsupported update component.");
        var matches = release.assets.Where(x => x.name == name).ToArray();
        if (matches.Length != 1) throw new ArgumentException("The release must contain exactly one " + name + " asset.");
        var asset = matches[0];
        string expected = "https://github.com/" + Repository + "/releases/download/" + release.tag_name + "/" + name;
        if (asset.id <= 0 || asset.size < 1 || asset.size > 128L * 1024 * 1024 || asset.browser_download_url != expected
            || !Regex.IsMatch(asset.digest ?? "", "\\Asha256:[a-fA-F0-9]{64}\\z"))
            throw new ArgumentException("GitHub asset identity, size, URL or SHA-256 digest is missing or invalid. No fallback to unverified assets is allowed.");
        return asset;
    }
    public static Uri Redirect(Uri original, string location)
    {
        if (location == null || location.Length > 16384 || !Uri.TryCreate(original, location, out var next) || next.Scheme != "https" || !next.IsDefaultPort || next.UserInfo != "" || next.Fragment != "")
            throw new ArgumentException("Unsafe GitHub asset redirect refused.");
        bool github = next.Host == "github.com" && next.Query == "" && next.AbsolutePath.StartsWith("/" + Repository + "/releases/download/", StringComparison.Ordinal);
        if (!github && next.Host != "release-assets.githubusercontent.com" && next.Host != "objects.githubusercontent.com")
            throw new ArgumentException("GitHub redirected outside the permitted asset hosts.");
        return next;
    }
}
