using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FishTank;

public static class SelfHostedMfa
{
	private static string FilePath => Path.Combine(Core.Data, "delivery-mfa.dpapi");

	public static DeliveryConfiguration Load()
	{
		if (!File.Exists(FilePath))
		{
			return null;
		}
		if (new FileInfo(FilePath).Length > 16384)
		{
			throw new IOException("Invalid delivery configuration");
		}
		byte[] array = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), null, DataProtectionScope.CurrentUser);
		try
		{
			DeliveryConfiguration deliveryConfiguration = Core.Parse<DeliveryConfiguration>(Encoding.UTF8.GetString(array));
			Validate(deliveryConfiguration);
			return deliveryConfiguration;
		}
		finally
		{
			Array.Clear(array, 0, array.Length);
		}
	}

	public static void Validate(DeliveryConfiguration value)
	{
		if (value == null || value.Version != 1 || !Uri.TryCreate(value.Origin, UriKind.Absolute, out var result) || result.Scheme != "https" || result.UserInfo != "" || result.Query != "" || result.Fragment != "" || result.AbsolutePath != "/" || !Regex.IsMatch(value.DeviceToken ?? "", "\\A[A-Za-z0-9_-]{43}\\z") || !Regex.IsMatch(value.Binding ?? "", "\\A[a-f0-9]{32}\\z"))
		{
			throw new ArgumentException("Use an HTTPS server origin, its private device token and returned binding.");
		}
	}

	internal static Dictionary<string, object> Post(DeliveryConfiguration config, string path, object payload)
	{
		ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
		HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(config.Origin.TrimEnd('/') + path);
		httpWebRequest.Method = "POST";
		httpWebRequest.ContentType = "application/json";
		httpWebRequest.Headers["Authorization"] = "Bearer " + config.DeviceToken;
		httpWebRequest.AllowAutoRedirect = false;
		httpWebRequest.UseDefaultCredentials = false;
		httpWebRequest.Timeout = 20000;
		httpWebRequest.ReadWriteTimeout = 20000;
		byte[] bytes = Encoding.UTF8.GetBytes(Core.Json(payload));
		httpWebRequest.ContentLength = bytes.Length;
		try
		{
			using (Stream stream = httpWebRequest.GetRequestStream())
			{
				stream.Write(bytes, 0, bytes.Length);
			}
			using HttpWebResponse httpWebResponse = (HttpWebResponse)httpWebRequest.GetResponse();
			if (httpWebResponse.StatusCode != HttpStatusCode.OK)
			{
				throw new IOException("Verification service refused the request");
			}
			using StreamReader streamReader = new StreamReader(httpWebResponse.GetResponseStream());
            int responseLimit = path == "/v1/devices/list" ? 131072 : 8192;
			char[] array = new char[responseLimit + 1];
			int i;
			int num;
			for (i = 0; i < array.Length; i += num)
			{
				if ((num = streamReader.Read(array, i, array.Length - i)) <= 0)
				{
					break;
				}
			}
			if (i > responseLimit)
			{
				throw new IOException("Verification response too large");
			}
			return Core.Parse<Dictionary<string, object>>(new string(array, 0, i));
		}
		catch (WebException ex)
		{
			HttpWebResponse httpWebResponse2 = ex.Response as HttpWebResponse;
			int num2 = (int)(httpWebResponse2?.StatusCode ?? ((HttpStatusCode)0));
			httpWebResponse2?.Dispose();
			throw new IOException(num2 switch
			{
				403 => "Code invalid, expired, context-mismatched or used.",
				429 => "Delivery limit reached. Wait before requesting another code.",
				_ => "Verification server unavailable or unauthorized (HTTP " + num2 + "). No verification was granted.",
			});
		}
	}

	public static DeliveryConfiguration Discover(string origin, string token)
	{
		DeliveryConfiguration deliveryConfiguration = new DeliveryConfiguration();
		deliveryConfiguration.Origin = origin;
		deliveryConfiguration.DeviceToken = token;
		deliveryConfiguration.Binding = new string('0', 32);
		DeliveryConfiguration deliveryConfiguration2 = deliveryConfiguration;
		Validate(deliveryConfiguration2);
		Dictionary<string, object> dictionary = Post(deliveryConfiguration2, "/v1/status", new { });
		if (!dictionary.ContainsKey("version") || Convert.ToInt32(dictionary["version"]) != 1)
		{
			throw new IOException("Unsupported server protocol");
		}
		deliveryConfiguration2.Binding = Convert.ToString(dictionary["binding"]);
		Validate(deliveryConfiguration2);
		return deliveryConfiguration2;
	}

	public static void Save(string password, string factor, DeliveryConfiguration config)
	{
		Validate(config);
		if (!AppAccess.Mfa.Configured)
		{
			throw new InvalidOperationException("Enroll an offline authenticator first; keep recovery access if the server disconnects.");
		}
		AppAccess.ChangeDeliveryConfiguration(password, factor, delegate
		{
			Write(config);
		});
	}

	public static void Configure(string password, string factor, string origin, string token)
	{
		if (!AppAccess.Mfa.Configured)
		{
			throw new InvalidOperationException("Enroll an offline authenticator first.");
		}
		AppAccess.ChangeDeliveryConfiguration(password, factor, delegate
		{
			Write(Discover(origin, token));
		});
	}

	internal static void Clear()
	{
		if (File.Exists(FilePath))
		{
			File.Delete(FilePath);
		}
	}

	private static void Write(DeliveryConfiguration config)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(Core.Json(config));
		string text = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try
		{
			byte[] array = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
			using (FileStream fileStream = new FileStream(text, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			{
				fileStream.Write(array, 0, array.Length);
				fileStream.Flush(flushToDisk: true);
			}
			if (File.Exists(FilePath))
			{
				File.Replace(text, FilePath, null);
			}
			else
			{
				File.Move(text, FilePath);
			}
		}
		finally
		{
			Array.Clear(bytes, 0, bytes.Length);
			if (File.Exists(text))
			{
				File.Delete(text);
			}
		}
	}

	public static string Send(string password, string channel, string purpose)
	{
		if ((channel != "email" && channel != "sms") || (purpose != "enroll" && purpose != "signin" && purpose != "settings"))
		{
			throw new ArgumentException("Invalid delivery context");
		}
		AppAccess.AuthorizeCodeDelivery(password);
		DeliveryConfiguration deliveryConfiguration = Load();
		if (deliveryConfiguration == null)
		{
			throw new InvalidOperationException("Configure your verification server first");
		}
		if (purpose != "enroll" && !((channel == "email") ? deliveryConfiguration.EmailEnrolled : deliveryConfiguration.SmsEnrolled))
		{
			throw new InvalidOperationException("Verify and enroll this channel first");
		}
		Dictionary<string, object> dictionary = Post(deliveryConfiguration, "/v1/challenge", new
		{
			channel = channel,
			purpose = purpose,
			binding = deliveryConfiguration.Binding
		});
		string text = Convert.ToString(dictionary["challenge"]);
		if (!Regex.IsMatch(text ?? "", "\\A[A-Za-z0-9_-]{43}\\z") || Convert.ToString(dictionary["binding"]) != deliveryConfiguration.Binding || Convert.ToString(dictionary["channel"]) != channel || Convert.ToString(dictionary["purpose"]) != purpose)
		{
			throw new IOException("Invalid challenge binding");
		}
		return text;
	}

	public static bool Check(string challenge, string code, string channel, string purpose)
	{
		DeliveryConfiguration deliveryConfiguration = Load();
		if (deliveryConfiguration == null)
		{
			return false;
		}
		Dictionary<string, object> dictionary = Post(deliveryConfiguration, "/v1/check", new { challenge, code, purpose });
		if (dictionary.ContainsKey("approved") && dictionary["approved"] is bool && (bool)dictionary["approved"] && Convert.ToString(dictionary["challenge"]) == challenge && Convert.ToString(dictionary["channel"]) == channel && Convert.ToString(dictionary["purpose"]) == purpose)
		{
			return Convert.ToString(dictionary["binding"]) == deliveryConfiguration.Binding;
		}
		return false;
	}

	public static string Factor(string id, string channel, string purpose, string code)
	{
		return "delivery:" + channel + ":" + purpose + ":" + id + ":" + code;
	}

	public static bool VerifyFactor(string factor, bool settings)
	{
		string[] array = (factor ?? "").Split(':');
		if (array.Length != 5 || array[0] != "delivery" || array[2] != (settings ? "settings" : "signin") || !Regex.IsMatch(array[3], "\\A[A-Za-z0-9_-]{43}\\z") || !Regex.IsMatch(array[4], "\\A[0-9]{8}\\z"))
		{
			return false;
		}
		DeliveryConfiguration deliveryConfiguration = Load();
		if (deliveryConfiguration == null || ((!(array[1] == "email") || !deliveryConfiguration.EmailEnrolled) && (!(array[1] == "sms") || !deliveryConfiguration.SmsEnrolled)))
		{
			return false;
		}
		return Check(array[3], array[4], array[1], array[2]);
	}

	public static void EnrollChannel(string password, string existingFactor, string channel, string challenge, string code)
	{
		if (channel != "email" && channel != "sms")
		{
			throw new ArgumentException("Invalid channel");
		}
		AppAccess.ChangeDeliveryConfiguration(password, existingFactor, delegate
		{
			DeliveryConfiguration deliveryConfiguration = Load();
			if (deliveryConfiguration == null || !Check(challenge, code, channel, "enroll"))
			{
				throw new UnauthorizedAccessException("Destination ownership was not verified.");
			}
			if (channel == "email")
			{
				deliveryConfiguration.EmailEnrolled = true;
			}
			else
			{
				deliveryConfiguration.SmsEnrolled = true;
			}
			Write(deliveryConfiguration);
		});
	}
}
