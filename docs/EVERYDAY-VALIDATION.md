# FishTank 0.13 validation

The application source at commit 8dc4bc6fa2614a838d1c3969ac359adbe1783ff0 passed [GitHub Actions run 41561519367](https://github.com/KailanBess/FishTank/actions/runs/41561519367). Windows, browser UI and server/source jobs all succeeded on November 2, 2026 (America/Chicago).

| Check | Result |
| --- | --- |
| Logic/update policy | 132 passed |
| Browser/session/vault compatibility | 183 passed |
| Native Windows security/collectors | 100 passed |
| Isolated-profile resilience | 33 passed |
| Expansion regression suite | 47 passed, including 200 CSV fuzz inputs |
| Windows UI | 3,693 passed; captures include all four server-wizard tabs and the sample demo |
| Browser UI | 22 passed |
| Collector fixtures | 28 passed |
| Update/rollback fixtures | 20 passed; signatures and restart mocked |
| Server OTP/WebAuthn/signed status | 26 passed |
| Source credential scan and server dependency audit | Passed |
| Repeated unsigned executable rebuilds | SHA-256 matched within the CI workspace |
| Installer/uninstaller | Isolated file operations passed; vault/unrelated files retained |

New regression cases cover secret-free vault-health findings, repeating/expired/reused passwords, simulated demo actions, selective cleanup and changed-file rejection, portable encrypted-vault authentication and overwrite refusal, disconnected destination alerts and reconnection, failed-copy throttling, release-note state and TLS troubleshooting. Tests use generated fixture profiles and sample credentials; they do not import the owner's real vault or contact the owner's server.

The desktop ZIP is rebuilt from the same application source and includes a SHA256SUMS.txt manifest. Final publishing changes add documentation and the ZIP, without changing app code. The existing server kit remains compatible; its protocol and service source were not changed for this client wizard.

Live server deployment/routing, actual email/SMS delivery, physical security-key and multi-PC acceptance, publisher signing, real Narrator testing and an independent security audit remain external checks. This is an unsigned development preview.
