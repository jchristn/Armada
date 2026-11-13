# Simulated user session: usage-based captain selection (v1.3.0)

- **Date:** 2026-11-04 (session 16:51 to 17:10 UTC)
- **Requirements followed:** `~/Code/Agents/requirements/SIMULATED_USER_TESTING.md` (observe and report only; no product changes)
- **Evidence:** request/response transcripts, Helm transcripts, newman output and Dashboard screenshots under `/private/tmp/claude-501/-Users-joelchristner-Code-Armada/c46a1e34-e847-452e-9a51-f17423999359/scratchpad/simuser/` (screenshots in its `shots/` folder). Evidence file names are cited per finding. Nothing was committed.

## Environment

| Item | Value |
|---|---|
| Host | macOS (Darwin 25.6.0), loopback only |
| Admiral | v1.3.0 from `feature/v1.3.0`, REST `http://127.0.0.1:25170`, MCP `http://127.0.0.1:25171/mcp`, Dashboard `/dashboard/` |
| Database | SQLite in the isolated data directory `benchmarks/.run/sim` |
| Captains | `ClaudeCode` runtime backed by a stub `claude` that waits about 3 seconds and exits 0 without committing, so every mission ended `Failed` (Unfinished, no commit) after one no-op redispatch. This is a harness artifact and was not evaluated as a finding. |
| Usage | `Custom` capture mode running `benchmarks/stub/usage.py` (one fixed five-hour window; the stub reports a reset three hours after each capture, so the reset time moves with every capture) |
| Starting policy | Mode `Observe`, revision 0. The harness settings file had already set `Strategy` to `MostRemainingAllowance` and `SettlementLagSeconds` to 0, so those values are not the shipped defaults. |
| Not available | Grafana/Prometheus (telemetry disabled in the harness settings), Harbor, Mobile app. The harness owner restarted the Admiral at about 16:55 UTC; that was not product behavior. |
| Setup time | About 5 minutes from first request to the first dispatched mission. |

## Users simulated

| User | How obtained | Used for |
|---|---|---|
| Global administrator | Bearer token `default`; Dashboard sign-in `admin@armada` / `password` per README, then "Skip for now" and "Skip and continue" (password not changed) | All journeys |
| Tenant administrator `tadmin@sim.test` | `POST /api/v1/users` with `IsTenantAdmin: true`, then `POST /api/v1/authenticate`, token sent as `X-Token` | Recovery and settings permission checks |
| Viewer `viewer@sim.test` | `POST /api/v1/users` with no admin flags, then `POST /api/v1/authenticate` | Read, preview, recovery and settings denial; Dashboard as viewer |

## Fixtures created

- Vessels `sim-repo`, `sim-repo-c1`, `sim-repo-c2`, `sim-repo-c3` (all pointing at the local bare repository).
- Account `acct-a` at 88% used (constrained, 12% headroom): captains `con-a1`, `con-a2` (con-a2 deleted later to check retained names).
- Account `acct-b` at 40% used (healthy): captain `healthy-b`.
- Account `acct-c` at 87% used (room for exactly one 2% reservation above the 10% reserve): captains `tight-c1`, `tight-c2`, `tight-c3`.
- 10 missions: Observe auto-routed, Enforce auto-routed, three pinned to `con-a1` (fallback grant, reserve exception, permission checks), three concurrent missions pinned to the three `acct-c` captains.

## Summary

| Severity | Count |
|---|---|
| S1 | 0 |
| S2 | 4 |
| S3 | 11 |
| S4 | 3 |

| Category | Count |
|---|---|
| Defect | 8 |
| Documentation | 3 |
| Friction | 5 |
| Nonconformance | 1 |
| Question | 1 |

The three that matter most:

1. **UT-001:** an empty, malformed or wrongly typed JSON body on the selection POST routes (recovery, history enumerate, candidates enumerate) returns HTTP 500 with an HTML page.
2. **UT-002:** sending a partial `UsageSelection` group (for example only `Mode`) through `PUT /api/v1/settings` returns 200 and silently resets the omitted fields to defaults. In this session the strategy changed from Most remaining allowance to First available without any warning.
3. **UT-004:** estimated reservations on accounts shared by several captains did not settle after fresh captures, while the single-captain account settled on its first capture. Missions on the shared accounts kept waiting with `BelowReserve` and a recheck hint three hours away.

