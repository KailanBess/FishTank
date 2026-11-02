namespace FishTank;

public sealed class AccessRecord
{
	public int Version = 2;

	public int Iterations = 600000;

	public int Failures;

	public string Salt;

	public string Verifier;

	public long BlockedUntilUtcTicks;

	public int MfaFailures;

	public long MfaBlockedUntilUtcTicks;

	public string SecurityStamp = "";

	public string MfaSecret;

	public bool AskMfaOnSignIn;

	public long LastTotpStep = -1L;

	public string[] RecoveryHashes = new string[0];
}
