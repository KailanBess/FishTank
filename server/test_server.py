import io
import json
import os
import tempfile
import unittest
from pathlib import Path
from server import Service, Rejected


class VerificationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(dir=os.environ.get('FISHTANK_TEST_TEMP'))
        self.now = 10000
        self.sent = []
        self.app = Service(Path(self.temp.name) / 'test.sqlite3', 'harmless-test-pepper-' * 3,
                           lambda *args: self.sent.append(args), lambda: self.now)
        self.token = self.app.add_device('fixture', 'fixture@example.invalid', '+15551234567')
        self.binding = self.app.request(self.token, '/v1/status', {})['binding']

    def tearDown(self):
        self.temp.cleanup()

    def start(self, purpose='signin', channel='email'):
        return self.app.request(self.token, '/v1/challenge',
                                {'channel': channel, 'purpose': purpose, 'binding': self.binding})

    def check(self, challenge, code, purpose='signin', token=None):
        return self.app.request(token or self.token, '/v1/check',
                                {'challenge': challenge['challenge'], 'code': code, 'purpose': purpose})

    def test_email_round_trip_and_replay(self):
        challenge = self.start()
        self.assertEqual(self.sent[0][:2], ('email', 'fixture@example.invalid'))
        self.assertTrue(self.check(challenge, self.sent[0][2])['approved'])
        with self.assertRaises(Rejected):
            self.check(challenge, self.sent[0][2])

    def test_sms_and_context_binding(self):
        challenge = self.start('enroll', 'sms')
        self.assertEqual(self.sent[0][:2], ('sms', '+15551234567'))
        with self.assertRaises(Rejected):
            self.check(challenge, self.sent[0][2])
        self.assertTrue(self.check(challenge, self.sent[0][2], 'enroll')['approved'])

    def test_expiration(self):
        challenge = self.start()
        self.now += 301
        with self.assertRaises(Rejected):
            self.check(challenge, self.sent[0][2])

    def test_five_wrong_attempts_invalidate(self):
        challenge = self.start()
        wrong = '11111111' if self.sent[0][2] != '11111111' else '22222222'
        for _ in range(5):
            with self.assertRaises(Rejected):
                self.check(challenge, wrong)
        with self.assertRaises(Rejected):
            self.check(challenge, self.sent[0][2])

    def test_resend_replaces_previous_code(self):
        old = self.start()
        self.now += 61
        new = self.start()
        with self.assertRaises(Rejected):
            self.check(old, self.sent[0][2])
        self.assertTrue(self.check(new, self.sent[1][2])['approved'])

    def test_send_rate_limits(self):
        self.start()
        with self.assertRaises(Rejected) as result:
            self.start()
        self.assertEqual(result.exception.status, 429)
        for _ in range(4):
            self.now += 61
            self.start()
        self.now += 61
        with self.assertRaises(Rejected):
            self.start()

    def test_wrong_device_and_revocation(self):
        challenge = self.start()
        other = self.app.add_device('other', 'other@example.invalid', '')
        with self.assertRaises(Rejected):
            self.check(challenge, self.sent[0][2], token=other)
        with self.app.db() as db:
            db.execute('UPDATE devices SET enabled=0 WHERE name=?', ('fixture',))
        with self.assertRaises(Rejected):
            self.check(challenge, self.sent[0][2])

    def test_contact_change_invalidates_binding(self):
        challenge = self.start()
        with self.app.db() as db:
            db.execute('UPDATE devices SET binding=?', ('changed-binding',))
        with self.assertRaises(Rejected):
            self.check(challenge, self.sent[0][2])

    def test_no_plaintext_code_or_token_stored(self):
        self.start()
        with self.app.db() as db:
            rows = [dict(row) for row in db.execute('SELECT * FROM challenges')]
            devices = [dict(row) for row in db.execute('SELECT * FROM devices')]
        self.assertNotIn(self.sent[0][2], json.dumps(rows))
        self.assertNotIn(self.token, json.dumps(devices))

    def test_delivery_failure_never_approves(self):
        def fail(*args):
            raise RuntimeError('fixture delivery unavailable')
        self.app.deliver = fail
        with self.assertRaises(Rejected) as result:
            self.start()
        self.assertEqual(result.exception.status, 503)
        with self.app.db() as db:
            self.assertEqual(db.execute('SELECT delivered FROM challenges').fetchone()[0], 0)

    def test_invalid_destination_and_client_cannot_redirect(self):
        with self.assertRaises(ValueError):
            self.app.add_device('bad', 'x\r\nBcc:bad@example.invalid', '')
        with self.assertRaises(ValueError):
            self.app.add_device('bad', '', 'not-a-number')
        self.app.request(self.token, '/v1/challenge',
                         {'channel': 'email', 'purpose': 'signin', 'binding': self.binding,
                          'to': 'attacker@example.invalid'})
        self.assertEqual(self.sent[0][:2], ('email', 'fixture@example.invalid'))

    def test_wsgi_rejects_browser_and_oversized_requests(self):
        for extra, expected in [({'HTTP_ORIGIN': 'https://example.invalid'}, 405),
                                ({'CONTENT_LENGTH': '5000'}, 413)]:
            environ = {'REQUEST_METHOD': 'POST', 'PATH_INFO': '/v1/status',
                       'CONTENT_TYPE': 'application/json', 'CONTENT_LENGTH': '2',
                       'HTTP_AUTHORIZATION': 'Bearer ' + self.token, 'wsgi.input': io.BytesIO(b'{}')}
            environ.update(extra)
            status = []
            self.app(environ, lambda code, headers: status.append(code))
            self.assertTrue(status[0].startswith(str(expected)))


if __name__ == '__main__':
    unittest.main()
