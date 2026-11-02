using System;

namespace FishTank;

public static class BackupAgePolicy
{
    public static EvidenceItem Assess(string kind, DateTime createdUtc, DateTime verifiedUtc, DateTime nowUtc, int maximumDays)
    {
        if (maximumDays < 1 || maximumDays > 365) throw new ArgumentException("Backup age must be 1–365 days.");
        nowUtc = nowUtc.ToUniversalTime();
        createdUtc = createdUtc.Year == 1 ? DateTime.MinValue : createdUtc.ToUniversalTime();
        verifiedUtc = verifiedUtc.Year == 1 ? DateTime.MinValue : verifiedUtc.ToUniversalTime();
        string verification = verifiedUtc == DateTime.MinValue ? "No successful verification recorded." : "Last successful verification: " + verifiedUtc.ToString("u") + ".";
        string state, description;
        if (createdUtc > nowUtc || verifiedUtc > nowUtc)
        { state = "Unknown"; description = "A recorded backup date is in the future. Check the PC clock before relying on age warnings."; }
        else if (createdUtc == DateTime.MinValue)
        { state = "Unknown"; description = "No app-created backup date is recorded. Other backups may exist; verifying an older file does not establish when it was created."; }
        else
        {
            double age = (nowUtc - createdUtc).TotalDays;
            state = age >= maximumDays ? "Review" : "No issue observed";
            description = "Last recorded app-created copy: " + createdUtc.ToString("u") + " (" + (int)Math.Floor(age) + " days ago). Warning threshold: " + maximumDays + " days."
                + (state == "Review" ? " Create a fresh encrypted backup and test recovery." : " Its recorded copy age is within the selected limit.");
        }
        return new EvidenceItem { Name = kind + " backup age", State = state, Value = description + "\n" + verification
            + "\nBackup age and verification are separate. This does not confirm that the file is still available, on separate storage, or contains every current change." };
    }
}
