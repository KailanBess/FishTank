# Setup, scan progress, recovery and signed releases

## Using the app

New profiles receive a first-run setup guide. Each action is explicit: run read-only protection checks, set an app password, enroll MFA, or choose optional automation/reminders. Finish records completion; Do this later (or closing the guide) records deferral. Settings → First-run setup reopens it. No security setting changes merely from opening or finishing the guide.

Quick, full and custom scans have a progress window with stage, elapsed time, result and cancellation evidence. Defender does not supply a reliable percentage/current file for these commands, so the activity indicator is indeterminate. Hide the window to continue browsing; Antivirus → View scan progress reopens it. Scheduled quick/full scans use the same operation model and can be viewed there.

Request Defender cancellation sends the documented `MpCmdRun -Scan -Cancel` command for quick/full scans. It can also affect active scans started elsewhere. Custom scans do not use this command. A zero cancel-command exit is not a confirmed engine stop. FishTank reads Defender events 1000 (start), 1001 (completion) and 1002 (stopped), matching the single observed scan ID in the request window. Multiple/missing starts, unreadable logs or missing terminal records leave the result unverified. The event reports what Defender observed; it does not prove which application caused a stop. Windows may continue scanning after FishTank exits.

Clicking a Windows notification restores FishTank and opens the relevant monitoring history, scan progress, recovery center, integrity review or updates page. App locking still requires sign-in before routing. Notifications omit file paths and threat details.

Dashboard and Settings expose Recovery center: FishTank quarantine/restores, backup verification and isolated restore tests, folder reminders, recovery-code status, Windows recovery options and support reports. Opening quarantine switches to Advanced view explicitly. FishTank and Defender manage separate quarantine stores. A quarantined file can still be dangerous; existing restore confirmations and destination checks remain in force.

Accessibility adds keyboard shortcuts (Alt+1 Dashboard, Alt+2 Antivirus, Ctrl+F settings search, Ctrl+Shift+R recovery, existing Ctrl+Shift+L app lock), accessible names/roles for scan status and actions, plus Windows High Contrast colors and button focus rendering. Changing Windows colors refreshes the main shell. Text scaling, reduced motion and existing stronger focus settings remain in Appearance. Keyboard focus and real assistive-technology use still benefit from manual testing.

## Signing and publishing

Settings → Signed release readiness explains installed signature inspection, publisher trust pins and the release configuration. Only an independently verified public certificate fingerprint belongs in app preferences.

The existing release workflow now checks the release tag against source versions and requires signing configuration before costly builds. It signs both executables, checks certificate validity/code-signing usage, verifies timestamped signatures, and packages a signed desktop/CLI ZIP with configs, the public certificate fingerprint and SHA256SUMS.txt. The release publishes both executables plus the bundle and checksum manifest.

In the GitHub `release` environment, configure encrypted secrets `WINDOWS_SIGNING_PFX_BASE64` and `WINDOWS_SIGNING_PASSWORD`, and variable `WINDOWS_TIMESTAMP_URL` for the certificate provider’s supported HTTPS RFC 3161 endpoint. Use a genuine code-signing certificate with its private key. Do not paste credentials into chat or commit them. The stable numeric tag must match both executable assembly versions. Source-only checks can run with `scripts/Test-ReleaseReadiness.ps1 -Tag v0.11.0.0`; add `-RequireSigningConfiguration` in the configured release environment.

No signing certificate was supplied for this change. The downloadable development preview remains unsigned; a trusted signed release cannot be manufactured from source code alone. Existing signed-update checks are not bypassed.

References: Microsoft’s [MpCmdRun command documentation](https://learn.microsoft.com/en-us/defender-endpoint/command-line-arguments-microsoft-defender-antivirus) and [Defender scan troubleshooting](https://learn.microsoft.com/en-us/defender-endpoint/troubleshoot-mdav-scan-issues).
