# Deploying passkeys and optional device status

The owner confirmed the FishTank verification service has **not been installed on the existing server**. This package prepares it; no live deployment, delivery, physical key enrollment or remote report upload has occurred.

## Initial Debian installation

1. Copy the `server` folder to a temporary directory on your Debian server. Review `install-debian.sh`, then run `sudo sh ./install-debian.sh` from that folder. It installs into `/opt/fishtank-mfa`, creates the restricted systemd service and private state directory, and leaves the service stopped. It refuses existing service configuration/code instead of overwriting it.
2. Edit `/etc/fishtank-mfa/environment` on the server. Generate a fresh pepper locally with `python3 -c 'import secrets; print(secrets.token_urlsafe(48))'`. Configure actual SMTP/Postfix and optional SMS delivery using the existing setup guides. Set `FISHTANK_ORIGIN` to your exact stable HTTPS origin, such as `https://fishyserver.tail781f78.ts.net` **only if you control its HTTPS routing**. No path, credentials, query or fragment is accepted. Changing origin invalidates existing passkey credentials for the old origin.
3. Inspect the existing reverse proxy/Tailscale Serve configuration before changing it. The Fishyserver/OpenCode bridge is a separate service. Do not replace its root route. Use a dedicated hostname, or add only these unoccupied routes to `127.0.0.1:8787`, preserving their complete paths: `/passkeys`, `/passkeys.js`, `/passkeys/options`, `/passkeys/finish`, `/v1/status`, `/v1/challenge`, `/v1/check`, `/v1/passkeys/*`, `/v1/devices/*`. The Caddy example is for a dedicated hostname. Keep bodies/tokens out of logs, limit abusive connections, and allow bodies up to 64 KiB at the proxy; the application applies tighter per-endpoint limits. The raw listener stays on loopback.
4. In a root shell, load the root-owned environment without printing it, then provision a device:

   ```sh
   set -a
   . /etc/fishtank-mfa/environment
   set +a
   /opt/fishtank-mfa/venv/bin/python /opt/fishtank-mfa/server.py add-device
   systemctl enable --now fishtank-mfa
   systemctl status fishtank-mfa
   ```

   Save the displayed device token privately. Each device receives its own token and fixed delivery destinations. Never commit tokens, peppers, database copies or environment files.
5. In FishTank, configure app password and offline MFA first. Open Settings → App access → Authenticator and recovery codes and configure the HTTPS delivery server/token. Verify and enroll the delivery channel. Confirm an actual email/SMS arrives; queue acceptance is not proof of delivery.
6. Open Ctrl+K → Passkeys and security keys. Enrollment needs the app password and a server-delivered **settings** OTP. Continue in the system browser, approve your authenticator and check completion in FishTank. A registered passkey becomes an optional online second factor alongside the app password. Keep offline MFA and recovery codes. It never decrypts the password vault.
7. Open Ctrl+K → Devices and signed reports to enroll the reporting key using another settings OTP. Enable manual publishing, preview/export a summary if desired, then publish it. Sharing defaults off; there is no periodic upload or remote-command API.

## Peer groups and trust

Device summaries are private to their bearer-authorized owner unless the server administrator assigns reporting keys to the same group. With the service stopped and a private database backup made, the administrator can use SQLite parameterized administration or a database tool to set `report_keys.group_name` for selected unique device owners. Client requests cannot choose groups. Remove the group assignment to stop future peer visibility; previously downloaded copies cannot be recalled. Revoking a device disables its authentication and hides its report.

Reports carry an RSA/SHA-256 signature, random profile ID, sequence, UTC timestamp, app version and a bounded protection summary. They omit hostnames, account names, paths, raw collector data and vault contents. Server publishing requires an enrolled key, increasing sequence and a timestamp within ten minutes. Desktop verification requires an independently supplied key fingerprint. A signature is not a security verdict, and imported offline reports require the reader to compare timestamps/sequences against earlier reports. The server is trusted to enforce access and availability; a compromised server can deny access or lie about online authentication results.

## Validation and pending checks

Run `python3 -m pip install -r requirements.lock` and `python3 -m unittest discover -s . -v` inside the server folder. Tests use real generated EC/RSA keys and synthetic WebAuthn attestation/authentication data. They cover context/origin/signature/user-verification failures, replay/expiry, OTP step-up and report isolation. They do not replace testing your real HTTPS proxy, mail/modem delivery, browser, Windows Hello/security key, backups or certificate renewal.

Use the existing Debian hardening and private database-backup guidance. Upgrade source and pinned dependencies in a maintenance window, retaining code/database rollback copies. The initial installer is not an automatic upgrade or schema-downgrade tool.
