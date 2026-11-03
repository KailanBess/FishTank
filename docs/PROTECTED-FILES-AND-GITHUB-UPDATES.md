# FishTank 0.8 preview: protected files and GitHub updates

## What outside-app protection means

**Protected Files → Open protected files** creates `.ftsafe` containers with a separate passphrase. Both file contents and internal relative filenames are encrypted. Explorer can still see the container's name and size, but cannot list its contents without the passphrase. The app password, Windows account password, and verification server do not reset or substitute for this passphrase.

This does not automatically encrypt your whole PC, hide system files, protect all copies of a document, or block an intruder controlling the same unlocked Windows account. Malware can record keystrokes and read unlocked memory or permitted plaintext exports. Windows administrators can change the installed app. Use a standard Windows account, current Windows/Defender protection, strong sign-in, screen locking and BitLocker/device encryption. Full-drive encryption protects stolen storage while it is locked; it does not stop malware after sign-in.

### Choose the action deliberately

- **Create encrypted copies:** ordinary originals remain readable. No source files are removed.
- **Encrypt and move personal files/folder files:** you select and confirm the scope. FishTank opens sources exclusively, rejects links/hardlinks and system/account/app-data paths, creates the encrypted container, flushes and verifies it, creates and verifies a recovery copy, and only then requests deletion of those selected originals by their existing file handles. Failures leave originals or report which originals remain. Folder directories are not removed.
- **Unlock container:** verifies authentication and all file hashes before showing internal names; content stays in memory until an explicit export. The passphrase is never stored as a saved key. Use a unique long random passphrase and keep it privately; there is no reset.
- **Export permitted plaintext:** explicitly writes all container files into a new output folder. Existing output folders/files are not overwritten. Windows output-folder ACLs restrict ordinary other users, and exported files get an internet-origin marker so normal Windows warnings can apply. No exported file is automatically opened. Locking cannot revoke these permitted outside-app copies. Interrupted exports may leave partial plaintext output; the app reports that limitation.
- **Lock now:** clears the owned decrypted buffer and file-name grid. Open containers also lock after five minutes without interaction and on Windows lock/disconnect, independently of whether an app password exists. An app lock locks the container too. An unlock finishing after a lock is discarded rather than reopening the session.

Limits per container: **256 files, 64 MiB total primary content**, at most 4,096 enumerated folder entries. Local ordinary files only. Copies preserve primary file bytes, not NTFS streams, ACLs, original timestamps, sparse/compression metadata, or every filesystem attribute. Moves refuse files with alternate streams—including an internet-origin marker—rather than silently destroy those streams. For those files, use encrypted copies or Windows filesystem/drive encryption. Unsupported permissions, filesystem features and incomplete reads cause refusal; no verification bypass is offered.

The recovery copy is beside the selected container initially; put a copy on separate protected storage before relying on it against drive failure. Deleting an original is **not secure erasure**. Old versions, cloud sync, backups, thumbnails, temporary files, filesystem metadata and deleted-sector remnants may remain. Use storage encryption and review existing copies. The app does not wipe drives or bulk-encrypt unrelated files. A strong passphrase protects a locked copied container even on another Windows account, but malicious software on an unlocked machine remains a threat.

### Format and review

`FTSAFE01` uses random 128-bit salt/IV, PBKDF2-HMAC-SHA256 (600,000 iterations), independent 256-bit encryption/authentication keys, AES-256-CBC with PKCS#7 padding and encrypt-then-HMAC-SHA256 over the complete header/ciphertext. Authentication is checked before decrypting/parsing. The encrypted binary payload has bounded counts, names, lengths and per-file SHA-256 checks. Unsafe relative paths, Windows device names and alternate-stream paths are refused during import and export.

The current implementation buffers bounded decrypted content while unlocked; memory use can exceed the 64 MiB content size. Owned buffers are cleared on lock/disposal, but .NET managed strings and runtime/provider buffers cannot be guaranteed erased. This format and its native file-handling paths have not had an independent security audit. Do not treat this preview as a substitute for an established audited encrypted-drive product for critical data.

## Updates from GitHub

Open **Security Center → Security and integrity tools → Downloads and updates**:

