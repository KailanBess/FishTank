"""Optional WebAuthn ceremonies for the existing authenticated device service."""
import hashlib
import json
import re
import secrets
from urllib.parse import urlsplit


class Passkeys:
    def __init__(self, service, origin, reject):
        self.service, self.reject = service, reject
        parsed = urlsplit(origin)
        if parsed.scheme != 'https' or not parsed.hostname or parsed.username or parsed.password or parsed.query or parsed.fragment or parsed.path not in ('', '/'):
            raise ValueError('FISHTANK_ORIGIN must be an HTTPS origin')
        self.origin, self.rp = origin.rstrip('/'), parsed.hostname
        with service.db() as db:
            db.executescript('''
              CREATE TABLE IF NOT EXISTS passkeys(id TEXT PRIMARY KEY, owner TEXT, binding TEXT,
                public_key BLOB, counter INTEGER, created INTEGER);
              CREATE TABLE IF NOT EXISTS key_flows(id TEXT PRIMARY KEY, ticket TEXT UNIQUE,
                owner TEXT, binding TEXT, mode TEXT, purpose TEXT, challenge BLOB,
                expires INTEGER, state TEXT, consumed INTEGER DEFAULT 0);
              CREATE TABLE IF NOT EXISTS key_events(owner TEXT, kind TEXT, recorded INTEGER);
            ''')

    def native(self, token, device, path, body):
        owner, now = device['token'], int(self.service.clock())
        if path == '/v1/passkeys/list':
            with self.service.db() as db:
                keys = db.execute('SELECT id,created FROM passkeys WHERE owner=? AND binding=?', (owner, device['binding'])).fetchall()
                events = db.execute('SELECT kind,recorded FROM key_events WHERE owner=? ORDER BY recorded DESC LIMIT 50', (owner,)).fetchall()
            return {'keys': [dict(k) for k in keys], 'events': [dict(e) for e in events]}
        if path == '/v1/passkeys/start':
            from webauthn import generate_registration_options, generate_authentication_options
            from webauthn.helpers.structs import AuthenticatorSelectionCriteria, PublicKeyCredentialDescriptor, UserVerificationRequirement, ResidentKeyRequirement
            mode, purpose = body.get('mode'), body.get('purpose')
            if mode not in ('register', 'authenticate') or purpose not in ('signin', 'settings'):
                raise self.reject(400, 'Invalid ceremony context')
            if body.get('binding') != device['binding']:
                raise self.reject(409, 'Device binding changed')
            if mode == 'register':
                # Existing server-verified settings OTP is consumed once; a stolen bearer alone cannot enroll.
                self.service.request(token, '/v1/check', {'challenge': body.get('challenge'), 'code': body.get('code'), 'purpose': 'settings'})
            with self.service.db() as db:
                db.execute('BEGIN IMMEDIATE')
                db.execute('DELETE FROM key_flows WHERE expires<?', (now - 300,))
                count = db.execute('SELECT COUNT(*) FROM key_flows WHERE owner=? AND expires>?', (owner, now)).fetchone()[0]
                keys = db.execute('SELECT id FROM passkeys WHERE owner=? AND binding=?', (owner, device['binding'])).fetchall()
                if count >= 5 or (mode == 'register' and len(keys) >= 10):
                    raise self.reject(429, 'Too many ceremonies or registered keys')
                if mode == 'authenticate' and not keys:
                    raise self.reject(409, 'Enroll a passkey first')
                descriptors = [PublicKeyCredentialDescriptor(id=decode(k['id'])) for k in keys]
                if mode == 'register':
                    options = generate_registration_options(rp_id=self.rp, rp_name='FishTank', user_name='FishTank device', user_id=bytes.fromhex(device['binding']), exclude_credentials=descriptors,
                        authenticator_selection=AuthenticatorSelectionCriteria(user_verification=UserVerificationRequirement.REQUIRED, resident_key=ResidentKeyRequirement.PREFERRED))
                else:
                    options = generate_authentication_options(rp_id=self.rp, allow_credentials=descriptors, user_verification=UserVerificationRequirement.REQUIRED)
                flow, ticket = secrets.token_urlsafe(32), secrets.token_urlsafe(32)
                db.execute('INSERT INTO key_flows(id,ticket,owner,binding,mode,purpose,challenge,expires,state) VALUES(?,?,?,?,?,?,?,?,?)',
                           (flow, digest(ticket), owner, device['binding'], mode, purpose, options.challenge, now + 180, 'pending'))
            return {'flow': flow, 'url': self.origin + '/passkeys#' + ticket, 'binding': device['binding'], 'purpose': purpose}
        if path in ('/v1/passkeys/poll', '/v1/passkeys/consume'):
            with self.service.db() as db:
                db.execute('BEGIN IMMEDIATE')
                row = db.execute('SELECT * FROM key_flows WHERE id=? AND owner=?', (body.get('flow'), owner)).fetchone()
                if row is None or row['expires'] <= now or row['binding'] != device['binding'] or row['purpose'] != body.get('purpose') or row['consumed']:
                    raise self.reject(403, 'Ceremony expired, mismatched or already consumed')
                if path.endswith('/consume'):
                    if row['state'] != 'approved' or row['mode'] != 'authenticate':
                        raise self.reject(403, 'No authentication was approved')
                    db.execute('UPDATE key_flows SET consumed=1 WHERE id=?', (row['id'],))
                return {'state': row['state'], 'mode': row['mode'], 'purpose': row['purpose'], 'binding': row['binding']}
        if path == '/v1/passkeys/remove':
            self.service.request(token, '/v1/check', {'challenge': body.get('challenge'), 'code': body.get('code'), 'purpose': 'settings'})
            with self.service.db() as db:
                db.execute('DELETE FROM passkeys WHERE id=? AND owner=?', (body.get('id'), owner))
                db.execute('UPDATE key_flows SET state="revoked" WHERE owner=?', (owner,))
                self.event(db, owner, 'Passkey revoked')
            return {'removed': True}
        raise self.reject(404, 'Unknown passkey endpoint')

    def event(self, db, owner, kind):
        db.execute('INSERT INTO key_events VALUES(?,?,?)', (owner, kind, int(self.service.clock())))
        db.execute('DELETE FROM key_events WHERE owner=? AND rowid NOT IN (SELECT rowid FROM key_events WHERE owner=? ORDER BY rowid DESC LIMIT 100)', (owner, owner))

    def browser(self, path, body):
        from webauthn import generate_registration_options, generate_authentication_options, options_to_json, verify_registration_response, verify_authentication_response
        from webauthn.helpers.structs import AuthenticatorSelectionCriteria, PublicKeyCredentialDescriptor, UserVerificationRequirement, ResidentKeyRequirement
        ticket = body.get('ticket', '')
        if not isinstance(ticket, str) or not re.fullmatch(r'[A-Za-z0-9_-]{43}', ticket):
            raise self.reject(403, 'Invalid ceremony ticket')
        now = int(self.service.clock())
        with self.service.db() as db:
            db.execute('BEGIN IMMEDIATE')
            row = db.execute('SELECT f.* FROM key_flows f JOIN devices d ON f.owner=d.token AND f.binding=d.binding WHERE ticket=? AND d.enabled=1', (digest(ticket),)).fetchone()
            if row is None or row['expires'] <= now or row['state'] != 'pending':
                raise self.reject(403, 'Ceremony unavailable or already completed')
            keys = db.execute('SELECT * FROM passkeys WHERE owner=? AND binding=?', (row['owner'], row['binding'])).fetchall()
            if path == '/passkeys/options':
                descriptors = [PublicKeyCredentialDescriptor(id=decode(k['id'])) for k in keys]
                if row['mode'] == 'register':
                    options = generate_registration_options(rp_id=self.rp, rp_name='FishTank', user_name='FishTank device', user_id=bytes.fromhex(row['binding']), challenge=row['challenge'], exclude_credentials=descriptors,
                        authenticator_selection=AuthenticatorSelectionCriteria(user_verification=UserVerificationRequirement.REQUIRED, resident_key=ResidentKeyRequirement.PREFERRED))
                else:
                    options = generate_authentication_options(rp_id=self.rp, challenge=row['challenge'], allow_credentials=descriptors, user_verification=UserVerificationRequirement.REQUIRED)
                return {'mode': row['mode'], 'purpose': row['purpose'], 'publicKey': json.loads(options_to_json(options))}
            if path != '/passkeys/finish':
                raise self.reject(404, 'Unknown ceremony endpoint')
            credential = body.get('credential')
            try:
                client = json.loads(decode(credential['response']['clientDataJSON']))
                if client.get('crossOrigin', False) is not False or client.get('topOrigin'):
                    raise ValueError('Cross-origin ceremonies are not supported')
                if row['mode'] == 'register':
                    if len(keys) >= 10:
                        raise ValueError('Key limit reached')
                    verified = verify_registration_response(credential=credential, expected_challenge=row['challenge'], expected_rp_id=self.rp, expected_origin=self.origin, require_user_verification=True)
                    db.execute('INSERT INTO passkeys VALUES(?,?,?,?,?,?)', (encode(verified.credential_id), row['owner'], row['binding'], verified.credential_public_key, verified.sign_count, now))
                else:
                    key = next((k for k in keys if isinstance(credential, dict) and k['id'] == credential.get('id')), None)
                    if key is None:
                        raise ValueError('Unknown credential')
                    # User handles, when supplied, must identify this exact device binding.
                    handle = credential.get('response', {}).get('userHandle')
                    if handle and decode(handle) != bytes.fromhex(row['binding']):
                        raise ValueError('Wrong user handle')
                    verified = verify_authentication_response(credential=credential, expected_challenge=row['challenge'], expected_rp_id=self.rp, expected_origin=self.origin, credential_public_key=key['public_key'], credential_current_sign_count=key['counter'], require_user_verification=True)
                    db.execute('UPDATE passkeys SET counter=? WHERE id=?', (verified.new_sign_count, key['id']))
            except Exception:
                # Commit failure state before returning; one failed finish burns this ticket.
                db.execute('UPDATE key_flows SET state="failed" WHERE id=?', (row['id'],))
                self.event(db, row['owner'], 'Passkey verification failed')
                return {'approved': False}
            db.execute('UPDATE key_flows SET state="approved" WHERE id=?', (row['id'],))
            self.event(db, row['owner'], 'Passkey registered' if row['mode'] == 'register' else 'Passkey authentication approved')
            return {'approved': True}


def digest(value):
    return hashlib.sha256(value.encode()).hexdigest()


def encode(value):
    import base64
    return base64.urlsafe_b64encode(value).decode().rstrip('=')


def decode(value):
    import base64
    return base64.b64decode(value + '=' * (-len(value) % 4), altchars=b'-_', validate=True)
