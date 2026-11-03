"""FishTank verification service. Run behind an HTTPS reverse proxy; never log OTPs."""
import argparse
from contextlib import contextmanager
import hashlib
import hmac
import json
import os
import re
import secrets
import smtplib
import sqlite3
import ssl
import subprocess
import time
import urllib.request
from email.message import EmailMessage
from pathlib import Path


class Rejected(Exception):
    def __init__(self, status, message):
        self.status, self.message = status, message


class Service:
    def __init__(self, database, pepper, deliver=None, clock=time.time, origin=None):
        if len(pepper) < 32 or pepper.startswith('REPLACE'):
            raise ValueError('FISHTANK_PEPPER must have at least 32 random characters')
        self.database, self.pepper, self.clock = str(database), pepper.encode(), clock
        self.deliver = deliver or delivery
        Path(database).parent.mkdir(parents=True, exist_ok=True)
        with self.db() as db:
            db.executescript('''
              CREATE TABLE IF NOT EXISTS devices (
                token TEXT PRIMARY KEY, name TEXT, email TEXT, phone TEXT,
                binding TEXT NOT NULL, enabled INTEGER NOT NULL DEFAULT 1);
              CREATE TABLE IF NOT EXISTS challenges (
                id TEXT PRIMARY KEY, device TEXT, channel TEXT, binding TEXT,
                purpose TEXT, digest TEXT, created INTEGER, expires INTEGER,
                attempts INTEGER DEFAULT 0, used INTEGER DEFAULT 0,
                delivered INTEGER DEFAULT 0);
            ''')
        if os.name != 'nt':
            os.chmod(self.database, 0o600)
        self.passkeys = None
        from device_status import DeviceStatus
        self.device_status = DeviceStatus(self, Rejected)
        if origin:
            from passkeys import Passkeys
            self.passkeys = Passkeys(self, origin, Rejected)

    @contextmanager
    def db(self):
        db = sqlite3.connect(self.database, timeout=10)
        db.row_factory = sqlite3.Row
        try:
            with db:
                yield db
        finally:
            db.close()

    def add_device(self, name, email, phone):
        if not name or len(name) > 100:
            raise ValueError('Device name required (maximum 100 characters)')
        if email and (len(email) > 254 or not re.fullmatch(r'[^\s@<>]+@[^\s@<>]+\.[^\s@<>]+', email)):
            raise ValueError('Invalid email')
        if phone and not re.fullmatch(r'\+[1-9][0-9]{7,14}', phone):
            raise ValueError('Phone must use E.164, such as +15551234567')
        if not email and not phone:
            raise ValueError('Provide at least one delivery destination')
        token = secrets.token_urlsafe(32)
        with self.db() as db:
            db.execute('INSERT INTO devices(token,name,email,phone,binding) VALUES(?,?,?,?,?)',
                       (self.token_hash(token), name, email, phone, secrets.token_hex(16)))
        return token

    @staticmethod
    def token_hash(token):
        return hashlib.sha256(token.encode()).hexdigest()

    def digest(self, challenge, code):
        return hmac.new(self.pepper, (challenge + ':' + code).encode(), hashlib.sha256).hexdigest()

    def request(self, token, path, body):
        if not isinstance(token, str) or not re.fullmatch(r'[A-Za-z0-9_-]{43}', token):
            raise Rejected(401, 'Device authorization required')
        owner = self.token_hash(token)
        with self.db() as db:
            device = db.execute('SELECT * FROM devices WHERE token=? AND enabled=1', (owner,)).fetchone()
        if device is None:
            raise Rejected(401, 'Device authorization required')
        if path.startswith('/v1/devices/'):
            return self.device_status.request(token, device, path, body)
        if path.startswith('/v1/passkeys/'):
            if self.passkeys is None:
                raise Rejected(503, 'Passkeys are not configured on this server')
            return self.passkeys.native(token, device, path, body)
        if path == '/v1/status':
            return {'version': 1, 'binding': device['binding'],
                    'email_available': bool(device['email']), 'sms_available': bool(device['phone'])}
        if path == '/v1/challenge':
            channel, purpose = body.get('channel'), body.get('purpose')
            if channel not in ('email', 'sms') or purpose not in ('enroll', 'signin', 'settings'):
                raise Rejected(400, 'Invalid channel or purpose')
            if body.get('binding') != device['binding']:
                raise Rejected(409, 'Destination binding changed; reenrollment required')
            destination = device['email' if channel == 'email' else 'phone']
            if not destination:
                raise Rejected(400, 'Channel is not provisioned for this device')
            now = int(self.clock())
            challenge, code = secrets.token_urlsafe(32), f'{secrets.randbelow(100000000):08d}'
            with self.db() as db:
                db.execute('BEGIN IMMEDIATE')
                recent = db.execute('SELECT COUNT(*),MAX(created) FROM challenges WHERE device=? AND created>?',
                                    (owner, now - 3600)).fetchone()
                if recent[0] >= 5 or recent[1] is not None and now - recent[1] < 60:
                    raise Rejected(429, 'Delivery rate limit; wait before requesting another code')
                # Failed deliveries also consume the rate budget; no send-loop on outages.
                db.execute('UPDATE challenges SET used=1 WHERE device=? AND channel=?', (owner, channel))
                db.execute('INSERT INTO challenges(id,device,channel,binding,purpose,digest,created,expires) VALUES(?,?,?,?,?,?,?,?)',
                           (challenge, owner, channel, device['binding'], purpose,
                            self.digest(challenge, code), now, now + 300))
                db.execute('DELETE FROM challenges WHERE expires<?', (now - 86400,))
            try:
                self.deliver(channel, destination, code)
            except Exception:
                raise Rejected(503, 'Delivery failed; no usable code was issued') from None
            with self.db() as db:
                db.execute('UPDATE challenges SET delivered=1 WHERE id=?', (challenge,))
            return {'challenge': challenge, 'expires_in': 300, 'purpose': purpose,
                    'channel': channel, 'binding': device['binding']}
        if path == '/v1/check':
            challenge, code = body.get('challenge', ''), body.get('code', '')
            if not isinstance(challenge, str) or not isinstance(code, str) or not re.fullmatch(r'[0-9]{8}', code):
                raise Rejected(400, 'Enter an eight-digit code')
            now = int(self.clock())
            with self.db() as db:
                db.execute('BEGIN IMMEDIATE')
                row = db.execute('SELECT * FROM challenges WHERE id=? AND device=?', (challenge, owner)).fetchone()
                if row is None or row['used'] or not row['delivered'] or row['expires'] <= now or row['attempts'] >= 5:
                    raise Rejected(403, 'Code invalid, expired or already used')
                if row['binding'] != device['binding'] or body.get('purpose') != row['purpose']:
                    raise Rejected(403, 'Verification context does not match')
                matched = hmac.compare_digest(row['digest'], self.digest(challenge, code))
                db.execute('UPDATE challenges SET attempts=attempts+1,used=? WHERE id=?',
                           (1 if matched or row['attempts'] + 1 >= 5 else 0, challenge))
            if not matched:
                raise Rejected(403, 'Code invalid, expired or already used')
            return {'approved': True, 'challenge': challenge, 'purpose': row['purpose'],
                    'channel': row['channel'], 'binding': row['binding']}
        raise Rejected(404, 'Unknown endpoint')

    def __call__(self, environ, start_response):
        try:
            path = environ.get('PATH_INFO', '')
            if path in ('/passkeys', '/passkeys.js') and environ.get('REQUEST_METHOD') == 'GET' and self.passkeys:
                name = 'passkeys.html' if path == '/passkeys' else 'passkeys.js'
                data = Path(__file__).with_name(name).read_bytes()
                start_response('200 OK', [('Content-Type', 'text/html; charset=utf-8' if name.endswith('.html') else 'text/javascript; charset=utf-8'),
                    ('Content-Length', str(len(data))), ('Cache-Control', 'no-store'), ('Referrer-Policy', 'no-referrer'), ('X-Content-Type-Options', 'nosniff'),
                    ('Content-Security-Policy', "default-src 'none'; script-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'"),
                    ('Permissions-Policy', 'publickey-credentials-create=(self), publickey-credentials-get=(self)')])
                return [data]
            browser = path in ('/passkeys/options', '/passkeys/finish')
            if environ.get('REQUEST_METHOD') != 'POST' or (not browser and environ.get('HTTP_ORIGIN')):
                raise Rejected(405, 'Only authenticated non-browser POST requests are supported')
            if browser and (not self.passkeys or environ.get('HTTP_ORIGIN') != self.passkeys.origin or environ.get('HTTP_SEC_FETCH_SITE', 'same-origin') != 'same-origin'):
                raise Rejected(403, 'Same-origin ceremony required')
            if environ.get('CONTENT_TYPE', '').split(';')[0] != 'application/json':
                raise Rejected(415, 'JSON required')
            length = int(environ.get('CONTENT_LENGTH', '0'))
            if length < 2 or length > (65536 if browser else 16384 if path == '/v1/devices/publish' else 4096):
                raise Rejected(413, 'Invalid request size')
            body = json.loads(environ['wsgi.input'].read(length))
            if not isinstance(body, dict):
                raise Rejected(400, 'JSON object required')
            auth = environ.get('HTTP_AUTHORIZATION', '')
            token = auth[7:] if auth.startswith('Bearer ') else ''
            result = self.passkeys.browser(path, body) if browser else self.request(token, path, body)
            status = 200
        except Rejected as error:
            status, result = error.status, {'error': error.message}
        except (ValueError, TypeError, UnicodeError):
            status, result = 400, {'error': 'Invalid request'}
        except Exception:
            status, result = 503, {'error': 'Verification unavailable'}
        data = json.dumps(result).encode()
        reasons = {200: 'OK', 400: 'Bad Request', 401: 'Unauthorized', 403: 'Forbidden',
                   404: 'Not Found', 405: 'Method Not Allowed', 409: 'Conflict',
                   413: 'Content Too Large', 415: 'Unsupported Media Type',
                   429: 'Too Many Requests', 503: 'Service Unavailable'}
        start_response(f'{status} {reasons[status]}', [('Content-Type', 'application/json'),
                       ('Cache-Control', 'no-store'), ('Content-Length', str(len(data)))])
        return [data]


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        return None


