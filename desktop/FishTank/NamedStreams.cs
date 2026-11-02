using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace FishTank;

internal static class NamedStreams
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    private static string PathFor(string path, string name)
    {
        path = ScanPaths.Normalize(path); ScanPaths.RefuseReparseAncestors(path);
        if (!Regex.IsMatch(name ?? "", "\\A[A-Za-z0-9_.-]{1,64}\\z")) throw new ArgumentException("Invalid named-stream identifier.");
        return path + ":" + name;
    }
    internal static void Write(string path, string name, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > 4096) throw new IOException("Metadata stream exceeds its size limit.");
        using (var handle = CreateFile(PathFor(path, name), 0x40000000, 0, IntPtr.Zero, 1, 0x200000, IntPtr.Zero))
        {
            if (handle.IsInvalid) throw new IOException("Windows metadata stream could not be created; existing streams are not overwritten.");
            using (var stream = new FileStream(handle, FileAccess.Write)) { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
        }
    }
    internal static string Read(string path, string name)
    {
        using (var handle = CreateFile(PathFor(path, name), 0x80000000, 1, IntPtr.Zero, 3, 0x200000, IntPtr.Zero))
        {
            if (handle.IsInvalid) throw new IOException("Windows metadata stream could not be read.");
            using (var stream = new FileStream(handle, FileAccess.Read))
            { if (stream.Length > 4096) throw new IOException("Metadata stream exceeds its size limit."); using (var reader = new StreamReader(stream)) return reader.ReadToEnd(); }
        }
    }
}
