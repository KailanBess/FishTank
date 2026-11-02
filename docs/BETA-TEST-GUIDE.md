# FishTank outside-testing guide

Use a new extracted preview folder with `FishTank.exe --portable` and sample data. A disposable Windows VM is best for scans, quarantine and update testing. A separate Windows account is useful for UI/profile testing but does not isolate machine-wide Defender actions. Do not import your real passwords or use an important document as a recovery fixture.

## Uncoached tasks

Ask the tester to attempt these outcomes without telling them which buttons to press. Record where they hesitate before helping.

| Task | Expected result | Record |
| --- | --- | --- |
| Launch and postpone setup, then find it again. | The app opens; setup can be resumed from Settings / More tools or settings search. Optional automation remains off. | Found unaided? Time and confusing wording. |
| Make text easier to read and find keyboard help. | Appearance controls offer text size, motion and focus options; keyboard navigation works. | Window size, Windows scale, text scale, focus/clipping problems. |
| Review protection status. | Missing or unsupported evidence says it could not be checked; the app never equates an unknown check with safety. | Was the next action understandable? |
| Create a sample vault and an email/service connection. | Sample credentials are saved; lists do not reveal passwords by default. | Any lost input, confusing fields or unexpected visibility. |
| Lock and reopen the app and vault. | The app password and vault passphrase have distinct roles; access is denied for a wrong credential. | Did the tester understand which credential was required? |
| Make an encrypted sample backup and test restore in isolation. | The original active vault stays unchanged; the sample backup can be authenticated and reopened. | Any uncertainty about destination or freshness. |
| Find activity history and a support report. | Relevant events are searchable. The report can be edited before saving and does not upload itself. | Could the tester remove a selected line and save that exact text? |
| Use the local browser dashboard on a permitted test machine. | Pairing is single-use; account lists omit passwords; lock → immediately re-enter → unlock does not lose typed input. | Browser/OS, input loss, errors, narrow-screen layout. |
| Review Recovery center. | The tester can find quarantine, backup verification, installation results and recovery-code status. | Could they tell FishTank quarantine from Defender quarantine? |

## Failure and release acceptance in a disposable VM

- Disconnect the VM's network, request an update check, and confirm the error does not claim an update was checked successfully or installed. Reconnect and retry.
- With only harmless sample files, observe a quick scan and its elapsed/stage display. Request cancellation only if acceptable on that VM. A request or command exit alone must not be labeled a confirmed engine stop.
- Use the automated resilience suite for corrupt/busy settings, wrong/tampered vault data, clock rollback, partial restore writes and disposed browser requests. Do not corrupt a real profile manually.
- Use update-helper fixtures for staging tampering and locked-executable rollback. A real signed-install test still requires the maintainer certificate, a newer matching release tag, publisher-pin verification and a snapshot of the VM.
- Save a VM snapshot before real signed-install/interruption tests. Verify both executable versions/hashes, retained backups, restart behavior and current profile/vault integrity afterwards. Record every incomplete state; do not assume an interrupted update succeeded.

## Feedback template

App version / source commit:
Windows and browser versions:
Window size / Windows display scale / app text scale:
Task attempted:
Completed unaided, completed with help, or blocked:
Expected result:
Actual result:
Steps to reproduce:
Exact non-sensitive error text:
Screenshot filename (review and redact before sharing):
Severity: prevents use / loses input / confusing / cosmetic:
One thing you expected to find somewhere else:

Keep real passwords, tokens, recovery codes, email addresses, file paths and machine identifiers out of feedback. Preview and redact the support report before sharing it. No feedback is submitted automatically; the maintainer chooses whom to ask and where to collect responses.
