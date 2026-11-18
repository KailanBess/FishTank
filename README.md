# FishTank

FishTank **0.17.0** is a Windows security companion with a desktop app, CLI, optional local browser dashboard and a separate self-hosted verification server kit. This preview adds a consolidated **Safety and release readiness** review and optional shared quiet hours for release announcements.

## Download and trust

Download the current `FishTank-desktop-preview.zip` from [Releases](../../releases), extract the whole package into a new folder and run FishTank. Preview releases are unsigned. Aquarium can install approved preview packages; FishTank also retains its explicit opt-in GitHub preview update controls.

Preview updates check GitHub SHA-256 metadata, component identity and version, with a separate apply confirmation and retained executable rollback copies. Stable signed updates retain their separate publisher-pin and Authenticode requirements. No certificate checks are bypassed. A genuine code-signing certificate and independent security review remain release gates; do not treat an unsigned preview as independently audited protection for important secrets.

## What you can do

- Review Windows protection health, Defender scans, integrity findings, disk/backup-age warnings and an activity timeline. Findings distinguish missing/stale evidence from observed issues.
- Use opt-in scan schedules, scan queues, live Defender alerts and tray monitoring. Windows policies and other antivirus products can affect availability.
- Review ransomware protection, ASR/CFA, application/hardware security, software updates, persistence and network exposure. Changes require explicit preview/confirmation and the applicable administrator/offline MFA access.
- Use a master-passphrase-encrypted password vault and protected-file containers, encrypted backup copies, verification history and isolated restore drills.
- Use app access controls, offline MFA and recovery codes, configurable idle locking, privacy tools, profiles and a previewed/redacted support report.
- Pair an optional browser dashboard on the same PC. Pairing/session expiry, locking and vault authentication remain in force.
- Configure optional email/SMS verification, server passkeys and signed device reports through a separate self-hosted service. These features require deployment and live tests.

Use **Ctrl+K** or **Settings → All tools** to find features. **Safety and release readiness** summarizes app access, offline recovery, publisher configuration, backup/restore evidence and optional server setup without exposing paths, account names, tokens or vault contents. It links directly to restore drills, signature inspection and connection/delivery tests. A configured connection or recorded backup is not proof of current delivery or restore success.

Aquarium can apply reviewed family colors and back up FishTank appearance only. Vaults, credentials, authentication records and security policy stay behind FishTank's native authenticated recovery tools. Supporting Aquarium quiet hours affect FishTank release announcements; security warnings and errors keep their existing controls.

## Server status

The server kit contains source, service examples and setup instructions for Debian. It is not a deployed service. Live email/SMS delivery, device enrollment and outage recovery still require configured infrastructure and a real test. The self-hosted route needs a sender domain/mail configuration and, for cellular SMS, a compatible modem and active SMS-capable SIM. Keep runtime secrets, tokens, databases and private configuration outside Git.

Read [server installation](server/README.md), [self-hosted delivery](server/SELF-HOSTED-SETUP.md) and [server expansion](docs/SERVER-EXPANSION.md). The desktop connection wizard tests entered/saved HTTPS settings and supports actual delivery enrollment without disabling certificate verification.

## Guides

- [Recovery, accessibility and signed releases](docs/RELEASE-AND-RECOVERY.md)
- [Release review and remaining gates](docs/RELEASE-REVIEW.md)
- [Outside usability testing](docs/BETA-TEST-GUIDE.md)
- [Protection and update setup](docs/PROTECTED-FILES-AND-GITHUB-UPDATES.md)
- [Security coverage](docs/SECURITY-COVERAGE.md)
- [Everyday tools](docs/EVERYDAY-TOOLS.md)
- [Local dashboard and account organization](docs/BROWSER-AND-ACCOUNT-ORGANIZATION.md)
- [Source recovery limitations](desktop/README.md)

Older binaries in `windows/` and historical uploaded reports are preserved reference material. Current downloads come from Releases; those historical reports do not prove current validation.

## Build and verification

Build on Windows with the .NET 8 SDK and .NET Framework 4.8 references:

```powershell
dotnet build desktop/FishTank.csproj -c Release
./scripts/Run-ReleaseChecks.ps1
```

Server tests use simulated delivery and do not send email/SMS:

```sh
cd server
python3 -m unittest -v test_server test_transports
```

CI checks Windows security, resilience, UI, shared logic, browser sessions and server behavior before publishing a preview. Physical display/screen-reader tests, separate Windows-account recovery, genuine signed install/rollback interruption testing, independent security review and live delivery/outage checks remain external acceptance work. No installed profile, vault or Windows protection policy is modified by isolated fixtures.