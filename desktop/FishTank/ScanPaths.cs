using System;
using System.IO;

namespace FishTank;

public static class ScanPaths
{
	public static string Normalize(string path)
	{
		if (string.IsNullOrWhiteSpace(path) || path.IndexOfAny(new char[4] { '\0', '\r', '\n', '"' }) >= 0)
		{
			throw new ArgumentException("Invalid scan path");
		}
		path = Path.GetFullPath(path);
		string pathRoot = Path.GetPathRoot(path);
		if (pathRoot == null || pathRoot.Length != 3 || pathRoot[1] != ':' || path.IndexOf(':', 3) >= 0 || new DriveInfo(pathRoot).DriveType == DriveType.Network)
		{
			throw new IOException("Detailed scanning accepts local drive paths without alternate data streams. Network roots are refused.");
		}
		return path;
	}

	public static bool Within(string path, string root)
	{
		path = Normalize(path);
		root = Normalize(root);
		if (!string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
		{
			return path.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	public static void RefuseReparseAncestors(string path)
	{
		path = Normalize(path);
		string pathRoot = Path.GetPathRoot(path);
		string text = pathRoot;
		string[] array = path.Substring(pathRoot.Length).Split('\\');
		foreach (string text2 in array)
		{
			if (text2.Length != 0)
			{
				text = Path.Combine(text, text2);
				if ((File.GetAttributes(text) & FileAttributes.ReparsePoint) != 0)
				{
					throw new IOException("Reparse points are not followed.");
				}
			}
		}
	}
}
