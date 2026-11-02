using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace FishTank;

public static class ExplanationCatalog
{
	private static readonly Dictionary<string, string> messages = new Dictionary<string, string>(StringComparer.Ordinal)
	{
		{ "An operation is still running. Closing FishTank may leave the Windows operation running without its final application record. Close anyway?", "Something is still running. If you close FishTank, Windows may keep working, but FishTank may not save the final result. Close anyway?" },
		{ "Quick and full scans follow Defender’s existing remediation policy. Custom scans request no remediation; independent real-time protection can still act.", "Quick and full scans let Defender handle threats using its current settings. Custom scans ask Defender to report results without fixing files. Windows' live protection can still change or remove a file." },
		{ "The optional observer watches one folder without subfolders. File bursts create low-confidence alerts; this does not block ransomware or replace Defender.", "Watch changes in one folder. Folders inside it are not watched. Many changes at once can trigger an alert, but the alert may be harmless. This tool does not stop ransomware or replace Defender." },
		{ "Read-only inventory. FishTank does not label connections malicious without evidence or add firewall rules automatically.", "See your current network settings. FishTank does not change them, add firewall rules, or call a connection dangerous without evidence." },
		{ "SFC and DISM can modify Windows files. They require your confirmation and Windows administrator approval.", "These Windows repair tools can change system files. You must agree here and approve Windows' administrator prompt." },
		{ "Files are encrypted with your Windows identity before source removal. Restoring can reintroduce a threat. Existing files are never overwritten.", "FishTank first locks a recovery copy to your Windows account, then removes the original file. Restoring it could bring a threat back. It will not replace an existing file." },
		{ "Discovery uses Tailscale’s supported status interface. FishTank does not enroll devices, change VPN or DNS settings, or store Tailscale login keys.", "FishTank asks Tailscale which devices it can see. It does not add devices, change network settings, or save your Tailscale login keys." },
		{ "These local checks can identify settings that deserve attention. Unavailable checks remain unknown. FishTank does not silently enable, disable or repair Windows security settings.", "These checks can point out settings to review. If a check cannot run, its result is unknown. FishTank does not switch Windows protection on or off or repair it without asking." },
		{ "Choose a known-good file to remember its SHA-256, then compare it later. A changed file may be a legitimate update; an unchanged file is not proof of safety.", "Choose a file you trust. FishTank saves its digital fingerprint so you can check for changes later. Changes can be normal updates. A matching fingerprint does not prove a file is safe." },
		{ "Email checks send the full address and your API key to Have I Been Pwned. Password checks send only a five-character hash prefix. Queries, keys and results are not saved by FishTank.", "Email lookup shares the whole email address and your service key with Have I Been Pwned. Password lookup shares only the first five characters of a digital fingerprint, not the password. FishTank does not save these searches, keys or results." },
		{ "A result can help you act on known breaches. No service can prove information has never leaked or search every private breach. Email lookup requires your own HIBP API key.", "This checks known data leaks. It cannot find every leak or prove your information has never leaked. Email searches need your own Have I Been Pwned service key." },
		{ "The encrypted vault stores names, usernames, passwords, sites and notes. It locks after five minutes of inactivity or when Windows locks. The master passphrase cannot be reset if forgotten.", "The locked password file holds account names, usernames, passwords, websites and notes. It locks after five idle minutes or when Windows locks. Its main password cannot be reset if forgotten." },
		{ "Generate 1–65,536 characters, subject to the selected character sets. Choose uppercase, lowercase, digits, symbols and lookalike exclusions. Generation works offline; websites may impose smaller limits.", "Make a password from 1 to 65,536 characters, if your selected character groups fit. Choose letters, numbers and symbols, and whether to avoid similar-looking characters. No internet is needed. Websites may allow shorter passwords only." },
		{ "Require a separate password to open this data profile through the desktop or interactive CLI. The password vault still needs its own master passphrase. The background service stays independent.", "Set a password for opening this FishTank profile in the app or terminal. Your saved passwords still need their own vault password. This does not lock the background service." },
		{ "Authenticator MFA is not enrolled. Add an external authenticator and one-use recovery codes, then choose whether to require them at sign-in.", "No code-generating app is connected yet. Connect one and save its backup sign-in codes. Then choose whether opening FishTank needs an extra code." },
		{ "Defender controls antivirus exclusions, scan schedules and engine behavior. FishTank appearance settings never change Windows security settings.", "Use Defender to choose what scans skip, when scans run, and how threats are handled. Changing FishTank's look does not change Windows protection." },
		{ "Working… You can keep browsing. Some Windows checks require administrator access.", "Working… You can keep using other pages. Some checks need Windows administrator permission." },
		{ "No matching records are available. This does not indicate a clean device.", "There are no results to show. That does not mean this device is free of threats." },
		{ "Connection state comes from the installed Tailscale client. Peer visibility does not grant FishTank management permission.", "Tailscale reports whether a device is connected. Seeing a device does not give FishTank permission to control it." },
		{ "Run DISM RestoreHealth? This modifies the Windows image and may contact Windows Update.", "Repair Windows' system image? This changes Windows files and may download files from Windows Update." },
		{ "Restore this entry? This can reintroduce a threat. A recovery copy will be retained.", "Put this file back? It could bring a threat back. FishTank will keep the recovery copy." },
		{ "Enter your current app password and a fresh authenticator or unused recovery code. This is required even when sign-in MFA prompts are off.", "Enter your FishTank password and a new code from your code app, or an unused backup code. Security changes need this check even if opening FishTank does not ask for a code." },
		{ "Enter your current FishTank app password. This is separate from the password-vault master passphrase.", "Enter the password used to open FishTank. Your saved-password vault has a separate password." },
		{ "Shown once. Each code replaces the authenticator code for one verification, but still requires your app password. Keep them outside FishTank and separate from your device. New codes invalidate the old set.", "You can see these backup codes only now. Each works once instead of a code from your code app. You still need your FishTank password. Store them outside FishTank and away from this device. Making new codes stops the old ones working." },
		{ "Save these recovery codes as plaintext? Anyone with the file and your app password can use them. Choose a private location outside the app folder.", "Save these backup codes in an unprotected text file? Anyone with that file and your FishTank password could use them. Choose a private place outside FishTank's folder." },
		{ "Close without confirming the codes are saved? You can generate a replacement set later using your password and authenticator.", "Close before confirming you saved the backup codes? You can make new ones later with your password and code app." },
		{ "In an external authenticator app, add a time-based account manually. Use the setup key below, SHA-1, 6 digits and a 30-second period. Prefer another device.", "Open a separate code-generating app, preferably on another device. Add an account using the setup key below. Choose time-based codes, SHA-1, 6 digits, and 30 seconds." },
		{ "The setup key generates every code for this enrollment. FishTank does not send it to a service or online QR generator. No QR image is required; manual key entry is supported.", "Keep this setup key secret: it can generate all the sign-in codes. FishTank does not upload it or use an online QR service. You can type the key into your code app." },
		{ "Enrollment is saved only after both the new authenticator and current access credentials verify. Cancel leaves the old enrollment unchanged.", "FishTank saves the new code app only after checking its code and your current sign-in details. Cancel keeps the previous setup." },
		{ "Stop requiring MFA at sign-in? Future sign-ins will need only the app password. Enrollment stays saved. Verification is required to apply this change.", "Stop asking for an extra code when FishTank opens? Your app password will still be required. The code app stays connected. You must verify your identity to make this change." },
		{ "Remove the authenticator and all recovery codes? The app password will remain enabled.", "Disconnect the code app and delete all backup sign-in codes? Your FishTank password will still be required." },
		{ "Send a code through your own configured server. It uses only the email/phone pre-provisioned by your server administrator. Delivery may incur gateway charges.", "Send a code using your own server. It can send only to the email address or phone number already set by the server administrator. Sending may cost money." },
		{ "Server accepted delivery. Enter its eight-digit code. It expires in five minutes; a resend invalidates the previous code.", "The server accepted the send request. This does not confirm delivery. Enter the eight-digit code when it arrives. It lasts five minutes. Requesting a new code stops the old one working." },
		{ "Host the included Debian verification service, provision a device with its fixed email/phone, then enter the token privately here. SMTP/SMS delivery credentials stay on the server. Keep an offline authenticator/recovery option; it is required before enabling these alternatives.", "Install the verification service on your Debian server. Add this device and its email address or phone number there. Enter its private device key here. Email and text-message service passwords stay on the server. Set up a code app and keep backup codes first, so you can still sign in if the server is offline." },
		{ "Connect to the entered HTTPS origin with this device token? Only configure a server you own and trust. Replacing the connection resets channel enrollment.", "Connect to this encrypted server address using the private device key? Use only a server you own and trust. Changing servers means you must verify your email and phone again." },
		{ "Too many MFA attempts. Wait for the cooldown before verifying a factor again.", "Too many code attempts. Wait for the lockout to end before trying another code." },
		{ "App access configuration changed in another process. Reauthenticate and try again.", "Another FishTank window changed the sign-in settings. Verify your identity again, then retry." },
		{ "App access configuration changed in another process; removal was refused.", "Another FishTank window changed the sign-in settings. Nothing was removed. Verify your identity again before retrying." },
		{ "The new authenticator code is invalid or expired. Enrollment was not changed.", "The new code is wrong or too old. Your code-app setup was not changed. Try a fresh code." },
		{ "Password or MFA/recovery code is incorrect, expired or used. Repeated attempts are throttled.", "The password or code did not work. The code may be wrong, too old, or already used. Too many tries cause a waiting period." },
		{ "App password saved. Existing MFA enrollment and prompt settings are preserved. Your password vault still uses its separate master passphrase.", "Your FishTank password was saved. Your extra-code setup and sign-in choices stay the same. The password vault still has its own main password." },
		{ "Remove the app access password and any MFA enrollment/recovery codes? The password-manager master passphrase remains required.", "Remove the password used to open FishTank, its extra-code setup and its backup codes? Your password vault will still require its own password." },
		{ "Preview changes on the app behind this window. These controls change presentation only.", "See how changes look in the app behind this window. These settings change only its appearance." },
		{ "Choose which dashboard cards appear and move them into your preferred order. Text size is never changed by spacing presets.", "Choose which home-page cards to show and their order. Spacing choices do not change your text size." },
		{ "Settings apply live. Reduced motion removes decorative hover color changes; no decorative animation runs in the background.", "Changes appear immediately. Reduced motion stops hover effects and bubble animations." },
		{ "Compact workspace preserves your selected font and text size. Light and dark themes use separate readable text palettes; accents are kept on controls and highlights.", "Compact spacing keeps your font and text size. Light and dark themes each have matching text colors. Accent colors mark controls and highlights." },
		{ "Only FishTank appearance preferences are included. No security policy, VPN credentials, device enrollments, histories or quarantine contents are imported or exported.", "Import and export copy only FishTank's appearance settings. They do not copy security rules, network login keys, connected devices, history or isolated files." },
		{ "Save & close persists your preferences. Cancel restores the appearance from before this window opened. Revert restores that same starting point while keeping this window open. Corrupt settings use a previous valid backup, or safe defaults.", "Save & close keeps your choices. Cancel restores the settings from before this window opened. Revert does the same but keeps this window open. Damaged settings use the last good backup or default settings." },
		{ "Choose a unique app passphrase of at least 12 characters. It is separate from your password-vault master passphrase.", "Choose a FishTank password you do not use elsewhere, with at least 12 characters. Use a different password for your saved-password vault." },
		{ "Use a unique passphrase of at least 12 characters. It cannot be reset or recovered if forgotten.", "Use a password you do not use elsewhere, with at least 12 characters. If you forget it, it cannot be reset or recovered." },
		{ "Your master passphrase decrypts this local vault. It is never sent to a service.", "Your main vault password unlocks the saved passwords on this PC. It is never sent to an online service." },
		{ "Generation is local and uses cryptographic randomness. Sites may reject long passwords or certain symbols. No automatic website password change or autofill is performed.", "Passwords are made on this PC using secure random choices. Some websites limit password length or symbols. FishTank does not change website passwords or fill them in for you." },
		{ "Copied · Clipboard will be cleared after 30 seconds if it has not changed. Other apps can read a copied password.", "Copied. FishTank clears the clipboard after 30 seconds if nothing else replaces it. Other apps can read the password while it is copied." },
		{ "Remove this entry from the active vault? Encrypted backups may still contain previous versions.", "Delete this saved account from the current vault? Older copies may remain in locked backups." },
		{ "Encrypted backup saved. Keep its master passphrase separately; losing it prevents recovery.", "A locked backup was saved. Keep its vault password in a separate safe place. You cannot restore it without that password." },
		{ "Restore an encrypted backup? After its master passphrase is verified, it replaces the active vault. The previous encrypted file is retained as a .bak file.", "Restore this locked backup? Once its vault password is checked, it replaces your current vault. The previous locked vault is kept in a .bak file." },
		{ "No known breach was returned for this address. This does not prove it has never leaked.", "No known leak was found for this address. It may still have appeared in a leak this service does not know about." },
		{ "No scan report selected. Reports contain actual engine outcomes and coverage gaps; they do not certify a clean device.", "Choose a scan report. Reports show what the scanner returned and what it could not check. They cannot prove the whole device is safe." },
		{ "Table shows up to 1,000 filtered records. Exports include every stored record. Review does not mean confirmed malware.", "This table shows at most 1,000 matching results. An exported report includes all saved results. A 'Review' result means you should check it; it does not confirm a threat." },
		{ "Export a plaintext scan report? It contains file paths, hashes and scanner output. Review it before sharing. No file content is included by FishTank.", "Save an unprotected scan report? It includes file locations, digital fingerprints and scanner messages. Read it before sharing. FishTank does not include the files' contents." },
		{ "Select one of the 50 most recently written local encrypted reports (local write time shown). A Running checkpoint can indicate an interrupted job.", "Choose from the 50 most recently saved locked reports on this PC. Times show when each was saved. A report still marked Running may be from an interrupted scan." },
		{ "Custom scans request no remediation. Defender’s own real-time policy remains active. Reparse points are not followed; access failures, exclusions and limits are recorded. Archives follow Defender policy; FishTank does not unpack them. File-level scanning can be slow.", "Custom scans ask Defender to report without fixing files. Defender's live protection can still act. FishTank skips links that redirect to other locations and records files it cannot check, files you excluded, and limits it reached. Defender decides how to scan archives; FishTank does not unpack them. Checking files one by one can be slow." },
		{ "Cancel requested. A delegated Defender operation may continue; pending files will not be scanned.", "Cancellation requested. Defender may keep working on a scan already handed to it. FishTank will not start checking the waiting files." },
		{ "Cancel the remaining queue? Wait for it to stop before closing. Windows may continue a delegated scan.", "Stop scanning the waiting files? Wait for FishTank to stop before closing. Windows may keep working on a scan already started." },
		{ "No security verdict inferred.", "This result does not tell us whether the device is safe." },
		{ "Run SFC repair", "Check and repair Windows system files" },
		{ "Run DISM repair", "Repair Windows' system image" },
		{ "Scanner readiness / policy", "Check whether Defender is ready and see its scan settings" },
		{ "Remember a file", "Save a file fingerprint for later comparison" },
		{ "Compare remembered files", "Check saved files for changes" },
		{ "Multifactor authentication", "Extra sign-in checks" },
		{ "HTTPS server origin", "Encrypted server address, such as https://your-server/" },
		{ "Private device token", "Private key that lets this device contact your server" },
		{ "Authenticator-app codes (TOTP) and single-use recovery codes work offline. Your app password is still required.\r\n\r\nTurn the sign-in requirement off to stop MFA prompts at startup; enrollment remains saved for turning it back on. Sensitive security changes still require the password and an existing MFA/recovery code.\r\n\r\nThis local app lock does not protect against someone controlling the same Windows account. Your password vault keeps its own master passphrase.", "Your code app and one-use backup codes work without internet. You still need your FishTank password.\r\n\r\nYou can stop the extra-code prompt when opening the app and turn it back on later. Security changes still need your password and a valid code.\r\n\r\nThis app lock cannot stop someone who controls your Windows account. Your password vault has its own separate password." },
		{ "Email lookup sends the full address to Have I Been Pwned over HTTPS. A user-provided HIBP API subscription/key is required; it is not saved. Look up your own address or one you are authorized to check.", "This sends the whole email address to Have I Been Pwned over an encrypted connection. You need your own paid service key; FishTank does not save it. Search only your address or one you have permission to check." },
		{ "The password is hashed locally. Only the first five SHA-1 hash characters are sent to Pwned Passwords; the full password and hash are never sent. The provider sees your IP and a partial hash. Padded responses are requested. SHA-1 is used only for this lookup protocol, never for vault encryption.", "FishTank makes a digital fingerprint of the password on this PC. It shares only the first five fingerprint characters with Pwned Passwords. Your full password and fingerprint stay here. The service sees your internet address and that small part of the fingerprint. FishTank asks for extra filler in the reply to make the search less revealing. SHA-1 is used for this search, not to lock your password vault." },
		{ "No lookup has been made.\r\n\r\nA missing result cannot prove your information is safe. HIBP does not cover every leak or reveal leaked passwords. This feature does not search private messages, identity documents or all dark-web sources.", "Nothing has been searched yet.\r\n\r\nFinding no match does not prove your information is safe. Have I Been Pwned does not know about every leak and does not show leaked passwords. This tool does not search private messages, ID documents or every dark-web source." },
		{ "Send only the first five hash characters to api.pwnedpasswords.com to check this password?\n\nThe service sees your IP address and partial hash. The full password and hash remain local. No results or secrets are saved by FishTank.", "Check this password with api.pwnedpasswords.com?\n\nOnly the first five characters of its digital fingerprint will be sent. The service sees your internet address and that partial fingerprint. The whole password and fingerprint stay on this PC. FishTank saves no results or secrets." },
		{ "No match was returned in the checked Pwned Passwords range.\n\nThis is not proof the password is secret, strong or safe to reuse.", "This password was not found in the checked part of the known-leaks list.\n\nIt could still be leaked or weak. Do not reuse passwords across accounts." },
		{ "Review the affected accounts, use distinct passwords and enable multifactor authentication. A record does not prove the current password is compromised.", "Check the listed accounts. Give each its own password and turn on an extra sign-in check. A leak record does not prove your current password was stolen." },
		{ "Locked · Master passphrase required. Generator works without a vault. Auto-lock after 5 minutes of inactivity or when Windows locks.", "Locked. Enter your vault password to open saved accounts. You can make a new password without opening the vault. It locks after 5 idle minutes or when Windows locks." },
		{ "Locked · Create or unlock your vault. No password data is visible. Auto-lock: 5 minutes.", "Locked. Create or open your password vault. Saved passwords are hidden while locked. It locks again after 5 idle minutes." },
		{ "Generated password copied. It was not saved; add an entry to store it. Clipboard clear timer: 30 seconds.", "The new password was copied but not saved. Add a saved account to keep it. FishTank will try to clear its copied password after 30 seconds." },
		{ "These are presentation colors, not security verdicts. Emulator/platform labels and game-cover controls stay in FishBowl; FishTank adapts them to security cards and evidence panes.", "These colors change how FishTank looks. They do not say whether your PC is safe. FishBowl's game controls are adapted here to security cards and result details." },
		{ "App password enabled: interactive unlock is required. Passwords and MFA codes are never accepted in command-line arguments or environment variables.", "This FishTank profile is password-protected. Open an interactive terminal and enter your password when asked. Passwords and extra codes cannot be passed as command arguments or environment settings." },
		{ "Enroll an offline authenticator first; keep recovery access if the server disconnects.", "Set up a code-generating app first. Keep backup sign-in codes so you can still sign in if your server is offline." },
		{ "Enroll an offline authenticator first.", "Set up a code-generating app before enabling email or text-message codes." },
		{ "Delivery limit reached. Wait before requesting another code.", "Too many codes were requested. Wait before asking for another one." },
		{ "Code invalid, expired, context-mismatched or used.", "The code did not work. It may be wrong, too old, already used, or meant for a different sign-in or change. Use a fresh code for this action." },
		{ "Verify and enroll this channel first", "Verify that you can receive a code by this method before using it to sign in." },
		{ "Configure your verification server first", "Connect FishTank to your verification server before requesting a code." },
		{ "Destination ownership was not verified.", "The code did not prove you can receive messages at that email address or phone number. This method was not enabled." }
	};

