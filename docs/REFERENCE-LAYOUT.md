# Reference dashboard layout

The first layout pass replaces the large Dashboard banner with a modular two-column protection overview and a compact evidence panel. Device checks, monitoring status and scan text come from existing application state. Missing evidence stays unassessed; no VPN connection, speed, premium offer, fake timer or artificial security score is introduced.

Customize FishTank offers **Use violet dashboard style** as an explicit choice. This changes presentation while retaining the user's font, text scale, density and motion choices. Existing saved appearance files are not overwritten. Security actions, policy, collectors, credentials and app access are unchanged. Responsive refinement of the remaining dashboard cards is follow-up work.

The protection overview now exposes Scan schedule and Finding details directly. Existing activity timeline, preview/edit report export and Recovery center remain in the dashboard attention section. These reuse existing scheduling, evidence and recovery behavior; no protective action is performed merely by opening the dashboard.

Aquarium hosting support: the main Desktop window alone is marked when AQUARIUM_HOSTED=1, with compatible DPI mode selected before UI startup. Sign-in remains independent and is not marked. Standalone startup remains unchanged. Hosting does not grant access, change security preferences or bypass confirmations. Native security-dialog and mixed-DPI QA remain required.

Reference correction: the dashboard now begins with a centered full-scan control and three evidence/recovery tiles. Long attention/checklist material follows the main dashboard instead of pushing it below the fold. New profiles use the violet reference preset; existing customization is retained unless the user applies the reference appearance. The hosting marker is published after the first Shown cycle to prevent early reparenting.

The default reference preset uses a plain violet background, restrained card corners, a centered full-scan power control, and three cards showing saved protection evidence, actual monitoring state and recovery actions. Existing font size, density and accessibility choices are retained when applying the preset. Run tests/ReferenceLayout to verify delayed workspace readiness and render an isolated dashboard without starting a scan.


The managed visibility bridge handles Aquarium show/hide commands on the WinForms UI thread. It restores child-control visibility after native parenting and refuses host show requests while the app is locked or parked. The isolated workspace fixture exercises the real Desktop with generated data and no scans.

