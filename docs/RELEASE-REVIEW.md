# FishTank 0.11.1 release-readiness review

Reviewed November 2, 2026. This is an implementation review and regression-test pass, not an independent security audit or a certification of protection.

## Findings fixed

| Finding | Change | Evidence |
| --- | --- | --- |
| Missing-file checks could hide unavailable security records; an unexpected directory at the app-access path could be treated as disabled access. | Read security records directly, default only on genuine file/directory absence, and require sign-in when app-access existence is unknown. Corrupt records remain unchanged. | Isolated-profile missing, busy, corrupt and invalid-access-record checks. |
| A queued browser vault request could execute after the backend was disposed. | Mark disposal under the same gate as vault operations and reject queued/new requests before they can reopen a vault. | Deterministic queued-create/dispose race test and revoked-session check. |
| Rolling the clock backwards could extend an unlocked vault's lifetime. | Treat backwards time as an auto-lock condition. | Vault clock-rollback regression check. |
| An interrupted quarantine restore could leave a partial destination, with plaintext buffers not cleared on every failure. | Write to a temporary sibling, flush, then publish without overwrite; clean temporary files and clear the plaintext buffer in a finally block. Cleanup failures explicitly identify the temporary file for recovery. | Injected write interruption, existing-destination preservation and complete-copy tests. |
| Interrupted or unreadable update results were easy to overlook. | Put incomplete/failed update recovery at the top of Dashboard attention items and link to installation/rollback results from Recovery center. | Pending and malformed result tests. |
| Browser passphrase input could be cleared while a preceding vault-lock request was still finishing. | Disable the master-passphrase fields until locking and its state refresh finish, so input cannot be accepted and then cleared by that response. | Existing hosted browser regression exercises immediate lock → re-entry → unlock. |
| A previous test fixture added a second backup type and collided with an unrelated Single() assertion. | Select the intended password-vault backup by kind and path. | Native security-suite fixture corrected; coverage is retained. |
| Windows PowerShell children could inherit PowerShell 7 module paths and fail to load their built-in signature/collector modules. | Set compatible built-in/machine module paths for the child process only; keep the user's environment and all trust checks unchanged. Include bounded verifier diagnostics on failure. | Reproduced the module-load error locally, then confirmed the real verifier reports the unsigned fixture as NotSigned. |
| The expanded embedded collector exceeded Windows' command-line length limit after Base64 encoding. | Send the trusted embedded resource over UTF-8 standard input with a short bootstrap command and the existing timeout. No execution-policy changes or temporary scripts are needed. | An actual protection-health collection completes and returns at least seven supported evidence states. |
| Streamed collector requests on hosted Windows could fall back to generic inventory output. | Bind the validated category inside the embedded resource and require exact category-specific health/live-protection output. | Hosted Windows now passes both category checks; generic inventory is not accepted as protection-health evidence. |
| Settings had accumulated setup, authentication, diagnostics and connection controls on one long page. | Group everyday settings into appearance/access and automation/connections; move occasional tasks into More tools. Preserve settings search and Recovery center access. | Windows UI checks and screenshots at 100/150% text scale in both themes. |

## Verification

- 132 logic checks passed locally.
- 33 new isolated-profile resilience checks passed locally, including a blocked loopback proxy that makes the real update check fail without contacting GitHub, an actual built-in signature-verifier check and category-specific protection-health/live-protection collections.
- 46 existing app-insight/polish checks are included in the native security suite and have a separate local fixture runner.
- 3,639 Windows UI checks passed locally after the Settings changes.
- 28 collector fixture checks and eight PowerShell resource/build-script syntax checks passed locally.
- Update-helper fixtures cover success, post-replacement verification failure, a locked second executable and tampered staging. Signatures/restart are mocked; these are not trusted signed-install acceptance tests.
- Hosted verification passed for code commit `05cc907e32281395bc726557a7c63e036cef3025`: [GitHub Actions run 41551861972](https://github.com/KailanBess/FishTank/actions/runs/41551861972). Both Windows and browser jobs are green. The run passed 132 logic, 183 browser/session/vault, 100 native Windows security, 33 resilience, 3,639 UI, 28 collector, 20 mocked update/rollback and 22 browser UI checks; all eight PowerShell scripts parsed. CLI build and unsigned packaging also passed.

The local sandbox cannot start an HTTP.sys URL listener and cannot complete the existing security suite's Windows ACL/private-profile operations. The hosted Windows job has now passed these native and HTTP checks; the local sandbox restriction remains. UI screenshots were inspected; physical display scaling and real screen-reader behavior remain manual acceptance items.

## Reproducible checks

Run `scripts/Run-ReleaseChecks.ps1` on a Windows test machine with .NET SDK 8 and .NET Framework 4.8. It runs the logic/browser, PowerShell, collector, isolated-profile, native security, UI and update/rollback suites and saves logs plus `release-checks.json` under the ignored `test-artifacts` directory. Failed commands stay failed. The summary separately lists manual/external release gates and never treats automated checks alone as a trusted-release approval.

New failure/recovery checks also run in the normal Windows CI and signed-release workflow. The executable preview is version 0.11.1.0. No existing Windows user profile, production vault, system protection policy, certificate trust store or real installed app was changed by the isolated fixtures.

## Remaining release gates

1. Configure a genuine code-signing certificate in GitHub's encrypted release environment, then test a timestamped signed installation and rollback on a disposable Windows VM. No signing credentials are available in this session; the preview remains unsigned.
2. Have another person follow BETA-TEST-GUIDE.md without coaching. Record task completion, confusion and reproducible bugs. No outside feedback has been collected or fabricated, and no messages were sent.
3. Commission an independent security review of authentication, vault/file encryption, quarantine restore, local browser access and the update boundary before relying on FishTank for important secrets.
4. Complete a separate Windows-account check, physical DPI/high-contrast/screen-reader acceptance, external authenticator use, and real email/SMS outage/recovery tests where those services are configured.
5. Test actual process/power interruption during a signed installation. Fixture-level rollback and an incomplete-update notice do not prove recovery from every filesystem or power-loss condition.
