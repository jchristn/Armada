# Simulated User Testing Results 1.0

Procedure: [SIMULATED_USER_TESTING.md](../SIMULATED_USER_TESTING.md). This is the first run, done unattended by an
agent acting as the user; the human run with real captains is still required (see the end of this file).

| | |
|---|---|
| Build | `work/sim-testing` from `main` at 145b3fb4 (version 1.0.0), `-c Release` for the Admiral host, Helm, and the dashboard `dist/` |
| Date | 2026-10-04 and 2026-10-05 |
| Tester | Agent (no human) |
| Captain | Scripted stub captain (`StubCaptainRuntime` from `src/Test.Shared`, installed with `ArmadaServer.RuntimeFactory.Override`) in place of Claude Code; a mission commits one file, Ask turns call the thread's `dispatch` tool, planning and refinement get a fixed plan |
| Stack | Throwaway Admiral host (`ArmadaServer` plus the stub) with `ARMADA_DATA_DIR` in a temp folder on ports 39300/39301; a second fresh one on 39310/39311 for the TUI first run; 13 temp repositories with bare origins; no access to `~/.armada` (checked before and after) |
| Surfaces | Dashboard in headless Chrome through Playwright at 1440x900 (all 28 main routes also loaded in all 9 locales); TUI (`armada tui`, Release build) in a 160x48 pseudo-terminal through Python `pty` and `pyte` |
| Evidence | Screenshots and text frames kept outside the repository (agent scratchpad `simtest/findings/dash` and `simtest/findings/tui`); file names are quoted below |

## Summary

- Every P1 and P2 task passed on the dashboard after fixes, some with findings; P3 passed except restore, which was
  not run. On the TUI, first-run sign-in, the setup wizard, dispatch, Ask Armada with the Approvals center, mission
  detail and log, and API-key sign-in with revocation were run end to end; the remaining TUI tasks were checked
  only for the screen (list, filters, counts), not as complete flows.
- Findings: 38 product findings. 3 S2 and 4 S3 were fixed on this branch (each with a regression test). Still open:
  0 S1, 0 S2, 13 S3, 18 S4, listed below with suggested fixes and owners.
- Exit criterion (section 7 of the procedure): **not met yet.** No S1 or S2 findings remain open, but the human run
  with real captains has not been done and the TUI flows were not all run end to end.
- Times are script times (an agent does not read or hesitate). Budget one to two minutes per task for a person.
  The setup wizard from sign-in to a landed first mission took 6.7 s of clicks plus status polling on the first run
  and 31.8 s on the second, which included an automatic rescue of a failed stub mission (see the harness notes).

## Results by task

