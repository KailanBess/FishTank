using System;
using System.IO;

namespace FishTank;

public sealed class PasswordEntry
{
	public string Id = Guid.NewGuid().ToString("N");

	public string Title = "";

	public string Username = "";

	public string Password = "";

	public string Website = "";

	public string Notes = "";

	public string UpdatedUtc = DateTime.UtcNow.ToString("o");

	public void Validate()
	{
		if (!Guid.TryParseExact(Id, "N", out var _) || string.IsNullOrWhiteSpace(Title) || Title.Length > 256 || Username == null || Username.Length > 1024 || Password == null || Password.Length < 1 || Password.Length > 65536 || Website == null || Website.Length > 2048 || Notes == null || Notes.Length > 10000)
		{
			throw new InvalidDataException("Invalid password entry or field size.");
		}
	}

	public PasswordEntry Copy()
	{
		return Core.Parse<PasswordEntry>(Core.Json(this));
	}
}
