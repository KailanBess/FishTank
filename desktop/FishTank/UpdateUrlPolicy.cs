using System;

namespace FishTank;

public static class UpdateUrlPolicy
{
    public static Uri RequireHttps(string url)
    {
        if (url == null || url.Length > 2048 || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo != "" || uri.Fragment != "" || uri.Query != "" || !uri.IsDefaultPort)
            throw new ArgumentException("Use a direct HTTPS URL on port 443, without credentials, query parameters or fragments. Redirects are refused.");
        return uri;
    }
}
