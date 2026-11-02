# Email and SMS on your own Debian 13 server

## Simple explanation

Your Debian server can make and check the codes itself. It can also hand email to your own Postfix mail server and texts to your own cellular modem. You do not need Brevo or a commercial SMS API for those routes.

You still need:

- For normal internet email: a sender domain you control, working outgoing mail connectivity, suitable public IP/DNS configuration, and a configured mail server. Installing Postfix alone does not guarantee inbox delivery.
- For actual SMS: a compatible cellular modem connected to Debian and an active SMS-capable SIM. You still use a mobile carrier and its plan; Debian cannot send carrier texts using ordinary internet access alone.
- For remote app access: a working connection to your server. Tailscale can keep the verification API private. It does not carry messages to external email inboxes or phone numbers.

If you have none of those yet, FishTank's existing authenticator and backup codes work offline. Keep them available while setting up delivery.

**Status:** code and templates prepared locally; not installed on your Debian server. No real email or SMS delivery has been tested.

## Full setup details

### 1. Install the verification backend

Copy this package onto Debian. Use the existing README's installation steps to put it in `/opt/fishtank-mfa`, create its virtual environment and its separate `fishtank-mfa` system account, and install the systemd service. The application binds only to `127.0.0.1:8787`.

For the local Postfix/modem route, use `environment.selfhosted.example` instead of `environment.example`. Store the real configuration at `/etc/fishtank-mfa/environment`, owned by root with mode `0600`. Generate a unique random pepper privately and replace the sender-domain placeholder. Keep the pepper and database backups together in protected storage; do not paste them into chat.

The two new settings are:

```ini
EMAIL_TRANSPORT=postfix
SMS_TRANSPORT=gammu
```

Existing external-relay and gateway configurations remain supported with `EMAIL_TRANSPORT=smtp` and `SMS_TRANSPORT=gateway`, which are also the defaults when these settings are absent.

### 2. Set up local email delivery

Install and configure Postfix for the real sender domain and your server's network. FishTank's `postfix` transport submits to the fixed address `127.0.0.1:25`; no SMTP password or remote relay is used by FishTank in this mode. The local connection is not TLS-encrypted; it is deliberately restricted in code to loopback on the same trusted server. Postfix handles the onward SMTP connection and its TLS policy.

Configure Postfix to listen only on loopback if this is a send-only machine. Restrict relaying to local submissions; do not enable an open relay. Confirm public outbound SMTP connectivity with your ISP/server host. Tailscale's private address is not your public mail-sending address. Configure forward and reverse DNS, sender authentication and TLS for onward delivery. Use the receiver's requirements: Gmail requires SPF or DKIM, forward/reverse DNS and TLS for all senders, and has additional rules for bulk senders. SPF, DKIM and DMARC together are a sensible target, but no invented DNS values are supplied here because your domain/IP are not configured.

The domain in `SMTP_FROM` must be yours. The server's `ts.net` hostname is for its HTTPS certificate, not a sender domain you control. A local-only mailbox is a different setup and does not deliver to an ordinary public inbox by itself.

### 3. Set up the local SMS modem

Install Gammu SMSD and configure it for the actual supported modem, serial device, SIM and carrier. The serial device and connection type depend on hardware; this package does not guess them. Confirm the modem is registered on the mobile network and that the SIM plan permits this usage.

The backend calls `/usr/bin/gammu-smsd-inject -c /etc/gammu-smsdrc TEXT <fixed-device-number>`. Message text goes through standard input, not command-line arguments or a shell. Its exit status confirms queue submission, not receipt on the phone. Do not enable verbose content logging.

The backend's sandbox must be allowed to submit to Gammu's actual queue. `gammu-sandbox.conf.example` shows a restricted systemd override for a spool at `/var/spool/gammu` with group `gammu`. Inspect your Debian package's service account, spool paths and permissions before adapting it. Allow only the required queue directory and configuration-group access. Keep `NoNewPrivileges=true`; do not run the verification service as root or make the queue world-readable/writable. The SMSD process, rather than FishTank, needs access to the modem device.

Queued email and SMS messages contain the code in plaintext until dispatched, and queue software may retain sent/failed copies. Protect both queues and their backups, configure limited retention, and keep bodies/codes out of logs. The verification database stores code digests, not plaintext codes, but transport queues have their own storage. If queued delivery takes more than five minutes, the code will be expired when received.

### 4. Enable private HTTPS

Inspect existing routes first:

```sh
tailscale serve status
```

After checking for conflicts, publish the loopback backend privately:

```sh
tailscale serve --bg http://127.0.0.1:8787
```

Use `https://fishyserver.tail781f78.ts.net/` in FishTank after confirming this is still your server's hostname. Follow Tailscale's HTTPS enablement flow if needed. Keep the tailnet access policy limited to your intended clients. Do not use Funnel for this private API. Do not replace an existing Serve route without reviewing it.

### 5. Add this PC and connect the app

Run the backend's `add-device` command locally as the service account with the service environment loaded by systemd. For example, after installing the service:

```sh
sudo systemd-run --quiet --wait --collect --pty \
  --property=User=fishtank-mfa --property=Group=fishtank-mfa \
  --property=EnvironmentFile=/etc/fishtank-mfa/environment \
  /opt/fishtank-mfa/venv/bin/python /opt/fishtank-mfa/server.py add-device
```

Enter a unique device name and the fixed recipient email/phone. Keep the displayed device token private. In FishTank, open **Settings → Multifactor authentication → Email / phone server**, enter the server URL and token privately, then test/save the connection. Verify each delivery method using a code actually received at its destination. An app password and offline authenticator must already be configured.

### 6. Verify before relying on delivery

Receive a real email and a real text, then complete enrollment and sign-in for each. Test an expired code, replay, resend, wrong code and server outage. Confirm offline authenticator/recovery access still works. Revoke a test device and confirm it loses access. To change destinations, revoke and provision a replacement device with a new token/binding and enroll it again; do not directly edit destination columns while retaining the old binding.

Keep private backups of the database, pepper and configuration. Confirm restoration on an isolated instance; restoring older state can also restore old verification/revocation state. Monitor service health and queue failures without logging secrets. Existing per-device limits remain one request per minute and five per hour, with five checks per challenge and a five-minute lifetime. Provider/network-wide abuse limits and an independent security review remain separate work.

## Verification performed here

The 12 existing backend tests and 5 added transport tests passed on Windows using actual SQLite and mocked delivery interfaces. They check queue-call construction, fixed loopback mail submission, secrets excluded from command arguments, invalid-recipient rejection, failure handling, expiry, replay, binding and revocation. They do **not** prove Debian service permissions, modem compatibility, real SMTP/SMS delivery or HTTPS reachability.

## Primary references

- [Postfix basic configuration](https://www.postfix.org/BASIC_CONFIGURATION_README.html)
- [Gmail sender requirements](https://support.google.com/mail/answer/81126?hl=en)
- [Debian 13 Gammu SMSD injection manual](https://manpages.debian.org/trixie/gammu-smsd/gammu-smsd-inject.1.en.html)
- [Tailscale Serve](https://tailscale.com/docs/reference/tailscale-cli/serve)
