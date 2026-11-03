using System;
using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace FishTank;

public static class Core
{
	public static readonly string Base = AppDomain.CurrentDomain.BaseDirectory;

	public static string Data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FishTank");

	private static readonly object Gate = new object();

	private static readonly SemaphoreSlim ScanGate = new SemaphoreSlim(1, 1);

	public static bool Admin => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

	private static JavaScriptSerializer Serializer()
	{
		JavaScriptSerializer javaScriptSerializer = new JavaScriptSerializer();
		javaScriptSerializer.MaxJsonLength = 157286400;
		javaScriptSerializer.RecursionLimit = 64;
		return javaScriptSerializer;
	}

	public static string Json(object o)
	{
		return Serializer().Serialize(o);
	}

	public static T Parse<T>(string s)
	{
		return Serializer().Deserialize<T>(s);
	}

	public static void Init()
	{
		RestrictDirectory(Data);
	}

	internal static void RestrictDirectory(string path)
	{
		Directory.CreateDirectory(path);
		DirectorySecurity directorySecurity = new DirectorySecurity();
		directorySecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
		SecurityIdentifier[] array = new SecurityIdentifier[3]
		{
			WindowsIdentity.GetCurrent().User,
			new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
			new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null)
		};
		foreach (SecurityIdentifier identity in array)
		{
			directorySecurity.AddAccessRule(new FileSystemAccessRule(identity, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
		}
		Directory.SetAccessControl(path, directorySecurity);
	}

	public static void Audit(string action, object detail)
	{
		lock (Gate)
		{
			Directory.CreateDirectory(Data);
			string text = Path.Combine(Data, "events-" + DateTime.UtcNow.ToString("yyyy-MM-dd") + ".jsonl");
			if (File.Exists(text) && new FileInfo(text).Length > 20971520)
			{
				throw new IOException("Daily audit size limit reached.");
			}
			File.AppendAllText(text, Json(new
			{
				time = DateTime.UtcNow.ToString("o"),
				action = action,
				detail = detail
			}) + Environment.NewLine, Encoding.UTF8);
		}
	}

	public static string Quote(string s)
	{
		if (s == null || s.IndexOfAny(new char[4] { '"', '\r', '\n', '\0' }) >= 0)
		{
			throw new ArgumentException("Invalid command argument");
		}
		int num = 0;
		int num2 = s.Length - 1;
		while (num2 >= 0 && s[num2] == '\\')
		{
			num++;
			num2--;
		}
		return "\"" + s + new string('\\', num) + "\"";
	}

	public static CommandResult Run(string exe, string args, int timeout)
	{
		return RunCancelable(exe, args, timeout, CancellationToken.None);
	}

	public static CommandResult RunCancelable(string exe, string args, int timeout, CancellationToken cancellation, string standardInput = null)
	{
		cancellation.ThrowIfCancellationRequested();
		using Process process = new Process();
		process.StartInfo = new ProcessStartInfo(exe, args)
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};
        process.StartInfo.RedirectStandardInput = standardInput != null;
		StringBuilder output = new StringBuilder();
        // A PowerShell 7 launcher can pass its incompatible module path to this .NET process.
        // Scope module selection to the Windows PowerShell child, without changing the user's environment.
        string windowsPowerShell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell\\v1.0\\powershell.exe");
        if (string.Equals(Path.GetFullPath(exe), windowsPowerShell, StringComparison.OrdinalIgnoreCase))
            process.StartInfo.EnvironmentVariables["PSModulePath"] = Path.Combine(Path.GetDirectoryName(windowsPowerShell), "Modules")
                + Path.PathSeparator + Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsPowerShell\\Modules");
		StringBuilder error = new StringBuilder();
		object sync = new object();
		DataReceivedEventHandler value = delegate(object s, DataReceivedEventArgs e)
		{
			if (e.Data != null)
			{
				lock (sync)
				{
					if (output.Length < 2097152)
					{
						output.AppendLine(e.Data);
					}
				}
			}
		};
		process.OutputDataReceived += value;
		process.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
		{
			if (e.Data != null)
			{
				lock (sync)
				{
					if (error.Length < 2097152)
					{
						error.AppendLine(e.Data);
					}
				}
			}
		};
		process.Start();
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();
        Task inputWriter = null;
        if (standardInput != null) inputWriter = Task.Run(() => {
            using (var input = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false))) input.Write(standardInput);
        });
		Stopwatch stopwatch = Stopwatch.StartNew();
		while (!process.WaitForExit(100))
		{
			if (cancellation.IsCancellationRequested || stopwatch.ElapsedMilliseconds >= timeout)
			{
				try
				{
					process.Kill();
				}
				catch
				{
				}
				process.Dispose();
				cancellation.ThrowIfCancellationRequested();
				throw new TimeoutException("Command timed out; a delegated Windows operation may continue.");
			}
		}
		process.WaitForExit();
        if (inputWriter != null) inputWriter.GetAwaiter().GetResult();
		CommandResult commandResult = new CommandResult();
		commandResult.ExitCode = process.ExitCode;
		commandResult.Output = output.ToString();
		commandResult.Error = error.ToString();
		CommandResult result = commandResult;
		process.Dispose();
		return result;
	}

	public static string Collect(string mode) => Collect(mode, 90000);
	public static string Collect(string mode, int timeoutMilliseconds)
	{
        if (timeoutMilliseconds < 1000 || timeoutMilliseconds > 90000) throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
		if (Array.IndexOf(new string[] { "Health", "Network", "Persistence", "Events", "Tailscale", "Hardening", "Accounts", "Browsers", "ScanPolicy", "SecurityHealth", "StartupInventory", "Permissions", "Exposure", "DiskHealth", "LiveProtection" }, mode) < 0)
		{
			throw new ArgumentException("Unknown collector");
		}
		string exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell\\v1.0\\powershell.exe");
		string text;
		using (Stream stream = typeof(Core).Assembly.GetManifestResourceStream("FishTank.Collect.ps1"))
		{
			using StreamReader streamReader = new StreamReader(stream);
			text = streamReader.ReadToEnd();
		}
        // Specialize the validated mode in the resource itself; never silently fall back to Health.
        const string modeDeclaration = "[string]$Mode='Health'";
        if (text.IndexOf(modeDeclaration, StringComparison.Ordinal) < 0
            || text.IndexOf(modeDeclaration, StringComparison.Ordinal) != text.LastIndexOf(modeDeclaration, StringComparison.Ordinal))
            throw new InvalidDataException("Embedded collector mode declaration was not recognized.");
        text = text.Replace(modeDeclaration, "[string]$Mode='" + mode + "'");
        // Keep the command short even as the embedded collector grows beyond Windows' argv limit.
        string bootstrap = "[Console]::InputEncoding = [System.Text.UTF8Encoding]::new($false); & ([ScriptBlock]::Create([Console]::In.ReadToEnd()))";
        string text2 = Convert.ToBase64String(Encoding.Unicode.GetBytes(bootstrap));
        CommandResult commandResult = RunCancelable(exe, "-NoProfile -NonInteractive -EncodedCommand " + text2, timeoutMilliseconds, CancellationToken.None, text);
		if (commandResult.ExitCode != 0)
		{
			throw new IOException(commandResult.Output + "\n" + commandResult.Error);
		}
		Parse<object[]>(commandResult.Output);
		return commandResult.Output;
	}

	public static string Defender()
	{
		string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft\\Windows Defender\\Platform");
		if (Directory.Exists(path))
		{
			string[] directories = Directory.GetDirectories(path);
			Array.Sort(directories, StringComparer.OrdinalIgnoreCase);
			Array.Reverse(directories);
			string[] array = directories;
			foreach (string path2 in array)
			{
				string text = Path.Combine(path2, "MpCmdRun.exe");
				if (File.Exists(text))
				{
					return text;
				}
			}
		}
		string text2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows Defender\\MpCmdRun.exe");
		if (!File.Exists(text2))
		{
			throw new FileNotFoundException("Microsoft Defender command line scanner unavailable.");
		}
		return text2;
	}

	internal static bool BeginScan()
	{
		return ScanGate.Wait(0);
	}

	internal static void EndScan()
	{
		ScanGate.Release();
	}

	public static string Scan(string kind, string path)
	{
		if (!ScanGate.Wait(0))
		{
			throw new InvalidOperationException("A scan is already active in this process.");
		}
		try
		{
			string args;
			switch (kind)
			{
			case "Quick":
				args = "-Scan -ScanType 1";
				break;
			case "Full":
				args = "-Scan -ScanType 2";
				break;
			case "Custom":
				path = Path.GetFullPath(path);
				if (!File.Exists(path) && !Directory.Exists(path))
				{
					throw new FileNotFoundException(path);
				}
				if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
				{
					throw new IOException("Reparse-point scan roots are refused.");
				}
				args = "-Scan -ScanType 3 -File " + Quote(path) + " -DisableRemediation";
				break;
			default:
				throw new ArgumentException("Unknown scan kind");
			}
			Audit("scan-start", new { kind, path });
			CommandResult commandResult = Run(Defender(), args, 86400000);
			string interpretation = ((commandResult.ExitCode == 0) ? "Defender completed: no malware found OR malware successfully remediated. Review Defender events." : ((commandResult.ExitCode == 2) ? "Defender reports malware not remediated, required action, or scan errors. Review raw output and Defender events." : "Scanner failed or returned an unrecognized status."));
			var anon = new
			{
				kind = kind,
				path = path,
				exitCode = commandResult.ExitCode,
				interpretation = interpretation,
				output = commandResult.Output,
				error = commandResult.Error
			};
			Audit("scan-complete", anon);
			return Json(anon);
		}
		catch (Exception ex)
		{
			Audit("scan-failed", new { kind, errorType = ex.GetType().Name });
			throw;
		}
		finally
		{
			ScanGate.Release();
		}
	}

	public static string Hash(string path)
	{
		using FileStream inputStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
		using SHA256 sHA = SHA256.Create();
		return BitConverter.ToString(sHA.ComputeHash(inputStream)).Replace("-", "").ToLowerInvariant();
	}

	public static string UpdateSignatures()
	{
		Audit("signature-update-request", new { });
		CommandResult commandResult = Run(Defender(), "-SignatureUpdate", 600000);
		Audit("signature-update-result", commandResult);
		return Json(commandResult);
	}

	public static string History()
	{
		StringBuilder stringBuilder = new StringBuilder();
		string[] files = Directory.GetFiles(Data, "events-*.jsonl");
		foreach (string path in files)
		{
			if (stringBuilder.Length > 2097152)
			{
				break;
			}
			stringBuilder.AppendLine(File.ReadAllText(path));
		}
		if (stringBuilder.Length != 0)
		{
			return stringBuilder.ToString();
		}
		return "No application events recorded.";
	}
}
