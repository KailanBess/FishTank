using System;
using System.Collections.Generic;
using System.Linq;

namespace FishTank;

public sealed class DetailedReport
{
	public int Version = 1;

	public string Id = Guid.NewGuid().ToString("N");

	public string StartedUtc = DateTime.UtcNow.ToString("o");

	public string EndedUtc;

	public string State = "Running";

	public string Engine = "Microsoft Defender (custom, no remediation)";

	public string Failure = "";

	public ScanPlan Plan;

	public List<ScanItem> Items = new List<ScanItem>();

	public int Scanned => Items.Count((ScanItem i) => i.EngineExitCode.HasValue);

	public int Skipped => Items.Count((ScanItem i) => i.State == "Skipped");

	public int Review => Items.Count((ScanItem i) => i.State == "ReviewRequired");

	public int Errors => Items.Count((ScanItem i) => i.State == "Error");
}
