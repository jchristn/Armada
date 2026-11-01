# Captain Active eligibility

`Captain.Active` is persisted separately from runtime `State`. It defaults to true
for new objects, omitted JSON, new rows, and rows upgraded by migration 112. False
prevents new work; it does not stop an existing process, cancel its mission, or
release an existing reservation. Reactivation restores normal eligibility.

REST POST accepts `Active`; PUT changes it only when supplied. MCP
`create_captain` and `update_captain` accept optional `active`. Helm accepts
`armada captain add NAME --active=false` and
`armada captain update ID --active=true`; omission on update preserves it.
Dashboard, mobile, TUI and the legacy web UI display eligibility independently of
State and provide create/edit controls. The .NET client reuses the Core Captain
model; mobile reuses the Dashboard model and form payload builder, so neither
requires another serialization model or endpoint.

Automatic, persona, tier, envelope and recovery selection excludes disabled
captains. Explicit dispatch assignments are rejected before creating the voyage;
a queued mission whose requested captain is later disabled waits instead of
silently choosing another captain. Both database claim/reservation overloads
check Active atomically on all four providers. Ask turns, summaries, planning,
refinement, evaluation, categorization and runtime launch boundaries also check
eligibility. Errors direct the operator to enable Active.

Database `ICaptainMethods.UpdateAsync` intentionally preserves Active by default.
A configuration editor must pass `updateActive: true`; REST and MCP do so only
when the request carries Active. Operational snapshots, including snapshots
loaded before a concurrent disable, cannot reactivate a captain while recording
completion, recovery or heartbeat changes.

## Verification on the mission branch

.NET SDK 10.0.401 / net10.0 was used, the newest installed targeted framework.
The full suite is left to Armada's post-stage integration gate.

- `dotnet build src/Armada.Server -f net10.0 --nologo -v quiet`: passed.
- `dotnet build src/Armada.Tui -f net10.0 --nologo -v quiet`: passed; one transient
  shared-output copy retry while another build was running.
- `dotnet build src/Test.Automated -f net10.0 --nologo -v quiet`: passed without warnings.
- `dotnet run --project src/Test.Automated -f net10.0 -- --suites Models.Captain,Database.Captain,Database.SchemaMigration,Database.MigrationHygiene,Services.CaptainService,Services.CaptainRouting,Services.CaptainTier,Services.PersonaCaptainPriority,Services.MissionAssignmentBlocker,Services.WorkerEnvelope,Services.AskTurnCoordinator,Services.AskThreadService,Services.ModelProbe,Services.HelmRemoteTarget,Services.PlanningSession,Services.ObjectiveRefinement,E2E.Captain,E2E.ApiContract,Client.RouteSurface,Tui.Parity`: 387 passed, zero failed/skipped.
- `dotnet run --project src/Test.Automated -f net10.0 --no-build -- --suites Database.Captain,Database.MigrationHygiene --db-type PROVIDER --db-host 127.0.0.1 --db-port PORT --db-user USER --db-pass PASSWORD --db-name armada_test`:
  39/39 passed separately on postgres:17, mysql:8.4 and
  mcr.microsoft.com/mssql/server:2022-latest, using disposable containers and
  their ephemeral published ports. Containers were removed after each run.
- Dashboard: `npm ci`, `npm run build`, and
  `npm run test:run -- src/pages/CaptainCheckboxLayout.test.ts src/lib/captainForm.test.ts src/components/shared/CaptainPicker.test.tsx src/pages/CaptainsToolbarLayout.test.ts`:
  build passed, 24 tests passed.
- Mobile: `npm ci --no-audit --no-fund`, `npm run typecheck`, and
  `npm run test:ci -- --runInBand src/__tests__/captains.test.tsx`:
  typecheck passed, 14 tests passed including accessibility auditing. Jest
  printed its existing open-handles advisory and exited zero.
- `bash scripts/common/generate-api-surface.sh -f net10.0`: regenerated MCP and
  CLI contract additions. Unrelated inherited usage-capture settings drift was
  excluded from this mission's baseline change.
- `git -c core.whitespace=cr-at-eol diff --check`: passed; existing CRLF files
  retain their line endings.

Named regressions cover omitted/default JSON, false serialization, migration of
populated rows, REST/MCP create/read/update, omitted update preservation, explicit
CLI true/false parsing, stale operational snapshots, atomic claim rejection,
explicit assignment with an active fallback available, persona/tier reactivation,
recovery preserving held work, and disabling during a known in-flight Ask request
before rejecting the next turn and summary. Dashboard contracts pin both native
checkbox labels, inline row direction, intrinsic input width, and disabled picker
options.

Headless Chrome rendered the actual built Dashboard CSS against the captain
modal markup at 1280, 768 and 390 pixels, in light and dark themes. Each switch
measured 13 by 13 pixels with a 7 pixel label gap; text remained within its label,
and center offsets were below 0.01 pixels. Keyboard Tab reached auto-approve
with a visible 2 pixel accent outline. The narrow dark-theme rendering was also
visually inspected. This was a built-CSS fixture, not a live authenticated server
browser session. Full device end-to-end flows were not run.

## Landing and required release follow-up

Voyage `vyg_mvxc33sx_97SzD7UnXCc` is configured for `MergeAndPush`. During worker
execution this mission is `InProgress`. `MergeQueueService.LandEntryAsync`
refuses publication unless the mission is `WorkProduced` or `LandingFailed`;
the worker must exit before the pipeline can land its work. No manual status
mutation or unassociated merge is used to bypass that gate.

After the pipeline lands and pushes this branch, identify the exact published
landing commit M from the mission/merge evidence. In a clean checkout at M,
verify `git rev-parse HEAD` equals M and `git ls-remote origin refs/heads/main`
reports M, then run:

```sh
./scripts/macos/build-all.sh v1.2.0
```

This exact platform wrapper is documented in `docs/DOCKER.md` and
`docs/RELEASING.md`; it invokes `scripts/common/build-all.sh`. It builds, pushes,
and pulls the server, dashboard and proxy images as `latest` and `v1.2.0`, for
linux/amd64 and linux/arm64/v8. Record the image digests and command exit status
against M. A build on the mission branch does not satisfy this requirement.

Main landing/push and this post-landing release build remain pending when the
worker hands off. No release image or digest is claimed by this implementation.
