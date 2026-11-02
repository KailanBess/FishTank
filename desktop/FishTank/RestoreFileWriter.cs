using System;
using System.IO;
namespace FishTank;

internal static class RestoreFileWriter
{
    internal static void WriteNew(string destination, byte[] content, Action<Stream, byte[]> write = null)
    {
        string folder = Path.GetDirectoryName(Path.GetFullPath(destination));
        string temporary = Path.Combine(folder, ".fishtank-restore-" + Guid.NewGuid().ToString("N") + ".tmp");
        bool created = false;
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = true;
                if (write == null) file.Write(content, 0, content.Length); else write(file, content);
                file.Flush(true);
            }
            File.Move(temporary, destination); // No overwrite; publish only after the complete copy is durable.
        }
        finally
        {
            if (created && File.Exists(temporary))
                try { File.Delete(temporary); }
                catch (Exception ex) { throw new IOException("Restore did not publish a complete destination. A temporary plaintext restore file could not be removed: " + temporary, ex); }
        }
    }
}
