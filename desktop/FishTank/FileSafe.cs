using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace FishTank;

public sealed class FileSafeSource
{
    public string Name;
    public Stream Content;
}
public sealed class FileSafeItem
{
    public string Name;
    public long Length;
    internal int Offset;
}

// Versioned encrypt-then-MAC envelope: PBKDF2-SHA256, AES-256-CBC, HMAC-SHA256.
// Header and ciphertext are authenticated before any plaintext is parsed or exposed.
public static class FileSafe
{
    public const long MaximumContentBytes = 64L * 1024 * 1024;
    public const int MaximumFiles = 256;
    internal const int MaximumPayloadBytes = 66 * 1024 * 1024;
    private const int HeaderLength = 52;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("FTSAFE01");

    public static string SafeName(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 1024 || name.Any(c => c < 32) || name.StartsWith("/") || name.Contains('\\')
            || name.IndexOfAny(new[] { ':', '<', '>', '"', '|', '?', '*', '\0', '\r', '\n' }) >= 0)
            throw new InvalidDataException("Use ordinary relative file names without links, absolute paths or alternate streams.");
        foreach (string component in name.Split('/'))
        {
            string stem = component.Split('.')[0].TrimEnd(' ', '.').ToUpperInvariant();
            if (component == "" || component.Length > 255 || component == "." || component == ".." || component.EndsWith(".") || component.EndsWith(" ")
                || new[] { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$", "CLOCK$", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³" }.Contains(stem))
                throw new InvalidDataException("The protected file contains an unsafe or reserved path name.");
        }
        return name;
    }
    private static byte[] Random(int count) { var b = new byte[count]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b); return b; }
    internal static bool Equal(byte[] a, byte[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        int difference = 0; for (int i = 0; i < a.Length; i++) difference |= a[i] ^ b[i]; return difference == 0;
    }
    private static byte[] Keys(string password, byte[] salt)
    {
        if (string.IsNullOrEmpty(password) || password.Length > 1024) throw new ArgumentException("A passphrase is required (maximum 1,024 characters).");
        using (var derive = new Rfc2898DeriveBytes(password, salt, 600000, HashAlgorithmName.SHA256)) return derive.GetBytes(64);
    }
    private static byte[] Mac(Stream stream, byte[] key, long length)
    {
        using (var hmac = new HMACSHA256(key))
        {
            stream.Position = 0; var buffer = new byte[65536]; long remaining = length;
            while (remaining > 0) { int read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining)); if (read == 0) throw new EndOfStreamException(); hmac.TransformBlock(buffer, 0, read, buffer, 0); remaining -= read; }
            hmac.TransformFinalBlock(new byte[0], 0, 0); return hmac.Hash;
        }
    }
    public static void Create(string destination, string password, IList<FileSafeSource> sources)
    {
        if (password == null || password.Length < 12 || password.Length > 1024) throw new ArgumentException("Use a unique passphrase of 12–1,024 characters. It cannot be reset.");
        if (sources == null || sources.Count < 1 || sources.Count > MaximumFiles) throw new ArgumentException("Choose 1–256 files per container.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
        foreach (var source in sources)
        {
            if (!names.Add(SafeName(source.Name))) throw new InvalidDataException("Duplicate file names cannot be protected in one container.");
            if (source.Content == null || !source.Content.CanRead || !source.Content.CanSeek || source.Content.Length > MaximumContentBytes)
                throw new IOException("Readable, seekable files up to 64 MiB are required.");
            total += source.Content.Length;
            if (total > MaximumContentBytes) throw new IOException("The selected files exceed the 64 MiB total container limit.");
        }
        byte[] salt = Random(16), iv = Random(16), keys = Keys(password, salt);
        byte[] encryptionKey = keys.Take(32).ToArray(), macKey = keys.Skip(32).ToArray();
        bool created = false, complete = false;
        try
        {
            using (var file = new FileStream(destination, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                created = true;
                using (var header = new BinaryWriter(file, Encoding.UTF8, true)) { header.Write(Magic); header.Write(600000); header.Write(salt); header.Write(iv); header.Write(0L); }
                using (var aes = Aes.Create())
                {
                    aes.Key = encryptionKey; aes.IV = iv; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
                    using (var cipher = new CryptoStream(file, aes.CreateEncryptor(), CryptoStreamMode.Write, true))
                    using (var payload = new BinaryWriter(cipher, Encoding.UTF8, true))
                    {
                        payload.Write(sources.Count);
                        var buffer = new byte[65536];
                        try
                        {
                            foreach (var source in sources)
                            {
                                byte[] name = new UTF8Encoding(false, true).GetBytes(source.Name);
                                if (name.Length > 4096) throw new InvalidDataException("File name is too long.");
                                long length = source.Content.Length; source.Content.Position = 0;
                                byte[] digest; using (var sha = SHA256.Create()) digest = sha.ComputeHash(source.Content);
                                source.Content.Position = 0;
                                payload.Write(name.Length); payload.Write(name); payload.Write(length); payload.Write(digest);
                                long left = length;
                                while (left > 0) { int read = source.Content.Read(buffer, 0, (int)Math.Min(buffer.Length, left)); if (read == 0) throw new EndOfStreamException(); cipher.Write(buffer, 0, read); left -= read; }
                                if (source.Content.ReadByte() != -1) throw new IOException("Source size changed during protection.");
                            }
                        }
                        finally { Array.Clear(buffer, 0, buffer.Length); }
                    }
                }
                long end = file.Length;
                file.Position = 44; using (var header = new BinaryWriter(file, Encoding.UTF8, true)) header.Write(end - HeaderLength);
                byte[] mac = Mac(file, macKey, end); file.Position = end; file.Write(mac, 0, mac.Length); file.Flush(true);
            }
            complete = true;
        }
        finally
        {
            Array.Clear(keys, 0, keys.Length); Array.Clear(encryptionKey, 0, encryptionKey.Length); Array.Clear(macKey, 0, macKey.Length);
            if (created && !complete && File.Exists(destination)) File.Delete(destination);
        }
    }
    public static FileSafeSession Open(string path, string password)
    {
        using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var reader = new BinaryReader(file, Encoding.UTF8, true))
        {
            if (file.Length < 100 || file.Length > MaximumPayloadBytes + 128) throw new InvalidDataException("Invalid or oversized protected container.");
            if (!Equal(reader.ReadBytes(8), Magic) || reader.ReadInt32() != 600000) throw new InvalidDataException("Unsupported protected container format.");
            byte[] salt = reader.ReadBytes(16), iv = reader.ReadBytes(16); long length = reader.ReadInt64();
            if (length < 16 || length % 16 != 0 || length > MaximumPayloadBytes || file.Length != HeaderLength + length + 32)
                throw new InvalidDataException("Invalid protected container length.");
            byte[] keys = Keys(password, salt), encryptionKey = keys.Take(32).ToArray(), macKey = keys.Skip(32).ToArray();
            byte[] plain = null;
            try
            {
                file.Position = HeaderLength + length; byte[] expected = reader.ReadBytes(32);
                if (!Equal(expected, Mac(file, macKey, HeaderLength + length))) throw new CryptographicException("Wrong passphrase or damaged/tampered container. No content was exposed.");
                byte[] ciphertext = new byte[(int)length]; file.Position = HeaderLength;
                int offset = 0; while (offset < ciphertext.Length) { int read = file.Read(ciphertext, offset, ciphertext.Length - offset); if (read == 0) throw new EndOfStreamException(); offset += read; }
                using (var aes = Aes.Create())
                {
                    aes.Key = encryptionKey; aes.IV = iv; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
                    using (var decrypt = aes.CreateDecryptor()) plain = decrypt.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
                }
                file.Position = 0;
                string fingerprint; using (var sha = SHA256.Create()) fingerprint = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "");
                var session = new FileSafeSession(plain) { ContainerSha256 = fingerprint }; plain = null; return session;
            }
            finally
            {
                Array.Clear(keys, 0, keys.Length); Array.Clear(encryptionKey, 0, encryptionKey.Length); Array.Clear(macKey, 0, macKey.Length);
                if (plain != null) Array.Clear(plain, 0, plain.Length);
            }
        }
    }
}

