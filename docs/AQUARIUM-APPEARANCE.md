# Aquarium appearance

New profiles start with the Aquarium Ocean/Mint appearance. Existing appearance files continue to load their saved choices. In Customize FishTank, choose **Use Aquarium style**, preview, and **Save & close** to apply it to an existing profile. Cancel/Revert retains the normal preview recovery behavior.

Aquarium Ocean, Aquarium Midnight and Aquarium Slate backgrounds, plus Aquarium Mint, Aquarium Blue and Aquarium Coral accents, are available alongside every existing theme and accent. Rounded surfaces and a subtle gradient use the existing rendering system. Font, text scale, density, motion, card order and security preferences stay independent. All customization controls remain available. An explicitly applied family preset disables custom-palette overrides while retaining their saved color values; custom palettes can be enabled again afterward. Windows high contrast continues to override decorative colors.

FishTank is the Security member of Aquarium; FishBowl is Emulation. This styling change does not alter protection, credentials, scanning, server, networking or update behavior. Earlier image-search references guide the aquatic design; no fixed mockup images are bundled or claimed as a pixel-exact match.

Validation: the Windows UI regression suite exercises family-theme preview switching along with existing themes, Cancel/Revert and accessibility. Review cards, navigation, focus and readability at different text scales, then save/restart to verify persistence. No profile data is bundled.

## Implementation review — 0.15.6

Reviewed the merged Aquarium appearance change (`42f01dd`) against its parent source and the downloadable preview. The merge wired the preset, theme/accent choices and new-profile appearance into the app, but the repository ZIP still contained the earlier build. Version 0.15.6 rebuilds both executables and includes this guide in the download.

The review also corrected three inconsistencies: the Defaults button and unreadable-settings fallback now use the same Aquarium defaults as a new profile; Harmonized blending applies after family theme colors are chosen; and Windows high contrast suppresses decorative backgrounds/wallpapers as well as overriding the palette. Existing valid profiles and accessibility choices remain preserved. The preset can be previewed, cancelled, reverted or saved through the existing Customize flow.

Local validation: 93 portable appearance/settings/palette checks, 133 existing logic checks, 183 browser/vault checks, 30 collector fixtures, 33 coverage fixtures and 26 server tests passed. All four Windows test programs (UI, security, expansion and resilience) compile. Archive preparation (14 checks), mocked preview publishing (7 checks), and isolated install/uninstall file operations passed. The source credential-pattern scan also passed; it is not a security audit.

Native Windows execution and visual acceptance remain pending for this review. The updater fixture's Windows-style cleanup guard prevents a passing Linux run; no real update was installed. The portable appearance harness uses a serializer fixture and explicit high-contrast palette input, so it does not test native Windows settings detection or rendering. Added Windows UI coverage clicks the preset, checks that preview does not save, verifies Cancel, then saves and reloads the family style. GitHub API access returned Forbidden, so hosted Windows job outcomes could not be inspected here. The packaging build also reported unavailable NuGet vulnerability advisory data (NU1900); compilation succeeded, but this was not a dependency vulnerability check.
