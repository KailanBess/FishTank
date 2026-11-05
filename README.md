# FishTank

Version 0.15.6 includes [Aquarium family themes and customization](docs/AQUARIUM-APPEARANCE.md). The latest source and ZIP include the merged style changes and review fixes. Native Windows verification of this build remains pending. New profiles use Aquarium Ocean/Mint; existing profiles keep their saved choices. Open Customize FishTank → Use Aquarium style to preview and save the preset.

Windows application builds and a self-hosted email/SMS verification backend.

Version 0.14 adds ransomware protection review, audit-first ASR/CFA controls, application/hardware security, software update inventory, opt-in persistence/network monitoring and incident-response cases. See [wider security coverage](docs/SECURITY-COVERAGE.md) and [0.14 validation](docs/SECURITY-COVERAGE-VALIDATION.md). Open Settings → All tools, or Ctrl+K. Windows changes require explicit preview/confirmation, administrator access and offline MFA; monitoring starts off.

Version 0.13 adds backup failure alerts, a vault health dashboard, recorded security changes, selective storage cleanup, portable profile transfer, isolated sample mode, in-app release notes and a four-step server connection wizard. Read [everyday tools](docs/EVERYDAY-TOOLS.md). Open Settings → All tools, or press Ctrl+K.

The 0.12 expansion adds Ctrl+K tool search, favorites, scan queues, encrypted automatic backups, vault history/attachments/import/undo, notifications, profiles/layouts, offline help, signed device reports and optional server passkeys/status. Read [app expansion](docs/APP-EXPANSION.md) and [initial server deployment](docs/SERVER-EXPANSION.md). The server is not deployed and the preview is unsigned. Download the [server setup kit](packages/FishTank-server-kit.zip) when ready to install it.

The [0.15.6 desktop preview](packages/FishTank-desktop-preview.zip) adds a [local browser dashboard and organized email/service password vault](docs/BROWSER-AND-ACCOUNT-ORGANIZATION.md). Open Settings → Open browser dashboard to pair locally; see the guide for Windows listener permissions and vault compatibility. It also includes [optional live Defender alerts and tray monitoring](docs/LIVE-DETECTION.md), alongside [disk-health and backup-age warnings](docs/DISK-HEALTH-AND-BACKUP-AGE.md), with separate creation/verification dates and configurable reminders. It retains passphrase-encrypted file containers and verified GitHub installation; see [protection and update setup](docs/PROTECTED-FILES-AND-GITHUB-UPDATES.md). Earlier [security/integrity tools](docs/SECURITY-AND-INTEGRITY.md) remain included. Windows, browser and server checks pass; see [0.13 validation evidence](docs/EVERYDAY-VALIDATION.md). Live server deployment, trusted signing and an independent audit remain pending. Original uploaded binaries below remain unchanged.

## Files

- [Windows desktop app](windows/FishTank.exe) and [CLI](windows/FishTank.Cli.exe): uploaded, unsigned development builds, preserved unchanged.
- [Recovered desktop/CLI C# source](desktop/README.md): recovered from the uploaded assemblies and compiled successfully for .NET Framework 4.8. Read the recovery limitations before using rebuilt binaries.
- [Server ZIP](packages/FishTank-server-selfhosted.zip): download using GitHub's **Download raw file** button, then copy it to Debian.
- [Server source and installation instructions](server/README.md).
- [Self-hosted Postfix/Gammu setup](server/SELF-HOSTED-SETUP.md).
- [Previous update report](docs/FishTank-update-status.md) and [remaining checklist](docs/FishTank-remaining-checklist.md): historical reports supplied with the uploads; their test claims are not new verification.

## Current deployment status

The user reported preparing Debian 13 with Python, a virtual environment, Waitress, loopback-only Postfix, Gammu SMSD, and a stopped FishTank systemd service definition. The application files have not yet been transferred to that server. Live email/SMS delivery and desktop integration have not been verified here. A sender domain/mail configuration and a compatible cellular modem with an active SMS-capable SIM remain necessary for the self-hosted route.

The backend generates and validates codes itself; authentik is not required. Keep runtime secrets, device tokens, databases, and private configuration outside Git. The included environment files contain placeholders only.

## Backend tests

```sh
cd server
python3 -m unittest -v test_server test_transports
```

These tests exercise SQLite verification state and simulated delivery interfaces; they do not send real email or SMS. Windows builds require Windows and have not been executed in this Linux workspace.

Dashboard, scan scheduling, activity timeline, folder backup reminders, settings search and support-report controls are documented in [APP-INSIGHTS.md](docs/APP-INSIGHTS.md).
Setup, scan progress/cancellation evidence, recovery, accessibility and signed release preparation are described in [RELEASE-AND-RECOVERY.md](docs/RELEASE-AND-RECOVERY.md).
Release-readiness findings and the outside-testing guide are in [RELEASE-REVIEW.md](docs/RELEASE-REVIEW.md) and [BETA-TEST-GUIDE.md](docs/BETA-TEST-GUIDE.md).
