# Optional live Defender alerts

In Simple or Advanced view, open **Monitoring → Start live Defender alerts**. This defaults off. FishTank subscribes to new Microsoft Defender Operational events and reads Defender protection status about once a minute. It does not change antivirus policy, scan every file itself or automatically delete files.

The history shows Defender detections, protection actions and failures, attack-surface-reduction and controlled-folder blocks/audits, and protection configuration changes. Audit events are not blocks; configuration changes are not proof of intrusion. Coverage warnings identify unavailable logs, missing/stale status, passive protection and interrupted monitoring. No events is not a clean-device verdict.

## Background operation

Enable **Keep running in tray when window closes** separately to continue while the window is hidden. The visible tray icon provides Show and Exit. Hiding locks app access and open vaults; restoring respects the configured app sign-in. Exit stops this listener. Windows Defender operates independently. FishTank does not install a background service or Windows startup entry: reopen and sign in after restart/logoff. Events while it is stopped may be absent; this is not a complete historical log reader.

Optional generic Windows notifications omit file paths. Windows notification settings or Focus Assist can suppress delivery. Notification throttling does not discard events from the bounded history.

## Privacy and limits

Up to 250 recent entries are stored locally using the existing Windows-account DPAPI protected store. Selected event details can contain paths and process names; advanced details and exports should be treated as private. Export requires confirmation and writes a new plaintext JSON file. FishTank does not upload these events or read the detected file contents.

Coverage depends on Defender, its exclusions, policies, event-log access and the running FishTank process. Other antivirus products, encrypted/offline content and every possible threat are outside this listener's coverage. Real detections should be reviewed in Windows Security.

## Validation

Portable policy tests cover event classification, missing/stale/passive/off protection, bounded history/deduplication and safe XML detail parsing. Collector fixtures cover typed Defender status and unavailable data. Windows security/UI harnesses include opt-in lifecycle, history display and tray lock checks. They compile in the Linux workspace, but native subscriptions, real Windows events, notifications, tray interaction and visual appearance require Windows runtime verification. No malicious samples were used.
