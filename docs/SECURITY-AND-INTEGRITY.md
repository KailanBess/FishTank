# FishTank 0.7 security and integrity preview

Open **Security Center → Security and integrity tools**. Simple view remains the default. The tools explain findings; **Show full evidence (Advanced)** reveals raw before/after values and timestamps. No view automatically changes Windows policy, removes startup entries, closes ports, or grants permissions.

## Features implemented

| Feature | Behavior and scope |
| --- | --- |
| Protection health | Checks Defender real-time protection, signature age, firewall profiles, Secure Boot, system-drive BitLocker status, Windows Update service configuration and known restart markers. Unsupported or inaccessible checks stay unknown. It does not query Microsoft's update catalog or prove all updates are installed. Open Windows Update for that assessment. |
| Publisher and checksum checks | Windows Authenticode inspection plus SHA-256 comparison. Windows may use trust/revocation services. Valid signatures and matching hashes do not establish that a file is harmless. |
| Folder integrity | One explicitly accepted folder baseline, optional recursion and exact relative file/folder exclusions. Comparison reports added, changed, missing, unchanged and unknown paths. Limits: 5,000 files, 20,000 enumerated entries, 64 MiB per file, 512 MiB total. Reparse paths are refused/skipped. Incomplete baselines are not accepted; partial comparisons never infer that unreadable files are missing. |
| Startup changes | Inventories startup commands, scheduled task definitions and automatic services; compares with prior reviews or an explicitly accepted baseline. Command/action content is hashed rather than retained. Startup/task/service inventories are each capped at 500; gaps are unknown. A change is not a malware verdict. |
| Permissions | Current-user camera/microphone/location consent metadata, machine app-privacy policy and a separate browser-extension review. Metadata does not cover every app or prove present access; open Windows Privacy settings for decisions. |
| Security history | Up to six active snapshots per security category and five folder comparison reports, protected with current-user Windows DPAPI. Previous encrypted store versions may remain as `.bak` files. Explicit export warns about paths, accounts and network data. Exports are private reports, not signed forensic evidence. |
| Accounts | Existing read-only checks for local administrators/accounts, UAC, Remote Desktop/NLA, recent failed sign-ins and file shares, now available alongside history/comparison. Access-denied results remain unknown. |
| Backups and recovery | Authenticated decryption and validation of an existing encrypted FishTank password-vault backup; an isolated restore drill restores and reopens it in a temporary folder under the private profile, then deletes that folder. The active vault is not replaced. Windows backup/recovery settings are available separately. This does not create a full-PC backup or prove disk/BitLocker recovery. |
| Network exposure | Local TCP listeners with process names and bindings, UDP endpoints, and enabled inbound allow rules/port filters. TCP/UDP output is capped at 500 entries, firewall rules at 100. Router, rule-scope and VPN policy still determine reachability; no external port scan is performed. |
| App locking | Five-minute app-input inactivity default, Windows lock/disconnect default, configurable idle interval (0 disables it), and Ctrl+Shift+L. An app password is required. Visible app windows are hidden during reauthentication and open password-manager sessions are locked. The vault retains its independent five-minute/Windows-lock behavior. Already authorized operations can finish while the UI is locked. |
| Verified updates | Optional direct HTTPS manifest checks/downloads with consent. Packages must match the manifest checksum, a valid Windows Authenticode signature and configured certificate SHA-256 pin, recognized FishTank assembly identity, and a version newer than installed and no lower than the highest staged version. Packages are staged only; nothing is executed or installed. Limits: 64 KiB manifest, 128 MiB package, bounded transfer time. No redirects, credentials in URLs, nonstandard HTTPS ports or cross-host package URLs. |
| Reminders | Opt-in in-app reminders for security reviews and backup verification, optional generic Windows desktop notifications, and off/hourly/daily/weekly folder comparisons. Schedules run only while the desktop app is open. No scheduled task, privileged background monitor or online notification service is installed. Notification delivery depends on Windows settings. |

New app passwords/MFA remain configured in **Settings**. Security preference/trust changes and baseline replacement require the app password and an enrolled factor when app access is enabled. Publisher trust configuration requires an app password. Disabling sign-in MFA does not remove MFA requirements for sensitive security changes.

## Important limits

