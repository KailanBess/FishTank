using System;
using System.Collections.Generic;
using System.Linq;

namespace FishTank;

public static class PasswordOrganization
{
    public static string EmailFor(PasswordEntry entry, IEnumerable<PasswordEntry> entries)
        => entry.LinkedEmailId == "" ? entry.Email : entries.FirstOrDefault(e => e.Id == entry.LinkedEmailId && e.Kind == "Email")?.Email ?? "Missing email connection";

    public static IEnumerable<PasswordEntry> Ordered(IEnumerable<PasswordEntry> source)
    {
        var entries = source.ToList();
        foreach (var email in entries.Where(e => e.Kind == "Email").OrderBy(e => e.Email, StringComparer.OrdinalIgnoreCase))
        {
            yield return email;
            foreach (var service in entries.Where(e => e.LinkedEmailId == email.Id).OrderBy(e => e.Title, StringComparer.OrdinalIgnoreCase)) yield return service;
        }
        foreach (var service in entries.Where(e => e.Kind == "Service" && e.LinkedEmailId == "").OrderBy(e => e.Title, StringComparer.OrdinalIgnoreCase)) yield return service;
    }
}