1. **Check GitHub for updates** queries the fixed `KailanBess/FishTank` repository's public stable release API. Drafts and prereleases are excluded. A source commit or preview ZIP on `main` is not an installable release.
2. **Download and verify GitHub update** downloads both `FishTank.exe` and `FishTank.Cli.exe`. Each must have valid GitHub asset identity/size/SHA-256 metadata, the expected assembly identity and the advertised newer version. Signed mode additionally requires a valid Windows Authenticode signature matching your publisher pin. Explicitly enabled Preview mode accepts unsigned published prereleases with GitHub provenance checks; see PREVIEW-UPDATES.md. Downgrades below the installed/staged version floor are refused. No access token or app credential is sent. GitHub redirects are limited to HTTPS on GitHub's known asset hosts.
3. **Install verified GitHub update** is a separate confirmed action. It refuses while tracked scans/vault/file operations run, locks open vaults, starts an embedded helper under the same Windows user, and closes FishTank. The helper rechecks paths, hashes, signatures, versions and current installed metadata; prepares replacement copies; waits for the original process; replaces only the two executables; verifies the result; retains old executable backups; records the result; and restarts the signed desktop app on success. Existing private data and runtime configuration are not replaced. Portable mode is preserved.
4. **Last GitHub installation result** shows pending, updated, failed, or recovery-required status. Failed verification cannot produce a success record or restart. Caught replacement failures attempt rollback in reverse order; the old backups are retained. This is not a crash/power-loss-atomic transaction. If rollback or power-loss recovery is incomplete, use the retained executable backups before running again.

Installation needs write permission to the current app directory on a filesystem supporting atomic replacement; no administrator elevation or execution-policy bypass is requested automatically. Close other instances first. An installation into protected Program Files may need a deliberate administrator-assisted update. Drive-root/Windows-system targets and linked paths are refused. Staged files are reverified immediately before installation, not assumed trusted merely because a prior download passed.

Optional startup checking is opt-in in **Locking and reminders** and only checks metadata once while the app is open. It does not download, install or restart automatically. Checks contact `api.github.com`; asset downloads use `github.com`, `release-assets.githubusercontent.com`, or `objects.githubusercontent.com`. Private repositories and authenticated private releases are not supported; no GitHub token is embedded in the app. Certificate verification can contact Windows trust/revocation services.

### Activation still requires a real signed release

The development preview is unsigned. No publisher certificate/private key is supplied or invented. Unsigned previews cannot pass the signed-update gate, and an encountered certificate is never automatically trusted. Configure the publisher pin from an authenticated publisher source. A first signed update must have a higher assembly version than the installed preview (for example, 0.10.1.0 after the 0.10.0 preview).

`.github/workflows/release.yml` prepares signed release publication after tests on a stable version tag or manual dispatch. In GitHub **Settings → Environments → release**, configure these securely:

- Secret `WINDOWS_SIGNING_PFX_BASE64`: your trusted code-signing certificate and private key in PFX form, base64 encoded.
- Secret `WINDOWS_SIGNING_PASSWORD`: its password.
- Variable `WINDOWS_TIMESTAMP_URL`: the certificate provider's supported HTTPS RFC 3161 timestamp endpoint.

Do not paste keys/passwords into chat or commit them. Use release-environment approvals and protected version tags. The signing script uses an isolated temporary certificate store, verifies signatures and timestamps, and removes its imported material; it does not remove certificates from your existing personal store. Releases publish only after signing succeeds, with both executable assets and a **public** certificate fingerprint. Existing published releases are not silently overwritten. The release tag must match both executable assembly versions.

Without those prerequisites the workflow refuses publication and the app refuses installation. No signed release has been published or installed by this development task. The custom direct-HTTPS staging option remains available separately; it is not a bypass for GitHub installation checks.

## Validation and pending work

Linux-executed checks: 100 logic/security checks, 10 collector fixture checks, 10 update-helper fixture checks, and 17 existing backend tests. They cover encrypted round trips, wrong-passphrase rejection, header/ciphertext/MAC tampering, name/content absence from encrypted bytes, refusal to overwrite exports/containers, lock behavior in the core, path/device-name guards, GitHub repository/digest/redirect policy, existing security logic, collector fixtures, and update-helper replacement/rollback in isolated temporary directories. The helper tests mock signature trust and restart; they do not prove real Windows signature validation or a live signed installation. PowerShell resource/build scripts parse, and desktop/CLI plus Windows harnesses compile with zero warnings/errors. Workflow actions are pinned to verified official v4 commit SHAs.

Windows harnesses include native exclusive-handle moves, recovery verification, refusal to delete an original when protection fails, app-directory protection, locked name visibility, UI screenshots, and prior authentication/backup tests. They compile here but cannot execute in this Linux workspace. Windows runtime/DPI/accessibility checks, real signed GitHub downloads/installation, sleep/resume/disconnect cases, disk-full/crash recovery, and independent cryptographic/native-filesystem review remain necessary.

### Direct update screen (0.15.3)

Use **Settings → GitHub updates** for the visible Check, Download and verify, and Apply update sequence. Opening the screen performs only local readiness checks. Apply remains disabled until the selected verification mode is configured and a complete checked update is available. Preview mode needs one-time unsigned consent; signed mode needs publisher trust. This also appears in All tools as GitHub updates. The older Downloads and updates tab remains available for file/publisher inspection and update results.

### Certificate-free previews (0.15.4)

Settings → GitHub updates → Use preview updates enables the optional unsigned path. See PREVIEW-UPDATES.md for setup, verification and publishing. No certificate is required for this mode. Signed-update requirements remain separate.
