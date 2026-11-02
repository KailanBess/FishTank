namespace FishTank;

public static class NavigationPolicy
{
	public static bool IsVisible(string page, bool advanced)
	{
		return advanced || page == "Dashboard" || page == "Antivirus" || page == "Security Center"
			|| page == "Breach Checks" || page == "Protected Files" || page == "Password Manager" || page == "Settings";
	}
}
