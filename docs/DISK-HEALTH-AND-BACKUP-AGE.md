# Disk health and backup-age warnings — 0.8.1 preview

Open **Security Center → Security and integrity tools → Disk health and free space**. The Dashboard shows saved disk warnings, unknown/outdated readings, and backup-age concerns for backup types you have used. Rendering the Dashboard does not run hardware commands.

Disk checks read Windows physical-disk health/operational status, available reliability counters and mounted fixed-volume capacity. They never repair a volume, delete files, change firmware, or request elevation automatically. Drivers, USB adapters, RAID, virtual disks and access restrictions can hide telemetry; missing or invalid readings remain **Unknown**. A hardware “healthy” report cannot predict every drive failure.

Review prompts use these general thresholds, not a vendor failure prediction:

- Reported physical health is warning/unhealthy, or Windows reports a recognized degraded/error state.
- Temperature at least 70°C, reported wear used at least 90%, or a nonzero historical uncorrected read/write error count.
- Free space below 10% **or** 2 GiB on a mounted fixed volume. Low space is not itself evidence of failing hardware; review usage carefully before deleting anything.

The full evidence includes values, limits and missing-data messages. Current warning states remain visible on repeated checks; an unchanged bad reading is not relabeled safe. Up to 64 disks and 64 mounted fixed volumes are inspected, with partial coverage explicitly unknown. Each completed report is saved in the existing encrypted security history. Automatic disk checks do not reset your general security-review reminder date.

## Backup age means copy age, not verification age

**Backup and recovery → Show backup age and verification dates** shows separate dates for app-created backup copies and successful verification. Exporting an encrypted password-vault backup records its creation date and encrypted-content fingerprint. Creating a protected-file recovery copy records its creation and successful verification. Records are protected with current-user DPAPI; up to 20 observations per backup type are retained, plus the store's previous encrypted version.

Verifying a selected password-vault backup records the fingerprint of the bytes actually authenticated/decrypted. It does **not** reset that copy's creation date. A successful isolated restore is recorded separately. An unrecognized/external backup or older installation's verification record can have an unknown creation age; filesystem timestamps are not used to invent a fresh backup date. Future dates require clock review. Unset dates remain unset across timezone conversion.

Warnings cover **Password vault** and **Protected files** separately. Automatic age notices apply to used backup types, rather than demanding a protected-file backup from someone who has never used that feature. Other Windows/third-party backups are not inventoried. Recorded age does not prove that a backup still exists, is on separate storage, contains every current change, or can recover an entire PC. Successful backup creation is not reported as failed merely because age tracking could not be saved; the UI explains that tracking remains unavailable.

## Preferences and defaults

In **Locking and reminders**:

- Disk checking is enabled by default, daily, while FishTank is open; choose hourly/daily/weekly or disable automatic checks. Failed attempts are throttled too. Manual checks remain available.
- Backup-age limit defaults to **7 days**, configurable from 1–365 days. A copy reaches the warning threshold at that age.
- Backup-age notifications use the existing opt-in reminders setting and its repeat interval (1–90 days). Dashboard/status findings remain available with reminders off. Windows desktop notifications remain separately opt-in and omit private paths.

No background service or Windows scheduled task is installed. Checks/reminders stop with the desktop app. Device telemetry may take time to read; unsupported checks remain unknown rather than triggering an automatic repair.

## Validation

In this Linux workspace, 110 logic/security checks and 23 PowerShell collector fixture checks pass. New cases cover creation versus verification age, exact age boundaries, missing/future/timezone-adjusted empty dates, healthy/unhealthy device reports, high wear/temperature, low/boundary free space and unavailable/invalid telemetry. Existing update-helper tests and backend tests are also rerun for the packaged preview.

Desktop/CLI and Windows harnesses compile. Windows-only tests cover real collector attempts, encrypted tracking round trips, and preserving old creation age after reverification. UI fixtures disable automatic hardware jobs to keep screenshot tests isolated. Those Windows-native/UI checks have not executed here; real hardware support, permissions, graphical layout and notification delivery still need Windows validation. Signing, live update publication and independent security review remain separate requirements.
