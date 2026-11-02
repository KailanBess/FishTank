using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace FishTank;

public static class Remote
{
	public static string Fingerprint(X509Certificate cert)
	{
		using SHA256 sHA = SHA256.Create();
		return BitConverter.ToString(sHA.ComputeHash(cert.GetRawCertData())).Replace("-", "");
	}

	public static Enrollment Authorize(RemoteConfig config, X509Certificate cert)
	{
		if (cert == null || config.Devices == null)
		{
			return null;
		}
		X509Certificate2 x509Certificate = new X509Certificate2(cert);
		if (DateTime.Now < x509Certificate.NotBefore || DateTime.Now > x509Certificate.NotAfter)
		{
			return null;
		}
		Enrollment[] devices = config.Devices;
		foreach (Enrollment enrollment in devices)
		{
			if (string.Equals(enrollment.CertificateSha256, Fingerprint(cert), StringComparison.OrdinalIgnoreCase))
			{
				return enrollment;
			}
		}
		return null;
	}

	public static bool IsTailAddress(string address)
	{
		if (!IPAddress.TryParse(address, out var address2) || IPAddress.IsLoopback(address2))
		{
			return false;
		}
		NetworkInterface[] allNetworkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
		foreach (NetworkInterface networkInterface in allNetworkInterfaces)
		{
			if (networkInterface.Name.IndexOf("Tailscale", StringComparison.OrdinalIgnoreCase) < 0 && networkInterface.Description.IndexOf("Tailscale", StringComparison.OrdinalIgnoreCase) < 0)
			{
				continue;
			}
			foreach (UnicastIPAddressInformation unicastAddress in networkInterface.GetIPProperties().UnicastAddresses)
			{
				if (unicastAddress.Address.Equals(address2))
				{
					return true;
				}
			}
		}
		return false;
	}

	public static string Dispatch(string request, Enrollment enrollment)
	{
		if (enrollment == null)
		{
			throw new UnauthorizedAccessException("Device not enrolled");
		}
		switch (request)
		{
		case "health":
			return Core.Collect("Health");
		case "history":
			return Core.History();
		case "update-status":
			return "Application updates: manually staged, no automatic installer. Defender signature status is in health.";
		case "quarantine-status":
			return "Quarantine is per interactive user; the remote host does not expose user vaults.";
		case "scan-quick":
			if (enrollment.AllowScan)
			{
				return Core.Scan("Quick", null);
			}
			break;
		}
		throw new UnauthorizedAccessException("Operation not authorized or unsupported");
	}

	public static void Host(string configPath)
	{
		RemoteConfig config = Core.Parse<RemoteConfig>(File.ReadAllText(configPath));
		if (!IsTailAddress(config.Address) || config.Port < 1024 || config.Port > 65535)
		{
			throw new ArgumentException("Bind address must be assigned to a local Tailscale adapter, port 1024-65535.");
		}
		X509Certificate2 x509Certificate = null;
		using (X509Store x509Store = new X509Store(StoreName.My, StoreLocation.CurrentUser))
		{
			x509Store.Open(OpenFlags.ReadOnly);
			X509Certificate2Enumerator enumerator = x509Store.Certificates.GetEnumerator();
			while (enumerator.MoveNext())
			{
				X509Certificate2 current = enumerator.Current;
				if (current.Thumbprint == config.ServerCertificateThumbprint && current.HasPrivateKey)
				{
					x509Certificate = current;
				}
			}
		}
		if (x509Certificate == null)
		{
			throw new InvalidOperationException("Server certificate/private key missing in CurrentUser/My.");
		}
		TcpListener tcpListener = new TcpListener(IPAddress.Parse(config.Address), config.Port);
		tcpListener.Start(8);
		Console.WriteLine("FishTank enrolled-device TLS host on " + config.Address + ":" + config.Port + ". Ctrl+C to stop. No firewall rules changed.");
		try
		{
			while (true)
			{
				using TcpClient tcpClient = tcpListener.AcceptTcpClient();
				tcpClient.ReceiveTimeout = 5000;
				tcpClient.SendTimeout = 5000;
				Enrollment enrollment = null;
				try
				{
					using SslStream sslStream = new SslStream(tcpClient.GetStream(), leaveInnerStreamOpen: false, delegate(object s, X509Certificate c, X509Chain chain, SslPolicyErrors errors)
					{
						enrollment = Authorize(config, c);
						return enrollment != null;
					});
					sslStream.ReadTimeout = 5000;
					sslStream.WriteTimeout = 5000;
					sslStream.AuthenticateAsServer(x509Certificate, clientCertificateRequired: true, SslProtocols.Tls12, checkCertificateRevocation: false);
					if (enrollment == null)
					{
						throw new UnauthorizedAccessException();
					}
					string text = ReadLine(sslStream, 128);
					Core.Audit("remote-request", new
					{
						peer = enrollment.CertificateSha256,
						operation = text
					});
					string result = Dispatch(text, enrollment);
					byte[] bytes = Encoding.UTF8.GetBytes(Core.Json(new
					{
						ok = true,
						result = result
					}) + "\n");
					sslStream.Write(bytes, 0, bytes.Length);
				}
				catch (Exception ex)
				{
					Core.Audit("remote-failure", ex.Message);
				}
			}
		}
		finally
		{
			tcpListener.Stop();
		}
	}

