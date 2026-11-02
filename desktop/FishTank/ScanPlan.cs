using System;
using System.Linq;

namespace FishTank;

public sealed class ScanPlan
{
	public int Version = 1;

	public int MaximumFiles = 10000;

	public int DelayMilliseconds;

	public long MaximumFileBytes;

	public bool HashFiles = true;

	public string Name = "Custom scan";

	public string[] Targets = new string[0];

	public string[] ExcludedPaths = new string[0];

	public void Validate()
	{
		if (Version != 1 || string.IsNullOrWhiteSpace(Name) || Name.Length > 64 || Targets == null || Targets.Length < 1 || Targets.Length > 100 || ExcludedPaths == null || ExcludedPaths.Length > 100 || MaximumFiles < 1 || MaximumFiles > 100000 || DelayMilliseconds < 0 || DelayMilliseconds > 10000 || MaximumFileBytes < 0 || MaximumFileBytes > 1099511627776L)
		{
			throw new ArgumentException("Invalid scan plan. Use 1–100 targets, at most 100 exclusions, 1–100,000 files and a delay of 0–10,000 ms.");
		}
		foreach (string item in Targets.Concat(ExcludedPaths))
		{
			ScanPaths.Normalize(item);
		}
	}

	public ScanPlan Copy()
	{
		return Core.Parse<ScanPlan>(Core.Json(this));
	}
}