| Task | Surface | Result | Time | Findings |
|------|---------|--------|------|----------|
| T1.1 First run and sign in | Dashboard | Pass | 1.2-4.4 s | Forced password change works; old password and the `default` token rejected afterwards |
| T1.1 | TUI | Pass with findings | 5 s | F9, F10 |
| T1.2 First mission via setup wizard | Dashboard | Pass with findings | 6.7 s (run 1), 31.8 s (run 2) | F6 (fixed), F21, F35, F37 |
| T1.2 | TUI | Pass with findings (wizard completed; the mission failed for a harness reason) | about 1 min of keystrokes | F24, F37 |
| T1.3 Understand what happened | Dashboard | Pass with findings | under 10 s | F18, F22 |
| T1.3 | TUI | Pass | under 10 s | F22 |
| T1.4 Dispatch without the wizard | Dashboard | Pass with findings | 4.7 s | F4 (fixed), F23, F25 |
| T1.4 | TUI | Pass with findings | under 20 s of keystrokes; the mission then waited 17 min | F11 |
| T1.5 Ask Armada | Dashboard | Pass with findings | proposal 1 s, landed 9.5 s after approval | F20 |
| T1.5 / T2.4 | TUI | Pass | proposal under 1 s; landed under 10 s after `a` in the Approvals center | F20, F31 |
| T1.6 Settings basics | Dashboard | Fail, then Pass after fix | 6 s | F1 (fixed) |
| T2.1 Register vessels | Dashboard | Pass with findings | 10.8 s | F23, F24 |
| T2.2 Import many repositories | Dashboard | Pass | 7.0 s (12 found, 9 created, 3 skipped as existing) | -- |
| T2.3 Dispatch a voyage | Dashboard | Pass with findings | voyage landed in 14 s; page stale for 90 s | F4 (fixed), F26 |
| T2.4 Ask: propose, approve, reject, track | Dashboard | Pass with findings | 3 s to approve; merge queue landed 10 s later | F14, F15, F20 |
| T2.5 Planning session | Dashboard | Pass with findings | 43 s (reply only visible after reload) | F16 |
| T2.6 Backlog | Dashboard | Fail, then Pass after fix | 14 s refine-apply-dispatch | F3 (fixed), F11, F16, F32, F33 |
| T2.7 Review and landing | Dashboard | Pass with findings | 23 s to the error, then 6 s via Manage Branches | F11, F12, F13 |
| T2.8 Merge queue | Dashboard | Pass with findings | auto-processed and landed in 10 s | F14 |
| T2.9 Fleet actions | Dashboard | Pass | run of 11 vessels finished in a few seconds (10 succeeded, 1 skipped: no working directory) | F34 |
| T2.10 Vessel health | Dashboard | Pass | 10.7 s for Evaluate all | -- |
| T2.x screens | TUI | Screens checked (not full flows) | -- | Missions, Merge Queue, Vessel Health, Fleet Actions, Planning, Needs You, Backlog render with data |
| T3.1 Users | Dashboard | Pass with findings | 6.3 s | F27 |
| T3.2 Credentials | Dashboard + TUI | Pass with findings | create 3.7 s; TUI API-key sign-in; revoke gives 401 and the TUI returns to "Your session expired" | F27 |
| T3.3 Server settings, Diagnostics | Dashboard | Pass with findings | -- | F29 (the Settings fail in Diagnostics is a harness artifact) |
| T3.3 | TUI | Screens checked | -- | Users, Credentials, Diagnostics, Events |
| T3.4 Delivery basics | Dashboard | Fail, then Pass after fix | release from Draft Release in 9 s after the fix | F2 (fixed), F19, F28, F38 |
| T3.5 Activity and audit | Dashboard | Pass with findings | -- | F17 |
| T3.6 Backup | Dashboard | Backup Pass (file download, no visible confirmation); Restore Not run | -- | -- |
| Locale sweep (28 routes x 9 locales) | Dashboard | Fail, then Pass after fix | -- | F1 (fixed) |
| Admiral restart | Dashboard + TUI | Pass with findings | -- | F8 |

## Findings

Severity per the procedure: S1 blocker, S2 major, S3 minor, S4 cosmetic. "Owner" names the area that should take an
open finding; this branch only changed files outside the areas other workstreams own.

### Fixed on this branch

#### F1 (S2, fixed) Six locales blank the whole dashboard on the Settings page
- Persona / task / surface: P1 T1.6, dashboard.
- Steps: sign in, open Settings (`/dashboard/server`), pick Deutsch (or Francais, Italiano, Japanese, Traditional
  Chinese, Cantonese) in the language picker.
- Expected: the page in German. Actual: white page, "Maximum call stack size exceeded"; the locale is stored, so
  reloading the same URL stays blank (the picker is gone). Spanish and Simplified Chinese were unaffected because their
  catalogs had the template.
- Cause: the DOM translator translated a matched message through its template with `translateText`, which ran the
  same pattern on the template ("Copy {{title}} HTTP config to clipboard") and recursed until the stack overflowed.
- Fix: `src/Armada.Dashboard/src/i18n/runtime.ts` looks templates up in the catalog only. Test:
  `i18n/runtime.test.ts` (16 message shapes against a catalog without templates). Verified live: 28 routes x 9
  locales with no page errors. Evidence: `t16-04-german.png` (before).