The core journeys worked. Observe was the default, Enforce chose the healthy account, a pinned constrained captain waited on the correct gate, fallback and reserve-exception grants admitted work with retained audit records, only one of three concurrent missions started on the tight account, and switching back to Observe released pending work.

## Coverage

"Yes" means exercised in this session. "No" gives the reason.

| Feature | REST | MCP | Helm | Dashboard | TUI |
|---|---|---|---|---|---|
| Observe is the default (settings read) | Yes | Yes | No (no Helm settings command used) | Yes (after restore) | No (needs an interactive terminal) |
| Switch to Enforce with expected revision; stale revision 409 | Yes | Yes (stale) | No | Yes (stale conflict keeps edit) | No |
| Settings validation (bad enum, bounds, wrong type, malformed, partial group) | Yes | No | No | No | No |
| Mission selection summary | Yes | Yes | Yes | Yes | No |
| History, outcome filter, paging | Yes | Yes | Yes | Yes | No |
| Decision detail, candidates, candidates enumerate | Yes | Yes | Yes (`--decision`) | Yes (comparison) | No |
| Preview (Pending and non-Pending) | Yes | Yes | Yes | Yes | No |
| `enumerate` types `captain_selection_decision` / `_candidate` | n/a | Yes | n/a | n/a | n/a |
| Enforce prefers the healthy account | Yes | Yes (read) | Yes (read) | Yes (read) | No |
| Pinned constrained captain waits; gate named | Yes | Yes | Yes | Yes | No |
| Recovery: grant fallback | No | No | Yes | No | No |
| Recovery: reserve exception | Yes | No | No | No | No |
| Recovery: missing/blank/oversized reason, stale revision | Yes | Yes | Yes | Yes (Save disabled; conflict) | No |
| Recovery as viewer (403) and tenant admin (allowed) | Yes | No | No | Yes (viewer mission view) | No |
| Concurrent admissions on one tight account | Yes | n/a | n/a | n/a | n/a |
| Reservation estimates separate from observed usage | Yes | No | No | Yes (Captain Usage) | No |
| Settlement after fresh capture | Yes | n/a | n/a | Yes | No |
| Deleted captain name retained | Yes | No | Yes | No | No |
| Error quality: unknown IDs, bad bodies, unknown options | Yes | Yes | Yes | n/a | No |
| Docs examples and Postman folder run as written | Yes (newman) | n/a | n/a | n/a | n/a |
| Switch back to Observe | Yes | No | No | Yes (verified) | No |
| Telemetry (`armada.selection.*`, Grafana dashboard) | No: telemetry disabled and Grafana not running in the harness |
| Needs You `selection_blocked` | No: only reserve waits occurred, which the docs exclude, and none reached 15 minutes |
| Mobile | No: not available in this harness |

## Findings

### UT-001: Empty, malformed or wrongly typed JSON on selection POST routes returns HTTP 500 HTML

- **Severity:** S2
- **Category:** Defect
- **Surface:** REST API
- **User:** Global administrator
- **Oracle:** Crash; REST_API.md Captain Selection status table (400 for malformed values)
- **Frequency:** Always (5 of 5 bodies, 3 routes)

**Steps**
1. `POST /api/v1/missions/msn_mw2cxb0x_xpCDV16gLLe/captain-selection/recovery` with an empty body.
2. Repeat with `{"expectedRevision":` and with `{"expectedRevision":"zero","allowFallback":true,"reason":"x"}`.
3. `POST /api/v1/missions/{id}/captain-selection/history/enumerate` with `{"PageSize":`.
4. `POST /api/v1/captain-selection-decisions/{id}/candidates/enumerate` with `{`.

**Expected:** 400 with an `ApiErrorResponse` naming the problem, as `PUT /api/v1/settings` does for the same malformed input.
**Actual:** HTTP 500 with an HTML body titled "It's me, not you". An empty body on history enumerate is accepted (200), but on recovery it is a 500.