	private static readonly string[,] glossary = new string[31, 2]
	{
		{ "MFA|multifactor", "MFA: an extra identity check as well as your password." },
		{ "authenticator|TOTP", "Authenticator / TOTP: a code app or device that makes a new sign-in code about every 30 seconds." },
		{ "recovery code", "Recovery code: a backup sign-in code that works once. Your app password is still required." },
		{ "master passphrase|decrypt", "Master passphrase: the main password that unlocks your password vault. Losing it can mean losing access to the saved passwords." },
		{ "enroll|enrollment|provision", "Enrollment / provisioning: setting up a device or delivery method. Email and phone setup includes proving you can receive a code." },
		{ "token|credential", "Device token / credential: a private access key or sign-in detail. Keep it secret." },
		{ "SMTP", "SMTP: the system a server uses to send email. You need a working mail server or relay for actual delivery." },
		{ "SMS|gateway|modem", "SMS gateway: the connection that sends real text messages. A provider or a cellular modem with an active SIM is still needed." },
		{ "HTTPS|TLS", "HTTPS / TLS: encryption for a network connection. It does not itself send email or text messages." },
		{ "origin|binding", "Server origin: the server's base web address. Binding: a value that ties verification to the device's configured destinations; a changed destination needs new verification." },
		{ "rate limit|throttl|cooldown", "Rate limit / cooldown: a waiting period after too many requests or attempts. Repeated tries can extend the wait." },
		{ "expired|already used|replay", "Expired / used code: the code is too old or was already accepted. Request or wait for a fresh code; a code works only once." },
		{ "unauthorized|HTTP 401|HTTP 403", "Unauthorized / refused request: access was denied. Check the device's saved connection and enrollment. Do not treat a refusal as successful verification." },
		{ "server unavailable|HTTP 0|HTTP 503|disconnect", "Server unavailable: the verification request did not succeed. Check your connection, Tailscale and server. Use your offline authenticator or a recovery code if needed." },
		{ "SHA-256|SHA-1|hash|fingerprint", "Hash / digital fingerprint: a value calculated from data. Matching fingerprints help detect changes; they do not prove a file is harmless. SHA-1 here is for the breach-lookup or authenticator protocol, not vault encryption." },
		{ "quarantine|recovery copy", "Quarantine: keep a locked recovery copy and remove the original file. Restoring it could bring a threat back." },
		{ "baseline|integrity", "File baseline / integrity: compare a file's current fingerprint with a saved one. Changes can be legitimate; unchanged does not mean safe." },
		{ "remediation|Defender|engine", "Scan engine / remediation: Defender checks files; remediation means taking action on a threat. Existing Windows protection may act independently of FishTank." },
		{ "ReviewRequired|Review required|Unknown|unavailable|not assessed|Needs review", "Unknown / needs review: there is not enough evidence for a safety conclusion. Read the details and resolve checks that could not run." },
		{ "Skipped|Canceled|truncat|coverage", "Skipped / canceled / truncated: some files were not checked or some output was shortened. These are gaps, not clean results." },
		{ "SFC|DISM", "SFC / DISM: Windows repair tools. They can change system files and need your permission plus Windows administrator approval." },
		{ "DNS|firewall|VPN|Tailscale", "DNS finds network addresses. A firewall controls network access. Tailscale is a private network between your devices; seeing a device does not give permission to manage it." },
		{ "HIBP|Pwned|breach", "Breach lookup: search known data leaks. Email lookup shares the address and your service key; password lookup shares only part of a fingerprint. A missing match does not prove safety." },
		{ "encrypted|DPAPI|ciphertext", "Encrypted: data is locked using a key or password. DPAPI locks it to a Windows account. Someone controlling that account can still be a threat." },
		{ "plaintext|export|CSV|JSON", "Plaintext / export: a readable copy outside the protected storage. It may reveal private details. CSV is a spreadsheet-style table; JSON is structured text." },
		{ "reparse|symlink|junction|hard.link", "Filesystem links: names that can lead to another file or location. FishTank refuses unsafe paths rather than silently following them." },
		{ "accent|palette|theme|Harmonized|Monochrome", "Theme / palette: the app's colors. Accent is its highlight color. Harmonized blends that color into surfaces; Monochrome uses one color. These choices do not change protection." },
		{ "density|spacing|Compact|Roomy|text size", "Spacing changes the room between controls. Text size changes lettering. Compact spacing keeps your chosen font and text size." },
		{ "opacity|wallpaper|Gradient|Dots|Waves|Tile", "Background options change the app's look. Opacity controls visibility. Fit shows the whole image, Fill covers the area with possible cropping, and Tile repeats it." },
		{ "focus|keyboard|screen.reader|reduced motion", "Focus shows which control the keyboard will operate. Reduced motion stops decorative effects. Use Tab to move between controls and F1 for explanations." },
		{ "revok|destination", "Revocation removes a device's access. A destination is the email address or phone number receiving a code; changing it requires verification again." }
	};

	public static string Rewrite(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return text;
		}
		if (messages.TryGetValue(text, out var value))
		{
			return value;
		}
		string[] array = text.Replace("\r\n", "\n").Split('\n');
		bool flag = false;
		for (int i = 0; i < array.Length; i++)
		{
			if (messages.TryGetValue(array[i], out value))
			{
				array[i] = value;
				flag = true;
			}
		}
		if (!flag)
		{
			return text;
		}
		return string.Join(Environment.NewLine, array);
	}

	public static string Explain(string text)
	{
		StringBuilder stringBuilder = new StringBuilder(Rewrite(text));
		bool flag = false;
		for (int i = 0; i < glossary.GetLength(0); i++)
		{
			if (Regex.IsMatch(text, glossary[i, 0], RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
			{
				if (!flag)
				{
					stringBuilder.AppendLine().AppendLine().AppendLine("Terms explained");
					flag = true;
				}
				stringBuilder.AppendLine().AppendLine(glossary[i, 1]);
			}
		}
		return stringBuilder.ToString();
	}
}
