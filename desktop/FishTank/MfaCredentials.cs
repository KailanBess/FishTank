using System;

namespace FishTank;

public sealed class MfaCredentials : IDisposable
{
	public string Password;

	public string Factor;

	public void Dispose()
	{
		Password = null;
		Factor = null;
	}
}
