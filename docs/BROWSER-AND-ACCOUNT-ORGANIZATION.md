# Local browser dashboard and organized password vault

FishTank 0.10.0 adds a browser dashboard on the same Windows PC and email/service organization in the existing encrypted password vault. It needs no Debian server, hosted account, or additional Windows runtime beyond the app's .NET Framework 4.8 requirement. Windows 10/11 and an up-to-date browser are required. The Windows app remains running as the local agent.

## Open the dashboard

1. Extract the new preview into its own folder and open FishTank normally. Sign in if you have configured app access.
2. Go to **Settings → Open browser dashboard**. A private pairing link opens in your default browser.
3. Use Overview for protection/disk checks and backup-age evidence; Live alerts shows the recent retained Defender summaries. Quick scans need confirmation in the desktop window because existing Defender policy can remove detected threats.
4. Open **Passwords & connections**, then create or unlock your vault with its separate master passphrase.

The listener starts only when explicitly opened and stops when the desktop app exits or you select **Stop browser dashboard**. Browser support is not a conversion of every desktop feature: file containers, backups/restores, quarantine, updates and advanced system operations remain in the desktop app. Live monitoring is enabled separately in the desktop Monitoring page. This first version is local to one PC; there is no remote Debian/Tailscale dashboard or multi-device synchronization.

### One-time Windows listener permission, if needed

Windows HTTP.sys can require a URL reservation before a normal user can start a listener. If FishTank reports a listener permission error:

1. In your **normal** Windows PowerShell, run `whoami` and note the Windows account that will run FishTank.
2. Open a separate **PowerShell as administrator**, and substitute that exact account below:

```powershell
netsh http add urlacl url=http://127.0.0.1:47653/ user="YOUR-PC\YOUR-USER" listen=yes delegate=no
```

3. Close the administrator shell. Run FishTank as your normal user, then try **Open browser dashboard** again.

This reserves a single loopback URL for the specified account. Do not use Everyone, a wildcard host, or open a firewall port. If port 47653 or its reservation belongs to another program, leave it alone and resolve the conflict first. Inspect existing reservations with `netsh http show urlacl`; FishTank never deletes or replaces one automatically. To remove only the reservation you added, stop the browser dashboard and run `netsh http delete urlacl url=http://127.0.0.1:47653/` as administrator.

## Organize email accounts and services

Example:

```text
Personal email — you@example.com — email password
  EA — EA username — its own password
  Steam — Steam username — its own password
  Another service — linked account sign-in (no separate password)
Work email — you@work.example — a different email password
  Work service — service username — its own password
```

- **Email** entries store the email address, its password, an optional username, site and notes.
- **Service** entries can link to a saved email account while retaining their own username and password. Multiple accounts for the same service are allowed.
- Use **linked account sign-in** only for a service that does not have its own password. FishTank documents this method; it does not perform OAuth, sign in automatically or grant connections online.
- Changing an email address updates the connection display through its stable account ID. Move/unlink its services before deleting the email or changing its type; orphaned connections are rejected.
- Search by email, service, username or site. Existing standalone entries remain intact and can be linked by editing them.

In the desktop password manager, **Add email account** creates the parent. Select it and choose **Add linked service**. The browser uses **Add email → Connect service**. Notes and passwords are retrieved only when opening an entry; normal lists omit both. Passwords start masked, and explicit browser reveals hide after 30 seconds.

Both interfaces use `passwords.ftvault` in the same FishTank Windows data profile. The browser has a separate unlock session. Unlocking one interface does not unlock the other. Concurrent saves detect external changes and refuse to overwrite them: lock and reopen before continuing. Make encrypted backups from the desktop password manager; the browser does not export plaintext or choose arbitrary file paths.

### Existing vault compatibility

The new app reads the original version 1 encrypted payload without altering it on unlock. A successful save writes version 2, preserving old credentials and adding organization fields. The prior encrypted file is retained as `.bak` by the existing atomic-save mechanism (later saves replace that backup). **Older FishTank versions cannot open the new payload.** Make a separate encrypted backup before the first edit and keep the updated app. Portable mode and normal mode use different data profiles, as before.

## Browser session protection and limits

Pairing links work once and expire after one minute. Opening a new link revokes the previous browser session and locks its browser vault. The link's fragment is removed from the address bar by the page before pairing. Do not share it or screen-record the initial link. Browser history/synchronization or browser extensions may still observe a navigated link before removal.

Session credentials stay in page memory, not cookies, localStorage or sessionStorage. Reloading requires a new pairing link. Requests require the exact loopback Host and Origin, a bearer credential and an independent request nonce. There is no CORS, remote bind, proxy forwarding support, third-party script, analytics or request logging. Static pages are non-sensitive; data/actions require authentication. Responses disable caching and framing; the page has a restrictive content-security policy. Requests are size/time/concurrency bounded; unsuccessful vault unlocks and pairing guesses are rate limited.

Sessions expire after five minutes without a non-status API action, or eight hours absolutely. Automatic status refresh does not keep them unlocked. Browser vaults separately lock after five minutes without a vault action. Leaving the tab clears the displayed vault and requests a vault lock; closing a tab cannot guarantee immediate server notification, so expiry still applies. App locking, Windows lock/disconnection, tray parking, stopping the dashboard and exiting FishTank revoke browser access. Reopening or unlocking the desktop does not revive an old browser session. Results from an in-flight action are withheld after revocation; an already authorized Windows operation may continue.

HTTP is limited to `127.0.0.1`; this is not an encrypted remote connection. Do not publish it through a reverse proxy, port forward, Tailscale Serve or public interface. Local malware, administrators, a compromised browser/extension, or another program impersonating a stopped local listener remain outside the protection boundary. Keep the machine/browser trusted, use app locking and a unique vault master passphrase, and use drive encryption for stolen-device protection. Managed runtimes cannot guarantee erasure of every in-memory string. This is a development preview, not an audited password-manager release.

## Validation

The portable integration harness exercises the actual HTTP server, authentication/expiry policy, vault encryption, v1 migration, connected account validation, parent deletion protection and concurrent-edit rejection. It uses a portable serializer and fixtures for Windows collectors. Chromium UI checks exercise pairing, vault creation, email/service editing, linked sign-in, search, explicit reveal, locks, script-like text safety and layouts at 1440, 1024, 390 and 320 pixels, plus enlarged text. Captured screenshots were inspected in the Linux workspace. Windows-specific serialization/editor/lock checks are included in the Windows harness and compile here; native Windows execution, HTTP.sys permission setup, Defender operations and the Windows desktop visuals still require Windows verification. GitHub Actions runs these harnesses; remote run outcomes must be reviewed separately.