**Evidence:** `ev-rest-recovery-bad.txt`, `ev-rest-bad-inputs.txt`

```
$ empty body
<html> <head> <title>It's me, not you</title> ... <h2>Internal server error</h2>
[HTTP 500]
```

**Suggested direction:** Treat a JSON parse failure on these routes as 400, the same way the settings route already does.

### UT-002: A partial UsageSelection group silently resets omitted fields to defaults

- **Severity:** S2
- **Category:** Defect (with a documentation gap)
- **Surface:** REST API
- **User:** Global administrator
- **Oracle:** REST_API.md `PUT /api/v1/settings` ("Accepts partial updates ... omitted fields are unchanged"); the user's expectation
- **Frequency:** Always (1 of 1)

**Steps**
1. Policy at revision 1: Enforce, `MostRemainingAllowance`, `SettlementLagSeconds` 0.
2. `PUT /api/v1/settings` with `{"UsageSelection":{"Mode":"Enforce"},"ExpectedUsageSelectionRevision":1}`.
3. `GET /api/v1/settings`.

**Expected:** Either the omitted fields stay as they were, or the request is rejected because the group must be complete. The PUT section of REST_API.md does not list `UsageSelection` among the editable fields at all; only MCP_API.md says "complete group".
**Actual:** 200, revision 2, and `Strategy` became `FirstAvailable` and `SettlementLagSeconds` became 60. Nothing in the response says that other values changed. An operator who sends only the mode switches strategy without noticing.

**Evidence:** `ev-settings-bad.txt`

```
$ partial group {Mode:Enforce}  ... [HTTP 200]
{'Strategy': 'FirstAvailable', 'Mode': 'Enforce', ... 'SettlementLagSeconds': 60, ...} 2
```

**Suggested direction:** Reject an incomplete group with 400 naming the missing fields, or merge onto the current values. Document the `UsageSelection` group in the PUT section.

### UT-003: Unknown or wrong-case history filters return an empty page instead of 400

- **Severity:** S2
- **Category:** Defect
- **Surface:** REST API, MCP
- **User:** Global administrator
- **Oracle:** REST_API.md Captain Selection status table ("400: Malformed paging or filter values")
- **Frequency:** Always

**Steps**
1. Mission `msn_mw2cxb0x_xpCDV16gLLe` has 2 Waiting decisions.
2. `GET .../captain-selection/history?outcome=waiting` (lowercase), then `?outcome=Bogus`, then `?trigger=Nope`.
3. MCP `get_captain_selection_history` with `query.outcome = "Bogus"`.

**Expected:** 400 for a value that is not `Admitted` or `Waiting`, or case-insensitive matching.
**Actual:** 200 with `totalRecords: 0` in every case. A user who types `waiting` concludes the mission has no waits. Helm validates `--outcome` locally, so only REST and MCP callers are affected.

**Evidence:** `ev-rest-bad-inputs.txt`, `ev-mcp-errors.txt`

```
$ history?outcome=waiting
{"success":true,"pageNumber":1,"pageSize":25,"totalPages":0,"totalRecords":0,"objects":[],"totalMs":0}
```

### UT-004: Reservations on shared accounts do not settle after fresh captures

- **Severity:** S2
- **Category:** Defect (suspected; see the caveat)
- **Surface:** REST API, Dashboard (Captain Usage), mission selection waits
- **User:** Global administrator
- **Oracle:** ENFORCEMENT_AND_MISSION_INSPECTION.md (ended work settles on a verified active capture that began after execution end plus the lag); plan section 14 step 6; the product contradicting itself (the single-captain account did settle)
- **Frequency:** 2 of 2 shared accounts; 0 of 1 single-captain accounts

**Steps**
1. `SettlementLagSeconds` is 0. All missions on `acct-a` (`con-a1`, `con-a2`) ended by 16:56:58Z. The only `acct-c` admission (`tight-c3`) ended at about 16:58:25Z.
2. Captures: `acct-a` at 16:57:20Z (scheduled), 17:02:34Z (manual) and 17:08:00Z (manual); `acct-c` at 17:03:01Z (scheduled) and 17:08:00Z (manual, through `tight-c3`, the captain that ran).
3. `GET /api/v1/usage/latest` after each capture.