#### F2 (S2, fixed) Draft Release never opens
- P3 T3.4, dashboard. Steps: open a voyage (or a backlog item, or Checks) and select Draft Release.
- Expected: the Create Release form. Actual: `/releases/new` shows "Loading..." forever (all requests 200).
- Cause: the static `releases/new` route has no `:id`, and the page only treated `id === 'new'` as create mode.
- Fix: `ReleaseDetail.tsx` treats a missing id as create mode. Test: `pages/ReleaseDetail.test.tsx`. Verified live
  (release created from a voyage). Evidence: `t34-03-draft-release.png`.

#### F3 (S2, fixed) Summarize in backlog refinement crashes the page
- P2 T2.6, dashboard. Steps: backlog item, Start Refinement (choose a captain), send a message, select the reply,
  Summarize.
- Expected: the Refinement Summary Draft. Actual: blank page, "Cannot read properties of undefined (reading 'map')".
- Cause: the `objective-refinement-session.summary.created` event is `{ sessionId, messageId, summary }`; the page
  used the whole event as the draft, so `acceptanceCriteria` was undefined.
- Fix: `lib/refinementSummary.ts` reads the draft from the event and normalizes the list fields; used by
  `ObjectiveDetail.tsx`. Test: `lib/refinementSummary.test.ts`. Verified live: summarize, apply, open in Dispatch,
  dispatch. Evidence: `t26-11-summarize-crash.png` (before).

#### F4 (S3, fixed) Voyage and mission pages do not update while work runs
- P1 T1.4, P2 T2.3, dashboard. Steps: create a voyage with three missions and stay on the voyage page.
- Expected: statuses advance as missions run and land. Actual: after 90 s the page still showed InProgress, one
  mission InProgress and two Pending, and "Created (19 seconds ago)", while the server had completed the voyage
  14 s after creation. Only a reload showed the truth.
- Fix: new `lib/useLiveRefresh.ts` (debounced reload on `mission.*`/`voyage.*` events and after a reconnect) used by
  `VoyageDetail.tsx` and `MissionDetail.tsx`; the voyage page no longer flashes "Loading..." on refresh. Test:
  `pages/VoyageDetail.test.tsx`. Verified live (mission page followed Pending to Complete on its own).

#### F5 (S3, fixed) Header background-activity indicator always fails
- Every page, dashboard. The header polls `GET /api/v1/jobs?status=Queued%2CRunning&pageSize=100` and gets 400
  "unknown job status 'Queued%2CRunning'", so running jobs were never shown.
- Fix: `Armada.Core/Models/JobQuery.cs` decodes querystring values before parsing. Test:
  `Database.Jobs/querystring_percent_encoded`.

#### F6 (S3, fixed) Stale tooltips and screen-reader labels after React reuses an element
- P1 T1.2, dashboard. In the setup wizard, the Captain step's Captain Name field had the label "Display name for this
  repository inside Armada." and Model had "Git clone URL or local repository path..." (left from the Vessel step).
- Cause: the translator cached each attribute's first value and wrote it back after React changed it.
- Fix: `i18n/runtime.ts` adopts attribute and button values that changed since it last wrote them. Test:
  `i18n/runtime.test.ts`. Verified live (labels correct on the second run).

#### F7 (S3, fixed) Landing leaves `armada-landing/*` branches in the user's checkout
- P1 T1.2, any Local Merge landing. After each landed mission, `git branch` in the working directory showed
  `armada-landing/armada/<captain>/<mission>`; one per landed mission accumulates.
- Fix: `GitService.MergeBranchLocalAsync` deletes the scratch ref after the merge (success or failure) and after
  materializing a missing target branch. Tests: `Services.GitService` merge cases now assert no
  `refs/heads/armada-landing/` remains (both failed on the old code).

### Open

