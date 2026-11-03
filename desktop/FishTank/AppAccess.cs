using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FishTank;

public static class AppAccess
{
	[ThreadStatic]
	private static string verifiedCredential;

	public static string Store => Path.Combine(Core.Data, "app-access.dpapi");

	public static bool SessionUnlocked { get; private set; }

    public static bool Enabled
    {
        get
        {
            try { return LocalStoreFiles.Exists(Store); }
            catch (IOException) { return true; } // Unknown access state must still require sign-in.
            catch (InvalidDataException) { return true; }
            catch (UnauthorizedAccessException) { return true; }
        }
    }

	public static MfaStatus Mfa
	{
		get
		{
			AccessRecord accessRecord = Read();
			MfaStatus mfaStatus = new MfaStatus();
			mfaStatus.Configured = accessRecord != null && accessRecord.MfaSecret != null;
			mfaStatus.AskOnSignIn = accessRecord?.AskMfaOnSignIn ?? false;
			mfaStatus.RecoveryCodesRemaining = ((accessRecord != null) ? accessRecord.RecoveryHashes.Length : 0);
			return mfaStatus;
		}
	}

	public static DateTime BlockedUntilUtc
	{
		get
		{
			AccessRecord accessRecord = Read();
			if (accessRecord != null)
			{
				return new DateTime(accessRecord.BlockedUntilUtcTicks, DateTimeKind.Utc);
			}
			return DateTime.MinValue;
		}
	}

	private static AccessRecord Read()
	{
        byte[] encrypted = LocalStoreFiles.ReadOptional(Store, 8192);
        if (encrypted == null) return null;
        byte[] array = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
		try
		{
			AccessRecord accessRecord = Core.Parse<AccessRecord>(Encoding.UTF8.GetString(array));
			if (accessRecord == null || (accessRecord.Version != 1 && accessRecord.Version != 2) || accessRecord.Iterations != 600000 || accessRecord.Failures < 0 || accessRecord.Failures > 1000 || accessRecord.BlockedUntilUtcTicks < 0 || accessRecord.BlockedUntilUtcTicks > DateTime.MaxValue.Ticks || Convert.FromBase64String(accessRecord.Salt).Length != 16 || Convert.FromBase64String(accessRecord.Verifier).Length != 32 || accessRecord.MfaFailures < 0 || accessRecord.MfaFailures > 1000 || accessRecord.MfaBlockedUntilUtcTicks < 0 || accessRecord.MfaBlockedUntilUtcTicks > DateTime.MaxValue.Ticks || accessRecord.SecurityStamp == null || accessRecord.SecurityStamp.Length > 64 || accessRecord.RecoveryHashes == null || accessRecord.RecoveryHashes.Length > 10 || accessRecord.RecoveryHashes.Distinct().Count() != accessRecord.RecoveryHashes.Length || accessRecord.LastTotpStep < -1 || accessRecord.LastTotpStep > 10000000000L)
			{
				throw new InvalidDataException("App access settings are invalid. Access remains locked.");
			}
			string[] recoveryHashes = accessRecord.RecoveryHashes;
			foreach (string text in recoveryHashes)
			{
				if (!Regex.IsMatch(text ?? "", "\\A[0-9A-F]{64}\\z"))
				{
					throw new InvalidDataException("Invalid MFA recovery configuration.");
				}
			}
			if (accessRecord.MfaSecret != null)
			{
				byte[] array2 = Totp.Decode(accessRecord.MfaSecret);
				try
				{
					if (array2.Length != 20)
					{
						throw new InvalidDataException("Invalid MFA configuration.");
					}
				}
				finally
				{
					Array.Clear(array2, 0, array2.Length);
				}
			}
			else if (accessRecord.AskMfaOnSignIn || accessRecord.RecoveryHashes.Length > 0 || accessRecord.LastTotpStep != -1)
			{
				throw new InvalidDataException("Incomplete MFA configuration. Access remains locked.");
			}
			return accessRecord;
		}
		finally
		{
			Array.Clear(array, 0, array.Length);
		}
	}