public sealed class FileSafeSession : IDisposable
{
    internal string ContainerSha256 { get; set; }
    private readonly object gate = new object();
    private byte[] plaintext;
    private readonly List<FileSafeItem> items = new List<FileSafeItem>();
    internal FileSafeSession(byte[] plaintext)
    {
        this.plaintext = plaintext;
        using (var stream = new MemoryStream(plaintext, false))
        using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
        {
            int count = reader.ReadInt32(); if (count < 1 || count > FileSafe.MaximumFiles) throw new InvalidDataException("Invalid protected file count.");
            long total = 0; var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < count; i++)
            {
                int size = reader.ReadInt32(); if (size < 1 || size > 4096) throw new InvalidDataException("Invalid protected file name.");
                string name = FileSafe.SafeName(new UTF8Encoding(false, true).GetString(reader.ReadBytes(size)));
                long length = reader.ReadInt64(); byte[] digest = reader.ReadBytes(32);
                if (!names.Add(name) || length < 0 || length > FileSafe.MaximumContentBytes || (total += length) > FileSafe.MaximumContentBytes || stream.Position + length > stream.Length || digest.Length != 32)
                    throw new InvalidDataException("Invalid protected file record.");
                int position = (int)stream.Position;
                using (var sha = SHA256.Create()) if (!FileSafe.Equal(digest, sha.ComputeHash(plaintext, position, (int)length))) throw new CryptographicException("Protected file content failed verification.");
                items.Add(new FileSafeItem { Name = name, Length = length, Offset = position }); stream.Position += length;
            }
            if (stream.Position != stream.Length) throw new InvalidDataException("Unexpected trailing protected data.");
        }
    }
    public IList<FileSafeItem> Files
    {
        get { lock (gate) { NeedUnlocked(); return items.Select(x => new FileSafeItem { Name = x.Name, Length = x.Length }).ToList().AsReadOnly(); } }
    }
    private void NeedUnlocked() { if (plaintext == null) throw new InvalidOperationException("Protected files are locked."); }
    public void ExtractAll(string destination, Action<string> restrictDirectory = null)
    {
        lock (gate) NeedUnlocked();
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Choose a new output folder. Existing paths are never overwritten.");
        Directory.CreateDirectory(destination); restrictDirectory?.Invoke(destination);
        string root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var buffer = new byte[65536];
        try
        {
            foreach (var item in items)
            {
                string path = Path.GetFullPath(Path.Combine(root, FileSafe.SafeName(item.Name).Replace('/', Path.DirectorySeparatorChar)));
                if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Export path escaped the output folder.");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    long left = item.Length; int offset = item.Offset;
                    while (left > 0)
                    {
                        int count = (int)Math.Min(buffer.Length, left);
                        lock (gate) { NeedUnlocked(); Buffer.BlockCopy(plaintext, offset, buffer, 0, count); }
                        file.Write(buffer, 0, count); left -= count; offset += count;
                    }
                    lock (gate) NeedUnlocked();
                    file.Flush(true);
                }
            }
        }
        finally { Array.Clear(buffer, 0, buffer.Length); }
    }
    public void Dispose()
    {
        lock (gate) { if (plaintext != null) { Array.Clear(plaintext, 0, plaintext.Length); plaintext = null; } foreach (var item in items) item.Name = ""; }
    }
}
