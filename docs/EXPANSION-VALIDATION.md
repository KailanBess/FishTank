# FishTank 0.12 validation

The application source at commit 5c2552a98de99e0d496e24719bcbde143f4c04ac passed [GitHub Actions run 41558562241](https://github.com/KailanBess/FishTank/actions/runs/41558562241). All Windows, browser UI and server/source jobs completed successfully on November 2, 2026 (local time).

| Check | Result |
| --- | --- |
| Logic and update-policy checks | 132 passed |
| Browser/session/vault compatibility | 183 passed |
| Native Windows security and collectors | 100 passed |
| Isolated-profile resilience | 33 passed |
| Workspace/vault/backup/report expansion | 23 passed, including 200 CSV fuzz inputs |
| Windows UI | 3,672 passed, with screenshots |
| Browser UI | 22 passed |
| Collector fixtures | 28 passed |
| Update/rollback helper fixtures | 20 passed; signatures/restart mocked |
| OTP, WebAuthn and signed-status server tests | 26 passed |
| Pinned server dependency audit | No known vulnerabilities reported in this run |
| Source credential-pattern scan | Passed; values are never printed on findings |
| Dependency SBOM | 11 pinned/direct dependency records |
| Repeated unsigned executable builds | SHA-256 matched across two rebuilds in the same CI workspace |
| Installer/uninstaller | File operations passed in an isolated directory; sample vault and unrelated files retained |

The download packages are rebuilt from the same application source. Final packaging changes add documentation, release-orchestration gates and Linux line-ending rules. Each ZIP includes SHA256SUMS.txt; every entry was checked against that manifest. ZIP/signing timestamps are outside the repeated executable-build claim.

The service has not been deployed on the owner's existing server. Live HTTPS routing, email/modem delivery, Windows Hello/security-key enrollment, multiple physical devices, publisher certificate/secrets, trusted signed installation, real Narrator acceptance, outside usability feedback and an independent security audit remain pending. This is an unsigned development preview, not a protection certification.
