using System;
using System.IO;

namespace FishTank;

public sealed class PasswordEntry
{
	public string Id = Guid.NewGuid().ToString("N");

	public string Title = "";

	public string Username = "";

	public string Kind = "Service";
	public string Email = "";
	public string LinkedEmailId = "";
	public bool UsesLinkedSignIn;

	public string Password = "";

	public string Website = "";

	public string Notes = "";

	public string UpdatedUtc = DateTime.UtcNow.ToString("o");
    public string ExpiresUtc;
    public System.Collections.Generic.List<PasswordRevision> History = new System.Collections.Generic.List<PasswordRevision>();
    public System.Collections.Generic.List<VaultAttachment> Attachments = new System.Collections.Generic.List<VaultAttachment>();

	public void Validate()
	{
        if (History == null || History.Count > 5 || Attachments == null || Attachments.Count > 5
            || History.Exists(x => x == null || x.Password == null || x.Password.Length > 65536 || !DateTime.TryParse(x.ChangedUtc, out _))
            || (!string.IsNullOrEmpty(ExpiresUtc) && !DateTime.TryParse(ExpiresUtc, out _))) throw new InvalidDataException("Invalid password history, expiry or attachment metadata.");
        foreach (var attachment in Attachments) { if (attachment == null) throw new InvalidDataException("Invalid attachment."); attachment.Validate(); }
		if (!Guid.TryParseExact(Id, "N", out var _) || string.IsNullOrWhiteSpace(Title) || Title.Length > 256 || Username == null || Username.Length > 1024 || Password == null || (!UsesLinkedSignIn && Password.Length < 1) || Password.Length > 65536 || Website == null || Website.Length > 2048 || Notes == null || Notes.Length > 10000)
		{
			throw new InvalidDataException("Invalid password entry or field size.");
		}
		if ((Kind != "Email" && Kind != "Service") || Email == null || Email.Length > 320 || LinkedEmailId == null
			|| (LinkedEmailId != "" && (!Guid.TryParseExact(LinkedEmailId, "N", out _) || LinkedEmailId == Id))
			|| (Kind == "Email" && (string.IsNullOrWhiteSpace(Email) || LinkedEmailId != "" || UsesLinkedSignIn))
			|| (UsesLinkedSignIn && (Kind != "Service" || LinkedEmailId == "" || Password != "")))
			throw new InvalidDataException("Choose Email or Service. Email accounts need an address; linked sign-in needs an email connection and no separate password.");
	}

	public PasswordEntry Copy()
	{
		return Core.Parse<PasswordEntry>(Core.Json(this));
	}
}
