# FishTank 0.13 everyday tools

Open **Settings → All tools** or press **Ctrl+K**. This release adds the eight requested capabilities to the native desktop app.

| Feature | Where to find it | Behavior |
| --- | --- | --- |
| Backup failure alerts | Automatic encrypted backups; Notification center | While backups are enabled and FishTank is open, the destination is checked each minute. Disconnection, inaccessible/low-space status, scheduled copy failures and reconnection are recorded. Repeated destination problems are throttled to daily reminders; a failed copy is throttled by its configured interval. Reconnection does not count as a successful backup. |
| Vault health dashboard | Passwords → Open password manager → unlock → Vault health dashboard | Filter expired, soon-expiring, reused, short and repeating-pattern passwords. Findings show titles and reasons, never password values. This is a local heuristic review, not a breach lookup or proof of strength. |
| Security-change tracking | All tools → Security changes | Choose two recorded checks in the same category and inspect before/after evidence. Unknown coverage remains unknown. You can request a fresh read-only check separately; opening this tool does not run collectors. |
| Storage cleanup preview | All tools → Storage cleanup preview | Preview recognized FishTank download remnants and settings temporary files in the current profile, older than seven days. Select files explicitly and confirm removal. Changed files are rejected. Vaults, backups, verified updates, rollback copies, arbitrary directories and unrelated filenames are excluded. |
| Portable profile export | All tools → Portable profile transfer | Preview and export encrypted vault bytes plus appearance settings without wallpaper paths. Import validates limits/checksum and creates a new named profile without replacing an existing one. Reenroll app authentication, MFA, server credentials and device keys on the destination. |
| Safe demonstration mode | All tools → Sample demonstration mode; or `FishTank.exe --demo` | A separate sample-only process starts before real profile initialization. It uses in-memory data and simulated scan/backup/restore results, with no real collectors, vault access, server requests or background scheduler. Close it to finish. |
| In-app update notes | All tools → What's new | Search notes for the installed version, mark them read, and receive one retained release announcement per version. Popup preferences still apply. Notes are bundled offline; the app does not download or execute release text. |
| Server connection wizard | All tools → Server connection wizard; also under App access | Four steps cover service installation guidance, HTTPS/device-token testing, authenticated connection saving and real delivery enrollment, then optional passkeys/reporting. DNS, TLS, token, missing-route and timeout failures have specific guidance. |

## Profile transfer details

The `.ftprofile` file contains an already-encrypted vault and readable appearance settings. It does not contain the vault master passphrase or Windows DPAPI records. Its SHA-256 detects accidental damage, not a maliciously replaced bundle; verify the imported vault with its master passphrase before relying on it. Encrypted vaults are limited to 16 MiB; transfer files to 24 MiB. Imports clear wallpaper paths and refuse existing profile names and linked destination ancestors.

Named profiles are stored under `%LOCALAPPDATA%\FishTank\profiles`, including when exporting from a portable instance. Import creates no app-password/MFA enrollment automatically. Open the imported profile using Profiles and layouts, configure app access, and test recovery before removing anything from the original PC. Schedules, history, backup destinations, browser pairings, server tokens, passkey associations and reporting identities are deliberately absent from the bundle. Existing external passkeys are not deleted from their authenticators or the old server device.

## Cleanup and backup limits

Cleanup only previews top-level recognized files in the app profile: interrupted `github-download-...exe` / `release-download-...exe` files and named settings temporary files. It does not scan your Downloads folder or recurse through the PC. Size, modification time and SHA-256 are checked again before removal. Cleanup is selective and permanent; cancellation leaves the files alone.

Backup alerts follow the current notification and quiet-hour settings. The destination check establishes availability/free-space observations; the actual copy still checks write permissions and file integrity. No alert can be delivered while FishTank is closed. Failure recording does not require the disconnected destination to be present, so one missing drive no longer aborts the rest of scheduled app work.

## Server setup remains external

The wizard does not install the service or alter HTTPS routes. Use [SERVER-EXPANSION.md](SERVER-EXPANSION.md) and the server kit first. An HTTPS/device-status response does not certify mail/SMS delivery, passkey enrollment or remote sharing. Connection saving needs the existing app password and a fresh local MFA/recovery factor; actual destination enrollment also verifies the delivered one-use code. Keep offline recovery access. TLS certificate verification stays enabled.

The preview remains unsigned. Live deployment, physical authenticator testing, real delivery, publisher signing and independent usability/accessibility/security acceptance remain external checks.
