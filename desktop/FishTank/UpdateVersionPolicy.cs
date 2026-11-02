using System;

namespace FishTank;

public static class UpdateVersionPolicy
{
    public static void RequireNewer(string candidate, string installed, string highestStaged)
    {
        if (!Version.TryParse(candidate, out var next) || !Version.TryParse(installed, out var current)
            || !Version.TryParse(highestStaged, out var floor) || next <= current || next < floor)
            throw new ArgumentException("Update refused: its version must exceed the installed version and cannot be below the highest previously staged version.");
    }
}
