"""Explicitly enrolled signed, bounded status summaries; no remote commands."""
import base64
import hashlib
import json
from datetime import datetime, timezone


class DeviceStatus:
    def __init__(self, service, reject):
        self.service, self.reject = service, reject
        with service.db() as db:
            db.executescript('''
              CREATE TABLE IF NOT EXISTS report_keys(owner TEXT PRIMARY KEY, modulus TEXT, exponent TEXT,
                fingerprint TEXT, sequence INTEGER DEFAULT 0, device_id TEXT, group_name TEXT);
              CREATE TABLE IF NOT EXISTS device_reports(owner TEXT PRIMARY KEY, report TEXT, received INTEGER);
            ''')

    def request(self, token, device, path, body):
        owner = device['token']
        if path == '/v1/devices/enroll':
            modulus, exponent = body.get('modulus', ''), body.get('exponent', '')
            self.public_key(modulus, exponent)
            self.service.request(token, '/v1/check', {'challenge': body.get('challenge'), 'code': body.get('code'), 'purpose': 'settings'})
            fingerprint = self.fingerprint(modulus, exponent)
            with self.service.db() as db:
                existing = db.execute('SELECT fingerprint FROM report_keys WHERE owner=?', (owner,)).fetchone()
                if existing and existing['fingerprint'] != fingerprint:
                    raise self.reject(409, 'Revoke the previous reporting key before enrolling a replacement')
                db.execute('INSERT OR IGNORE INTO report_keys(owner,modulus,exponent,fingerprint) VALUES(?,?,?,?)', (owner, modulus, exponent, fingerprint))
            return {'fingerprint': fingerprint, 'enrolled': True}
        if path == '/v1/devices/revoke':
            self.service.request(token, '/v1/check', {'challenge': body.get('challenge'), 'code': body.get('code'), 'purpose': 'settings'})
            with self.service.db() as db:
                db.execute('DELETE FROM report_keys WHERE owner=?', (owner,))
                db.execute('DELETE FROM device_reports WHERE owner=?', (owner,))
            return {'revoked': True}
        if path == '/v1/devices/publish':
            report = body.get('report')
            if not isinstance(report, dict) or len(json.dumps(report)) > 12000:
                raise self.reject(400, 'Invalid report')
            with self.service.db() as db:
                db.execute('BEGIN IMMEDIATE')
                key = db.execute('SELECT * FROM report_keys WHERE owner=?', (owner,)).fetchone()
                if key is None:
                    raise self.reject(403, 'Explicit reporting key enrollment required')
                try:
                    payload = base64.b64decode(report['Payload'], validate=True)
                    if len(payload) > 4096 or report['Modulus'] != key['modulus'] or report['Exponent'] != key['exponent'] or report['Fingerprint'] != key['fingerprint']:
                        raise ValueError('Key mismatch or report too large')
                    from cryptography.hazmat.primitives import hashes
                    from cryptography.hazmat.primitives.asymmetric import padding
                    self.public_key(key['modulus'], key['exponent']).verify(base64.b64decode(report['Signature'], validate=True), payload, padding.PKCS1v15(), hashes.SHA256())
                    summary = json.loads(payload)
                    expected = {'Version', 'Type', 'DeviceId', 'RecordedUtc', 'AppVersion', 'Protection', 'Sequence'}
                    if set(summary) != expected or summary['Version'] != 1 or summary['Type'] != 'FishTank.HealthReport' or type(summary['Sequence']) is not int or summary['Sequence'] <= key['sequence']:
                        raise ValueError('Wrong report type or replay')
                    import re
                    if not isinstance(summary['DeviceId'], str) or not re.fullmatch('[a-f0-9]{32}', summary['DeviceId']) or (key['device_id'] and summary['DeviceId'] != key['device_id']):
                        raise ValueError('Device identity changed')
                    for field, limit in [('Protection', 1000), ('AppVersion', 100)]:
                        if not isinstance(summary[field], str) or len(summary[field]) > limit:
                            raise ValueError('Invalid summary field')
                    stamp = datetime.fromisoformat(summary['RecordedUtc'].replace('Z', '+00:00'))
                    if stamp.tzinfo is None or abs(stamp.timestamp() - self.service.clock()) > 600:
                        raise ValueError('Report is stale or future-dated')
                except Exception:
                    raise self.reject(403, 'Report signature, context or freshness invalid')
                db.execute('UPDATE report_keys SET sequence=?,device_id=? WHERE owner=?', (summary['Sequence'], summary['DeviceId'], owner))
                db.execute('INSERT OR REPLACE INTO device_reports VALUES(?,?,?)', (owner, json.dumps(report), int(self.service.clock())))
            return {'stored': True, 'sequence': summary['Sequence']}
        if path == '/v1/devices/list':
            with self.service.db() as db:
                key = db.execute('SELECT group_name FROM report_keys WHERE owner=?', (owner,)).fetchone()
                # Groups can only be assigned by the server administrator, never from a client request.
                rows = db.execute('SELECT r.report,r.received FROM device_reports r JOIN report_keys k ON r.owner=k.owner JOIN devices d ON d.token=k.owner WHERE d.enabled=1 AND (k.owner=? OR (? IS NOT NULL AND k.group_name=?)) LIMIT 10',
                                  (owner, key['group_name'] if key else None, key['group_name'] if key else None)).fetchall()
            return {'reports': [{'report': json.loads(r['report']), 'received': r['received']} for r in rows]}
        raise self.reject(404, 'Unknown device status endpoint')

    @staticmethod
    def fingerprint(modulus, exponent):
        return hashlib.sha256((modulus + ':' + exponent).encode('ascii')).hexdigest().upper()

    @staticmethod
    def public_key(modulus, exponent):
        from cryptography.hazmat.primitives.asymmetric.rsa import RSAPublicNumbers
        if not isinstance(modulus, str) or not isinstance(exponent, str) or len(modulus) > 1024 or len(exponent) > 12:
            raise ValueError('Invalid reporting key')
        n, e = int.from_bytes(base64.b64decode(modulus, validate=True), 'big'), int.from_bytes(base64.b64decode(exponent, validate=True), 'big')
        if n.bit_length() < 2048 or n.bit_length() > 4096 or e != 65537:
            raise ValueError('Use a 2048–4096 bit RSA reporting key with exponent 65537')
        return RSAPublicNumbers(e, n).public_key()