This application is a user-mode companion to Windows security, not an antivirus engine or tamper-resistant monitor. An attacker controlling the same Windows account or an administrator can alter the program and its private data. DPAPI does not create a defense against the same logged-in user. File comparisons are bounded observations over time, not an atomic filesystem snapshot; concurrent changes and privileged interference remain possible. A trusted baseline is only as trustworthy as the original files and your review.

Protect backups, master passphrases, recovery codes and exported reports. Temporary restore copies contain encrypted vault data; decrypted keys and contents exist in process memory during validation. Managed strings cannot be guaranteed erased from memory. Folder and review histories remain local unless explicitly exported. Review consent before performing online breach checks, certificate verification or release-feed downloads.

## Signed-release setup still required

Current development executables are **unsigned**. A release-signing certificate and its protected private key, an authenticated publisher certificate pin, and a published signed release are not available in this workspace. FishTank therefore refuses to treat these previews as verified updates. Do not disable verification to bypass that refusal.

The publisher must sign both desktop and CLI executables with a trusted Authenticode certificate and a suitable timestamp, verify the final signatures, and compute hashes **after** signing. Protect signing keys in an appropriate certificate store/HSM or signing service; do not commit keys or PFX files. For a local release workflow, use the Windows SDK SignTool with SHA-256 and the certificate provider's supported timestamp configuration, then `signtool verify /pa /all /v` on each output. Signing has not been performed here.

After publishing the signed package and manifest on one HTTPS host, configure the authenticated manifest URL and certificate SHA-256 under **Locking and reminders**. Example manifest shape (the following is illustrative, not an active release):

```json
{
  "Version": "0.7.1.0",
  "Sha256": "64_HEXADECIMAL_CHARACTERS_FROM_THE_SIGNED_EXECUTABLE",
  "PackageUrl": "https://releases.example.com/FishTank.exe"
}
```

There is no automatic installer, service restart, rollback mechanism, or certificate-rotation service. Stage desktop and CLI independently if required. Reverify staged files before manually installing them. A certificate rotation requires deliberate trust reconfiguration. HTTPS feed metadata alone is not a trusted update announcement until package verification passes.

## Validation

In the Linux workspace: desktop/CLI and both Windows harnesses compile; 64 logic/security checks, 10 mocked collector checks, and 17 existing backend tests pass. Both PowerShell resources parse under PowerShell 7.4.22. NuGet's vulnerability query reported no vulnerable packages for the desktop project's reference dependencies; this does not audit Windows/.NET runtime vulnerabilities or application logic.

Windows-only checks are implemented and wired into GitHub Actions: .NET Framework/DPAPI storage, app-password/MFA/replay behavior, encrypted backup integrity and isolated restoration, unsigned publisher rejection, a real read-only health collector, and UI/lock-flow tests. Those tests have **not been executed in this Linux workspace**. The UI harness captures all main pages and security tabs in light/dark themes and 100%/150% app text scale, plus checks that locking hides windows and locks the vault. Screenshot review, actual Windows DPI, screen readers, sleep/resume, RDP disconnect, long-running work during lock, and signed-update success with a real release still need Windows validation.

Run on Windows:

```powershell
dotnet run --project tests/Logic/Logic.csproj
./tests/Check-PowerShell.ps1
./tests/Test-Collectors.ps1
dotnet build tests/WindowsSecurity/WindowsSecurity.csproj -c Release
& ./tests/WindowsSecurity/bin/Release/net48/WindowsSecurity.exe
dotnet build tests/WindowsUi/WindowsUi.csproj -c Release
& ./tests/WindowsUi/bin/Release/net48/WindowsUi.exe "$PWD/ui-artifacts"
```

Rebuild/package the unsigned preview with `./scripts/Package-Preview.ps1`. It includes only the two executables, their runtime configuration, public documentation and checksums. `-NoBuild` packages existing outputs and is intended for the already-built CI workflow. No signing or installation occurs in that script.

## Independent review remains outstanding

No independent security review has occurred. A reviewer should examine authentication and recovery, local authorization, cryptographic storage/backups, parsing and update trust, filesystem race conditions, failure paths, baseline replacement, notification privacy, and application-lock behavior under modal dialogs and in-flight work. A compilation, test suite or dependency scan is not a substitute for that review. Do not describe this preview as audited, tamper-proof, or proven safe for important credentials.

Debian email/SMS delivery remains a separate deployment: its server files, sender domain/mail delivery, modem/SIM, private HTTPS and actual enrollment/delivery tests are still required.
