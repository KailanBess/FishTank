using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace FishTank;

public static class IntegrityBaseline
{
	private static readonly object Gate = new object();

	private static string Store => Path.Combine(Core.Data, "integrity-baseline.dpapi");

	private static void CheckLocalPath(string path)
	{
		string pathRoot = Path.GetPathRoot(path);
		if (pathRoot == null || pathRoot.Length != 3 || pathRoot[1] != ':' || path.IndexOf(':', 3) >= 0 || new DriveInfo(pathRoot).DriveType == DriveType.Network)
		{
			throw new IOException("Choose a local drive path without alternate streams.");
		}
		string text = pathRoot;
		string[] array = path.Substring(pathRoot.Length).Split('\\');
		foreach (string text2 in array)
		{
			if (text2.Length != 0)
			{
				text = Path.Combine(text, text2);
				if (!File.Exists(text) && !Directory.Exists(text))
				{
					break;
				}
				if ((File.GetAttributes(text) & FileAttributes.ReparsePoint) != 0)
				{
					throw new IOException("Reparse-point baseline paths are not inspected.");
				}
			}
		}
	}

	private static List<BaselineEntry> Read()
	{
		if (!File.Exists(Store))
		{
			return new List<BaselineEntry>();
		}
		if (new FileInfo(Store).Length > 1048576)
		{
			throw new IOException("Baseline storage too large");
		}
		byte[] array = ProtectedData.Unprotect(File.ReadAllBytes(Store), null, DataProtectionScope.CurrentUser);
		try
		{
			List<BaselineEntry> list = Core.Parse<List<BaselineEntry>>(Encoding.UTF8.GetString(array));
			if (list == null || list.Count > 500)
			{
				throw new InvalidDataException("Invalid baseline");
			}
			return list;
		}
		finally
		{
			Array.Clear(array, 0, array.Length);
		}
	}

	public static void Capture(string path)
	{
		path = Path.GetFullPath(path);
		CheckLocalPath(path);
		if (!File.Exists(path))
		{
			throw new IOException("Choose a local regular file.");
		}
		string sha = Core.Hash(path);
		lock (Gate)
		{
			List<BaselineEntry> list = Read();
			list.RemoveAll((BaselineEntry e) => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
			list.Add(new BaselineEntry
			{
				Path = path,
				Sha256 = sha,
				CapturedUtc = DateTime.UtcNow.ToString("o")
			});
			if (list.Count > 500)
			{
				throw new IOException("Maximum 500 baseline files");
			}
			byte[] bytes = Encoding.UTF8.GetBytes(Core.Json(list));
			try
			{
				byte[] array = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
				string text = Store + "." + Guid.NewGuid().ToString("N") + ".tmp";
				try
				{
					using (FileStream fileStream = new FileStream(text, FileMode.CreateNew, FileAccess.Write, FileShare.None))
					{
						fileStream.Write(array, 0, array.Length);
						fileStream.Flush(flushToDisk: true);
					}
					if (File.Exists(Store))
					{
						File.Replace(text, Store, Store + ".bak");
					}
					else
					{
						File.Move(text, Store);
					}
				}
				finally
				{
					if (File.Exists(text))
					{
						File.Delete(text);
					}
				}
			}
			finally
			{
				Array.Clear(bytes, 0, bytes.Length);
			}
		}
		Core.Audit("integrity-baseline-captured", new { path });
	}

	public static string Compare()
	{
		List<object> list = new List<object>();
		lock (Gate)
		{
			foreach (BaselineEntry item in Read())
			{
				try
				{
					CheckLocalPath(item.Path);
					if (!File.Exists(item.Path))
					{
						list.Add(new
						{
							Name = item.Path,
							State = "Missing",
							Value = "Previously baselined file is missing; this is not by itself proof of malware."
						});
						continue;
					}
					string text = Core.Hash(item.Path);
					list.Add(new
					{
						Name = item.Path,
						State = ((text == item.Sha256) ? "Unchanged" : "Changed"),
						Value = "Captured: " + item.CapturedUtc + "\nBaseline SHA-256: " + item.Sha256 + "\nCurrent SHA-256: " + text + "\nA change may be a legitimate update. No repair or blocking performed."
					});
				}
				catch (Exception ex)
				{
					list.Add(new
					{
						Name = item.Path,
						State = "Unknown",
						Value = ex.Message
					});
				}
			}
		}
		return Core.Json(list);
	}
}
