using System;
using System.Diagnostics;
using System.IO;

namespace FishTank;

public static class TransferLimits
{
    public static void CopyBounded(Stream source, Stream destination, long maximum)
    {
        var timer = Stopwatch.StartNew();
        var buffer = new byte[8192]; long total = 0; int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > maximum || timer.Elapsed > TimeSpan.FromMinutes(2)) throw new IOException("Download exceeds its size or time limit.");
            destination.Write(buffer, 0, read);
        }
    }
}
