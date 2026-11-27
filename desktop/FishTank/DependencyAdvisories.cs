using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace FishTank;

public sealed class DependencyPackage
{
    public string Ecosystem { get; private set; }
    public string Name { get; private set; }
    public string Version { get; private set; }
    public DependencyPackage(string ecosystem, string name, string version)
    {
        if ((ecosystem != "NuGet" && ecosystem != "PyPI") || !Regex.IsMatch(name ?? "", @"\A[A-Za-z0-9][A-Za-z0-9_.-]{0,99}\z") ||
            !Regex.IsMatch(version ?? "", @"\A[0-9A-Za-z][0-9A-Za-z.+_-]{0,99}\z")) throw new InvalidDataException("Unsupported package identity/version.");
        Ecosystem = ecosystem; Name = ecosystem == "PyPI" ? Regex.Replace(name.ToLowerInvariant(), "[-_.]+", "-") : name;
        Version = version;
    }
}
public sealed class DependencySnapshot
{
    public string SourceSha256 { get; private set; }
    public string Format { get; private set; }
    public int SkippedItems { get; private set; }
    public IReadOnlyList<DependencyPackage> Packages { get; private set; }
    internal DependencySnapshot(string hash, string format, int skipped, IEnumerable<DependencyPackage> packages)
    { SourceSha256 = hash; Format = format; SkippedItems = skipped; Packages = Array.AsReadOnly(packages.ToArray()); }
}
public sealed class DependencyFinding
{
    public DependencyPackage Package { get; internal set; }
    public string State { get; internal set; } = "Unknown";
    public string Code { get; internal set; } = "not-queried";
    public string[] AdvisoryIds { get; internal set; } = new string[0];
}
public sealed class DependencyAdvisoryReport
{
    public int SchemaVersion { get; internal set; } = 1;
    public DateTime RecordedAtUtc { get; internal set; }
    public string Provider { get { return "https://api.osv.dev/v1/querybatch"; } }
    public DependencySnapshot Input { get; internal set; }
    public DependencyFinding[] Findings { get; internal set; }
    public string Coverage { get { return "User-selected snapshot only; restore freshness, installed software, native/OS components and provider coverage are not verified. Advisory IDs may alias the same issue. No matches is not proof of safety. No severity, exploitability, fix or protection verdict is inferred."; } }
}

internal static class DependencyJson
{
    internal const int MaximumBytes = 4 * 1024 * 1024;
    internal static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength = MaximumBytes, RecursionLimit = 32 }; }
    internal static Dictionary<string, object> Parse(string text)
    {
        if (text == null || text.Length > MaximumBytes) throw new InvalidDataException("Dependency JSON exceeds its limit.");
        new UniqueFields(text).Check();
        return Serializer().DeserializeObject(text) as Dictionary<string, object> ?? throw new InvalidDataException("Choose a supported JSON object.");
    }
    internal static string Text(Dictionary<string, object> item, string key)
    { object value; return item.TryGetValue(key, out value) ? value as string : null; }
    internal static IList List(Dictionary<string, object> item, string key)
    { object value; return item.TryGetValue(key, out value) ? value as IList : null; }
    // Reject duplicate keys before JavaScriptSerializer could silently choose a value.
    private sealed class UniqueFields
    {
        private readonly string text; private int offset;
        internal UniqueFields(string text) { this.text = text; }
        private void Space() { while (offset < text.Length && char.IsWhiteSpace(text[offset])) offset++; }
        private void Need(char value) { Space(); if (offset >= text.Length || text[offset++] != value) throw new InvalidDataException("Malformed JSON."); }
        private string String()
        {
            Space(); int start = offset; Need('"'); bool escape = false;
            while (offset < text.Length)
            {
                char c = text[offset++];
                if (!escape && c == '"') return Serializer().Deserialize<string>(text.Substring(start, offset - start));
                if (!escape && c == '\\') escape = true; else escape = false;
            }
            throw new InvalidDataException("Unterminated JSON string.");
        }
        private void Value(int depth)
        {
            if (depth > 32) throw new InvalidDataException("Dependency JSON is too deeply nested.");
            Space(); if (offset >= text.Length) throw new InvalidDataException("Incomplete JSON.");
            char c = text[offset];
            if (c == '{')
            {
                offset++; Space(); if (offset < text.Length && text[offset] == '}') { offset++; return; }
                var keys = new HashSet<string>(StringComparer.Ordinal);
                while (true)
                {
                    if (!keys.Add(String())) throw new InvalidDataException("Duplicate JSON fields are unsupported.");
                    Need(':'); Value(depth + 1); Space();
                    if (offset < text.Length && text[offset] == '}') { offset++; return; }
                    Need(',');
                }
            }
            if (c == '[')
            {
                offset++; Space(); if (offset < text.Length && text[offset] == ']') { offset++; return; }
                while (true) { Value(depth + 1); Space(); if (offset < text.Length && text[offset] == ']') { offset++; return; } Need(','); }
            }
            if (c == '"') { String(); return; }
            int begin = offset;
            while (offset < text.Length && !char.IsWhiteSpace(text[offset]) && ",]}".IndexOf(text[offset]) < 0) offset++;
            if (begin == offset) throw new InvalidDataException("Invalid JSON token.");
        }
        internal void Check() { Value(0); Space(); if (offset != text.Length) throw new InvalidDataException("Trailing JSON data."); }
    }
}

