using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace FishTank;

public sealed class PasswordSession : IDisposable
{
	public const int Iterations = 600000;

	private const int Limit = 16777216;

	private static readonly byte[] Magic = Encoding.ASCII.GetBytes("FTPV0001");

	private byte[] keys;

	private byte[] salt;

	private byte[] expected;

	private List<PasswordEntry> entries;

	private string path;

	public DateTime LastActivityUtc { get; private set; }

	public bool IsUnlocked => keys != null;
	internal string ContentSha256 => IsUnlocked && expected != null ? BitConverter.ToString(expected).Replace("-", "") : throw new InvalidOperationException("Password vault is locked.");

	public static string DefaultPath => Path.Combine(Core.Data, "passwords.ftvault");

	public IList<PasswordEntry> Entries
	{
		get
		{
			Touch();
			return entries.Select((PasswordEntry e) => e.Copy()).ToList().AsReadOnly();
		}
	}

	private static byte[] Random(int size)
	{
		byte[] array = new byte[size];
		using RandomNumberGenerator randomNumberGenerator = RandomNumberGenerator.Create();
		randomNumberGenerator.GetBytes(array);
		return array;
	}

	private static byte[] Hash(byte[] bytes)
	{
		using SHA256 sHA = SHA256.Create();
		return sHA.ComputeHash(bytes);
	}

	private static bool Equal(byte[] a, byte[] b)
	{
		if (a == null || b == null || a.Length != b.Length)
		{
			return false;
		}
		int num = 0;
		for (int i = 0; i < a.Length; i++)
		{
			num |= a[i] ^ b[i];
		}
		return num == 0;
	}

	private static byte[] Derive(string master, byte[] salt)
	{
		if (string.IsNullOrEmpty(master) || master.Length > 1024)
		{
			throw new ArgumentException("Master passphrase is required (maximum 1,024 characters).");
		}
		using Rfc2898DeriveBytes rfc2898DeriveBytes = new Rfc2898DeriveBytes(master, salt, 600000, HashAlgorithmName.SHA256);
		return rfc2898DeriveBytes.GetBytes(64);
	}

	private static void NewMaster(string master)
	{
		if (master == null || master.Length < 12 || master.Length > 1024)
		{
			throw new ArgumentException("Use a unique master passphrase of 12–1,024 characters. There is no password reset or recovery key.");
		}
	}

	public void Touch()
	{
		if (!IsUnlocked)
		{
			throw new InvalidOperationException("Password vault is locked.");
		}
		LastActivityUtc = DateTime.UtcNow;
	}

	public bool ShouldAutoLock(DateTime utcNow)
	{
		if (IsUnlocked)
		{
			return (utcNow - LastActivityUtc).TotalMinutes >= 5.0;
		}
		return false;
	}

	public static PasswordSession Create(string path, string master)
	{
		NewMaster(master);
		if (File.Exists(path))
		{
			throw new IOException("A password vault already exists. It was not overwritten.");
		}
		PasswordSession passwordSession = new PasswordSession();
		passwordSession.path = Path.GetFullPath(path);
		passwordSession.salt = Random(16);
		passwordSession.entries = new List<PasswordEntry>();
		PasswordSession passwordSession2 = passwordSession;
		try
		{
			passwordSession2.keys = Derive(master, passwordSession2.salt);
			passwordSession2.Persist(passwordSession2.entries);
			passwordSession2.Touch();
			return passwordSession2;
		}
		catch
		{
			passwordSession2.Dispose();
			throw;
		}
	}

