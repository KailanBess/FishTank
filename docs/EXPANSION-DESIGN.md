# Expansion design

The requested expansion covers all sixteen areas in the chat. Features are implemented through a searchable Tools hub, keeping the primary navigation stable. Automatic backup, external-drive prompts, remote reporting and OS integration start off. Existing authentication and vault master passphrases remain independent.

Security boundaries: vault history and attachments stay inside the authenticated encrypted envelope; import validates and previews before one atomic mutation. Backups copy ciphertext and distinguish a byte-for-byte copy from a passphrase-verified restore. Remote status uses the existing HTTPS server/device authorization, explicit device sharing and signed reports; it never exposes vault contents or arbitrary remote commands. Passkeys/security keys are WebAuthn credentials scoped to the configured server origin, with user verification and one-use challenges. They do not become vault decryption keys. Existing offline authenticator/recovery access remains available.

External requirements are deployment access to the existing server, real authenticator/key enrollment, a signing certificate for trusted distribution and separate hardware/assistive-technology acceptance. These must not be reported as completed merely because code or tests exist.

Implementation batches: local tools/notifications/privacy; backups/queue/device prompts; vault history/attachments/import/undo; profiles/layouts/recovery/help; server passkeys/status/signatures; packaging/developer checks; full regression and deliverables.
