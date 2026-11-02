using System;
using System.IO;
namespace FishTank;

// File.Exists hides permission and I/O failures; security state must distinguish them from absence.
internal static class LocalStoreFiles
{
    internal static bool Exists(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.Directory) != 0) throw new InvalidDataException("A security settings file is a directory. Existing settings were not replaced.");
            return true;
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }
    internal static byte[] ReadOptional(string path, int maximumBytes)
    {
        FileStream stream;
        try { stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        using (stream)
        {
            if (stream.Length > maximumBytes) throw new IOException("Saved security data exceeds its size limit.");
            var bytes = new byte[(int)stream.Length];
            int offset = 0;
            while (offset < bytes.Length)
            {
                int count = stream.Read(bytes, offset, bytes.Length - offset);
                if (count == 0) throw new EndOfStreamException("Security data changed during reading.");
                offset += count;
            }
            return bytes;
        }
    }
}
