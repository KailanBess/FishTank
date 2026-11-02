using System;
using System.Collections.Generic;
using System.IO;
namespace FishTank;
internal static class UpdateRecoveryStatus
{
    internal static string Notice()
    {
        try
        {
            var bytes = LocalStoreFiles.ReadOptional(Path.Combine(Core.Data, "github-update-result.json"), 16384);
            if (bytes == null) return null;
            var record = Core.Parse<Dictionary<string, object>>(System.Text.Encoding.UTF8.GetString(bytes));
            string state = record != null && record.TryGetValue("Status", out var value) ? Convert.ToString(value) : "";
            if (state == "Updated") return null;
            return state == "Pending" ? "An update was interrupted or may still be running. Review the installation result and retained executable backups before trying again."
                : state == "RecoveryRequired" ? "An update rollback was incomplete. Review the retained executable backups in Recovery center before another installation."
                : state == "Failed" ? "The last update failed. Review its installation and rollback result before trying again."
                : "The last update result could not be read reliably. Review installation recovery before trying again.";
        }
        catch { return "The last update result could not be read. Review installation recovery before trying again."; }
    }
}
