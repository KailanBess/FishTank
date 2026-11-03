using System;
using System.IO;
using System.ServiceProcess;
using System.Windows.Forms;

namespace FishTank;

public static class Program
{
	[STAThread]
	public static int Main(string[] args)
	{
		try
		{
			if (args.Length > 0 && args[0] == "--service")
			{
				ServiceBase.Run(new GuardService());
				return 0;
			}
			if (args.Length > 0 && args[0] == "--portable")
			{
				Core.Data = Path.Combine(Core.Base, "data");
				string[] array = new string[args.Length - 1];
				Array.Copy(args, 1, array, 0, array.Length);
				args = array;
			}
			if (args.Length >= 2 && args[0] == "--profile")
			{
				Core.Data = ProfilePaths.Resolve(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FishTank"), args[1]);
				var remaining = new string[args.Length - 2]; Array.Copy(args, 2, remaining, 0, remaining.Length); args = remaining;
			}
			string scanFile = null;
			if (args.Length == 2 && args[0] == "--scan-file") { scanFile = ScanPaths.Normalize(args[1]); ScanPaths.RefuseReparseAncestors(scanFile); args = new string[0]; }
			Core.Init();
			if (args.Length == 0)
			{
				Application.EnableVisualStyles();
				Application.SetCompatibleTextRenderingDefault(defaultValue: false);
				SimpleExplanations.Install();
				bool returnToSignIn;
				do
				{
					if (!AppUnlockDialog.Prompt())
					{
						return 1;
					}
					using Desktop desktop = new Desktop();
					if (scanFile != null) { string target = scanFile; desktop.Shown += delegate { desktop.OfferShellScan(target); }; scanFile = null; }
					Application.Run(desktop);
					returnToSignIn = desktop.ReturnToSignIn;
				}
				while (returnToSignIn);
				return 0;
			}
			if (!AppAccess.ConsoleUnlock())
			{
				return 1;
			}
			string text = args[0];
			switch (text)
			{
			case "health":
			case "network":
			case "events":
			case "persistence":
			case "tailscale":
			case "hardening":
			case "accounts":
			case "browsers":
				Console.WriteLine(Core.Collect(char.ToUpper(text[0]) + text.Substring(1)));
				break;
			case "scan":
				if (args.Length >= 2)
				{
					Console.WriteLine(Core.Scan(args[1], (args.Length > 2) ? args[2] : null));
					break;
				}
				goto default;
			default:
				if (text == "hash" && args.Length == 2)
				{
					Console.WriteLine(Core.Hash(args[1]));
					break;
				}
				switch (text)
				{
				case "history":
					Console.WriteLine(Core.History());
					break;
				case "vault-list":
					Console.WriteLine(Vault.List());
					break;
				case "quarantine":
					if (args.Length == 4 && args[3] == "--confirm")
					{
						Console.WriteLine(Core.Json(Vault.Put(args[1], args[2])));
						break;
					}
					goto default;
				default:
					if (text == "restore" && args.Length == 4 && args[3] == "--confirm")
					{
						Vault.Restore(args[1], args[2]);
						Console.WriteLine("Restored; encrypted recovery copy retained.");
						break;
					}
					if (text == "vault-delete" && args.Length == 3 && args[2] == "--confirm")
					{
						Vault.Delete(args[1]);
						Console.WriteLine("Vault entry deleted.");
						break;
					}
					if (text == "service-health")
					{
						Console.WriteLine(GuardService.Query("health"));
						break;
					}
					if (text == "remote-host" && args.Length == 2)
					{
						Remote.Host(args[1]);
						break;
					}
					if (text == "remote" && args.Length == 7)
					{
						Console.WriteLine(Remote.Request(args[1], int.Parse(args[2]), args[3], args[4], args[5] + ((args[6] == "--confirm") ? "" : throwError())));
						break;
					}
					throw new ArgumentException("Commands: health, network, events, persistence, tailscale, hardening, accounts, browsers, scan Quick|Full|Custom [path], hash path, history, vault-list, quarantine path reason --confirm, restore id path --confirm, vault-delete id --confirm, service-health, remote-host config, remote ip port serverSha256 clientThumbprint operation --confirm");
				}
				break;
			}
			return 0;
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine(ex.Message);
			return 1;
		}
	}

	private static string throwError()
	{
		throw new ArgumentException("Remote requests require --confirm");
	}
}
