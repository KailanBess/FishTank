# FishTank 0.12 app expansion

Open **Settings → All tools**, or press **Ctrl+K**. Favorites and recent tools are local to the current profile. Optional automatic actions start off.

| Area | Available controls | Scope and limits |
| --- | --- | --- |
| Finding tools | Keyword search, favorites, recent tools, current error messages | Opening a tool does not enable its automatic actions. |
| Scanning | Persistent sequential quick/full/custom queue, detailed saved profiles, result comparison, external-drive offers, exclusions/policy review | Runs while open. Interrupted jobs are retained and never silently restarted. Comparisons show scope and recorded result, not an invented threat delta. Defender owns remediation policy. |
| Protection | Existing evidence states, signature age/policy, explanations and restart evidence | Unknown evidence remains unknown. Read Windows Security for native engine status. |
| Password vault | Five prior passwords per entry, optional expiry, duplicate-password review, five encrypted attachments per entry, CSV/JSON import preview, plaintext JSON export preview, one-step undo | Attachments ≤256 KiB each; total vault ≤16 MiB. History/attachments stay under the master passphrase. Undo ends when the vault locks. Exports are readable secrets. |
| Account access | Local authentication history, server passkeys/security keys, server key events, revocation, existing recovery-code regeneration | Passkeys need the undeployed HTTPS service, a real authenticator and explicit enrollment. Existing app password and offline MFA remain. Vault master passphrase is separate. |
| Backups | Opt-in periodic ciphertext copies, destination/free-space check, source/copy hash comparison, bounded retention, configurable restore-drill reminders | Only unchanged, recorded copies in the exact selected destination are removed. A byte copy is not a successful passphrase-authenticated restore. Drills need you to enter the passphrase; plaintext is never restored unattended. |
| Recovery | Existing isolated restore verification and checklist, managed-copy comparison, recovery USB folder | USB folder contains instructions and available encrypted vault bytes; it is not bootable Windows media. Keep passphrases separately and verify copies. |
| Notifications | Encrypted center, read/delete/open actions, deduplication, snooze, quiet hours, minimum popup severity | Suppressed popups remain in history. Windows can independently suppress notifications. |
| Privacy | Windows permissions review, notification retention, preview/selective deletion of old audit days, opt-in automatic old-audit deletion, authentication-history clearing, editable diagnostics preview | Today's audit and vaults/backups are excluded. Existing exported reports and external backup copies are outside local deletion controls. |
| Devices | Separate named profiles, profile inventory, signed health-summary export and verification, optional manual remote publishing | Reporting key is protected by Windows DPAPI. Remote service enforces enrolled key, sequence and freshness; peers need administrator grouping and independently verified fingerprints. No remote commands. |
| Customization | Saved layouts, undo layout, compact mode, appearance import/export preview, Ctrl+Alt shortcut editor | Appearance imports remove wallpaper paths. Shortcuts open tools rather than execute security operations. |
| Accessibility | Large-controls preset, text scale, strong focus, existing keyboard/screen-reader labels, text-based evidence and comparisons | Dialog controls have accessible names and text status. Real Narrator, keyboard-only and high-DPI acceptance remain human checks. |
| Support | Searchable offline help, troubleshooting instructions, editable bug-report drafts and diagnostic preview | Nothing is sent automatically. Review saved reports before sharing. |
| Reliability | Scan operation journal with process identity, interrupted-queue recovery, atomic vault/import writes, low-space checks, encrypted-vault profile migration | Migration does not transfer Windows-bound MFA/tokens. A stopped app is not proof a Windows scan stopped. Free space can change after checks; write failures still preserve originals. |
| Development | Pinned server lockfile, dependency audit, source credential scan, dependency SBOM, CSV fuzz cases, repeated deterministic executable-build check | SBOM covers pinned server and direct desktop packages. Repeated build comparison excludes signatures/ZIP timestamps and does not prove every environment produces identical bytes. No independent audit is claimed. |
| Distribution | Per-user install/uninstall scripts, signed-script release preparation, Stable/Preview channel choice, deferred staging window, existing verified update replacement/rollback | Current preview is unsigned. Signed installation requires your independently verified publisher pin and a valid timestamped release. Preview releases use numeric tags and the same trust gates. No unattended install. |

## Vault compatibility

Vault payload version 3 adds history, expiry and attachments. Versions 1 and 2 still open. Once saved by this build, older FishTank versions cannot read that version-3 payload. Keep a verified encrypted backup before editing an existing vault. Atomic writes retain a previous `.bak`; encrypted backups can retain old/deleted secrets. Locking drops application references to nested history/attachments and undo; .NET strings cannot be guaranteed erased from all managed-memory copies.

CSV headers: `Title,Password` are required; `Username,Website,Notes` are optional. CSV quoting supports embedded commas/newlines. Import rejects malformed rows and broken email links, remaps IDs and persists the complete change atomically. JSON import/export retains relationships, history and attachments. Export never overwrites an existing destination.

## Installer use

Extract the complete ZIP into a new folder. For a development-only per-user installation, run `powershell.exe -NoProfile -File .\Install-FishTank.ps1 -AllowUnsignedPreview`. This explicit switch permits the unsigned preview; do not use it to bypass a signed production release's trust failure. Normal signed installation requires `-PublisherCertificateSha256` with a fingerprint verified separately. No execution-policy changes or administrator rights are requested by the installer.

It installs to `%LOCALAPPDATA%\Programs\FishTank`, refuses an existing installation, records owned files and registers an uninstall command. Use in-app verified updates for upgrades. `Uninstall-FishTank.ps1` removes recorded program files and owned integration entries, preserving profiles/vaults/backups and unrelated files. If installed files were edited, it stops for manual review. The `-NoRegistration` / `-FilesOnly` options support file-only operation and isolated installer tests.

## Server and external gates

See [SERVER-EXPANSION.md](SERVER-EXPANSION.md). The owner confirmed the service has not yet been deployed. Live HTTPS routing, delivery, Windows Hello/security-key enrollment, peer-device testing, publisher certificate/secrets, real signed installation and independent accessibility/security acceptance remain external requirements. Local fixtures do not satisfy those gates.
