using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace FishTank;

internal static class ProtectedStore
{
    private static readonly object Gate = new object();
    internal static T Read<T>(string name, Func<T> empty)
    {
        lock (Gate)
        {
            string path = Path.Combine(Core.Data, name + ".dpapi");
            byte[] encrypted = LocalStoreFiles.ReadOptional(path, 8 * 1024 * 1024);
            if (encrypted == null) return empty();
            byte[] plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            try { return Core.Parse<T>(Encoding.UTF8.GetString(plain)); }
            finally { Array.Clear(plain, 0, plain.Length); }
        }
    }
    internal static void Write<T>(string name, T value)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(Core.Data);
            string path = Path.Combine(Core.Data, name + ".dpapi");
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            byte[] plain = Encoding.UTF8.GetBytes(Core.Json(value));
            if (plain.Length > 7 * 1024 * 1024) throw new IOException("Security data exceeds its size limit; reduce the selected scope.");
            try
            {
                byte[] cipher = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
                AutomaticBackups.RequireSpace(Core.Data, cipher.Length);
                using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { file.Write(cipher, 0, cipher.Length); file.Flush(true); }
                if (File.Exists(path)) File.Replace(temp, path, path + ".bak"); else File.Move(temp, path);
            }
            finally { Array.Clear(plain, 0, plain.Length); if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