public static class DependencySnapshots
{
    public static DependencySnapshot Read(string path)
    {
        using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var data = new MemoryStream())
        {
            if (input.Length > DependencyJson.MaximumBytes) throw new InvalidDataException("Choose a JSON snapshot up to 4 MiB.");
            var buffer = new byte[8192]; int count;
            while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
            { if (data.Length + count > DependencyJson.MaximumBytes) throw new InvalidDataException("Snapshot exceeds 4 MiB."); data.Write(buffer, 0, count); }
            return Parse(data.ToArray());
        }
    }
    public static DependencySnapshot Parse(byte[] bytes)
    {
        if (bytes == null || bytes.Length > DependencyJson.MaximumBytes) throw new InvalidDataException("Choose a JSON snapshot up to 4 MiB.");
        string text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
        var root = DependencyJson.Parse(text);
        if (root.ContainsKey("libraries") && (root.ContainsKey("components") || root.ContainsKey("bomFormat"))) throw new InvalidDataException("Ambiguous snapshot format.");
        var packages = new List<DependencyPackage>(); int skipped = 0; string format;
        object value;
        if (root.TryGetValue("libraries", out value) && value is Dictionary<string, object> libraries)
        {
            if (!root.TryGetValue("version", out var schema) || !(schema is int) || (int)schema != 3) throw new InvalidDataException("Only NuGet assets schema 3 is supported.");
            format = "NuGet project.assets.json v3 snapshot";
            if (libraries.Count > 10000) throw new InvalidDataException("Snapshot item limit exceeded.");
            foreach (var item in libraries)
            {
                var library = item.Value as Dictionary<string, object>; int slash = item.Key.LastIndexOf('/');
                if (library == null || DependencyJson.Text(library, "type") != "package" || slash < 1) { skipped++; continue; }
                try { packages.Add(new DependencyPackage("NuGet", item.Key.Substring(0, slash), item.Key.Substring(slash + 1))); }
                catch (InvalidDataException) { skipped++; }
            }
        }
        else if (DependencyJson.Text(root, "bomFormat") == "CycloneDX" && DependencyJson.List(root, "components") is IList components)
        {
            if (DependencyJson.Text(root, "specVersion") != "1.5") throw new InvalidDataException("Only CycloneDX schema 1.5 is supported.");
            format = "CycloneDX 1.5 direct component snapshot";
            if (components.Count > 10000) throw new InvalidDataException("Snapshot item limit exceeded.");
            foreach (var valueItem in components)
            {
                var item = valueItem as Dictionary<string, object>; string purl = item == null ? null : DependencyJson.Text(item, "purl");
                try
                {
                    string ecosystem = purl != null && purl.StartsWith("pkg:nuget/", StringComparison.Ordinal) ? "NuGet" :
                        purl != null && purl.StartsWith("pkg:pypi/", StringComparison.Ordinal) ? "PyPI" : null;
                    if (ecosystem == null || purl.Contains('?') || purl.Contains('#')) throw new InvalidDataException();
                    string body = purl.Substring(ecosystem == "NuGet" ? 10 : 9); int at = body.LastIndexOf('@');
                    if (at < 1) throw new InvalidDataException();
                    var package = new DependencyPackage(ecosystem, Uri.UnescapeDataString(body.Substring(0, at)), Uri.UnescapeDataString(body.Substring(at + 1)));
                    var declared = DependencyJson.Text(item, "version"); if (item.ContainsKey("version") && declared == null || declared != null && declared != package.Version) throw new InvalidDataException();
                    packages.Add(package);
                    if (DependencyJson.List(item, "components") is IList nested) skipped += nested.Count; // Nested components are not silently claimed covered.
                }
                catch (Exception ex) when (ex is InvalidDataException || ex is UriFormatException) { skipped++; }
            }
        }
        else throw new InvalidDataException("Choose NuGet project.assets.json or a CycloneDX component snapshot with versioned NuGet/PyPI purls.");
        var unique = packages.GroupBy(p => p.Ecosystem + "/" + p.Name.ToLowerInvariant() + "/" + p.Version, StringComparer.Ordinal).Select(g => g.First()).ToArray();
        if (unique.Length == 0 || unique.Length > 256) throw new InvalidDataException("Choose a snapshot with 1–256 supported package versions.");
        string hash; using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        return new DependencySnapshot(hash, format, skipped, unique);
    }
}

