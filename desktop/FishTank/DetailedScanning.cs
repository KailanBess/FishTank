using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

namespace FishTank;

public static class DetailedScanning
{
	public static List<ScanItem> Inventory(ScanPlan plan, CancellationToken cancellation, IProgress<ScanProgress> progress)
	{
		plan.Validate();
		List<ScanItem> list = new List<ScanItem>();
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		Stack<string> stack = new Stack<string>(plan.Targets.Reverse());
		int num = 0;
		int num2 = 0;
		while (stack.Count > 0)
		{
			cancellation.ThrowIfCancellationRequested();
			string path = ScanPaths.Normalize(stack.Pop());
			if (!hashSet.Add(path))
			{
				continue;
			}
			if (++num2 > plan.MaximumFiles * 4 + 1000)
			{
				list.Add(new ScanItem
				{
					Path = path,
					State = "Skipped",
					Reason = "Inventory entry limit reached; remaining targets were not inspected."
				});
				break;
			}
			if (plan.ExcludedPaths.Any((string root) => ScanPaths.Within(path, root)))
			{
				list.Add(new ScanItem
				{
					Path = path,
					State = "Skipped",
					Reason = "Excluded by this scan plan (Windows Defender exclusions were not changed)."
				});
				continue;
			}
			try
			{
				ScanPaths.RefuseReparseAncestors(path);
				FileAttributes attributes = File.GetAttributes(path);
				if ((attributes & FileAttributes.Directory) != 0)
				{
					using IEnumerator<string> enumerator = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
					while (enumerator.MoveNext())
					{
						cancellation.ThrowIfCancellationRequested();
						if (stack.Count >= plan.MaximumFiles * 4 + 1000)
						{
							list.Add(new ScanItem
							{
								Path = path,
								State = "Skipped",
								Reason = "Pending-entry limit reached; directory enumeration is incomplete."
							});
							break;
						}
						stack.Push(enumerator.Current);
					}
				}
				else
				{
					if (num >= plan.MaximumFiles)
					{
						list.Add(new ScanItem
						{
							Path = path,
							State = "Skipped",
							Reason = "Maximum file count reached; remaining targets were not inspected."
						});
						break;
					}
					FileInfo fileInfo = new FileInfo(path);
					if (plan.MaximumFileBytes > 0 && fileInfo.Length > plan.MaximumFileBytes)
					{
						list.Add(new ScanItem
						{
							Path = path,
							Length = fileInfo.Length,
							State = "Skipped",
							Reason = "Exceeds the selected per-file size limit."
						});
					}
					else
					{
						list.Add(new ScanItem
						{
							Path = path,
							Length = fileInfo.Length
						});
						num++;
					}
				}
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex2)
			{
				list.Add(new ScanItem
				{
					Path = path,
					State = "Error",
					Reason = "Could not enumerate or access this path: " + ex2.Message
				});
			}
			if (progress != null && num2 % 100 == 0)
			{
				progress.Report(new ScanProgress
				{
					Phase = "Inventory",
					CurrentPath = path,
					Completed = num,
					Total = 0
				});
			}
		}
		return list;
	}

