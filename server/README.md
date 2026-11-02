# FishTank verification server for Debian 13

This is a real, tested backend foundation for hosting your own email/SMS code verification. It needs no graphical desktop. **It is not deployed.** FishTank 0.6 includes the desktop connection and destination-enrollment UI. SMTP/SMS live delivery and HTTPS deployment remain pending. No codes are printed or simulated in normal operation.

The server generates eight-digit cryptographic codes, expires them after five minutes, accepts each once, limits checking to five attempts, and binds every challenge to its device, delivery channel, purpose and destination revision. Resends invalidate the old channel challenge. Delivery is limited to once per minute and five times per hour per device, including failed deliveries. Provider-wide quotas and reverse-proxy abuse controls are still required before internet exposure.

Email uses an SMTP server/relay you control, with certificate-validated STARTTLS or implicit TLS. The new local Postfix transport submits only to fixed loopback 127.0.0.1:25. SMS can alternatively queue through a local Gammu SMSD installation and your own modem/SIM. See SELF-HOSTED-SETUP.md for the simple explanation, requirements and sandbox configuration. SMS uses an administrator-configured authenticated HTTPS gateway. Its contract is `POST {"to":"+15551234567","message":"..."}` and a 2xx response only when accepted for delivery. A modem daemon or provider adapter can implement that gateway. A Debian server without a carrier-connected gateway/modem cannot itself deliver SMS. Email/SMS are weaker than phishing-resistant hardware factors; retain authenticator/recovery access.

## Install on your server (review and run yourself)

Copy this folder to `/opt/fishtank-mfa`. Do not put real credentials into chat or the source archive.

```sh
sudo apt update
sudo apt install python3 python3-venv
sudo adduser --system --group --no-create-home fishtank-mfa
sudo install -d -m 700 -o fishtank-mfa -g fishtank-mfa /var/lib/fishtank-mfa
sudo install -d -m 700 /etc/fishtank-mfa
cd /opt/fishtank-mfa
sudo python3 -m venv venv
sudo venv/bin/pip install -r requirements.txt
sudo install -m 600 environment.example /etc/fishtank-mfa/environment
```

Edit `/etc/fishtank-mfa/environment` locally. Generate a unique pepper using `python3 -c 'import secrets; print(secrets.token_urlsafe(48))'`. Keep it and database backups private. Configure your SMTP details. Leave SMS unset until your gateway is available. Do not use placeholder credentials. The service must fail closed if delivery is unavailable.

Install the reviewed systemd unit:

```sh
sudo install -m 644 fishtank-mfa.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now fishtank-mfa
sudo systemctl status fishtank-mfa
```

The listener binds **127.0.0.1:8787 only**. Put a trusted-certificate HTTPS reverse proxy in front, optionally reachable only through Tailscale. `Caddyfile.example` is a template, not an installed/public service. Configure its real hostname and network rules deliberately. Never expose the raw HTTP port. Keep request bodies, bearer tokens, codes and query data out of proxy/application logs. Add upstream connection/IP request limits and monitor delivery-provider quotas. No automatic firewall changes are included.

## Provision a device

Provisioning is administrator-only CLI work. Load the root-owned environment in a root shell without printing it, then run `/opt/fishtank-mfa/venv/bin/python /opt/fishtank-mfa/server.py add-device`. It interactively asks for a device name and its fixed email/phone destinations. Save the displayed random device token privately; only its SHA-256 is stored on the server. Never ship a shared token in the desktop app. `revoke-device` disables every matching device name; use unique names.

Destinations are fixed by the server administrator. Clients cannot select arbitrary recipients. Desktop enrollment must confirm possession before enabling a channel. Updating contacts must also rotate the device binding and require desktop reenrollment. Do not directly edit contact columns without rotating that binding.

## API for the desktop integration

All endpoints use non-browser authenticated JSON POST, `Authorization: Bearer <device-token>`, and HTTPS at the proxy. No CORS is enabled.

- `/v1/status` → protocol version, destination binding, available channels; no full address/phone returned.
- `/v1/challenge` with `channel` (`email`/`sms`), `purpose` (`enroll`/`signin`/`settings`), `binding` → opaque challenge, expiration and matching context. A code is never in this response.
- `/v1/check` with `challenge`, eight-digit `code`, matching `purpose` → approved once with matching challenge/channel/binding context, or a distinct failure. Clients must validate all context fields and still verify their local app password. No arbitrary server approval flag may bypass that password.

FishTank 0.6 stores the per-device token with current-user DPAPI, requires existing app-password/offline-factor verification before connection changes, confirms destination possession before enabling a channel, validates challenge context and still requires the local app password. Keep authenticator/recovery access for server outages. CLI entry currently uses offline factors only.

## Testing and limits

Run `python3 -m unittest -v test_server.py`. Tests use in-memory delivery capture only; no message is sent and no live credential is used. They cover actual SQLite state transitions, expiry, replay, purpose/device/destination binding, resend, quotas, attempts, revocation, delivery failure, token/code hashes and HTTP validation.

SQLite stores destination contact information; protect the service account, database, pepper, SMTP credentials and SMS gateway token. Codes are HMAC-SHA-256 digests bound to their challenge; database theft alone does not expose the code without the pepper. Device bearer-token theft permits sending to that device's fixed destinations and attempting verification, so client storage and revocation matter. No per-IP limiter, hardware attestation, independent audit, horizontal scaling, encrypted contact database or delivery receipt guarantee is claimed. SMTP/gateway acceptance is not proof a person received a message.

Reference: [Python SMTP TLS](https://docs.python.org/3.12/library/smtplib.html), [Waitress deployment](https://docs.pylonsproject.org/projects/waitress/en/stable/usage.html).