**Expected:** The estimate settles once a fresh active capture follows the end of execution, as it did for `acct-b`. `acct-b` (single captain) went from 8% unsettled to none at its first capture after execution (16:58:01Z).
**Actual:** `acct-a` kept `UnsettledPercent: 8` and `acct-c` kept 2% through every capture up to 17:08:25Z. Missions pinned to these accounts kept waiting with `BelowReserve` and a recheck hint at the window reset, about three hours away. The Dashboard showed "8% awaiting settlement (not provider usage)".

**Evidence:** `ev-settlement.txt`, `ev-settlement2.txt`, `shots/60-usage.png`

```
cpt_mw2cu7xy_y35GPb7iAe9 2026-11-04T17:08:00Z 88 ... [{'ActivePercent': 0, 'UnsettledPercent': 8}]
cpt_mw2d1jie_OcMAeO0rAfX 2026-11-04T17:08:00Z 87 ... [{'ActivePercent': 0, 'UnsettledPercent': 2}]
cpt_mw2cu7yv_V81nDNwoX4R 2026-11-04T17:02:41Z 40 ... []
```

**Caveat:** Both unsettled accounts also had a Pending mission at the time, and the stub reports a new reset time on every capture. The cause could be the shared account, the pending work, or the moving fixed-window generation. The server log has no reconciliation lines that explain it.

### UT-005: Admission reasons claim "selected for headroom" when routing, fallback or a reserve exception chose the captain

- **Severity:** S3
- **Category:** Defect
- **Surface:** REST API, MCP, Helm, Dashboard
- **User:** Global administrator
- **Oracle:** Another surface; the product contradicting itself
- **Frequency:** Always (every Admitted decision in this session)

**Steps**
1. In Observe, dispatch `msn_mw2cumjw_JzpCnH8kWhd`. Legacy routing picks `con-a1` (12% headroom, rank 2). The strategy proposes `healthy-b`.
2. Read the summary, history and decision through REST, Helm and the Dashboard.
3. In Enforce, admit a pinned mission through fallback (`csd_mw2cyuzs_6E9fDZlj1Cp`) and another through a reserve exception (`csd_mw2czy92_9WPRlBMi12d`).

**Expected:** The decision reason says what actually happened: routing order in Observe, an authorized fallback, or an authorized reserve exception.
**Actual:** Every Admitted decision has `reasonCode: "SelectedForHeadroom"`. The Observe redispatch to `con-a2` has that reason even though its candidate row says `BelowReserve`. The summary text in Helm and the Dashboard says "The current routing chose this captain", while the history rows on the same screen say "Selected by the configured strategy." Only the candidate rows show `AuthorizedReserveException`.

**Evidence:** `ev-m1-history.txt`, `ev-m1-d2-cands.txt`, `ev-helm-variants.txt`, `shots/30-observe-History.png`, `ev-m3-fallback-decision.txt`

### UT-006: Selection routes use a different JSON casing and error envelope from the rest of the API

- **Severity:** S3
- **Category:** Friction (convention)
- **Surface:** REST API, MCP, Helm
- **User:** Global administrator
- **Oracle:** Convention; another surface
- **Frequency:** Always

**Steps**
1. Compare `GET /api/v1/missions/{id}/captain-selection/history` with `GET /api/v1/missions`.
2. Compare a 404 from `GET /api/v1/captain-selection-decisions/csd_nope` with the 404 from `DELETE` on the same family of paths.
3. Call MCP `get_captain_selection` and Helm `--json` for the same mission.

**Expected:** One casing and one error shape across REST, as on every other route (`Success`, `Objects`, `{"Error":"NotFound","StatusCode":404,...,"Data":{...}}`).
**Actual:** Selection REST bodies are camelCase (`success`, `objects`, `latestDecision`) and errors are `{"error":404,"statusCode":404,...,"data":null}` with a numeric `error` and no `Data.Code`. MCP and Helm `--json` return the same data in PascalCase. Helm prints the lowercase error JSON verbatim on a 409. Settings conflicts carry `Data.Code: SettingsRevisionConflict`, but recovery conflicts have `data: null`. Clients need two deserializers and two error parsers.

