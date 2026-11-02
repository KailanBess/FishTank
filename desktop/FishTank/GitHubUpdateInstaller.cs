using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace FishTank;

internal static class GitHubUpdateInstaller
{
    internal static void Start()
    {
        if (Application.OpenForms.OfType<Desktop>().Any(x => x.HasRunningOperation) || Application.OpenForms.OfType<ProtectedFilesDialog>().Any(x => x.HasRunningOperation)
            || Application.OpenForms.OfType<PasswordManagerDialog>().Any(x => x.HasRunningOperation)) throw new InvalidOperationException("Finish the current scan, file or vault operation before installing an update.");
        var pending = GitHubUpdates.Pending() ?? throw new InvalidOperationException("Download and verify a complete GitHub release first.");
        if (pending.Files == null || pending.Files.Length != 2) throw new IOException("Incomplete GitHub update plan.");
        var prefs = SecurityPreferences.Load();
        var files = pending.Files.Select(item => {
            if (item.Name != "FishTank.exe" && item.Name != "FishTank.Cli.exe") throw new IOException("Unexpected update component.");
            string expected = Path.Combine(Core.Data, "verified-updates", pending.Version, item.Name);
            if (!string.Equals(Path.GetFullPath(item.Path), Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase)) throw new IOException("Update source path changed.");
            var checkedFile = TrustedDownloads.StageCore(item.Path, item.Sha256, pending.Version, item.Name);
            if (!DownloadChecksum.Matches(item.PublisherPin, prefs.PublisherCertificateSha256) || checkedFile.PublisherPin != item.PublisherPin) throw new IOException("Update publisher trust changed. Download and verify it again.");
            string installed = Path.Combine(Core.Base, item.Name);
            string oldVersion = File.Exists(installed) ? AssemblyName.GetAssemblyName(installed).Version.ToString() : "";
            if (oldVersion != "") UpdateVersionPolicy.RequireNewer(pending.Version, oldVersion, prefs.HighestStagedVersion);
            return new { item.Name, item.Path, item.Sha256, item.PublisherPin, InstalledVersion = oldVersion };
        }).ToArray();
        string resultPath = Path.Combine(Core.Data, "github-update-result.json");
        using (var process = Process.GetCurrentProcess())
        {
            var plan = new { pending.Version, Files = files, TargetDirectory = Core.Base.TrimEnd(Path.DirectorySeparatorChar), StageRoot = Path.Combine(Core.Data, "verified-updates"),
                ParentPid = process.Id, ParentStartTicks = process.StartTime.ToUniversalTime().Ticks, ResultPath = resultPath,
                Portable = string.Equals(Path.GetFullPath(Core.Data), Path.Combine(Core.Base, "data"), StringComparison.OrdinalIgnoreCase) };
            string script;
            using (var resource = typeof(GitHubUpdateInstaller).Assembly.GetManifestResourceStream("FishTank.ApplyUpdate.ps1"))
            using (var reader = new StreamReader(resource)) script = reader.ReadToEnd();
            string json = Convert.ToBase64String(Encoding.UTF8.GetBytes(Core.Json(plan)));
            string command = Convert.ToBase64String(Encoding.Unicode.GetBytes("& {" + script + "\n} -PlanBase64 " + json));
            if (command.Length > 29000) throw new IOException("Update plan exceeds Windows command-line limits; use a shorter installation path.");
            File.WriteAllText(resultPath, Core.Json(new { Status = "Pending", pending.Version, Message = "Installation helper is starting. No successful replacement is recorded yet." }));
            string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell\\v1.0\\powershell.exe");
            Process.Start(new ProcessStartInfo(powershell, "-NoProfile -NonInteractive -EncodedCommand " + command) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Core.Base });
        }
        AppAccess.Lock();
        foreach (var vault in Application.OpenForms.OfType<PasswordManagerDialog>().ToArray()) vault.LockForAppLock();
        foreach (var safe in Application.OpenForms.OfType<ProtectedFilesDialog>().ToArray()) safe.LockForAppLock();
        Application.Exit();
    }
    internal static string LastResult()
    {
        string path = Path.Combine(Core.Data, "github-update-result.json");
        if (!File.Exists(path)) return "No GitHub installation result is recorded yet.";
        if (new FileInfo(path).Length > 16384) throw new IOException("Update result is oversized.");
        return File.ReadAllText(path);
    }
}