	public static PasswordSession Open(string path, string master)
	{
		PasswordSession passwordSession = new PasswordSession();
		passwordSession.path = Path.GetFullPath(path);
		PasswordSession passwordSession2 = passwordSession;
		byte[] array = null;
		byte[] array2 = null;
		try
		{
			array = ReadFile(path);
			ParseFile(array, out passwordSession2.salt, out var iv, out var cipher, out var mac, out var header, out var size);
			passwordSession2.keys = Derive(master, passwordSession2.salt);
			byte[] b;
			using (HMACSHA256 hMACSHA = new HMACSHA256(passwordSession2.keys.Skip(32).ToArray()))
			{
				b = hMACSHA.ComputeHash(header);
			}
			if (!Equal(mac, b))
			{
				throw new CryptographicException("Incorrect master passphrase or damaged/tampered vault. Nothing was overwritten.");
			}
			using (Aes aes = Aes.Create())
			{
				aes.KeySize = 256;
				aes.Mode = CipherMode.CBC;
				aes.Padding = PaddingMode.PKCS7;
				aes.Key = passwordSession2.keys.Take(32).ToArray();
				aes.IV = iv;
				using ICryptoTransform cryptoTransform = aes.CreateDecryptor();
				array2 = cryptoTransform.TransformFinalBlock(cipher, 0, size);
			}
			PasswordPayload passwordPayload = Core.Parse<PasswordPayload>(Encoding.UTF8.GetString(array2));
			ValidatePayload(passwordPayload);
			passwordSession2.entries = passwordPayload.Entries;
			passwordSession2.expected = Hash(array);
			passwordSession2.Touch();
			return passwordSession2;
		}
		catch
		{
			passwordSession2.Dispose();
			throw;
		}
		finally
		{
			if (array != null)
			{
				Array.Clear(array, 0, array.Length);
			}
			if (array2 != null)
			{
				Array.Clear(array2, 0, array2.Length);
			}
		}
	}

	private static void ValidatePayload(PasswordPayload payload)
	{
		if (payload == null || payload.Version != 1 || payload.Entries == null || payload.Entries.Count > 2000)
		{
			throw new InvalidDataException("Invalid vault payload.");
		}
		HashSet<string> hashSet = new HashSet<string>();
		foreach (PasswordEntry entry in payload.Entries)
		{
			if (entry == null)
			{
				throw new InvalidDataException("Invalid entry");
			}
			entry.Validate();
			if (!hashSet.Add(entry.Id))
			{
				throw new InvalidDataException("Duplicate entry ID");
			}
		}
	}

	private static byte[] ReadFile(string path)
	{
		FileInfo fileInfo = new FileInfo(path);
		if (!fileInfo.Exists || fileInfo.Length > 16777216 || fileInfo.Length < 96 || (fileInfo.Attributes & FileAttributes.ReparsePoint) != 0)
		{
			throw new InvalidDataException("Invalid or oversized vault file.");
		}
		return File.ReadAllBytes(path);
	}

	private static void ParseFile(byte[] file, out byte[] salt, out byte[] iv, out byte[] cipher, out byte[] mac, out byte[] header, out int size)
	{
		using MemoryStream input = new MemoryStream(file);
		using BinaryReader binaryReader = new BinaryReader(input);
		if (!Equal(binaryReader.ReadBytes(8), Magic) || binaryReader.ReadInt32() != 600000)
		{
			throw new InvalidDataException("Unsupported vault format or key-derivation parameters.");
		}
		salt = binaryReader.ReadBytes(16);
		iv = binaryReader.ReadBytes(16);
		size = binaryReader.ReadInt32();
		if (size < 16 || size % 16 != 0 || size > 16777136 || file.Length != 48 + size + 32)
		{
			throw new InvalidDataException("Invalid vault size.");
		}
		cipher = binaryReader.ReadBytes(size);
		mac = binaryReader.ReadBytes(32);
		header = file.Take(file.Length - 32).ToArray();
	}

	private byte[] Encrypt(List<PasswordEntry> candidate)
	{
		PasswordPayload passwordPayload = new PasswordPayload();
		passwordPayload.Entries = candidate;
		PasswordPayload passwordPayload2 = passwordPayload;
		ValidatePayload(passwordPayload2);
		byte[] bytes = Encoding.UTF8.GetBytes(Core.Json(passwordPayload2));
		if (bytes.Length > 16777088)
		{
			Array.Clear(bytes, 0, bytes.Length);
			throw new IOException("Vault size limit (16 MiB) reached.");
		}
		try
		{
			byte[] array = Random(16);
			byte[] array2;
			using (Aes aes = Aes.Create())
			{
				aes.KeySize = 256;
				aes.Mode = CipherMode.CBC;
				aes.Padding = PaddingMode.PKCS7;
				aes.Key = keys.Take(32).ToArray();
				aes.IV = array;
				using ICryptoTransform cryptoTransform = aes.CreateEncryptor();
				array2 = cryptoTransform.TransformFinalBlock(bytes, 0, bytes.Length);
			}
			using MemoryStream memoryStream = new MemoryStream();
			using (BinaryWriter binaryWriter = new BinaryWriter(memoryStream, Encoding.UTF8, leaveOpen: true))
			{
				binaryWriter.Write(Magic);
				binaryWriter.Write(600000);
				binaryWriter.Write(salt);
				binaryWriter.Write(array);
				binaryWriter.Write(array2.Length);
				binaryWriter.Write(array2);
			}
			byte[] buffer = memoryStream.ToArray();
			byte[] array3;
			using (HMACSHA256 hMACSHA = new HMACSHA256(keys.Skip(32).ToArray()))
			{
				array3 = hMACSHA.ComputeHash(buffer);
			}
			memoryStream.Write(array3, 0, array3.Length);
			return memoryStream.ToArray();
		}
		finally
		{
			Array.Clear(bytes, 0, bytes.Length);
		}
	}

