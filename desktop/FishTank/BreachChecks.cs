using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FishTank;

public static class BreachChecks
{
	public static string PasswordHash(string password)
	{
		if (string.IsNullOrEmpty(password) || password.Length > 65536)
		{
			throw new ArgumentException("Enter a password of 1–65,536 characters.");
		}
		byte[] bytes = Encoding.UTF8.GetBytes(password);
		try
		{
			using SHA1 sHA = SHA1.Create();
			return BitConverter.ToString(sHA.ComputeHash(bytes)).Replace("-", "");
		}
		finally
		{
			Array.Clear(bytes, 0, bytes.Length);
		}
	}

	public static long ParsePasswordRange(string hash, string response)
	{
		if (!Regex.IsMatch(hash ?? "", "\\A[0-9A-F]{40}\\z") || string.IsNullOrEmpty(response) || response.Length > 2097152)
		{
			throw new InvalidDataException("Invalid password-range response.");
		}
		long num = 0L;
		int num2 = 0;
		string[] array = response.Split('\n');
		foreach (string text in array)
		{
			string text2 = text.Trim();
			if (text2.Length != 0)
			{
				Match match = Regex.Match(text2, "\\A([0-9A-Fa-f]{35}):([0-9]{1,18})\\z");
				if (!match.Success || !long.TryParse(match.Groups[2].Value, out var result))
				{
					throw new InvalidDataException("Unexpected password-range format; no conclusion can be drawn.");
				}
				num2++;
				if (string.Equals(match.Groups[1].Value, hash.Substring(5), StringComparison.OrdinalIgnoreCase))
				{
					num = Math.Max(num, result);
				}
			}
		}
		if (num2 == 0)
		{
			throw new InvalidDataException("Empty password-range response; no conclusion can be drawn.");
		}
		return num;
	}

	public static long CheckPassword(string password, bool consent, BreachTransport transport)
	{
		if (!consent)
		{
			throw new UnauthorizedAccessException("Explicit consent is required for each external lookup.");
		}
		string text = PasswordHash(password);
		Dictionary<string, string> dictionary = new Dictionary<string, string>();
		dictionary.Add("Add-Padding", "true");
		Dictionary<string, string> headers = dictionary;
		BreachResponse breachResponse = (transport ?? new BreachTransport(Fetch))(new Uri("https://api.pwnedpasswords.com/range/" + text.Substring(0, 5)), headers);
		EnsureSuccess(breachResponse, email: false);
		return ParsePasswordRange(text, breachResponse.Body);
	}

	public static string NormalizeEmail(string email)
	{
		if (string.IsNullOrWhiteSpace(email) || email.Length > 254 || email.IndexOfAny(new char[2] { '\r', '\n' }) >= 0)
		{
			throw new ArgumentException("Enter one valid email address.");
		}
		email = email.Trim();
		MailAddress mailAddress = new MailAddress(email);
		if (!string.Equals(mailAddress.Address, email, StringComparison.Ordinal))
		{
			throw new ArgumentException("Use only the email address, without a display name.");
		}
		return email;
	}

	public static List<Dictionary<string, object>> CheckEmail(string email, string apiKey, bool consent, BreachTransport transport)
	{
		if (!consent)
		{
			throw new UnauthorizedAccessException("Explicit consent is required before sending an email address to Have I Been Pwned.");
		}
		email = NormalizeEmail(email);
		if (!Regex.IsMatch(apiKey ?? "", "\\A[0-9a-fA-F]{32}\\z"))
		{
			throw new ArgumentException("Enter your 32-character HIBP API key. The key stays in this dialog's memory only.");
		}
		BreachResponse breachResponse = (transport ?? new BreachTransport(Fetch))(new Uri("https://haveibeenpwned.com/api/v3/breachedaccount/" + Uri.EscapeDataString(email) + "?truncateResponse=false"), new Dictionary<string, string> { { "hibp-api-key", apiKey } });
		if (breachResponse.Status == 404)
		{
			return new List<Dictionary<string, object>>();
		}
		EnsureSuccess(breachResponse, email: true);
		if (breachResponse.Body == null || breachResponse.Body.Length > 2097152)
		{
			throw new InvalidDataException("Unexpected breach response size.");
		}
		List<Dictionary<string, object>> list = Core.Parse<List<Dictionary<string, object>>>(breachResponse.Body);
		if (list == null || list.Count > 10000)
		{
			throw new InvalidDataException("Unexpected breach response.");
		}
		foreach (Dictionary<string, object> item in list)
		{
			if (item == null || !item.ContainsKey("Name"))
			{
				throw new InvalidDataException("Unexpected breach record.");
			}
		}
		return list;
	}

	private static void EnsureSuccess(BreachResponse response, bool email)
	{
		if (response == null)
		{
			throw new IOException("The lookup service returned no response.");
		}
		if (response.Status == 200)
		{
			return;
		}
		if (response.Status == 429)
		{
			throw new IOException("HIBP rate limit reached. Try again later; no breach conclusion was drawn.");
		}
		if (response.Status == 401 || response.Status == 403)
		{
			throw new IOException("HIBP rejected authorization. Check the API key and subscription permissions.");
		}
		throw new IOException("Lookup unavailable (HTTP " + response.Status + "). No breach conclusion was drawn.");
	}

	private static BreachResponse Fetch(Uri uri, Dictionary<string, string> headers)
	{
		if (uri.Scheme != "https" || (uri.Host != "haveibeenpwned.com" && uri.Host != "api.pwnedpasswords.com"))
		{
			throw new ArgumentException("Unapproved lookup host");
		}
		ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
		HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(uri);
		httpWebRequest.Method = "GET";
		httpWebRequest.UserAgent = "FishTank/0.3 breach checker";
		httpWebRequest.AllowAutoRedirect = false;
		httpWebRequest.Timeout = 20000;
		httpWebRequest.ReadWriteTimeout = 20000;
		httpWebRequest.UseDefaultCredentials = false;
		httpWebRequest.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
		foreach (KeyValuePair<string, string> header in headers)
		{
			httpWebRequest.Headers[header.Key] = header.Value;
		}
		try
		{
			using HttpWebResponse response = (HttpWebResponse)httpWebRequest.GetResponse();
			return ReadResponse(response);
		}
		catch (WebException ex)
		{
			if (ex.Response is HttpWebResponse httpWebResponse)
			{
				using (httpWebResponse)
				{
					BreachResponse breachResponse = new BreachResponse();
					breachResponse.Status = (int)httpWebResponse.StatusCode;
					breachResponse.RetryAfter = httpWebResponse.Headers["Retry-After"];
					return breachResponse;
				}
			}
			throw new IOException("Could not reach the breach service securely. Check connectivity and try again. Nothing was checked.");
		}
	}

	private static BreachResponse ReadResponse(HttpWebResponse response)
	{
		if (response.ContentLength > 2097152)
		{
			throw new IOException("Lookup response exceeded the size limit.");
		}
		using Stream stream = response.GetResponseStream();
		using StreamReader streamReader = new StreamReader(stream);
		StringBuilder stringBuilder = new StringBuilder();
		char[] array = new char[4096];
		int num;
		while ((num = streamReader.Read(array, 0, array.Length)) > 0)
		{
			if (stringBuilder.Length + num > 2097152)
			{
				throw new IOException("Lookup response exceeded the size limit.");
			}
			stringBuilder.Append(array, 0, num);
		}
		BreachResponse breachResponse = new BreachResponse();
		breachResponse.Status = (int)response.StatusCode;
		breachResponse.Body = stringBuilder.ToString();
		return breachResponse;
	}
}
