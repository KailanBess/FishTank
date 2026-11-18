using System;
using System.Collections.Generic;
using System.Linq;

namespace FishTank;

public static class SafetyReadiness
{
    // A local review is evidence about configuration, never certification of the product.
    public static string Report()
    {
        var lines = new List<string> { "FishTank safety and recovery readiness", "Version: " + typeof(Core).Assembly.GetName().Version,
            "This report omits paths, account names, server addresses, tokens and vault contents. It does not contact the server or change protection.", "" };
        Check(lines, "App access", () => AppAccess.Enabled ? "Password configured; this does not establish password strength." : "No app password configured. Review app access settings.");
        Check(lines, "Offline recovery", () =>
        {
            var mfa = AppAccess.Mfa;
            return !mfa.Configured ? "Authenticator not enrolled. Review offline MFA before using optional online delivery."
                : mfa.RecoveryCodesRemaining == 0 ? "Authenticator enrolled, but no recovery codes remain. Generate and safely store replacement codes."
                : "Authenticator enrolled and recovery codes remain. Verify recovery access without exposing the codes.";
        });
        Check(lines, "Publisher trust", () => string.IsNullOrEmpty(SecurityPreferences.Load().PublisherCertificateSha256)
            ? "No independently verified publisher pin configured. Preview checksums do not establish publisher identity."
            : "Publisher pin configured. Inspect the installed executable signature separately; a pin alone does not prove a signed installation.");
        Check(lines, "Backup evidence", () =>
        {
            var records = BackupHistory.Observations();
            if (records.Count == 0) return "No FishTank backup observations recorded. Create an encrypted backup and run an isolated restore drill.";
            return "Recorded backup copies: " + records.Count + "; passphrase-verified isolated restore evidence: " + records.Count(r => r.RestoreTestUtc != DateTime.MinValue)
                + ". Check backup age and destination availability in Recovery center. Recorded history is not a fresh restore test.";
        });
        Check(lines, "Optional server", () => SelfHostedMfa.Load() == null
            ? "Not configured. Offline features remain available. Email/SMS requires a configured service and a real delivery test."
            : "Connection settings exist. Run the saved connection test and actual delivery enrollment in the server wizard; saved settings do not prove delivery.");
        lines.Add("");
        lines.Add("External release gates: independent security review, trusted signed install/rollback with interruption recovery, separate Windows account, physical display/assistive-technology testing, outside usability feedback, and real delivery outage/recovery where configured.");
        lines.Add("Automated regression passes do not complete these external gates. Do not rely on a preview for important secrets before independent review and recovery validation.");
        return string.Join(Environment.NewLine + Environment.NewLine, lines);
    }

    private static void Check(List<string> lines, string name, Func<string> read)
    {
        try { lines.Add(name + ": " + read()); }
        catch { lines.Add(name + ": unknown because its protected records could not be read. Existing data was preserved; review Recovery center."); }
    }
}
