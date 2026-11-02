using System;
using System.Linq;

namespace FishTank;

internal static class DiskHealthChecks
{
    internal static string Run()
    {
        string report = SecurityReviews.Run("DiskHealth", false);
        SecurityPreferences.Update(p => p.LastDiskHealthCheckUtc = DateTime.UtcNow);
        return report;
    }
    internal static string Notification()
    {
        var snapshot = SecurityReviews.Latest("DiskHealth");
        if (snapshot == null) return "Disk health is unknown: no completed check is recorded.";
        int review = snapshot.Items.Count(x => x.State == "Review"), unknown = snapshot.Items.Count(x => x.State == "Unknown");
        if (review > 0) return "Disk health: " + review + " findings need review; " + unknown + " checks are unknown. Back up important files and open Disk health for the hardware and free-space evidence.";
        return unknown > 0 ? "Disk health: " + unknown + " checks are unknown or unsupported. Review the available hardware evidence; no drive-failure prediction is possible from missing data." : null;
    }
    internal static string CachedWarning()
    {
        var snapshot = SecurityReviews.Latest("DiskHealth");
        if (snapshot != null && (snapshot.CapturedUtc.ToUniversalTime() > DateTime.UtcNow || DateTime.UtcNow - snapshot.CapturedUtc.ToUniversalTime() > TimeSpan.FromHours(SecurityPreferences.Load().DiskCheckHours)))
            return "Disk health is unknown now: the last recorded check is outdated or has a future timestamp. Run a disk-health check; past healthy readings are not current evidence.";
        string warning = Notification();
        if (warning == null) return null;
        return warning + (snapshot == null ? "" : "\nLast recorded check: " + snapshot.CapturedUtc.ToLocalTime().ToString("g") + ". This is past evidence; run a check for current readings.");
    }
}
