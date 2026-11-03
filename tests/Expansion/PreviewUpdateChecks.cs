using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using FishTank;

internal static class PreviewUpdateChecks
{
    internal static void Run(Action<bool,string> check)
    {
        Action<Action,string> reject = (action, name) => { bool rejected = false; try { action(); } catch (IOException) { rejected = true; } catch (ArgumentException) { rejected = true; } catch (InvalidOperationException) { rejected = true; } check(rejected, name); };
        check(!GitHubPreviewUpdates.Allowed(new UpdateChannelPreferences()), "Unsigned previews enabled by default");
        var consent = new UpdateChannelPreferences { Channel = "Preview", AllowUnsignedPreviewUpdates = true };
        var release = new GitHubRelease { id = 9001, tag_name = "v99.0.0", prerelease = true };
        string fixture = Path.Combine(Core.Data, "preview-fixtures"); Directory.CreateDirectory(fixture);
        var staged = new System.Collections.Generic.List<StagedExecutable>();
        var assets = new System.Collections.Generic.List<GitHubAsset>();
        foreach (string name in new[] { "FishTank", "FishTank.Cli" }) {
            string file = name + ".exe";
            var identity = new AssemblyName(name) { Version = new Version(99, 0, 0, 0) };
            var assembly = AppDomain.CurrentDomain.DefineDynamicAssembly(identity, AssemblyBuilderAccess.Save, fixture);
            var module = assembly.DefineDynamicModule(name, file); module.DefineType("InertFixture", TypeAttributes.Public).CreateType(); assembly.Save(file);
            string path = Path.Combine(fixture, file);
            var asset = new GitHubAsset { id = assets.Count + 101, name = file, size = new FileInfo(path).Length, digest = "sha256:" + DownloadChecksum.Compute(path), browser_download_url = "https://github.com/KailanBess/FishTank/releases/download/v99.0.0/" + file };
            assets.Add(asset);
        }
        release.assets = assets.ToArray();
        reject(() => GitHubPreviewUpdates.ValidateRelease(release, new UpdateChannelPreferences { Channel = "Preview" }), "Preview channel alone bypasses unsigned opt-in");
        reject(() => GitHubPreviewUpdates.ValidateRelease(release, new UpdateChannelPreferences { Channel = "Stable", AllowUnsignedPreviewUpdates = true }), "Stable channel accepts unsigned previews");
        release.draft = true; reject(() => GitHubPreviewUpdates.ValidateRelease(release, consent), "Draft preview accepted"); release.draft = false;
        release.prerelease = false; reject(() => GitHubPreviewUpdates.ValidateRelease(release, consent), "Stable release treated as unsigned preview"); release.prerelease = true;
        GitHubPreviewUpdates.ValidateRelease(release, consent); check(true, "Valid preview metadata rejected");
        foreach (var asset in assets) staged.Add(GitHubPreviewUpdates.Stage(Path.Combine(fixture, asset.name), asset, "99.0.0.0"));
        check(staged.All(x => x.PublisherPin == "" && x.Path.Contains("github-preview-updates") && File.Exists(x.Path)), "Preview stage mixed with publisher-verified files");
        var pending = new PendingGitHubUpdate { TrustMode = GitHubPreviewUpdates.Mode, Repository = GitHubReleasePolicy.Repository, ReleaseId = release.id, ReleaseTag = release.tag_name, Version = "99.0.0.0", Files = staged.ToArray() };
        GitHubPreviewUpdates.ValidatePending(pending, release, consent); check(true, "Valid staged preview provenance rejected");
        pending.Repository = "other/repository"; reject(() => GitHubPreviewUpdates.ValidatePending(pending, release, consent), "Different preview repository accepted"); pending.Repository = GitHubReleasePolicy.Repository;
        assets[0].id++; reject(() => GitHubPreviewUpdates.ValidatePending(pending, release, consent), "Changed GitHub asset identity accepted"); assets[0].id--;
        string digest = assets[0].digest; assets[0].digest = "sha256:" + new string('A', 64); reject(() => GitHubPreviewUpdates.ValidatePending(pending, release, consent), "Changed GitHub digest accepted"); assets[0].digest = digest;
        pending.ReleaseTag = "v98.0.0"; reject(() => GitHubPreviewUpdates.ValidatePending(pending, release, consent), "Changed release tag accepted"); pending.ReleaseTag = release.tag_name;
        reject(() => GitHubPreviewUpdates.ValidatePending(pending, release, new UpdateChannelPreferences()), "Revoked unsigned consent ignored");
        GitHubPreviewUpdates.Recheck(staged[0], pending.Version); check(true, "Valid preview bytes could not be rechecked");
        SecurityPreferences.Update(p => p.HighestStagedVersion = "100.0.0.0");
        reject(() => GitHubPreviewUpdates.Recheck(staged[0], pending.Version), "Preview below the highest staged version was accepted");
        SecurityPreferences.Update(p => p.HighestStagedVersion = "99.0.0.0");
        string originalPath = staged[0].Path; staged[0].Path = Path.Combine(fixture, assets[0].name); reject(() => GitHubPreviewUpdates.Recheck(staged[0], pending.Version), "Preview escaped its staging directory"); staged[0].Path = originalPath;
        File.AppendAllText(originalPath, "tampered fixture"); reject(() => GitHubPreviewUpdates.Recheck(staged[0], pending.Version), "Tampered preview accepted before installation");
        reject(() => GitHubPreviewUpdates.Stage(Path.Combine(fixture, assets[0].name), assets[0], "0.1.0.0"), "Older/incorrect preview version accepted");
        SecurityPreferences.Update(p => p.HighestStagedVersion = "0.0.0.0");
        UpdateChannelPreferences.Save(consent); check(GitHubPreviewUpdates.Allowed(UpdateChannelPreferences.Load()), "Preview consent did not persist");
        UpdateChannelPreferences.Save(new UpdateChannelPreferences());
    }
}
