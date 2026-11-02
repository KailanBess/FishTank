using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace FishTank;

public static class Vault
{
	private struct Info
	{
		public uint Attr;

		public System.Runtime.InteropServices.ComTypes.FILETIME Creation;

		public System.Runtime.InteropServices.ComTypes.FILETIME Access;

		public System.Runtime.InteropServices.ComTypes.FILETIME Write;

		public uint Volume;

		public uint High;

		public uint Low;

		public uint Links;

		public uint IndexHigh;

		public uint IndexLow;
	}

	public static string Folder => Path.Combine(Core.Data, "vault");

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern SafeFileHandle CreateFile(string p, uint access, uint share, IntPtr sec, uint creation, uint flags, IntPtr template);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool GetFileInformationByHandle(SafeFileHandle h, out Info i);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool SetFileInformationByHandle(SafeFileHandle h, int cls, ref int data, uint size);

	private static void Check(string path)
	{
		string value = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\') + "\\";
		if (!path.StartsWith(value, StringComparison.OrdinalIgnoreCase) || path.StartsWith(Core.Data.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase) || path.IndexOf(':', 3) >= 0)
		{
			throw new IOException("Only ordinary files inside the current user's profile, outside application storage, can be quarantined/restored.");
		}
	}

	internal static List<SafeFileHandle> LockParents(string path)
	{
		List<SafeFileHandle> list = new List<SafeFileHandle>();
		try
		{
			string directoryName = Path.GetDirectoryName(path);
			while (!string.IsNullOrEmpty(directoryName))
			{
				SafeFileHandle safeFileHandle = CreateFile(directoryName, 128u, 3u, IntPtr.Zero, 3u, 35651584u, IntPtr.Zero);
				if (safeFileHandle.IsInvalid)
				{
					safeFileHandle.Dispose();
					throw new IOException("Cannot lock parent directory.");
				}
				list.Add(safeFileHandle);
				if (!GetFileInformationByHandle(safeFileHandle, out var i) || (i.Attr & 0x400) != 0)
				{
					throw new IOException("Reparse points are refused.");
				}
				directoryName = Path.GetDirectoryName(directoryName);
			}
			return list;
		}
		catch
		{
			foreach (SafeFileHandle item in list)
			{
				item.Dispose();
			}
			throw;
		}
	}

	private static byte[] Digest(byte[] b)
	{
		using SHA256 sHA = SHA256.Create();
		return sHA.ComputeHash(b);
	}

	private static string Hex(byte[] b)
	{
		return BitConverter.ToString(b).Replace("-", "").ToLowerInvariant();
	}

	public static VaultEntry Put(string path, string reason)
	{
		path = Path.GetFullPath(path);
		Check(path);
		if (string.IsNullOrWhiteSpace(reason))
		{
			throw new ArgumentException("A quarantine reason is required.");
		}
		Directory.CreateDirectory(Folder);
		List<SafeFileHandle> list = LockParents(path);
		try
		{
			using SafeFileHandle safeFileHandle = CreateFile(path, 2147549184u, 0u, IntPtr.Zero, 3u, 2097152u, IntPtr.Zero);
			if (safeFileHandle.IsInvalid)
			{
				throw new IOException("File is locked or access was denied.");
			}
			if (!GetFileInformationByHandle(safeFileHandle, out var i) || (i.Attr & 0x410) != 0 || i.Links != 1 || i.High != 0 || i.Low > 67108864)
			{
				throw new IOException("Only single-link regular files up to 64 MiB are supported.");
			}
			using FileStream fileStream = new FileStream(safeFileHandle, FileAccess.Read);
			byte[] array = new byte[i.Low];
			int j;
			int num;
			for (j = 0; j < array.Length; j += num)
			{
				if ((num = fileStream.Read(array, j, array.Length - j)) <= 0)
				{
					break;
				}
			}
			if (j != array.Length)
			{
				throw new IOException("Short read.");
			}
			VaultEntry vaultEntry = new VaultEntry();
			vaultEntry.Id = Guid.NewGuid().ToString("N");
			vaultEntry.Original = path;
			vaultEntry.Sha256 = Hex(Digest(array));
			vaultEntry.Created = DateTime.UtcNow.ToString("o");
			vaultEntry.Reason = reason;
			VaultEntry vaultEntry2 = vaultEntry;
			byte[] bytes = Encoding.UTF8.GetBytes(Core.Json(new
			{
				entry = vaultEntry2,
				content = Convert.ToBase64String(array)
			}));
			byte[] array2 = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
			string path2 = Path.Combine(Folder, vaultEntry2.Id + ".vault");
			using (FileStream fileStream2 = new FileStream(path2, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			{
				fileStream2.Write(array2, 0, array2.Length);
				fileStream2.Flush(flushToDisk: true);
			}
			Array.Clear(array, 0, array.Length);
			Array.Clear(bytes, 0, bytes.Length);
			Core.Audit("quarantine-staged", vaultEntry2);
			int data = 1;
			if (!SetFileInformationByHandle(safeFileHandle, 4, ref data, 4u))
			{
				throw new IOException("Encrypted recovery copy saved, but original removal failed. Source is NOT neutralized.");
			}
			Core.Audit("quarantine-original-delete-pending", vaultEntry2);
			return vaultEntry2;
		}
		finally
		{
			foreach (SafeFileHandle item in list)
			{
				item.Dispose();
			}
		}
	}

	public static Dictionary<string, object> Read(string id)
	{
		if (!Guid.TryParseExact(id, "N", out var _))
		{
			throw new ArgumentException("Invalid vault identifier");
		}
		string text = Path.Combine(Folder, id + ".vault");
		if (new FileInfo(text).Length > 104857600)
		{
			throw new IOException("Vault size limit exceeded.");
		}
		return Core.Parse<Dictionary<string, object>>(Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(text), null, DataProtectionScope.CurrentUser)));
	}

	public static string List()
	{
		Directory.CreateDirectory(Folder);
		List<object> list = new List<object>();
		string[] files = Directory.GetFiles(Folder, "*.vault");
		foreach (string path in files)
		{
			try
			{
				list.Add(Read(Path.GetFileNameWithoutExtension(path))["entry"]);
			}
			catch (Exception ex)
			{
				list.Add(new
				{
					id = Path.GetFileNameWithoutExtension(path),
					error = ex.Message
				});
			}
		}
		return Core.Json(list);
	}

	public static void Restore(string id, string destination)
	{
		destination = Path.GetFullPath(destination);
		Check(destination);
		List<SafeFileHandle> list = LockParents(destination);
		try
		{
			Dictionary<string, object> dictionary = Read(id);
			Dictionary<string, object> dictionary2 = (Dictionary<string, object>)dictionary["entry"];
			byte[] array = Convert.FromBase64String((string)dictionary["content"]);
			if (Hex(Digest(array)) != (string)dictionary2["Sha256"])
			{
				throw new CryptographicException("Vault hash mismatch");
			}
			Core.Audit("restore-request", new { id, destination });
			using (FileStream fileStream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			{
				fileStream.Write(array, 0, array.Length);
				fileStream.Flush(flushToDisk: true);
			}
			Array.Clear(array, 0, array.Length);
			Core.Audit("restore-complete", new { id, destination });
		}
		finally
		{
			foreach (SafeFileHandle item in list)
			{
				item.Dispose();
			}
		}
	}

	public static void Delete(string id)
	{
		Read(id);
		Core.Audit("vault-delete-authorized", new { id });
		File.Delete(Path.Combine(Folder, id + ".vault"));
	}
}
