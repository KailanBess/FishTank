using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace FishTank;

public static class ScanReports
{
	private static string Folder => Path.Combine(Core.Data, "scan-reports");

	private static string ReportPath(string id)
	{
		if (!Guid.TryParseExact(id, "N", out var _))
		{
			throw new ArgumentException("Invalid scan report ID");
		}
		return Path.Combine(Folder, id + ".dpapi");
	}

	public static void Save(DetailedReport report)
	{
		Directory.CreateDirectory(Folder);
		byte[] bytes = Encoding.UTF8.GetBytes(Core.Json(report));
		if (bytes.Length > 67108864)
		{
			throw new IOException("Scan report exceeds 64 MiB; scanning stopped. The previous checkpoint is retained.");
		}
		string text = ReportPath(report.Id);
		string text2 = text + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try
		{
			byte[] array = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
			using (FileStream fileStream = new FileStream(text2, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			{
				fileStream.Write(array, 0, array.Length);
				fileStream.Flush(flushToDisk: true);
			}
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
			Array.Clear(bytes, 0, bytes.Length);
			if (File.Exists(text2))
			{
				File.Delete(text2);
			}
		}
	}

	public static DetailedReport Read(string id)
	{
		string text = ReportPath(id);
		if (new FileInfo(text).Length > 68157440)
		{
			throw new IOException("Oversized report");
		}
		byte[] array = ProtectedData.Unprotect(File.ReadAllBytes(text), null, DataProtectionScope.CurrentUser);
		try
		{
			DetailedReport detailedReport = Core.Parse<DetailedReport>(Encoding.UTF8.GetString(array));
			if (detailedReport == null || detailedReport.Version != 1 || detailedReport.Id != id || detailedReport.Items == null || detailedReport.Items.Count > 500000)
			{
				throw new InvalidDataException("Invalid report");
			}
			return detailedReport;
		}
		finally
		{
			Array.Clear(array, 0, array.Length);
		}
	}

	public static string[] RecentIds()
	{
		if (!Directory.Exists(Folder))
		{
			return new string[0];
		}
		return (from f in (from f in new DirectoryInfo(Folder).GetFiles("*.dpapi")
				orderby f.LastWriteTimeUtc descending
				select f).Take(50)
			select Path.GetFileNameWithoutExtension(f.Name)).ToArray();
	}

	public static string CsvField(string value)
	{
		value = value ?? "";
		string text = value.TrimStart();
		if (text.Length > 0 && "=+-@\t\r\n".IndexOf(text[0]) >= 0)
		{
			value = "'" + value;
		}
		return "\"" + value.Replace("\"", "\"\"") + "\"";
	}

	public static void Export(DetailedReport report, string path, bool csv)
	{
		using FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
		using StreamWriter streamWriter = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
		if (!csv)
		{
			streamWriter.Write(Core.Json(report));
			return;
		}
		streamWriter.WriteLine("Path,State,Reason,Length,SHA256,EngineExitCode,OutputTruncated,Output,Error");
		foreach (ScanItem item in report.Items)
		{
			streamWriter.WriteLine(string.Join(",", new string[9]
			{
				item.Path,
				item.State,
				item.Reason,
				item.Length.ToString(),
				item.Sha256,
				item.EngineExitCode.HasValue ? item.EngineExitCode.Value.ToString() : "",
				item.OutputTruncated.ToString(),
				item.Output,
				item.Error
			}.Select(CsvField).ToArray()));
		}
	}
}
