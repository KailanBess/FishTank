import hashlib
import io
import json
import os
import secrets
import tempfile
import unittest
from pathlib import Path
import cbor2
from cryptography.hazmat.primitives import hashes
from cryptography.hazmat.primitives.asymmetric import ec
from server import Service, Rejected
from passkeys import encode, decode


class PasskeyTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(dir=os.environ.get('FISHTANK_TEST_TEMP'))
        self.now, self.sent = 10000, []
        self.app = Service(Path(self.temp.name) / 'db', 'fixture-pepper-' * 4, lambda *a: self.sent.append(a), lambda: self.now, 'https://tank.example.invalid')
        self.token = self.app.add_device('fixture', 'a@example.invalid', '')
        self.binding = self.app.request(self.token, '/v1/status', {})['binding']
        self.private = ec.generate_private_key(ec.SECP256R1())
        self.id = secrets.token_bytes(32)

    def tearDown(self):
        self.temp.cleanup()

    def start(self, register=False, purpose='signin'):
        body = {'mode': 'register' if register else 'authenticate', 'purpose': purpose, 'binding': self.binding}
        if register:
            self.now += 61
            challenge = self.app.request(self.token, '/v1/challenge', {'channel': 'email', 'purpose': 'settings', 'binding': self.binding})
            body.update(challenge=challenge['challenge'], code=self.sent[-1][2])
        flow = self.app.request(self.token, '/v1/passkeys/start', body)
        ticket = flow['url'].split('#')[1]
        options = self.app.passkeys.browser('/passkeys/options', {'ticket': ticket})['publicKey']
        return flow, ticket, options

    def credential(self, options, register=False, origin='https://tank.example.invalid', count=1, verified=True):
        client = json.dumps({'type': 'webauthn.create' if register else 'webauthn.get', 'challenge': options['challenge'], 'origin': origin, 'crossOrigin': False}).encode()
        auth = hashlib.sha256(b'tank.example.invalid').digest() + bytes([0x41 if register else 0x01 | (4 if verified else 0)]) + (0 if register else count).to_bytes(4, 'big')
        if register:
            auth = auth[:32] + bytes([0x45 if verified else 0x41]) + auth[33:]
            public = self.private.public_key().public_numbers()
            cose = cbor2.dumps({1: 2, 3: -7, -1: 1, -2: public.x.to_bytes(32, 'big'), -3: public.y.to_bytes(32, 'big')})
            auth += bytes(16) + len(self.id).to_bytes(2, 'big') + self.id + cose
            response = {'clientDataJSON': encode(client), 'attestationObject': encode(cbor2.dumps({'fmt': 'none', 'attStmt': {}, 'authData': auth}))}
        else:
            signature = self.private.sign(auth + hashlib.sha256(client).digest(), ec.ECDSA(hashes.SHA256()))
            response = {'clientDataJSON': encode(client), 'authenticatorData': encode(auth), 'signature': encode(signature), 'userHandle': encode(bytes.fromhex(self.binding))}
        return {'id': encode(self.id), 'rawId': encode(self.id), 'type': 'public-key', 'response': response, 'clientExtensionResults': {}}

    def enroll(self):
        flow, ticket, options = self.start(True, 'settings')
        result = self.app.passkeys.browser('/passkeys/finish', {'ticket': ticket, 'credential': self.credential(options, True)})
        self.assertTrue(result['approved'])
        return flow

    def test_real_cryptographic_registration_authentication_and_single_consume(self):
        self.enroll()
        flow, ticket, options = self.start()
        self.assertTrue(self.app.passkeys.browser('/passkeys/finish', {'ticket': ticket, 'credential': self.credential(options)})['approved'])
        result = self.app.request(self.token, '/v1/passkeys/consume', {'flow': flow['flow'], 'purpose': 'signin'})
        self.assertEqual(result['state'], 'approved')
        with self.assertRaises(Rejected):
            self.app.request(self.token, '/v1/passkeys/consume', {'flow': flow['flow'], 'purpose': 'signin'})
        with self.assertRaises(Rejected):
            self.app.passkeys.browser('/passkeys/finish', {'ticket': ticket, 'credential': self.credential(options)})

    def test_bearer_alone_cannot_enroll(self):
        with self.assertRaises(Rejected):
            self.app.request(self.token, '/v1/passkeys/start', {'mode': 'register', 'purpose': 'settings', 'binding': self.binding})

    def test_wrong_origin_signature_and_user_verification_are_rejected(self):
        self.enroll()
        for mutation in ('origin', 'signature', 'verification', 'challenge'):
            flow, ticket, options = self.start()
            credential = self.credential(options, origin='https://evil.example.invalid' if mutation == 'origin' else 'https://tank.example.invalid', verified=mutation != 'verification')
            if mutation == 'signature': credential['response']['signature'] = encode(bytes(70))
            if mutation == 'challenge':
                client = json.loads(decode(credential['response']['clientDataJSON'])); client['challenge'] = encode(bytes(32)); credential['response']['clientDataJSON'] = encode(json.dumps(client).encode())
            self.assertFalse(self.app.passkeys.browser('/passkeys/finish', {'ticket': ticket, 'credential': credential})['approved'])
            with self.assertRaises(Rejected): self.app.request(self.token, '/v1/passkeys/consume', {'flow': flow['flow'], 'purpose': 'signin'})

    def test_wrong_device_context_expiry_and_revocation(self):
        self.enroll()
        flow, ticket, options = self.start()
        other = self.app.add_device('other', 'b@example.invalid', '')
        with self.assertRaises(Rejected): self.app.request(other, '/v1/passkeys/poll', {'flow': flow['flow'], 'purpose': 'signin'})
        with self.assertRaises(Rejected): self.app.request(self.token, '/v1/passkeys/poll', {'flow': flow['flow'], 'purpose': 'settings'})
        self.now += 181
        with self.assertRaises(Rejected): self.app.passkeys.browser('/passkeys/options', {'ticket': ticket})
        flow, ticket, options = self.start()
        with self.app.db() as db: db.execute('UPDATE devices SET enabled=0')
        with self.assertRaises(Rejected): self.app.passkeys.browser('/passkeys/options', {'ticket': ticket})

    def test_wsgi_browser_origin_and_static_security_headers(self):
        responses = []
        self.app({'REQUEST_METHOD': 'GET', 'PATH_INFO': '/passkeys'}, lambda s, h: responses.append((s, dict(h))))
        self.assertIn("frame-ancestors 'none'", responses[-1][1]['Content-Security-Policy'])
        for origin in ('https://evil.example.invalid', ''):
            data = b'{"ticket":"fixture"}'
            self.app({'REQUEST_METHOD': 'POST', 'PATH_INFO': '/passkeys/options', 'HTTP_ORIGIN': origin, 'CONTENT_TYPE': 'application/json', 'CONTENT_LENGTH': str(len(data)), 'wsgi.input': io.BytesIO(data)}, lambda s, h: responses.append((s, h)))
            self.assertTrue(responses[-1][0].startswith('403'))


if __name__ == '__main__': unittest.main()
