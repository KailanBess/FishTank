using System;
using System.IO;
using System.Windows.Forms;
namespace FishTank;
public sealed partial class Desktop
{
    private void OpenReleaseReadiness()
    {
        using (var dialog = UtilityDialog("Signed release readiness", out var panel))
        {
            Help(panel, "Installed version: " + typeof(Core).Assembly.GetName().Version + ". Development preview ZIPs are unsigned. A trusted publisher signature requires the maintainer’s code-signing certificate; FishTank does not create a substitute certificate or bypass signature verification.");
            try { Help(panel, string.IsNullOrEmpty(SecurityPreferences.Load().PublisherCertificateSha256) ? "Publisher trust pin: not configured. Obtain the public certificate SHA-256 fingerprint from an independently trusted publisher source before enabling verified updates." : "Publisher trust pin: configured. Each downloaded update still needs checksum, publisher and version verification."); }
            catch { Help(panel, "Publisher trust preferences could not be read. Existing preferences were preserved."); }
            panel.Controls.Add(UtilityButton("Inspect installed executable signature", () => { dialog.Close(); StartWork(() => TrustedDownloads.Inspect(Path.Combine(Core.Base, "FishTank.exe")), "Message"); }));
            panel.Controls.Add(UtilityButton("Review verified updates and installation results", () => OpenToolsTab("Downloads and updates")));
            panel.Controls.Add(UtilityButton("Configure independently verified publisher pin", () => OpenToolsTab("Locking and reminders")));
            Help(panel, "Maintainer setup: the GitHub release environment needs WINDOWS_SIGNING_PFX_BASE64 and WINDOWS_SIGNING_PASSWORD secrets, plus the WINDOWS_TIMESTAMP_URL variable. Use a stable version tag matching both executable assembly versions. The release workflow validates readiness, signs and verifies both files, and packages the signed bundle with checksums. Credentials belong in GitHub’s encrypted environment secrets, not source files or chat.");
            dialog.ShowDialog(this);
        }
    }
}
