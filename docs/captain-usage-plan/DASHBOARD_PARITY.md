# Account usage client parity

Phase 6 replaces the phase 5 client exceptions with implemented screens. All clients use the same tenant-scoped usage API and distinguish unknown observations from zero usage.

| Surface | Dashboard | TUI | Mobile |
|---|---|---|---|
| Captain list | Usage column and View Usage | Batch-loaded Usage column and View Usage (`U`) | Paged batch-loaded indicator, swipe and menu View Usage |
| Captain detail | Usage card, windows and capture | Usage panel, windows and Capture now (`C`) | Native Usage section, windows and Capture now |
| Account/host limits | Activity Limits | Activity Limits (`source=limits`), paged | Activity Limits tab, paged |
| Captain configuration | Capture settings | Captain form: inherited flag, mode, interval, Custom JSON | Native inherited flag, mode, interval, Custom executable/arguments |
| Global configuration | Usage capture settings | Server settings Usage capture group | Server configuration Usage capture section |
| Cooldown | Expiring disabled control | Expiring disabled control, exact server deadline | Expiring disabled control, exact server deadline |

`ArmadaClient.UsagePath` serializes the batch captain IDs as comma-separated values. Both lists issue one latest request per visible page, with at most 100 IDs; mobile uses 25-row pages. Account percentages are never summed. Last-good age, stale, authentication, unsupported, estimated and failure states remain separate. Thresholds have text labels alongside color. Native components follow their host theme and screen-reader conventions.

The shared clients retain the snapshots, series and mission query contracts for callers; phase 6 client screens present latest account limits rather than reproducing the dashboard's interactive historical charts.

Regenerate manifests with `python3 scripts/tui/generate-parity-manifest.py` and `python3 scripts/mobile/generate-parity-manifest.py`.

Current Dashboard navigation uses peer Token Usage and Captain Usage tabs. Captain Usage combines the interactive historical charts and the latest account limits table. Old `source=usage-limits` and nested limits links open its account-table section with filters preserved; `source=usage-overview` remains the stable chart/page URL.

TUI and Mobile retain their native Account Limits surface (`source=usage-limits`, with `source=limits` and `source=usage-overview` aliases). Historical quota charts remain outside native presentation scope. The parity manifests document that exception and have no planned entries.
