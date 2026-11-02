using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace FishTank;

public sealed class GuardService : ServiceBase
{
	private struct ServiceStatus
	{
		public uint Type;

		public uint State;

		public uint Controls;

		public uint Win32Exit;

		public uint ServiceExit;

		public uint CheckPoint;

		public uint WaitHint;

		public uint Pid;

		public uint Flags;
	}

	private volatile bool stopping;

	private Thread worker;

	private NamedPipeServerStream active;

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint pid);

	[DllImport("advapi32.dll", SetLastError = true)]
	private static extern bool QueryServiceStatusEx(IntPtr service, int level, out ServiceStatus status, int size, out int needed);

	private static void VerifyServer(NamedPipeClientStream pipe)
	{
		if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var pid))
		{
			throw new UnauthorizedAccessException("Cannot authenticate pipe server");
		}
		using ServiceController serviceController = new ServiceController("FishTank");
		if (!QueryServiceStatusEx(serviceController.ServiceHandle.DangerousGetHandle(), 0, out var status, Marshal.SizeOf(typeof(ServiceStatus)), out var _) || status.State != 4 || status.Pid != pid)
		{
			throw new UnauthorizedAccessException("Pipe server is not the running registered FishTank service");
		}
	}

	public GuardService()
	{
		base.ServiceName = "FishTank";
		base.CanStop = true;
		base.AutoLog = true;
	}

	protected override void OnStart(string[] args)
	{
		Core.Data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FishTank");
		Core.Init();
		worker = new Thread(Loop)
		{
			IsBackground = true
		};
		worker.Start();
	}

	protected override void OnStop()
	{
		stopping = true;
		if (active != null)
		{
			active.Dispose();
		}
		if (worker != null)
		{
			worker.Join(5000);
		}
	}

	private void Loop()
	{
		DateTime dateTime = DateTime.MinValue;
		while (!stopping)
		{
			try
			{
				if (DateTime.UtcNow >= dateTime)
				{
					try
					{
						Core.Audit("health-snapshot", Core.Collect("Health"));
					}
					catch (Exception ex)
					{
						Core.Audit("health-error", ex.Message);
					}
					dateTime = DateTime.UtcNow.AddMinutes(15.0);
				}
				PipeSecurity pipeSecurity = new PipeSecurity();
				pipeSecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
				WellKnownSidType[] array = new WellKnownSidType[3]
				{
					WellKnownSidType.LocalServiceSid,
					WellKnownSidType.LocalSystemSid,
					WellKnownSidType.BuiltinAdministratorsSid
				};
				foreach (WellKnownSidType sidType in array)
				{
					pipeSecurity.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(sidType, null), PipeAccessRights.FullControl, AccessControlType.Allow));
				}
				pipeSecurity.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
				using NamedPipeServerStream namedPipeServerStream = new NamedPipeServerStream("FishTank.v1", PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, pipeSecurity);
				active = namedPipeServerStream;
				IAsyncResult asyncResult = namedPipeServerStream.BeginWaitForConnection(null, null);
				while (!asyncResult.AsyncWaitHandle.WaitOne(1000) && !stopping && !(DateTime.UtcNow >= dateTime))
				{
				}
				if (stopping)
				{
					break;
				}
				if (!asyncResult.IsCompleted)
				{
					continue;
				}
				namedPipeServerStream.EndWaitForConnection(asyncResult);
				byte[] array2 = new byte[64];
				IAsyncResult asyncResult2 = namedPipeServerStream.BeginRead(array2, 0, array2.Length, null, null);
				if (asyncResult2.AsyncWaitHandle.WaitOne(3000))
				{
					string text = Encoding.UTF8.GetString(array2, 0, namedPipeServerStream.EndRead(asyncResult2)).Trim();
					string s = ((text == "health") ? Core.Collect("Health") : ((text == "history") ? Core.History() : "Unsupported command; service exposes read-only health/history."));
					byte[] bytes = Encoding.UTF8.GetBytes(s);
					IAsyncResult asyncResult3 = namedPipeServerStream.BeginWrite(bytes, 0, bytes.Length, null, null);
					if (asyncResult3.AsyncWaitHandle.WaitOne(3000))
					{
						namedPipeServerStream.EndWrite(asyncResult3);
					}
				}
			}
			catch (Exception ex2)
			{
				if (!stopping)
				{
					try
					{
						Core.Audit("service-error", ex2.Message);
					}
					catch
					{
					}
					Thread.Sleep(1000);
				}
			}
		}
	}

	public static string Query(string command)
	{
		if (command != "health" && command != "history")
		{
			throw new ArgumentException("Unknown command");
		}
		using NamedPipeClientStream namedPipeClientStream = new NamedPipeClientStream(".", "FishTank.v1", PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
		namedPipeClientStream.Connect(2000);
		VerifyServer(namedPipeClientStream);
		byte[] bytes = Encoding.UTF8.GetBytes(command);
		namedPipeClientStream.Write(bytes, 0, bytes.Length);
		namedPipeClientStream.Flush();
		using MemoryStream memoryStream = new MemoryStream();
		byte[] array = new byte[4096];
		while (true)
		{
			IAsyncResult asyncResult = namedPipeClientStream.BeginRead(array, 0, array.Length, null, null);
			if (!asyncResult.AsyncWaitHandle.WaitOne(95000))
			{
				throw new System.TimeoutException();
			}
			int num = namedPipeClientStream.EndRead(asyncResult);
			if (num == 0)
			{
				break;
			}
			memoryStream.Write(array, 0, num);
			if (memoryStream.Length > 4194304)
			{
				throw new IOException("IPC response too large");
			}
		}
		return Encoding.UTF8.GetString(memoryStream.ToArray());
	}
}
