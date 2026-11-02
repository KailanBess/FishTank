namespace FishTank;

public sealed class RemoteConfig
{
	public string Address;

	public int Port = 47831;

	public string ServerCertificateThumbprint;

	public Enrollment[] Devices;
}
