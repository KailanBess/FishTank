# FishTank 0.14 validation

Application source commit `1b337eac1489c68f5cb2a329db979d7f0dbbb717` passed [GitHub Actions run 41570751652](https://github.com/KailanBess/FishTank/actions/runs/41570751652). Windows, browser UI and server/source jobs succeeded on November 2, 2026 (America/Chicago).

| Check | Result |
| --- | --- |
| Logic/update policy | 132 passed |
| Browser/session/vault compatibility | 183 passed |
| Native Windows security/collectors | 101 passed, including the embedded hardware-collector binding |
| Existing collector fixtures | 28 passed |
| New coverage and protection-change fixtures | 33 passed |
| Isolated-profile resilience | 33 passed |
| Expansion regression suite | 65 passed, including 200 CSV fuzz inputs |
| Windows UI | 3,723 passed; security centers, both protection-preview forms and incident workspace captured |
| Browser UI | 22 passed |
| Update/rollback fixtures | 20 passed; signatures/restart mocked |
| Server OTP/WebAuthn/signed status | 26 passed |
| Credential-pattern scan and pinned server dependency audit | Passed |
| Repeated unsigned executable rebuilds | SHA-256 matched in the same CI workspace |
| Installer/uninstaller | Isolated file operations passed; vault/unrelated files retained |

New fixtures verify that collection modes do not invoke protection writes, command arguments are hashed, unavailable tasks/logs remain unknown, process-ID changes do not change stable endpoint identities, managed/passive-Defender conditions refuse local changes, audit-first and compatibility acknowledgement are required, stale previews and unknown rules are rejected, Block/Warn is not weakened to Audit, and failed Windows readback is not called success.

Native app tests cover transition validation, bounded/partial evidence, opt-in monitoring, change/gap/failure handling, newly observed persistence under partial coverage, incident workflow/operator labels, case-path validation and preserved-evidence checksum verification. Tests use disposable profiles and fixtures. No protection-changing command was applied to the owner's PC.

The desktop ZIP is rebuilt from the same app source. Final publishing changes add documentation/package artifacts, without changing the application code. Its SHA256SUMS.txt manifest is checked for every bundled file.

These checks do not certify real ASR/CFA attack prevention, physical firmware/driver security, administrator-managed policy compatibility or successful incident eradication. Actual enforcement and ordinary app/backup workflows require a disposable-VM test and separate acceptance. Firmware changes, real update sources, publisher signing, Narrator/accessibility and independent security review remain external validation. The download is an unsigned development preview; the existing optional server remains undeployed.
