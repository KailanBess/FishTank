# FishTank

Windows application builds and a self-hosted email/SMS verification backend.

The [0.8.0 desktop preview](packages/FishTank-desktop-preview.zip) adds passphrase-encrypted personal file containers and GitHub release checking, verification and confirmed installation with executable backups/rollback. See [protected files and GitHub setup](docs/PROTECTED-FILES-AND-GITHUB-UPDATES.md) for the limits and required release-signing setup. Earlier [security/integrity tools](docs/SECURITY-AND-INTEGRITY.md) remain included. Builds and available cross-platform checks pass; Windows execution, a real signed release/install, and an independent audit remain pending. Original uploaded binaries below remain unchanged.

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
