using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace FishTank;

public static class PasswordGenerator
{
	public const int MaximumLength = 65536;

	public static string Generate(GeneratorOptions options)
	{
		if (options == null || options.Length < 1 || options.Length > 65536)
		{
			throw new ArgumentException("Choose a password length from 1 to 65,536 characters.");
		}
		List<string> list = new List<string>();
		if (options.Lower)
		{
			list.Add("abcdefghijklmnopqrstuvwxyz");
		}
		if (options.Upper)
		{
			list.Add("ABCDEFGHIJKLMNOPQRSTUVWXYZ");
		}
		if (options.Digits)
		{
			list.Add("0123456789");
		}
		if (options.Symbols)
		{
			string symbolCharacters = options.SymbolCharacters;
			if (string.IsNullOrEmpty(symbolCharacters) || symbolCharacters.Length > 94 || symbolCharacters.Any((char c) => c < '!' || c > '~' || char.IsLetterOrDigit(c)))
			{
				throw new ArgumentException("Symbols must contain 1–94 printable ASCII punctuation characters.");
			}
			list.Add(new string(symbolCharacters.Distinct().ToArray()));
		}
		if (options.ExcludeAmbiguous)
		{
			for (int num = 0; num < list.Count; num++)
			{
				list[num] = new string(list[num].Where((char c) => "Il1O0o|`'\"".IndexOf(c) < 0).ToArray());
			}
		}
		if (list.Count == 0 || list.Any((string s) => s.Length == 0) || options.Length < list.Count)
		{
			throw new ArgumentException("Enable a nonempty character set and choose at least one character per selected set.");
		}
		string text = string.Concat(list.ToArray());
		int num2 = 256 - 256 % text.Length;
		byte[] array = new byte[4096];
		char[] array2 = new char[options.Length];
		try
		{
			using RandomNumberGenerator randomNumberGenerator = RandomNumberGenerator.Create();
			int num3 = array.Length;
			for (int num4 = 0; num4 < 10000; num4++)
			{
				int num5 = 0;
				while (num5 < array2.Length)
				{
					if (num3 == array.Length)
					{
						randomNumberGenerator.GetBytes(array);
						num3 = 0;
					}
					int num6 = array[num3++];
					if (num6 < num2)
					{
						array2[num5++] = text[num6 % text.Length];
					}
				}
				bool flag = true;
				foreach (string group in list)
				{
					if (!array2.Any((char c) => group.IndexOf(c) >= 0))
					{
						flag = false;
						break;
					}
				}
				if (flag)
				{
					return new string(array2);
				}
			}
			throw new CryptographicException("Could not generate a password meeting the requested sets.");
		}
		finally
		{
			Array.Clear(array, 0, array.Length);
			Array.Clear(array2, 0, array2.Length);
		}
	}
}
