using System;
using System.Security.Cryptography;

namespace FishTank;

// Bearer credentials stay in page memory, never cookies, URLs, or persistent browser storage.
public sealed class BrowserSessionPolicy
{
    private readonly object gate = new object();
    private string pair, session, csrf;
    private DateTime pairUntil, issued, activity, attemptsSince;
    private int attempts;
    public static string Token() { var bytes = new byte[32]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes); return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant(); }
    private static bool Equal(string a, string b) { if (a == null || b == null || a.Length != 64 || b.Length != 64) return false; int difference = 0; for (int i = 0; i < a.Length; i++) difference |= a[i] ^ b[i]; return difference == 0; }
    public string BeginPair(DateTime now)
    {
        lock (gate) { Clear(); pair = Token(); pairUntil = now.AddMinutes(1); return pair; }
    }
    public string[] Pair(string token, DateTime now)
    {
        lock (gate)
        {
            if (now < attemptsSince || now - attemptsSince >= TimeSpan.FromMinutes(1)) { attemptsSince = now; attempts = 0; }
            if (++attempts > 20 || now >= pairUntil || now < pairUntil.AddMinutes(-1) || !Equal(pair, token)) return null;
            pair = null; session = Token(); csrf = Token(); issued = activity = now;
            return new[] { session, csrf };
        }
    }
    public bool Check(string token, string nonce, DateTime now, bool touch)
    {
        lock (gate)
        {
            if (now < activity || now < issued || now - activity >= TimeSpan.FromMinutes(5) || now - issued >= TimeSpan.FromHours(8)) { Clear(); return false; }
            if (!Equal(session, token) || !Equal(csrf, nonce)) return false;
            if (touch) activity = now;
            return true;
        }
    }
    public bool Expire(DateTime now)
    {
        lock (gate) {
            if (session == null) return false;
            if (now >= activity && now >= issued && now - activity < TimeSpan.FromMinutes(5) && now - issued < TimeSpan.FromHours(8)) return false;
            Clear(); return true;
        }
    }
    public void Revoke() { lock (gate) Clear(); }
    private void Clear() { pair = session = csrf = null; }
}
