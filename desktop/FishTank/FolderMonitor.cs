using System;
using System.Collections.Generic;
using System.IO;

namespace FishTank;

public sealed class FolderMonitor : IDisposable
{
	private FileSystemWatcher watcher;

	private readonly Queue<DateTime> times = new Queue<DateTime>();

	private DateTime lastAlert = DateTime.MinValue;

	private readonly object gate = new object();

	public void Start(string path)
	{
		Dispose();
		path = Path.GetFullPath(path);
		if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
		{
			throw new IOException("Reparse root refused");
		}
		watcher = new FileSystemWatcher(path)
		{
			IncludeSubdirectories = false,
			NotifyFilter = (NotifyFilters.FileName | NotifyFilters.LastWrite),
			InternalBufferSize = 16384
		};
		watcher.Changed += OnEvent;
		watcher.Created += OnEvent;
		watcher.Renamed += OnEvent;
		watcher.Error += delegate(object s, ErrorEventArgs e)
		{
			try
			{
				Core.Audit("monitor-gap", e.GetException().Message);
			}
			catch
			{
			}
		};
		watcher.EnableRaisingEvents = true;
		Core.Audit("monitor-start", new
		{
			path = path,
			scope = "Selected folder only, nonrecursive; alerting only"
		});
	}

	private void OnEvent(object sender, FileSystemEventArgs e)
	{
		lock (gate)
		{
			DateTime utcNow = DateTime.UtcNow;
			times.Enqueue(utcNow);
			while (times.Count > 0 && (utcNow - times.Peek()).TotalSeconds > 10.0)
			{
				times.Dequeue();
			}
			if (times.Count >= 100 && (utcNow - lastAlert).TotalMinutes >= 1.0)
			{
				lastAlert = utcNow;
				try
				{
					Core.Audit("file-burst-low-confidence", new
					{
						count = times.Count,
						message = "High file event volume. Backups, builds and sync can cause this. This is not a ransomware verdict."
					});
				}
				catch
				{
				}
			}
			while (times.Count > 1000)
			{
				times.Dequeue();
			}
		}
	}

	public void Dispose()
	{
		if (watcher != null)
		{
			watcher.Dispose();
			watcher = null;
		}
	}
}
