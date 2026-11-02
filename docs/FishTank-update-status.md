# FishTank update — November 2, 2026

## Completed locally

- Added authored plain-language explanations and contextual definitions across app windows, settings, results, information cards, confirmations and errors.
- Preserved full original wording and technical values. Editable fields, authenticator setup keys/URIs and recovery-code fields are excluded from the information reader.
- Added a saved simple-explanation preference, live label/status updates, per-item explanation menus and F1 access. Fixed toolbar restoration after appearance previews and scrolling for longer information cards.
- Added self-hosted delivery transports: fixed-loopback Postfix email submission and local Gammu SMSD queueing for a cellular modem. Existing TLS SMTP and HTTPS gateway transports remain available.
- Prepared the Debian configuration examples and simple/full setup guide in FishTank-server-selfhosted.zip.

## Verification

- App suite: 102 passed, 0 failed, 1 skipped. Hard-link creation was denied by this Windows environment, so that check still needs a suitable Windows test environment.
- Final explanation/layout checks after the last wording and layout adjustments: 7 passed, 0 failed. Small-window rendering was visually inspected.
- Backend: 17 tests passed, using actual SQLite plus simulated transport interfaces. No real message was sent.
- The app and CLI compile successfully. The build is unsigned.

## Still required on the real server

- A working server connection and installation on Debian 13. The connected server bridge returned an availability error; no remote installation was performed.
- For public email without a relay provider: your sender domain, configured Postfix, public sending connectivity, forward/reverse DNS, sender authentication and TLS for onward delivery.
- For SMS without an API provider: compatible cellular modem, active SMS-capable SIM/carrier plan, configured Gammu SMSD and verified queue/service permissions.
- Private Tailscale HTTPS configuration, a provisioned device token, app connection, actual delivery, enrollment, sign-in, outage and revocation tests.
- Real external-authenticator testing, full screen-reader/system-DPI checks, installer/upgrade/code-signing verification and independent security review.

Windows-owned prompts remain Windows interfaces; their related FishTank controls and confirmations provide explanations. CLI structured output remains unchanged. Authenticator and recovery codes continue to provide the offline authentication option.

See the packaged SELF-HOSTED-SETUP.md for requirements, commands, transport limitations and primary documentation links.
