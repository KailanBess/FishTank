using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FishTank;

public static class Totp
{
	private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

	public static string NewSecret()
	{
		byte[] array = new byte[20];
		using (RandomNumberGenerator randomNumberGenerator = RandomNumberGenerator.Create())
		{
			randomNumberGenerator.GetBytes(array);
		}
		try
		{
			return Encode(array);
		}
		finally
		{
			Array.Clear(array, 0, array.Length);
		}
	}

	public static string Encode(byte[] bytes)
	{
		StringBuilder stringBuilder = new StringBuilder();
		int num = 0;
		int num2 = 0;
		foreach (byte b in bytes)
		{
			num = (num << 8) | b;
			num2 += 8;
			while (num2 >= 5)
			{
				num2 -= 5;
				stringBuilder.Append("ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"[(num >> num2) & 0x1F]);
			}
		}
		if (num2 > 0)
		{
			stringBuilder.Append("ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"[(num << 5 - num2) & 0x1F]);
		}
		return stringBuilder.ToString();
	}

	public static byte[] Decode(string encoded)
	{
		if (string.IsNullOrEmpty(encoded) || encoded.Length > 128 || !Regex.IsMatch(encoded, "\\A[A-Z2-7]+\\z"))
		{
			throw new ArgumentException("Invalid authenticator secret.");
		}
		List<byte> list = new List<byte>();
		int num = 0;
		int num2 = 0;
		foreach (char value in encoded)
		{
			num = (num << 5) | "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567".IndexOf(value);
			num2 += 5;
			if (num2 >= 8)
			{
				num2 -= 8;
				list.Add((byte)(num >> num2));
			}
		}
		if (num2 > 0 && (num & ((1 << num2) - 1)) != 0)
		{
			throw new ArgumentException("Non-canonical authenticator secret.");
		}
		byte[] array = list.ToArray();
		if (Encode(array) != encoded)
		{
			throw new ArgumentException("Non-canonical authenticator secret.");
		}
		return array;
	}

	public static long Step(DateTime utcNow)
	{
		long num = utcNow.ToUniversalTime().Ticks - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
		if (num < 0)
		{
			throw new ArgumentException("Authenticator time must follow the Unix epoch.");
		}
		return num / 10000000 / 30;
	}

	public static string Code(byte[] secret, long step, int digits)
	{
		if (secret == null || secret.Length < 16 || step < 0 || (digits != 6 && digits != 8))
		{
			throw new ArgumentException("Invalid TOTP parameters.");
		}
		byte[] array = new byte[8];
		ulong num = (ulong)step;
		for (int num2 = 7; num2 >= 0; num2--)
		{
			array[num2] = (byte)num;
			num >>= 8;
		}
		byte[] array2;
		using (HMACSHA1 hMACSHA = new HMACSHA1(secret))
		{
			array2 = hMACSHA.ComputeHash(array);
		}
		try
		{
			int num3 = array2[array2.Length - 1] & 0xF;
			int num4 = ((array2[num3] & 0x7F) << 24) | (array2[num3 + 1] << 16) | (array2[num3 + 2] << 8) | array2[num3 + 3];
			return (num4 % ((digits == 6) ? 1000000 : 100000000)).ToString((digits == 6) ? "D6" : "D8", CultureInfo.InvariantCulture);
		}
		finally
		{
			Array.Clear(array2, 0, array2.Length);
		}
	}

	public static string Code(string secret, DateTime utcNow)
	{
		byte[] array = Decode(secret);
		try
		{
			return Code(array, Step(utcNow), 6);
		}
		finally
		{
			Array.Clear(array, 0, array.Length);
		}
	}

	public static long Match(string secret, string code, DateTime utcNow, long lastAccepted)
	{
		if (!Regex.IsMatch(code ?? "", "\\A[0-9]{6}\\z"))
		{
			return -1L;
		}
		byte[] array = Decode(secret);
		try
		{
			long num = Step(utcNow);
			long result = -1L;
			for (long num2 = Math.Max(0L, num - 1); num2 <= num + 1; num2++)
			{
				string text = Code(array, num2, 6);
				int num3 = 0;
				for (int i = 0; i < 6; i++)
				{
					num3 |= text[i] ^ code[i];
				}
				if (num3 == 0 && num2 > lastAccepted)
				{
					result = num2;
				}
			}
			return result;
		}
		finally
		{
			Array.Clear(array, 0, array.Length);
		}
	}

	public static string EnrollmentUri(string secret, string account)
	{
		byte[] array = Decode(secret);
		try
		{
			if (array.Length != 20)
			{
				throw new ArgumentException("Authenticator secret must be 160 bits.");
			}
		}
		finally
		{
			Array.Clear(array, 0, array.Length);
		}
		return "otpauth://totp/" + Uri.EscapeDataString("FishTank:" + account) + "?secret=" + secret + "&issuer=FishTank&algorithm=SHA1&digits=6&period=30";
	}

	public static string NormalizeRecovery(string code)
	{
		return Regex.Replace((code ?? "").ToUpperInvariant(), "[- ]", "");
	}

	public static string RecoveryHash(string code)
	{
		string text = NormalizeRecovery(code);
		if (!Regex.IsMatch(text, "\\A[A-Z2-7]{26}\\z"))
		{
			return null;
		}
		byte[] bytes = Encoding.UTF8.GetBytes("FishTankRecovery:v1:" + text);
		try
		{
			using SHA256 sHA = SHA256.Create();
			return BitConverter.ToString(sHA.ComputeHash(bytes)).Replace("-", "");
		}
		finally
		{
			Array.Clear(bytes, 0, bytes.Length);
		}
	}

	public static string[] NewRecoveryCodes()
	{
		string[] array = new string[10];
		using RandomNumberGenerator randomNumberGenerator = RandomNumberGenerator.Create();
		for (int i = 0; i < array.Length; i++)
		{
			byte[] array2 = new byte[16];
			randomNumberGenerator.GetBytes(array2);
			string text = Encode(array2);
			Array.Clear(array2, 0, array2.Length);
			array[i] = text.Substring(0, 6) + "-" + text.Substring(6, 6) + "-" + text.Substring(12, 6) + "-" + text.Substring(18, 8);
		}
		return array;
	}

	public static bool FixedEquals(string first, string second)
	{
		if (first == null || second == null || first.Length != second.Length)
		{
			return false;
		}
		int num = 0;
		for (int i = 0; i < first.Length; i++)
		{
			num |= first[i] ^ second[i];
		}
		return num == 0;
	}
}
