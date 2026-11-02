using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace FishTank;

public static class ScanProfiles
{
	private static string Folder => Path.Combine(Core.Data, "scan-profiles");

	private static string PathFor(string name)
	{
		if (!Regex.IsMatch(name ?? "", "\\A[A-Za-z0-9 _-]{1,64}\\z"))
		{
			throw new ArgumentException("Profile names use 1–64 letters, digits, spaces, underscores or hyphens.");
		}
		return Path.Combine(Folder, name + ".json");
	}

	public static void Save(ScanPlan plan)
	{
		plan.Validate();
		Directory.CreateDirectory(Folder);
		string text = PathFor(plan.Name);
		string text2 = text + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try
		{
			File.WriteAllText(text2, Core.Json(plan));
			if (File.Exists(text))
			{
				File.Replace(text2, text, null);
			}
			else
			{
				File.Move(text2, text);
			}
		}
		finally
		{
			if (File.Exists(text2))
			{
				File.Delete(text2);
			}
		}
	}

	public static ScanPlan Load(string name)
	{
		string text = PathFor(name);
		if (new FileInfo(text).Length > 131072)
		{
			throw new IOException("Oversized scan profile");
		}
		ScanPlan scanPlan = Core.Parse<ScanPlan>(File.ReadAllText(text));
		if (scanPlan == null)
		{
			throw new InvalidDataException("Invalid profile");
		}
		scanPlan.Validate();
		return scanPlan;
	}

	public static string[] Names()
	{
		if (!Directory.Exists(Folder))
		{
			return new string[0];
		}
		return Directory.GetFiles(Folder, "*.json").Select(Path.GetFileNameWithoutExtension).ToArray();
	}
}
