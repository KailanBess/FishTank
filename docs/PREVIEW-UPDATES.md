# Easy GitHub preview updates — 0.15.4

No signing certificate, password, GitHub login or private key is needed by users.

1. Install/extract version 0.15.4 once. Older builds cannot install the new unsigned preview format through their signed-only updater.
2. Open **Settings → GitHub updates → Use preview updates (no certificate)** and accept the unsigned-preview notice once for this profile.
3. Use **Check GitHub release**, **Download and verify**, then **Apply update** when a newer release is available. Apply is a separate confirmation and restarts FishTank after replacement.

Preview mode checks the fixed KailanBess/FishTank repository over HTTPS, GitHub release/asset identities and SHA-256 digests, component names, executable metadata, and advertised versions. Only published prereleases qualify for unsigned installation. Immediately before applying, FishTank rechecks the original GitHub release and refuses changed assets/digests, missing or revoked consent, downgrades, moved stages and tampered bytes. The helper verifies bytes again before/after replacement, retains rollback copies and reports incomplete recovery. Normal, portable and named-profile data/runtime configuration are retained.

**Preview releases are unsigned.** This mode trusts GitHub and this repository's publishing account; it does not establish a verified Windows publisher. A matching checksum is not a malware verdict. Stable signed updates retain their separate Authenticode/certificate-pin checks. Turning off preview updates blocks applying staged previews and returns to the Stable channel. Nothing installs merely from opening/checking the screen.

## Publishing

The main-branch CI workflow publishes a preview-vMajor.Minor.Build prerelease only after the Windows, browser and server/source jobs all pass. Publishing runs only for pushes to KailanBess/FishTank main; pull requests cannot publish. The publishing job downloads the same run's Windows artifact, verifies every packaged checksum and executable identity/version, and creates a draft containing both executables, a bootstrap ZIP and SHA256SUMS.txt. It compares GitHub's asset digests with local hashes before making the prerelease public. No signing secrets are needed. Existing published versions are never overwritten; another release requires an assembly-version bump.

Preview tags use the separate preview-v namespace so they do not collide with stable vMajor.Minor.Build tags or trigger the signing workflow. The stable signed-release workflow is unchanged. An unsigned preview does not qualify as a signed release.

## Validation

Expansion fixtures exercise opt-in, channels, draft/stable rejection, inert unsigned assemblies, isolated staging, repository/release/asset provenance, changed digests, revoked consent, rechecking and tamper rejection. Installer fixtures exercise both verification modes, failure rollback, locked executables, checksum corruption and profile restart arguments. OS signatures/restarts in those installer fixtures are mocked; no real installation or Windows trust store is changed. UI tests open the update screen without contacting GitHub and inspect preview readiness. Publishing preparation verifies the actual preview ZIP locally; GitHub publication is verified separately after CI.
