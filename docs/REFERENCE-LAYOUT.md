# Reference dashboard layout

The first layout pass replaces the large Dashboard banner with a modular two-column protection overview and a compact evidence panel. Device checks, monitoring status and scan text come from existing application state. Missing evidence stays unassessed; no VPN connection, speed, premium offer, fake timer or artificial security score is introduced.

Customize FishTank offers **Use violet dashboard style** as an explicit choice. This changes presentation while retaining the user's font, text scale, density and motion choices. Existing saved appearance files are not overwritten. Security actions, policy, collectors, credentials and app access are unchanged. Responsive refinement of the remaining dashboard cards is follow-up work.

The protection overview now exposes Scan schedule and Finding details directly. Existing activity timeline, preview/edit report export and Recovery center remain in the dashboard attention section. These reuse existing scheduling, evidence and recovery behavior; no protective action is performed merely by opening the dashboard.

Aquarium hosting support: the main Desktop window alone is marked when AQUARIUM_HOSTED=1, with compatible DPI mode selected before UI startup. Sign-in remains independent and is not marked. Standalone startup remains unchanged. Hosting does not grant access, change security preferences or bypass confirmations. Native security-dialog and mixed-DPI QA remain required.
