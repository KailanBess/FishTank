using System.Collections.Generic;

namespace FishTank;

public sealed class PasswordPayload
{
	public int Version = 2;

	public List<PasswordEntry> Entries = new List<PasswordEntry>();
}
