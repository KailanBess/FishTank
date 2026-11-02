using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FishTank;

// Exact loopback origin only. No CORS, proxy headers, arbitrary commands, files or remote binding.
internal sealed class LocalBrowserServer : IDisposable
{
    private readonly HttpListener listener = new HttpListener();
    private readonly BrowserSessionPolicy sessions = new BrowserSessionPolicy();
    private readonly SemaphoreSlim slots = new SemaphoreSlim(8, 8);
    private readonly SemaphoreSlim operation = new SemaphoreSlim(1, 1);
    private readonly Func<bool> allowed;
    private readonly Func<string, string, Task<string>> execute;
    private readonly Action lockVault;
    private volatile bool disposed;
    private readonly Timer expiry;
    internal string Origin { get; private set; }
    internal LocalBrowserServer(Func<bool> allowed, Func<string, string, Task<string>> execute, Action lockVault, int port = 47653)
    {
        this.allowed = allowed; this.execute = execute; this.lockVault = lockVault;
        if (port == 0) { var reservation = new TcpListener(IPAddress.Loopback, 0); reservation.Start(); port = ((IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop(); }
        if (port < 1024 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        Origin = "http://127.0.0.1:" + port;
        listener.Prefixes.Add(Origin + "/");
        try { listener.Start(); } catch { listener.Close(); throw; }
        expiry = new Timer(_ => { if (sessions.Expire(DateTime.UtcNow)) lockVault(); }, null, 1000, 1000);
        _ = Listen();
    }
    internal string PairingUrl()
    {
        if (disposed || !Access()) throw new UnauthorizedAccessException("Unlock FishTank before pairing a browser.");
        lockVault(); return Origin + "/#pair=" + sessions.BeginPair(DateTime.UtcNow);
    }
    internal void Revoke() { sessions.Revoke(); lockVault(); }
    private bool Access() { try { return !disposed && allowed(); } catch { return false; } }
    private async Task Listen()
    {
        while (!disposed)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync().ConfigureAwait(false); } catch { return; }
            if (!slots.Wait(0)) { try { context.Response.StatusCode = 429; context.Response.Close(); } catch { } continue; }
            _ = Task.Run(async () => { try { await Handle(context).ConfigureAwait(false); } catch { try { context.Response.Abort(); } catch { } } finally { slots.Release(); } });
        }
    }
    private static string Resource(string name)
    {
        using (var stream = typeof(LocalBrowserServer).Assembly.GetManifestResourceStream("FishTank.Browser." + name))
        { if (stream == null) throw new InvalidOperationException("Browser resource is missing."); using (var reader = new StreamReader(stream)) return reader.ReadToEnd(); }
    }
    private async Task Handle(HttpListenerContext context)
    {
        var request = context.Request; var response = context.Response;
        response.Headers["Cache-Control"] = "no-store";
        response.Headers["Pragma"] = "no-cache";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["X-Frame-Options"] = "DENY";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers["Content-Security-Policy"] = "default-src 'none'; script-src 'self'; style-src 'self'; connect-src 'self'; img-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'; object-src 'none'";
        response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        response.KeepAlive = false;
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90)))
        using (timeout.Token.Register(() => { try { response.Abort(); } catch { } }))
        {
            if (request.RemoteEndPoint == null || !IPAddress.IsLoopback(request.RemoteEndPoint.Address) || request.Headers["Host"] != new Uri(Origin).Authority
                || request.Url.Query != "" || (request.Headers["Sec-Fetch-Site"] != null && request.Headers["Sec-Fetch-Site"] != "same-origin" && request.Headers["Sec-Fetch-Site"] != "none"))
            { await Reply(response, 403, "application/json", "{\"error\":\"Local origin required.\"}"); return; }
            string path = request.Url.AbsolutePath;
            if (request.HttpMethod == "GET" && (path == "/" || path == "/app.js" || path == "/app.css"))
            {
                await Reply(response, 200, path == "/" ? "text/html" : path.EndsWith(".js") ? "text/javascript" : "text/css", Resource(path == "/" ? "index.html" : path.Substring(1))); return;
            }
            if (request.HttpMethod != "POST" || request.Headers["Origin"] != Origin || request.ContentType == null || request.ContentType.Split(';')[0].Trim() != "application/json")
            { await Reply(response, 403, "application/json", "{\"error\":\"Same-origin JSON request required.\"}"); return; }
            if (!operation.Wait(0)) { await Reply(response, 429, "application/json", "{\"error\":\"Another request is running. Try again shortly.\"}"); return; }
            try
            {
                if (!Access()) { Revoke(); await Reply(response, 401, "application/json", "{\"error\":\"Unlock and pair again from the desktop app.\"}"); return; }
                if (request.ContentLength64 < 0 || request.ContentLength64 > 131072) { await Reply(response, 413, "application/json", "{\"error\":\"Request is too large or has no size.\"}"); return; }
                string token = request.Headers["Authorization"];
                token = token != null && token.StartsWith("Bearer ", StringComparison.Ordinal) ? token.Substring(7) : null;
                string nonce = request.Headers["X-FishTank-CSRF"];
                if (path != "/api/pair" && !sessions.Check(token, nonce, DateTime.UtcNow, false))
                { Revoke(); await Reply(response, 401, "application/json", "{\"error\":\"Session expired. Pair again from the desktop app.\"}"); return; }
                string body;
                using (var input = new MemoryStream())
                {
                    var buffer = new byte[4096]; int count;
                    using (var bodyTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                    using (bodyTimeout.Token.Register(() => { try { request.InputStream.Close(); } catch { } }))
                        while ((count = await request.InputStream.ReadAsync(buffer, 0, buffer.Length, bodyTimeout.Token).ConfigureAwait(false)) > 0)
                        { if (input.Length + count > 131072) throw new InvalidDataException(); input.Write(buffer, 0, count); }
                    body = new UTF8Encoding(false, true).GetString(input.ToArray());
                }
                if (path == "/api/pair")
                {
                    // Pair token is the raw JSON string, validated without general object deserialization.
                    string raw = body.Length == 66 && body[0] == '"' && body[65] == '"' ? body.Substring(1, 64) : null;
                    var result = sessions.Pair(raw, DateTime.UtcNow);
                    if (result == null) { await Reply(response, 401, "application/json", "{\"error\":\"Pairing link expired or already used. Open a new link from FishTank.\"}"); return; }
                    await Reply(response, 200, "application/json", "{\"session\":\"" + result[0] + "\",\"csrf\":\"" + result[1] + "\"}"); return;
                }
                bool touch = path != "/api/status";
                if (!sessions.Check(token, nonce, DateTime.UtcNow, touch)) { await Reply(response, 401, "application/json", "{\"error\":\"Pair again from the desktop.\"}"); return; }
                if (path == "/api/lock") { Revoke(); await Reply(response, 200, "application/json", "{\"locked\":true}"); return; }
                string json = await execute(path, body).ConfigureAwait(false);
                if (!Access() || !sessions.Check(token, nonce, DateTime.UtcNow, false)) { Revoke(); await Reply(response, 401, "application/json", "{\"error\":\"Access locked. Pair again from the desktop.\"}"); return; }
                await Reply(response, 200, "application/json", json);
            }
            catch (BrowserRequestException ex) { await Reply(response, ex.Status, "application/json", "{\"error\":\"" + ex.SafeMessage + "\"}"); }
            catch { await Reply(response, 400, "application/json", "{\"error\":\"Operation failed. Check the vault passphrase, field values, or changes made in another window. Nothing was automatically overwritten.\"}"); }
            finally { operation.Release(); }
        }
    }
    private static async Task Reply(HttpListenerResponse response, int status, string type, string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text); response.StatusCode = status; response.ContentType = type + "; charset=utf-8"; response.ContentLength64 = bytes.Length;
        try { await response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false); } finally { response.Close(); }
    }
    public void Dispose() { disposed = true; expiry.Dispose(); sessions.Revoke(); listener.Close(); lockVault(); }
}
internal sealed class BrowserRequestException : Exception
{
    internal int Status { get; }
    internal string SafeMessage { get; }
    internal BrowserRequestException(int status, string safeMessage) { Status = status; SafeMessage = safeMessage; }
}
