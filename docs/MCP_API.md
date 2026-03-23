# Armada MCP API Reference

**Version:** 0.3.0
**Default URL:** `http://localhost:7891`
**Protocol:** [Model Context Protocol](https://modelcontextprotocol.io/) (MCP) over HTTP
**Server Library:** Voltaic (McpHttpServer)
**Server Name:** `Armada`

---

## Table of Contents

- [Overview](#overview)
- [Connection](#connection)
  - [HTTP Transport](#http-transport)
  - [Stdio Transport](#stdio-transport)
  - [Port Configuration](#port-configuration)
- [Authentication](#authentication)
  - [MCP Authentication Scope](#mcp-authentication-scope)
- [Tools](#tools)
  - **Status**
    - [armada_status](#armada_status)
    - [armada_stop_server](#armada_stop_server)
  - **Enumeration**
    - [armada_enumerate](#armada_enumerate)
  - **Fleets**
    - [armada_get_fleet](#armada_get_fleet)
    - [armada_create_fleet](#armada_create_fleet)
    - [armada_update_fleet](#armada_update_fleet)
    - [armada_delete_fleet](#armada_delete_fleet)
    - [armada_delete_fleets](#armada_delete_fleets)
  - **Vessels**
    - [armada_get_vessel](#armada_get_vessel)
    - [armada_add_vessel](#armada_add_vessel)
    - [armada_update_vessel](#armada_update_vessel)
    - [armada_update_vessel_context](#armada_update_vessel_context)
    - [armada_delete_vessel](#armada_delete_vessel)
    - [armada_delete_vessels](#armada_delete_vessels)
  - **Voyages**
    - [armada_dispatch](#armada_dispatch)
    - [armada_voyage_status](#armada_voyage_status)
    - [armada_cancel_voyage](#armada_cancel_voyage)
    - [armada_purge_voyage](#armada_purge_voyage)
    - [armada_delete_voyages](#armada_delete_voyages)
  - **Missions**
    - [armada_mission_status](#armada_mission_status)
    - [armada_create_mission](#armada_create_mission)
    - [armada_update_mission](#armada_update_mission)
    - [armada_cancel_mission](#armada_cancel_mission)
    - [armada_restart_mission](#armada_restart_mission)
    - [armada_purge_mission](#armada_purge_mission)
    - [armada_delete_missions](#armada_delete_missions)
    - [armada_transition_mission_status](#armada_transition_mission_status)
    - [armada_get_mission_diff](#armada_get_mission_diff)
    - [armada_get_mission_log](#armada_get_mission_log)
  - **Captains**
    - [armada_get_captain](#armada_get_captain)
    - [armada_create_captain](#armada_create_captain)
    - [armada_update_captain](#armada_update_captain)
    - [armada_stop_captain](#armada_stop_captain)
    - [armada_stop_all](#armada_stop_all)
    - [armada_delete_captain](#armada_delete_captain)
    - [armada_delete_captains](#armada_delete_captains)
    - [armada_get_captain_log](#armada_get_captain_log)
  - **Signals**
    - [armada_send_signal](#armada_send_signal)
    - [armada_delete_signals](#armada_delete_signals)
  - **Events**
    - [armada_delete_event](#armada_delete_event)
    - [armada_delete_events](#armada_delete_events)
  - **Docks**
    - [armada_get_dock](#armada_get_dock)
    - [armada_delete_dock](#armada_delete_dock)
    - [armada_purge_dock](#armada_purge_dock)
    - [armada_delete_docks](#armada_delete_docks)
  - **Merge Queue**
    - [armada_get_merge_entry](#armada_get_merge_entry)
    - [armada_enqueue_merge](#armada_enqueue_merge)
    - [armada_cancel_merge](#armada_cancel_merge)
    - [armada_process_merge_queue](#armada_process_merge_queue)
    - [armada_delete_merge](#armada_delete_merge)
    - [armada_purge_merge_queue](#armada_purge_merge_queue)
    - [armada_purge_merge_entry](#armada_purge_merge_entry)
    - [armada_purge_merge_entries](#armada_purge_merge_entries)
  - **Backup and Restore**
    - [armada_backup](#armada_backup)
    - [armada_restore](#armada_restore)
- [Data Types](#data-types)
  - [Models](#models)
  - [Enumerations](#enumerations)
- [Client Configuration](#client-configuration)
  - [Claude Desktop](#claude-desktop)
  - [Claude Code](#claude-code)
  - [Generic MCP Client](#generic-mcp-client)

---

## Overview

Armada exposes a full MCP server that allows AI agents and MCP-compatible clients to interact with the Admiral orchestrator. Through MCP tools, agents have **full parity** with the REST API — every capability available via HTTP is also available via MCP:

- Query system status, stop the server
- Full CRUD on fleets, vessels, captains
- Dispatch voyages and standalone missions, cancel, purge
- Transition mission status with validation
- Read mission diffs and session logs (with pagination)
- Read captain session logs (with pagination)
- List and inspect docks (worktrees)
- Send signals to captains
- Stop individual captains or all captains (emergency stop)
- Manage the merge queue (enqueue, cancel, process, inspect)

The MCP server shares the same tool implementations as the stdio transport, registered via `McpToolRegistrar.RegisterAll()`.

---

## Connection

### HTTP Transport

The primary MCP transport is HTTP, served by `McpHttpServer` from the Voltaic library. The server listens on a dedicated port.

```
http://localhost:7891
```

MCP clients communicate using the standard MCP JSON-RPC protocol over HTTP. The server supports the full MCP tool-calling lifecycle:

1. **Initialize** — Client discovers server capabilities and available tools
2. **Call Tool** — Client invokes a tool with arguments
3. **Response** — Server returns the tool result

### Stdio Transport

Armada also supports an MCP stdio transport for direct process-based communication. The same tools registered via `McpToolRegistrar` are available on both transports. Use stdio when running Armada as a child process of an MCP client.

### Port Configuration

| Setting | Default | Description |
|---|---|---|
| `ArmadaSettings.McpPort` | `7891` | MCP HTTP server port |
| `RestSettings.Hostname` | `localhost` | Bind hostname |

The MCP port can be configured in the Armada settings file. The hostname is shared with the REST API configuration.

---

## Authentication

The MCP server does **not** currently enforce authentication. All MCP operations run in the context of the default tenant. Access control should be managed at the network level (firewall, bind address).

> **Note:** Unlike the REST API, which supports bearer tokens, encrypted session tokens, and API keys as of v0.3.0, the MCP server remains unauthenticated. Multi-tenant MCP authentication is planned for a future release. For now, MCP clients have unrestricted access to all operations within the default tenant context.

### MCP Authentication Scope

MCP tools (served via stdio) remain **unauthenticated by design**. The MCP transport assumes the orchestrating agent (e.g., Claude Code) is already trusted and running locally. All MCP tool operations use the default tenant context (`ten_default`). If multi-tenant isolation is required for MCP clients, use the authenticated REST API instead.

This is a deliberate architectural decision, not a missing feature. The stdio transport has no network attack surface -- the only caller is the parent process that spawned Armada. Adding authentication to stdio would add complexity without meaningful security benefit. The HTTP MCP transport inherits the same unauthenticated model for consistency, but should be bound to `localhost` or protected by a firewall in production.

---

## Tools

### armada_status

Get aggregate status of all active work in Armada.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {}
}
```

No parameters required.

**Response:** [ArmadaStatus](#armadastatus) object.

```json
{
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
      "voyage": { "id": "vyg_...", "title": "Feature batch 1", "...": "..." },
      "totalMissions": 5,
      "completedMissions": 3,
      "failedMissions": 0,
      "inProgressMissions": 2
    }
  ],
  "recentSignals": [],
  "timestampUtc": "2026-03-07T12:34:56.789Z"
}
```

---

### armada_stop_server

Initiate a graceful shutdown of the Admiral server.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {}
}
```

No parameters required.

**Response:**

```json
{ "Status": "shutting_down" }
```

> **Note:** Only available when the server provides a stop callback (HTTP transport, not stdio).

---

### armada_enumerate

Paginated enumeration of any entity type with filtering and sorting. This is the MCP equivalent of the `POST /api/v1/{entity}/enumerate` REST endpoints. Returns paginated results with total counts, page metadata, and query timing. Supports: fleets, vessels, captains, missions, voyages, docks, signals, events, merge_queue.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "entityType": { "type": "string", "description": "Entity type to enumerate (fleets, vessels, captains, missions, voyages, docks, signals, events, merge_queue)" },
    "pageNumber": { "type": "integer", "description": "Page number (1-based, default 1)" },
    "pageSize": { "type": "integer", "description": "Results per page (default 10, max 1000)" },
    "order": { "type": "string", "description": "Sort order: CreatedAscending, CreatedDescending" },
    "createdAfter": { "type": "string", "description": "ISO 8601 timestamp filter" },
    "createdBefore": { "type": "string", "description": "ISO 8601 timestamp filter" },
    "status": { "type": "string", "description": "Filter by status (entity-specific)" },
    "fleetId": { "type": "string", "description": "Filter by fleet ID (vessels)" },
    "vesselId": { "type": "string", "description": "Filter by vessel ID (missions, docks)" },
    "captainId": { "type": "string", "description": "Filter by captain ID (missions, events, signals)" },
    "voyageId": { "type": "string", "description": "Filter by voyage ID (missions, events)" },
    "missionId": { "type": "string", "description": "Filter by mission ID (events)" },
    "eventType": { "type": "string", "description": "Filter by event type (events only)" },
    "signalType": { "type": "string", "description": "Filter by signal type (signals only)" },
    "toCaptainId": { "type": "string", "description": "Filter by recipient captain (signals only)" },
    "unreadOnly": { "type": "boolean", "description": "Unread only (signals only)" },
    "includeDescription": { "type": "boolean", "description": "Include full Description on missions/voyages (default false). Returns descriptionLength hint when false." },
    "includeContext": { "type": "boolean", "description": "Include ProjectContext and StyleGuide on vessels (default false). Returns length hints when false." },
    "includeTestOutput": { "type": "boolean", "description": "Include TestOutput on merge queue entries (default false). Returns testOutputLength hint when false." },
    "includePayload": { "type": "boolean", "description": "Include full Payload on events (default false). Returns payloadLength hint when false." },
    "includeMessage": { "type": "boolean", "description": "Include full Message on signals (default false). Returns messageLength hint when false." }
  },
  "required": ["entityType"]
}
```

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

| Include flag | Applies to | Default | Description |
|---|---|---|---|
| `includeDescription` | missions, voyages | `false` | Include full Description. Returns `descriptionLength` hint when false. |
| `includeContext` | vessels | `false` | Include ProjectContext and StyleGuide. Returns length hints when false. |
| `includeTestOutput` | merge_queue | `false` | Include TestOutput. Returns `testOutputLength` hint when false. |
| `includePayload` | events | `false` | Include full Payload. Returns `payloadLength` hint when false. |
| `includeMessage` | signals | `false` | Include full Message. Returns `messageLength` hint when false. |

**Example -- page 2 of in-progress missions, 25 per page:**

```json
{
  "entityType": "missions",
  "status": "InProgress",
  "pageNumber": 2,
  "pageSize": 25
}
```

**Response:** [EnumerationResult](#enumerationresult) object.

```json
{
  "Success": true,
  "PageNumber": 2,
  "PageSize": 25,
  "TotalPages": 4,
  "TotalRecords": 87,
  "Objects": [ { "Id": "msn_...", "..." : "..." }, "..." ],
  "TotalMs": 1.23
}
```

> **Note:** When enumerating `missions`, the `DiffSnapshot` field is excluded from results to keep payloads compact. Use `armada_get_mission_diff` to retrieve the full diff for a specific mission.

---

### armada_dispatch

Dispatch a new voyage with missions to a vessel. This is the primary way to assign work to the Armada system.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "title": {
      "type": "string",
      "description": "Voyage title"
    },
    "description": {
      "type": "string",
      "description": "Voyage description"
    },
    "vesselId": {
      "type": "string",
      "description": "Target vessel ID"
    },
    "missions": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "title": { "type": "string" },
          "description": { "type": "string" }
        }
      }
    }
  },
  "required": ["title", "vesselId", "missions"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `title` | string | Yes | Voyage title |
| `description` | string | No | Voyage description |
| `vesselId` | string | Yes | Target vessel ID (prefix `vsl_`) |
| `missions` | array | Yes | Array of mission objects with `title` and optional `description` |

**Example Input:**

```json
{
  "title": "Implement authentication",
  "description": "Add JWT auth to the API",
  "vesselId": "vsl_abc123def456ghi789jk",
  "missions": [
    {
      "title": "Add JWT middleware",
      "description": "Create middleware that validates JWT tokens on protected routes"
    },
    {
      "title": "Add login endpoint",
      "description": "Create POST /auth/login that returns a JWT"
    },
    {
      "title": "Add user registration",
      "description": "Create POST /auth/register with email/password"
    }
  ]
}
```

**Response:** [Voyage](#voyage) object (the newly created voyage with all missions).

---

### armada_voyage_status

Get status of a specific voyage. Returns summary with mission counts by default; opt-in to full mission details.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "voyageId": {
      "type": "string",
      "description": "Voyage ID"
    },
    "summary": {
      "type": "boolean",
      "description": "Return summary with mission counts only (default true)"
    },
    "includeMissions": {
      "type": "boolean",
      "description": "Include full mission objects in response (default false)"
    },
    "includeDescription": {
      "type": "boolean",
      "description": "Include full Description on voyage and missions (default false)"
    },
    "includeDiffs": {
      "type": "boolean",
      "description": "Include DiffSnapshot on missions (default false)"
    },
    "includeLogs": {
      "type": "boolean",
      "description": "Include session logs on missions (default false)"
    }
  },
  "required": ["voyageId"]
}
```

| Parameter | Type | Required | Default | Description |
|---|---|---|---|---|
| `voyageId` | string | Yes | -- | Voyage ID (prefix `vyg_`) |
| `summary` | boolean | No | `true` | Return summary with mission counts only |
| `includeMissions` | boolean | No | `false` | Include full mission objects in response |
| `includeDescription` | boolean | No | `false` | Include full Description on voyage and missions |
| `includeDiffs` | boolean | No | `false` | Include DiffSnapshot on missions |
| `includeLogs` | boolean | No | `false` | Include session logs on missions |

**Response (default summary mode):**

```json
{
  "Voyage": {
    "id": "vyg_abc123def456ghi789jk",
    "title": "Implement authentication",
    "status": "InProgress"
  },
  "TotalMissions": 5,
  "MissionCountsByStatus": {
    "Pending": 1,
    "InProgress": 2,
    "Complete": 2
  }
}
```

**Response (with `includeMissions: true`):**

```json
{
  "Voyage": {
    "id": "vyg_abc123def456ghi789jk",
    "title": "Implement authentication",
    "status": "InProgress"
  },
  "TotalMissions": 5,
  "MissionCountsByStatus": {
    "Pending": 1,
    "InProgress": 2,
    "Complete": 2
  },
  "Missions": [
    {
      "id": "msn_abc123def456ghi789jk",
      "title": "Add JWT middleware",
      "status": "Complete",
      "...": "..."
    },
    {
      "id": "msn_def456ghi789jkl012mn",
      "title": "Add login endpoint",
      "status": "InProgress",
      "...": "..."
    }
  ]
}
```

| Field | Type | Description |
|---|---|---|
| `Voyage` | object \| null | [Voyage](#voyage) object, or null if not found |
| `TotalMissions` | int | Total number of missions in this voyage |
| `MissionCountsByStatus` | object | Map of status string to count |
| `Missions` | array \| null | List of [Mission](#mission) objects (only present when `includeMissions` is true) |

---

### armada_mission_status

Get status of a specific mission.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "missionId": {
      "type": "string",
      "description": "Mission ID"
    }
  },
  "required": ["missionId"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `missionId` | string | Yes | Mission ID (prefix `msn_`) |

**Response:** [Mission](#mission) object, or `{"error": "Mission not found"}` if the ID does not exist.

> **Note:** The `DiffSnapshot` field is excluded from status responses to keep payloads compact. Use `armada_get_mission_diff` to retrieve the full diff.

---

### armada_get_fleet

Get details of a specific fleet including all its vessels.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "fleetId": {
      "type": "string",
      "description": "Fleet ID"
    }
  },
  "required": ["fleetId"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `fleetId` | string | Yes | Fleet ID (prefix `flt_`) |

**Response:**

```json
{
  "fleet": {
    "id": "flt_abc123def456ghi789jk",
    "name": "Backend Services",
    "...": "..."
  },
  "vessels": [
    {
      "id": "vsl_abc123def456ghi789jk",
      "name": "auth-service",
      "repoUrl": "git@github.com:org/auth-service.git",
      "...": "..."
    }
  ]
}
```

Returns `{"error": "Fleet not found"}` if the ID does not exist.

| Field | Type | Description |
|---|---|---|
| `fleet` | object | [Fleet](#fleet) object |
| `vessels` | array | List of [Vessel](#vessel) objects in this fleet |

---

### armada_add_vessel

Register a new vessel (git repository) in a fleet.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "name": {
      "type": "string",
      "description": "Display name for the vessel"
    },
    "repoUrl": {
      "type": "string",
      "description": "Git repository URL (HTTPS or SSH)"
    },
    "fleetId": {
      "type": "string",
      "description": "Fleet ID to add the vessel to"
    },
    "defaultBranch": {
      "type": "string",
      "description": "Default branch name (defaults to main)"
    },
    "projectContext": {
      "type": "string",
      "description": "Project context describing architecture, key files, and dependencies"
    },
    "styleGuide": {
      "type": "string",
      "description": "Style guide describing naming conventions, patterns, and library preferences"
    },
    "workingDirectory": {
      "type": "string",
      "description": "Optional local directory where completed mission changes will be pulled after merge"
    },
    "enableModelContext": {
      "type": "boolean",
      "description": "Enable model context accumulation -- agents will update context with key information discovered during missions (default false)"
    }
  },
  "required": ["name", "repoUrl", "fleetId"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `name` | string | Yes | Display name for the vessel |
| `repoUrl` | string | Yes | Git repository URL (HTTPS or SSH) |
| `fleetId` | string | Yes | Fleet ID to add the vessel to (prefix `flt_`) |
| `defaultBranch` | string | No | Default branch name (defaults to `"main"`) |
| `projectContext` | string | No | Project context describing architecture, key files, and dependencies |
| `styleGuide` | string | No | Style guide describing naming conventions, patterns, and library preferences |
| `workingDirectory` | string | No | Optional local directory where completed mission changes will be pulled after merge |
| `enableModelContext` | boolean | No | Enable model context accumulation (default false) |

**Example Input:**

```json
{
  "name": "payment-service",
  "repoUrl": "git@github.com:org/payment-service.git",
  "fleetId": "flt_abc123def456ghi789jk",
  "defaultBranch": "main"
}
```

**Response:** The newly created [Vessel](#vessel) object.

---

### armada_delete_event

Delete a single event by ID.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "eventId": {
      "type": "string",
      "description": "Event ID to delete (evt_ prefix)"
    }
  },
  "required": ["eventId"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `eventId` | string | Yes | Event ID to delete (prefix `evt_`) |

**Response:**

```json
{
  "Status": "deleted",
  "EventId": "evt_abc123def456ghi789jk"
}
```

Returns `{ "Error": "Event not found: evt_..." }` if the event does not exist.

---

### armada_delete_events

Delete multiple events by ID. Returns a summary of deleted and skipped entries.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "ids": { "type": "array", "items": { "type": "string" }, "description": "List of event IDs to delete (evt_ prefix)" }
  },
  "required": ["ids"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `ids` | string[] | Yes | List of event IDs to delete (prefix `evt_`) |

**Response:**

```json
{
  "Status": "deleted",
  "Deleted": 2,
  "Skipped": [
    { "Id": "evt_xyz789", "Reason": "Not found" }
  ]
}
```

Returns `{ "Error": "ids is required and must not be empty" }` if no IDs are provided.

---

### armada_send_signal

Send a signal/message to a captain.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "captainId": {
      "type": "string",
      "description": "Target captain ID"
    },
    "message": {
      "type": "string",
      "description": "Signal message"
    }
  },
  "required": ["captainId", "message"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `captainId` | string | Yes | Target captain ID (prefix `cpt_`) |
| `message` | string | Yes | Signal message content |

The signal is created with type `Mail` (persistent message) from the Admiral (no `fromCaptainId`).

**Response:** The newly created [Signal](#signal) object.

---

### armada_delete_signals

Soft-delete multiple signals by marking them as read. Returns a summary of deleted and skipped entries.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "ids": { "type": "array", "items": { "type": "string" }, "description": "List of signal IDs to delete (sig_ prefix)" }
  },
  "required": ["ids"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `ids` | string[] | Yes | List of signal IDs to soft-delete (prefix `sig_`) |

**Response:**

```json
{
  "Status": "deleted",
  "Deleted": 2,
  "Skipped": [
    { "Id": "sig_xyz789", "Reason": "Not found" }
  ]
}
```

Returns `{ "Error": "ids is required and must not be empty" }` if no IDs are provided.

---

### armada_stop_captain

Stop a specific captain agent.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "captainId": {
      "type": "string",
      "description": "Captain ID to stop"
    }
  },
  "required": ["captainId"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `captainId` | string | Yes | Captain ID to stop (prefix `cpt_`) |

**Response:**

```json
{
  "status": "stopped",
  "captainId": "cpt_abc123def456ghi789jk"
}
```

---

### armada_stop_all

Emergency stop all running captains.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {}
}
```

No parameters required.

**Response:**

```json
{
  "status": "all_stopped"
}
```

---

### armada_cancel_mission

Cancel a specific mission.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "missionId": {
      "type": "string",
      "description": "Mission ID to cancel"
    }
  },
  "required": ["missionId"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `missionId` | string | Yes | Mission ID to cancel (prefix `msn_`) |

Sets the mission status to `Cancelled`. Returns `{"error": "Mission not found"}` if the ID does not exist.

**Response:** The updated [Mission](#mission) object with status `Cancelled`.

---

### armada_restart_mission

Restart a failed or cancelled mission, resetting it to `Pending` for re-dispatch. Optionally update the title and description (instructions) before restarting. Clears captain assignment, branch, PR URL, and timing fields.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "missionId": {
      "type": "string",
      "description": "Mission ID to restart"
    },
    "title": {
      "type": "string",
      "description": "Optional new title. Omit to keep original."
    },
    "description": {
      "type": "string",
      "description": "Optional new description/instructions. Omit to keep original."
    }
  },
  "required": ["missionId"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `missionId` | string | Yes | Mission ID to restart (prefix `msn_`) |
| `title` | string | No | New mission title. Omit to keep the original. |
| `description` | string | No | New description/instructions. Omit to keep the original. |

Only `Failed` or `Cancelled` missions can be restarted. Returns `{"error": "..."}` if the mission is not found or is in an invalid status.

**Response:** The updated [Mission](#mission) object with status `Pending`.

---

### armada_cancel_voyage

Cancel an entire voyage and all its pending missions. Missions that are already `Complete` or `Cancelled` are not affected.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "voyageId": {
      "type": "string",
      "description": "Voyage ID to cancel"
    }
  },
  "required": ["voyageId"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `voyageId` | string | Yes | Voyage ID to cancel (prefix `vyg_`) |

Returns `{"error": "Voyage not found"}` if the ID does not exist.

**Response:**

```json
{
  "voyage": {
    "id": "vyg_abc123def456ghi789jk",
    "title": "Implement authentication",
    "status": "Cancelled",
    "...": "..."
  },
  "cancelledMissions": 3
}
```

| Field | Type | Description |
|---|---|---|
| `voyage` | object | The updated [Voyage](#voyage) object with status `Cancelled` |
| `cancelledMissions` | int | Number of missions that were cancelled |

---

### armada_purge_voyage

Permanently delete a voyage and all its missions from the database. **This cannot be undone.**

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "voyageId": { "type": "string", "description": "Voyage ID (vyg_ prefix)" }
  },
  "required": ["voyageId"]
}
```

**Response:**

```json
{ "Status": "deleted", "VoyageId": "vyg_...", "MissionsDeleted": 3 }
```

---

### armada_delete_voyages

Permanently delete multiple voyages and their associated missions from the database by ID. Voyages that are Open/InProgress or have active missions are skipped. Returns a summary of deleted and skipped entries. **This cannot be undone.**

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "ids": { "type": "array", "items": { "type": "string" }, "description": "List of voyage IDs to delete (vyg_ prefix)" }
  },
  "required": ["ids"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `ids` | string[] | Yes | List of voyage IDs to delete (prefix `vyg_`) |

**Response:**

```json
{
  "Status": "deleted",
  "Deleted": 2,
  "Skipped": [
    { "Id": "vyg_xyz789", "Reason": "Cannot delete voyage while status is Open. Cancel the voyage first." }
  ]
}
```

Returns `{ "Error": "ids is required and must not be empty" }` if no IDs are provided.

---

### armada_create_fleet

Create a new fleet (collection of repositories).

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "name": { "type": "string", "description": "Fleet name" },
    "description": { "type": "string", "description": "Fleet description" }
  },
  "required": ["name"]
}
```

**Response:** [Fleet](#fleet) object.

---

### armada_update_fleet

Update an existing fleet's name or description.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "fleetId": { "type": "string", "description": "Fleet ID (flt_ prefix)" },
    "name": { "type": "string", "description": "New fleet name" },
    "description": { "type": "string", "description": "New fleet description" }
  },
  "required": ["fleetId"]
}
```

**Response:** Updated [Fleet](#fleet) object, or `{ "Error": "Fleet not found" }`.

---

### armada_delete_fleet

Delete a fleet by ID.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "fleetId": { "type": "string", "description": "Fleet ID (flt_ prefix)" }
  },
  "required": ["fleetId"]
}
```

**Response:**

```json
{ "Status": "deleted", "FleetId": "flt_..." }
```

---

### armada_delete_fleets

Permanently delete multiple fleets from the database by ID. Returns a summary of deleted and skipped entries. **This cannot be undone.**

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "ids": { "type": "array", "items": { "type": "string" }, "description": "List of fleet IDs to delete (flt_ prefix)" }
  },
  "required": ["ids"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `ids` | string[] | Yes | List of fleet IDs to delete (prefix `flt_`) |

**Response:**

```json
{
  "Status": "deleted",
  "Deleted": 2,
  "Skipped": [
    { "Id": "flt_xyz789", "Reason": "Not found" }
  ]
}
```

Returns `{ "Error": "ids is required and must not be empty" }` if no IDs are provided.

---

### armada_get_vessel

Get details of a specific vessel (repository).

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "vesselId": { "type": "string", "description": "Vessel ID (vsl_ prefix)" }
  },
  "required": ["vesselId"]
}
```

**Response:** [Vessel](#vessel) object, or `{ "Error": "Vessel not found" }`.

---

### armada_update_vessel

Update an existing vessel's properties.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "vesselId": { "type": "string", "description": "Vessel ID (vsl_ prefix)" },
    "name": { "type": "string", "description": "New display name" },
    "repoUrl": { "type": "string", "description": "New repository URL" },
    "defaultBranch": { "type": "string", "description": "New default branch" },
    "projectContext": { "type": "string", "description": "New project context" },
    "styleGuide": { "type": "string", "description": "New style guide" },
    "workingDirectory": { "type": "string", "description": "New local directory where completed mission changes will be pulled after merge" },
    "enableModelContext": { "type": "boolean", "description": "Enable or disable model context accumulation" },
    "modelContext": { "type": "string", "description": "Agent-accumulated context about this repository" }
  },
  "required": ["vesselId"]
}
```

**Response:** Updated [Vessel](#vessel) object, or `{ "Error": "Vessel not found" }`.

---

### armada_update_vessel_context

Update a vessel's project context and style guide without modifying other properties.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "vesselId": { "type": "string", "description": "Vessel ID (vsl_ prefix)" },
    "projectContext": { "type": "string", "description": "Project context describing architecture, key files, and dependencies" },
    "styleGuide": { "type": "string", "description": "Style guide describing naming conventions, patterns, and library preferences" },
    "modelContext": { "type": "string", "description": "Agent-accumulated context about this repository -- key information discovered during missions" }
  },
  "required": ["vesselId"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `vesselId` | string | Yes | Vessel ID (prefix `vsl_`) |
| `projectContext` | string | No | Project context describing architecture, key files, and dependencies |
| `styleGuide` | string | No | Style guide describing naming conventions, patterns, and library preferences |
| `modelContext` | string | No | Agent-accumulated context about this repository |

**Response:** Updated [Vessel](#vessel) object, or `{ "Error": "Vessel not found" }`.

---

### armada_delete_vessel

Delete a vessel by ID.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "vesselId": { "type": "string", "description": "Vessel ID (vsl_ prefix)" }
  },
  "required": ["vesselId"]
}
```

**Response:**

```json
{ "Status": "deleted", "VesselId": "vsl_..." }
```

---

### armada_delete_vessels

Permanently delete multiple vessels from the database by ID. Returns a summary of deleted and skipped entries. **This cannot be undone.**

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "ids": { "type": "array", "items": { "type": "string" }, "description": "List of vessel IDs to delete (vsl_ prefix)" }
  },
  "required": ["ids"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `ids` | string[] | Yes | List of vessel IDs to delete (prefix `vsl_`) |

**Response:**

```json
{
  "Status": "deleted",
  "Deleted": 2,
  "Skipped": [
    { "Id": "vsl_xyz789", "Reason": "Not found" }
  ]
}
```

Returns `{ "Error": "ids is required and must not be empty" }` if no IDs are provided.

---

### armada_create_mission

Create and dispatch a standalone mission to a vessel. The Admiral assigns a captain and sets up a worktree.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "title": { "type": "string", "description": "Mission title" },
    "description": { "type": "string", "description": "Mission description/instructions" },
    "vesselId": { "type": "string", "description": "Target vessel ID (vsl_ prefix)" },
    "voyageId": { "type": "string", "description": "Optional voyage ID to associate with (vyg_ prefix)" }
  },
  "required": ["title", "description", "vesselId"]
}
```

**Response:** [Mission](#mission) object.

---

### armada_update_mission

Update an existing mission's metadata fields. Operational fields (status, timestamps, captain assignment) are managed by the system and cannot be overwritten.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "missionId": { "type": "string", "description": "Mission ID (msn_ prefix)" },
    "title": { "type": "string", "description": "New mission title" },
    "description": { "type": "string", "description": "New mission description/instructions" },
    "vesselId": { "type": "string", "description": "New target vessel ID (vsl_ prefix)" },
    "voyageId": { "type": "string", "description": "New voyage association (vyg_ prefix)" },
    "priority": { "type": "integer", "description": "New priority (lower is higher priority)" },
    "branchName": { "type": "string", "description": "Git branch name for this mission" },
    "prUrl": { "type": "string", "description": "Pull request URL" },
    "parentMissionId": { "type": "string", "description": "Parent mission ID for sub-tasks (msn_ prefix)" }
  },
  "required": ["missionId"]
}
```

| Parameter | Required | Description |
|---|---|---|
| `missionId` | Yes | Mission ID (msn_ prefix) |
| `title` | No | New mission title |
| `description` | No | New mission description/instructions |
| `vesselId` | No | New target vessel ID |
| `voyageId` | No | New voyage association |
| `priority` | No | New priority (lower = higher priority) |
| `branchName` | No | Git branch name |
| `prUrl` | No | Pull request URL |
| `parentMissionId` | No | Parent mission ID for sub-tasks |

**Response:** Updated [Mission](#mission) object, or `{ "Error": "Mission not found" }`.

---

### armada_purge_mission

Permanently delete a mission from the database. This cannot be undone.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "missionId": { "type": "string", "description": "Mission ID (msn_ prefix)" }
  },
  "required": ["missionId"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `missionId` | string | Yes | Mission ID to delete (prefix `msn_`) |

**Response:**

```json
{ "Status": "deleted", "MissionId": "msn_..." }
```

Returns `{ "Error": "Mission not found" }` if the ID does not exist.

---

### armada_delete_missions

Permanently delete multiple missions from the database by ID. Returns a summary of deleted and skipped entries. **This cannot be undone.**

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "ids": { "type": "array", "items": { "type": "string" }, "description": "List of mission IDs to delete (msn_ prefix)" }
  },
  "required": ["ids"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `ids` | string[] | Yes | List of mission IDs to delete (prefix `msn_`) |

**Response:**

```json
{
  "Status": "deleted",
  "Deleted": 2,
  "Skipped": [
    { "Id": "msn_xyz789", "Reason": "Not found" }
  ]
}
```

Returns `{ "Error": "ids is required and must not be empty" }` if no IDs are provided.

---

### armada_transition_mission_status

Transition a mission to a new status with validation.

**Valid transitions:**

| From | To |
|---|---|
| Pending | Assigned, Cancelled |
| Assigned | InProgress, Cancelled |
| InProgress | WorkProduced, Testing, Review, Complete, Failed, Cancelled |
| WorkProduced | PullRequestOpen, Complete, LandingFailed, Cancelled |
| PullRequestOpen | Complete, LandingFailed, Cancelled |
| Testing | Review, InProgress, Complete, Failed |
| Review | Complete, InProgress, Failed |
| LandingFailed | WorkProduced, Failed, Cancelled |

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "missionId": { "type": "string", "description": "Mission ID (msn_ prefix)" },
    "status": { "type": "string", "description": "Target status" }
  },
  "required": ["missionId", "status"]
}
```

**Response:** Updated [Mission](#mission) object, or an error object if the transition is invalid.

---

### armada_get_mission_diff

Get the git diff of changes made by a captain for a mission. Returns saved diff if available, otherwise live worktree diff.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "missionId": { "type": "string", "description": "Mission ID (msn_ prefix)" }
  },
  "required": ["missionId"]
}
```

**Response:**

```json
{
  "MissionId": "msn_...",
  "Branch": "armada/msn_...",
  "Diff": "diff --git a/file.cs b/file.cs\n..."
}
```

---

### armada_get_mission_log

Get the session log for a mission. Supports pagination.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "missionId": { "type": "string", "description": "Mission ID (msn_ prefix)" },
    "lines": { "type": "integer", "description": "Number of lines to return (default 100)" },
    "offset": { "type": "integer", "description": "Line offset to start from (default 0)" }
  },
  "required": ["missionId"]
}
```

**Response:**

```json
{
  "MissionId": "msn_...",
  "Log": "line1\nline2\n...",
  "Lines": 100,
  "TotalLines": 2345
}
```

---

### armada_create_captain

Register a new captain (AI agent).

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "name": { "type": "string", "description": "Captain display name" },
    "runtime": { "type": "string", "description": "Agent runtime: ClaudeCode, Codex, Gemini, Cursor" },
    "systemInstructions": { "type": "string", "description": "System instructions for this captain -- injected into every mission prompt to specialize behavior" }
  },
  "required": ["name"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `name` | string | Yes | Captain display name |
| `runtime` | string | No | Agent runtime: `ClaudeCode`, `Codex`, `Gemini`, `Cursor` |
| `systemInstructions` | string | No | System instructions injected into every mission prompt for this captain |

**Response:** [Captain](#captain) object.

---

### armada_get_captain

Get details of a specific captain (AI agent).

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "captainId": { "type": "string", "description": "Captain ID (cpt_ prefix)" }
  },
  "required": ["captainId"]
}
```

**Response:** [Captain](#captain) object, or `{ "Error": "Captain not found" }`.

---

### armada_update_captain

Update a captain's name or runtime. Operational fields (state, process, mission) are preserved.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "captainId": { "type": "string", "description": "Captain ID (cpt_ prefix)" },
    "name": { "type": "string", "description": "New display name" },
    "runtime": { "type": "string", "description": "New agent runtime: ClaudeCode, Codex, Gemini, Cursor" },
    "systemInstructions": { "type": "string", "description": "New system instructions for this captain" }
  },
  "required": ["captainId"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `captainId` | string | Yes | Captain ID (prefix `cpt_`) |
| `name` | string | No | New display name |
| `runtime` | string | No | New agent runtime: `ClaudeCode`, `Codex`, `Gemini`, `Cursor` |
| `systemInstructions` | string | No | New system instructions for this captain |

**Response:** Updated [Captain](#captain) object, or `{ "Error": "Captain not found" }`.

---

### armada_delete_captain

Delete a captain. If working, the captain is recalled first.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "captainId": { "type": "string", "description": "Captain ID (cpt_ prefix)" }
  },
  "required": ["captainId"]
}
```

**Response:**

```json
{ "Status": "deleted", "CaptainId": "cpt_..." }
```

---

### armada_delete_captains

Permanently delete multiple captains from the database by ID. Captains that are Working or have active missions are skipped. Returns a summary of deleted and skipped entries. **This cannot be undone.**

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "ids": { "type": "array", "items": { "type": "string" }, "description": "List of captain IDs to delete (cpt_ prefix)" }
  },
  "required": ["ids"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `ids` | string[] | Yes | List of captain IDs to delete (prefix `cpt_`) |

**Response:**

```json
{
  "Status": "deleted",
  "Deleted": 2,
  "Skipped": [
    { "Id": "cpt_xyz789", "Reason": "Cannot delete captain while state is Working. Stop the captain first." }
  ]
}
```

Returns `{ "Error": "ids is required and must not be empty" }` if no IDs are provided.

---

### armada_get_captain_log

Get the current session log for a captain. Supports pagination.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "captainId": { "type": "string", "description": "Captain ID (cpt_ prefix)" },
    "lines": { "type": "integer", "description": "Number of lines to return (default 100)" },
    "offset": { "type": "integer", "description": "Line offset to start from (default 0)" }
  },
  "required": ["captainId"]
}
```

**Response:**

```json
{
  "CaptainId": "cpt_...",
  "Log": "line1\nline2\n...",
  "Lines": 100,
  "TotalLines": 567
}
```

---

### armada_get_dock

Get a dock (git worktree) by ID.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "dockId": { "type": "string", "description": "Dock ID (dck_ prefix)" }
  },
  "required": ["dockId"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `dockId` | string | Yes | Dock ID (prefix `dck_`) |

**Response:** Dock object, or `{ "Error": "Dock not found" }`.

---

### armada_delete_dock

Delete a dock and clean up its git worktree. Blocked if the dock is actively in use by a captain.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "dockId": { "type": "string", "description": "Dock ID (dck_ prefix)" }
  },
  "required": ["dockId"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `dockId` | string | Yes | Dock ID to delete (prefix `dck_`) |

**Response:**

```json
{ "Status": "deleted", "DockId": "dck_..." }
```

Returns `{ "Error": "Dock not found" }` if the ID does not exist.
Returns `{ "Error": "Cannot delete dock while it is actively in use by a captain" }` if the dock is active.

---

### armada_purge_dock

Force purge a dock and its git worktree, even if a mission references it. **This cannot be undone.**

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "dockId": { "type": "string", "description": "Dock ID (dck_ prefix)" }
  },
  "required": ["dockId"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `dockId` | string | Yes | Dock ID to purge (prefix `dck_`) |

**Response:**

```json
{ "Status": "purged", "DockId": "dck_..." }
```

Returns `{ "Error": "Dock not found" }` if the ID does not exist.

---

### armada_delete_docks

Permanently delete multiple docks and their git worktrees from the database by ID. Returns a summary of deleted and skipped entries. **This cannot be undone.**

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "ids": { "type": "array", "items": { "type": "string" }, "description": "List of dock IDs to delete (dck_ prefix)" }
  },
  "required": ["ids"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `ids` | string[] | Yes | List of dock IDs to delete (prefix `dck_`) |

**Response:**

```json
{
  "Status": "deleted",
  "Deleted": 2,
  "Skipped": [
    { "Id": "dck_xyz789", "Reason": "Not found" }
  ]
}
```

Returns `{ "Error": "ids is required and must not be empty" }` if no IDs are provided.

---

### armada_get_merge_entry

Get details of a specific merge queue entry.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "entryId": { "type": "string", "description": "Merge entry ID (mrg_ prefix)" }
  },
  "required": ["entryId"]
}
```

**Response:** [MergeEntry](#mergeentry) object, or `{ "Error": "Merge entry not found" }`.

---

### armada_enqueue_merge

Add a branch to the merge queue for testing and merging.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "missionId": { "type": "string", "description": "Associated mission ID (msn_ prefix)" },
    "vesselId": { "type": "string", "description": "Target vessel ID (vsl_ prefix)" },
    "branchName": { "type": "string", "description": "Branch name to merge" },
    "targetBranch": { "type": "string", "description": "Target branch (defaults to main)" },
    "priority": { "type": "integer", "description": "Queue priority (lower = higher, default 0)" },
    "testCommand": { "type": "string", "description": "Custom test command to run" }
  },
  "required": ["vesselId", "branchName"]
}
```

**Response:** [MergeEntry](#mergeentry) object.

---

### armada_cancel_merge

Cancel a queued merge entry.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "entryId": { "type": "string", "description": "Merge entry ID (mrg_ prefix)" }
  },
  "required": ["entryId"]
}
```

**Response:**

```json
{ "Status": "cancelled", "EntryId": "mrg_..." }
```

---

### armada_process_merge_queue

Process the merge queue: creates integration branches, runs tests, and lands passing batches.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {}
}
```

**Response:**

```json
{ "Status": "processed" }
```

---

### armada_delete_merge

Permanently delete a terminal merge queue entry from the database. Only entries in Landed, Failed, or Cancelled status can be deleted. **This cannot be undone.**

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "entryId": { "type": "string", "description": "Merge entry ID (mrg_ prefix)" }
  },
  "required": ["entryId"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `entryId` | string | Yes | Merge entry ID to delete (prefix `mrg_`) |

**Response:**

```json
{ "Status": "deleted", "EntryId": "mrg_..." }
```

Returns `{ "Error": "Merge entry not found" }` if the ID does not exist.
Returns `{ "Error": "Cannot delete merge entry in non-terminal status ..." }` if the entry is not in a terminal state.

---

### armada_purge_merge_queue

Permanently delete all terminal merge queue entries (Landed, Failed, Cancelled) from the database. Optionally filter by vessel ID and/or status. **This cannot be undone.**

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "vesselId": { "type": "string", "description": "Optional vessel ID filter (vsl_ prefix)" },
    "status": { "type": "string", "description": "Optional status filter: Landed, Failed, or Cancelled" }
  }
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `vesselId` | string | No | Filter to only purge entries for a specific vessel (prefix `vsl_`) |
| `status` | string | No | Filter to only purge entries with a specific terminal status: `Landed`, `Failed`, or `Cancelled` |

**Response:**

```json
{ "Status": "purged", "EntriesDeleted": 5 }
```

Returns `{ "Error": "Invalid status. Must be one of: Landed, Failed, Cancelled" }` if an invalid status is provided.

---

### armada_purge_merge_entry

Permanently delete a single terminal merge queue entry from the database by ID. Only entries in Landed, Failed, or Cancelled status can be purged. **This cannot be undone.**

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "entryId": { "type": "string", "description": "Merge entry ID (mrg_ prefix)" }
  },
  "required": ["entryId"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `entryId` | string | Yes | Merge entry ID to purge (prefix `mrg_`) |

**Response:**

```json
{ "Status": "purged", "EntryId": "mrg_..." }
```

Returns `{ "Error": "Merge entry not found" }` if the ID does not exist.
Returns `{ "Error": "Cannot purge merge entry in non-terminal status ..." }` if the entry is not in a terminal state.

---

### armada_purge_merge_entries

Permanently delete multiple terminal merge queue entries from the database by ID. Returns a summary of purged and skipped entries. **This cannot be undone.**

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "entryIds": { "type": "array", "items": { "type": "string" }, "description": "List of merge entry IDs to purge (mrg_ prefix)" }
  },
  "required": ["entryIds"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `entryIds` | string[] | Yes | List of merge entry IDs to purge (prefix `mrg_`) |

**Response:**

```json
{
  "Status": "purged",
  "EntriesPurged": 2,
  "Skipped": [
    { "EntryId": "mrg_xyz789", "Reason": "Not in terminal state (status: Testing)" }
  ]
}
```

Returns `{ "Error": "entryIds is required and must not be empty" }` if no IDs are provided.

---

### armada_backup

Create a backup of the Armada database and settings as a ZIP archive.

**Input Schema:**

| Parameter | Type | Required | Description |
|---|---|---|---|
| `outputPath` | string | No | File path for the backup ZIP. Defaults to `~/.armada/backups/armada-backup-{timestamp}.zip` |

**Response:**

```json
{
  "Path": "~/.armada/backups/armada-backup-20260311T120000Z.zip",
  "Timestamp": "2026-03-11T12:00:00Z",
  "SchemaVersion": 9,
  "SizeBytes": 245760,
  "RecordCounts": {
    "Fleets": 2,
    "Vessels": 5,
    "Captains": 3,
    "Missions": 42,
    "Voyages": 8,
    "Signals": 15,
    "Events": 120,
    "Docks": 6,
    "MergeEntries": 3
  }
}
```

**ZIP Contents:**

| File | Description |
|---|---|
| `armada.db` | SQLite database snapshot created via the SQLite online backup API |
| `settings.json` | Current Armada server configuration |
| `manifest.json` | Backup metadata: timestamp, schema version, Armada version, record counts per table |

---

### armada_restore

Restore Armada from a previously created backup ZIP file.

**Input Schema:**

| Parameter | Type | Required | Description |
|---|---|---|---|
| `filePath` | string | Yes | Path to the backup ZIP file to restore from |

**Validation:**
- ZIP must contain `armada.db` with a valid `schema_migrations` table
- A safety backup is automatically created before overwriting the current database

**Response:**

```json
{
  "Status": "restored",
  "SafetyBackupPath": "~/.armada/backups/armada-safety-backup-20260311T120000Z.zip",
  "SchemaVersion": 9,
  "Message": "Database restored from armada-backup-20260311T120000Z.zip. Restart the server to reload the restored data."
}
```

> **Note:** Restart the server after restoring to ensure all in-memory state is refreshed.

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
| `missionsByStatus` | object | Map of status string to count (e.g., `{"Pending": 3}`) |
| `voyages` | array | List of [VoyageProgress](#voyageprogress) objects |
| `recentSignals` | array | List of recent [Signal](#signal) objects |
| `timestampUtc` | string | ISO 8601 UTC timestamp |

#### VoyageProgress

| Field | Type | Description |
|---|---|---|
| `voyage` | object | [Voyage](#voyage) object |
| `totalMissions` | int | Total missions in this voyage |
| `completedMissions` | int | Missions with status Complete |
| `failedMissions` | int | Missions with status Failed |
| `inProgressMissions` | int | Missions currently in progress |

#### EnumerationResult

Paginated result wrapper returned by `armada_enumerate`.

| Field | Type | Description |
|---|---|---|
| `success` | bool | Whether the query succeeded |
| `pageNumber` | int | Current page number (1-based) |
| `pageSize` | int | Items per page |
| `totalPages` | int | Total number of pages |
| `totalRecords` | long | Total matching records |
| `objects` | array | Array of entity objects for this page |
| `totalMs` | double | Query execution time in milliseconds |

#### Fleet

| Field | Type | Description |
|---|---|---|
| `id` | string | Fleet ID (prefix `flt_`) |
| `name` | string | Fleet display name |
| `description` | string \| null | Fleet description |
| `active` | bool | Whether the fleet is active |
| `createdUtc` | string | ISO 8601 creation timestamp |
| `lastUpdateUtc` | string | ISO 8601 last update timestamp |

#### Vessel

| Field | Type | Description |
|---|---|---|
| `id` | string | Vessel ID (prefix `vsl_`) |
| `fleetId` | string \| null | Parent fleet ID |
| `name` | string | Display name |
| `repoUrl` | string \| null | Git repository URL |
| `localPath` | string \| null | Bare repository clone path |
| `workingDirectory` | string \| null | User checkout path for merge operations |
| `defaultBranch` | string | Default branch name (default: `"main"`) |
| `projectContext` | string \| null | Project context describing architecture, key files, and dependencies |
| `styleGuide` | string \| null | Style guide describing naming conventions, patterns, and library preferences |
| `landingMode` | string \| null | [LandingModeEnum](#landingmodeenum) — per-vessel landing policy override |
| `branchCleanupPolicy` | string \| null | [BranchCleanupPolicyEnum](#branchcleanuppolicyenum) — per-vessel branch cleanup override |
| `active` | bool | Whether the vessel is active |
| `createdUtc` | string | ISO 8601 creation timestamp |
| `lastUpdateUtc` | string | ISO 8601 last update timestamp |

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
| `landingMode` | string \| null | [LandingModeEnum](#landingmodeenum) — per-voyage landing policy override |

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
| `branchName` | string \| null | Git branch name created for this mission |
| `dockId` | string \| null | Assigned dock (worktree) ID |
| `processId` | int \| null | OS process ID of the agent working this mission |
| `prUrl` | string \| null | Pull request URL |
| `commitHash` | string \| null | Git commit hash (HEAD) captured at mission completion |
| `diffSnapshot` | string \| null | Always `null` in list/status responses. Use `armada_get_mission_diff` to retrieve the full diff. |
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
| `state` | string | [CaptainStateEnum](#captainstateenum) value |
| `currentMissionId` | string \| null | Currently assigned mission ID |
| `currentDockId` | string \| null | Currently assigned dock (worktree) ID |
| `processId` | int \| null | OS process ID of the agent |
| `recoveryAttempts` | int | Number of recovery attempts after stalls |
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
| `id` | string | Event ID (prefix `evt_`) |
| `eventType` | string | Event type (e.g., `"mission.created"`, `"captain.stalled"`) |
| `entityType` | string \| null | Entity type (e.g., `"mission"`, `"captain"`, `"voyage"`) |
| `entityId` | string \| null | Entity ID |
| `captainId` | string \| null | Related captain ID |
| `missionId` | string \| null | Related mission ID |
| `vesselId` | string \| null | Related vessel ID |
| `voyageId` | string \| null | Related voyage ID |
| `message` | string | Human-readable event description |
| `payload` | string \| null | Optional JSON payload |
| `createdUtc` | string | ISO 8601 creation timestamp |

#### Dock

| Field | Type | Description |
|---|---|---|
| `id` | string | Dock ID (prefix `dck_`) |
| `vesselId` | string | Parent vessel ID |
| `captainId` | string \| null | Assigned captain ID |
| `branchName` | string \| null | Git branch name |
| `worktreePath` | string \| null | Filesystem path to the worktree |
| `active` | bool | Whether the dock is active |
| `createdUtc` | string | ISO 8601 creation timestamp |
| `lastUpdateUtc` | string | ISO 8601 last update timestamp |

#### MergeEntry

| Field | Type | Description |
|---|---|---|
| `id` | string | Merge entry ID (prefix `mrg_`) |
| `missionId` | string \| null | Associated mission ID |
| `vesselId` | string | Target vessel ID |
| `branchName` | string | Branch to merge |
| `targetBranch` | string | Target branch (default `"main"`) |
| `status` | string | [MergeStatusEnum](#mergestatusenum) value |
| `priority` | int | Queue priority (lower = higher) |
| `batchId` | string \| null | Batch identifier during testing |
| `testCommand` | string \| null | Custom test command |
| `testOutput` | string \| null | Test output/error |
| `testExitCode` | int \| null | Test exit code |
| `createdUtc` | string | ISO 8601 creation timestamp |
| `lastUpdateUtc` | string | ISO 8601 last update timestamp |
| `testStartedUtc` | string \| null | ISO 8601 test start timestamp |
| `completedUtc` | string \| null | ISO 8601 completion timestamp |

---

### Enumerations

#### MissionStatusEnum

| Value | Description |
|---|---|
| `Pending` | Not yet assigned to a captain |
| `Assigned` | Assigned to a captain, not yet started |
| `InProgress` | Captain actively working |
| `WorkProduced` | Agent exited successfully; work ready for landing |
| `PullRequestOpen` | Pull request created, awaiting merge confirmation |
| `Testing` | Work complete, under automated testing |
| `Review` | Awaiting human review |
| `Complete` | Successfully completed (code landed) |
| `Failed` | Mission failed |
| `LandingFailed` | Landing (merge/PR) failed; may be retried |
| `Cancelled` | Mission was cancelled |

#### LandingModeEnum

| Value | Description |
|---|---|
| `LocalMerge` | Merge branch into default branch locally and push |
| `PullRequest` | Create a pull request and poll for merge confirmation |
| `MergeQueue` | Enqueue the branch into Armada's merge queue |
| `None` | No automated landing; leave work on the branch |

#### BranchCleanupPolicyEnum

| Value | Description |
|---|---|
| `LocalOnly` | Delete the local branch after landing |
| `LocalAndRemote` | Delete both local and remote branches after landing |
| `None` | Do not delete branches after landing |

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

#### MergeStatusEnum

| Value | Description |
|---|---|
| `Queued` | Waiting to be picked up |
| `Testing` | Merged into integration branch, tests running |
| `Passed` | Tests passed |
| `Failed` | Tests failed |
| `Landed` | Successfully merged to target |
| `Cancelled` | Removed from queue |

#### AgentRuntimeEnum

| Value | Description |
|---|---|
| `ClaudeCode` | Anthropic Claude Code CLI |
| `Codex` | OpenAI Codex CLI |
| `Gemini` | Google Gemini CLI |
| `Cursor` | Cursor agent CLI |
| `Custom` | Custom agent runtime |

---

## Client Configuration

### Claude Desktop

Add Armada as an MCP server in your Claude Desktop configuration (`claude_desktop_config.json`):

```json
{
  "mcpServers": {
    "armada": {
      "url": "http://localhost:7891"
    }
  }
}
```

### Claude Code

Add Armada as an MCP server via the CLI:

```bash
claude mcp add armada http://localhost:7891
```

Or add to your project's `.mcp.json`:

```json
{
  "mcpServers": {
    "armada": {
      "type": "url",
      "url": "http://localhost:7891"
    }
  }
}
```

### Generic MCP Client

Any MCP-compatible client can connect to the Armada MCP server using the HTTP transport at the configured URL. The server advertises all tools via the standard MCP `tools/list` method during initialization.

**Tool discovery example (JSON-RPC):**

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/list"
}
```

**Tool call example (JSON-RPC):**

```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/call",
  "params": {
    "name": "armada_status",
    "arguments": {}
  }
}
```

**Response:**

```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "result": {
    "content": [
      {
        "type": "text",
        "text": "{\"totalCaptains\":4,\"idleCaptains\":1,...}"
      }
    ]
  }
}
```
