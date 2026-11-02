namespace FishTank;

public sealed class ScanItem
{
	public string Path;

	public string State = "Pending";

	public string Reason = "";

	public string Sha256 = "";

	public string Output = "";

	public string Error = "";

	public string StartedUtc;

	public string EndedUtc;

	public long Length;

	public int? EngineExitCode;

	public bool OutputTruncated;
}
