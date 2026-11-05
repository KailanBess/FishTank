using System.Text.Json;
namespace FishTank {
    // Portable serializer fixture; production Windows serialization has separate native checks.
    public static class Core {
        public static string Data;
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions { IncludeFields = true, PropertyNameCaseInsensitive = true };
        public static string Json(object value) => JsonSerializer.Serialize(value, Options);
        public static T Parse<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
    }
}
namespace System.Windows.Forms {
    // Native high-contrast detection is not available here. Tests call the explicit palette overload.
    internal static class SystemInformation { public static bool HighContrast => false; }
}
