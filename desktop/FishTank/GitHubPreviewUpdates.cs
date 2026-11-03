using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace FishTank;

// Explicitly lower assurance than the separate pinned-publisher update path.
internal static class GitHubPreviewUpdates
{
    internal const string Mode = "GitHubPreview";
    internal static bool Allowed(UpdateChannelPreferences preferences) => preferences != null && preferences.Channel == "Preview" && preferences.AllowUnsignedPreviewUpdates;
    internal static void ValidateRelease(GitHubRelease release, UpdateChannelPreferences preferences)
    {
        if (!Allowed(preferences)) throw new InvalidOperationException("Enable preview updates explicitly before downloading or applying unsigned releases.");
        if (release == null || !release.prerelease) throw new InvalidOperationException("Unsigned preview installation requires a published prerelease, not a stable or draft release.");
        GitHubReleasePolicy.Asset(release, "FishTank.exe", true); GitHubReleasePolicy.Asset(release, "FishTank.Cli.exe", true);
    }
    internal static StagedExecutable Stage(string path, GitHubAsset asset, string version)
    {
        string checksum = DownloadChecksum.Normalize(asset.digest.Substring(7));
        ScanPaths.RefuseReparseAncestors(path);
        using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
            if (input.Length != asset.size || !DownloadChecksum.Matches(checksum, DownloadChecksum.Compute(path))) throw new IOException("GitHub preview length or checksum mismatch.");
            var identity = AssemblyName.GetAssemblyName(path);
            if (identity.Name + ".exe" != asset.name || identity.Version.ToString() != version) throw new IOException("GitHub preview assembly identity/version mismatch.");
            var prefs = SecurityPreferences.Load();
            UpdateVersionPolicy.RequireNewer(version, typeof(Program).Assembly.GetName().Version.ToString(), prefs.HighestStagedVersion);
            string directory = Path.Combine(Core.Data, "github-preview-updates", version);
            Directory.CreateDirectory(directory); ScanPaths.RefuseReparseAncestors(directory);
            AutomaticBackups.RequireSpace(directory, input.Length);
            string destination = Path.Combine(directory, asset.name); bool created = false;
            try {
                if (!File.Exists(destination)) using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { created = true; input.CopyTo(output); output.Flush(true); }
                ScanPaths.RefuseReparseAncestors(destination);
                if (!DownloadChecksum.Matches(checksum, DownloadChecksum.Compute(destination))) throw new IOException("Staged GitHub preview checksum mismatch.");
                SecurityPreferences.Update(p => p.HighestStagedVersion = version);
                return new StagedExecutable { Name = asset.name, Path = destination, Version = version, Sha256 = checksum, PublisherPin = "", GitHubAssetId = asset.id };
            } catch { if (created && File.Exists(destination)) File.Delete(destination); throw; }
        }
    }
    internal static void ValidatePending(PendingGitHubUpdate pending, GitHubRelease release, UpdateChannelPreferences preferences)
    {
        ValidateRelease(release, preferences);
        if (pending == null || pending.TrustMode != Mode || pending.Repository != GitHubReleasePolicy.Repository || pending.ReleaseId != release.id || pending.ReleaseTag != release.tag_name
            || pending.Version != GitHubReleasePolicy.Version(release.tag_name) || pending.Files == null || pending.Files.Length != 2
            || !pending.Files.Select(x => x.Name).OrderBy(x => x).SequenceEqual(new[] { "FishTank.Cli.exe", "FishTank.exe" })) throw new IOException("Preview update provenance is missing or changed.");
        foreach (var file in pending.Files) {
            var asset = GitHubReleasePolicy.Asset(release, file.Name, true);
            if (file.GitHubAssetId != asset.id || !DownloadChecksum.Matches(file.Sha256, asset.digest.Substring(7)) || !string.IsNullOrEmpty(file.PublisherPin)) throw new IOException("GitHub preview asset identity or digest changed. Download it again.");
        }
    }
    internal static StagedExecutable Recheck(StagedExecutable file, string version)
    {
        UpdateVersionPolicy.RequireNewer(version, typeof(Program).Assembly.GetName().Version.ToString(), SecurityPreferences.Load().HighestStagedVersion);
        string expected = Path.Combine(Core.Data, "github-preview-updates", version, file.Name);
        if (!string.Equals(Path.GetFullPath(expected), Path.GetFullPath(file.Path), StringComparison.OrdinalIgnoreCase)) throw new IOException("Preview update source escaped its staging directory.");
        ScanPaths.RefuseReparseAncestors(expected);
        using (var held = new FileStream(expected, FileMode.Open, FileAccess.Read, FileShare.Read)) {
            if (!DownloadChecksum.Matches(file.Sha256, DownloadChecksum.Compute(expected))) throw new IOException("Staged preview checksum mismatch. Nothing was installed.");
            var identity = AssemblyName.GetAssemblyName(expected);
            if (identity.Name + ".exe" != file.Name || identity.Version.ToString() != version) throw new IOException("Staged preview identity/version changed.");
        }
        return file;
    }
}