public static class DependencyAdvisories
{
    public static async Task<DependencyAdvisoryReport> CheckAsync(DependencySnapshot snapshot,
        Func<string, CancellationToken, Task<string>> transport = null, CancellationToken token = default)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        transport = transport ?? PostAsync; token.ThrowIfCancellationRequested();
        var findings = snapshot.Packages.Select(p => new DependencyFinding { Package = p }).ToArray();
        var ids = findings.Select(_ => new HashSet<string>(StringComparer.Ordinal)).ToArray();
        var seenTokens = findings.Select(_ => new HashSet<string>(StringComparer.Ordinal)).ToArray();
        int totalIds = 0;
        var pending = Enumerable.Range(0, findings.Length).ToDictionary(i => i, i => (string)null);
        for (int page = 0; page < 4 && pending.Count > 0; page++)
        {
            token.ThrowIfCancellationRequested(); var batch = pending.ToArray(); pending.Clear();
            var queries = batch.Select(item => {
                var p = findings[item.Key].Package;
                var query = new Dictionary<string, object> { ["package"] = new { name = p.Name, ecosystem = p.Ecosystem }, ["version"] = p.Version };
                if (item.Value != null) query["page_token"] = item.Value; return query;
            }).ToArray();
            string response;
            try { response = await transport(DependencyJson.Serializer().Serialize(new { queries }), token).ConfigureAwait(false); }
            catch (OperationCanceledException) { token.ThrowIfCancellationRequested(); foreach (var item in batch) { findings[item.Key].State = ids[item.Key].Count > 0 ? "Partial" : "Unavailable"; findings[item.Key].Code = "provider-timeout"; } break; }
            catch (Exception ex) when (ex is IOException || ex is WebException)
            { foreach (var item in batch) { findings[item.Key].State = "Unavailable"; findings[item.Key].Code = "provider-unavailable"; } break; }
            IList results;
            try
            {
                var root = DependencyJson.Parse(response); results = DependencyJson.List(root, "results");
                if (results == null || results.Count != batch.Length) throw new InvalidDataException();
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is ArgumentException || ex is InvalidOperationException)
            { foreach (var item in batch) { findings[item.Key].State = ids[item.Key].Count > 0 ? "Partial" : "Unknown"; findings[item.Key].Code = "invalid-provider-response"; } break; }
            for (int i = 0; i < batch.Length; i++)
            {
                int index = batch[i].Key; var result = results[i] as Dictionary<string, object>;
                try
                {
                    if (result == null) throw new InvalidDataException();
                    object raw; IList vulns = null; bool limited = false;
                    if (result.TryGetValue("vulns", out raw)) { vulns = raw as IList; if (vulns == null) throw new InvalidDataException(); limited = vulns.Count > 2000; }
                    if (vulns != null) foreach (var item in vulns.Cast<object>().Take(2000))
                    {
                        var row = item as Dictionary<string, object>; string id = row == null ? null : DependencyJson.Text(row, "id");
                        if (id == null || !Regex.IsMatch(id, @"\A[A-Za-z0-9][A-Za-z0-9_.:-]{0,127}\z")) throw new InvalidDataException();
                        if (!ids[index].Contains(id) && totalIds >= 2000) { limited = true; break; }
                        if (ids[index].Add(id)) totalIds++;
                    }
                    if (result.ContainsKey("error") || result.ContainsKey("errors") || (result.Count != 0 && !result.ContainsKey("vulns") && !result.ContainsKey("next_page_token"))) throw new InvalidDataException();
                    if (limited) { findings[index].State = "Partial"; findings[index].Code = "advisory-id-limit"; continue; }
                    string next = DependencyJson.Text(result, "next_page_token");
                    if (result.ContainsKey("next_page_token") && next == null) throw new InvalidDataException();
                    if (!string.IsNullOrEmpty(next))
                    {
                        if (!Regex.IsMatch(next, @"\A[A-Za-z0-9_+/=-]{1,2048}\z") || !seenTokens[index].Add(next)) throw new InvalidDataException();
                        pending[index] = next; findings[index].State = "Partial"; findings[index].Code = "more-provider-pages";
                    }
                    else { findings[index].State = ids[index].Count == 0 ? "No published matches" : "Advisory IDs reported"; findings[index].Code = "provider-query-complete"; }
                }
                catch (InvalidDataException) { findings[index].State = ids[index].Count == 0 ? "Unknown" : "Partial"; findings[index].Code = "invalid-provider-result"; }
            }
        }
        foreach (var index in pending.Keys) { findings[index].State = "Partial"; findings[index].Code = "provider-page-limit"; }
        token.ThrowIfCancellationRequested();
        for (int i = 0; i < findings.Length; i++) findings[i].AdvisoryIds = ids[i].OrderBy(id => id, StringComparer.Ordinal).ToArray();
        return new DependencyAdvisoryReport { Input = snapshot, RecordedAtUtc = DateTime.UtcNow, Findings = findings };
    }
    private static async Task<string> PostAsync(string json, CancellationToken token)
    {
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            var request = (HttpWebRequest)WebRequest.Create("https://api.osv.dev/v1/querybatch");
            request.Method = "POST"; request.ContentType = "application/json"; request.Accept = "application/json"; request.UserAgent = "FishTank-dependency-review";
            request.ServerCertificateValidationCallback = (sender, certificate, chain, errors) => errors == System.Net.Security.SslPolicyErrors.None;
            request.AllowAutoRedirect = false; request.UseDefaultCredentials = false; request.Credentials = null;
            request.Timeout = 15000; request.ReadWriteTimeout = 15000; request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            byte[] bytes = Encoding.UTF8.GetBytes(json); request.ContentLength = bytes.Length;
            using (deadline.Token.Register(request.Abort))
            {
                try
                {
                    using (var output = await request.GetRequestStreamAsync().ConfigureAwait(false)) await output.WriteAsync(bytes, 0, bytes.Length, deadline.Token).ConfigureAwait(false);
                    using (var response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false))
                    using (var input = response.GetResponseStream())
                    using (var data = new MemoryStream())
                    {
                        if (response.StatusCode != HttpStatusCode.OK || response.ContentLength > DependencyJson.MaximumBytes) throw new IOException("Provider unavailable or response limit exceeded.");
                        var buffer = new byte[8192]; int count;
                        while ((count = await input.ReadAsync(buffer, 0, buffer.Length, deadline.Token).ConfigureAwait(false)) != 0)
                        { if (data.Length + count > DependencyJson.MaximumBytes) throw new IOException("Provider response limit exceeded."); data.Write(buffer, 0, count); }
                        return new UTF8Encoding(false, true).GetString(data.ToArray());
                    }
                }
                catch (WebException) when (deadline.IsCancellationRequested) { token.ThrowIfCancellationRequested(); throw new OperationCanceledException(deadline.Token); }
            }
        }
    }
}