**Evidence:** `ev-m1-history.txt`, `ev-rest-bad-inputs.txt`, `ev-mcp-reads.txt`, `ev-helm-recovery1.txt`

### UT-007: The Postman Captain Selection folder fails 2 of 25 assertions as written

- **Severity:** S3
- **Category:** Documentation
- **Surface:** Postman collection (`Armada.postman_collection.json`)
- **User:** Global administrator
- **Oracle:** The product's own documentation (ENFORCEMENT_AND_MISSION_INSPECTION.md says the collection includes reusable requests with compiled assertions)
- **Frequency:** Always

**Steps**
1. `newman run Armada.postman_collection.json --folder "Captain Selection"` with environment variables `baseUrl=http://127.0.0.1:25170`, `bearerToken=default`, `missionId`, `selectionDecisionId`, `selectionRecoveryRevision=2`.

**Expected:** All assertions pass.
**Actual:** "Read Selection Settings and Revision" asserts `body.usageSelection`, but the settings response is PascalCase (`UsageSelection`), so it fails and never stores `usageSelectionRevision`. "Update Selection Settings" then sends `"ExpectedUsageSelectionRevision": ,` and gets 400. The other 8 requests pass.

**Evidence:** `ev-newman.txt`

```
1. AssertionError  Selection group, revision and capabilities  expected undefined to be an object
2. AssertionError  Expected status  expected response to have status code 200 but got 400
```

### UT-008: A pinned constrained mission's wait does not point to the available next step

- **Severity:** S3
- **Category:** Friction
- **Surface:** REST API (mission `AssignmentBlocker`), Helm, Dashboard
- **User:** Global administrator
- **Oracle:** Plan section 14 step 4 ("names the correct gate, offers authorized next steps"); the user's expectation
- **Frequency:** Always (3 pinned missions)

**Steps**
1. Enforce. Create a mission with `RequestedCaptainId` set to `con-a1` (acct-a below the reserve) while `healthy-b` is idle with 56% headroom.
2. Read `GET /api/v1/missions/{id}` and the selection summary and candidates.

**Expected:** The wait names the reserve gate (it does) and says that an administrator can allow fallback to an eligible captain, or use a reserve exception.
**Actual:** `AssignmentBlocker` says "No captain currently satisfies the configured usage policy." with `Advice: []` and `Captains: []`, although `healthy-b` satisfies the policy. The decision reason is only `BelowReserve`. The fallback hint appears only per candidate, and those candidates show "Effective headroom: Unknown" even though `healthy-b` had a fresh 60% reading. The create response's warning says only "could not be assigned to any captain" (see UT-014).

**Evidence:** `ev-m3-summary.txt`, `ev-m3-preview.txt`, `ev-helm-recovery1.txt`, `shots/30-pending-Compare_captains.png`

### UT-009: Page-size validation differs between history and preview

- **Severity:** S3
- **Category:** Defect
- **Surface:** REST API
- **User:** Global administrator
- **Oracle:** REST_API.md (`pageSize` 1 to 1000; 400 for malformed paging); the product contradicting itself
- **Frequency:** Always

**Steps**
1. `GET .../captain-selection/history?pageSize=0`, then `?pageSize=1001`; `POST .../history/enumerate` with `{"pageSize":5000}`.
2. `POST .../captain-selection/preview?pageSize=0`.

**Expected:** The same rule on every selection route.
**Actual:** History silently clamps (0 becomes 1, 1001 and 5000 become 1000) and returns 200. Preview returns 400 "Page number must be positive and page size between 1 and 1000."

**Evidence:** `ev-rest-bad-inputs.txt`

### UT-010: Helm errors are generic and are written to stdout

- **Severity:** S3
- **Category:** Nonconformance
- **Surface:** Helm CLI
- **User:** Global administrator
- **Oracle:** SIMULATED_USER_TESTING.md CLI guidance (results to stdout, diagnostics to stderr); useful-error requirement
- **Frequency:** Always

