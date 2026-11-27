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

## Screenshots (to capture)

Not captured yet: they need a Release build signed in to the demo Admiral (RS-16.9a) so every screen shows
representative fleets, voyages, missions, and model profiles. No real account, host, repository, or key may appear.
Capture with `scripts/mobile/capture-store-screenshots.sh <target>` after setting `MISSION_ID`, `VOYAGE_ID`, and
`PROFILE_ID` (see the script header). The status bar uses the 9:41 demo values.

| Target | Folder | Device | Size |
| --- | --- | --- | --- |
| App Store iPhone 6.9" | `ios-6.9/` | iPhone Pro Max simulator | 1320 x 2868 |
| App Store iPad 13" | `ipad-13/` | iPad Pro 13-inch simulator | 2064 x 2752 |
| Google Play phone | `android-phone/` | Pixel emulator with the display at 1080 x 2160 (long side at most twice the short side) | 1080 x 2160 |

| File | Shows |
| --- | --- |
| `01-home.png` | Dashboard home: active voyages, missions in flight, items that need you |
| `02-voyages.png` | Voyages list with a mixed open and frontier pipeline running |
| `03-voyage-pipeline.png` | One voyage: its stages (Architect, Worker, Judge) and the captain and model of each |
| `04-mission-attempts.png` | Mission detail with the Attempts panel: a repair and an escalation to a stronger model |
| `05-model-profiles.png` | Captains hub, Model Profiles tab with tiers |
| `06-model-profile.png` | One open model's profile: scorecard by persona and its timeouts |
| `07-approvals.png` | Approvals inbox with Approve and Deny |
| `08-dark-mission.png` | The mission from 04 in dark mode |

After capturing, update this section with the capture date and what each shot shows.
