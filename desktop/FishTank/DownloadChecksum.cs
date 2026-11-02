using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace FishTank;

public static class DownloadChecksum
{
	public static string Normalize(string expected)
	{
		expected = (expected ?? "").Trim();
		if (!Regex.IsMatch(expected, "\\A[0-9a-fA-F]{64}\\z"))
			throw new ArgumentException("Paste exactly 64 hexadecimal characters from the publisher's SHA-256 checksum.");
		return expected.ToUpperInvariant();
	}

	public static string Compute(string path)
	{
		if (Environment.OSVersion.Platform == PlatformID.Win32NT)
		{
			path = ScanPaths.Normalize(path);
			ScanPaths.RefuseReparseAncestors(path);
		}
		if ((File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
			throw new IOException("Choose a regular file rather than a folder or link.");
		using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
		using (var sha = SHA256.Create())
			return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "");
	}

	public static bool Matches(string expected, string actual)
	{
		return string.Equals(Normalize(expected), Normalize(actual), StringComparison.Ordinal);
	}
}