**Steps**
1. `armada mission selection msn_nope`, then `armada mission selection <mission> --decision csd_nope`.
2. Re-run the first with `2>/dev/null`.

**Expected:** "Mission msn_nope was not found" (the server already says "Mission not found."), on stderr, with a non-zero exit code.
**Actual:** `Error: Response status code does not indicate success: 404 (Not Found).` printed on stdout (it still shows with stderr discarded). The exit code is 255, which is correct. A 409 on `selection-recovery` prints the raw server JSON.

**Evidence:** `ev-helm-variants.txt`, `ev-helm-recovery1.txt`

### UT-011: Settings validation errors expose .NET type names and do not list valid values

- **Severity:** S3
- **Category:** Defect
- **Surface:** REST API
- **User:** Global administrator
- **Oracle:** Useful-error requirement (no internals; say how to fix)
- **Frequency:** Always

**Steps**
1. `PUT /api/v1/settings` with `UsageSelection.Mode: "Bogus"`, then `ReservePercent: "ten"`, then `ExpectedUsageSelectionRevision: "x"`.

**Expected:** "Mode must be Disabled, Observe or Enforce" and similar messages, with `Data.Field`.
**Actual:** `The JSON value could not be converted to Armada.Core.Enums.UsageSelectionModeEnum. Path: $.UsageSelection.Mode | LineNumber: 0 | BytePositionInLine: 69.` and ``System.Nullable`1[System.Int64]``. A range violation, by contrast, is clear ("Comfortable headroom must exceed the protected reserve", with `Data.Field`).

**Evidence:** `ev-settings-bad.txt`

### UT-012: Selection docs contradict the shipped behavior

- **Severity:** S3
- **Category:** Documentation
- **Surface:** Docs
- **User:** Global administrator
- **Oracle:** The product's own documentation versus behavior
- **Frequency:** Always

**Details**
- `LIVE_CONFIGURATION_AND_OBSERVE.md` still says "Enforce remains unavailable; the server refuses live requests to enable it", "Choose Disabled or Observe", and that "Generic MCP enumerate integration and Helm/native client controls remain in the plan". All three are now false. `MCP_API.md` links this file as "contracts, scope and examples". A banner points to the newer guide, but the body still reads as current.
- The REST_API.md `PUT /api/v1/settings` section lists every editable group except `UsageSelection` and `ExpectedUsageSelectionRevision`. They appear only in the v1.3.0 note at the top of the file.
- The REST example summary omits fields that are always returned (`trigger`, `mode`, `strategy`, `executionAttemptId`, `selectedCaptainAvailable`, `algorithmVersion`, `policyRevisionId`, `nextReconsiderUtc`), and it says `hypotheticalCaptainId` is carried by Observe decisions only. The field is present (null) on every decision.

**Evidence:** `ev-settings-enforce.txt` (Enforce accepted), `ev-mcp-reads.txt` (enumerate works), `ev-helm-summary.txt`

### UT-013: A viewer opening Server settings sees a load failure instead of a read-only or permission state

- **Severity:** S3
- **Category:** Friction
- **Surface:** Dashboard
- **User:** Viewer
- **Oracle:** LIVE_CONFIGURATION_AND_OBSERVE.md (non-admin read-only behavior covered by UI tests); convention
- **Frequency:** Always

**Steps**
1. Sign in as `viewer@sim.test`, open `/dashboard/server`.

**Expected:** Either the Captain selection policy shown read-only, or a clear "requires a global administrator" message.
**Actual:** A red "Error: Failed to load server settings. Health data is available, but configuration and backup sections could not be loaded." The API returns 403 for a viewer's `GET /api/v1/settings`, so the page treats a permission boundary as a failure.

**Evidence:** `shots/70-viewer-server.png`, `ev-perms.txt`

### UT-014: Mission create changes its response shape when the mission waits

- **Severity:** S3
- **Category:** Documentation
- **Surface:** REST API
- **User:** Global administrator
- **Oracle:** REST_API.md `POST /api/v1/missions` (201 with a Mission body)
- **Frequency:** Always when the mission cannot be assigned at once

**Steps**
1. Enforce. `POST /api/v1/missions` pinned to a constrained captain.

**Expected:** The documented Mission object, or a documented wrapper.
**Actual:** `201 {"Mission":{...},"Warning":"Mission created but could not be assigned to any captain. It will be retried on the next health check cycle."}`. A script that reads `Id` from the documented shape fails. During this session that happened and the retry created a duplicate mission. The warning does not mention that captain selection is waiting, or why.

**Evidence:** `ev-m3-create.txt`, `conc-1.txt`

### UT-015: The default tenant's timestamps are eight hours in the future

- **Severity:** S3
- **Category:** Defect
- **Surface:** REST API
- **User:** Global administrator, viewer (`whoami`)
- **Oracle:** The product contradicting itself
- **Frequency:** Always

**Steps**
1. `GET /api/v1/tenants/default` and `GET /api/v1/whoami` at 16:57Z.

**Expected:** About 16:50:46Z, the same as the `default` user created in the same first start.
**Actual:** `"CreatedUtc":"2026-11-05T00:50:46.000000Z"`. This is outside the selection feature, but any audit that uses tenant timestamps is affected.

**Evidence:** `ev-perms.txt`

### UT-016: Helm selection output has small rendering problems

- **Severity:** S4
- **Category:** Friction
- **Surface:** Helm CLI
- **Oracle:** Convention
- **Frequency:** Always

**Details**
- `--preview` on a non-Pending mission prints "Only Pending missions can preview a new selection." followed by "Page 1 of 0 · 0 total records · 0.0ms" and an empty table.
- Every table footer shows `0.0ms`.
- `selection-recovery --help` lists `--page` and `--page-size`, which have no effect on that command.

**Evidence:** `ev-helm-variants.txt`, `ev-helm-recovery1.txt`

### UT-017: Dashboard polish in the selection and usage views

- **Severity:** S4
- **Category:** Friction
- **Surface:** Dashboard
- **Oracle:** Convention; another surface
- **Frequency:** Always

**Details**
- Times appear as local time without a zone ("11/4/2026, 8:52:45 AM"), while Helm prints UTC ("2026-11-04 16:52:45Z") for the same decision.
- The Settings card titled "Captain selection" contains a fieldset whose legend is also "Captain selection".
- The Captain Usage table lists captain IDs, not names, and repeats "Allow interactive terminal (PTY) fallback" as both the label and the help text.
- In Compare captains, the "Proposed" state has no badge styling, while Blocked and Selected do.

**Evidence:** `shots/10-settings-selection.png`, `shots/30-observe-Compare_captains.png`, `shots/60-usage.png`

### UT-018: Should a no-op redispatch be recorded as a new Dispatch decision?

- **Severity:** S4
- **Category:** Question
- **Surface:** REST API, history views
- **Oracle:** The simulated user's expectation

**Details:** Every mission that ran produced two Admitted decisions about four seconds apart, both with `trigger: "Dispatch"`. The second came from Armada's automatic no-op redispatch. In Enforce, the second attempt consumed another 2% reservation, and in one case it was blocked by the first attempt's own unsettled reservation (`msn_mw2d1tml_vHYLGEVgD3s`). History cannot tell a user that the second row was an automatic retry. Is a distinct trigger such as `Redispatch` intended?

**Evidence:** `ev-m1-history.txt`, `ev-concurrency.txt`

## Things that worked

- Observe was the default mode at revision 0, and the Dashboard, REST and MCP agreed on mode, strategy, revision and capabilities.
- The revision check behaved well: a stale or replayed `ExpectedUsageSelectionRevision` gave 409 `SettingsRevisionConflict` with no change. The Dashboard kept the unsaved strategy choice after a conflict and asked for a refresh.
- Observe kept legacy routing (`con-a1`) and recorded the hypothetical choice (`healthy-b`) with ranked candidates, bands, headroom and the recorded policy.
- In Enforce, auto-routing chose `healthy-b` on the healthy account, and the candidate rows explained every blocked captain.
- The pinned constrained captain waited with `BelowReserve`, and the other captains showed `FallbackPermissionRequired`. Granting fallback through Helm admitted the mission to `healthy-b` within about 10 seconds. A reserve exception through REST admitted `con-a1` with `AuthorizedReserveException`. Both decisions keep the exact recovery revision, actor, reason and time.
- Recovery guards held: a missing, blank or 513-character reason gave 400 (REST), a local Helm error, and a required-property error in MCP. A stale revision gave 409 everywhere. A viewer got 403. A tenant administrator could save recovery but not settings. Viewers got 404 for other users' missions and decisions. The Dashboard disabled Save until a reason was entered and kept the draft reason on conflict.
- Concurrency: of three simultaneous missions on one account with room for one reservation, exactly one started. The other two waited with `BelowReserve`.
- `GET /api/v1/usage/latest` and the Dashboard Captain Usage page show reservation estimates separately from observed provider usage ("Estimated Armada reservations: 0% running, 8% awaiting settlement (not provider usage)").
- A deleted captain's name stays readable in history (`selectedCaptainAvailable: false`).
- Helm strict parsing rejects unknown options and stray arguments with a pointer to the bad token, enforces mutually exclusive modes and the outcome and page bounds, and `--json` output parses.
- MCP lists all seven selection tools, and `enumerate` supports both new entity types with a clear error when `missionId` is missing.
- Usage capture returns 202 with a JobId, joins a capture already in flight for a shared account, and returns 429 with `Retry-After` during cooldown.
- Switching back to Observe at the end released the waiting missions under legacy routing. The final policy matches the starting group exactly (revision 5).

## Disposition (fix iteration, 2026-11-04)

The session itself changed nothing. Fixes were made afterwards in a separate iteration and are covered by new tests.

| Finding | Disposition |
|---|---|
| UT-001 | Fixed. Malformed, empty or wrongly typed JSON on every selection POST route returns 400 with an `ApiErrorResponse` that says what the body must contain (`E2E.CaptainSelectionApi` malformed_inputs_filters_and_partial_settings). |
| UT-002 | Fixed. A `UsageSelection` group sent to `PUT /api/v1/settings` changes only the members sent; omitted members keep their current values. Out-of-range values return 400 and change nothing. REST_API.md documents the group. MCP `update_usage_selection_settings` still takes the complete group, as MCP_API.md states. |
| UT-003 | Fixed. History `outcome` and `trigger` accept any letter case and reject unknown values with 400 on REST and MCP; a `from` that is not earlier than `to` is rejected. |
| UT-004 | Fixed (root cause found). The usage stub reports whole-second `capturedUtc`, so the observation time was up to a second before the probe start and the reconciler's `ProbeStartedUtc <= CapturedUtc` rule rejected the proof. The single-captain account settled only because of sub-second timing. Both reconcilers now allow one second of timestamp resolution; the capture and probe must still both start after the execution ended (`Database.CaptainSelection` whole_second_capture_timestamps_settle_ended_work). Shared accounts were not the cause. A related open question: reservations of a deleted captain cannot settle (its configuration cannot be verified) and become uncertain after `MaximumUnsettledSeconds`. |
| UT-005 | Fixed. Admitted decisions record `SelectedByRouting` in Observe, `AuthorizedReserveException` or `AuthorizedFallback` when a recovery permission was used, and `SelectedForHeadroom` otherwise, with translations in all eight locales (`Services.UsageSelectionReason`). |
| UT-007 | Fixed. The two settings requests read PascalCase settings members. |
| UT-009 | Fixed for query strings: `pageNumber` below 1 or `pageSize` outside 1..1000 returns 400 on every selection GET, matching preview. POST enumerate bodies keep the product-wide clamp. |
| UT-012 | Fixed. LIVE_CONFIGURATION_AND_OBSERVE.md, REST_API.md (`PUT /api/v1/settings` group, decision fields, reason codes, filter and paging rules). |
| UT-006, UT-010, UT-011, UT-013, UT-014, UT-015, UT-018 | Left for the owner: they change the public JSON contract or are product-wide (error envelopes and casing, Helm error rendering, enum conversion messages, viewer settings page, mission-create response shape, default tenant timestamps, redispatch decision policy). |
| UT-008, UT-016, UT-017 | Deferred: wait advice for a pinned constrained mission, and small Helm and Dashboard polish items. |
