# FishTank 0.14 wider security coverage

Open **Settings → All tools** or **Ctrl+K**. All eight requested areas are included. Opening a center displays saved observations; collect a fresh check explicitly. Optional persistence/network monitoring starts off. No Windows protection setting is changed merely by installing or opening this build.

| Area | Included capability | Action boundary |
| --- | --- | --- |
| Ransomware protection center | Controlled Folder Access preference, configured additional folders/app exceptions, recent blocked/audited writes, backup/restore readiness link | CFA Audit/Block changes use an expiring preview and explicit authorization. No folder/app exception is silently added. Audit does not block writes. |
| Attack-surface reduction manager | Configured ASR rules/exclusions, recent audit/block events, eight explained rule choices and audit-first setup | Change one known rule at a time. Block requires current Audit plus an explicit compatibility-review acknowledgement. Existing Block/Warn is not reduced to Audit. Unknown rules remain review-only. |
| Application-control review | Smart App Control configured state, code-integrity runtime, Code Integrity/AppLocker events, selected-file publisher inspection | No boot/app-control policy is rewritten. Selected files are never executed. Windows signature verification can consult certificate trust/revocation services. |
| Hardware-security review | Secure Boot, TPM readiness, VBS configured/running observations, memory integrity and explicit driver-blocklist preference, Code Integrity events | Missing firmware/driver/permission data is Unknown. Preferences do not establish effective enforcement. Use supported Windows/firmware tools after compatibility checks. |
| Software update inventory | Registry and current-user packaged-app versions; optional source-reported WinGet update candidates with timestamps | No `Win32_Product` repair query, automatic agreements or installer execution. The packaged WinGet executable must pass Windows signature validation with Microsoft publisher identity. This is not a CVE scanner or complete portable-app inventory. |
| Persistence monitoring | Startup commands, all enumerated services, task definitions and WMI event subscriptions; three recorded snapshots per category and comparisons | Command/script definitions are hashed. No startup entry, service, task or subscription is deleted or disabled. First capture is an inventory, not an accepted trusted baseline. |
| Network exposure monitor | TCP/UDP endpoints, effective firewall rules/filter hashes, Remote Desktop and remote-management configuration changes | Process IDs are excluded from stable identities. No remote probes, automatic firewall changes or claims of Internet reachability. Legitimate updates and transient listeners need human review. |
| Incident response workspace | Local cases, notes, timestamped timeline, selected preserved observations/activity, file metadata/hash, operator checklist and editable export preview | No automatic adapter disconnection, process termination, file copying/execution, account changes or uploads. Operator reports are explicitly unverified; hashes are not a forensic chain-of-custody certification. |

## Protection-change workflow

1. Collect current Ransomware or ASR evidence and review the event/coverage limitations.
2. Open **Preview an audit-first protection change**. Choose CFA or one of the eight catalogued ASR rules, then request a fresh preview. It shows the previous/requested mode and can be exported as a text reference.
3. Local changes require a Windows 10/11 client build of at least 19041, active Microsoft Defender with real-time protection, administrator execution, and configured FishTank app access/offline MFA. This build compatibility check does not determine Windows servicing/lifecycle support. CFA setup also checks cloud-delivered protection. Domain/enrollment/policy-managed settings and unknown prerequisites are refused.
4. Confirm the single change and authenticate with the app password and fresh MFA/recovery factor. The preview is bound to this profile and expires after ten minutes; Windows prerequisites/current preference are checked again. The pending request is consumed before the Windows command, so an interruption never silently replays it.
5. Windows preference readback must match the request. An accepted preference is not an attack test or proof of complete protection. Local change history distinguishes attempts from verified readback and unknown/failure outcomes.
6. Before changing Audit to Block, review compatibility and audit events yourself. FishTank does not automatically weaken Block/Warn or disable protection for recovery. Keep the preview's previous numeric mode and use Windows Security or the administrator's supported management process if compatibility needs correction.

ASR choices cover Office child processes, Office-created executables, credential-process access, script-downloaded executables, vulnerable-driver abuse, Office process injection, email-delivered executable content and WMI subscription persistence. These choices are a curated subset; other configured rule IDs are visible without being editable here.

**Test in a disposable Windows VM before applying changes to an important PC.** CFA blocking may also deny legitimate unsigned software, including this unsigned FishTank preview, access to protected folders. Audit and verify normal backups/workflows before enforcement. No FishTank executable or arbitrary application is silently allowlisted.

## Collection and monitoring limits

Observations are local and stored with Windows DPAPI. Up to three bounded snapshots per category are retained. Caps include 500 services/startup entries/tasks, 100 WMI objects per inspected class, 500 endpoints per protocol, 500 effective firewall rules, 400 uninstall records per view and 200 packaged applications. Limits, conflicting identities, access failures and disabled/unavailable event logs remain Unknown. Event queries cover the latest 100 matching records over seven days; no events is not proof of no incident.

Persistence/network monitoring is opt-in, runs only while the app is open, and uses a shared interval of 5–1440 minutes (30 by default). It defers while foreground work/dialogs are active. Changes trigger review notices, not automatic containment. Coverage-gap reminders are throttled; collection failure is recorded as unknown and is not retried every second. Missing entries under partial coverage are not called removals. Existing notification, snooze and quiet-hour preferences apply.

Software update queries contact already-configured WinGet sources only after confirmation. Source consent, unavailable tooling, signature failures, timeouts and unsuccessful queries remain unknown. Vendor updates and Windows Update remain under user control. Recorded available-version output can become stale.

## Incident data and recovery

Create/select a case, save private notes deliberately, and choose individual observations to preserve. Preserved JSON is checksummed and protected locally. File preservation records metadata/hash only (128 MiB hashing limit); it does not preserve the original bytes. Limits: 20 local cases, 200 timeline entries and ten evidence items per case, 256 KiB per evidence item, 3 MiB per case. Closed cases remain retained and can be reopened; closure does not certify eradication.

The response checklist guides isolation review, offline recovery-copy verification, credential rotation on a trusted device and recovery/monitoring review. Opening network settings does not isolate the machine; confirm any actual disconnection in Windows, considering remote-support access first. Operator confirmations are not independently verified. Do not rotate credentials on a device you still suspect is compromised.

Exports are readable text containing the selected evidence/notes and potentially usernames or paths. Review and redact before saving/sharing. Editing an export does not modify protected originals and can invalidate embedded evidence hashes. No report is sent automatically. The workspace is an investigation aid, not professional forensic acquisition or a guarantee that malware has been removed.

## Primary references and external acceptance

- [Microsoft ASR rule reference](https://learn.microsoft.com/en-us/defender-endpoint/attack-surface-reduction-rules-reference)
- [Microsoft Controlled Folder Access configuration](https://learn.microsoft.com/en-us/defender-endpoint/controlled-folder-access-configure)
- [Microsoft Smart App Control overview](https://learn.microsoft.com/windows/apps/develop/smart-app-control/overview)
- [Microsoft memory-integrity configuration and compatibility](https://learn.microsoft.com/en-us/windows/security/hardware-security/enable-virtualization-based-protection-of-code-integrity)
- [Microsoft WinGet upgrade command](https://learn.microsoft.com/en-us/windows/package-manager/winget/upgrade)

Tests mock protection-changing commands; they do not enable blocking on the owner's PC or certify real attack prevention. Effective ASR/CFA enforcement, managed-device policy behavior, real application compatibility, firmware/driver acceptance, actual update sources, incident-response practice, publisher signing and independent security/accessibility review still require separate validation. The existing server remains undeployed and is not required for these local tools.
