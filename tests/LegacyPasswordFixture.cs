using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using FishTank;

internal static class LegacyPasswordFixture
{
    internal static void Write(string path, string master)
    {
        // Exact original v1 envelope and payload, without the new organization fields.
        byte[] salt = Enumerable.Range(1,16).Select(i => (byte)i).ToArray();
        byte[] iv = Enumerable.Range(21,16).Select(i => (byte)i).ToArray();
        byte[] keys;
        using (var derive = new Rfc2898DeriveBytes(master, salt, 600000, HashAlgorithmName.SHA256)) keys = derive.GetBytes(64);
        var payload = new { Version = 1, Entries = new[] { new { Id = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", Title = "Legacy standalone", Username = "legacy-user", Password = "Legacy fixture password!", Website = "example.test", Notes = "Original notes", UpdatedUtc = "2026-11-02T00:00:00Z" } } };
        byte[] plain = Encoding.UTF8.GetBytes(Core.Json(payload)); byte[] cipher;
        using (var aes = Aes.Create()) { aes.Key = keys.Take(32).ToArray(); aes.IV = iv; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7; using (var encrypt = aes.CreateEncryptor()) cipher = encrypt.TransformFinalBlock(plain, 0, plain.Length); }
        using (var stream = new MemoryStream())
        {
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true)) { writer.Write(Encoding.ASCII.GetBytes("FTPV0001")); writer.Write(600000); writer.Write(salt); writer.Write(iv); writer.Write(cipher.Length); writer.Write(cipher); }
            using (var hmac = new HMACSHA256(keys.Skip(32).ToArray())) { byte[] mac = hmac.ComputeHash(stream.ToArray()); stream.Write(mac, 0, mac.Length); }
            File.WriteAllBytes(path, stream.ToArray());
        }
        Array.Clear(keys, 0, keys.Length); Array.Clear(plain, 0, plain.Length);
    }
}