	public static DetailedReport Run(ScanPlan plan, CancellationToken cancellation, ManualResetEventSlim resume, IProgress<ScanProgress> progress, FileScanEngine engine)
	{
		plan.Validate();
		if (!Core.BeginScan())
		{
			throw new InvalidOperationException("A FishTank scan is already running in this process.");
		}
		DetailedReport detailedReport = new DetailedReport();
		detailedReport.Plan = plan.Copy();
		DetailedReport detailedReport2 = detailedReport;
		DateTime utcNow = DateTime.UtcNow;
		try
		{
			ScanReports.Save(detailedReport2);
			detailedReport2.Items = Inventory(plan, cancellation, progress);
			List<ScanItem> list = detailedReport2.Items.Where((ScanItem i) => i.State == "Pending").ToList();
			int num = 0;
			progress?.Report(new ScanProgress
			{
				Phase = "Queued",
				CurrentPath = "",
				Completed = 0,
				Total = list.Count
			});
			foreach (ScanItem item in list)
			{
				resume.Wait(cancellation);
				cancellation.ThrowIfCancellationRequested();
				progress?.Report(new ScanProgress
				{
					Phase = "Scanning",
					CurrentPath = item.Path,
					Completed = num,
					Total = list.Count
				});
				item.StartedUtc = DateTime.UtcNow.ToString("o");
				try
				{
					ScanPaths.RefuseReparseAncestors(item.Path);
					long length = new FileInfo(item.Path).Length;
					if (plan.MaximumFileBytes > 0 && length > plan.MaximumFileBytes)
					{
						item.State = "Skipped";
						item.Reason = "File grew beyond the selected size limit.";
						continue;
					}
					item.Length = length;
					string text = (plan.HashFiles ? HashFile(item.Path, cancellation) : "");
					CommandResult commandResult = (engine ?? new FileScanEngine(NativeEngine))(item.Path, cancellation);
					item.EngineExitCode = commandResult.ExitCode;
					item.Output = Clip(commandResult.Output, ref item.OutputTruncated);
					item.Error = Clip(commandResult.Error, ref item.OutputTruncated);
					item.State = ((commandResult.ExitCode == 0) ? "EngineCompleted" : ((commandResult.ExitCode == 2) ? "ReviewRequired" : "Error"));
					item.Reason = ((commandResult.ExitCode == 0) ? "Defender completed this custom scan. This is not proof the device is clean." : ((commandResult.ExitCode == 2) ? "Defender returned 2: malware/action-required OR scan errors. Review the evidence; no exact threat count is inferred." : "Defender returned an error or unrecognized status."));
					if (plan.HashFiles)
					{
						string text2 = HashFile(item.Path, cancellation);
						if (text != text2)
						{
							item.State = "ReviewRequired";
							item.Reason = "File content changed during the scan; the result cannot be bound to a stable SHA-256 snapshot.";
						}
						else
						{
							item.Sha256 = text2;
						}
					}
				}
				catch (OperationCanceledException)
				{
					item.State = "Canceled";
					item.Reason = "Queue canceled. A delegated Defender operation may continue; no clean result is inferred.";
					throw;
				}
				catch (Exception ex2)
				{
					item.State = "Error";
					item.Reason = ex2.Message;
				}
				finally
				{
					item.EndedUtc = DateTime.UtcNow.ToString("o");
					num++;
				}
				if ((DateTime.UtcNow - utcNow).TotalSeconds >= 30.0)
				{
					ScanReports.Save(detailedReport2);
					utcNow = DateTime.UtcNow;
				}
				if (plan.DelayMilliseconds > 0 && cancellation.WaitHandle.WaitOne(plan.DelayMilliseconds))
				{
					cancellation.ThrowIfCancellationRequested();
				}
			}
			detailedReport2.State = ((detailedReport2.Errors > 0 || detailedReport2.Skipped > 0 || detailedReport2.Review > 0) ? "CompletedWithGapsOrReview" : "Completed");
		}
		catch (OperationCanceledException)
		{
			detailedReport2.State = "Canceled";
			foreach (ScanItem item2 in detailedReport2.Items.Where((ScanItem i) => i.State == "Pending"))
			{
				item2.State = "Skipped";
				item2.Reason = "Not scanned because the queue was canceled.";
			}
		}
		catch (Exception ex4)
		{
			detailedReport2.State = "Failed";
			detailedReport2.Failure = ex4.Message;
			foreach (ScanItem item3 in detailedReport2.Items.Where((ScanItem i) => i.State == "Pending"))
			{
				item3.State = "Skipped";
				item3.Reason = "Not scanned because the job failed.";
			}
		}
		finally
		{
			detailedReport2.EndedUtc = DateTime.UtcNow.ToString("o");
			try
			{
				ScanReports.Save(detailedReport2);
				try
				{
					Core.Audit("scan-complete", new
					{
						kind = "Detailed",
						id = detailedReport2.Id,
						state = detailedReport2.State,
						engineCalls = detailedReport2.Scanned,
						review = detailedReport2.Review,
						skipped = detailedReport2.Skipped,
						errors = detailedReport2.Errors
					});
				}
				catch (Exception ex5)
				{
					detailedReport2.Failure = "Scan ended, but the audit summary could not be saved: " + ex5.Message;
					if (detailedReport2.State == "Completed")
					{
						detailedReport2.State = "CompletedWithGapsOrReview";
					}
					ScanReports.Save(detailedReport2);
				}
			}
			finally
			{
				Core.EndScan();
			}
		}
		progress?.Report(new ScanProgress
		{
			Phase = detailedReport2.State,
			CurrentPath = "",
			Completed = detailedReport2.Scanned,
			Total = detailedReport2.Items.Count
		});
		return detailedReport2;
	}

	private static string Clip(string value, ref bool truncated)
	{
		value = value ?? "";
		if (value.Length <= 4096)
		{
			return value;
		}
		truncated = true;
		return value.Substring(0, 4096) + "\n[output truncated]";
	}

	public static string HashFile(string path, CancellationToken cancellation)
	{
		using FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
		using SHA256 sHA = SHA256.Create();
		byte[] array = new byte[1048576];
		try
		{
			int inputCount;
			while ((inputCount = fileStream.Read(array, 0, array.Length)) > 0)
			{
				cancellation.ThrowIfCancellationRequested();
				sHA.TransformBlock(array, 0, inputCount, array, 0);
			}
			cancellation.ThrowIfCancellationRequested();
			sHA.TransformFinalBlock(new byte[0], 0, 0);
			return BitConverter.ToString(sHA.Hash).Replace("-", "").ToLowerInvariant();
		}
		finally
		{
			Array.Clear(array, 0, array.Length);
		}
	}

	private static CommandResult NativeEngine(string path, CancellationToken cancellation)
	{
		return Core.RunCancelable(Core.Defender(), "-Scan -ScanType 3 -File " + Core.Quote(path) + " -DisableRemediation", 86400000, cancellation);
	}
}
