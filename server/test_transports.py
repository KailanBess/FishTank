import os
import subprocess
import unittest
from unittest.mock import patch
from server import delivery


class LocalTransportTests(unittest.TestCase):
    def test_local_mail_uses_only_fixed_loopback_without_login_or_shell(self):
        with patch.dict(os.environ, {'EMAIL_TRANSPORT': 'postfix', 'SMTP_FROM': 'security@example.invalid', 'SMTP_HOST': 'attacker.example.invalid'}, clear=True), patch('server.smtplib.SMTP') as smtp, patch('server.subprocess.run') as run:
            delivery('email', 'owner@example.invalid', '12345678')
            smtp.assert_called_once_with('127.0.0.1', 25, timeout=15)
            session = smtp.return_value.__enter__.return_value
            session.login.assert_not_called()
            message = session.send_message.call_args.args[0]
            self.assertEqual(message['To'], 'owner@example.invalid')
            self.assertIn('12345678', message.get_content())
            run.assert_not_called()

    def test_local_modem_queues_code_over_stdin(self):
        with patch.dict(os.environ, {'SMS_TRANSPORT': 'gammu'}, clear=True), patch('server.subprocess.run') as run:
            delivery('sms', '+15551234567', '12345678')
            args, kwargs = run.call_args
            self.assertEqual(args[0], ['/usr/bin/gammu-smsd-inject', '-c', '/etc/gammu-smsdrc', 'TEXT', '+15551234567'])
            self.assertNotIn('12345678', str(args))
            self.assertIn(b'12345678', kwargs['input'])
            self.assertFalse(kwargs.get('shell', False))

    def test_bad_destinations_and_header_injection_are_rejected_before_execution(self):
        with patch.dict(os.environ, {'EMAIL_TRANSPORT': 'postfix', 'SMS_TRANSPORT': 'gammu', 'SMTP_FROM': 'security@example.invalid'}, clear=True), patch('server.subprocess.run') as run:
            for channel, recipient, code in [('email', 'x@example.invalid\nBcc: y@example.invalid', '12345678'), ('sms', '--help', '12345678'), ('email', 'x@example.invalid', '123\n5678'), ('other', '+15551234567', '12345678')]:
                with self.assertRaises(ValueError):
                    delivery(channel, recipient, code)
            run.assert_not_called()

    def test_missing_binary_and_queue_failures_are_not_success(self):
        for error in [FileNotFoundError(), subprocess.CalledProcessError(1, 'fixture'), subprocess.TimeoutExpired('fixture', 15)]:
            with patch.dict(os.environ, {'SMS_TRANSPORT': 'gammu'}, clear=True), patch('server.subprocess.run', side_effect=error):
                with self.assertRaises(type(error)):
                    delivery('sms', '+15551234567', '12345678')

    def test_unknown_transports_do_not_fall_back(self):
        with patch.dict(os.environ, {'EMAIL_TRANSPORT': 'other', 'SMS_TRANSPORT': 'other', 'SMTP_FROM': 'security@example.invalid'}, clear=True), patch('server.subprocess.run') as run:
            for channel, recipient in [('email', 'owner@example.invalid'), ('sms', '+15551234567')]:
                with self.assertRaises(ValueError):
                    delivery(channel, recipient, '12345678')
            run.assert_not_called()


if __name__ == '__main__':
    unittest.main()