	private static string Identity(AccessRecord record)
	{
		return record.Salt + ":" + record.Verifier + ":" + record.SecurityStamp;
	}

	internal static void ChangeDeliveryConfiguration(string password, string factor, Action change)
	{
		MutateMfa(password, factor, DateTime.UtcNow, delegate
		{
			change();
		});
	}

	internal static void AuthorizeCodeDelivery(string password)
	{
		using (new FileStream(Store + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
		{
			AccessRecord accessRecord = Read();
			if (accessRecord == null || accessRecord.MfaSecret == null)
			{
				throw new UnauthorizedAccessException("App password and offline MFA enrollment required before delivery setup.");
			}
			if (DateTime.UtcNow.Ticks < accessRecord.BlockedUntilUtcTicks)
			{
				throw new UnauthorizedAccessException("Wait for the access cooldown.");
			}
			byte[] array = Derive(password, Convert.FromBase64String(accessRecord.Salt));
			bool flag;
			try
			{
				flag = Equal(array, Convert.FromBase64String(accessRecord.Verifier));
			}
			finally
			{
				Array.Clear(array, 0, array.Length);
			}
			if (!flag)
			{
				accessRecord.Failures = Math.Min(1000, accessRecord.Failures + 1);
				accessRecord.BlockedUntilUtcTicks = ((accessRecord.Failures < 3) ? 0 : DateTime.UtcNow.AddSeconds(Math.Min(300.0, 5.0 * Math.Pow(2.0, Math.Min(6, accessRecord.Failures - 3)))).Ticks);
				Write(accessRecord);
				throw new UnauthorizedAccessException("Current app password is required to request a message.");
			}
		}
	}

	private static byte[] Derive(string password, byte[] salt)
	{
		if (string.IsNullOrEmpty(password) || password.Length > 1024)
		{
			throw new ArgumentException("Enter an app password (maximum 1,024 characters).");
		}
		using Rfc2898DeriveBytes rfc2898DeriveBytes = new Rfc2898DeriveBytes(password, salt, 600000, HashAlgorithmName.SHA256);
		return rfc2898DeriveBytes.GetBytes(32);
	}

	private static bool Equal(byte[] a, byte[] b)
	{
		if (a.Length != b.Length)
		{
			return false;
		}
		int num = 0;
		for (int i = 0; i < a.Length; i++)
		{
			num |= a[i] ^ b[i];
		}
		return num == 0;
	}

	private static void Write(AccessRecord record)
	{
		record.Version = 2;
		byte[] bytes = Encoding.UTF8.GetBytes(Core.Json(record));
		string text = Store + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try
		{
			byte[] array = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
			using (FileStream fileStream = new FileStream(text, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			{
				fileStream.Write(array, 0, array.Length);
				fileStream.Flush(flushToDisk: true);
			}
			if (Enabled)
			{
				File.Replace(text, Store, null);
			}
			else
			{
				File.Move(text, Store);
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

	public static bool Verify(string password)
	{
		return AuthenticateAt(password, null, DateTime.UtcNow, forceMfa: false);
	}

	public static bool Authenticate(string password, string factor)
	{
		return AuthenticateAt(password, factor, DateTime.UtcNow, forceMfa: false);
	}

	public static bool VerifyAt(string password, DateTime utcNow)
	{
		return AuthenticateAt(password, null, utcNow, forceMfa: false);
	}

	public static bool AuthenticateAt(string password, string factor, DateTime utcNow, bool forceMfa)
	{
		SessionUnlocked = false;
		verifiedCredential = null;
		using (new FileStream(Store + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
		{
			AccessRecord accessRecord = Read();
			if (accessRecord == null)
			{
				SessionUnlocked = true;
				try { AuthenticationHistory.Record(true, forceMfa ? "Security settings verification" : "App sign-in"); } catch { }
				return true;
			}
			if (utcNow.Ticks < accessRecord.BlockedUntilUtcTicks)
			{
				throw new UnauthorizedAccessException("Too many attempts. Try again after " + new DateTime(accessRecord.BlockedUntilUtcTicks, DateTimeKind.Utc).ToLocalTime().ToString("T") + ".");
			}
			bool flag = accessRecord.MfaSecret != null && (accessRecord.AskMfaOnSignIn || forceMfa);
			if (flag && utcNow.Ticks < accessRecord.MfaBlockedUntilUtcTicks)
			{
				throw new UnauthorizedAccessException("Too many MFA attempts. Wait for the cooldown before verifying a factor again.");
			}
			byte[] array = Derive(password, Convert.FromBase64String(accessRecord.Salt));
			bool flag2;
			try
			{
				flag2 = Equal(array, Convert.FromBase64String(accessRecord.Verifier));
			}
			finally
			{
				Array.Clear(array, 0, array.Length);
			}
			if (flag2 && flag)
			{
				long num = Totp.Match(accessRecord.MfaSecret, (factor ?? "").Trim(), utcNow, accessRecord.LastTotpStep);
				string first = Totp.RecoveryHash(factor);
				int index = -1;
				for (int i = 0; i < accessRecord.RecoveryHashes.Length; i++)
				{
					if (Totp.FixedEquals(first, accessRecord.RecoveryHashes[i]))
					{
						index = i;
					}
				}
				if (num >= 0)
				{
					accessRecord.LastTotpStep = num;
				}
				else if (index >= 0)
				{
					accessRecord.RecoveryHashes = accessRecord.RecoveryHashes.Where((string hash, int num2) => num2 != index).ToArray();
				}
				else if (!((factor ?? "").StartsWith("delivery:") ? SelfHostedMfa.VerifyFactor(factor, forceMfa) : ServerPasskeys.Consume(factor, forceMfa)))
				{
					flag2 = false;
					accessRecord.MfaFailures = Math.Min(1000, accessRecord.MfaFailures + 1);
					accessRecord.MfaBlockedUntilUtcTicks = ((accessRecord.MfaFailures < 3) ? 0 : utcNow.AddSeconds(Math.Min(300.0, 5.0 * Math.Pow(2.0, Math.Min(6, accessRecord.MfaFailures - 3)))).Ticks);
				}
			}
			if (flag2)
			{
				accessRecord.Failures = 0;
				accessRecord.BlockedUntilUtcTicks = 0L;
				if (flag)
				{
					accessRecord.MfaFailures = 0;
					accessRecord.MfaBlockedUntilUtcTicks = 0L;
				}
				Write(accessRecord);
				verifiedCredential = Identity(accessRecord);
				SessionUnlocked = true;
                try { AuthenticationHistory.Record(true, forceMfa ? "Security settings verification" : "App sign-in"); } catch { }
				return true;
			}
			verifiedCredential = null;
			SessionUnlocked = false;
			accessRecord.Failures = Math.Min(1000, accessRecord.Failures + 1);
			accessRecord.BlockedUntilUtcTicks = ((accessRecord.Failures < 3) ? 0 : utcNow.AddSeconds(Math.Min(300.0, 5.0 * Math.Pow(2.0, Math.Min(6, accessRecord.Failures - 3)))).Ticks);
			Write(accessRecord);
			try { AuthenticationHistory.Record(false, forceMfa ? "Security settings verification" : "App sign-in"); } catch { }
			return false;
		}
	}

	public static void SetPassword(string current, string replacement, string factor = null)
	{
		if (string.IsNullOrWhiteSpace(replacement) || replacement.Length < 12 || replacement.Length > 1024)
		{
			throw new ArgumentException("Choose a unique app passphrase of 12–1,024 characters.");
		}
		bool enabled = Enabled;
		if (enabled && !AuthenticateAt(current, factor, DateTime.UtcNow, forceMfa: true))
		{
			throw new UnauthorizedAccessException("Password or MFA/recovery code is incorrect, expired or already used.");
		}
		string text = (enabled ? verifiedCredential : null);
		using (new FileStream(Store + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
		{
			AccessRecord accessRecord = Read();
			if (accessRecord != null != enabled || (accessRecord != null && Identity(accessRecord) != text))
			{
				throw new IOException("App access configuration changed in another process. Reauthenticate and try again.");
			}
			byte[] array = new byte[16];
			using (RandomNumberGenerator randomNumberGenerator = RandomNumberGenerator.Create())
			{
				randomNumberGenerator.GetBytes(array);
			}
			byte[] array2 = Derive(replacement, array);
			try
			{
				AccessRecord accessRecord2 = accessRecord ?? new AccessRecord();
				accessRecord2.Salt = Convert.ToBase64String(array);
				accessRecord2.Verifier = Convert.ToBase64String(array2);
				accessRecord2.SecurityStamp = Guid.NewGuid().ToString("N");
				Write(accessRecord2);
			}
			finally
			{
				Array.Clear(array2, 0, array2.Length);
			}
			verifiedCredential = null;
			SessionUnlocked = true;
		}
	}

	public static void RemovePassword(string current, string factor = null)
	{
		if (!Enabled)
		{
			return;
		}
		if (!AuthenticateAt(current, factor, DateTime.UtcNow, forceMfa: true))
		{
			throw new UnauthorizedAccessException("Password or MFA/recovery code is incorrect, expired or already used.");
		}
		string text = verifiedCredential;
		using (new FileStream(Store + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
		{
			AccessRecord accessRecord = Read();
			if (accessRecord == null || Identity(accessRecord) != text)
			{
				throw new IOException("App access configuration changed in another process; removal was refused.");
			}
			File.Delete(Store);
			SelfHostedMfa.Clear();
		}
		verifiedCredential = null;
		SessionUnlocked = true;
	}

	private static void MutateMfa(string password, string factor, DateTime now, Action<AccessRecord> mutation)
	{
		if (!Enabled)
		{
			throw new InvalidOperationException("Set an app password before configuring MFA.");
		}
		if (!AuthenticateAt(password, factor, now, forceMfa: true))
		{
			throw new UnauthorizedAccessException("Password or MFA/recovery code is incorrect, expired or already used.");
		}
		string text = verifiedCredential;
		using (new FileStream(Store + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
		{
			AccessRecord accessRecord = Read();
			if (accessRecord == null || Identity(accessRecord) != text)
			{
				throw new IOException("App access configuration changed. Reauthenticate and try again.");
			}
			mutation(accessRecord);
			accessRecord.SecurityStamp = Guid.NewGuid().ToString("N");
			Write(accessRecord);
		}
		verifiedCredential = null;
	}

	internal static void ChangeSecurityConfiguration(string password, string factor, Action change)
	{
		MutateMfa(password, factor, DateTime.UtcNow, delegate { change(); });
	}

	public static string[] EnrollMfa(string password, string oldFactor, string secret, string confirmation, DateTime utcNow)
	{
		byte[] array = Totp.Decode(secret);
		try
		{
			if (array.Length != 20)
			{
				throw new ArgumentException("Use a 160-bit authenticator secret.");
			}
		}
		finally
		{
			Array.Clear(array, 0, array.Length);
		}
		long step = Totp.Match(secret, confirmation, utcNow, -1L);
		if (step < 0)
		{
			throw new ArgumentException("The new authenticator code is invalid or expired. Enrollment was not changed.");
		}
		string[] codes = Totp.NewRecoveryCodes();
		MutateMfa(password, oldFactor, utcNow, delegate(AccessRecord record)
		{
			record.MfaSecret = secret;
			record.MfaFailures = 0;
			record.MfaBlockedUntilUtcTicks = 0L;
			record.AskMfaOnSignIn = true;
			record.LastTotpStep = step;
			record.RecoveryHashes = codes.Select(Totp.RecoveryHash).ToArray();
		});
		return codes;
	}

	public static void SetMfaPrompt(string password, string factor, bool ask, DateTime utcNow)
	{
		MutateMfa(password, factor, utcNow, delegate(AccessRecord record)
		{
			if (record.MfaSecret == null)
			{
				throw new InvalidOperationException("Set up an authenticator first.");
			}
			record.AskMfaOnSignIn = ask;
		});
	}

	public static void ForgetMfa(string password, string factor, DateTime utcNow)
	{
		MutateMfa(password, factor, utcNow, delegate(AccessRecord record)
		{
			SelfHostedMfa.Clear();
			record.MfaSecret = null;
			record.MfaFailures = 0;
			record.MfaBlockedUntilUtcTicks = 0L;
			record.AskMfaOnSignIn = false;
			record.LastTotpStep = -1L;
			record.RecoveryHashes = new string[0];
		});
	}

	public static string[] RegenerateRecovery(string password, string factor, DateTime utcNow)
	{
		string[] codes = Totp.NewRecoveryCodes();
		MutateMfa(password, factor, utcNow, delegate(AccessRecord record)
		{
			if (record.MfaSecret == null)
			{
				throw new InvalidOperationException("Set up an authenticator first.");
			}
			record.RecoveryHashes = codes.Select(Totp.RecoveryHash).ToArray();
		});
		return codes;
	}

	public static void Lock()
	{
		verifiedCredential = null;
		SessionUnlocked = false;
	}

	private static string ReadConsoleSecret(string label, int maximum)
	{
		Console.Write(label);
		StringBuilder stringBuilder = new StringBuilder();
		while (true)
		{
			ConsoleKeyInfo consoleKeyInfo2;
			ConsoleKeyInfo consoleKeyInfo = (consoleKeyInfo2 = Console.ReadKey(intercept: true));
			if (consoleKeyInfo.Key == ConsoleKey.Enter)
			{
				break;
			}
			if (consoleKeyInfo2.Key == ConsoleKey.Escape)
			{
				Console.WriteLine();
				stringBuilder.Clear();
				return null;
			}
			if (consoleKeyInfo2.Key == ConsoleKey.Backspace)
			{
				if (stringBuilder.Length > 0)
				{
					stringBuilder.Length--;
				}
			}
			else if (!char.IsControl(consoleKeyInfo2.KeyChar) && stringBuilder.Length < maximum)
			{
				stringBuilder.Append(consoleKeyInfo2.KeyChar);
			}
		}
		Console.WriteLine();
		string result = stringBuilder.ToString();
		stringBuilder.Clear();
		return result;
	}

	public static bool ConsoleUnlock()
	{
		if (!Enabled)
		{
			return true;
		}
		if (Console.IsInputRedirected)
		{
			throw new UnauthorizedAccessException("App password enabled: interactive unlock is required. Passwords and MFA codes are never accepted in command-line arguments or environment variables.");
		}
		for (int i = 0; i < 3; i++)
		{
			string text = ReadConsoleSecret("FishTank app password: ", 1024);
			string text2 = null;
			if (text == null)
			{
				return false;
			}
			try
			{
				if (Mfa.AskOnSignIn)
				{
					text2 = ReadConsoleSecret("Authenticator or one-use recovery code: ", 128);
					if (text2 == null)
					{
						return false;
					}
				}
				if (Authenticate(text, text2))
				{
					return true;
				}
			}
			finally
			{
				text = null;
				text2 = null;
			}
			Console.Error.WriteLine("Password or MFA/recovery code is incorrect, expired or already used.");
		}
		return false;
	}
}
