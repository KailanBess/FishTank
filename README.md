# FishTank

Windows application builds and a self-hosted email/SMS verification backend.

## Files

- [Windows desktop app](windows/FishTank.exe) and [CLI](windows/FishTank.Cli.exe): uploaded, unsigned development builds. Desktop source code is not included, so these binaries cannot be rebuilt from this repository.
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