def delivery(channel, destination, code):
    if channel not in ('email', 'sms') or not re.fullmatch(r'[0-9]{8}', code):
        raise ValueError('Invalid delivery context')
    if channel == 'email' and not re.fullmatch(r'[^\s@<>]+@[^\s@<>]+\.[^\s@<>]+', destination):
        raise ValueError('Invalid email destination')
    if channel == 'sms' and not re.fullmatch(r'\+[1-9][0-9]{7,14}', destination):
        raise ValueError('Invalid SMS destination')
    if channel == 'email':
        sender = os.environ['SMTP_FROM']
        if len(sender) > 254 or not re.fullmatch(r'[^\s@<>]+@[^\s@<>]+\.[^\s@<>]+', sender) or sender.startswith('-'):
            raise ValueError('Invalid sender')
        message = EmailMessage()
        message['From'], message['To'], message['Subject'] = sender, destination, 'FishTank verification code'
        message.set_content(f'Your FishTank verification code is {code}. It expires in five minutes. '
                            'Do not share it. If you did not request this code, ignore it.')
        transport = os.environ.get('EMAIL_TRANSPORT', 'smtp')
        if transport == 'postfix':
            # Fixed loopback destination, never an administrator/client-supplied remote host.
            # No setgid helper or service sandbox relaxation is needed for local mail.
            # Postfix acceptance is queueing, not proof of inbox delivery.
            with smtplib.SMTP('127.0.0.1', 25, timeout=15) as smtp:
                smtp.send_message(message)
            return
        if transport != 'smtp':
            raise ValueError('Unsupported email transport')
        host = os.environ['SMTP_HOST']
        mode = os.environ.get('SMTP_MODE', 'starttls')
        context = ssl.create_default_context()
        if mode == 'tls':
            smtp = smtplib.SMTP_SSL(host, int(os.environ.get('SMTP_PORT', '465')), timeout=15, context=context)
        elif mode == 'starttls':
            smtp = smtplib.SMTP(host, int(os.environ.get('SMTP_PORT', '587')), timeout=15)
            smtp.ehlo()
            smtp.starttls(context=context)
            smtp.ehlo()
        else:
            raise ValueError('TLS delivery required')
        with smtp:
            if os.environ.get('SMTP_USER'):
                smtp.login(os.environ['SMTP_USER'], os.environ['SMTP_PASSWORD'])
            smtp.send_message(message)
    else:
        transport = os.environ.get('SMS_TRANSPORT', 'gateway')
        if transport == 'gammu':
            config = os.environ.get('GAMMU_CONFIG', '/etc/gammu-smsdrc')
            if not os.path.isabs(config):
                raise ValueError('Gammu configuration must be an absolute path')
            # Gammu SMSD later submits the queued message using the attached modem.
            subprocess.run(['/usr/bin/gammu-smsd-inject', '-c', config, 'TEXT', destination],
                           input=f'FishTank code: {code}. Expires in five minutes.\n'.encode('ascii'),
                           stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                           check=True, timeout=15)
            return
        if transport != 'gateway':
            raise ValueError('Unsupported SMS transport')
        # Administrator-controlled HTTPS adapter, never a URL supplied by a client.
        endpoint = os.environ['SMS_GATEWAY_URL']
        if not endpoint.startswith('https://'):
            raise ValueError('HTTPS SMS gateway required')
        payload = json.dumps({'to': destination, 'message': f'FishTank code: {code}. Expires in five minutes.'}).encode()
        request = urllib.request.Request(endpoint, payload, {'Content-Type': 'application/json',
                         'Authorization': 'Bearer ' + os.environ['SMS_GATEWAY_TOKEN']}, method='POST')
        with urllib.request.build_opener(NoRedirect()).open(request, timeout=15) as response:
            if response.status not in (200, 201, 202, 204):
                raise ValueError('Gateway rejected delivery')


def create_app():
    return Service(os.environ.get('FISHTANK_DATABASE', '/var/lib/fishtank-mfa/service.sqlite3'),
                   os.environ['FISHTANK_PEPPER'], origin=os.environ.get('FISHTANK_ORIGIN'))


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('command', choices=('add-device', 'revoke-device'))
    args = parser.parse_args()
    app = create_app()
    if args.command == 'add-device':
        token = app.add_device(input('Device name: ').strip(), input('Email (optional): ').strip(),
                               input('Phone in +country format (optional): ').strip())
        print('Device token (shown once; keep private):', token)
    else:
        name = input('Exact device name to revoke: ').strip()
        with app.db() as db:
            cursor = db.execute('UPDATE devices SET enabled=0 WHERE name=?', (name,))
            print('Revoked matching devices:', cursor.rowcount)
