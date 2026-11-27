using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
namespace FishTank;

public static class DependencyAdvisoryExport
{
    public static string Serialize(DependencyAdvisoryReport report)
    {
        if (report == null) throw new ArgumentNullException(nameof(report));
        return DependencyJson.Serializer().Serialize(new {
            schemaVersion = report.SchemaVersion, recordedAtUtc = report.RecordedAtUtc.ToString("o", CultureInfo.InvariantCulture), provider = report.Provider,
            source = new { sha256 = report.Input.SourceSha256, format = report.Input.Format, skippedItems = report.Input.SkippedItems },
            findings = report.Findings.Select(f => new { ecosystem = f.Package.Ecosystem, name = f.Package.Name, version = f.Package.Version,
                state = f.State, code = f.Code, advisoryIds = f.AdvisoryIds,
                advisoryUrls = f.AdvisoryIds.Select(id => "https://osv.dev/vulnerability/" + Uri.EscapeDataString(id)).ToArray() }).ToArray(),
            coverage = report.Coverage });
    }
    public static void SaveNew(string destination, DependencyAdvisoryReport report, IEnumerable<string> protectedDirectories)
    {
        string path = Path.GetFullPath(destination);
        if (!Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase) || File.Exists(path)) throw new IOException("Choose a new JSON report filename.");
        foreach (var directory in protectedDirectories)
        {
            string prefix = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new IOException("Save reports outside profile/program folders.");
        }
        for (string part = path; !string.IsNullOrEmpty(part); part = Path.GetDirectoryName(part))
            try { if ((File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked report destinations are unsupported."); }
            catch (FileNotFoundException) { } catch (DirectoryNotFoundException) { }
        RestoreFileWriter.WriteNew(path, new UTF8Encoding(false).GetBytes(Serialize(report)));
    }
}
