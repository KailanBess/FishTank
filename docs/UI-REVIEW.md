# Simple and Advanced update

This records the initial 0.6.1 UI work. The current preview also includes the [0.7.0 security workspace and its validation requirements](SECURITY-AND-INTEGRITY.md).

Simple view is the default: Dashboard, Antivirus, Security Center, Breach Checks, Passwords, and Settings. Switch views on Dashboard or Settings. Advanced view adds monitoring, network inventory, system repairs, manual quarantine, and Tailscale devices. Detailed scan and diagnostic controls appear in Advanced view. Changing views does not change protection, credentials, monitoring, or Windows policy. It is a presentation preference, not an authorization boundary.

New profiles default to simple explanations. Existing saved explanation preferences remain respected. Essential warnings and original technical details remain available.

The Security Center now includes a local setup checklist (app password, authenticator/sign-in setting, remaining recovery codes, and enrolled email/SMS channels), a shortcut to Windows Security, and SHA-256 download comparison. The checklist never contacts the verification server and does not claim to test delivery or malware protection. Download comparison does not run or upload the selected file; checksums must come from a trusted publisher. Hash equality is not a safety verdict. Windows network paths, alternate data streams, and reparse paths are refused.

## Sidebar and layout fixes

- Sidebar buttons use one fill for normal and hover states, recalculated whenever selection changes. A text color and border indicate the current page. Keyboard focus remains visible.
- Custom buttons clear their background before painting rounded corners and reset stale hover state when hidden or disabled.
- Sidebar widths and row heights scale with text size, and all page names have accessible labels and tooltips.
- Scan actions wrap using the shared action-row layout instead of a fixed-height panel. Page blocks fit the available width instead of forcing a 650-pixel minimum.
- Advanced dashboard cards are omitted in Simple view so links do not point to hidden pages.

## Verification and limits

In the Linux development workspace, the desktop, CLI, and Windows UI harness compiled with zero warnings/errors. The checksum/navigation suite passed 26 checks. The existing 17 backend tests passed with simulated delivery.

Windows execution and visual inspection remain required. The Windows UI harness exercises every visible page in Simple/Advanced modes at 100%/150% app text scale with Light/Dark themes at a 1000x740 window. It checks navigation colors, accessible names, page width, and captures screenshots. The GitHub workflow runs it on Windows and saves screenshots and unsigned builds as artifacts. A successful workflow does not replace human inspection of those screenshots or screen-reader testing.

Run locally on Windows with .NET 8 SDK and .NET Framework 4.8:

```powershell
dotnet run --project tests/Logic/Logic.csproj
dotnet build tests/WindowsUi/WindowsUi.csproj -c Release
& ./tests/WindowsUi/bin/Release/net48/WindowsUi.exe "$PWD/ui-artifacts"
```

Before replacing an installed build, inspect screenshots and test pointer hover, keyboard focus, navigation while work runs, theme preview/save/cancel, real Windows DPI settings (100%, 125%, 150%, 200%), smaller screens, and dialogs with long text. App text scale does not substitute for actual Windows DPI testing. Test app/vault sign-in, recovery, backups, scans, and real server delivery separately. No independent security audit or guarantee of zero graphical defects is claimed.

## Browser dashboard, 0.10.0

The new local browser dashboard was exercised in Chromium with the actual loopback server and vault implementation, using fixture Windows status/collectors. Its 22 UI checks passed. Screenshots at desktop, tablet, 390px and 320px widths, plus enlarged text, were captured; desktop overview, mobile account grouping and narrow account-editor screenshots were inspected. Long service-name overflow found during testing was fixed. Separate server/session/vault checks (183 passing) cover authentication, encryption, legacy migration and conflict protection.

Run `dotnet build tests/Browser/Browser.csproj`, then `python3 tests/browser-ui.py` with Playwright/Chromium installed. `--dotnet`, `--chromium`, and `--output` select local tools/output; all credentials are disposable fixtures. GitHub Actions saves the browser screenshots separately. The Windows editor harness also covers the new account/connection controls. It compiles here; native Windows UI/runtime validation is still pending. See [the browser setup guide](BROWSER-AND-ACCOUNT-ORGANIZATION.md).
