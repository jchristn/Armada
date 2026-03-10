# Armada WebSocket API Reference

**Version:** 0.2.0
**Default URL:** `ws://localhost:7892`
**Protocol:** WebSocket (RFC 6455) via WatsonWebsocket
**Transport:** JSON text frames

---

## Table of Contents

- [Connection](#connection)
  - [URL Construction](#url-construction)
  - [Port Discovery](#port-discovery)
  - [SSL/TLS](#ssltls)
- [Message Format](#message-format)
- [Routes](#routes)
  - [subscribe](#subscribe)
  - [command](#command)
- [Server-Pushed Events](#server-pushed-events)
  - [status.snapshot](#statussnapshot)
  - [mission.changed](#missionchanged)
  - [captain.changed](#captainchanged)
  - [Generic Events](#generic-events)
- [Command Actions](#command-actions)
  - [Status & Control](#status--control)
  - [Fleet Actions](#fleet-actions)
  - [Vessel Actions](#vessel-actions)
  - [Voyage Actions](#voyage-actions)
  - [Mission Actions](#mission-actions)
  - [Captain Actions](#captain-actions)
  - [Signal Actions](#signal-actions)
  - [Event Actions](#event-actions)
  - [Dock Actions](#dock-actions)
  - [Merge Queue Actions](#merge-queue-actions)
  - [Enumerate](#enumerate)
- [Pagination](#pagination)
- [Mission Status Transitions](#mission-status-transitions)
- [Error Handling](#error-handling)
- [Data Types](#data-types)
  - [Models](#models)
  - [Enumerations](#enumerations)
- [Client Examples](#client-examples)
  - [JavaScript](#javascript)
  - [C# / .NET](#c--net)
  - [Python](#python)

---

## Connection

### URL Construction

The WebSocket server runs on a dedicated port, separate from the REST API. The default configuration is:

```
ws://localhost:7892
```

The port is configurable via `ArmadaSettings.WebSocketPort` (default: `7892`). The hostname matches the REST API's `RestSettings.Hostname` (default: `localhost`).

### Port Discovery

Clients can discover the WebSocket port dynamically by querying the REST API health endpoint:

```
GET http://localhost:7890/api/v1/status/health
```

The response includes the WebSocket port. If the health endpoint is unavailable, clients should fall back to `AdmiralPort + 2` (e.g., `7890 + 2 = 7892`).

### SSL/TLS

When `RestSettings.Ssl` is enabled, use `wss://` instead of `ws://`:

```
wss://localhost:7892
```

SSL applies to both the REST API and the WebSocket server.

---

## Message Format

All messages are JSON text frames. The `Route` property in the client-to-server message envelope must be **PascalCase**. Server responses use **camelCase** property naming.

### Client-to-Server

Messages sent from the client to the server must include a `Route` field to select the handler:

```json
{
  "Route": "subscribe"
}
```

```json
{
  "Route": "command",
  "action": "status"
}
```

### Server-to-Client

All server messages include a `type` field indicating the event kind, and a `timestamp` field with the UTC time:

```json
{
  "type": "mission.changed",
  "missionId": "msn_abc123",
  "status": "Complete",
  "title": "Implement feature X",
  "timestamp": "2026-03-07T12:34:56.789Z"
}
```

---

## Routes

### subscribe

Subscribe to real-time event broadcasts. Upon connection with this route, the server immediately sends a `status.snapshot` message containing the current Armada state.

**Client sends:**

```json
{
  "Route": "subscribe"
}
```

**Server responds with:** a [`status.snapshot`](#statussnapshot) message.

After the initial snapshot, the client will receive all broadcast events ([`mission.changed`](#missionchanged), [`captain.changed`](#captainchanged), and [generic events](#generic-events)) as they occur.

---

### command

Send a command to the Admiral for execution. The `action` field determines which command to run.

**Client sends:**

```json
{
  "Route": "command",
  "action": "<action_name>",
  ...additional fields depending on action
}
```

**Server responds with:** a `command.result` or `command.error` message.

See [Command Actions](#command-actions) for the full list of 35 supported actions.

---

## Server-Pushed Events

These events are broadcast to **all connected clients** whenever state changes occur in the Armada system. Clients do not need to request these — they are pushed automatically after subscribing.

### status.snapshot

Sent immediately when a client connects via the `subscribe` route. Contains a full snapshot of the current Armada state.

```json
{
  "type": "status.snapshot",
  "data": {
    "totalCaptains": 4,
    "idleCaptains": 1,
    "workingCaptains": 2,
    "stalledCaptains": 1,
    "activeVoyages": 2,
    "missionsByStatus": {
      "Pending": 3,
      "InProgress": 2,
      "Complete": 10,
      "Failed": 1
    },
    "voyages": [
      {
        "voyage": { "...": "Voyage object" },
        "totalMissions": 5,
        "completedMissions": 3,
        "failedMissions": 0,
        "inProgressMissions": 2
      }
    ],
    "recentSignals": [],
    "timestampUtc": "2026-03-07T12:34:56.789Z"
  },
  "timestamp": "2026-03-07T12:34:56.789Z"
}
```

**`data` field:** [ArmadaStatus](#armadastatus) object.

---

### mission.changed

Broadcast when a mission's status changes (e.g., assigned, started, completed, failed).

```json
{
  "type": "mission.changed",
  "missionId": "msn_abc123def456ghi789jk",
  "status": "InProgress",
  "title": "Add input validation to signup form",
  "timestamp": "2026-03-07T12:35:00.000Z"
}
```

| Field | Type | Description |
|---|---|---|
| `type` | string | Always `"mission.changed"` |
| `missionId` | string | Mission ID (prefix `msn_`) |
| `status` | string | New [MissionStatusEnum](#missionstatusenum) value |
| `title` | string \| null | Mission title |
| `timestamp` | string | ISO 8601 UTC timestamp |

---

### captain.changed

Broadcast when a captain's state changes (e.g., idle to working, working to stalled).

```json
{
  "type": "captain.changed",
  "captainId": "cpt_abc123def456ghi789jk",
  "state": "Working",
  "name": "captain-1",
  "timestamp": "2026-03-07T12:35:00.000Z"
}
```

| Field | Type | Description |
|---|---|---|
| `type` | string | Always `"captain.changed"` |
| `captainId` | string | Captain ID (prefix `cpt_`) |
| `state` | string | New [CaptainStateEnum](#captainstateenum) value |
| `name` | string \| null | Captain display name |
| `timestamp` | string | ISO 8601 UTC timestamp |

---

### Generic Events

Broadcast for general system events (e.g., escalation triggers, merge queue updates, voyage completion).

```json
{
  "type": "voyage.completed",
  "message": "Voyage 'Feature batch 1' completed successfully",
  "data": { "voyageId": "vyg_abc123def456ghi789jk" },
  "timestamp": "2026-03-07T12:35:00.000Z"
}
```

| Field | Type | Description |
|---|---|---|
| `type` | string | Event type string (e.g., `"voyage.completed"`, `"escalation.triggered"`) |
| `message` | string | Human-readable event description |
| `data` | object \| null | Optional additional event data |
| `timestamp` | string | ISO 8601 UTC timestamp |

---

## Command Actions

Commands are sent via the `command` route. Each command returns a `command.result` message on success or a `command.error` message on failure. The WebSocket command surface has full parity with the REST and MCP APIs.

### Command Actions Summary

| Category | Action | Description | Required Fields |
|---|---|---|---|
| **Status & Control** | `status` | Get current ArmadaStatus | — |
| | `stop_captain` | Stop specific captain | `captainId` |
| | `stop_all` | Emergency stop all captains | — |
| **Fleet** | `list_fleets` | List/enumerate fleets | optional `query` |
| | `get_fleet` | Get fleet by ID | `id` |
| | `create_fleet` | Create fleet | `data` |
| | `update_fleet` | Update fleet | `id`, `data` |
| | `delete_fleet` | Delete fleet | `id` |
| **Vessel** | `list_vessels` | List/enumerate vessels | optional `query` |
| | `get_vessel` | Get vessel by ID | `id` |
| | `create_vessel` | Create vessel | `data` |
| | `update_vessel` | Update vessel | `id`, `data` |
| | `update_vessel_context` | Update vessel project context and style guide | `id`, `data` |
| | `delete_vessel` | Delete vessel | `id` |
| **Voyage** | `list_voyages` | List/enumerate voyages | optional `query` |
| | `get_voyage` | Get voyage by ID | `id` |
| | `create_voyage` | Create voyage | `data` |
| | `cancel_voyage` | Cancel voyage | `id` |
| | `purge_voyage` | Permanently delete voyage and all missions | `id` |
| **Mission** | `list_missions` | List/enumerate missions | optional `query` |
| | `get_mission` | Get mission by ID | `id` |
| | `create_mission` | Create and dispatch mission | `data` |
| | `update_mission` | Update mission | `id`, `data` |
| | `transition_mission_status` | Transition mission status | `id`, `status` |
| | `cancel_mission` | Cancel mission | `id` |
| | `purge_mission` | Permanently delete mission | `id` |
| | `restart_mission` | Restart failed/cancelled mission | `id`, optional `data.title`, `data.description` |
| **Captain** | `list_captains` | List/enumerate captains | optional `query` |
| | `get_captain` | Get captain by ID | `id` |
| | `create_captain` | Create captain | `data` |
| | `update_captain` | Update captain (preserves operational fields) | `id`, `data` |
| | `delete_captain` | Delete captain (auto-recalls if working) | `id` |
| **Signal** | `list_signals` | List/enumerate signals | optional `query` |
| | `send_signal` | Create signal | `data` |
| **Event** | `list_events` | List/enumerate events | optional `query` |
| **Dock** | `list_docks` | List/enumerate docks | optional `query` |
| **Merge Queue** | `list_merge_queue` | List merge queue entries | optional `query` |
| | `get_merge_entry` | Get merge entry by ID | `id` |
| | `enqueue_merge` | Enqueue branch for merge | `data` |
| | `cancel_merge` | Cancel merge entry | `id` |
| | `process_merge_queue` | Process the merge queue | — |

---

### Status & Control

#### status

Get the current Armada status.

**Request:**

```json
{
  "Route": "command",
  "action": "status"
}
```

**Response:**

```json
{
  "type": "command.result",
  "action": "status",
  "data": {
    "totalCaptains": 4,
    "idleCaptains": 1,
    "workingCaptains": 2,
    "stalledCaptains": 1,
    "activeVoyages": 2,
    "missionsByStatus": { "Pending": 3, "InProgress": 2 },
    "voyages": [],
    "recentSignals": [],
    "timestampUtc": "2026-03-07T12:34:56.789Z"
  }
}
```

**`data` field:** [ArmadaStatus](#armadastatus) object.

---

#### stop_captain

Stop a specific captain agent.

**Request:**

```json
{
  "Route": "command",
  "action": "stop_captain",
  "captainId": "cpt_abc123def456ghi789jk"
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `"stop_captain"` |
| `captainId` | string | Yes | ID of the captain to stop |

**Response:**

```json
{
  "type": "command.result",
  "action": "stop_captain",
  "data": {
    "status": "stopped",
    "captainId": "cpt_abc123def456ghi789jk"
  }
}
```

---

#### stop_all

Emergency stop all running captains.

**Request:**

```json
{
  "Route": "command",
  "action": "stop_all"
}
```

**Response:**

```json
{
  "type": "command.result",
  "action": "stop_all",
  "data": {
    "status": "all_stopped"
  }
}
```

---

#### stop_server

Initiate a graceful shutdown of the Admiral server. The server will respond before shutting down after a brief delay.

**Request:**

```json
{
  "Route": "command",
  "action": "stop_server"
}
```

**Response:**

```json
{
  "type": "command.result",
  "action": "stop_server",
  "data": {
    "status": "shutting_down"
  }
}
```

---

### Fleet Actions

#### list_fleets

List or enumerate fleets with optional pagination and filtering.

**Request:**

```json
{
  "Route": "command",
  "action": "list_fleets",
  "query": {
    "pageNumber": 1,
    "pageSize": 25,
    "order": "CreatedDescending"
  }
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `"list_fleets"` |
| `query` | object | No | [EnumerationQuery](#enumerationquery) for pagination/filtering |

**Response:**

```json
{
  "type": "command.result",
  "action": "list_fleets",
  "data": {
    "success": true,
    "pageNumber": 1,
    "pageSize": 25,
    "totalPages": 1,
    "totalRecords": 3,
    "objects": [
      { "id": "flt_abc123", "name": "my-fleet", "...": "..." }
    ],
    "totalMs": 1.23
  }
}
```

---

#### get_fleet

Get a fleet by ID.

**Request:**

```json
{
  "Route": "command",
  "action": "get_fleet",
  "id": "flt_abc123"
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `"get_fleet"` |
| `id` | string | Yes | Fleet ID (prefix `flt_`) |

**Response:**

```json
{
  "type": "command.result",
  "action": "get_fleet",
  "data": {
    "id": "flt_abc123",
    "name": "my-fleet",
    "...": "..."
  }
}
```

---

#### create_fleet

Create a new fleet.

**Request:**

```json
{
  "Route": "command",
  "action": "create_fleet",
  "data": {
    "Name": "my-fleet"
  }
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `"create_fleet"` |
| `data` | object | Yes | Fleet creation data |

**Response:**

```json
{
  "type": "command.result",
  "action": "create_fleet",
  "data": {
    "id": "flt_abc123",
    "name": "my-fleet",
    "...": "..."
  }
}
```

---

#### update_fleet

Update an existing fleet.

**Request:**

```json
{
  "Route": "command",
  "action": "update_fleet",
  "id": "flt_abc123",
  "data": {
    "Name": "renamed-fleet"
  }
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `"update_fleet"` |
| `id` | string | Yes | Fleet ID (prefix `flt_`) |
| `data` | object | Yes | Fields to update |

**Response:**

```json
{
  "type": "command.result",
  "action": "update_fleet",
  "data": {
    "id": "flt_abc123",
    "name": "renamed-fleet",
    "...": "..."
  }
}
```

---

#### delete_fleet

Delete a fleet.

**Request:**

```json
{
  "Route": "command",
  "action": "delete_fleet",
  "id": "flt_abc123"
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `"delete_fleet"` |
| `id` | string | Yes | Fleet ID (prefix `flt_`) |

**Response:**

```json
{
  "type": "command.result",
  "action": "delete_fleet",
  "data": {
    "success": true
  }
}
```

---

### Vessel Actions

#### list_vessels

List or enumerate vessels with optional pagination and filtering.

**Request:**

```json
{
  "Route": "command",
  "action": "list_vessels",
  "query": {
    "pageNumber": 1,
    "pageSize": 50,
    "fleetId": "flt_abc123"
  }
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `"list_vessels"` |
| `query` | object | No | [EnumerationQuery](#enumerationquery) for pagination/filtering |

**Response:**

```json
{
  "type": "command.result",
  "action": "list_vessels",
  "data": {
    "success": true,
    "pageNumber": 1,
    "pageSize": 50,
    "totalPages": 1,
    "totalRecords": 5,
    "objects": [
      { "id": "vsl_abc123", "name": "my-repo", "...": "..." }
    ],
    "totalMs": 0.89
  }
}
```

---

#### get_vessel

Get a vessel by ID.

**Request:**

```json
{
  "Route": "command",
  "action": "get_vessel",
  "id": "vsl_abc123"
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `"get_vessel"` |
| `id` | string | Yes | Vessel ID (prefix `vsl_`) |

---

#### create_vessel

Create a new vessel.

**Request:**

```json
{
  "Route": "command",
  "action": "create_vessel",
  "data": {
    "Name": "my-repo",
    "FleetId": "flt_abc123",
    "RepositoryUrl": "https://github.com/org/repo.git"
  }
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `"create_vessel"` |
| `data` | object | Yes | Vessel creation data |

---

#### update_vessel

Update an existing vessel.

**Request:**

```json
{
  "Route": "command",
  "action": "update_vessel",
  "id": "vsl_abc123",
  "data": {
    "Name": "renamed-repo"
  }
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `"update_vessel"` |
| `id` | string | Yes | Vessel ID (prefix `vsl_`) |
| `data` | object | Yes | Fields to update |

---

#### update_vessel_context

Partial update of a vessel's project context and style guide fields only. Unlike `update_vessel`, this only modifies the `projectContext` and `styleGuide` fields.

**Request:**

```json
{
  "Route": "command",
  "action": "update_vessel_context",
  "id": "vsl_abc123",
  "data": {
    "ProjectContext": "C#/.NET multi-agent orchestration system...",
    "StyleGuide": "Use explicit types, no var keyword..."
  }
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `"update_vessel_context"` |
| `id` | string | Yes | Vessel ID (prefix `vsl_`) |
| `data.ProjectContext` | string | No | Project context describing architecture, key files, and dependencies |
| `data.StyleGuide` | string | No | Style guide describing naming conventions, patterns, and library preferences |

**Response:**

```json
{
  "type": "command.result",
  "action": "update_vessel_context",
  "data": {
    "id": "vsl_abc123",
    "name": "my-repo",
    "projectContext": "C#/.NET multi-agent orchestration system...",
    "styleGuide": "Use explicit types, no var keyword...",
    "...": "..."
  }
}
```

**Errors:** `command.error` if vessel not found.

---

#### delete_vessel

Delete a vessel.

**Request:**

```json
{
  "Route": "command",
  "action": "delete_vessel",
  "id": "vsl_abc123"
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `"delete_vessel"` |
| `id` | string | Yes | Vessel ID (prefix `vsl_`) |

---

### Voyage Actions

#### list_voyages

List or enumerate voyages with optional pagination and filtering.

**Request:**

```json
{
  "Route": "command",
  "action": "list_voyages",
  "query": {
    "pageNumber": 1,
    "pageSize": 25,
    "status": "InProgress"
  }
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `"list_voyages"` |
| `query` | object | No | [EnumerationQuery](#enumerationquery) for pagination/filtering |

---

#### get_voyage

Get a voyage by ID. Returns the voyage object along with its missions.

**Request:**

```json
{
  "Route": "command",
  "action": "get_voyage",
  "id": "vyg_abc123"
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `"get_voyage"` |
| `id` | string | Yes | Voyage ID (prefix `vyg_`) |

**Response:**

```json
{
  "type": "command.result",
  "action": "get_voyage",
  "data": {
    "voyage": { "id": "vyg_abc123", "title": "Feature batch", "...": "..." },
    "missions": [
      { "id": "msn_abc123", "title": "Task 1", "status": "Complete", "...": "..." }
    ]
  }
}
```

---

#### create_voyage

Create a new voyage. Optionally include a `vesselId` and `missions[]` array for immediate dispatch.

**Request (basic):**

```json
{
  "Route": "command",
  "action": "create_voyage",
  "data": {
    "Title": "Feature batch 1",
    "Description": "Implement auth features"
  }
}
```

**Request (with missions for dispatch):**

```json
{
  "Route": "command",
  "action": "create_voyage",
  "data": {
    "Title": "Feature batch 1",
    "VesselId": "vsl_abc123",
    "Missions": [
      { "Title": "Add login page", "Description": "Create login form" },
      { "Title": "Add signup page", "Description": "Create signup form" }
    ]
  }
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `"create_voyage"` |
| `data` | object | Yes | Voyage creation data |
| `data.VesselId` | string | No | Target vessel for missions |
| `data.Missions` | array | No | Array of mission objects to create and dispatch |

---

#### cancel_voyage

Cancel a voyage. All pending and assigned missions are also cancelled.

**Request:**

```json
{
  "Route": "command",
  "action": "cancel_voyage",
  "id": "vyg_abc123"
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `"cancel_voyage"` |
| `id` | string | Yes | Voyage ID (prefix `vyg_`) |

---

#### purge_voyage

Permanently delete a voyage and all of its missions.

**Request:**

```json
{
  "Route": "command",
  "action": "purge_voyage",
  "id": "vyg_abc123"
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `"purge_voyage"` |
| `id` | string | Yes | Voyage ID (prefix `vyg_`) |

---

### Mission Actions

#### list_missions

List or enumerate missions with optional pagination and filtering.

**Request:**

```json
{
  "Route": "command",
  "action": "list_missions",
  "query": {
    "pageNumber": 1,
    "pageSize": 50,
    "voyageId": "vyg_abc123",
    "status": "InProgress"
  }
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `"list_missions"` |
| `query` | object | No | [EnumerationQuery](#enumerationquery) for pagination/filtering |

---

#### get_mission

Get a mission by ID.

**Request:**

```json
{
  "Route": "command",
  "action": "get_mission",
  "id": "msn_abc123"
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `"get_mission"` |
| `id` | string | Yes | Mission ID (prefix `msn_`) |

---

#### create_mission

Create a new mission and dispatch it for assignment.

**Request:**

```json
{
  "Route": "command",
  "action": "create_mission",
  "data": {
    "Title": "Implement feature X",
    "Description": "Add the feature X to the system",
    "VesselId": "vsl_abc123",
    "VoyageId": "vyg_abc123",
    "Priority": 50
  }
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `"create_mission"` |
| `data` | object | Yes | Mission creation data |

---

#### update_mission

Update an existing mission.

**Request:**

```json
{
  "Route": "command",
  "action": "update_mission",
  "id": "msn_abc123",
  "data": {
    "Title": "Updated title",
    "Priority": 10
  }
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `"update_mission"` |
| `id` | string | Yes | Mission ID (prefix `msn_`) |
| `data` | object | Yes | Fields to update |

---

#### transition_mission_status

Transition a mission to a new status. The transition must be valid according to the [Mission Status Transitions](#mission-status-transitions) rules.

**Request:**

```json
{
  "Route": "command",
  "action": "transition_mission_status",
  "id": "msn_abc123",
  "status": "Complete"
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `"transition_mission_status"` |
| `id` | string | Yes | Mission ID (prefix `msn_`) |
| `status` | string | Yes | Target [MissionStatusEnum](#missionstatusenum) value |

**Response:**

```json
{
  "type": "command.result",
  "action": "transition_mission_status",
  "data": {
    "id": "msn_abc123",
    "status": "Complete",
    "...": "..."
  }
}
```

---

#### cancel_mission

Cancel a mission.

**Request:**

```json
{
  "Route": "command",
  "action": "cancel_mission",
  "id": "msn_abc123"
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `"cancel_mission"` |
| `id` | string | Yes | Mission ID (prefix `msn_`) |

---

#### purge_mission

Permanently delete a mission from the database. This action is irreversible.

**Request:**

```json
{
  "Route": "command",
  "action": "purge_mission",
  "id": "msn_abc123"
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `"purge_mission"` |
| `id` | string | Yes | Mission ID (prefix `msn_`) |

**Response:**

```json
{
  "type": "command.result",
  "action": "purge_mission",
  "data": {
    "status": "deleted",
    "missionId": "msn_abc123"
  }
}
```

**Errors:** `command.error` if mission not found.

---

#### restart_mission

Restart a failed or cancelled mission, resetting it to `Pending` for re-dispatch. Optionally update the title and description before restarting.

**Request:**

```json
{
  "Route": "command",
  "action": "restart_mission",
  "id": "msn_abc123",
  "data": {
    "title": "Updated title",
    "description": "Updated instructions"
  }
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `"restart_mission"` |
| `id` | string | Yes | Mission ID (prefix `msn_`) |
| `data.title` | string | No | New title. Omit to keep original. |
| `data.description` | string | No | New description. Omit to keep original. |

**Response:**

```json
{
  "type": "command.result",
  "action": "restart_mission",
  "data": {
    "id": "msn_abc123",
    "status": "Pending",
    "title": "Updated title",
    "..."
  }
}
```

**Errors:** `command.error` if mission not found or not in `Failed`/`Cancelled` status.

---

#### get_mission_diff

Get the git diff for a mission. Returns a saved diff file if available, otherwise attempts a live diff from the worktree.

**Request:**

```json
{
  "Route": "command",
  "action": "get_mission_diff",
  "id": "msn_abc123"
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `"get_mission_diff"` |
| `id` | string | Yes | Mission ID (prefix `msn_`) |

**Response:**

```json
{
  "type": "command.result",
  "action": "get_mission_diff",
  "data": {
    "missionId": "msn_abc123",
    "branch": "armada/msn_abc123",
    "diff": "diff --git a/file.cs..."
  }
}
```

---

#### get_mission_log

Get the session log for a mission with pagination support.

**Request:**

```json
{
  "Route": "command",
  "action": "get_mission_log",
  "id": "msn_abc123",
  "lines": 50,
  "offset": 0
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `"get_mission_log"` |
| `id` | string | Yes | Mission ID (prefix `msn_`) |
| `lines` | integer | No | Number of lines to return (default 100) |
| `offset` | integer | No | Line offset to start from (default 0) |

**Response:**

```json
{
  "type": "command.result",
  "action": "get_mission_log",
  "data": {
    "missionId": "msn_abc123",
    "log": "line1\nline2\n...",
    "lines": 50,
    "totalLines": 200
  }
}
```

---

### Captain Actions

#### list_captains

List or enumerate captains with optional pagination and filtering.

**Request:**

```json
{
  "Route": "command",
  "action": "list_captains",
  "query": {
    "pageNumber": 1,
    "pageSize": 25
  }
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `"list_captains"` |
| `query` | object | No | [EnumerationQuery](#enumerationquery) for pagination/filtering |

---

#### get_captain

Get a captain by ID.

**Request:**

```json
{
  "Route": "command",
  "action": "get_captain",
  "id": "cpt_abc123"
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `"get_captain"` |
| `id` | string | Yes | Captain ID (prefix `cpt_`) |

---

#### create_captain

Create a new captain.

**Request:**

```json
{
  "Route": "command",
  "action": "create_captain",
  "data": {
    "Name": "captain-1",
    "Runtime": "ClaudeCode"
  }
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `"create_captain"` |
| `data` | object | Yes | Captain creation data |

---

#### update_captain

Update an existing captain. Operational fields (state, current mission, heartbeat) are preserved and cannot be overwritten.

**Request:**

```json
{
  "Route": "command",
  "action": "update_captain",
  "id": "cpt_abc123",
  "data": {
    "Name": "captain-primary"
  }
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `"update_captain"` |
| `id` | string | Yes | Captain ID (prefix `cpt_`) |
| `data` | object | Yes | Fields to update |

---

#### delete_captain

Delete a captain. If the captain is currently working, it is automatically recalled before deletion.

**Request:**

```json
{
  "Route": "command",
  "action": "delete_captain",
  "id": "cpt_abc123"
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `"delete_captain"` |
| `id` | string | Yes | Captain ID (prefix `cpt_`) |

---

#### get_captain_log

Get the current session log for a captain with pagination support. The log is resolved via the `.current` pointer file.

**Request:**

```json
{
  "Route": "command",
  "action": "get_captain_log",
  "id": "cpt_abc123",
  "lines": 50,
  "offset": 0
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `"get_captain_log"` |
| `id` | string | Yes | Captain ID (prefix `cpt_`) |
| `lines` | integer | No | Number of lines to return (default 100) |
| `offset` | integer | No | Line offset to start from (default 0) |

**Response:**

```json
{
  "type": "command.result",
  "action": "get_captain_log",
  "data": {
    "captainId": "cpt_abc123",
    "log": "line1\nline2\n...",
    "lines": 50,
    "totalLines": 150
  }
}
```

---

### Signal Actions

#### list_signals

List or enumerate signals with optional pagination and filtering.

**Request:**

```json
{
  "Route": "command",
  "action": "list_signals",
  "query": {
    "toCaptainId": "cpt_abc123",
    "unreadOnly": true
  }
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `"list_signals"` |
| `query` | object | No | [EnumerationQuery](#enumerationquery) for pagination/filtering |

---

#### send_signal

Create and send a signal.

**Request:**

```json
{
  "Route": "command",
  "action": "send_signal",
  "data": {
    "ToCaptainId": "cpt_abc123",
    "Type": "Nudge",
    "Payload": "{\"message\": \"Please check the test failures\"}"
  }
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `"send_signal"` |
| `data` | object | Yes | Signal creation data |

---

### Event Actions

#### list_events

List or enumerate events with optional pagination and filtering.

**Request:**

```json
{
  "Route": "command",
  "action": "list_events",
  "query": {
    "pageSize": 50,
    "eventType": "escalation.triggered"
  }
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `"list_events"` |
| `query` | object | No | [EnumerationQuery](#enumerationquery) for pagination/filtering |

---

### Dock Actions

#### list_docks

List or enumerate docks (git worktrees) with optional pagination and filtering.

**Request:**

```json
{
  "Route": "command",
  "action": "list_docks",
  "query": {
    "vesselId": "vsl_abc123"
  }
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `"list_docks"` |
| `query` | object | No | [EnumerationQuery](#enumerationquery) for pagination/filtering |

---

### Merge Queue Actions

#### list_merge_queue

List merge queue entries with optional pagination and filtering.

**Request:**

```json
{
  "Route": "command",
  "action": "list_merge_queue",
  "query": {
    "pageNumber": 1,
    "pageSize": 25
  }
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `"list_merge_queue"` |
| `query` | object | No | [EnumerationQuery](#enumerationquery) for pagination/filtering |

---

#### get_merge_entry

Get a merge queue entry by ID.

**Request:**

```json
{
  "Route": "command",
  "action": "get_merge_entry",
  "id": "mrg_abc123"
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `"get_merge_entry"` |
| `id` | string | Yes | Merge entry ID |

---

#### enqueue_merge

Enqueue a branch for merge.

**Request:**

```json
{
  "Route": "command",
  "action": "enqueue_merge",
  "data": {
    "VesselId": "vsl_abc123",
    "BranchName": "feature/my-feature",
    "MissionId": "msn_abc123"
  }
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `"enqueue_merge"` |
| `data` | object | Yes | Merge entry creation data |

---

#### cancel_merge

Cancel a merge queue entry.

**Request:**

```json
{
  "Route": "command",
  "action": "cancel_merge",
  "id": "mrg_abc123"
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `"cancel_merge"` |
| `id` | string | Yes | Merge entry ID |

---

#### process_merge_queue

Trigger processing of the merge queue.

**Request:**

```json
{
  "Route": "command",
  "action": "process_merge_queue"
}
```

**Response:**

```json
{
  "type": "command.result",
  "action": "process_merge_queue",
  "data": {
    "success": true
  }
}
```

---

### Enumerate

#### enumerate

Generic paginated enumeration of any entity type with filtering and sorting. This is the WebSocket equivalent of the REST `POST /api/v1/{entity}/enumerate` endpoints and the MCP `armada_enumerate` tool.

**Request:**

```json
{
  "Route": "command",
  "action": "enumerate",
  "entityType": "missions",
  "query": {
    "pageNumber": 2,
    "pageSize": 25,
    "status": "InProgress"
  }
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `"enumerate"` |
| `entityType` | string | Yes | Entity type to enumerate (see table below) |
| `query` | object | No | [EnumerationQuery](#enumerationquery) for pagination/filtering |

**Supported entity types:**

| `entityType` value | Supported filters |
|---|---|
| `fleets` | `createdAfter`, `createdBefore` |
| `vessels` | `fleetId`, `createdAfter`, `createdBefore` |
| `captains` | `status` (Idle/Working/Stalled), `createdAfter`, `createdBefore` |
| `missions` | `status`, `vesselId`, `captainId`, `voyageId`, `createdAfter`, `createdBefore` |
| `voyages` | `status` (Active/Complete/Cancelled), `createdAfter`, `createdBefore` |
| `docks` | `vesselId`, `createdAfter`, `createdBefore` |
| `signals` | `signalType`, `captainId`, `toCaptainId`, `unreadOnly`, `createdAfter`, `createdBefore` |
| `events` | `eventType`, `captainId`, `missionId`, `vesselId`, `voyageId`, `createdAfter`, `createdBefore` |
| `merge_queue` | `status` (Queued/Testing/Passed/Failed/Landed/Cancelled), `createdAfter`, `createdBefore` |

Singular forms (e.g., `"fleet"`, `"mission"`) are also accepted.

**Response:**

```json
{
  "type": "command.result",
  "action": "enumerate",
  "data": {
    "objects": [ ... ],
    "totalRecords": 42,
    "pageSize": 25,
    "pageNumber": 2,
    "totalPages": 2,
    "success": true,
    "totalMs": 1.23
  }
}
```

**Error (unknown entity type):**

```json
{
  "type": "command.error",
  "action": "enumerate",
  "error": "Unknown entity type: bananas. Valid types: fleets, vessels, captains, missions, voyages, docks, signals, events, merge_queue"
}
```

---

## Pagination

All `list_*` actions and the `enumerate` action support an optional `query` object for pagination and filtering via [EnumerationQuery](#enumerationquery).

### EnumerationQuery

| Field | Type | Default | Description |
|---|---|---|---|
| `pageNumber` | int | 1 | Page number (1-based) |
| `pageSize` | int | 100 | Results per page (min 1, max 1000) |
| `order` | string | `"CreatedDescending"` | Sort order: `"CreatedAscending"` or `"CreatedDescending"` |
| `createdAfter` | string | null | Filter: only records created after this ISO 8601 datetime |
| `createdBefore` | string | null | Filter: only records created before this ISO 8601 datetime |
| `status` | string | null | Filter by status string (e.g., `"InProgress"`, `"Pending"`) |
| `fleetId` | string | null | Filter by fleet ID |
| `vesselId` | string | null | Filter by vessel ID |
| `captainId` | string | null | Filter by captain ID |
| `voyageId` | string | null | Filter by voyage ID |
| `missionId` | string | null | Filter by mission ID |
| `eventType` | string | null | Filter events by type |
| `signalType` | string | null | Filter signals by [SignalTypeEnum](#signaltypeenum) |
| `toCaptainId` | string | null | Filter signals by recipient captain |
| `unreadOnly` | bool | false | Filter signals to unread only |

### Enumeration Response Format

All `list_*` actions return a paginated response:

```json
{
  "type": "command.result",
  "action": "list_missions",
  "data": {
    "success": true,
    "pageNumber": 1,
    "pageSize": 100,
    "totalPages": 3,
    "totalRecords": 245,
    "objects": [ "..." ],
    "totalMs": 2.45
  }
}
```

| Field | Type | Description |
|---|---|---|
| `success` | bool | Whether the query succeeded |
| `pageNumber` | int | Current page number |
| `pageSize` | int | Results per page |
| `totalPages` | int | Total number of pages |
| `totalRecords` | int | Total matching records |
| `objects` | array | Array of result objects |
| `totalMs` | float | Query execution time in milliseconds |

---

## Mission Status Transitions

Not all status transitions are valid. The following table documents the allowed transitions:

| From | Allowed To |
|---|---|
| `Pending` | `Assigned`, `Cancelled` |
| `Assigned` | `InProgress`, `Cancelled` |
| `InProgress` | `Testing`, `Review`, `Complete`, `Failed`, `Cancelled` |
| `Testing` | `Review`, `InProgress`, `Complete`, `Failed` |
| `Review` | `Complete`, `InProgress`, `Failed` |

Invalid transitions will return a `command.error` response.

---

## Error Handling

### Command Errors

When a command fails, the server returns a `command.error` message:

```json
{
  "type": "command.error",
  "action": "get_fleet",
  "error": "Fleet not found"
}
```

If the command body cannot be parsed or an exception occurs:

```json
{
  "type": "command.error",
  "error": "Unexpected character encountered while parsing value"
}
```

### Unknown Actions

Sending an unrecognized `action` value returns:

```json
{
  "type": "command.error",
  "action": "bad_action",
  "error": "Unknown action: bad_action"
}
```

### Unknown Routes

Sending a message to a route other than `subscribe` or `command` returns:

```json
{
  "type": "error",
  "message": "Unknown route: bad_route"
}
```

### No Route Specified

If a message is sent without a route:

```json
{
  "type": "error",
  "message": "Send a message with route 'subscribe' or 'command'"
}
```

---

## Data Types

### Models

#### ArmadaStatus

| Field | Type | Description |
|---|---|---|
| `totalCaptains` | int | Total registered captains |
| `idleCaptains` | int | Captains in Idle state |
| `workingCaptains` | int | Captains in Working state |
| `stalledCaptains` | int | Captains in Stalled state |
| `activeVoyages` | int | Number of active (non-complete) voyages |
| `missionsByStatus` | object | Map of status string to count (e.g., `{"Pending": 3, "InProgress": 2}`) |
| `voyages` | array | List of [VoyageProgress](#voyageprogress) objects |
| `recentSignals` | array | List of recent [Signal](#signal) objects |
| `timestampUtc` | string | ISO 8601 UTC timestamp of the snapshot |

#### VoyageProgress

| Field | Type | Description |
|---|---|---|
| `voyage` | object | [Voyage](#voyage) object |
| `totalMissions` | int | Total missions in this voyage |
| `completedMissions` | int | Missions with status Complete |
| `failedMissions` | int | Missions with status Failed |
| `inProgressMissions` | int | Missions currently in progress |

#### Voyage

| Field | Type | Description |
|---|---|---|
| `id` | string | Voyage ID (prefix `vyg_`) |
| `title` | string | Voyage title |
| `description` | string \| null | Voyage description |
| `status` | string | [VoyageStatusEnum](#voyagestatusenum) value |
| `createdUtc` | string | ISO 8601 creation timestamp |
| `completedUtc` | string \| null | ISO 8601 completion timestamp |
| `lastUpdateUtc` | string | ISO 8601 last update timestamp |
| `autoPush` | bool \| null | Override global auto-push setting |
| `autoCreatePullRequests` | bool \| null | Override global auto-create PR setting |
| `autoMergePullRequests` | bool \| null | Override global auto-merge PR setting |

#### Vessel

| Field | Type | Description |
|---|---|---|
| `id` | string | Vessel ID (prefix `vsl_`) |
| `fleetId` | string \| null | Parent fleet ID |
| `name` | string | Vessel name |
| `repoUrl` | string \| null | Remote repository URL |
| `localPath` | string \| null | Local path to the bare repository clone |
| `workingDirectory` | string \| null | Local working directory for merge on completion |
| `defaultBranch` | string | Default branch name (default `"main"`) |
| `projectContext` | string \| null | Project context describing architecture, key files, and dependencies |
| `styleGuide` | string \| null | Style guide describing naming conventions, patterns, and library preferences |
| `active` | bool | Whether the vessel is active |
| `createdUtc` | string | ISO 8601 creation timestamp |
| `lastUpdateUtc` | string | ISO 8601 last update timestamp |

#### Mission

| Field | Type | Description |
|---|---|---|
| `id` | string | Mission ID (prefix `msn_`) |
| `voyageId` | string \| null | Parent voyage ID |
| `vesselId` | string \| null | Target vessel ID |
| `captainId` | string \| null | Assigned captain ID |
| `title` | string | Mission title |
| `description` | string \| null | Mission description |
| `status` | string | [MissionStatusEnum](#missionstatusenum) value |
| `priority` | int | Priority (lower = higher priority, default 100) |
| `parentMissionId` | string \| null | Parent mission ID for sub-tasks |
| `branchName` | string \| null | Git branch created for this mission |
| `dockId` | string \| null | Assigned dock ID for this mission's worktree |
| `processId` | int \| null | OS process ID for the agent working this mission |
| `prUrl` | string \| null | Pull request URL |
| `commitHash` | string \| null | Git commit hash (HEAD) captured at mission completion |
| `diffSnapshot` | string \| null | Saved git diff snapshot captured at mission completion |
| `createdUtc` | string | ISO 8601 creation timestamp |
| `startedUtc` | string \| null | ISO 8601 start timestamp |
| `completedUtc` | string \| null | ISO 8601 completion timestamp |
| `lastUpdateUtc` | string | ISO 8601 last update timestamp |

#### Captain

| Field | Type | Description |
|---|---|---|
| `id` | string | Captain ID (prefix `cpt_`) |
| `name` | string | Display name |
| `runtime` | string | [AgentRuntimeEnum](#agentruntimeenum) value |
| `maxParallelism` | int | Maximum concurrent missions (default 1, minimum 1) |
| `state` | string | [CaptainStateEnum](#captainstateenum) value |
| `currentMissionId` | string \| null | Currently assigned mission |
| `currentDockId` | string \| null | Currently assigned dock (worktree) |
| `processId` | int \| null | OS process ID |
| `recoveryAttempts` | int | Number of recovery attempts |
| `lastHeartbeatUtc` | string \| null | ISO 8601 last heartbeat timestamp |
| `createdUtc` | string | ISO 8601 creation timestamp |
| `lastUpdateUtc` | string | ISO 8601 last update timestamp |

#### Signal

| Field | Type | Description |
|---|---|---|
| `id` | string | Signal ID (prefix `sig_`) |
| `fromCaptainId` | string \| null | Sender captain ID (null = Admiral) |
| `toCaptainId` | string \| null | Recipient captain ID (null = Admiral) |
| `type` | string | [SignalTypeEnum](#signaltypeenum) value |
| `payload` | string \| null | JSON payload string |
| `read` | bool | Whether the signal has been read |
| `createdUtc` | string | ISO 8601 creation timestamp |

#### ArmadaEvent

| Field | Type | Description |
|---|---|---|
| `id` | string | Event ID |
| `type` | string | Event type string |
| `message` | string | Human-readable description |
| `data` | string \| null | JSON data payload |
| `createdUtc` | string | ISO 8601 creation timestamp |

#### Dock

| Field | Type | Description |
|---|---|---|
| `id` | string | Dock ID (prefix `dck_`) |
| `vesselId` | string | Parent vessel ID |
| `captainId` | string \| null | Assigned captain ID |
| `worktreePath` | string \| null | Filesystem path to the worktree |
| `branchName` | string \| null | Current branch name |
| `active` | bool | Whether the dock is active and usable |
| `createdUtc` | string | ISO 8601 creation timestamp |
| `lastUpdateUtc` | string | ISO 8601 last update timestamp |

#### MergeEntry

| Field | Type | Description |
|---|---|---|
| `id` | string | Merge entry ID |
| `vesselId` | string | Target vessel ID |
| `missionId` | string \| null | Associated mission ID |
| `branchName` | string | Branch to merge |
| `status` | string | [MergeStatusEnum](#mergestatusenum) value |
| `createdUtc` | string | ISO 8601 creation timestamp |
| `completedUtc` | string \| null | ISO 8601 completion timestamp |

---

### Enumerations

#### MissionStatusEnum

| Value | Description |
|---|---|
| `Pending` | Not yet assigned to a captain |
| `Assigned` | Assigned to a captain, not yet started |
| `InProgress` | Captain actively working |
| `Testing` | Work complete, under automated testing |
| `Review` | Awaiting human review |
| `Complete` | Successfully completed |
| `Failed` | Mission failed |
| `Cancelled` | Mission was cancelled |

#### VoyageStatusEnum

| Value | Description |
|---|---|
| `Open` | Voyage created, missions being set up |
| `InProgress` | Has active missions in progress |
| `Complete` | All missions completed |
| `Cancelled` | Voyage was cancelled |

#### CaptainStateEnum

| Value | Description |
|---|---|
| `Idle` | Available for assignment |
| `Working` | Actively working on a mission |
| `Stalled` | Process appears stalled (no heartbeat) |
| `Stopping` | In process of stopping |

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

#### AgentRuntimeEnum

| Value | Description |
|---|---|
| `ClaudeCode` | Anthropic Claude Code CLI |
| `Codex` | OpenAI Codex CLI |
| `Gemini` | Google Gemini CLI |
| `Cursor` | Cursor agent CLI |
| `Custom` | Custom agent runtime |

#### MergeStatusEnum

| Value | Description |
|---|---|
| `Pending` | Waiting in the queue |
| `InProgress` | Currently being merged |
| `Complete` | Successfully merged |
| `Failed` | Merge failed |
| `Cancelled` | Merge was cancelled |

#### EnumerationOrderEnum

| Value | Description |
|---|---|
| `CreatedAscending` | Sort by creation time, oldest first |
| `CreatedDescending` | Sort by creation time, newest first |

---

## Client Examples

### JavaScript

```javascript
const ws = new WebSocket("ws://localhost:7892");

ws.onopen = () => {
  // Subscribe to receive real-time broadcasts
  ws.send(JSON.stringify({ Route: "subscribe" }));
};

ws.onmessage = (event) => {
  const msg = JSON.parse(event.data);

  switch (msg.type) {
    case "status.snapshot":
      console.log("Initial status:", msg.data);
      break;
    case "mission.changed":
      console.log(`Mission ${msg.missionId}: ${msg.status}`);
      break;
    case "captain.changed":
      console.log(`Captain ${msg.captainId}: ${msg.state}`);
      break;
    case "command.result":
      console.log(`Command '${msg.action}' result:`, msg.data);
      break;
    case "command.error":
      console.error(`Command '${msg.action}' error:`, msg.error);
      break;
  }
};

// List fleets with pagination
ws.send(JSON.stringify({
  Route: "command",
  action: "list_fleets",
  query: { pageNumber: 1, pageSize: 25 }
}));

// Get a specific fleet
ws.send(JSON.stringify({
  Route: "command",
  action: "get_fleet",
  id: "flt_abc123def456ghi789jk"
}));

// Create a new voyage with missions
ws.send(JSON.stringify({
  Route: "command",
  action: "create_voyage",
  data: {
    Title: "Feature batch 1",
    VesselId: "vsl_abc123def456ghi789jk",
    Missions: [
      { Title: "Add login page", Description: "Create the login form" },
      { Title: "Add signup page", Description: "Create the signup form" }
    ]
  }
}));

// Transition a mission status
ws.send(JSON.stringify({
  Route: "command",
  action: "transition_mission_status",
  id: "msn_abc123def456ghi789jk",
  status: "Complete"
}));

// Update a captain
ws.send(JSON.stringify({
  Route: "command",
  action: "update_captain",
  id: "cpt_abc123def456ghi789jk",
  data: { Name: "captain-primary" }
}));

// Delete a vessel
ws.send(JSON.stringify({
  Route: "command",
  action: "delete_vessel",
  id: "vsl_abc123def456ghi789jk"
}));

// Stop a specific captain
ws.send(JSON.stringify({
  Route: "command",
  action: "stop_captain",
  captainId: "cpt_abc123def456ghi789jk"
}));
```

### C# / .NET

```csharp
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

ClientWebSocket ws = new ClientWebSocket();
await ws.ConnectAsync(new Uri("ws://localhost:7892"), CancellationToken.None);

// Subscribe to broadcasts
string subscribe = JsonSerializer.Serialize(new { Route = "subscribe" });
byte[] subscribeBytes = Encoding.UTF8.GetBytes(subscribe);
await ws.SendAsync(subscribeBytes, WebSocketMessageType.Text, true, CancellationToken.None);

// Send a command: list missions filtered by voyage
string listMissions = JsonSerializer.Serialize(new
{
    Route = "command",
    action = "list_missions",
    query = new
    {
        pageNumber = 1,
        pageSize = 50,
        voyageId = "vyg_abc123def456ghi789jk",
        status = "InProgress"
    }
});
byte[] listBytes = Encoding.UTF8.GetBytes(listMissions);
await ws.SendAsync(listBytes, WebSocketMessageType.Text, true, CancellationToken.None);

// Send a command: create a fleet
string createFleet = JsonSerializer.Serialize(new
{
    Route = "command",
    action = "create_fleet",
    data = new { Name = "my-fleet" }
});
byte[] createBytes = Encoding.UTF8.GetBytes(createFleet);
await ws.SendAsync(createBytes, WebSocketMessageType.Text, true, CancellationToken.None);

// Send a command: transition mission status
string transitionMission = JsonSerializer.Serialize(new
{
    Route = "command",
    action = "transition_mission_status",
    id = "msn_abc123def456ghi789jk",
    status = "Complete"
});
byte[] transitionBytes = Encoding.UTF8.GetBytes(transitionMission);
await ws.SendAsync(transitionBytes, WebSocketMessageType.Text, true, CancellationToken.None);

// Receive messages
byte[] buffer = new byte[8192];
while (ws.State == WebSocketState.Open)
{
    WebSocketReceiveResult result = await ws.ReceiveAsync(buffer, CancellationToken.None);
    string json = Encoding.UTF8.GetString(buffer, 0, result.Count);
    JsonDocument doc = JsonDocument.Parse(json);
    string type = doc.RootElement.GetProperty("type").GetString() ?? "";

    switch (type)
    {
        case "status.snapshot":
            Console.WriteLine($"Status snapshot received");
            break;
        case "mission.changed":
            string missionId = doc.RootElement.GetProperty("missionId").GetString() ?? "";
            string missionStatus = doc.RootElement.GetProperty("status").GetString() ?? "";
            Console.WriteLine($"Mission {missionId}: {missionStatus}");
            break;
        case "captain.changed":
            string captainId = doc.RootElement.GetProperty("captainId").GetString() ?? "";
            string state = doc.RootElement.GetProperty("state").GetString() ?? "";
            Console.WriteLine($"Captain {captainId}: {state}");
            break;
        case "command.result":
            string action = doc.RootElement.GetProperty("action").GetString() ?? "";
            Console.WriteLine($"Command '{action}' succeeded");
            break;
        case "command.error":
            string error = doc.RootElement.GetProperty("error").GetString() ?? "";
            Console.WriteLine($"Command error: {error}");
            break;
    }
}
```

### Python

```python
import asyncio
import json
import websockets

async def main():
    async with websockets.connect("ws://localhost:7892") as ws:
        # Subscribe to broadcasts
        await ws.send(json.dumps({"Route": "subscribe"}))

        # List fleets with pagination
        await ws.send(json.dumps({
            "Route": "command",
            "action": "list_fleets",
            "query": {"pageNumber": 1, "pageSize": 25}
        }))

        # Get a specific voyage
        await ws.send(json.dumps({
            "Route": "command",
            "action": "get_voyage",
            "id": "vyg_abc123def456ghi789jk"
        }))

        # Create a mission
        await ws.send(json.dumps({
            "Route": "command",
            "action": "create_mission",
            "data": {
                "Title": "Implement feature X",
                "Description": "Add feature X to the system",
                "VesselId": "vsl_abc123def456ghi789jk",
                "VoyageId": "vyg_abc123def456ghi789jk"
            }
        }))

        # Transition mission status
        await ws.send(json.dumps({
            "Route": "command",
            "action": "transition_mission_status",
            "id": "msn_abc123def456ghi789jk",
            "status": "Review"
        }))

        # Update a captain
        await ws.send(json.dumps({
            "Route": "command",
            "action": "update_captain",
            "id": "cpt_abc123def456ghi789jk",
            "data": {"Name": "captain-primary"}
        }))

        # Delete a fleet
        await ws.send(json.dumps({
            "Route": "command",
            "action": "delete_fleet",
            "id": "flt_abc123def456ghi789jk"
        }))

        # Receive events
        async for message in ws:
            event = json.loads(message)
            event_type = event.get("type")

            if event_type == "status.snapshot":
                print(f"Status: {event['data']}")
            elif event_type == "mission.changed":
                print(f"Mission {event['missionId']}: {event['status']}")
            elif event_type == "captain.changed":
                print(f"Captain {event['captainId']}: {event['state']}")
            elif event_type == "command.result":
                print(f"Command '{event['action']}' result: {event['data']}")
            elif event_type == "command.error":
                print(f"Command error: {event.get('error')}")

asyncio.run(main())
```
