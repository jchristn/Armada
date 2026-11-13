# Decisions Pending: Mission Post-Mortem and API Surface Audit

Date: 2026-10-24. Covers mission `msn_mvmiaphg_7NXYdbigLoJ` (voyage `vyg_mvmiapgc_FNetD0BinPP`) and an audit of discrepancies between Armada's API surfaces (REST, MCP, WebSocket, CLI, TypeScript client, .NET client).

## Status of the work already done

| Item | State |
|---|---|
| Ask Armada multi-select and bulk delete (the mission's goal) | Finished by hand from the mission's diff. Commit `94bffb7ef`, pushed to `main`. |
| Persona view modal Edit button | Commit `30a7db64f`, pushed. |
| Codex captain tools probed on the captain's Harbor instead of the Admiral | Commit `3d9075db2`, pushed. |
| Images | `build-all.sh v1.1.0`: the server image pushed, then Docker Hub timed out on the local pull and the script stopped. The pulls were retried and the dashboard and proxy builds run by hand. Server `sha256:430b12cc...`, dashboard `sha256:793885aa...`, proxy `sha256:3e905747...`. |
| view.homedns.org | DocMon pull, dry run, then recreate. Both services healthy; server started 16:02:21 UTC; running image matches the build; bind mounts unchanged. Evidence in `/tmp/armada-askbulk-rollout-20261024`. |
| Tests | Parity, client, API contract and Ask suites: 681/681. Dashboard: 924/924. The final full `Test.Automated` run was skipped at the user's request. |

## Post-mortem: msn_mvmiaphg_7NXYdbigLoJ

### 1. Why it went to DeepSeek instead of Claude

- The Worker persona had **no captain priorities** (`CaptainPriorities: []`, last updated 14:38:33 UTC).
- Request history shows `PUT /api/v1/personas/Worker` from the dashboard session (`admin@armada`) at 14:38:33 with a **275-byte** body. The dashboard's payload with `captainPriorities: []` is exactly 275 bytes; with even a minimal `{ "runtime": "ClaudeCode" }` entry it is 299 bytes or more. The save sent an empty list.
- The mission was created at 14:40:51 from an Ask Armada proposal (thread `ath_mvmi8ys1_iwsXUgsd8AB`, approved 14:40:54). It had `Persona: null`, which routes as Worker (`MissionService.RoutingPersona`). With no priorities, normal routing picked the idle `Deepseek v4.1 Flash` captain (`cpt_mvfprx19_gETFJOfmwqA`).
- **Likely cause (not proven):** in the persona editor, "Runtime for new priority" is a dropdown beside a separate **Add priority** button (`PersonaPriorityEditor.tsx`). Choosing Claude Code in the dropdown adds nothing until the button is clicked; saving without clicking it saves an empty list with no warning.
- **Fix to consider:** warn on save when a runtime is chosen but not added, or add it automatically.

### 2. Why it failed

The mission never failed on its own; it was cancelled. The timeline (UTC):

1. **14:40 to 14:45:** DeepSeek finished the work and committed `074c77e`; the process exited 0.
2. **14:45 to 14:50:** the stage check `dotnet test "src/Armada.sln" -c Release` timed out after about 5 minutes. That command takes far longer on this machine (one test assembly alone took 27 m 21 s), so this check cannot pass in its window.
3. **15:00:18:** two recovery paths fired one second apart for the same mission: "The stage check timed out" and "Agent interrupted by server/Harbor restart (exit -1)" (`AdmiralService.cs:2244`). Harbor had not restarted (PID 14090, up 1d12h). Each path relaunched the captain (15:00:19 and 15:00:20), so **two DeepSeek agents ran in the same dock**. They killed each other's test runs (`MSBuild child node exited prematurely`); the dock reflog shows the double reset.
4. **15:11:23:** the Ask Armada captain cancelled the mission through MCP `cancel_mission`, right after a user message at 15:11:18.
5. **After the cancel, the agent kept running** inside Harbor on the Mac: it committed `1eb7d5cc6` at 15:29 and was still running `dotnet test` at 16:00. `stop_captain` could not reach it (the captain was already Idle with no process ID). Its `dotnet test` processes were killed by hand; **the agent loop itself is still inside the Harbor process (PID 14090)** and only a Harbor restart ends it.

### Bugs this exposed

- **Cancel never stops the process.** See finding A3 below.
- **Duplicate relaunch.** The stage-check-timeout path and the interrupted-exit path both call resume-in-place for the same mission within a second, with no guard against a second launch on the same dock.
- **Stage-check timeout far shorter than its command.** The vessel's stage check runs the whole solution's tests with about a 5-minute limit.
- **Interrupted exit misclassified.** An exit of -1 after the stage check was reported as a "server/Harbor restart" although neither restarted.

## API surface audit

Read-only audit. Runtime behavior below comes from reading the code, except the items marked **verified**, which were checked against source and the route baseline.

### Baseline

- REST: all 374 route registrations match `docs/api-surface-1.0.json` (376 entries; the extra two are framework-provided `GET /openapi.json` and `GET /swagger`).
- MCP: all 175 tools match the baseline.
- WebSocket: handled commands match the baseline; `docs/WEBSOCKET_API.md` documents every command and event.
- `docs/REST_API.md` and `docs/MCP_API.md` document every route and tool.

### Operations per entity

Key: C create, R get one, U update, D delete, L list, E enumerate, B bulk delete. "via enum" means only the generic MCP `enumerate` tool covers it.

| Entity | REST | MCP | WS | CLI | TS client | .NET client |
|---|---|---|---|---|---|---|
| Fleets | CRUDL E B | CRUD B, L via enum | CRUDL | add/list/remove (no update/show) | CRUDL B | CRUDL B |
| Vessels | CRUDL E B, import, PATCH context, branches, git-status, history, readiness, build-context, landing-preview | CRUD B, context, import tools, health | CRUDL, context | add/list/remove/history/import (no update) | all but B and PATCH | same as TS |
| Captains | CRUDL E B, stop, stop-all, log, tools, chat, unquarantine, CLI policy | CRUD B, stop, stop_all, log, tools, `release_captain`, policy; no chat, no `modelEndpointId` | CRUDL, stop, stop_all, log | add/list/remove/update/stop/stop-all | all but B; `recallCaptain` is a dead route | recall goes to `/stop` |
| Missions | CRUDL E B, cancel, purge, restart, retry-landing, status, diff, log, review, autoland, instructions, landing-preview, GitHub PR, history, summaries | most; no instructions, landing-preview, GitHub PR, history or summaries | L, summaries, R, C, U, transition, cancel, purge, restart, diff, log | create/list/show/cancel/restart/retry, diff, log | all but B (bulk purges one by one) | same |
| Voyages | CRL E B, cancel (DELETE), purge | dispatch, voyage_status, cancel, purge, delete_voyages | L, R, C, cancel, purge | create/list/show/cancel/retry | `getVoyageStatus` is a dead route | maps to `GET /voyages/{id}` |
| Personas | CRUDL E | CRUD, L via enum | R, C, U, D (no list) | none | CRUDL | CRUDL |
| Pipelines | CRUDL E | CRUD | R, C, U, D | none | CRUDL | CRUDL |
| Prompt templates | CRUL E, reset (no delete anywhere) | L, R, C, U, reset | R, U | none | full | full |
| Playbooks | CRUDL E | CRUD | none | add/list/remove/show | full | full |
| Docks | RL E D B, purge, repair, unstick | R, D, B, purge, repair, unstick | L | none | L, R, D | same plus listed exceptions |
| Signals | CRL E D B, read, recent, recipient | send, delete_signals only (no get, no mark-read) | L, send | none | L, R, C, B, read | same |
| Events | RL E D B | delete_event, delete_events (no get) | L | none | L, R, B | same |
| Merge queue | CRL E D, process (one or all), purge (one or batch) | full | L, R, enqueue, cancel, process | none | `processAllMergeQueue` and `cancelMergeEntry` are dead routes | remapped |
| Ask threads | full, including B | full, including `delete_ask_threads`; no thread CLI-permission policy tool | events only | none | full | full |
| Backlog / objectives | full, plus GitHub import and refinement sessions | full; no GitHub import, no refinement-session delete | none | backlog create/delete/list/reorder/show/update | full | full |
| Planning sessions | C, R, L, D, dispatch, messages, stop, stop-turn, summarize | create, get, dispatch only | none | none | full | full |
| Check runs | CRL E D, import, GitHub sync, retry | get, run, retry | none | none | full | full |
| Releases | CRUDL E, refresh, PRs | create, get only | none | none | full | full |
| Deployments | CRUDL E, approve, deny, verify, rollback | create, get, approve, verify, rollback (no deny, update or delete) | none | none | full | full |
| Environments | CRUDL E | nothing, not even enumerate | none | none | full | full |
| Incidents, runbook executions (U/D), workflow profiles, project profiles, skills | CRUDL E | via enum only (runbooks: get + start execution) | none | none | full | full |
| Memories | CRUDL | CRUD, search, enum | none | none | full | full |
| Model endpoints | CRUDL, validate, duplicate, health-check | full | none | none | full | full |
| Harbors | CRUDL, enable, disable, metrics, probe | all but probe | none | none | all but probe | all but probe |
| CLI permissions | requests L/R/decide, rules CRUDL | full, plus the prompt tool | list and decide requests only | none | full | full |
| Fleet actions | C, U, D, R, E, run, runs, targets | all but a get tool (enum covers it) | none | action list/run/status/cancel (no create/update/delete) | full | full |
| Jobs | L, R, cancel | via enum only (no cancel) | none | none | full | full |
| Credentials, users, tenants, push, settings, request history, history, workspace, storage, server ops | REST only | `stop_server`, backup/restore, `token_usage_summary` only | stop_server, backup, restore | server and config commands | full (push is mobile only) | full |
| Papercuts | none | `papercut_summary` | none | none | none | none |

### A. Functional gaps a user would hit

1. **Dashboard and mobile call four REST routes that do not exist (verified).** In `src/Armada.Dashboard/src/api/client.ts`:
   - `:961` `recallCaptain` posts `/api/v1/captains/{id}/recall` (used in `Captains.tsx:261`, `CaptainDetail.tsx:213`, mobile `useCaptainActions.tsx:90`).
   - `:1057` `getVoyageStatus` reads `/api/v1/voyages/{id}/status` (used in `Voyages.tsx:174`, mobile `VoyagesList.tsx`).
   - `:1089` `processAllMergeQueue` posts `/api/v1/merge-queue/process-all`; the server route is `POST /merge-queue/process` (used in `MergeQueue.tsx:169`).
   - `:1090` `cancelMergeEntry` posts `/api/v1/merge-queue/{id}/cancel`; the server cancels with `DELETE /merge-queue/{id}` (used in `MergeQueueDetail.tsx:100`, `MergeQueue.tsx:203`).
   - The .NET client already remaps these (`ArmadaClient.Captains.cs:135-145`, `ArmadaClient.MergeQueue.cs:79-100`, `ArmadaClient.Voyages.cs:60-70`), so the TUI works. Both parity manifests mark them "implemented" (`Tui/parity.json:1132,2564,3052,3100`; `Mobile/parity.json:1180,2604,3124,3172`), so no test caught it.
2. **CLI `armada reset` calls a route that does not exist (verified).** `DELETE /api/v1/server/shutdown` (`src/Armada.Helm/Commands/ResetCommand.cs:55`); the real route is `POST /api/v1/server/stop`. The exception is swallowed, so the server is never stopped through the API.
3. **Cancel never stops the agent (verified).** Every cancel path marks the mission Cancelled; none asks Harbor to kill the job (`HarborConnectionManager.KillJobAsync`, which `RemoteAgentRuntime` uses for a real stop). Most also clear the captain's `ProcessId`, after which `stop_captain` cannot stop it, and there is no route to kill a specific Harbor job.

   | Path | Releases the captain | Stops the process / Harbor job |
   |---|---|---|
   | REST `DELETE /api/v1/missions/{id}` (`MissionRoutes.cs:827`) | No (captain stays Working on a cancelled mission) | No |
   | MCP `cancel_mission` (`McpMissionTools.cs:233`), used in this incident | Yes, clears `ProcessId` | No |
   | WebSocket `cancel_mission` (`WebSocketCommandHandler.cs` ~600) | Yes, clears `ProcessId` | No |
   | REST voyage cancel (`VoyageRoutes.cs` ~310) | Yes, clears `ProcessId` | No |
   | MCP `cancel_voyage` (`McpVoyageTools.cs` ~280) | Yes, clears `ProcessId` | No |
4. **WebSocket `update_persona` drops the routing fields** (`WebSocketCommandHandler.cs:1084-1095`). Only `Description` and `PromptTemplateName` are applied. `DefaultCaptainId`, `CaptainPriorities`, `PriorityAvailability` and `PriorityExhaustion` are dropped without an error, and `LastUpdateUtc` is not set.
5. **WebSocket `create_persona` skips validation** (`:1078-1082`): no `PersonaCaptainPrioritySelector.Validate`, no `DefaultCaptainId` existence check, no TenantId, UserId or Scope. REST does all of these (`PersonaRoutes.cs:131-144`), and so does MCP (`McpPersonaTools.cs:73-86`).
6. **REST and MCP need different permissions for persona, pipeline and prompt-template writes.** REST allows any authenticated user and then limits a regular user to user-scoped objects (`RouteAuthorizationRegistry.cs:286-296`). MCP requires tenant admin (`McpToolAuthorizationRegistry.cs:179-184`).
7. **MCP cannot create personal personas, pipelines or playbooks.** `create_persona`, `create_pipeline` and `create_playbook` do not call `ScopedVisibility.ResolveCreateScope` and take no `scope`, so they always create tenant-wide objects. REST honors a requested scope (`PersonaRoutes.cs:144`, `PipelineRoutes.cs:133`, `PlaybookRoutes.cs:124`). Only the MCP prompt-template tools resolve scope (`McpPromptTemplateTools.cs:125,210`).
8. **MCP cannot attach a captain to a managed model endpoint.** `create_captain` and `update_captain` have no `modelEndpointId` (`McpCaptainTools.cs:118-163`). An MCP-created API-endpoint captain therefore never matches a persona priority that names an endpoint.
9. **MCP is missing operations that REST has:** `deny_deployment`; release update, delete and refresh; runbook create, update and delete; check-run delete, import and GitHub sync; Harbor probe; job cancel; get one event or signal; mark a signal read; refinement-session delete; planning-session list, message, stop and summarize; CLI permission policy for an Ask thread (`set_captain_cli_permission_policy` takes only `captainId`); captain chat; and environments entirely (though `create_deployment` accepts `environmentName`). Some may be intentional; the documented MCP exclusions (`docs/MCP_API.md:254-261`) do not list them.
10. **WebSocket has no `list_personas`, and its `enumerate` covers only 9 entity types** (`WebSocketCommandHandler.cs:986-1036`).

### B. Naming and shape inconsistencies

- Bulk-delete arguments: `delete_ask_threads` takes `threadIds` and `purge_merge_entries` takes `entryIds`; every other plural tool takes `ids`. REST always takes `{ Ids }`. `delete_playbook` takes `id`, while other single deletes take `<entity>Id`.
- Same action, different names: MCP `release_captain` is REST `POST /captains/{id}/unquarantine`. The TS client's `deleteMission` calls REST "Cancel a mission" (`DELETE /missions/{id}`). CLI `mission retry` re-creates the mission, which differs from MCP `retry_landing`.
- Deleting a built-in persona: REST returns 400 (`PersonaRoutes.cs:225`); MCP and WebSocket return Conflict (`McpPersonaTools.cs:187`).
- Missing `DefaultCaptainId`: REST returns 400; MCP returns NotFound.
- REST checks `DefaultCaptainId` without tenant scope (`_database.Captains.ReadAsync`, `PersonaRoutes.cs:131,175`); MCP checks within the caller's scope (`McpCallerScope.ReadCaptainAsync`, `:73,145`).
- Clearing `DefaultCaptainId`: REST clears only when the field is present and null or empty (`PersonaRoutes.cs:185`); MCP clears only on an empty string (`:153`).
- REST `POST /personas` keeps a client-sent `IsBuiltIn` (`PersonaRoutes.cs:128-146`), so a caller could create a persona that cannot be deleted. MCP never sets it.
- .NET `UpdatePersonaAsync` sends a full `Persona` with nulls omitted (`ArmadaJson.cs:54`); since `CaptainPriorities` is never null, every update replaces it, unlike the TS client's partial updates.
- Persona `Active` cannot be updated on any surface (`PersonaUpdateRequest` and `PersonaArgs` have no such field).
- `create_fleet` lacks `defaultPipelineId`; `add_vessel` lacks the autoLand* and definitionOfDone* fields that the update tools have.
- TS `restartCaptain` (`client.ts:999`) deletes and re-creates the captain: new ID, and `modelEndpointId` and `cliPermissionPolicy` are lost.

### C. Documentation drift

- `docs/REST_API.md:5975`, `:5990`, `:7991` say an omitted or null `DefaultCaptainId` on PUT clears it. The code (`PersonaRoutes.cs:182-186`) and the route's own OpenAPI description keep an omitted field and clear only on explicit null.
- `docs/WEBSOCKET_API.md:1072` describes `update_persona` as taking a "partial Persona", but only two fields are applied (A4).
- The exception reason for `POST /api/v1/missions/delete/multiple` in `ClientRouteSurfaceSuite.cs` says the dashboard deletes "with DELETE per mission"; it actually purges each through `DELETE /missions/{id}/purge` (`Missions.tsx:242`).

### Persona write operations

| | REST | MCP | WS |
|---|---|---|---|
| Create, update, delete | `POST /personas`, `PUT /personas/{name}`, `DELETE /personas/{name}` | `create_persona`, `update_persona`, `delete_persona` | create, update, delete |
| `DefaultCaptainId` | yes | yes | create only, not checked |
| `CaptainPriorities` | yes, validated | yes, validated | create only, not validated |
| `PriorityAvailability`, `PriorityExhaustion` | yes | yes | create only |
| Scope | yes (admins can change it on update) | no | no |
| Permission level | Authenticated | TenantAdmin | Global admin |

### Bulk-delete coverage

| Entity | REST `delete/multiple` | MCP plural tool | Single delete on MCP | TS / .NET bulk |
|---|---|---|---|---|
| Ask threads (added 2026-10-24) | yes | `delete_ask_threads` (`threadIds`) | yes | yes / yes |
| Captains | yes | yes | yes | no (listed exception) |
| Docks | yes | yes | yes | no (listed exception) |
| Events | yes | yes | yes | yes |
| Fleets | yes | yes | yes | yes |
| Missions | yes | yes | cancel and purge only | no (purges one by one) |
| Signals | yes | yes | no single delete | yes |
| Vessels | yes | yes | yes | no (listed exception) |
| Voyages | yes | yes | cancel and purge only | no (listed exception) |
| Request history | yes, plus delete by filter | none | none | yes |
| Token usage | delete by filter only | none | none | no |
| Merge queue | `POST /merge-queue/purge` | `purge_merge_entries`, `purge_merge_queue` | yes | no |

WebSocket and the CLI have no bulk deletes. Personas, pipelines, objectives, memories and the remaining entities have only single deletes on every surface.

### Known intentional exceptions (reasons quoted from the code)

- `ClientRouteSurfaceSuite.cs:29-36` (requests outside the Admiral's routes): `/dashboard/i18n/armada.json` ("Static i18n catalog..."); three `/proxy-api/v1/...` routes ("Armada.Proxy route").
- `ClientRouteSurfaceSuite.cs:40-79`, 41 routes with no .NET client method:
  - Every `POST .../enumerate` except Ask threads: "Enumerate twin of the GET list; the dashboard and the client use the GET list with query parameters".
  - Captain, dock, vessel and voyage bulk deletes: "Batch delete not offered by the dashboard...".
  - Mission bulk delete: "Batch delete not used by the dashboard (bulk delete calls DELETE per mission)" (inaccurate; see C).
  - Dock purge, repair and unstick; merge-entry purge; batch merge purge; `evaluate-autoland`: "Operator recovery / also the MCP tool ...; no dashboard function".
  - `DELETE /events/{id}`, `DELETE /signals/{id}`: "Single delete; the dashboard deletes through .../delete/multiple".
  - Credentials, tenants and users by ID: "The dashboard reads ... from the list".
  - Signals `recent` and `recipient`: "Captain-facing feed".
  - `GET /token-usage`: "Raw token-usage records; the dashboard uses the summary". Token-usage delete by filter: "Token-usage purge; no dashboard function".
  - `PATCH /vessels/{id}/context`: "Context patch (the MCP tool update_vessel_context)...".
  - Harbor probe: "Harbor probe; no dashboard function". `POST /onboarding`: "Self-registration (sign-up) flow; the dashboard and TUI only sign in". `GET /swagger`: "Swagger UI (HTML); the API Explorer reads /openapi.json".
- `ClientCoverageSuite.cs:32-43`: server stop, restart, reset, rebuild, rollback and restore are "Destructive"; synthetic activity, synthetic token-usage import and opening a workspace terminal are covered by other suites.
- `ApiContractSuite.cs:40-43`: `stop_server`, `backup` and `restore` are not sent by the dispatch check because "they stop the server or touch backup files".
- `docs/MCP_API.md:254-261`: MCP deliberately does not expose workspace files, standalone planning-session transcripts, request history, GitHub import, sync and PR helpers, Mux endpoint discovery, or OpenAPI.
- `WebSocketCommandHandler.cs:76-90`: "every command, including reads, requires a global administrator; tenant admins and users use the tenant-scoped REST API instead".
- Parity manifests: `Tui/parity.json` has 5 "extension" entries and no gaps. `Mobile/parity.json` has 22 "not-applicable" entries (11 `/objectives` aliases: "no dashboard page calls it: the backlog pages use the /backlog aliases"; enumerate twins; `getEntity`; `importCheckRun`; `openWorkspaceTerminal`; `settings.requireHarborForLaunch`) and 12 "extension" entries (push devices, proxy sign-in).

### Not determined

- Runtime behavior for unverified items comes from reading code, not from running it.
- Mobile code that calls the API outside the shared dashboard client was not fully audited.
- WebSocket event payloads were not compared field by field with the REST models.

## Proposed fix order

1. **Cancel stops the Harbor job.** One shared cancel method in the core mission service that every surface (REST, MCP, WebSocket; mission and voyage) calls: ask Harbor to kill the job, wait for its exit confirmation, release the captain, then mark the mission Cancelled. Add a test per surface, and a route or tool to kill an orphaned Harbor job.
2. **Fix the four dead dashboard calls and CLI `reset`.** Make the parity tests check that each client call's route exists in the API surface baseline, so a dead route cannot be marked "implemented" again.
3. **Bring WebSocket persona writes and the MCP captain and persona tools in line with REST** (routing fields, validation, scope, `modelEndpointId`, error codes, `IsBuiltIn`), and correct the docs in section C.
4. **Stop the duplicate relaunch.** Guard resume-in-place so one mission cannot be relaunched twice on the same dock, and stop classifying a stage-check exit as a "server/Harbor restart".
5. **Fix the vessel's stage-check timeout** so it covers the command it runs, or run a narrower check.
6. **Persona priority editor:** warn on save when a runtime is chosen but not added.

## Open decisions

- [ ] Restart Harbor (PID 14090) to end the orphaned DeepSeek agent loop for the cancelled mission?
- [ ] Which fixes to start on, and in what order (proposed order above)?
- [ ] Should the MCP gaps in A9 be filled, or added to the documented MCP exclusions?
- [ ] Should REST and MCP use the same permission level for persona, pipeline and prompt-template writes (A6), and which one?
