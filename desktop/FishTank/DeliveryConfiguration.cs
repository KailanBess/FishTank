namespace FishTank;

public sealed class DeliveryConfiguration
{
	public int Version = 1;

	public string Origin;

	public string DeviceToken;

	public string Binding;

	public bool EmailEnrolled;

	public bool SmsEnrolled;
}
