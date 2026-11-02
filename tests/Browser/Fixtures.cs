using System;
using System.IO;
using System.Text.Json;
namespace FishTank;
// Portable serializer and collector fixtures; actual server, session policy and vault code are linked unchanged.
public static class Core
{
    public static string Data = Path.Combine(Path.GetTempPath(), "FishTank-browser-test-" + Guid.NewGuid().ToString("N"));
    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions { IncludeFields = true, PropertyNameCaseInsensitive = true };
    public static string Json(object value) => JsonSerializer.Serialize(value, Options);
    public static T Parse<T>(string text) => JsonSerializer.Deserialize<T>(text, Options);
}
internal static class SecurityReviews { internal static string Run(string category, bool baseline) => Core.Json(new { category, baseline }); }
internal static class DiskHealthChecks { internal static string Run() => "{\"fixture\":\"disk\"}"; }
internal static class BackupHistory { internal static string Report() => "{\"fixture\":\"backups\"}"; }
