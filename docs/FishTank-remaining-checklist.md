# FishTank remaining checklist

Saved November 2, 2026. Items below remain unverified or incomplete.

## Server and email setup

- [ ] Choose an email provider. Brevo was recommended but has not been selected or configured.
- [ ] Decide whether to buy or use a domain for a sender such as `security@yourdomain.com`.
- [ ] Authenticate that domain with the email provider.
- [ ] Install the FishTank verification backend on Debian.
- [ ] Configure HTTPS through Tailscale Serve at `fishyserver.tail781f78.ts.net`.
- [ ] Configure SMTP credentials privately on the server.
- [ ] Provision a device token and connect FishTank.
- [ ] Test actual email delivery, enrollment and sign-in.

## Phone verification

- [ ] Choose an SMS provider or a cellular modem/gateway.
- [ ] Configure the server’s SMS gateway.
- [ ] Test real phone-code delivery, enrollment and sign-in.

## App appearance and behavior

- [ ] Confirm whether the navigation changes fix the refresh flash and minimize button moving left.
- [ ] Review the FishBowl customization mapping for any missing colors, variants or behavior.
- [ ] Try the bubble animation and confirm its appearance.
- [ ] Check layouts at different display scales, window sizes and text sizes.
- [ ] Complete accessibility and keyboard/screen-reader checks.

## Simple explanations throughout the app

User requirement: every bit of information should offer a simpler output option.

- [x] Provide an “Explain simply” option across app information, settings, status, results and app-owned messages. Windows-owned prompts keep their native interface.
- [x] Allow switching between simple explanations and full details without losing the current task or entered information.
- [x] Offer an app-wide preference for simple explanations by default, while keeping full details available.
- [x] Add authored plain-language messages and contextual term definitions; preserve raw diagnostic values.
- [x] Preserve essential security warnings and original details; add checks for warning meaning and raw-value preservation.
- [ ] Make these controls accessible by keyboard and screen reader.
- [ ] Review every information surface for coverage and check that simple and detailed versions convey the same facts.

## Authentication and security verification

- [ ] Test with a real external authenticator.
- [ ] Test the complete desktop → HTTPS server → email/SMS verification flow.
- [ ] Test server outages, reconnection, device revocation and destination changes.
- [ ] Add email/SMS selection to CLI sign-in if wanted; CLI currently uses authenticator/recovery codes.
- [ ] Review server access rules, abuse limits, monitoring and private backups.
- [ ] Obtain an independent security review before relying on the app for important passwords or protection.
- [ ] Rerun the skipped hard-link test in a suitable Windows test environment.
- [ ] Complete installer, upgrade and code-signing verification.

## Decisions still open

- Email: use Brevo or another provider?
- Domain: do you already own one?
- SMS: paid provider or your own modem?
- Access: keep the verification server private to Tailscale?
- Features: which additional protection features should be prioritized?

## Current status

The app remains an unsigned development build. The backend has not been deployed, and real email/SMS delivery has not been verified.

This file records remaining work and requirements; it does not indicate that implementation or verification has occurred.

Update: local self-hosted Postfix/Gammu support and configuration package are prepared. Debian deployment and actual delivery are still pending. See FishTank-update-status.md.