	private void Persist(List<PasswordEntry> candidate)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path));
		using (new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
		{
			byte[] array = (File.Exists(path) ? Hash(ReadFile(path)) : null);
			if (!Equal(array, expected) && (array != null || expected != null))
			{
				throw new IOException("Vault changed in another process. Lock and reopen it before saving.");
			}
			byte[] array2 = Encrypt(candidate);
			try
			{
				AtomicWrite(path, array2, expected == null);
				expected = Hash(array2);
			}
			finally
			{
				Array.Clear(array2, 0, array2.Length);
			}
		}
	}

	private static void AtomicWrite(string path, byte[] data, bool create)
	{
		string sourceFileName = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try
		{
			using (FileStream fileStream = new FileStream(sourceFileName, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			{
				fileStream.Write(data, 0, data.Length);
				fileStream.Flush(flushToDisk: true);
			}
			if (create)
			{
				File.Move(sourceFileName, path);
			}
			else
			{
				File.Replace(sourceFileName, path, path + ".bak");
			}
		}
		finally
		{
			if (File.Exists(sourceFileName))
			{
				File.Delete(sourceFileName);
			}
		}
	}

	public void Put(PasswordEntry entry)
	{
		Touch();
		entry.Validate();
		List<PasswordEntry> list = entries.Select((PasswordEntry e) => e.Copy()).ToList();
		int num = list.FindIndex((PasswordEntry e) => e.Id == entry.Id);
		PasswordEntry passwordEntry = entry.Copy();
		passwordEntry.UpdatedUtc = DateTime.UtcNow.ToString("o");
		if (num < 0)
		{
			list.Add(passwordEntry);
		}
		else
		{
			list[num] = passwordEntry;
		}
		Persist(list);
		entries = list;
	}

	public void Delete(string id)
	{
		Touch();
		List<PasswordEntry> list = (from e in entries
			where e.Id != id
			select e.Copy()).ToList();
		if (list.Count == entries.Count)
		{
			throw new ArgumentException("Entry not found");
		}
		Persist(list);
		entries = list;
	}

	public void ExportEncrypted(string destination)
	{
		Touch();
		destination = Path.GetFullPath(destination);
		if (string.Equals(destination, path, StringComparison.OrdinalIgnoreCase) || string.Equals(destination, path + ".bak", StringComparison.OrdinalIgnoreCase))
		{
			throw new IOException("Choose a new backup filename outside the active vault path.");
		}
		byte[] array = ReadFile(path);
		try
		{
			if (!Equal(Hash(array), expected))
			{
				throw new IOException("Vault changed. Lock and reopen before backup.");
			}
			using FileStream fileStream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
			fileStream.Write(array, 0, array.Length);
			fileStream.Flush(flushToDisk: true);
		}
		finally
		{
			Array.Clear(array, 0, array.Length);
		}
	}

	public static PasswordSession RestoreEncrypted(string backup, string destination, string master)
	{
		using (PasswordSession passwordSession = Open(backup, master))
		{
			byte[] array = ReadFile(backup);
			try
			{
				if (!Equal(Hash(array), passwordSession.expected))
				{
					throw new IOException("Backup changed while validating");
				}
				using (new FileStream(destination + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
				{
					AtomicWrite(destination, array, !File.Exists(destination));
				}
			}
			finally
			{
				Array.Clear(array, 0, array.Length);
			}
		}
		return Open(destination, master);
	}

	public void Dispose()
	{
		if (keys != null)
		{
			Array.Clear(keys, 0, keys.Length);
		}
		keys = null;
		if (salt != null)
		{
			Array.Clear(salt, 0, salt.Length);
		}
		salt = null;
		if (expected != null)
		{
			Array.Clear(expected, 0, expected.Length);
		}
		expected = null;
		if (entries != null)
		{
			foreach (PasswordEntry entry in entries)
			{
				entry.Password = "";
				entry.Username = "";
				entry.Notes = "";
			}
			entries.Clear();
		}
		entries = null;
	}
}
