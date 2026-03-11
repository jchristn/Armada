# Armada REST API Reference

**Version:** 0.1.0
**Base URL:** `http://localhost:7890`
**Content-Type:** `application/json`

---

## Table of Contents

- [Authentication](#authentication)
- [Pagination](#pagination)
- [Error Responses](#error-responses)
- [Endpoints](#endpoints)
  - [Status](#status)
  - [Fleets](#fleets)
  - [Vessels](#vessels)
  - [Voyages](#voyages)
  - [Missions](#missions)
  - [Captains](#captains)
  - [Signals](#signals)
  - [Events](#events)
  - [Docks](#docks)
  - [Merge Queue](#merge-queue)
- [Data Types](#data-types)
  - [Models](#models)
  - [Enumerations](#enumerations)
  - [Request Types](#request-types)
  - [Response Wrappers](#response-wrappers)

---

## Authentication

When `ApiKey` is configured in settings, all endpoints (except health check and dashboard) require an API key via the `X-Api-Key` header.

```
X-Api-Key: your-api-key-here
```

Unauthenticated requests return `401 Unauthorized`.

**Exempt routes** (no authentication required):
- `GET /api/v1/status/health`
- `GET /dashboard` and all `/dashboard/*` paths
- `GET /` (redirects to `/dashboard`)

---

## Pagination

All list endpoints return paginated results wrapped in `EnumerationResult<T>`. There are two ways to query:

### GET with Query String Parameters

```
GET /api/v1/missions?pageNumber=2&pageSize=25&status=InProgress&order=CreatedAscending
```

### POST /enumerate with JSON Body

```
POST /api/v1/missions/enumerate
Content-Type: application/json

{
  "PageNumber": 2,
  "PageSize": 25,
  "Status": "InProgress",
  "Order": "CreatedAscending"
}
```

Query string parameters **override** body values on POST enumerate endpoints, allowing defaults in the body with per-request overrides via URL.

### Pagination Parameters

| Parameter | Type | Default | Range | Description |
|---|---|---|---|---|
| `pageNumber` | int | 1 | >= 1 | Page number (1-based) |
| `pageSize` | int | 100 | 1 - 1000 | Results per page |
| `order` | string | `CreatedDescending` | `CreatedAscending`, `CreatedDescending` | Sort order by creation date |
| `createdAfter` | datetime | null | ISO 8601 | Filter: created after this timestamp |
| `createdBefore` | datetime | null | ISO 8601 | Filter: created before this timestamp |

### Entity-Specific Filters

| Parameter | Applies To | Description |
|---|---|---|
| `status` | missions, voyages, captains | Filter by status value |
| `fleetId` | vessels | Filter by fleet ID |
| `vesselId` | missions, docks, events | Filter by vessel ID |
| `captainId` | missions, docks, events | Filter by captain ID |
| `voyageId` | missions, events | Filter by voyage ID |
| `missionId` | events | Filter by mission ID |
| `type` | events | Filter by event type (alias for `eventType`) |
| `signalType` | signals | Filter by signal type |
| `toCaptainId` | signals | Filter by recipient captain ID |
| `unreadOnly` | signals | `true` to return only unread signals |

### Paginated Response Shape

```json
{
  "Success": true,
  "PageNumber": 1,
  "PageSize": 25,
  "TotalPages": 4,
  "TotalRecords": 87,
  "Objects": [ ... ],
  "TotalMs": 3.14
}
```

---

## Error Responses

All errors return a JSON object with `Error` and `Message` fields:

```json
{
  "Error": "NotFound",
  "Message": "Mission not found"
}
```

| Error Value | HTTP Status | Description |
|---|---|---|
| `BadRequest` | 400 | Invalid input, missing required fields, or invalid state transition |
| `Unauthorized` | 401 | Missing or invalid API key |
| `NotFound` | 404 | Entity not found |

---

## Endpoints

### Status

#### GET /api/v1/status

Returns aggregate status including captain counts, mission breakdown, active voyages, and recent signals.

**Response:** `200 OK` - [ArmadaStatus](#armadastatus)

```json
{
  "TotalCaptains": 5,
  "IdleCaptains": 2,
  "WorkingCaptains": 3,
  "StalledCaptains": 0,
  "ActiveVoyages": 1,
  "MissionsByStatus": {
    "Pending": 3,
    "InProgress": 2,
    "Complete": 10
  },
  "Voyages": [],
  "RecentSignals": [],
  "TimestampUtc": "2026-03-07T12:00:00Z"
}
```

---

#### GET /api/v1/status/health

Health check endpoint. **Does not require authentication.**

**Response:** `200 OK`

```json
{
  "Status": "healthy",
  "Timestamp": "2026-03-07T12:00:00Z",
  "StartUtc": "2026-03-07T08:00:00Z",
  "Uptime": "0.04:00:00",
  "Version": "0.1.0",
  "Ports": {
    "Admiral": 7890,
    "Mcp": 7891,
    "WebSocket": 7892
  }
}
```

---

#### POST /api/v1/server/stop

Initiates a graceful shutdown of the Admiral server.

**Response:** `200 OK`

```json
{
  "Status": "shutting_down"
}
```

---

### Fleets

A fleet is a named collection of repositories (vessels) under management.

#### GET /api/v1/fleets

List all fleets with pagination.

**Query Parameters:** [Pagination parameters](#pagination-parameters)

**Response:** `200 OK` - [EnumerationResult](#enumerationresultt)\<[Fleet](#fleet)\>

```bash
curl http://localhost:7890/api/v1/fleets?pageSize=10
```

---

#### POST /api/v1/fleets/enumerate

Paginated enumeration of fleets with optional filtering and sorting.

**Request Body:** [EnumerationQuery](#enumerationquery) (optional)

**Response:** `200 OK` - [EnumerationResult](#enumerationresultt)\<[Fleet](#fleet)\>

```bash
curl -X POST http://localhost:7890/api/v1/fleets/enumerate \
  -H "Content-Type: application/json" \
  -d '{"PageSize": 10, "Order": "CreatedAscending"}'
```

---

#### POST /api/v1/fleets

Create a new fleet.

**Request Body:** [Fleet](#fleet)

| Field | Type | Required | Description |
|---|---|---|---|
| `Name` | string | yes | Fleet name |
| `Description` | string | no | Fleet description |

**Response:** `201 Created` - [Fleet](#fleet)

```bash
curl -X POST http://localhost:7890/api/v1/fleets \
  -H "Content-Type: application/json" \
  -d '{"Name": "Production Fleet", "Description": "Production repositories"}'
```

---

#### GET /api/v1/fleets/{id}

Get a single fleet by ID, including all its vessels.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Fleet ID (`flt_` prefix) |

**Response:** `200 OK` - `{ Fleet: Fleet, Vessels: Vessel[] }`
**Error:** `404` - Fleet not found

```bash
curl http://localhost:7890/api/v1/fleets/flt_abc123
```

---

#### PUT /api/v1/fleets/{id}

Update an existing fleet.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Fleet ID (`flt_` prefix) |

**Request Body:** [Fleet](#fleet) (fields to update)

**Response:** `200 OK` - [Fleet](#fleet)
**Error:** `404` - Fleet not found

```bash
curl -X PUT http://localhost:7890/api/v1/fleets/flt_abc123 \
  -H "Content-Type: application/json" \
  -d '{"Name": "Renamed Fleet"}'
```

---

#### DELETE /api/v1/fleets/{id}

Delete a fleet. Vessels in the fleet are not deleted; their `FleetId` is set to null.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Fleet ID (`flt_` prefix) |

**Response:** `204 No Content`

```bash
curl -X DELETE http://localhost:7890/api/v1/fleets/flt_abc123
```

---

### Vessels

A vessel is a git repository registered with Armada.

#### GET /api/v1/vessels

List all vessels with pagination.

**Query Parameters:** [Pagination parameters](#pagination-parameters), plus:

| Parameter | Type | Description |
|---|---|---|
| `fleetId` | string | Filter by fleet ID |

**Response:** `200 OK` - [EnumerationResult](#enumerationresultt)\<[Vessel](#vessel)\>

```bash
curl http://localhost:7890/api/v1/vessels?fleetId=flt_abc123
```

---

#### POST /api/v1/vessels/enumerate

Paginated enumeration of vessels with optional filtering and sorting.

**Request Body:** [EnumerationQuery](#enumerationquery) (optional)

**Response:** `200 OK` - [EnumerationResult](#enumerationresultt)\<[Vessel](#vessel)\>

```bash
curl -X POST http://localhost:7890/api/v1/vessels/enumerate \
  -H "Content-Type: application/json" \
  -d '{"FleetId": "flt_abc123", "PageSize": 50}'
```

---

#### POST /api/v1/vessels

Register a new vessel (git repository).

**Request Body:** [Vessel](#vessel)

| Field | Type | Required | Description |
|---|---|---|---|
| `Name` | string | yes | Vessel name |
| `RepoUrl` | string | yes | Remote repository URL |
| `FleetId` | string | no | Fleet to assign to |
| `DefaultBranch` | string | no | Default branch name (default: `"main"`) |

**Response:** `201 Created` - [Vessel](#vessel)

```bash
curl -X POST http://localhost:7890/api/v1/vessels \
  -H "Content-Type: application/json" \
  -d '{"Name": "MyRepo", "RepoUrl": "https://github.com/org/repo.git", "FleetId": "flt_abc123"}'
```

---

#### GET /api/v1/vessels/{id}

Get a single vessel by ID.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Vessel ID (`vsl_` prefix) |

**Response:** `200 OK` - [Vessel](#vessel)
**Error:** `404` - Vessel not found

---

#### PUT /api/v1/vessels/{id}

Update an existing vessel.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Vessel ID (`vsl_` prefix) |

**Request Body:** [Vessel](#vessel) (fields to update)

**Response:** `200 OK` - [Vessel](#vessel)
**Error:** `404` - Vessel not found

---

#### DELETE /api/v1/vessels/{id}

Delete a vessel.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Vessel ID (`vsl_` prefix) |

**Response:** `204 No Content`

---

#### PATCH /api/v1/vessels/{id}/context

Update only the `ProjectContext` and `StyleGuide` fields of a vessel.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Vessel ID (`vsl_` prefix) |

**Request Body:**

| Field | Type | Required | Description |
|---|---|---|---|
| `ProjectContext` | string | no | Project context describing architecture, key files, and dependencies |
| `StyleGuide` | string | no | Style guide describing naming conventions, patterns, and library preferences |

```bash
curl -X PATCH http://localhost:7890/api/v1/vessels/vsl_abc123/context \
  -H "Content-Type: application/json" \
  -d '{"ProjectContext": "C# .NET 8 project with SQLite", "StyleGuide": "Use PascalCase for public members"}'
```

**Response:** `200 OK` - [Vessel](#vessel)
**Error:** `404` - Vessel not found

---

### Voyages

A voyage is a batch of related missions tracked together.

#### GET /api/v1/voyages

List all voyages with pagination.

**Query Parameters:** [Pagination parameters](#pagination-parameters), plus:

| Parameter | Type | Description |
|---|---|---|
| `status` | string | Filter by voyage status (`Open`, `InProgress`, `Complete`, `Cancelled`) |

**Response:** `200 OK` - [EnumerationResult](#enumerationresultt)\<[Voyage](#voyage)\>

```bash
curl http://localhost:7890/api/v1/voyages?status=InProgress
```

---

#### POST /api/v1/voyages/enumerate

Paginated enumeration of voyages with optional filtering and sorting.

**Request Body:** [EnumerationQuery](#enumerationquery) (optional)

**Response:** `200 OK` - [EnumerationResult](#enumerationresultt)\<[Voyage](#voyage)\>

---

#### POST /api/v1/voyages

Create a new voyage with optional missions. Missions are automatically dispatched to the target vessel.

**Request Body:** [VoyageRequest](#voyagerequest)

| Field | Type | Required | Description |
|---|---|---|---|
| `Title` | string | yes | Voyage title |
| `Description` | string | no | Voyage description |
| `VesselId` | string | yes | Target vessel ID |
| `Missions` | array | no | List of [MissionRequest](#missionrequest) objects |

**Response:** `201 Created` - [Voyage](#voyage)

```bash
curl -X POST http://localhost:7890/api/v1/voyages \
  -H "Content-Type: application/json" \
  -d '{
    "Title": "API Hardening",
    "Description": "Security improvements",
    "VesselId": "vsl_abc123",
    "Missions": [
      {"Title": "Add rate limiting", "Description": "Add rate limiting middleware"},
      {"Title": "Add input validation", "Description": "Validate all POST endpoints"}
    ]
  }'
```

---

#### GET /api/v1/voyages/{id}

Get a voyage and all its associated missions.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Voyage ID (`vyg_` prefix) |

**Response:** `200 OK` - [VoyageDetail](#voyagedetail)

```json
{
  "Voyage": { ... },
  "Missions": [ ... ]
}
```

**Error:** `404` - Voyage not found

---

#### DELETE /api/v1/voyages/{id}

Cancel a voyage. Sets the voyage status to `Cancelled` and cancels all `Pending` or `Assigned` missions. In-progress missions are not affected.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Voyage ID (`vyg_` prefix) |

**Response:** `200 OK`

```json
{
  "Voyage": { "Id": "vyg_abc123", "Status": "Cancelled", "..." : "..." },
  "CancelledMissions": 3
}
```

**Error:** `404` - Voyage not found

---

#### DELETE /api/v1/voyages/{id}/purge

Permanently delete a voyage and all its associated missions from the database. **This cannot be undone.**

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Voyage ID (`vyg_` prefix) |

**Response:** `200 OK`

```json
{
  "Status": "deleted",
  "VoyageId": "vyg_abc123",
  "MissionsDeleted": 5
}
```

**Error:** `404` - Voyage not found

---

### Missions

A mission is an atomic unit of work assigned to a captain (AI agent).

#### GET /api/v1/missions

List all missions with pagination.

**Query Parameters:** [Pagination parameters](#pagination-parameters), plus:

| Parameter | Type | Description |
|---|---|---|
| `status` | string | Filter by mission status |
| `vesselId` | string | Filter by vessel ID |
| `captainId` | string | Filter by captain ID |
| `voyageId` | string | Filter by voyage ID |

**Response:** `200 OK` - [EnumerationResult](#enumerationresultt)\<[Mission](#mission)\>

```bash
curl http://localhost:7890/api/v1/missions?status=InProgress&vesselId=vsl_abc123
```

---

#### POST /api/v1/missions/enumerate

Paginated enumeration of missions with optional filtering and sorting.

**Request Body:** [EnumerationQuery](#enumerationquery) (optional)

**Response:** `200 OK` - [EnumerationResult](#enumerationresultt)\<[Mission](#mission)\>

```bash
curl -X POST http://localhost:7890/api/v1/missions/enumerate \
  -H "Content-Type: application/json" \
  -d '{"Status": "InProgress", "VesselId": "vsl_abc123", "PageSize": 25}'
```

---

#### POST /api/v1/missions

Create and dispatch a new mission. If a `VesselId` is provided, the Admiral will assign a captain and set up a worktree.

**Request Body:** [Mission](#mission)

| Field | Type | Required | Description |
|---|---|---|---|
| `Title` | string | yes | Mission title |
| `Description` | string | no | Detailed instructions for the AI agent |
| `VesselId` | string | no | Target vessel (required for auto-dispatch) |
| `VoyageId` | string | no | Parent voyage ID |
| `Priority` | int | no | Priority (lower = higher priority, default: 100) |

**Response:** `201 Created` - [Mission](#mission)

```bash
curl -X POST http://localhost:7890/api/v1/missions \
  -H "Content-Type: application/json" \
  -d '{"Title": "Fix login bug", "Description": "The login form does not validate email addresses", "VesselId": "vsl_abc123"}'
```

---

#### GET /api/v1/missions/{id}

Get a single mission by ID.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Mission ID (`msn_` prefix) |

**Response:** `200 OK` - [Mission](#mission)
**Error:** `404` - Mission not found

---

#### PUT /api/v1/missions/{id}

Update mission fields (title, description, priority, etc.). Does not change status -- use the status transition endpoint for that.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Mission ID (`msn_` prefix) |

**Request Body:** [Mission](#mission) (fields to update)

**Response:** `200 OK` - [Mission](#mission)
**Error:** `404` - Mission not found

---

#### PUT /api/v1/missions/{id}/status

Transition a mission to a new status. Only valid transitions are allowed.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Mission ID (`msn_` prefix) |

**Request Body:** [StatusTransitionRequest](#statustransitionrequest)

| Field | Type | Required | Description |
|---|---|---|---|
| `Status` | string | yes | Target status name |

**Response:** `200 OK` - [Mission](#mission)
**Error:** `400` - Invalid transition or invalid status name
**Error:** `404` - Mission not found

**Valid Status Transitions:**

| From | Allowed Targets |
|---|---|
| `Pending` | `Assigned`, `Cancelled` |
| `Assigned` | `InProgress`, `Cancelled` |
| `InProgress` | `Testing`, `Review`, `Complete`, `Failed`, `Cancelled` |
| `Testing` | `Review`, `InProgress`, `Complete`, `Failed` |
| `Review` | `Complete`, `InProgress`, `Failed` |
| `Complete` | (terminal) |
| `Failed` | (terminal) |
| `Cancelled` | (terminal) |

```bash
curl -X PUT http://localhost:7890/api/v1/missions/msn_abc123/status \
  -H "Content-Type: application/json" \
  -d '{"Status": "Assigned"}'
```

---

#### DELETE /api/v1/missions/{id}

Cancel a mission by setting its status to `Cancelled`. Returns the full updated mission.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Mission ID (`msn_` prefix) |

**Response:** `200 OK` - [Mission](#mission) (with `Status: "Cancelled"`)

**Error:** `404` - Mission not found

---

#### POST /api/v1/missions/{id}/restart

Restart a failed or cancelled mission by resetting it to `Pending` for re-dispatch. Clears captain assignment, branch, PR URL, and timing fields. Optionally update the title and description (instructions) before restarting.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Mission ID (`msn_` prefix) |

**Request Body (optional):**
```json
{
  "Title": "Updated mission title",
  "Description": "Updated instructions for the captain"
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `Title` | string | No | New mission title. Omit to keep the original. |
| `Description` | string | No | New mission description/instructions. Omit to keep the original. |

**Response:** `200 OK` - [Mission](#mission) (with `Status: "Pending"`)

**Errors:**
- `400` - Mission is not in `Failed` or `Cancelled` status
- `404` - Mission not found

---

#### GET /api/v1/missions/{id}/diff

Returns the git diff of changes made by a captain in the mission's worktree. Checks for a saved diff file first (captured at completion), then falls back to a live worktree diff.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Mission ID (`msn_` prefix) |

**Response:** `200 OK`

```json
{
  "MissionId": "msn_abc123",
  "Branch": "armada/msn_abc123",
  "Diff": "diff --git a/src/auth.ts b/src/auth.ts\n..."
}
```

**Error:** `404` - Mission not found or no diff available

---

#### GET /api/v1/missions/{id}/log

Returns the session log (captured stdout/stderr) for a mission. Log files are written to disk when a captain executes a mission. Supports pagination via query parameters.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Mission ID (`msn_` prefix) |

**Query Parameters:**
| Parameter | Type | Default | Description |
|---|---|---|---|
| `lines` | integer | 100 | Number of lines to return |
| `offset` | integer | 0 | Line offset (0-based, skip this many lines from start) |

**Response:** `200 OK`

```json
{
  "MissionId": "msn_abc123",
  "Log": "Starting mission...\nCloning repository...\n...",
  "Lines": 100,
  "TotalLines": 542
}
```

If the mission exists but has no log file yet, returns an empty log:

```json
{
  "MissionId": "msn_abc123",
  "Log": "",
  "Lines": 0,
  "TotalLines": 0
}
```

**Error:** `404` - Mission not found

```bash
# Get first 50 lines
curl http://localhost:8080/api/v1/missions/msn_abc123/log?lines=50 \
  -H "X-Api-Key: your-key"

# Get lines 100-200
curl http://localhost:8080/api/v1/missions/msn_abc123/log?offset=100&lines=100 \
  -H "X-Api-Key: your-key"
```

---

### Captains

A captain is an AI agent instance (Claude Code, Codex, etc.) that executes missions.

#### GET /api/v1/captains

List all captains with pagination.

**Query Parameters:** [Pagination parameters](#pagination-parameters), plus:

| Parameter | Type | Description |
|---|---|---|
| `status` | string | Filter by captain state (`Idle`, `Working`, `Stalled`, `Stopping`) |

**Response:** `200 OK` - [EnumerationResult](#enumerationresultt)\<[Captain](#captain)\>

```bash
curl http://localhost:7890/api/v1/captains?status=Working
```

---

#### POST /api/v1/captains/enumerate

Paginated enumeration of captains with optional filtering and sorting.

**Request Body:** [EnumerationQuery](#enumerationquery) (optional)

**Response:** `200 OK` - [EnumerationResult](#enumerationresultt)\<[Captain](#captain)\>

---

#### POST /api/v1/captains

Register a new captain (AI agent).

**Request Body:** [Captain](#captain)

| Field | Type | Required | Description |
|---|---|---|---|
| `Name` | string | yes | Captain name |
| `Runtime` | string | no | Agent runtime type (default: `ClaudeCode`) |

**Response:** `201 Created` - [Captain](#captain)

```bash
curl -X POST http://localhost:7890/api/v1/captains \
  -H "Content-Type: application/json" \
  -d '{"Name": "captain-1", "Runtime": "ClaudeCode"}'
```

---

#### GET /api/v1/captains/{id}

Get a single captain by ID.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Captain ID (`cpt_` prefix) |

**Response:** `200 OK` - [Captain](#captain)
**Error:** `404` - Captain not found

---

#### PUT /api/v1/captains/{id}

Update a captain's name or runtime. Operational fields (state, process, mission) are preserved.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Captain ID (`cpt_` prefix) |

**Request Body:**
```json
{
  "name": "captain-bravo",
  "runtime": "Codex"
}
```

**Response:** `200 OK` - [Captain](#captain)
**Error:** `404` - Captain not found

```bash
curl -X PUT http://localhost:7890/api/v1/captains/cpt_abc123 \
  -H "Content-Type: application/json" \
  -H "x-api-key: YOUR_KEY" \
  -d '{"name": "captain-bravo", "runtime": "Codex"}'
```

---

#### POST /api/v1/captains/{id}/stop

Stop a running captain agent. Kills its OS process and recalls it to idle state.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Captain ID (`cpt_` prefix) |

**Response:** `200 OK`

```json
{
  "Status": "stopped"
}
```

**Error:** `404` - Captain not found

---

#### POST /api/v1/captains/stop-all

Emergency stop all running captains, recalling them to idle state.

**Response:** `200 OK`

```json
{
  "Status": "all_stopped"
}
```

---

#### GET /api/v1/captains/{id}/log

Returns the current session log for a captain. The captain's `.current` pointer file is resolved to find the active mission's log file. Supports pagination via query parameters.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Captain ID (`cpt_` prefix) |

**Query Parameters:**
| Parameter | Type | Default | Description |
|---|---|---|---|
| `lines` | integer | 100 | Number of lines to return |
| `offset` | integer | 0 | Line offset (0-based, skip this many lines from start) |

**Response:** `200 OK`

```json
{
  "CaptainId": "cpt_abc123",
  "Log": "[2026-03-07] Processing task...\nRunning tests...\n...",
  "Lines": 100,
  "TotalLines": 203
}
```

If the captain has no active log (no pointer file or target file missing), returns an empty log:

```json
{
  "CaptainId": "cpt_abc123",
  "Log": "",
  "Lines": 0,
  "TotalLines": 0
}
```

**Error:** `404` - Captain not found

```bash
curl http://localhost:8080/api/v1/captains/cpt_abc123/log?lines=200 \
  -H "X-Api-Key: your-key"
```

---

#### DELETE /api/v1/captains/{id}

Delete a captain. Blocked if the captain is currently working or has active missions.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Captain ID (`cpt_` prefix) |

**Response:** `204 No Content`
**Error:** `404` - Captain not found
**Error:** `409 Conflict` - Cannot delete captain while state is Working. Stop the captain first.
**Error:** `409 Conflict` - Cannot delete captain with active missions in Assigned or InProgress status. Cancel or complete them first.

---

### Signals

A signal is a message between the admiral and captains or between captains.

#### GET /api/v1/signals

List recent signals with pagination.

**Query Parameters:** [Pagination parameters](#pagination-parameters), plus:

| Parameter | Type | Description |
|---|---|---|
| `signalType` | string | Filter by signal type |
| `toCaptainId` | string | Filter by recipient captain ID |
| `unreadOnly` | bool | `true` to return only unread signals |

**Response:** `200 OK` - [EnumerationResult](#enumerationresultt)\<[Signal](#signal)\>

```bash
curl http://localhost:7890/api/v1/signals?toCaptainId=cpt_abc123&unreadOnly=true
```

---

#### POST /api/v1/signals/enumerate

Paginated enumeration of signals with optional filtering and sorting.

**Request Body:** [EnumerationQuery](#enumerationquery) (optional)

**Response:** `200 OK` - [EnumerationResult](#enumerationresultt)\<[Signal](#signal)\>

---

#### POST /api/v1/signals

Send a new signal (message).

**Request Body:** [Signal](#signal)

| Field | Type | Required | Description |
|---|---|---|---|
| `Type` | string | no | Signal type (default: `Nudge`) |
| `Payload` | string | no | Signal payload (message content) |
| `ToCaptainId` | string | no | Recipient captain ID (null = to Admiral) |
| `FromCaptainId` | string | no | Sender captain ID (null = from Admiral) |

**Response:** `201 Created` - [Signal](#signal)

```bash
curl -X POST http://localhost:7890/api/v1/signals \
  -H "Content-Type: application/json" \
  -d '{"Type": "Mail", "Payload": "Please check the test results", "ToCaptainId": "cpt_abc123"}'
```

---

### Events

System events represent state changes and audit trail entries generated automatically by the server.

#### GET /api/v1/events

List system events with pagination.

**Query Parameters:** [Pagination parameters](#pagination-parameters), plus:

| Parameter | Type | Description |
|---|---|---|
| `type` | string | Filter by event type (e.g. `mission.status_changed`) |
| `captainId` | string | Filter by captain ID |
| `missionId` | string | Filter by mission ID |
| `vesselId` | string | Filter by vessel ID |
| `voyageId` | string | Filter by voyage ID |
| `limit` | int | Alias for `pageSize` |

**Response:** `200 OK` - [EnumerationResult](#enumerationresultt)\<[ArmadaEvent](#armadaevent)\>

```bash
curl http://localhost:7890/api/v1/events?type=mission.status_changed&missionId=msn_abc123
```

---

#### POST /api/v1/events/enumerate

Paginated enumeration of events with optional filtering and sorting.

**Request Body:** [EnumerationQuery](#enumerationquery) (optional)

**Response:** `200 OK` - [EnumerationResult](#enumerationresultt)\<[ArmadaEvent](#armadaevent)\>

---

### Docks

Docks are git worktrees provisioned for captains. They are managed internally by the Admiral and cannot be created or deleted directly via the API. These endpoints provide read-only access to dock state.

#### `GET /api/v1/docks`

List all docks with optional filtering.

**Query Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `vesselId` | string | Filter by vessel ID |
| `pageNumber` | integer | Page number (1-based, default 1) |
| `pageSize` | integer | Results per page (default 100) |
| `order` | string | Sort order: `CreatedAscending`, `CreatedDescending` |

**Response:** `200 OK`

```json
{
  "Objects": [],
  "TotalRecords": 0,
  "PageSize": 100,
  "PageNumber": 1,
  "TotalPages": 0,
  "Success": true,
  "TotalMs": 0.5
}
```

---

#### `POST /api/v1/docks/enumerate`

Paginated enumeration of docks with optional filtering and sorting.

**Request Body:**

```json
{
  "PageNumber": 1,
  "PageSize": 25,
  "VesselId": "vsl_abc123"
}
```

**Response:** `200 OK` — Same shape as `GET /api/v1/docks`.

---

### Merge Queue

A bors-style merge queue that batches branches, runs tests, and lands passing batches.

#### GET /api/v1/merge-queue

List merge queue entries with pagination.

**Query Parameters:** [Pagination parameters](#pagination-parameters)

**Response:** `200 OK` - [EnumerationResult](#enumerationresultt)\<[MergeEntry](#mergeentry)\>

```bash
curl http://localhost:7890/api/v1/merge-queue
```

---

#### POST /api/v1/merge-queue/enumerate

Paginated enumeration of merge queue entries with optional filtering and sorting.

**Request Body:** [EnumerationQuery](#enumerationquery) (optional)

**Response:** `200 OK` - [EnumerationResult](#enumerationresultt)\<[MergeEntry](#mergeentry)\>

---

#### POST /api/v1/merge-queue

Enqueue a branch for testing and merging.

**Request Body:** [MergeEntry](#mergeentry)

| Field | Type | Required | Description |
|---|---|---|---|
| `BranchName` | string | yes | Branch to merge |
| `TargetBranch` | string | no | Target branch (default: `"main"`) |
| `MissionId` | string | no | Parent mission ID |
| `VesselId` | string | no | Vessel ID |
| `Priority` | int | no | Queue priority (lower = higher, default: 0) |
| `TestCommand` | string | no | Test command for verification |

**Response:** `201 Created` - [MergeEntry](#mergeentry)

```bash
curl -X POST http://localhost:7890/api/v1/merge-queue \
  -H "Content-Type: application/json" \
  -d '{"BranchName": "armada/msn_abc123", "TargetBranch": "main", "MissionId": "msn_abc123"}'
```

---

#### GET /api/v1/merge-queue/{id}

Get a single merge queue entry by ID.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Merge entry ID (`mrg_` prefix) |

**Response:** `200 OK` - [MergeEntry](#mergeentry)
**Error:** `404` - Merge entry not found

---

#### DELETE /api/v1/merge-queue/{id}

Cancel a queued merge entry.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Merge entry ID (`mrg_` prefix) |

**Response:** `204 No Content`

---

#### POST /api/v1/merge-queue/process

Trigger processing of the merge queue. Creates integration branches, runs tests, and lands passing batches.

**Response:** `200 OK`

```json
{
  "Status": "processed"
}
```

---

## Data Types

### Models

#### Fleet

A named collection of repositories under management.

```json
{
  "Id": "flt_abc123",
  "Name": "Production Fleet",
  "Description": "Production repositories",
  "Active": true,
  "CreatedUtc": "2026-03-07T12:00:00Z",
  "LastUpdateUtc": "2026-03-07T12:00:00Z"
}
```

| Field | Type | Default | Description |
|---|---|---|---|
| `Id` | string | auto-generated | Unique ID with `flt_` prefix |
| `Name` | string | `"My Fleet"` | Fleet name |
| `Description` | string? | null | Fleet description |
| `Active` | bool | true | Whether fleet is active |
| `CreatedUtc` | datetime | now | Creation timestamp (UTC) |
| `LastUpdateUtc` | datetime | now | Last update timestamp (UTC) |

---

#### Vessel

A git repository registered with Armada.

```json
{
  "Id": "vsl_abc123",
  "FleetId": "flt_abc123",
  "Name": "MyRepo",
  "RepoUrl": "https://github.com/org/repo.git",
  "LocalPath": "/home/user/.armada/repos/MyRepo",
  "WorkingDirectory": null,
  "DefaultBranch": "main",
  "ProjectContext": null,
  "StyleGuide": null,
  "Active": true,
  "CreatedUtc": "2026-03-07T12:00:00Z",
  "LastUpdateUtc": "2026-03-07T12:00:00Z"
}
```

| Field | Type | Default | Description |
|---|---|---|---|
| `Id` | string | auto-generated | Unique ID with `vsl_` prefix |
| `FleetId` | string? | null | Parent fleet ID |
| `Name` | string | `"My Vessel"` | Vessel name |
| `RepoUrl` | string? | null | Remote repository URL |
| `LocalPath` | string? | null | Local path to bare repository clone |
| `WorkingDirectory` | string? | null | Local working directory for merge on completion |
| `DefaultBranch` | string | `"main"` | Default branch name |
| `ProjectContext` | string? | null | Project context describing architecture, key files, and dependencies |
| `StyleGuide` | string? | null | Style guide describing naming conventions, patterns, and library preferences |
| `Active` | bool | true | Whether vessel is active |
| `CreatedUtc` | datetime | now | Creation timestamp (UTC) |
| `LastUpdateUtc` | datetime | now | Last update timestamp (UTC) |

---

#### Voyage

A batch of related missions tracked together.

```json
{
  "Id": "vyg_abc123",
  "Title": "API Hardening",
  "Description": "Security improvements across the API",
  "Status": "InProgress",
  "CreatedUtc": "2026-03-07T12:00:00Z",
  "CompletedUtc": null,
  "LastUpdateUtc": "2026-03-07T12:00:00Z",
  "AutoPush": null,
  "AutoCreatePullRequests": null,
  "AutoMergePullRequests": null
}
```

| Field | Type | Default | Description |
|---|---|---|---|
| `Id` | string | auto-generated | Unique ID with `vyg_` prefix |
| `Title` | string | `"New Voyage"` | Voyage title |
| `Description` | string? | null | Voyage description |
| `Status` | [VoyageStatusEnum](#voyagestatusenum) | `Open` | Current status |
| `CreatedUtc` | datetime | now | Creation timestamp (UTC) |
| `CompletedUtc` | datetime? | null | Completion timestamp (UTC) |
| `LastUpdateUtc` | datetime | now | Last update timestamp (UTC) |
| `AutoPush` | bool? | null | Per-voyage auto-push override (null = use global setting) |
| `AutoCreatePullRequests` | bool? | null | Per-voyage auto-create PRs override |
| `AutoMergePullRequests` | bool? | null | Per-voyage auto-merge PRs override |

---

#### Mission

An atomic unit of work assigned to a captain.

```json
{
  "Id": "msn_abc123",
  "VoyageId": "vyg_abc123",
  "VesselId": "vsl_abc123",
  "CaptainId": "cpt_abc123",
  "Title": "Fix login bug",
  "Description": "The login form does not validate email addresses",
  "Status": "InProgress",
  "Priority": 100,
  "ParentMissionId": null,
  "BranchName": "armada/msn_abc123",
  "DockId": null,
  "ProcessId": null,
  "PrUrl": null,
  "CommitHash": null,
  "DiffSnapshot": null,
  "CreatedUtc": "2026-03-07T12:00:00Z",
  "StartedUtc": "2026-03-07T12:05:00Z",
  "CompletedUtc": null,
  "LastUpdateUtc": "2026-03-07T12:10:00Z"
}
```

| Field | Type | Default | Description |
|---|---|---|---|
| `Id` | string | auto-generated | Unique ID with `msn_` prefix |
| `VoyageId` | string? | null | Parent voyage ID |
| `VesselId` | string? | null | Target vessel (repository) ID |
| `CaptainId` | string? | null | Assigned captain (agent) ID |
| `Title` | string | `"New Mission"` | Mission title |
| `Description` | string? | null | Detailed instructions for the AI agent |
| `Status` | [MissionStatusEnum](#missionstatusenum) | `Pending` | Current status |
| `Priority` | int | 100 | Priority (lower number = higher priority) |
| `ParentMissionId` | string? | null | Parent mission ID for sub-tasks |
| `BranchName` | string? | null | Git branch name |
| `DockId` | string? | null | Dock identifier for the mission's worktree |
| `ProcessId` | int? | null | OS process ID of the agent working on the mission |
| `PrUrl` | string? | null | Pull request URL if created |
| `CommitHash` | string? | null | Git commit hash captured on completion |
| `DiffSnapshot` | string? | null | Git diff snapshot captured on completion |
| `CreatedUtc` | datetime | now | Creation timestamp (UTC) |
| `StartedUtc` | datetime? | null | Work start timestamp (UTC) |
| `CompletedUtc` | datetime? | null | Completion timestamp (UTC) |
| `LastUpdateUtc` | datetime | now | Last update timestamp (UTC) |

---

#### Captain

A worker AI agent instance executing missions.

```json
{
  "Id": "cpt_abc123",
  "Name": "captain-1",
  "Runtime": "ClaudeCode",
  "MaxParallelism": 1,
  "State": "Idle",
  "CurrentMissionId": null,
  "CurrentDockId": null,
  "ProcessId": null,
  "RecoveryAttempts": 0,
  "LastHeartbeatUtc": null,
  "CreatedUtc": "2026-03-07T12:00:00Z",
  "LastUpdateUtc": "2026-03-07T12:00:00Z"
}
```

| Field | Type | Default | Description |
|---|---|---|---|
| `Id` | string | auto-generated | Unique ID with `cpt_` prefix |
| `Name` | string | `"Captain"` | Captain name |
| `Runtime` | [AgentRuntimeEnum](#agentruntimeenum) | `ClaudeCode` | Agent runtime type |
| `MaxParallelism` | int | 1 | Maximum concurrent missions (minimum 1) |
| `State` | [CaptainStateEnum](#captainstateenum) | `Idle` | Current state |
| `CurrentMissionId` | string? | null | Currently assigned mission ID |
| `CurrentDockId` | string? | null | Currently assigned dock (worktree) ID |
| `ProcessId` | int? | null | OS process ID |
| `RecoveryAttempts` | int | 0 | Auto-recovery attempts for current mission |
| `LastHeartbeatUtc` | datetime? | null | Last heartbeat timestamp (UTC) |
| `CreatedUtc` | datetime | now | Creation timestamp (UTC) |
| `LastUpdateUtc` | datetime | now | Last update timestamp (UTC) |

---

#### Signal

A message between the admiral and captains.

```json
{
  "Id": "sig_abc123",
  "FromCaptainId": "cpt_abc123",
  "ToCaptainId": null,
  "Type": "Progress",
  "Payload": "Mission msn_abc123 transitioned to Testing",
  "Read": false,
  "CreatedUtc": "2026-03-07T12:00:00Z"
}
```

| Field | Type | Default | Description |
|---|---|---|---|
| `Id` | string | auto-generated | Unique ID with `sig_` prefix |
| `FromCaptainId` | string? | null | Sender captain ID (null = from Admiral) |
| `ToCaptainId` | string? | null | Recipient captain ID (null = to Admiral) |
| `Type` | [SignalTypeEnum](#signaltypeenum) | `Nudge` | Signal type |
| `Payload` | string? | null | Message payload |
| `Read` | bool | false | Whether signal has been read |
| `CreatedUtc` | datetime | now | Creation timestamp (UTC) |

---

#### ArmadaEvent

A recorded event representing a state change in the system.

```json
{
  "Id": "evt_abc123",
  "EventType": "mission.status_changed",
  "EntityType": "mission",
  "EntityId": "msn_abc123",
  "CaptainId": "cpt_abc123",
  "MissionId": "msn_abc123",
  "VesselId": "vsl_abc123",
  "VoyageId": "vyg_abc123",
  "Message": "Mission msn_abc123 transitioned to Complete",
  "Payload": null,
  "CreatedUtc": "2026-03-07T12:00:00Z"
}
```

| Field | Type | Default | Description |
|---|---|---|---|
| `Id` | string | auto-generated | Unique ID with `evt_` prefix |
| `EventType` | string | `""` | Event type identifier |
| `EntityType` | string? | null | Related entity type |
| `EntityId` | string? | null | Related entity ID |
| `CaptainId` | string? | null | Related captain ID |
| `MissionId` | string? | null | Related mission ID |
| `VesselId` | string? | null | Related vessel ID |
| `VoyageId` | string? | null | Related voyage ID |
| `Message` | string | `""` | Human-readable event message |
| `Payload` | string? | null | JSON payload with additional details |
| `CreatedUtc` | datetime | now | Event timestamp (UTC) |

**Known Event Types:**
- `mission.created` - Mission was created
- `mission.status_changed` - Mission status transitioned
- `mission.completed` - Mission completed successfully
- `mission.failed` - Mission failed
- `captain.launched` - Captain agent process started
- `captain.stopped` - Captain agent process stopped
- `captain.stalled` - Captain detected as stalled
- `voyage.created` - Voyage was created
- `voyage.completed` - All missions in voyage completed
- `voyage.deleted` - Voyage permanently deleted

---

#### MergeEntry

An entry in the merge queue representing a branch to be tested and merged.

```json
{
  "Id": "mrg_abc123",
  "MissionId": "msn_abc123",
  "VesselId": "vsl_abc123",
  "BranchName": "armada/msn_abc123",
  "TargetBranch": "main",
  "Status": "Queued",
  "Priority": 0,
  "BatchId": null,
  "TestCommand": "dotnet test",
  "TestOutput": null,
  "TestExitCode": null,
  "CreatedUtc": "2026-03-07T12:00:00Z",
  "LastUpdateUtc": "2026-03-07T12:00:00Z",
  "TestStartedUtc": null,
  "CompletedUtc": null
}
```

| Field | Type | Default | Description |
|---|---|---|---|
| `Id` | string | auto-generated | Unique ID with `mrg_` prefix |
| `MissionId` | string? | null | Parent mission ID |
| `VesselId` | string? | null | Vessel ID |
| `BranchName` | string | `"unknown"` | Branch to merge |
| `TargetBranch` | string | `"main"` | Target branch |
| `Status` | [MergeStatusEnum](#mergestatusenum) | `Queued` | Current status |
| `Priority` | int | 0 | Queue priority (lower = higher) |
| `BatchId` | string? | null | Batch ID during batch testing |
| `TestCommand` | string? | null | Test command for verification |
| `TestOutput` | string? | null | Test output or error message |
| `TestExitCode` | int? | null | Test process exit code |
| `CreatedUtc` | datetime | now | Creation timestamp (UTC) |
| `LastUpdateUtc` | datetime | now | Last update timestamp (UTC) |
| `TestStartedUtc` | datetime? | null | Test start timestamp (UTC) |
| `CompletedUtc` | datetime? | null | Completion timestamp (UTC) |

---

#### ArmadaStatus

Aggregate status summary returned by the status endpoint.

```json
{
  "TotalCaptains": 5,
  "IdleCaptains": 2,
  "WorkingCaptains": 3,
  "StalledCaptains": 0,
  "ActiveVoyages": 1,
  "MissionsByStatus": {
    "Pending": 3,
    "InProgress": 2,
    "Complete": 10
  },
  "Voyages": [
    {
      "Voyage": { ... },
      "TotalMissions": 5,
      "CompletedMissions": 3,
      "FailedMissions": 0,
      "InProgressMissions": 2
    }
  ],
  "RecentSignals": [],
  "TimestampUtc": "2026-03-07T12:00:00Z"
}
```

| Field | Type | Description |
|---|---|---|
| `TotalCaptains` | int | Total registered captains |
| `IdleCaptains` | int | Number of idle captains |
| `WorkingCaptains` | int | Number of working captains |
| `StalledCaptains` | int | Number of stalled captains |
| `ActiveVoyages` | int | Total active voyages |
| `MissionsByStatus` | dict\<string, int\> | Mission counts grouped by status |
| `Voyages` | array | Active [VoyageProgress](#voyageprogress) objects |
| `RecentSignals` | array | Recent [Signal](#signal) objects |
| `TimestampUtc` | datetime | Snapshot timestamp (UTC) |

---

#### VoyageProgress

Progress information for an active voyage, nested in ArmadaStatus.

| Field | Type | Description |
|---|---|---|
| `Voyage` | [Voyage](#voyage) | Voyage details |
| `TotalMissions` | int | Total missions in voyage |
| `CompletedMissions` | int | Number of completed missions |
| `FailedMissions` | int | Number of failed missions |
| `InProgressMissions` | int | Number of in-progress missions |

---

#### Dock

A git worktree provisioned for a captain. Docks are managed internally by the Admiral and are not directly created/deleted via API.

| Field | Type | Default | Description |
|---|---|---|---|
| `Id` | string | auto-generated | Unique ID with `dck_` prefix |
| `VesselId` | string | `""` | Vessel ID |
| `CaptainId` | string? | null | Captain currently using dock |
| `WorktreePath` | string? | null | Local filesystem path to worktree |
| `BranchName` | string? | null | Branch name checked out |
| `Active` | bool | true | Whether dock is active/usable |
| `CreatedUtc` | datetime | now | Creation timestamp (UTC) |
| `LastUpdateUtc` | datetime | now | Last update timestamp (UTC) |

---

### Enumerations

All enumerations serialize as strings in JSON (e.g., `"InProgress"`, not `2`).

#### MissionStatusEnum

| Value | Description |
|---|---|
| `Pending` | Created but not yet assigned to a captain |
| `Assigned` | Assigned to a captain, awaiting work start |
| `InProgress` | Captain is actively working |
| `Testing` | Work complete, under automated testing |
| `Review` | Awaiting human review |
| `Complete` | Successfully completed (terminal) |
| `Failed` | Mission failed (terminal) |
| `Cancelled` | Mission cancelled (terminal) |

---

#### VoyageStatusEnum

| Value | Description |
|---|---|
| `Open` | Created, missions being set up |
| `InProgress` | Has active missions in progress |
| `Complete` | All missions completed |
| `Cancelled` | Voyage was cancelled |

---

#### CaptainStateEnum

| Value | Description |
|---|---|
| `Idle` | Available for assignment |
| `Working` | Actively working on a mission |
| `Stalled` | Process appears stalled (no heartbeat) |
| `Stopping` | In the process of stopping |

---

#### AgentRuntimeEnum

| Value | Description |
|---|---|
| `ClaudeCode` | Anthropic Claude Code CLI |
| `Codex` | OpenAI Codex CLI |
| `Gemini` | Google Gemini CLI |
| `Cursor` | Cursor agent CLI |
| `Custom` | Custom agent runtime |

---

#### SignalTypeEnum

| Value | Description |
|---|---|
| `Assignment` | Mission assignment notification |
| `Progress` | Progress update from captain |
| `Completion` | Mission completion notification |
| `Error` | Error notification |
| `Heartbeat` | Heartbeat signal |
| `Nudge` | Ephemeral nudge message |
| `Mail` | Persistent mail message |

---

#### MergeStatusEnum

| Value | Description |
|---|---|
| `Queued` | Waiting to be picked up |
| `Testing` | Currently being tested |
| `Passed` | Tests passed, ready to land |
| `Failed` | Tests failed |
| `Landed` | Successfully merged into target branch |
| `Cancelled` | Removed from queue |

---

#### EnumerationOrderEnum

| Value | Description |
|---|---|
| `CreatedAscending` | Sort by creation date, oldest first |
| `CreatedDescending` | Sort by creation date, newest first (default) |

---

### Request Types

#### EnumerationQuery

Query parameters for paginated enumeration. Used as the POST body for all `/enumerate` endpoints.

```json
{
  "PageNumber": 1,
  "PageSize": 25,
  "Order": "CreatedDescending",
  "CreatedAfter": "2026-03-01T00:00:00Z",
  "CreatedBefore": null,
  "Status": "InProgress",
  "FleetId": null,
  "VesselId": "vsl_abc123",
  "CaptainId": null,
  "VoyageId": null,
  "MissionId": null,
  "EventType": null,
  "SignalType": null,
  "ToCaptainId": null,
  "UnreadOnly": null
}
```

All fields are optional. Omitted fields use defaults. See [Pagination](#pagination) for full details.

---

#### VoyageRequest

Request body for creating a voyage with missions.

```json
{
  "Title": "API Hardening",
  "Description": "Security improvements",
  "VesselId": "vsl_abc123",
  "Missions": [
    {"Title": "Add rate limiting", "Description": "Add rate limiting middleware"},
    {"Title": "Add input validation", "Description": "Validate all POST endpoints"}
  ]
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `Title` | string | yes | Voyage title |
| `Description` | string | no | Voyage description |
| `VesselId` | string | yes | Target vessel ID |
| `Missions` | array | no | List of MissionRequest objects |

---

#### MissionRequest

A mission within a VoyageRequest.

| Field | Type | Required | Description |
|---|---|---|---|
| `Title` | string | yes | Mission title |
| `Description` | string | no | Mission description/instructions |

---

#### StatusTransitionRequest

Request body for transitioning a mission status.

```json
{
  "Status": "InProgress"
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `Status` | string | yes | Target status name (case-insensitive) |

---

### Response Wrappers

#### EnumerationResult\<T\>

Paginated result wrapper returned by all list and enumerate endpoints.

```json
{
  "Success": true,
  "PageNumber": 1,
  "PageSize": 25,
  "TotalPages": 4,
  "TotalRecords": 87,
  "Objects": [ ... ],
  "TotalMs": 3.14
}
```

| Field | Type | Description |
|---|---|---|
| `Success` | bool | Whether the operation succeeded |
| `PageNumber` | int | Current page number (1-based) |
| `PageSize` | int | Number of items per page |
| `TotalPages` | int | Total number of pages |
| `TotalRecords` | long | Total records matching the query |
| `Objects` | array\<T\> | Result objects for this page |
| `TotalMs` | double | Query execution time in milliseconds |

---

#### VoyageDetail

Response from `GET /api/v1/voyages/{id}`.

```json
{
  "Voyage": { ... },
  "Missions": [ ... ]
}
```

| Field | Type | Description |
|---|---|---|
| `Voyage` | [Voyage](#voyage) | Voyage details |
| `Missions` | array\<[Mission](#mission)\> | All missions in this voyage |

---

#### MissionDiff

Response from `GET /api/v1/missions/{id}/diff`.

```json
{
  "MissionId": "msn_abc123",
  "Branch": "armada/msn_abc123",
  "Diff": "diff --git ..."
}
```

| Field | Type | Description |
|---|---|---|
| `MissionId` | string | Mission ID |
| `Branch` | string | Branch name |
| `Diff` | string | Git diff output |

---

#### MissionLog

Response from `GET /api/v1/missions/{id}/log`.

```json
{
  "MissionId": "msn_abc123",
  "Log": "line1\nline2\n...",
  "Lines": 100,
  "TotalLines": 542
}
```

| Field | Type | Description |
|---|---|---|
| `MissionId` | string | Mission ID |
| `Log` | string | Log content (newline-delimited) |
| `Lines` | integer | Number of lines returned |
| `TotalLines` | integer | Total lines in log file |

---

#### CaptainLog

Response from `GET /api/v1/captains/{id}/log`.

```json
{
  "CaptainId": "cpt_abc123",
  "Log": "line1\nline2\n...",
  "Lines": 100,
  "TotalLines": 203
}
```

| Field | Type | Description |
|---|---|---|
| `CaptainId` | string | Captain ID |
| `Log` | string | Log content (newline-delimited) |
| `Lines` | integer | Number of lines returned |
| `TotalLines` | integer | Total lines in log file |

---

## Endpoint Summary

| # | Method | URL | Description | Auth |
|---|---|---|---|---|
| 1 | GET | `/api/v1/status` | System status dashboard | Yes |
| 2 | GET | `/api/v1/status/health` | Health check | No |
| 3 | POST | `/api/v1/server/stop` | Graceful shutdown | Yes |
| 4 | GET | `/api/v1/fleets` | List fleets (paginated) | Yes |
| 5 | POST | `/api/v1/fleets/enumerate` | Enumerate fleets | Yes |
| 6 | POST | `/api/v1/fleets` | Create fleet | Yes |
| 7 | GET | `/api/v1/fleets/{id}` | Get fleet | Yes |
| 8 | PUT | `/api/v1/fleets/{id}` | Update fleet | Yes |
| 9 | DELETE | `/api/v1/fleets/{id}` | Delete fleet | Yes |
| 10 | GET | `/api/v1/vessels` | List vessels (paginated) | Yes |
| 11 | POST | `/api/v1/vessels/enumerate` | Enumerate vessels | Yes |
| 12 | POST | `/api/v1/vessels` | Create vessel | Yes |
| 13 | GET | `/api/v1/vessels/{id}` | Get vessel | Yes |
| 14 | PUT | `/api/v1/vessels/{id}` | Update vessel | Yes |
| 15 | DELETE | `/api/v1/vessels/{id}` | Delete vessel | Yes |
| 16 | GET | `/api/v1/voyages` | List voyages (paginated) | Yes |
| 17 | POST | `/api/v1/voyages/enumerate` | Enumerate voyages | Yes |
| 18 | POST | `/api/v1/voyages` | Create voyage with missions | Yes |
| 19 | GET | `/api/v1/voyages/{id}` | Get voyage with missions | Yes |
| 20 | DELETE | `/api/v1/voyages/{id}` | Cancel voyage | Yes |
| 21 | DELETE | `/api/v1/voyages/{id}/purge` | Permanently delete voyage | Yes |
| 22 | GET | `/api/v1/missions` | List missions (paginated) | Yes |
| 23 | POST | `/api/v1/missions/enumerate` | Enumerate missions | Yes |
| 24 | POST | `/api/v1/missions` | Create mission | Yes |
| 25 | GET | `/api/v1/missions/{id}` | Get mission | Yes |
| 26 | PUT | `/api/v1/missions/{id}` | Update mission | Yes |
| 27 | PUT | `/api/v1/missions/{id}/status` | Transition mission status | Yes |
| 28 | DELETE | `/api/v1/missions/{id}` | Cancel mission | Yes |
| 29 | POST | `/api/v1/missions/{id}/restart` | Restart failed/cancelled mission | Yes |
| 30 | GET | `/api/v1/missions/{id}/diff` | Get mission diff | Yes |
| 31 | GET | `/api/v1/missions/{id}/log` | Get mission log | Yes |
| 32 | GET | `/api/v1/captains` | List captains (paginated) | Yes |
| 33 | POST | `/api/v1/captains/enumerate` | Enumerate captains | Yes |
| 34 | POST | `/api/v1/captains` | Create captain | Yes |
| 35 | GET | `/api/v1/captains/{id}` | Get captain | Yes |
| 36 | PUT | `/api/v1/captains/{id}` | Update captain | Yes |
| 37 | POST | `/api/v1/captains/{id}/stop` | Stop captain | Yes |
| 38 | POST | `/api/v1/captains/stop-all` | Stop all captains | Yes |
| 39 | GET | `/api/v1/captains/{id}/log` | Get captain current log | Yes |
| 40 | DELETE | `/api/v1/captains/{id}` | Delete captain | Yes |
| 41 | GET | `/api/v1/signals` | List signals (paginated) | Yes |
| 42 | POST | `/api/v1/signals/enumerate` | Enumerate signals | Yes |
| 43 | POST | `/api/v1/signals` | Send signal | Yes |
| 44 | GET | `/api/v1/events` | List events (paginated) | Yes |
| 45 | POST | `/api/v1/events/enumerate` | Enumerate events | Yes |
| 46 | GET | `/api/v1/merge-queue` | List merge queue (paginated) | Yes |
| 47 | POST | `/api/v1/merge-queue/enumerate` | Enumerate merge queue | Yes |
| 48 | POST | `/api/v1/merge-queue` | Enqueue branch | Yes |
| 49 | GET | `/api/v1/merge-queue/{id}` | Get merge entry | Yes |
| 50 | DELETE | `/api/v1/merge-queue/{id}` | Cancel merge entry | Yes |
| 51 | POST | `/api/v1/merge-queue/process` | Process merge queue | Yes |

---

## Additional Ports

| Service | Default Port | Description |
|---|---|---|
| Admiral REST API | 7890 | This API |
| MCP Server | 7891 | Model Context Protocol (Voltaic) for AI tool use |
| WebSocket Hub | 7892 | Real-time event streaming and command interface |

## CORS

All responses include permissive CORS headers:
```
Access-Control-Allow-Origin: *
Access-Control-Allow-Methods: GET, POST, PUT, DELETE, OPTIONS
Access-Control-Allow-Headers: Content-Type, X-Api-Key
```
