import base64
import json
import os
import tempfile
import unittest
from datetime import datetime, timezone
from pathlib import Path
from cryptography.hazmat.primitives import hashes
from cryptography.hazmat.primitives.asymmetric import rsa, padding
from device_status import DeviceStatus
from server import Service, Rejected


def b64(value): return base64.b64encode(value).decode()


class StatusTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(dir=os.environ.get('FISHTANK_TEST_TEMP'))
        self.now, self.sent = 10000, []
        self.app = Service(Path(self.temp.name) / 'db', 'fixture-pepper-' * 4, lambda *a: self.sent.append(a), lambda: self.now)
        self.token = self.app.add_device('fixture', 'a@example.invalid', '')
        self.binding = self.app.request(self.token, '/v1/status', {})['binding']
        self.key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
        public = self.key.public_key().public_numbers()
        self.modulus, self.exponent = b64(public.n.to_bytes(256, 'big')), b64(public.e.to_bytes(3, 'big'))

    def tearDown(self): self.temp.cleanup()

    def grant(self):
        self.now += 61
        challenge = self.app.request(self.token, '/v1/challenge', {'channel': 'email', 'purpose': 'settings', 'binding': self.binding})
        return {'challenge': challenge['challenge'], 'code': self.sent[-1][2]}

    def enroll(self):
        return self.app.request(self.token, '/v1/devices/enroll', dict(modulus=self.modulus, exponent=self.exponent, **self.grant()))

    def report(self, sequence=1, extra=None, recorded=None):
        summary = {'Version': 1, 'Type': 'FishTank.HealthReport', 'DeviceId': 'a' * 32, 'RecordedUtc': datetime.fromtimestamp(recorded or self.now, timezone.utc).isoformat(), 'AppVersion': '0.12.0.0', 'Protection': 'Unknown', 'Sequence': sequence}
        if extra: summary.update(extra)
        payload = json.dumps(summary).encode()
        return {'Payload': b64(payload), 'Signature': b64(self.key.sign(payload, padding.PKCS1v15(), hashes.SHA256())), 'Modulus': self.modulus, 'Exponent': self.exponent, 'Fingerprint': DeviceStatus.fingerprint(self.modulus, self.exponent)}

    def publish(self, report): return self.app.request(self.token, '/v1/devices/publish', {'report': report})

    def test_enrollment_requires_step_up_and_signature_replay_is_rejected(self):
        with self.assertRaises(Rejected): self.app.request(self.token, '/v1/devices/enroll', {'modulus': self.modulus, 'exponent': self.exponent})
        self.enroll(); report = self.report()
        self.assertTrue(self.publish(report)['stored'])
        with self.assertRaises(Rejected): self.publish(report)
        self.assertTrue(self.publish(self.report(2))['stored'])

    def test_modified_payload_signature_and_freshness(self):
        self.enroll()
        report = self.report(); report['Payload'] = b64(b'{}')
        with self.assertRaises(Rejected): self.publish(report)
        report = self.report(); report['Signature'] = b64(bytes(256))
        with self.assertRaises(Rejected): self.publish(report)
        with self.assertRaises(Rejected): self.publish(self.report(recorded=self.now - 601))
        with self.assertRaises(Rejected): self.publish(self.report(extra={'Type': 'Remote.Command'}))
        with self.assertRaises(Rejected): self.publish(self.report(extra={'Path': 'private fixture'}))

    def test_peers_require_admin_group_and_revocation_removes_summary(self):
        self.enroll(); self.publish(self.report())
        other = self.app.add_device('other', 'b@example.invalid', '')
        self.assertEqual(self.app.request(other, '/v1/devices/list', {})['reports'], [])
        with self.app.db() as db:
            db.execute('INSERT INTO report_keys(owner,group_name) VALUES(?,?)', (self.app.token_hash(other), 'fixture-group'))
            db.execute('UPDATE report_keys SET group_name=? WHERE owner=?', ('fixture-group', self.app.token_hash(self.token)))
        self.assertEqual(len(self.app.request(other, '/v1/devices/list', {})['reports']), 1)
        self.app.request(self.token, '/v1/devices/revoke', self.grant())
        self.assertEqual(self.app.request(other, '/v1/devices/list', {})['reports'], [])
        with self.assertRaises(Rejected): self.publish(self.report(2))

    def test_stale_or_unknown_keys_cannot_publish(self):
        with self.assertRaises(Rejected): self.publish(self.report())
        self.enroll()
        report = self.report(); report['Modulus'] = b64(bytes(256))
        with self.assertRaises(Rejected): self.publish(report)


if __name__ == '__main__': unittest.main()