#### F8 (S3, fixed) Every Admiral restart signs everyone out
- Owner: server auth. Surfaces: dashboard and TUI ("Your session expired. Sign in again." after every restart).
- Steps: start a fresh Admiral (real `Armada.Server`, temp data dir), sign in, change the password, restart the
  Admiral, call `whoami` with the old session token: 200 before, 401 after.
- Cause: `settings.json` never gets `sessionTokenEncryptionKey`. `EnsureApiKeyAsync` saves the settings file before
  `StartAsync` generates the session key, and nothing saves it afterwards, so each start uses a new key.
- Suggested fix: generate the session key before `EnsureApiKeyAsync` (or save settings after generating it), with a
  restart test.
- Fix: the API key and session token key are generated before the single settings save, so the key persists. Test:
  `E2E.ServerStartup` `session_token_survives_restart`.

#### F9 (S3, fixed) TUI first sign-in fails when the user types the documented default password
- Owner: TUI. Steps: fresh profile against a localhost Admiral, Continue, type `password` (as the screen's "Default
  credentials" line says), Enter.
- Actual: "Authentication failed." The masked field was already prefilled with `password`, so the typed text was
  appended. Pressing Enter without typing works. Evidence: `s0-02-after-signin.txt`.
- Suggested fix: select the prefilled value so typing replaces it, or show the prefill as a placeholder.
- Fix: a prefilled password or API key starts selected; typing or pasting replaces it. Tests: two `Tui.Login` cases.

#### F10 (S3, fixed) The TUI works with the default password; the dashboard forces a change
- Owner: security / TUI. The TUI signed in with `admin@armada` / `password` on a fresh Admiral and created a fleet,
  vessel, captain, and mission through the wizard; only a header warning is shown. The dashboard blocks everything
  until the password is changed. `docs/TUI.md` says the TUI does not force a change, while the `TUI_APP_PLAN.md`
  progress log mentions a forced default-password change step. Evidence: `s0-05-signed-in.txt`.
- Suggested fix: decide one policy (the server could refuse non-password calls from a default-password session) and
  align the TUI, the docs, and the plan.
- Fix (docs): policy decided by the maintainer: the TUI warns and does not force a change; the dashboard forces it.
  `docs/TUI.md` and `TUI_APP_PLAN.md` W8.7 updated.

#### F11 (S3, fixed) Pending missions never say why they are waiting
- Owner: dashboard and TUI (mission and voyage pages), server (assignment reason).
- Case 1 (T2.6, T2.7): after Apply To Backlog Item the refinement session stays Active and keeps the only captain in
  Refining, so new missions sit in Pending with no explanation; the mission page shows "Ready To Land" next to
  "Mission is not in a landing state". They started 9 s after Stop Session.
- Case 2 (T1.4 TUI): after three failed missions the captain was quarantined for 15 minutes (crash loop). The
  captain list showed Idle via the API, and the mission waited 17 minutes with no reason on any page.
- Suggested fix: show the assignment blocker on Pending missions and voyages ("waiting for a captain: Setup Captain
  is Refining backlog item X" / "quarantined until 07:33"), and end or offer to end the refinement session on Apply.
- Fix: the server computes an `AssignmentBlocker` (typed reason, summary, clear time, captain activity) on
  `GET /missions/{id}` and Pending missions in `GET /voyages/{id}`; the dashboard mission page and TUI mission screen
  show it. Apply To Backlog Item ends the refinement session by default (`EndSession: false` keeps it). Tests:
  `Services.MissionAssignmentBlocker`, `Services.LandingPreviewMission`, refinement apply cases.

#### F12 (S3, fixed) Landing Mode None: a Land button that cannot land, and no way back to Complete
- Owner: dashboard, server. Steps: vessel with Landing Mode None, dispatch, open the WorkProduced mission.
- Actual: a primary Land button and a "Ready To Land" pill; Land returns 409 with a good message (set a landing mode,
  or merge from Manage Branches). After merging and pushing from Manage Branches, the mission stays WorkProduced.
  Evidence: `t27-09-landed.png`, `t27-11-merged.png`.
- Suggested fix: for None, replace Land with "Merge in Manage Branches"; reconcile WorkProduced missions whose branch
  is merged into the target.
- Fix: manual-only (Landing Mode None) missions show "Merge in Manage Branches" instead of Land (dashboard and TUI);
  `ManualLandingReconciler` completes a WorkProduced mission once its commit is in the target (`git merge-base
  --is-ancestor` exit code). Test: `Services.ManualLandingReconciler`.

#### F13 (S3, fixed) The vessel page's Edit Vessel lacks landing settings
- Owner: dashboard. Vessels > a vessel > More > Edit has no Landing Mode, Branch Cleanup, Agent Auto-Approve,
  concurrency, or auto-land fields; the Edit from the Vessels list (and Create) has them. Users looking on the vessel
  page cannot change how work lands. Suggested fix: one vessel form for both.
- Fix: one shared form, `components/vessels/VesselFormModal.tsx` with `lib/vesselForm.ts`, for Vessels Create/Edit and
  the vessel page Edit. The vessel PUT replaces the whole record, so each old form also reset the other's fields (the
  vessel page Edit cleared Landing Mode, auto-land, and auto-approve); the payload now starts from the stored vessel.
  Test: `pages/VesselDetail.test.tsx`.

#### F14 (S3, fixed) A voyage is Complete before its merge-queue mission lands
- Owner: server (voyage completion). T2.4/T2.8: a dispatch to a Merge Queue vessel showed voyage Complete while its
  only mission was WorkProduced and Queued in the merge queue; the Ask card read "Complete, 0 of 1 finished". It
  landed 10 s later here; with a slow or manual queue the voyage would claim completion for much longer.
- Suggested fix: keep the voyage InProgress (or "Awaiting merge queue") until its entries land or fail.
- Fix: a voyage stays InProgress while a WorkProduced mission has a Queued, Testing, or Passed merge-queue entry.
  Tests: two `Services.LandingPipeline` cases.

#### F15 (S3, fixed) The dashboard has no place to see pending Ask approvals
- Owner: dashboard. Needs You lists failures and alerts but not Ask proposals waiting for approval; the only way is to
  open each conversation. The TUI has an Approvals center (`Ctrl+A`) that worked well.
- Suggested fix: list pending Ask proposals (and other approvals) in Needs You.
- Fix: the inbox (`InboxService`, `GET /api/v1/inbox`, MCP `inbox`) lists the caller's pending, unexpired Ask
  proposals as kind `ask_proposal` linking to `/ask/<threadId>`; Needs You groups mission reviews, deployment
  approvals, and Ask proposals under "Waiting for your approval" with an action button. Tests:
  `Services.Inbox/pending_ask_proposals_for_caller_are_listed`, `pages/Inbox.test.tsx`.

#### F16 (S3, fixed) Fast captain replies in Planning and Refinement are not shown until a reload
- Owner: dashboard. Steps: planning session (or backlog refinement), send a message to a captain that answers within
  a few milliseconds. Actual: the reply exists on the server, but the page keeps the thinking placeholder and a Stop
  button indefinitely; reload shows it.
- Cause: the page applies the send POST's response (status Responding, empty reply) after the WebSocket updates that
  completed the turn, overwriting them. Real CLI captains are slower, so this mostly affects very fast endpoints.
- Suggested fix: refetch the session after the POST, or merge by `lastUpdateUtc`.
- Fix: `lib/liveMerge.ts` merges the send response by `lastUpdateUtc` (sub-millisecond aware) on Planning and the
  backlog item's refinement panel. Tests: `pages/Planning.test.tsx`, `lib/liveMerge.test.ts`.

#### F17 (S3, fixed) All Activity is the dashboard's own polling
- Owner: dashboard / request history. All 250 visible entries were GET requests from the open dashboard
  (`/api/v1/inbox`, `/status/health`, `/jobs`, ...); missions, approvals, and admin actions were not visible without
  filtering. Events showed the real history. Suggested fix: hide GET requests in All Activity by default.
- Fix: additive `HistoricalTimelineQuery.ExcludeReadRequests` (also `excludeReadRequests` on `GET /api/v1/history`)
  drops GET, HEAD, and OPTIONS request entries; All Activity sets it unless "Show GET requests" is checked. Tests:
  `Services.HistoricalTimelineService/enumerate_exclude_read_requests_hides_get_polling`, `pages/History.test.tsx`.

#### F18 (S3, fixed) Mission logs lose their line breaks in the dashboard
- Owner: dashboard. The log viewer renders mission logs as Markdown, so a six-line log becomes one paragraph. The TUI
  shows it line by line. Evidence: `t13-02-Log.png`. Suggested fix: keep single newlines (remark-breaks or
  `white-space: pre-wrap` in the log viewer).
- Fix: the log viewer's Markdown keeps single newlines (`lib/remarkLineBreaks.ts`). Test:
  `components/shared/LogViewer.test.tsx`.

#### F19 (S3, fixed) Deployment form lists 12 identical "Development" environments
- Owner: dashboard. Every vessel gets a default Development environment; Create Deployment's Environment picker lists
  them all by name only before a vessel is chosen. Suggested fix: filter by the chosen vessel, or show "vessel /
  environment".
- Fix: without a vessel the picker shows "vessel / environment" sorted by that label; with a vessel it lists only
  that vessel's environments, and changing the vessel clears an environment of another vessel. Test:
  `pages/Deployments.test.tsx`.

#### F37 (S3, fixed) The wizard handoff shows Failed with no reason
- Owner: dashboard and TUI. When the first mission fails, the handoff shows "Status: Failed" with no failure reason
  or log link, and Armada starts "[Rescue]" missions on its own that the handoff does not mention.
- Suggested fix: show the failure reason and a log link on the handoff, and mention rescue attempts.
- Fix: dashboard and TUI handoffs show the failure reason, a View Mission Log action, and the rescue missions found
  through the mission's incidents (rechecked until they settle); the full mission id is shown (also F21). Tests:
  `components/shared/MissionFailureDetails.test.tsx`, `Tui.System.Setup/handoff_explains_failed_mission`.

#### F20 (S4, fixed) Ask proposal cards show the vessel id, not its name
- Dashboard and TUI: "Dispatch voyage ... to vessel vsl_muuvmsev_bkdC3DJiinp with 1 mission(s)". The summary comes
  from the server; show the vessel name.
- Fix: proposal summaries name the vessel (`dispatch`, `create_mission`); unknown ids still show the id. Test:
  `Services.AskApprovalGate/proposal_summary_names_the_vessel`.

#### F21 (S4, fixed) Wizard handoff ids are shortened
- "MISSION ID msn_muuv6go5" is not an id; pasting it into the URL gives "Mission not found". Show the full id or a
  copy button (the Open Mission button works).
- Fix: the dashboard and TUI handoffs show the full mission id (dashboard with a copy button).

#### F22 (S4, fixed) "Ready To Land" on a Complete (already landed) mission
- Both surfaces; the server's landing preview ignores the mission status.
- Fix: the landing pill reads "Landed" for Complete and "Not Ready Yet" for Pending missions (with F11).

#### F23 (S4) Pluralization
- "1 branches" (Vessels), "with 1 mission(s)" (Dispatch toast, Ask cards), "Retry N failed mission(s)".

#### F24 (S4) Different vessel defaults on every surface
- Setup wizard: Landing Mode None and concurrent missions on (dashboard) but off (TUI); Create Vessel: Local Merge
  preselected, concurrency off; Import: "Default (use global setting)". Imported vessels with a working directory
  then land into the user's own checkout through the global default.

#### F25 (S4) Priority direction is unexplained on Dispatch
- "Higher priority missions are assigned first (default 100)" does not say that a lower number is higher priority
  (the wizard says so).

#### F26 (S4, fixed) Vessel pickers are ordered newest first
- Create Voyage, Dispatch, Planning, and Backlog list 12 vessels in creation order, not alphabetically.
- Fix: Create Voyage, Dispatch, Planning, Backlog, the backlog item page, and Deployments sort vessels by name
  (`lib/sortByName.ts`, test `lib/sortByName.test.ts`).

#### F27 (S4) Credentials: odd defaults
- Create Credential preselects the first user in the list (here a deactivated user) instead of the signed-in user.
  Creating a user also creates "Credential for <user>", which still shows Active after the user is disabled (check
  that the token is refused; owner: security). A user created by an admin with a chosen password is not asked to
  change it at first sign-in.

#### F28 (S4) Deployment form details
- Placeholders "mis_..." and "voy_..." do not match the id prefixes `msn_` and `vyg_`; the "Execute immediately"
  checkbox sits in the middle of the row, away from its label.

#### F29 (S4) Settings shows an MCP URL that does not answer
- With `rest.hostname` 127.0.0.1, the Settings MCP snippet shows `http://localhost:39301/rpc`, which returns 404
  (`127.0.0.1` works). The generated client URLs were fixed earlier; this snippet was not.

#### F30 (S4) Needs You keeps a failed mission after its rescue landed
- "Failed: Append Armada line to README" stays after "[Rescue] ..." completed the work.

#### F31 (S4) TUI toasts cover the Approvals center text
- Toasts on the right edge clip the empty-state text ("failed lan..."). Evidence: approvals frame after `a`.

#### F32 (S4) Start Refinement is disabled with no hint
- Disabled until a captain is picked, even when only one captain exists; preselect it or say why.

#### F33 (S4) Two state fields on a backlog item
- STATUS (Draft ... Cancelled) and BACKLOG STATE (Inbox ... Dispatched) sit next to each other without explanation.

#### F34 (S4) The Admiral API key cannot see fleet action runs
- The API-key identity (global admin in the system tenant) gets 404 for `/api/v1/fleet-action-runs/{id}` of a run in
  the default tenant, while missions and vessels of every tenant are visible. Owner: server routes.

#### F35 (S4) The setup wizard restarts at step 1 when reopened
- Closing and reopening the wizard mid-way starts again at Objective (dashboard).

#### F36 (S4) Console noise on every page
- `GET /proxy-api/v1/session/context` returns 404 on every load when the dashboard is served by the Admiral.

#### F38 (S4) Release page requests GitHub pull requests for a non-GitHub vessel
- `GET /api/v1/releases/{id}/github/pull-requests` returns 400 for a local bare-origin vessel (console only).

### Harness notes (not product findings)

- The stub names its commit file after a synthetic process id that restarts with each harness process; repositories
  that already held that file produced "nothing to commit", a failed mission, and automatic "[Rescue]" missions. The
  harness was changed to start each run at a fresh id; the failures above (Needs You, the TUI wizard mission) came
  from this.
- The harness sets the Admiral API key itself, so the server never writes `settings.json` (Diagnostics "Settings
  file not found"). F8 was confirmed with the real `Armada.Server` instead.
- Stub replies make Summarize return the plan unchanged and Ask progress updates repeat the stub's default text.

## Not run, and what a human still must check

1. The whole procedure with real captains (Claude Code and Codex, signed in): reply quality, streaming, thinking and
   tool display, Ask approval gating on real tool calls, and the ten-minute first landed mission (W6.7).
2. Visual judgement: layout, color, and wording on the dashboard (light and dark, 390 px) and the TUI in a real
   terminal (iTerm2 or Windows Terminal) at 80x24 and 160x48; this run read text, not pixels.
3. TUI flows run only as screen checks here: import, voyage creation, planning, backlog refinement, review and landing,
   merge queue, fleet action run, vessel health evaluation, users, settings, delivery, and backup.
4. Restore from backup on a throwaway stack (T3.6).
5. Triage the open S3 findings above, especially F8 (sign-outs on restart), F10 (default-password policy), F11, F12,
   and F14.