	public static string ReadLine(Stream stream, int max)
	{
		List<byte> list = new List<byte>();
		int num;
		while ((num = stream.ReadByte()) != -1)
		{
			if (num == 10)
			{
				return Encoding.UTF8.GetString(list.ToArray()).TrimEnd('\r');
			}
			if (list.Count >= max)
			{
				throw new IOException("Request limit exceeded");
			}
			list.Add((byte)num);
		}
		throw new IOException("Incomplete request");
	}

	public static string Request(string address, int port, string serverPin, string clientThumbprint, string operation)
	{
		if (!IPAddress.TryParse(address, out var address2))
		{
			throw new ArgumentException("Use a Tailscale IP address.");
		}
		byte[] addressBytes = address2.GetAddressBytes();
		if ((addressBytes.Length != 4 || addressBytes[0] != 100 || addressBytes[1] < 64 || addressBytes[1] > 127) && (addressBytes.Length != 16 || addressBytes[0] != 253 || addressBytes[1] != 122 || addressBytes[2] != 17 || addressBytes[3] != 92 || addressBytes[4] != 161 || addressBytes[5] != 224))
		{
			throw new ArgumentException("Destination must be in Tailscale address space.");
		}
		X509CertificateCollection x509CertificateCollection = new X509CertificateCollection();
		using (X509Store x509Store = new X509Store(StoreName.My, StoreLocation.CurrentUser))
		{
			x509Store.Open(OpenFlags.ReadOnly);
			X509Certificate2Enumerator enumerator = x509Store.Certificates.GetEnumerator();
			while (enumerator.MoveNext())
			{
				X509Certificate2 current = enumerator.Current;
				if (current.Thumbprint == clientThumbprint && current.HasPrivateKey)
				{
					x509CertificateCollection.Add(current);
				}
			}
		}
		if (x509CertificateCollection.Count != 1)
		{
			throw new InvalidOperationException("Client certificate/private key missing");
		}
		using TcpClient tcpClient = new TcpClient();
		IAsyncResult asyncResult = tcpClient.BeginConnect(address, port, null, null);
		if (!asyncResult.AsyncWaitHandle.WaitOne(5000))
		{
			throw new TimeoutException();
		}
		tcpClient.EndConnect(asyncResult);
		using SslStream sslStream = new SslStream(tcpClient.GetStream(), leaveInnerStreamOpen: false, (object s, X509Certificate c, X509Chain chain, SslPolicyErrors errors) => c != null && string.Equals(Fingerprint(c), serverPin, StringComparison.OrdinalIgnoreCase) && DateTime.Now >= new X509Certificate2(c).NotBefore && DateTime.Now <= new X509Certificate2(c).NotAfter);
		sslStream.ReadTimeout = 86400000;
		sslStream.WriteTimeout = 5000;
		sslStream.AuthenticateAsClient(address, x509CertificateCollection, SslProtocols.Tls12, checkCertificateRevocation: false);
		byte[] bytes = Encoding.UTF8.GetBytes(operation + "\n");
		sslStream.Write(bytes, 0, bytes.Length);
		return ReadLine(sslStream, 4194304);
	}
}
