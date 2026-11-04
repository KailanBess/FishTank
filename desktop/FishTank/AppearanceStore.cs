using System;
using System.IO;
using System.Text;

namespace FishTank;

public static class AppearanceStore
{
	public static string Notice = "";

	public static string FilePath => Path.Combine(Core.Data, "appearance.json");

	private static Appearance Read(string path)
	{
		FileInfo fileInfo = new FileInfo(path);
		if (fileInfo.Length > 32768)
		{
			throw new InvalidDataException("Appearance file too large");
		}
		Appearance appearance = Core.Parse<Appearance>(File.ReadAllText(path));
		if (appearance == null)
		{
			throw new InvalidDataException("Empty appearance settings");
		}
		appearance.Validate();
		return appearance;
	}

	public static Appearance Load()
	{
		Notice = "";
		if (!File.Exists(FilePath))
		{
			return new Appearance().WithReferenceStyle();
		}
		try
		{
			return Read(FilePath);
		}
		catch (Exception)
		{
			if (File.Exists(FilePath + ".bak"))
			{
				try
				{
					Appearance result = Read(FilePath + ".bak");
					Notice = "Appearance settings recovered from the previous saved version.";
					return result;
				}
				catch
				{
				}
			}
			Notice = "Appearance settings could not be read. Defaults are in use; the original file was retained.";
			return new Appearance();
		}
	}

	public static void Save(Appearance value)
	{
		value.Validate();
		Directory.CreateDirectory(Core.Data);
		string text = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try
		{
			byte[] bytes = Encoding.UTF8.GetBytes(Core.Json(value));
			using (FileStream fileStream = new FileStream(text, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			{
				fileStream.Write(bytes, 0, bytes.Length);
				fileStream.Flush(flushToDisk: true);
			}
			if (File.Exists(FilePath))
			{
				bool flag = false;
				try
				{
					Read(FilePath);
					flag = true;
				}
				catch
				{
				}
				File.Replace(text, FilePath, flag ? (FilePath + ".bak") : null);
			}
			else
			{
				File.Move(text, FilePath);
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

	public static Appearance Import(string path)
	{
		return Read(path);
	}

	public static void Export(Appearance value, string path)
	{
		value.Validate();
		File.WriteAllText(path, Core.Json(value));
	}
}

