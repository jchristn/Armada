# Dashboard surfaces for captain usage capture

This is the AC2 inventory of the dashboard surfaces that exist today for the captain usage plan
(`CAPTAIN_USAGE_PLAN.md`), the surfaces the plan defers, and the rendered verification performed on the
present ones. No phase 5 feature surface was added here.

## Present surfaces

| Plan surface | Dashboard surface | State styling present |
| --- | --- | --- |
| Captain list | `pages/Captains.tsx` (hub `pages/CaptainsHub.tsx`), `StatusBadge` state chips, per-column filters, bulk delete | loading, empty, error alert; state chips for Idle/Working/Stalled/Quarantined |
| Captain detail / edit | `pages/CaptainDetail.tsx` (state, settings, missions, log) and the captain edit form with `components/captains/MuxRuntimeFields.tsx` | loading card, error alert, empty mission list, form validation errors |
| Token charts | `pages/TokenUsage.tsx` (totals, `components/activity/TokenActivityHeatmap.tsx`, usage over time, usage by model, copy as image) and the Harbor charts that link into it (`?harborId=`) | loading, "No token usage for this time range", estimated-records note, disabled copy button |
| Dedicated jobs view | `pages/Jobs.tsx` (the `UsageProbe` kind is named in the kind tooltip in every locale) | loading, empty, error alert; `Running`/`Succeeded`/`Failed`/`Cancelled` badges |
| Global settings | `pages/Settings.tsx` with `components/settings/*` (retention, Ask, BlackBox, CLI permissions) | loading, error alert, empty; no capture switch yet (see below) |

## Absent surfaces, deferred to phase 5

These are the plan's phase 5 components. They do not exist in this build and were not added: the
usage capture switch and minimum interval in settings, `components/usage/` (CaptainUsageCard,
UsageWindowBar, UsageSparkline, UsageOverTimeChart, UsageMissionDrilldown, AccountLimitsTable,
UsageCaptureFields, UsageState), the captain capture fields in the edit form, provider limit history,
the usage mission drill-down, account/host limits, and the `Limits`/`Overview` tabs on the token usage
page. They depend on the phase 3 runtime probe adapters and the phase 4 REST/MCP routes, which are not
landed.

## Rendered verification (AC2 rework)

Built dashboard CSS (`npm run build`) rendered in headless Chrome over CDP at 1280, 768 and 390 pixels
in the light and dark themes, on faithful markup of the five present surfaces inside the real app shell
(`app-shell`/`app-layout`/`sidebar`/`main`). Thirty cases were measured for: page-level horizontal
overflow, content cut off by the content pane (the app hides horizontal overflow on `.main`), theme
token application, the base font size, the tab-order focus indicator of every control reached with real
Tab key events, and the state chip colors.

Observed and corrected:

- The Jobs `Running` and `Succeeded` badges had no `.tag` mapping, so both fell back to the neutral
  `.tag` default and read as "no state", unlike the same states everywhere else in the app. They are
  now on the working and complete pairs (theme-aware in light and dark). Pinned by
  `src/pages/JobsStatusBadgeCss.test.ts`.
- The shared form fields (`.form-group`, used by the settings and captain forms) declared
  `outline: none`, which outranks the app-wide keyboard focus ring in `src/a11y.css` (`:where()` adds
  no specificity), so a form showed only a border-color change on keyboard focus while every other
  control on the same page showed the ring. Keyboard focus gets the ring back; mouse focus is
  unchanged. Pinned by `src/components/settings/FormFieldsFocusCss.test.ts`.

Verified clean at every width and in both themes: no clipped content, no page-level horizontal
overflow, the 14px base font, 8px radii and the blue accents (`--accent` #2563eb light / #60a5fa dark),
theme tokens applied on every surface, and every keyboard focus stop carrying the accent ring
(`:focus-visible` matched on each stop).

Not verified: pixel-identical screenshots (geometry and computed styles were measured instead), other
browsers (Chrome 156 only), and the Windows/Linux Harbor app.

## Integrated recovery verification

The recovery Worker reran all 1497 dashboard tests and rebuilt the integrated branch.
`recovery-visual-check.mjs` checks representative existing surface markup against that production CSS
bundle at 1280/768/390 in both themes. All 30 checks passed, including geometry, keyboard focus
visibility/color and Jobs Running/Succeeded theme colors. Detailed measurements are recorded in
`recovery-visual-results.json`. The dark 390px settings screenshot was viewed. This remains a CSS
fixture check, not an authenticated page-flow or chart-interaction audit. See `RECOVERY_HANDOFF.md`
for the complete executed checks and pending independent review/landing gates.
