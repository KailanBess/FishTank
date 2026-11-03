# Live checking validation — 0.15

This release adds deterministic recovery/accuracy regression tests; it does not claim an antivirus detection percentage.

## Verified locally

- Desktop source compiles with .NET Framework reference assemblies.
- 135 Expansion assertions pass (70 new live reliability assertions plus the existing 65), including 200 existing CSV fuzz inputs.
- 132 portable policy assertions pass, covering missing/stale/passive/off protection, event interpretation, deduplication and safe XML parsing.
- 30 mocked collector assertions pass, including optional component flags, signature age/version, unavailable status and false/null values.
- 3,723 Windows UI assertions pass; all 12 embedded/build PowerShell scripts parse.

Live tests cover checkpoint restart recovery; watcher/replay deduplication; callback reordering; delivery failure/replacement; synchronous subscription failure; stopped/disposed callbacks; corrupt-store preservation; write failure/retry; events during persistence; reset recovery; rejected unexplained rewind; whole-batch prevalidation; incomplete backfill; old healthy results crossing protection-disable events; stop/restart races; failed queries; history count/byte caps; trim counters; external record mutation; expanded severity; missing/disabled components; stale signatures; notification burst summaries; query-start freshness and clock rollback.

Injected sources/readers/stores and explicit clocks avoid creating detections, changing Defender policy, installing malware samples or modifying the owner's profile. These tests do not measure engine efficacy.

## Windows CI and acceptance

GitHub's Windows workflow builds the full SDK projects and runs security, resilience, live reliability, UI, update/rollback, installer and reproducibility checks. Resilience attempts a read-only native Defender subscription/log query and reports unavailable/denied logs as unknown. An empty runner log does not test detections. Browser/server checks remain part of the workflow.

The local environment prevents an existing resilience fixture's loopback update-proxy connection; its full result must be checked on GitHub's runner. OS notification delivery, endpoint-specific schemas, real protection actions, managed-policy behavior and physical-device acceptance require testing on intended systems. No independent security audit or measured false-positive/detection-rate study was performed.

This preview remains unsigned. Live monitoring requires FishTank running and usable Defender telemetry. Server features remain undeployed and are independent of local live checking.
