# Live Defender checking (0.15)

Open **Monitoring → Start live Defender alerts**. Monitoring defaults off. FishTank observes Microsoft Defender status and selected Operational log records; Windows Defender performs protection and remediation. FishTank does not silently change antivirus policy or delete detected files.

## Delivery and recovery

FishTank subscribes to incoming records and reconciles the available Windows log every 10 seconds in batches of up to 500 matching events. A protected checkpoint records source record ID, event ID and source timestamp together with retained history. Restarting or reconnecting resumes after that anchor. Watcher/replay duplicates are suppressed. Out-of-order callbacks alone do not imply a reset.

Before trusting delivery, FishTank checks that the log is enabled/readable and the saved anchor still matches. An overwritten, missing or replaced anchor produces an explicit gap. Initial startup and reset recovery cover available matching records from the last 24 hours; older/deleted records cannot be reconstructed. Catch-up remains visibly incomplete until replay reaches its captured log head. A reset during a read prevents checkpoint advancement. Subscriptions retry after failure; log reconciliation continues independently.

Live and recovered historical records are labeled separately. The list uses source time; details also show receipt time. Recovery summaries are FishTank observations, not additional detections. Detections, successful/failed actions, suspicious behavior, ASR/CFA blocks/audits, engine failures, disabled components, configuration changes and tamper-blocked changes have distinct explanations. Audit/configuration events are not automatically treated as urgent malware alerts.

## Current protection status

Read-only Defender queries run about every 30 seconds with a 15-second process timeout. Freshness starts at query start, not completion. Protection-change events invalidate older observations; an in-flight result crossing a newer change is discarded. Previous-session callbacks/results are rejected. **Check now** requests an immediate cycle when no cycle is already running.

A subscription alone never establishes healthy delivery. Log validation older than 30 seconds is a coverage gap; protection observations older than 90 seconds or dated in the future become unknown. Unavailable, passive, disabled or incomplete readings are described explicitly. Service, behavior, download/network scanning, tamper protection and signature age add evidence; missing components are not silently presented as observed. Signature age over three days prompts review. These observations do not prove a clean device or detection of every threat.

## History and notifications

History retains up to 1,000 entries and a 4 MiB serialized checkpoint, trimming older entries as needed. Status/export diagnostics show retained/trimmed counts. Recent source identities remain deduplicated after history eviction within a bounded 10,000-identity cache. Large bursts can exceed these bounds; the Windows log remains the source for deeper investigation.

History/checkpoint save together in the Windows-account DPAPI store. Save failures remain visible and are retried; events arriving during a save remain pending. An unreadable/corrupt store is preserved, never replaced with empty defaults. Saving is blocked until storage is repaired and FishTank restarted. Back up the existing profile before repair.

Notifications use severity-aware grouping. Suppressed bursts produce a later count summary while individual source events remain in bounded history. Recovery produces a summary rather than one popup per old record. Generic notifications omit paths. Windows settings, Focus Assist, FishTank quiet hours or snoozing can suppress popups; review Monitoring and notice history. A summary is not an extra source detection.

## Background operation and privacy

Enable **Keep running in tray when window closes** separately to continue hidden. Hiding locks app access/open vaults; restoring respects sign-in. Exit stops FishTank monitoring. Defender continues independently. FishTank installs no service/startup entry: reopen/sign in after logoff/restart. Available missed records reconcile on resumption; uninterrupted monitoring while the process is stopped is impossible.

Selected metadata may include private file/process paths. Export writes a new plaintext JSON file with diagnostics and retained details after confirmation; existing files are not overwritten. Events are not uploaded and reported files are not opened by this listener.

Defender exclusions, OS policy, event retention/access, other antivirus products and offline/encrypted content limit coverage. FishTank cannot override these conditions or provide a measured detection rate. Review actual threats/failures in Windows Security. See [Microsoft's Defender event reference](https://learn.microsoft.com/en-us/defender-endpoint/troubleshoot-microsoft-defender-antivirus) and LIVE-VALIDATION.md.
