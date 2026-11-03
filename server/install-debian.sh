#!/bin/sh
# Reviewed, explicit initial deployment only. Does not expose ports or replace proxy routes.
set -eu
[ "$(id -u)" -eq 0 ] || { echo 'Run with sudo after reviewing this script.' >&2; exit 1; }
[ ! -e /etc/fishtank-mfa/environment ] || { echo 'Existing FishTank environment found. Use a reviewed upgrade instead.' >&2; exit 1; }
[ ! -e /opt/fishtank-mfa/server.py ] || { echo 'Existing service code found; refusing to replace it.' >&2; exit 1; }
source_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
apt-get update
apt-get install -y python3 python3-venv
getent passwd fishtank-mfa >/dev/null || adduser --system --group --no-create-home fishtank-mfa
install -d -m 755 /opt/fishtank-mfa
install -d -m 700 /etc/fishtank-mfa
install -d -m 700 -o fishtank-mfa -g fishtank-mfa /var/lib/fishtank-mfa
for name in server.py passkeys.py device_status.py passkeys.html passkeys.js requirements.lock; do
    install -m 644 "$source_dir/$name" "/opt/fishtank-mfa/$name"
done
python3 -m venv /opt/fishtank-mfa/venv
/opt/fishtank-mfa/venv/bin/pip install -r /opt/fishtank-mfa/requirements.lock
install -m 600 "$source_dir/environment.example" /etc/fishtank-mfa/environment
install -m 644 "$source_dir/fishtank-mfa.service" /etc/systemd/system/fishtank-mfa.service
systemctl daemon-reload
printf '%s\n' 'Installed service files. The service was NOT started or exposed.' \
 'Next: edit /etc/fishtank-mfa/environment locally: set a fresh pepper, delivery settings and your stable HTTPS FISHTANK_ORIGIN.' \
 'Configure HTTPS routes without replacing existing services. Then follow SERVER-EXPANSION.md to provision a private device token and test.'
