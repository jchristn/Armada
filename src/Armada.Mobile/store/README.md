# Store screenshots and graphics

Assets for the App Store and Google Play listings of Armada (see `../APP_STORE_SUBMISSION.md` and `listing.md`).
Every PNG is 24-bit (no alpha) except the Play icon, as the stores require.

## Google Play graphics (generated)

| File | Size | Shows |
| --- | --- | --- |
| `play-icon-512.png` | 512 x 512 | The app icon (`../assets/images/icon.png`) at 512 |
| `feature-graphic.png` | 1024 x 500, 24-bit | Dark slate gradient, the Armada logo, "Armada", and "Your fleet of AI coding agents, from your phone" |

Regenerate both with `scripts/mobile/generate-store-graphics.sh` (macOS, needs Google Chrome); the feature graphic is
rendered from `source/feature-graphic.html`.

## Screenshots

Captured 2026-11-27 from Release builds (iOS simulator, Android emulator) signed in as the non-admin reviewer of a local
demo Admiral seeded with `src/Armada.DemoSeed` (see `docs/DEMO_ADMIRAL.md`). Every name, repository and model in them
is fictional. Status bar at 9:41; 24-bit PNG with no alpha.

| Target | Folder | Device | Size |
| --- | --- | --- | --- |
| App Store iPhone 6.9" | `ios-6.9/` | iPhone 18 Pro Max simulator | 1320 x 2868 |
| App Store iPad 13" | `ipad-13/` | iPad Pro 13-inch (M5) simulator | 2064 x 2752 |
| Google Play phone | `android-phone/` | Pixel emulator with the display at 1080 x 2160 | 1080 x 2160 |

| File | Shows |
| --- | --- |
| `01-home.png` | Dashboard: system status, captains, missions by status |
| `02-voyages.png` | Voyages list |
| `03-voyage-pipeline.png` | One mixed open and frontier voyage and its mission tree |
| `04-mission-attempts.png` | Mission detail, Attempts panel: an open model escalated to a frontier model |
| `05-reliability.png` | Captains hub, Reliability tab: completion, repairs and escalations per model and persona |
| `06-model-profile.png` | The open model's profile and tier |
| `07-approvals.png` | Approvals inbox: reviews waiting and items that need intervention |

On iOS every `armada://` link opened with `simctl openurl` raises an "Open in Armada?" prompt, so the shots were driven
with Maestro (`openLink`, then tap Open) rather than `scripts/mobile/capture-store-screenshots.sh` alone. The app keeps its
own theme setting, so the simulator appearance switch does not produce a dark shot.
