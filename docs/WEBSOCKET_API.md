# Armada WebSocket API Reference

**Version:** 1.0.0
**Default URL:** `ws://localhost:7890/ws`
**Protocol:** WebSocket (RFC 6455) via Watson7
**Transport:** JSON text frames

## Remote Proxy Note

When the dashboard is opened directly from `Armada.Server`, connect to `/ws` on the Armada origin as usual.

When the dashboard is opened from `Armada.Proxy`, the browser still connects to `/ws` on the current origin, but the proxy relays that websocket session through the outbound tunnel to the selected Armada deployment. The dashboard websocket message format does not change in proxy mode; only the transport path changes.

Proxy prerequisites:

- an authenticated proxy browser session
- a selected connected deployment in the proxy session
- an active tunnel connection for that deployment

If the selected deployment disconnects or the tunnel drops, the proxy closes the browser websocket and the dashboard must reconnect through the proxy origin.

---

## Table of Contents

- [Connection](#connection)
  - [Authentication](#authentication)
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
  - [mission.activity](#missionactivity)
  - [mission.status_changed](#missionstatus_changed)
  - [voyage.changed](#voyagechanged)
  - [captain.changed](#captainchanged)
  - [objective.changed](#objectivechanged)
  - [objective-refinement-session.changed](#objective-refinement-sessionchanged)
  - [objective-refinement-session.message.created](#objective-refinement-sessionmessagecreated)
  - [objective-refinement-session.message.updated](#objective-refinement-sessionmessageupdated)
  - [objective-refinement-session.summary.created](#objective-refinement-sessionsummarycreated)
  - [objective-refinement-session.applied](#objective-refinement-sessionapplied)
  - [objective-refinement-session.deleted](#objective-refinement-sessiondeleted)
  - [check-run.changed](#check-runchanged)
  - [deployment.changed](#deploymentchanged)
  - [deployment.progress](#deploymentprogress)
  - [environment.health](#environmenthealth)
  - [incident.changed](#incidentchanged)
  - [runbook-execution.changed](#runbook-executionchanged)
  - [approval-needed](#approval-needed)
  - [Ask Armada thread events](#ask-armada-thread-events)
  - [CLI permission events](#cli-permission-events)
  - [Planning session events](#planning-session-events)
  - [Black box events](#black-box-events)
    - [blackbox.captured](#blackboxcaptured)
    - [blackbox.capture_failed](#blackboxcapture_failed)
    - [blackbox.analyzed](#blackboxanalyzed)
    - [blackbox.resolved](#blackboxresolved)
    - [blackbox.deleted](#blackboxdeleted)
    - [blackbox.storage_fallback](#blackboxstorage_fallback)
    - [blackbox.gc_completed](#blackboxgc_completed)
    - [blackbox.recovery_run](#blackboxrecovery_run)
  - [Generic Events](#generic-events)
- [Command Actions](#command-actions)
  - [Status & Control](#status--control)
  - [Fleet Actions](#fleet-actions)
  - [Vessel Actions](#vessel-actions)
  - [Voyage Actions](#voyage-actions)
  - [Mission Actions](#mission-actions)
  - [Captain Actions](#captain-actions)
  - [CLI Permission Actions](#cli-permission-actions)
  - [Signal Actions](#signal-actions)
  - [Event Actions](#event-actions)
  - [Dock Actions](#dock-actions)
  - [Merge Queue Actions](#merge-queue-actions)
  - [Backup and Restore Actions](#backup-and-restore-actions)
  - [Black Box Actions](#black-box-actions)
    - [blackbox.get](#blackboxget)
    - [blackbox.brief](#blackboxbrief)
    - [blackbox.list](#blackboxlist)
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

### Authentication

Every `/ws` upgrade must be authenticated; an upgrade without a valid credential is refused with HTTP `401` before the
handshake completes. The server accepts the same credentials as the REST API, in this order:

1. REST headers on the upgrade request: `Authorization: Bearer <credential token>`, `X-Token: <session token>`, or
   `X-Api-Key: <api key>` (non-browser clients).
2. The `token` query parameter: `ws://localhost:7890/ws?token=<token>`. The value may be a session token (what the
   dashboard holds after login), a bearer credential token, or the API key. Percent-encode it (session tokens contain
   `+`, `/`, and `=`). **This is the recommended form for browsers.**
3. The `Sec-WebSocket-Protocol` header: one protocol entry of the form `armada-token.<base64url(token)>` (an `armada`
   marker entry is ignored; any other entry is also tried as a raw token). Note that the Admiral's WebSocket server
   (Watson 7.2) does not echo a selected subprotocol in its `101` response, and browsers fail a connection that offered
   subprotocols but received none, so browsers should use the query parameter; .NET `ClientWebSocket` and similar
   clients accept the protocol form.

Scoped session tokens minted for a captain's MCP connection (thread-scoped Ask Armada tokens and mission-scoped tokens)
are refused on `/ws` and on REST; they are accepted only by the MCP server.

Each socket keeps the identity that authenticated it (tenant, user, admin flags) and receives only what that identity is
entitled to:

| Event family | Delivered to |
|---|---|
| Entity changes (`mission.changed`, `voyage.changed`, `captain.changed`, `check-run.changed`, `objective.changed`, `deployment.*`, `incident.changed`, `runbook-execution.changed`, `approval-needed`, planning and objective-refinement session events, generic events) | Sockets of the entity's tenant. Global admins may opt in to every tenant with `{ "Route": "subscribe", "AllTenants": true }`. Events whose tenant cannot be resolved go to global admins only. |
| Black box events (`blackbox.*`) | Sockets of the entry's tenant, plus global admins opted in to all tenants (not narrowed to the entry's owner). `blackbox.gc_completed` has no tenant and goes to global admins only. |
| Ask Armada events (`ask.*`) | Only the sockets of the thread owner (same tenant and user). The all-tenants opt-in does not apply. |
| CLI permission events (`cli_permission.*`) | The request's approvers and owner: global admins of the request's tenant or opted in to all tenants, the tenant's tenant admins, and the owner of the Ask thread or mission. Each copy carries that recipient's own `canDecide` and `canRemember`. |

Commands (`Route: "command"`) require a global administrator (see [command](#command)).

Through `Armada.Proxy`, the browser's query string and `Sec-WebSocket-Protocol` header are forwarded to the deployment's
local `/ws` upgrade, so the same token authenticates the relayed socket.

### URL Construction

The WebSocket server runs on the same port as the Admiral REST API, at the `/ws` path. The default configuration is:

```
ws://localhost:7890/ws
```

The port is configurable via `ArmadaSettings.AdmiralPort` (default: `7890`). The hostname matches the REST API's `RestSettings.Hostname` (default: `localhost`).

### Port Discovery

The WebSocket endpoint is always available at `/ws` on the Admiral port. Clients can confirm the Admiral port by querying the REST API health endpoint:

```
GET http://localhost:7890/api/v1/status/health
```

The response includes the Admiral port. The WebSocket endpoint is at `/ws` on the same port.

### SSL/TLS

When `RestSettings.Ssl` is enabled, use `wss://` instead of `ws://`:

```
wss://localhost:7890/ws
```

SSL applies to both the REST API and the WebSocket server.

---

## Message Format

All messages are JSON text frames.

**Casing rule.** Everything the server sends over the WebSocket (event envelopes, payloads, command replies, and the
entities inside them) uses **camelCase** property names with enum values as strings. Client message field names are
matched **case-insensitively**: `Route`/`route`, `action`, `id`, `data`, and so on are all accepted (the examples use
`Route` for the route and camelCase for everything else). This differs from the REST API, whose bodies are PascalCase
(see [REST_API.md](REST_API.md)); each transport is consistent within itself, and neither will change casing within 1.0.

**Contract.** The endpoints, routes, command actions, and event types in this document are frozen for 1.0 and listed in
[API_SURFACE_1.0.md](API_SURFACE_1.0.md) (compatibility rules: [COMPATIBILITY.md](COMPATIBILITY.md)). New event types,
payload fields, and command actions may be added in minor releases, so clients must ignore event types and fields they do
not recognize. The Harbor link endpoint (`Harbor.LinkPath`, default `/v1.0/harbor/connect`) is a separate,
**experimental** WebSocket used only by Harbor split mode ([HARBOR_PROTOCOL.md](HARBOR_PROTOCOL.md)) and is excluded from
the promise.

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

Every server message includes a `type` field. Pushed events use the envelope `type`, `message`, `data`, `timestamp`
(UTC), where `message` is present only on events that carry one (generic, planning, refinement, and black box events);
command replies use `type` (`command.result` or `command.error`), `action`, and `data` (results) or `error` and `code`
(errors):

```json
{
  "type": "mission.changed",
  "data": {
    "id": "msn_abc123",
    "title": "Implement feature X",
    "status": "Complete",
    "voyageId": "vyg_abc123"
  },
  "timestamp": "2026-03-07T12:34:56.789Z"
}
```

---

## Routes

### subscribe

Request the current state and set the all-tenants opt-in. The server immediately replies with a `status.snapshot`
message containing the current Armada state. Event delivery itself does not depend on this route: every authenticated
socket receives the events it is entitled to from the moment it connects, so send `subscribe` first to get a baseline
before applying events.

**Client sends:**

```json
{
  "Route": "subscribe"
}
```

A global administrator may add `"AllTenants": true` to receive entity events of every tenant (the snapshot echoes
`"allTenants": true`). The flag is ignored for everyone else. Ask Armada events stay owner-only either way.

**Server responds with:** a [`status.snapshot`](#statussnapshot) message.

The client receives all broadcast events it is entitled to ([`mission.changed`](#missionchanged), [`voyage.changed`](#voyagechanged), [`captain.changed`](#captainchanged), [`objective.changed`](#objectivechanged), [`objective-refinement-session.changed`](#objective-refinement-sessionchanged), [`objective-refinement-session.message.created`](#objective-refinement-sessionmessagecreated), [`objective-refinement-session.message.updated`](#objective-refinement-sessionmessageupdated), [`objective-refinement-session.summary.created`](#objective-refinement-sessionsummarycreated), [`objective-refinement-session.applied`](#objective-refinement-sessionapplied), [`objective-refinement-session.deleted`](#objective-refinement-sessiondeleted), [`check-run.changed`](#check-runchanged), [`deployment.changed`](#deploymentchanged), [`deployment.progress`](#deploymentprogress), [`environment.health`](#environmenthealth), [`incident.changed`](#incidentchanged), [`runbook-execution.changed`](#runbook-executionchanged), [`approval-needed`](#approval-needed), [planning session events](#planning-session-events), [black box events](#black-box-events), and [generic events](#generic-events)) as they occur. [Ask Armada thread events](#ask-armada-thread-events) go only to the thread owner's sockets.

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

**Authorization:** WebSocket commands operate outside tenant scope (they read and write records by id across every
tenant), so every command, reads included, requires a **global administrator** (the API key identity or an admin user).
Tenant admins and regular users receive `command.error` with `code` `Forbidden` (error text `Forbidden: WebSocket
commands require a global administrator. Use the REST API, which is tenant-scoped.`) and use the REST API instead. `stop_server`, `backup`, and
`restore` are therefore admin-only.

See [Command Actions](#command-actions) for the current operational action set. This WebSocket surface focuses on real-time monitoring and core orchestration commands; newer REST-only helpers such as Workspace, planning sessions, request history, GitHub objective import, GitHub Actions sync, GitHub PR evidence, and runtime discovery remain HTTP-only.

---

## Server-Pushed Events

These events are delivered to the connected clients **entitled to them** (see [Authentication](#authentication): the entity's tenant, plus opted-in global admins) whenever state changes occur in the Armada system. Clients do not need to request these -- they are pushed automatically to every authenticated socket, whether or not it
has sent `subscribe`.

### status.snapshot

Sent in reply to the `subscribe` route. Contains a snapshot of the current Armada state. The snapshot is the same
aggregate that `GET /api/v1/status` returns for the socket's identity: a global administrator sees every tenant (whatever
`allTenants` is set to); any other identity sees only its own tenant.

```json
{
  "type": "status.snapshot",
  "data": {
    "totalCaptains": 4,
    "idleCaptains": 1,
    "workingCaptains": 2,
    "stalledCaptains": 1,
    "activeVoyages": 2,
    "inProgressVoyages": 1,
    "openVoyages": 1,
    "memoryPressureDeferrals": 0,
    "missionsByStatus": {
      "Pending": 3,
      "InProgress": 2,
      "Complete": 10,
      "Failed": 1
    },
    "recentSignals": [],
    "remoteTunnel": {
      "enabled": false,
      "state": "Disabled",
      "tunnelUrl": null,
      "instanceId": "armada-1f2e3d4c5b6a",
      "lastError": null,
      "reconnectAttempts": 0,
      "latencyMs": null
    },
    "timestampUtc": "2026-03-07T12:34:56.789Z"
  },
  "allTenants": false,
  "timestamp": "2026-03-07T12:34:56.789Z"
}
```

**`data` field:** [ArmadaStatus](#armadastatus) object. **`allTenants`** (top level, beside `data`): whether this socket
receives every tenant's entity events (true only for a global administrator that subscribed with `"AllTenants": true`).

---

### mission.changed

Broadcast when a mission's status changes (e.g., assigned, started, completed, failed).

```json
{
  "type": "mission.changed",
  "data": {
    "id": "msn_abc123def456ghi789jk",
    "title": "Add input validation to signup form",
    "status": "InProgress",
    "voyageId": "vyg_abc123def456ghi789jk"
  },
  "timestamp": "2026-03-07T12:35:00.000Z"
}
```

| Field | Type | Description |
|---|---|---|
| `type` | string | Always `"mission.changed"` |
| `data.id` | string | Mission ID (prefix `msn_`) |
| `data.title` | string \| null | Mission title |
| `data.status` | string | New [MissionStatusEnum](#missionstatusenum) value |
| `data.voyageId` | string \| null | Parent voyage ID, or null for a standalone mission |
| `data.activity` | object \| null | The captain's current activity while it runs (see [mission.activity](#missionactivity)), or null |
| `timestamp` | string | ISO 8601 UTC timestamp |

---

### mission.activity

Broadcast while a mission's captain runs and its runtime streams structured output (Claude Code, Codex; see
[Mission progress streaming](CAPTAINS.md#mission-progress-streaming)): the captain's latest activity, at most about once
a second per mission (the newest wins). Not recorded as an event.

```json
{
  "type": "mission.activity",
  "data": {
    "id": "msn_abc123def456ghi789jk",
    "voyageId": "vyg_abc123def456ghi789jk",
    "activity": {
      "kind": "ToolCall",
      "toolName": "Bash",
      "description": "Running tests",
      "detail": "dotnet test src/App.sln",
      "summary": "Running tests: dotnet test src/App.sln",
      "timestampUtc": "2026-03-07T12:35:00.000Z"
    }
  },
  "timestamp": "2026-03-07T12:35:00.100Z"
}
```

| Field | Type | Description |
|---|---|---|
| `data.id` | string | Mission ID |
| `data.voyageId` | string \| null | Parent voyage ID |
| `data.activity.kind` | string | `ToolCall`, `Text`, or `Thinking` |
| `data.activity.toolName` | string \| null | Tool name of a tool call (for example `Bash`, `mcp__armada__armada_status`) |
| `data.activity.description` | string \| null | The description the tool call gave (Claude Code's Bash description) |
| `data.activity.detail` | string \| null | Short input of a tool call (the command, the path) or the first line of text or reasoning, at most 200 characters |
| `data.activity.summary` | string | One line describing the activity |
| `data.activity.timestampUtc` | string | When it was observed |

---

### mission.status_changed

Broadcast (and recorded as an event) when a mission's status changes. It carries the generic event fields plus the new
and previous status as typed fields, so clients never need to read them from the `message` text (which is not stable).
The recorded event's `Payload` (REST and MCP events) holds the same two values as `{"Status": ..., "PreviousStatus": ...}`.

```json
{
  "type": "mission.status_changed",
  "message": "Mission msn_abc123def456ghi789jk status changed",
  "data": {
    "entityType": "mission",
    "entityId": "msn_abc123def456ghi789jk",
    "captainId": "cpt_abc123def456ghi789jk",
    "missionId": "msn_abc123def456ghi789jk",
    "vesselId": "vsl_abc123def456ghi789jk",
    "voyageId": "vyg_abc123def456ghi789jk",
    "status": "WorkProduced",
    "previousStatus": "InProgress"
  },
  "timestamp": "2026-03-07T12:35:00.000Z"
}
```

| Field | Type | Description |
|---|---|---|
| `type` | string | Always `"mission.status_changed"` |
| `message` | string | Human-readable description (not stable; do not parse) |
| `data.entityType` | string | `"mission"` |
| `data.entityId` / `data.missionId` | string | Mission ID (prefix `msn_`) |
| `data.captainId` | string \| null | Assigned captain |
| `data.vesselId` | string \| null | Mission's vessel |
| `data.voyageId` | string \| null | Mission's voyage |
| `data.status` | string | New [MissionStatusEnum](#missionstatusenum) value |
| `data.previousStatus` | string \| null | Previous [MissionStatusEnum](#missionstatusenum) value, or null when unknown |
| `timestamp` | string | ISO 8601 UTC timestamp |

The .NET client reads this payload as `Armada.Client.Socket.MissionStatusChangedEvent` (`MissionStatus` and
`PreviousMissionStatus` are the typed values).

---

### voyage.changed

Broadcast when a voyage state changes.

```json
{
  "type": "voyage.changed",
  "data": {
    "id": "vyg_abc123def456ghi789jk",
    "status": "Complete",
    "title": "API Hardening"
  },
  "timestamp": "2026-03-07T12:35:00.000Z"
}
```

| Field | Type | Description |
|---|---|---|
| `type` | string | Always `"voyage.changed"` |
| `data.id` | string | Voyage ID (prefix `vyg_`) |
| `data.status` | string | New [VoyageStatusEnum](#voyagestatusenum) value |
| `data.title` | string \| null | Voyage title |
| `timestamp` | string | ISO 8601 UTC timestamp |

---

### captain.changed

Broadcast when a captain's state changes (e.g., idle to working, working to stalled).

```json
{
  "type": "captain.changed",
  "data": {
    "id": "cpt_abc123def456ghi789jk",
    "state": "Working",
    "name": "captain-1"
  },
  "timestamp": "2026-03-07T12:35:00.000Z"
}
```

| Field | Type | Description |
|---|---|---|
| `type` | string | Always `"captain.changed"` |
| `data.id` | string | Captain ID (prefix `cpt_`) |
| `data.state` | string | New [CaptainStateEnum](#captainstateenum) value |
| `data.name` | string \| null | Captain display name |
| `timestamp` | string | ISO 8601 UTC timestamp |

---

### check-run.changed

Broadcast when a structured check run is created, imported, updated, or retried.

```json
{
  "type": "check-run.changed",
  "data": {
    "id": "chk_abc123def456ghi789jk",
    "status": "Passed",
    "type": "UnitTest",
    "vesselId": "vsl_abc123def456ghi789jk",
    "label": "Unit tests"
  },
  "timestamp": "2026-03-07T12:35:00.000Z"
}
```

| Field | Type | Description |
|---|---|---|
| `type` | string | Always `"check-run.changed"` |
| `data` | object | Full serialized `CheckRun` payload with enum values emitted as strings |
| `timestamp` | string | ISO 8601 UTC timestamp |

---

### objective.changed

Broadcast when an objective or backlog record is created or updated.

```json
{
  "type": "objective.changed",
  "data": {
    "id": "obj_abc123def456ghi789jk",
    "status": "Scoped",
    "title": "Stabilize May rollout"
  },
  "timestamp": "2026-03-07T12:35:00.000Z"
}
```

| Field | Type | Description |
|---|---|---|
| `type` | string | Always `"objective.changed"` |
| `data` | object | Full serialized `Objective` payload with enum values emitted as strings, including backlog metadata and linkage fields |
| `timestamp` | string | ISO 8601 UTC timestamp |

---

### objective-refinement-session.changed

Broadcast when a backlog refinement session changes state.

```json
{
  "type": "objective-refinement-session.changed",
  "message": "Objective refinement session updated",
  "data": {
    "session": {
      "id": "ors_abc123def456ghi789jk",
      "objectiveId": "obj_abc123def456ghi789jk",
      "captainId": "cpt_abc123def456ghi789jk",
      "status": "Active"
    }
  },
  "timestamp": "2026-03-07T12:35:00.000Z"
}
```

| Field | Type | Description |
|---|---|---|
| `type` | string | Always `"objective-refinement-session.changed"` |
| `message` | string | Human-readable event label |
| `data.session` | object | Full serialized `ObjectiveRefinementSession` payload |
| `timestamp` | string | ISO 8601 UTC timestamp |

---

### objective-refinement-session.message.created

Broadcast when a refinement transcript message is created.

```json
{
  "type": "objective-refinement-session.message.created",
  "message": "Objective refinement message created",
  "data": {
    "sessionId": "ors_abc123def456ghi789jk",
    "objectiveId": "obj_abc123def456ghi789jk",
    "message": {
      "id": "orm_abc123def456ghi789jk",
      "role": "User",
      "sequence": 1,
      "content": "Focus on rollback safety."
    }
  },
  "timestamp": "2026-03-07T12:35:00.000Z"
}
```

| Field | Type | Description |
|---|---|---|
| `type` | string | Always `"objective-refinement-session.message.created"` |
| `message` | string | Human-readable event label |
| `data.sessionId` | string | Refinement session ID (prefix `ors_`) |
| `data.objectiveId` | string | Linked backlog/objective ID (prefix `obj_`) |
| `data.message` | object | Full serialized `ObjectiveRefinementMessage` payload |
| `timestamp` | string | ISO 8601 UTC timestamp |

---

### objective-refinement-session.message.updated

Broadcast when a refinement transcript message is updated, for example when an assistant turn finishes or selection state changes.

```json
{
  "type": "objective-refinement-session.message.updated",
  "message": "Objective refinement message updated",
  "data": {
    "sessionId": "ors_abc123def456ghi789jk",
    "objectiveId": "obj_abc123def456ghi789jk",
    "message": {
      "id": "orm_def456ghi789jkl012mn",
      "role": "Assistant",
      "sequence": 2,
      "isSelected": true
    }
  },
  "timestamp": "2026-03-07T12:35:00.000Z"
}
```

| Field | Type | Description |
|---|---|---|
| `type` | string | Always `"objective-refinement-session.message.updated"` |
| `message` | string | Human-readable event label |
| `data.sessionId` | string | Refinement session ID (prefix `ors_`) |
| `data.objectiveId` | string | Linked backlog/objective ID (prefix `obj_`) |
| `data.message` | object | Full serialized `ObjectiveRefinementMessage` payload |
| `timestamp` | string | ISO 8601 UTC timestamp |

---

### objective-refinement-session.summary.created

Broadcast when Armada creates a structured refinement summary from the transcript.

```json
{
  "type": "objective-refinement-session.summary.created",
  "message": "Objective refinement summary created",
  "data": {
    "sessionId": "ors_abc123def456ghi789jk",
    "messageId": "orm_def456ghi789jkl012mn",
    "summary": {
      "summary": "Stabilize rollback and verification sequencing."
    }
  },
  "timestamp": "2026-03-07T12:35:00.000Z"
}
```

| Field | Type | Description |
|---|---|---|
| `type` | string | Always `"objective-refinement-session.summary.created"` |
| `message` | string | Human-readable event label |
| `data.sessionId` | string | Refinement session ID (prefix `ors_`) |
| `data.messageId` | string | Source transcript message ID (prefix `orm_`) |
| `data.summary` | object | Serialized `ObjectiveRefinementSummaryResponse` payload |
| `timestamp` | string | ISO 8601 UTC timestamp |

---

### objective-refinement-session.applied

Broadcast when Armada applies a refinement summary back to the linked backlog item. Unless the apply request set
`EndSession` to `false` (default `true`), the session is then stopped, which releases its captain: expect
`objective-refinement-session.changed` (status `Stopped`), `captain.changed`, and the generic
`objective-refinement-session.stopped` event to follow.

```json
{
  "type": "objective-refinement-session.applied",
  "message": "Objective refinement summary applied",
  "data": {
    "sessionId": "ors_abc123def456ghi789jk",
    "objectiveId": "obj_abc123def456ghi789jk",
    "summary": {
      "summary": "Stabilize rollback and verification sequencing."
    }
  },
  "timestamp": "2026-03-07T12:35:00.000Z"
}
```

| Field | Type | Description |
|---|---|---|
| `type` | string | Always `"objective-refinement-session.applied"` |
| `message` | string | Human-readable event label |
| `data.sessionId` | string | Refinement session ID (prefix `ors_`) |
| `data.objectiveId` | string | Linked backlog/objective ID (prefix `obj_`) |
| `data.summary` | object | Serialized `ObjectiveRefinementSummaryResponse` payload |
| `timestamp` | string | ISO 8601 UTC timestamp |

---

### objective-refinement-session.deleted

Broadcast when a refinement session and its transcript are deleted (an active session is stopped first).

```json
{
  "type": "objective-refinement-session.deleted",
  "message": "Objective refinement session deleted",
  "data": {
    "sessionId": "ors_abc123def456ghi789jk",
    "objectiveId": "obj_abc123def456ghi789jk"
  },
  "timestamp": "2026-03-07T12:35:00.000Z"
}
```

| Field | Type | Description |
|---|---|---|
| `type` | string | Always `"objective-refinement-session.deleted"` |
| `message` | string | Human-readable event label |
| `data.sessionId` | string | Refinement session ID (prefix `ors_`) |
| `data.objectiveId` | string | Linked backlog/objective ID (prefix `obj_`) |
| `timestamp` | string | ISO 8601 UTC timestamp |

---

### deployment.changed

Broadcast when a deployment record changes, including approval, execution, verification, and rollback state.

```json
{
  "type": "deployment.changed",
  "data": {
    "id": "dpl_abc123def456ghi789jk",
    "title": "Release 1.4.0 to staging",
    "status": "Running",
    "environmentId": "env_abc123def456ghi789jk",
    "environmentName": "Staging",
    "verificationStatus": "NotRun",
    "...": "..."
  },
  "timestamp": "2026-03-07T12:35:00.000Z"
}
```

| Field | Type | Description |
|---|---|---|
| `type` | string | Always `"deployment.changed"` |
| `data` | object | Full serialized `Deployment` payload with enum values emitted as strings (`status`: `PendingApproval`, `Running`, `Succeeded`, `VerificationFailed`, `Failed`, `Denied`, `RollingBack`, `RolledBack`; `verificationStatus`: `NotRun`, `Running`, `Passed`, `Failed`, `Partial`, `Skipped`). `environmentName` is the display name of the target environment (clients that name a deployment the way the inbox does use `environmentName`, else the deployment `id`) |
| `timestamp` | string | ISO 8601 UTC timestamp |

---

### deployment.progress

Broadcast together with every `deployment.changed`, carrying a compact progress view of the same deployment.

```json
{
  "type": "deployment.progress",
  "data": {
    "id": "dpl_abc123def456ghi789jk",
    "title": "Release 1.4.0 to staging",
    "status": "Running",
    "verificationStatus": "NotRun",
    "environmentId": "env_abc123def456ghi789jk",
    "environmentName": "Staging",
    "startedUtc": "2026-03-07T12:34:00.000Z",
    "completedUtc": null,
    "lastUpdateUtc": "2026-03-07T12:35:00.000Z"
  },
  "timestamp": "2026-03-07T12:35:00.000Z"
}
```

| Field | Type | Description |
|---|---|---|
| `type` | string | Always `"deployment.progress"` |
| `data.id` | string | Deployment ID (prefix `dpl_`) |
| `data.title` | string | Deployment title |
| `data.status` | string | Current deployment status |
| `data.verificationStatus` | string | Current verification status |
| `data.environmentId` | string \| null | Target environment ID (prefix `env_`) |
| `data.environmentName` | string \| null | Target environment name |
| `data.startedUtc` | string \| null | Execution start timestamp |
| `data.completedUtc` | string \| null | Completion timestamp |
| `data.lastUpdateUtc` | string | Last update timestamp |
| `timestamp` | string | ISO 8601 UTC timestamp |

---

### environment.health

Broadcast together with `deployment.changed` whenever the deployment names an environment (by `environmentId` or
`environmentName`), carrying the rollout-monitoring and verification evidence for that environment.

```json
{
  "type": "environment.health",
  "data": {
    "environmentId": "env_abc123def456ghi789jk",
    "environmentName": "Staging",
    "id": "dpl_abc123def456ghi789jk",
    "title": "Release 1.4.0 to staging",
    "status": "Succeeded",
    "verificationStatus": "Passed",
    "lastMonitoredUtc": "2026-03-07T12:40:00.000Z",
    "lastRegressionAlertUtc": null,
    "latestMonitoringSummary": "Health endpoint returned 200 OK",
    "monitoringFailureCount": 0
  },
  "timestamp": "2026-03-07T12:40:00.000Z"
}
```

| Field | Type | Description |
|---|---|---|
| `type` | string | Always `"environment.health"` |
| `data.environmentId` | string \| null | Environment ID (prefix `env_`) |
| `data.environmentName` | string \| null | Environment name |
| `data.id` | string | Deployment ID (prefix `dpl_`) the update comes from |
| `data.title` | string | Deployment title |
| `data.status` | string | Deployment status |
| `data.verificationStatus` | string | Deployment verification status |
| `data.lastMonitoredUtc` | string \| null | Last rollout-monitoring check |
| `data.lastRegressionAlertUtc` | string \| null | Last regression alert |
| `data.latestMonitoringSummary` | string \| null | Latest monitoring summary text |
| `data.monitoringFailureCount` | int | Consecutive monitoring failures |
| `timestamp` | string | ISO 8601 UTC timestamp |

---

### incident.changed

Broadcast when an incident record is created or updated.

| Field | Type | Description |
|---|---|---|
| `type` | string | Always `"incident.changed"` |
| `data` | object | Full serialized `Incident` payload (prefix `inc_`; `status`: `Open`, `Monitoring`, `Mitigated`, `RolledBack`, `Closed`; `severity`: `Critical`, `High`, `Medium`, `Low`; includes `environmentId` and `environmentName`) |
| `timestamp` | string | ISO 8601 UTC timestamp |

---

### runbook-execution.changed

Broadcast when a runbook execution is started, updated, or finished.

| Field | Type | Description |
|---|---|---|
| `type` | string | Always `"runbook-execution.changed"` |
| `data` | object | Full serialized `RunbookExecution` payload (prefix `rbx_`; `status`: `Running`, `Completed`, `Cancelled`; includes `environmentId` and `environmentName`) |
| `timestamp` | string | ISO 8601 UTC timestamp |

---

### approval-needed

Broadcast when a mission enters review and awaits an explicit approve or deny decision.

```json
{
  "type": "approval-needed",
  "data": {
    "entityType": "mission",
    "entityId": "msn_abc123def456ghi789jk",
    "missionId": "msn_abc123def456ghi789jk",
    "title": "Review login rate limiting",
    "status": "Review",
    "vesselId": "vsl_abc123def456ghi789jk",
    "voyageId": "vyg_abc123def456ghi789jk",
    "reviewRequestedUtc": "2026-03-07T12:35:00.000Z"
  },
  "timestamp": "2026-03-07T12:35:00.000Z"
}
```

| Field | Type | Description |
|---|---|---|
| `type` | string | Always `"approval-needed"` |
| `data.entityType` | string | Currently `"mission"` |
| `data.entityId` | string | Entity identifier requiring approval |
| `data.missionId` | string | Mission identifier |
| `data.title` | string \| null | Mission title |
| `data.status` | string | Current mission status, normally `Review` |
| `data.vesselId` | string \| null | Linked vessel ID |
| `data.voyageId` | string \| null | Linked voyage ID |
| `data.reviewRequestedUtc` | string \| null | Review request timestamp |
| `timestamp` | string | ISO 8601 UTC timestamp |

---

### Ask Armada thread events

Sent only to the sockets of the thread's owner. Every payload carries `threadId`. Field names are camelCase.

| Type | Payload | When |
|---|---|---|
| `ask.turn` | `{ threadId, turnId, state, messageId, error? }` | `state` is `started`, then `completed`, `failed`, or `cancelled` (`messageId` is the persisted reply or error message, null when cancelled) |
| `ask.chunk` | `{ threadId, turnId, delta }` | Streamed reply text of a running turn |
| `ask.thinking` | `{ threadId, turnId, delta }` | Streamed reasoning (when `ShowThinking`) |
| `ask.tool` | `{ threadId, turnId, phase, id, name, arguments, ok, elapsedMs, result, permissionDenied }` | A tool call started (`phase: "started"`) or completed (`"completed"`). `permissionDenied` is `true` when the CLI refused the call because the turn's CLI tool permission policy did not grant it (Claude Code reports this in its result event, so a second `completed` event for an already completed call can arrive at the end of the turn); null otherwise |
| `ask.message` | `{ threadId, message }` | Any persisted message (user, reply, proposal card, action result, work update, summary, error, CLI permission card) with `toolCallCount` (read the calls with `POST /api/v1/ask/threads/{id}/messages/{messageId}/tool-calls/enumerate`), and `proposal`, `trackedWork`, and `cliPermissionRequest` embedded, and a captain reply's turn telemetry in `metrics` (see `AskMessage` in REST_API.md); also re-sent for a confirm card when its proposal is decided and for a CLI permission card when its request is decided, expires, or is cancelled |
| `ask.proposal` | `{ threadId, proposal }` | A proposal was created or changed status (`expiresUtc` set while pending) |
| `ask.work` | `{ threadId, trackedWorkId, snapshot, trackedWork }` | The snapshot of tracked work changed: counts with `missionCount` and `targetCount`, no rows (see `AskWorkSnapshot` in REST_API.md; rows are paged with `.../work/{workId}/missions/enumerate` and `.../targets/enumerate`) |
| `ask.thread` | `{ threadId, thread }` | Title, counters (`messageCount`, `unreadCount`), `activeWorkCount`, or `activeTurnId` changed |

The direct captain chat endpoint (`POST /api/v1/captains/{id}/chat` with a `TurnId`) also streams `ask.chunk`,
`ask.thinking`, and `ask.tool` (without `threadId`) to the caller's own sockets only.

```json
{
  "type": "ask.proposal",
  "data": {
    "threadId": "ath_muu5...",
    "proposal": { "id": "aap_muu5...", "toolName": "dispatch", "status": "Pending", "summaryText": "Dispatch voyage \"Fix flaky test\" to vessel vsl_... with 1 mission(s)", "expiresUtc": "2026-10-04T19:00:00Z" }
  },
  "timestamp": "2026-10-04T18:00:00.000Z"
}
```

### CLI permission events

Sent when a CLI captain's permission prompt (for example a shell command under the `ApproveInArmada` policy) starts
waiting for an approver, and when it is allowed, denied, expires, or is cancelled. Requests that a rule decides at once
are recorded but not announced. Recipients are the request's approvers and owner (see
[Authentication](#authentication)); the all-tenants opt-in admits a global admin to other tenants' requests. Field names
are camelCase.

| Type | Payload | When |
|---|---|---|
| `cli_permission.requested` | `{ requestId, status, request }` | A request is `Pending` |
| `cli_permission.resolved` | `{ requestId, status, request }` | The request became `Allowed`, `Denied`, `Expired`, or `Cancelled` |

`request` is the [CliPermissionRequest](REST_API.md#clipermissionrequest) (tool, redacted input and summary, suggested
rule, captain, mission, vessel or thread, `expiresUtc`, decision fields) with `canDecide` and `canRemember` computed for
the receiving socket's identity (`canDecide` is false once the request is no longer pending).

```json
{
  "type": "cli_permission.requested",
  "data": {
    "requestId": "cpr_muu5...",
    "status": "Pending",
    "request": { "id": "cpr_muu5...", "toolName": "Bash", "summaryText": "npm test", "suggestedRule": "Bash(npm test:*)", "captainId": "cpt_...", "threadId": "ath_...", "status": "Pending", "expiresUtc": "2026-10-05T12:10:00Z", "canDecide": true, "canRemember": true }
  },
  "timestamp": "2026-10-05T12:00:00.000Z"
}
```

### Planning session events

Broadcast to the planning session's tenant. Field names are camelCase.

| Type | Payload | When |
|---|---|---|
| `planning-session.changed` | `{ session }` | The session's status or metadata changed |
| `planning-session.message.created` | `{ sessionId, message }` | A transcript message was added |
| `planning-session.message.updated` | `{ sessionId, message }` | A streamed assistant message was updated |
| `planning-session.thinking` | `{ sessionId, messageId, delta }` | Streamed reasoning for the running turn |
| `planning-session.tool` | `{ sessionId, messageId, phase, id, name, arguments, ok, elapsedMs, result }` | A tool call started or completed |
| `planning-session.summary.created` | `{ sessionId, messageId, draft }` | A dispatch draft was generated |
| `planning-session.dispatch.created` | `{ sessionId, voyageId, messageId }` | A voyage was dispatched from the session |
| `planning-session.deleted` | `{ sessionId }` | The session was deleted |

`planning-session.created` and `planning-session.stopped` (and the refinement-session `created` and `stopped`
events) are generic events (see below).

---

### Black box events

Sent when the [black box](REST_API.md#black-box) captures, analyzes, resolves or deletes an entry, falls back to the
database store, finishes a garbage collection pass, or runs a recovery path. Each is also recorded as an event
(`EntityType` `blackbox`, `EntityId` the entry id, `Payload` the same values) readable through REST and MCP events.
The envelope carries a `message` (not stable; do not parse it). Delivery follows the entry's tenant: every socket of
that tenant (and global admins opted in to all tenants) receives the event, even though REST and MCP reads of a regular
user's entries are narrowed to their owner. `blackbox.gc_completed` has no tenant and reaches global admins only.
Field names are camelCase; enum values are strings.

| Type | Payload | When |
|---|---|---|
| [`blackbox.captured`](#blackboxcaptured) | `{ entryId, missionId, voyageId, vesselId, phase, step, kind, attempt, captureMode }` | An entry was captured (inline at the failure site, by the sweeper, or by a backfill) |
| [`blackbox.capture_failed`](#blackboxcapture_failed) | `{ missionId, voyageId, step, error }` | A capture threw; the failure itself is unaffected |
| [`blackbox.analyzed`](#blackboxanalyzed) | `{ entryId, missionId, voyageId, state, triage, rulesVersion }` | Enrichment and analysis finished (also after a re-analysis) |
| [`blackbox.resolved`](#blackboxresolved) | `{ entryId, missionId, voyageId, resolution }` | The resolution was set or reopened |
| [`blackbox.deleted`](#blackboxdeleted) | `{ entryId, missionId, voyageId, reason }` | An entry was deleted (`reason` `Manual`, `Purge`, `Expired`, `Evicted`, `Cascade`, `Retry` or `TenantDeleted`) |
| [`blackbox.storage_fallback`](#blackboxstorage_fallback) | `{ entryId, from, to, error }` | The configured store failed and the entry was written to the database instead |
| [`blackbox.gc_completed`](#blackboxgc_completed) | The `BlackBoxGcReport` (`dryRun`, `startedUtc`, `finishedUtc`, `durationMs`, `expired`, `evicted`, `cascaded`, `retriedDeletes`, `orphansRemoved`, `objectsMissing`, `bytesFreed`, `failures`, `candidateEntryIds`, `errors`) | A garbage collection pass finished |
| [`blackbox.recovery_run`](#blackboxrecovery_run) | `{ entryId, pathId, recommendationId, dryRun, succeeded, stepsRun, stepsFailed, userId }` | Every recovery dry run and run, and every recommendation dry run and apply |

#### blackbox.captured

```json
{
  "type": "blackbox.captured",
  "message": "Black box captured for msn_mvu1fjp0_9Wd6Uuyp262: External/OperatorTransition",
  "data": {
    "entryId": "bbx_mvu1ieiz_WMTEXGqlAJ5",
    "missionId": "msn_mvu1fjp0_9Wd6Uuyp262",
    "voyageId": "vyg_mvu1fjoz_xO6urNkcaHp",
    "vesselId": "vsl_mvu1ffz5_4Vb0gY5fhKv",
    "phase": "External",
    "step": "OperatorTransition",
    "kind": null,
    "attempt": 2,
    "captureMode": "Inline"
  },
  "timestamp": "2026-10-29T21:13:06.061924Z"
}
```

This entry was captured inline when an operator moved the mission to `Cancelled` (`PUT /api/v1/missions/{id}/status`).

#### blackbox.capture_failed

```json
{
  "type": "blackbox.capture_failed",
  "message": "Black box capture failed for msn_mvu1fjp0_9Wd6Uuyp262: The database is locked.",
  "data": {
    "missionId": "msn_mvu1fjp0_9Wd6Uuyp262",
    "voyageId": "vyg_mvu1fjoz_xO6urNkcaHp",
    "step": "ProcessCrash",
    "error": "The database is locked."
  },
  "timestamp": "2026-10-29T21:13:06.061924Z"
}
```

`error` is redacted. The sweeper captures a missed failure later (`captureMode` `Sweeper`).

#### blackbox.analyzed

```json
{
  "type": "blackbox.analyzed",
  "message": "Black box analyzed: bbx_mvu1ieiz_WMTEXGqlAJ5",
  "data": {
    "entryId": "bbx_mvu1ieiz_WMTEXGqlAJ5",
    "missionId": "msn_mvu1fjp0_9Wd6Uuyp262",
    "voyageId": "vyg_mvu1fjoz_xO6urNkcaHp",
    "state": "Analyzed",
    "triage": "External",
    "rulesVersion": 1
  },
  "timestamp": "2026-10-29T21:13:06.070652Z"
}
```

After `POST /api/v1/blackbox/{id}/reanalyze` the message reads `Black box re-analyzed: ...`.

#### blackbox.resolved

```json
{
  "type": "blackbox.resolved",
  "message": "Black box entry bbx_mvu1ftcv_zcdofOBmjhY set to Resolved",
  "data": {
    "entryId": "bbx_mvu1ftcv_zcdofOBmjhY",
    "missionId": "msn_mvu1fjp0_9Wd6Uuyp262",
    "voyageId": "vyg_mvu1fjoz_xO6urNkcaHp",
    "resolution": "Resolved"
  },
  "timestamp": "2026-10-29T21:11:39.835678Z"
}
```

#### blackbox.deleted

```json
{
  "type": "blackbox.deleted",
  "message": "Black box entry deleted (Manual): bbx_mvu1ftep_b5OtDr4leub",
  "data": {
    "entryId": "bbx_mvu1ftep_b5OtDr4leub",
    "missionId": "msn_mvu1fjpe_wAvcJzyllyF",
    "voyageId": "vyg_mvu1fjoz_xO6urNkcaHp",
    "reason": "Manual"
  },
  "timestamp": "2026-10-29T21:15:02.118204Z"
}
```

#### blackbox.storage_fallback

```json
{
  "type": "blackbox.storage_fallback",
  "message": "Black box storage fell back to the database for bbx_mvu1ieiz_WMTEXGqlAJ5",
  "data": {
    "entryId": "bbx_mvu1ieiz_WMTEXGqlAJ5",
    "from": "S3",
    "to": "Database",
    "error": "The remote name could not be resolved: 's3.devbox.internal'"
  },
  "timestamp": "2026-10-29T21:13:06.064310Z"
}
```

#### blackbox.gc_completed

```json
{
  "type": "blackbox.gc_completed",
  "message": "Black box garbage collection: 2 expired, 0 evicted, 0 cascaded, 0 orphans removed, 0 objects missing, 0 failures",
  "data": {
    "dryRun": false,
    "startedUtc": "2026-10-29T22:10:34.416490Z",
    "finishedUtc": "2026-10-29T22:10:34.431002Z",
    "durationMs": 14,
    "expired": 2,
    "evicted": 0,
    "cascaded": 0,
    "retriedDeletes": 0,
    "orphansRemoved": 0,
    "objectsMissing": 0,
    "bytesFreed": 18734,
    "failures": 0,
    "candidateEntryIds": [],
    "errors": []
  },
  "timestamp": "2026-10-29T22:10:34.431500Z"
}
```

#### blackbox.recovery_run

```json
{
  "type": "blackbox.recovery_run",
  "message": "Recovery path bbx-exec-006.restart ran on black box entry bbx_mvu1ftcv_zcdofOBmjhY",
  "data": {
    "entryId": "bbx_mvu1ftcv_zcdofOBmjhY",
    "pathId": "bbx-exec-006.restart",
    "recommendationId": null,
    "dryRun": false,
    "succeeded": true,
    "stepsRun": 1,
    "stepsFailed": 0,
    "userId": "default"
  },
  "timestamp": "2026-10-29T21:11:39.804858Z"
}
```

A dry run reads `Recovery path ... dry run on black box entry ...`, a failed run `... failed on ...`, and a
recommendation `Recommendation <id> ...` (with `recommendationId` set).

---

### Generic Events

Broadcast when the Admiral records an entity event that has no dedicated payload (deletions, purges, review
decisions, landing outcomes, captain launches, planning and refinement session lifecycle). Every generic event has the
same payload. The generic event types in 1.0 (also listed in [API_SURFACE_1.0.md](API_SURFACE_1.0.md#events)) are:

| Entity | Generic event types |
|---|---|
| Captain | `captain.launched`, `captain.batch_deleted` |
| Dock | `dock.deleted`, `dock.purged`, `dock.repaired`, `dock.unstuck`, `dock.batch_deleted` |
| Event | `event.deleted`, `event.batch_deleted` |
| Fleet | `fleet.batch_deleted` |
| Merge queue | `merge.purged`, `merge.batch_purged` |
| Mission | `mission.completed`, `mission.deleted`, `mission.landing_failed`, `mission.manual_complete_no_dock`, `mission.pull_request_open`, `mission.restarted`, `mission.review_approved`, `mission.review_denied`, `mission.work_produced`, `mission.batch_deleted`, and [`mission.status_changed`](#missionstatus_changed) (which adds `status` and `previousStatus`) |
| Objective refinement session | `objective-refinement-session.created`, `objective-refinement-session.stopped` |
| Planning session | `planning-session.created`, `planning-session.stopped` |
| Signal | `signal.batch_deleted` |
| Vessel | `vessel.batch_deleted` |
| Voyage | `voyage.deleted`, `voyage.batch_deleted` |

New types may be added in minor releases; clients must ignore types they do not recognize.

```json
{
  "type": "mission.deleted",
  "message": "Mission msn_abc123def456ghi789jk deleted",
  "data": {
    "entityType": "mission",
    "entityId": "msn_abc123def456ghi789jk",
    "captainId": null,
    "missionId": "msn_abc123def456ghi789jk",
    "vesselId": "vsl_abc123def456ghi789jk",
    "voyageId": null
  },
  "timestamp": "2026-03-07T12:35:00.000Z"
}
```

| Field | Type | Description |
|---|---|---|
| `type` | string | Event type string (for example `"mission.deleted"`, `"mission.landing_failed"`, `"dock.purged"`) |
| `message` | string | Human-readable event description (not stable) |
| `data.entityType` | string \| null | Entity type (`mission`, `voyage`, `captain`, `dock`, ...) |
| `data.entityId` | string \| null | Entity identifier |
| `data.captainId` | string \| null | Related captain |
| `data.missionId` | string \| null | Related mission |
| `data.vesselId` | string \| null | Related vessel |
| `data.voyageId` | string \| null | Related voyage |
| `timestamp` | string | ISO 8601 UTC timestamp |

Some planning and refinement session events (`planning-session.summary.created`, `planning-session.dispatch.created`,
`planning-session.deleted`, `objective-refinement-session.summary.created`, `objective-refinement-session.applied`,
`objective-refinement-session.deleted`) are delivered twice: once with their dedicated payload and once as a generic
event with the same type. Tell them apart by the payload (`sessionId` versus `entityId`).

---

## Command Actions

Commands are sent via the `command` route. Each command returns a `command.result` message on success or a `command.error` message on failure. The WebSocket command surface covers Armada's real-time and core operational flows; it does not currently expose the REST-only Workspace, planning-session, request-history, GitHub objective import, GitHub Actions sync, GitHub PR evidence, runtime-helper, or OpenAPI discovery surfaces.

### Command Actions Summary

| Category | Action | Description | Required Fields |
|---|---|---|---|
| **Status & Control** | `status` | Get current ArmadaStatus | - |
| | `stop_captain` | Stop specific captain | `captainId` |
| | `stop_all` | Emergency stop all captains | - |
| | `stop_server` | Stop the Admiral | - |
| **Fleet** | `list_fleets` | List/enumerate fleets | optional `query` |
| | `get_fleet` | Get fleet by ID | `id` |
| | `create_fleet` | Create fleet | `data` |
| | `update_fleet` | Update fleet | `id`, `data` |
| | `delete_fleet` | Delete fleet | `id` |
| **Vessel** | `list_vessels` | List/enumerate vessels | optional `query` |
| | `get_vessel` | Get vessel by ID | `id` |
| | `create_vessel` | Create vessel | `data` (with `RepoUrl`) |
| | `update_vessel` | Update vessel | `id`, `data` |
| | `update_vessel_context` | Update vessel project context and style guide | `id`, `data` |
| | `delete_vessel` | Delete vessel | `id` |
| **Voyage** | `list_voyages` | List/enumerate voyages | optional `query` |
| | `list_voyage_progress` | One page of voyages with mission counts (active by default) | optional `query`, `status` |
| | `get_voyage` | Get voyage by ID (with mission counts) | `id` |
| | `create_voyage` | Create voyage | `data` |
| | `cancel_voyage` | Cancel voyage | `id` |
| | `purge_voyage` | Permanently delete voyage and all missions | `id` |
| **Mission** | `list_missions` | List/enumerate full mission objects | optional `query` |
| | `list_missions_summary` | List/enumerate lightweight mission summaries | optional `query` |
| | `get_mission` | Get mission by ID | `id` |
| | `create_mission` | Create and dispatch mission | `data` |
| | `update_mission` | Update mission | `id`, `data` |
| | `transition_mission_status` | Transition mission status | `id`, `status` |
| | `cancel_mission` | Cancel mission | `id` |
| | `purge_mission` | Permanently delete mission | `id` |
| | `restart_mission` | Restart a `Failed`, `LandingFailed`, or `Cancelled` mission | `id`, optional `data.title`, `data.description` |
| **Captain** | `list_captains` | List/enumerate captains | optional `query` |
| | `get_captain` | Get captain by ID | `id` |
| | `create_captain` | Create captain | `data` |
| | `update_captain` | Update captain (preserves operational fields) | `id`, `data` |
| | `delete_captain` | Delete captain (refused while `Working` or with active missions) | `id` |
| **CLI Permission** | `list_cli_permission_requests` | One page of CLI permission requests, newest first | optional `status`, `captainId`, `query` (`pageNumber`, `pageSize`) |
| | `decide_cli_permission_request` | Allow once, allow and remember, or deny a pending request | `id`, `data` ([CliPermissionDecisionRequest](REST_API.md#clipermissiondecisionrequest)) |
| **Signal** | `list_signals` | List/enumerate signals | optional `query` |
| | `send_signal` | Create signal | `data` |
| **Event** | `list_events` | List/enumerate events | optional `query` |
| **Dock** | `list_docks` | List/enumerate docks | optional `query` |
| **Merge Queue** | `list_merge_queue` | List merge queue entries | optional `query` |
| | `get_merge_entry` | Get merge entry by ID | `id` |
| | `enqueue_merge` | Enqueue branch for merge | `data` |
| | `cancel_merge` | Cancel merge entry | `id` |
| | `process_merge_queue` | Process the merge queue | - |
| **Persona** | `get_persona` | Get a persona by name | `id` (persona name) |
| | `create_persona` | Create a persona; same validation as `POST /api/v1/personas` (`Name` and `PromptTemplateName` required, routing fields and default captain checked) | `data` (Persona object) |
| | `update_persona` | Update persona properties; same rules as `PUT /api/v1/personas/{name}` (`Description`, `PromptTemplateName`, `DefaultCaptainId`, `CaptainPriorities`, `PriorityAvailability`, `PriorityExhaustion`, `Scope`; an omitted `DefaultCaptainId` is kept and an explicit null clears it) | `id` (persona name), `data` (PersonaUpdateRequest) |
| | `delete_persona` | Delete a custom persona; a built-in persona answers `InvalidArgument` (REST `400`) | `id` (persona name) |
| **Pipeline** | `get_pipeline` | Get a pipeline by name | `id` (pipeline name) |
| | `create_pipeline` | Create a pipeline with stages | `data` (Pipeline object with Stages) |
| | `update_pipeline` | Update pipeline and stages | `id` (pipeline name), `data` (partial Pipeline) |
| | `delete_pipeline` | Delete a custom pipeline (blocked for built-in) | `id` (pipeline name) |
| **Prompt Template** | `get_prompt_template` | Get a prompt template by name | `id` (template name) |
| | `update_prompt_template` | Update template content | `id` (template name), `data` (partial PromptTemplate) |
| **Logs and diffs** | `get_mission_diff` | Unified diff of a mission's changes | `id` |
| | `get_mission_log` | Mission log lines | `id`, optional `lines`, `offset` |
| | `get_captain_log` | Captain log lines | `id`, optional `lines`, `offset` |
| **Enumerate** | `enumerate` | Paginated enumeration of any entity type | `entityType`, optional `query` |
| **Backup** | `backup` | Create a backup ZIP | optional `outputPath` |
| | `restore` | Restore from a backup ZIP | `filePath` |
| **Black Box** | `blackbox.get` | Get a black box entry (with its record by default) | `id` (`msn_`, `vyg_` or `bbx_`) or `data.entryId` / `data.missionId` / `data.voyageId`; optional `data.includeRecord` |
| | `blackbox.brief` | Get the Markdown FIX THIS brief of an entry | as `blackbox.get`; optional `data.audience`, `data.redactHosts` |
| | `blackbox.list` | One page of black box entries, newest first | optional `data` (BlackBoxEnumerateRequest filters), `query` |

Any other `action` is rejected with `command.error` code `UnknownAction` (`Unknown action: <action>`).

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
    "inProgressVoyages": 1,
    "openVoyages": 1,
    "missionsByStatus": { "Pending": 3, "InProgress": 2 },
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

Get a fleet by ID and the number of vessels it holds (`vesselCount`). The vessels are not embedded; page them with
`list_vessels` and `query.fleetId`.

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
    "fleet": { "id": "flt_abc123", "name": "my-fleet", "...": "..." },
    "vesselCount": 1
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
| `data` | object | Yes | The full fleet: the editable fields (`name`, `description`, `defaultPipelineId`, `active`) are replaced, so send every field you want to keep. `id`, `tenantId`, `userId`, and `createdUtc` are server-owned and always kept from the stored record (same rules as `PUT /api/v1/fleets/{id}`) |

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

Delete a fleet. Its vessels are kept.

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
    "status": "deleted"
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
    "RepoUrl": "https://github.com/org/repo.git"
  }
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `"create_vessel"` |
| `data` | object | Yes | Vessel creation data ([Vessel](#vessel) fields). `RepoUrl` is required (`command.error` `InvalidArgument` otherwise). Each configuration list (`ProtectedBranchPatterns`, `ProtectedPathPatterns`, `PrivateIdentifierDenylist`, `AutoLandPathAllowGlobs`, `AutoLandPathDenyGlobs`) holds at most 256 entries; more is `command.error` `InvalidArgument` (the same limit applies to `update_vessel`) |

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
| `data` | object | Yes | The full vessel: the record is replaced (send every field you want to keep). `id`, `tenantId`, and `userId` are always kept from the stored record, and the stored GitHub token override is preserved unless `GitHubTokenOverride` is sent (same rules as `PUT /api/v1/vessels/{id}`) |

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

Delete a vessel. Its `Pending`, `Assigned`, and `InProgress` missions are cancelled, its docks and their worktrees are
removed, and its bare repository is deleted when it lives under the Admiral's repos directory.

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

**Response:** `{ "type": "command.result", "action": "delete_vessel", "data": { "status": "deleted" } }`

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

#### list_voyage_progress

One page of voyages with mission counts ([VoyageProgress](#voyageprogress)), the same view as REST
`POST /api/v1/voyages/progress/enumerate`: with no status, the active voyages (InProgress first, then Open, newest first
within each); `status` (or `query.status`) selects one voyage status. Scoped to the connection's identity; needs an
authenticated connection.

**Request:**

```json
{
  "Route": "command",
  "action": "list_voyage_progress",
  "query": { "pageNumber": 1, "pageSize": 10 }
}
```

**Response:** `data` is an [EnumerationResult](#enumerationresult) of [VoyageProgress](#voyageprogress).

---

#### get_voyage

Get a voyage by ID. Returns the voyage with counts of its missions ([VoyageProgress](#voyageprogress)); the missions are
not embedded: page them with `list_missions` and `query.voyageId`.

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
    "totalMissions": 3,
    "completedMissions": 1,
    "failedMissions": 0,
    "inProgressMissions": 1,
    "missionCountsByStatus": { "Complete": 1, "InProgress": 1, "Pending": 1 },
    "vesselId": "vsl_abc123",
    "vesselCount": 1
  }
}
```

---

#### create_voyage

Create a new voyage. When `data` includes both a `VesselId` and a non-empty `Missions` array, the voyage is dispatched
(missions are created and assigned); otherwise an empty voyage is created. The reply's `data` is the voyage.

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
| `data.Title` | string | No | Voyage title |
| `data.Description` | string | No | Voyage description |
| `data.VesselId` | string | No | Target vessel for missions |
| `data.Missions` | array | No | Missions to create and dispatch: `Title`, `Description`, optional `Tier` and `RequestedCaptainId` |

---

#### cancel_voyage

Cancel a voyage. Its `Pending`, `Assigned`, and `Blocked` missions are also cancelled the same way `cancel_mission`
cancels one (a running agent is stopped first; an assigned captain with no other active mission returns to `Idle`);
`InProgress` missions keep running. A mission whose agent did not confirm it stopped is left unchanged and counted in
the reply's `notCancelledMissions`.

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

**Response:** `data` is `{ "voyage": { ... }, "cancelledMissions": 2, "notCancelledMissions": 0, "firstNotCancelledReason": null }`.

---

#### purge_voyage

Permanently delete a voyage and all of its missions (with their docks, worktrees, logs, and saved diffs). Refused with
`code` `Conflict` while the voyage is `Open` or `InProgress` (cancel it first) or while any of its missions is `Assigned`
or `InProgress`.

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

**Response:** `data` is `{ "status": "deleted", "voyageId": "vyg_abc123", "missionsDeleted": 3 }`.

---

### Mission Actions

#### list_missions

List or enumerate full mission objects with optional pagination and filtering.

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

#### list_missions_summary

List or enumerate lightweight mission summaries with optional pagination and filtering.

This action returns `MissionSummary` rows instead of full `Mission` objects. Use it for dashboards and status views that need IDs, status, routing fields, timestamps, and payload length hints without transferring `Description`, `DiffSnapshot`, or `AgentOutput`.

**Request:**

```json
{
  "Route": "command",
  "action": "list_missions_summary",
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
| `action` | string | Yes | `"list_missions_summary"` |
| `query` | object | No | [EnumerationQuery](#enumerationquery) for pagination/filtering |

**Summary object fields:**

| Field | Type | Description |
|---|---|---|
| `id` | string | Mission ID |
| `tenantId` | string \| null | Owning tenant |
| `userId` | string \| null | Owning user |
| `title` | string | Mission title |
| `status` | string | [MissionStatusEnum](#missionstatusenum) value |
| `vesselId` | string \| null | Linked vessel ID |
| `voyageId` | string \| null | Linked voyage ID |
| `captainId` | string \| null | Assigned captain ID |
| `branchName` | string \| null | Working branch name |
| `dockId` | string \| null | Linked dock ID |
| `processId` | int \| null | Local process ID when active |
| `prUrl` | string \| null | Pull request URL |
| `commitHash` | string \| null | Last recorded commit hash |
| `priority` | int | Mission priority |
| `parentMissionId` | string \| null | Parent mission ID |
| `persona` | string \| null | Assigned persona |
| `dependsOnMissionId` | string \| null | Dependency mission ID |
| `failureReason` | string \| null | Human-readable failure detail (not stable; do not parse) |
| `failureKind` | string \| null | Structured failure classification (see [Mission](#mission)) |
| `requiresReview` | bool | Whether the mission stops at a review gate |
| `reviewDenyAction` | string | `RetryStage` or `FailPipeline` |
| `reviewComment` | string \| null | Reviewer comment |
| `reviewedByUserId` | string \| null | Reviewer user ID |
| `reviewRequestedUtc` | string \| null | When review was requested |
| `reviewedUtc` | string \| null | When the review decision was made |
| `createdUtc` | string | Creation timestamp |
| `lastUpdateUtc` | string | Last update timestamp |
| `startedUtc` | string \| null | Start timestamp |
| `completedUtc` | string \| null | Completion timestamp |
| `totalRuntimeMs` | int \| null | Total captain runtime in milliseconds |
| `descriptionLength` | int | Saved description length |
| `diffSnapshotLength` | int | Saved diff length |
| `agentOutputLength` | int | Saved agent output length |

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

Create a new mission and dispatch it for assignment. When no captain can take it yet, the mission stays `Pending` and
the reply carries an extra top-level `warning` string ("Mission created but could not be assigned to any captain...");
it is retried on the next health check cycle.

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
| `data` | object | Yes | Mission metadata. Only `title`, `description`, `priority`, `vesselId`, `voyageId`, `branchName`, `prUrl`, and `parentMissionId` are applied (each is set to the value sent, so omitting one clears it); status, captain, dock, process, commit, diff, tenant, owner, and timestamps are kept (same rules as `PUT /api/v1/missions/{id}`). Use `transition_mission_status` to change status |

---

#### transition_mission_status

Transition a mission to a new status. The transition must be valid according to the [Mission Status Transitions](#mission-status-transitions) rules.

The same gates as `PUT /api/v1/missions/{id}/status` and the MCP `transition_mission_status` tool apply, each returning a `command.error` with code `Conflict`:

- A mission at a `Review` gate cannot be moved to any status other than `Cancelled`; use `approve_mission_review` or `deny_mission_review`.
- A manual `WorkProduced` or `Complete` is refused while the agent or its exit/stage-check handoff still owns the mission (`InProgress`, `Testing`, `Verifying`, or a process ID is set), and any transition other than `Cancelled` is refused while `Verifying`.
- For a write mission (any mode except `Audit` and `Research`), `WorkProduced` and `Complete` land the branch through the normal landing handler instead of writing the status. This needs an active dock; when landing is unavailable or the branch head cannot be resolved, the status is left unchanged. The response carries the mission as landing left it.

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

Cancel a mission. Cancelling stops the mission's agent first: the captain's process is stopped through the lifecycle that owns it (on its Harbor when it runs there) and the exit is confirmed, then the captain is released when it holds no other active mission, then the mission is set to `Cancelled`. The dock is kept for inspection. When the stop is not confirmed (for example the owning Harbor is disconnected), nothing changes and the cancel is refused, so no other work is assigned to a captain whose agent may still run. The reply's `data` is the cancelled mission; an unconfirmed stop
answers `command.error` with code `Conflict`.

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

Restart a `Failed`, `LandingFailed`, or `Cancelled` mission, resetting it to `Pending` for re-dispatch (captain, branch,
pull request URL, and timestamps are cleared). Optionally update the title and description before restarting.

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
    "...": "..."
  }
}
```

**Errors:** `command.error` with `code` `NotFound` when the mission does not exist, or `Conflict` when it is not
`Failed`, `LandingFailed`, or `Cancelled`.

---

#### get_mission_diff

Get the git diff for a mission. Returns the saved diff file if one exists, then the mission's `diffSnapshot`, and
otherwise a live diff of the mission's worktree against the vessel's default branch. When none is available the reply is
`command.error` with `code` `Unavailable`.

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

Get the session log for a mission with pagination support. A mission without a log file returns an empty `log` with
`lines` and `totalLines` of 0.

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

Update an existing captain. The configuration fields are replaced by `data` (send every field you want to keep);
`tenantId`, `userId`, the operational fields (`state`, `currentMissionId`, `currentDockId`, `processId`,
`recoveryAttempts`, `quarantineUntilUtc`, `quarantineReason`, `lastHeartbeatUtc`, `lastProcessAliveUtc`), and
`createdUtc` are always kept from the stored record, and runtime options are normalized for the runtime (same rules as
`PUT /api/v1/captains/{id}`).

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

Delete a captain. Refused with `code` `Conflict` while the captain is `Working` (stop it first) or while any of its
missions is `Assigned` or `InProgress`. On success `data` is `{ "status": "deleted" }`.

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

### CLI Permission Actions

Global administrators only, like every command. People who are not global admins decide CLI permission requests over
REST (`POST /api/v1/cli-permissions/requests/{id}/decide`) or MCP.

#### list_cli_permission_requests

List one page of CLI permission requests, newest first. The command never returns the whole list: paging comes from
`query.pageNumber` (default 1) and `query.pageSize` (1 to 1000, default 10).

**Request:**

```json
{
  "Route": "command",
  "action": "list_cli_permission_requests",
  "status": "Pending",
  "captainId": "cpt_abc123",
  "query": { "pageNumber": 1, "pageSize": 25 }
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `"list_cli_permission_requests"` |
| `status` | string | No | `Pending`, `Allowed`, `Denied`, `Expired`, or `Cancelled` (`InvalidArgument` otherwise) |
| `captainId` | string | No | Captain filter |
| `query` | object | No | `pageNumber` (default 1) and `pageSize` (1 to 1000, default 10) |

**Response:** `command.result` whose `data` is an enumeration result (`success`, `pageNumber`, `pageSize`, `totalPages`,
`totalRecords`, `objects`) of [CliPermissionRequest](REST_API.md#clipermissionrequest).

```json
{
  "type": "command.result",
  "action": "list_cli_permission_requests",
  "data": { "success": true, "pageNumber": 1, "pageSize": 25, "totalPages": 1, "totalRecords": 1, "objects": [ { "id": "cpr_abc123", "status": "Pending" } ] }
}
```

> **Breaking change** (see [DEPRECATION.md](../DEPRECATION.md), D-018): `data` used to be a bare array of up to 100
> requests; it is now one page with `totalRecords`.

---

#### decide_cli_permission_request

Decide a pending request; the waiting captain continues with the answer.

**Request:**

```json
{
  "Route": "command",
  "action": "decide_cli_permission_request",
  "id": "cpr_abc123",
  "data": { "decision": "AllowAndRemember", "rulePattern": "Bash(npm test:*)", "ruleScope": "Captain" }
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `"decide_cli_permission_request"` |
| `id` | string | Yes | Request ID (`cpr_`) |
| `data.decision` | string | Yes | `AllowOnce`, `AllowAndRemember`, `AllowForConversation` (the admin owner of the Ask thread; also sets the thread to `Bypass`), or `Deny` |
| `data.message` | string | No | Returned to the captain with a denial |
| `data.rulePattern` | string | No | Rule stored by `AllowAndRemember` (defaults to the suggested rule) |
| `data.ruleScope` | string | No | `Global`, `Vessel`, or `Captain` (default `Captain`) |

**Response:** `command.result` whose `data` is the decided request. A request that is no longer pending answers
`command.error` with `code` `Conflict`; a missing one `NotFound`; an invalid rule pattern `InvalidArgument`.

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
    "eventType": "mission.status_changed"
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

List merge queue entries with optional pagination. Only `pageNumber` and `pageSize` from `query` are applied; the
other filters are ignored for the merge queue.

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
| `id` | string | Yes | Merge entry ID (prefix `mrg_`) |

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
| `id` | string | Yes | Merge entry ID (prefix `mrg_`) |

**Response:** `data` is `{ "status": "cancelled" }`.

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
    "status": "processed"
  }
}
```

---

### Backup and Restore Actions

#### backup

Create a backup of the Armada database and settings as a ZIP archive.

**Request:**

```json
{
  "Route": "command",
  "action": "backup",
  "outputPath": "/home/alex/.armada/backups/my-backup.zip"
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `"backup"` |
| `outputPath` | string | No | Path on the Admiral host for the backup ZIP (a top-level field, not inside `data`). Defaults to `{DataDirectory}/backups/armada-backup-{yyyy-MM-dd-HHmmss}.zip` |

Backup and restore support the SQLite database provider only.

**Response:**

```json
{
  "type": "command.result",
  "action": "backup",
  "data": {
    "path": "/home/alex/.armada/backups/armada-backup-2026-03-11-120000.zip",
    "timestampUtc": "2026-03-11T12:00:00.0000000Z",
    "schemaVersion": 9,
    "sizeBytes": 245760,
    "recordCounts": {
      "Fleets": 2,
      "Vessels": 5,
      "Captains": 3,
      "Missions": 42,
      "Voyages": 8
    }
  }
}
```

---

#### restore

Restore Armada from a previously created backup ZIP file.

**Request:**

```json
{
  "Route": "command",
  "action": "restore",
  "filePath": "/home/alex/.armada/backups/armada-backup-2026-03-11-120000.zip"
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `"restore"` |
| `filePath` | string | Yes | Path on the Admiral host of the backup ZIP to restore from (a top-level field, not inside `data`). Missing: `command.error` `InvalidArgument` |

**Response:**

```json
{
  "type": "command.result",
  "action": "restore",
  "data": {
    "status": "restored",
    "backupPath": "/home/alex/.armada/backups/pre-restore-2026-03-11-120500.zip",
    "schemaVersion": 9,
    "message": "Database restored from armada-backup-2026-03-11-120000.zip. Restart the server to reload the restored data."
  }
}
```

> **Note:** A safety backup of the current state (`backupPath`, `pre-restore-{timestamp}.zip` under
> `{DataDirectory}/backups`) is created before overwriting. Restart the server after restoring.

---

### Black Box Actions

Read-only mirrors of the black box read routes and tools ([REST_API.md](REST_API.md#black-box),
[MCP_API.md](MCP_API.md#black-box)). Global administrators only, like every command; tenant admins and regular users
read black box entries over REST or MCP, which scope them. Replies are camelCase. Errors: `NotFound` for a missing
entry (or a mission or voyage without one), `InvalidArgument` for a bad audience, filter or body, `Conflict` while the
entry is being deleted, and `Unavailable` when the black box is not running or its store is unreachable.

#### blackbox.get

Get one entry. Name it with `id` (a `msn_` id means that mission's current entry, a `vyg_` id that voyage's current
entry, anything else an entry id) or with `data.entryId`, `data.missionId` or `data.voyageId`.

**Request:**

```json
{
  "Route": "command",
  "action": "blackbox.get",
  "id": "msn_mvu1fjp0_9Wd6Uuyp262",
  "data": { "includeRecord": false }
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `"blackbox.get"` |
| `id` | string | One of `id` or `data.*Id` | `msn_`, `vyg_` or `bbx_` id |
| `data.entryId`, `data.missionId`, `data.voyageId` | string | One of `id` or `data.*Id` | The entry, or a mission's or voyage's current entry (used instead of `id` when present) |
| `data.includeRecord` | boolean | No | Include the record (default `true`) |

**Response:** `command.result` whose `data` is the `BlackBoxEntry` (trimmed):

```json
{
  "type": "command.result",
  "action": "blackbox.get",
  "data": {
    "id": "bbx_mvu1ftcv_zcdofOBmjhY",
    "tenantId": "default",
    "userId": "default",
    "subjectType": "Mission",
    "missionId": "msn_mvu1fjp0_9Wd6Uuyp262",
    "voyageId": "vyg_mvu1fjoz_xO6urNkcaHp",
    "vesselId": "vsl_mvu1ffz5_4Vb0gY5fhKv",
    "missionTitle": "Retry landings deferred on a dirty checkout",
    "attempt": 1,
    "isCurrent": true,
    "phase": "Execution",
    "step": "ProcessCrash",
    "failureKind": "Crash",
    "statusAfter": "Failed",
    "triage": "Transient",
    "fingerprint": "9eab364095ddc8148f79ed0e7a81303bb19a5f115eba491962241d1a1403cead",
    "captureMode": "Backfill",
    "state": "Analyzed",
    "resolution": "Resolved",
    "pinned": false,
    "storageTarget": "Disk",
    "totalBytes": 9979,
    "objectCount": 5,
    "createdUtc": "2026-10-29T21:11:05.310433Z",
    "record": null
  }
}
```

```json
{
  "type": "command.error",
  "action": "blackbox.get",
  "error": "Black box entry bbx_doesnotexist_0000000000 not found.",
  "code": "NotFound"
}
```

#### blackbox.brief

Get the Markdown FIX THIS brief of one entry, rendered live. The entry is named as for `blackbox.get`.

**Request:**

```json
{
  "Route": "command",
  "action": "blackbox.brief",
  "id": "bbx_mvu1ftcv_zcdofOBmjhY",
  "data": { "audience": "user", "redactHosts": false }
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `"blackbox.brief"` |
| `id` or `data.entryId` / `data.missionId` / `data.voyageId` | string | Yes | The entry |
| `data.audience` | string | No | `agent` (default), `user` or `maintainer` |
| `data.redactHosts` | boolean | No | Strip absolute paths, host names and user names (default `false`) |

**Response:**

```json
{
  "type": "command.result",
  "action": "blackbox.brief",
  "data": {
    "entryId": "bbx_mvu1ftcv_zcdofOBmjhY",
    "audience": "User",
    "contentType": "text/markdown",
    "brief": "# What went wrong: Retry landings deferred on a dirty checkout (mission msn_mvu1fjp0_9Wd6Uuyp262)\n\nThis is what Armada recorded when the mission failed, and what you can do next.\n\n## Summary\n\n..."
  }
}
```

```json
{
  "type": "command.error",
  "action": "blackbox.brief",
  "error": "audience must be agent, user or maintainer.",
  "code": "InvalidArgument"
}
```

#### blackbox.list

One page of entries, newest first. The filters are a `BlackBoxEnumerateRequest` in `data` (`vesselId`, `voyageId`,
`missionId`, `captainId`, `fleetId`, `harborId`, `subjectType`, `phase`, `step`, `kind`, `triage`, `resolution`,
`state`, `captureMode`, `fingerprint`, `pinned`, `isCurrent`, `path`, `createdAfter`, `createdBefore`); paging comes
from `query` (`pageNumber`, `pageSize` 1 to 1000, `order`). When `query` is present it replaces the paging and order in
`data`.

**Request:**

```json
{
  "Route": "command",
  "action": "blackbox.list",
  "data": { "vesselId": "vsl_mvu1ffz5_4Vb0gY5fhKv", "isCurrent": true },
  "query": { "pageNumber": 1, "pageSize": 5 }
}
```

**Response:** `command.result` whose `data` is an enumeration result of `BlackBoxEntrySummary` (trimmed to one row):

```json
{
  "type": "command.result",
  "action": "blackbox.list",
  "data": {
    "success": true,
    "pageNumber": 1,
    "pageSize": 5,
    "totalPages": 1,
    "totalRecords": 3,
    "objects": [
      {
        "id": "bbx_mvu1ftew_95PyT7WmtrV",
        "subjectType": "Voyage",
        "missionId": null,
        "voyageId": "vyg_mvu1fjoz_xO6urNkcaHp",
        "vesselId": "vsl_mvu1ffz5_4Vb0gY5fhKv",
        "captainId": null,
        "missionTitle": "Harden landing retries",
        "vesselName": "demo-api",
        "attempt": 1,
        "isCurrent": true,
        "phase": "Voyage",
        "step": "SettlementFailed",
        "failureKind": "Crash",
        "statusBefore": null,
        "statusAfter": "Failed",
        "triage": "Transient",
        "fingerprint": "bcb7be821991076ba7d7551c9c5714b099bddaa13843c800b1bd4ee271e37329",
        "captureMode": "Backfill",
        "state": "Analyzed",
        "resolution": "Open",
        "pinned": false,
        "reasonExcerpt": "Mission msn_mvu1fjp0_9Wd6Uuyp262 failed: Captain process exited with code 134 after 4m12s while running dotnet test ...",
        "totalBytes": 9199,
        "objectCount": 5,
        "createdUtc": "2026-10-29T21:11:05.382585Z",
        "expiresUtc": "2027-04-27T21:11:05.382585Z"
      }
    ],
    "totalMs": 0
  }
}
```

The `enumerate` command does not take `blackbox`; use `blackbox.list`.

---


### Enumerate

#### enumerate

Generic paginated enumeration of any entity type with filtering and sorting. This is the WebSocket equivalent of the REST `POST /api/v1/{entity}/enumerate` endpoints and the MCP `enumerate` tool.

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
| `captains` | `status` ([CaptainStateEnum](#captainstateenum)), `createdAfter`, `createdBefore` |
| `missions` | `status`, `vesselId`, `captainId`, `voyageId`, `createdAfter`, `createdBefore` |
| `voyages` | `status` ([VoyageStatusEnum](#voyagestatusenum)), `createdAfter`, `createdBefore` |
| `docks` | `vesselId`, `createdAfter`, `createdBefore` |
| `signals` | `signalType`, `captainId`, `toCaptainId`, `unreadOnly`, `createdAfter`, `createdBefore` |
| `events` | `eventType`, `captainId`, `missionId`, `vesselId`, `voyageId`, `createdAfter`, `createdBefore` |
| `merge_queue` | Paging only (`pageNumber`, `pageSize`); filters are ignored |

Singular forms (e.g., `"fleet"`, `"mission"`) are also accepted, as are `merge-queue` and `mergequeue`. Entity type
names are case-insensitive.

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
  "error": "Unknown entity type: bananas. Valid types: fleets, vessels, captains, missions, voyages, docks, signals, events, merge_queue",
  "code": "InvalidArgument"
}
```

---

## Pagination

All `list_*` actions and the `enumerate` action support an optional `query` object (field names case-insensitive) for pagination and filtering via [EnumerationQuery](#enumerationquery).

### EnumerationQuery

| Field | Type | Default | Description |
|---|---|---|---|
| `pageNumber` | int | 1 | Page number (1-based) |
| `pageSize` | int | 10 | Results per page (min 1, max 1000; out-of-range values are clamped) |
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

`transition_mission_status` accepts only the transitions below (the same `MissionStateMachine` rules as
`PUT /api/v1/missions/{id}/status` and the MCP `transition_mission_status` tool); any other target, including any
transition out of `Complete`, `Failed`, or `Cancelled`, is rejected.

| From | Allowed To |
|---|---|
| `Pending` | `Assigned`, `Cancelled` |
| `Assigned` | `InProgress`, `Cancelled` |
| `InProgress` | `WorkProduced`, `Testing`, `Review`, `Complete`, `Failed`, `Cancelled` |
| `WorkProduced` | `PullRequestOpen`, `Complete`, `LandingFailed`, `Cancelled` |
| `PullRequestOpen` | `Complete`, `LandingFailed`, `Cancelled` |
| `Testing` | `Review`, `InProgress`, `Complete`, `Failed` |
| `Review` | `Complete`, `InProgress`, `Failed` |
| `LandingFailed` | `WorkProduced`, `Complete`, `Failed`, `Cancelled` |

An invalid transition returns `command.error` with `code` `Conflict`; an unknown status name returns `InvalidArgument`.

---

## Error Handling

### Command Errors

When a command fails, the server returns a `command.error` message. `code` is the machine-readable reason; branch on
it, never on the English `error` text (which is not stable).

```json
{
  "type": "command.error",
  "action": "get_fleet",
  "error": "Fleet not found",
  "code": "NotFound"
}
```

If a command throws (for example a `data` object that cannot be deserialized into the target model), `action` is null
and `code` comes from the exception type: `NotFound` for a missing key, `InvalidArgument` for an invalid argument or a
`data` object that cannot be deserialized, `Forbidden`, `Unavailable` for an unsupported operation (such as backup on a
non-SQLite database), `Conflict` for an invalid operation, and `InternalError` otherwise:

```json
{
  "type": "command.error",
  "action": null,
  "error": "Built-in backup and restore support SQLite only. Back up Postgresql with its own tools (pg_dump, mysqldump, or BACKUP DATABASE); see docs/UPGRADING.md.",
  "code": "Unavailable"
}
```

| Field | Type | Description |
|---|---|---|
| `type` | string | Always `"command.error"` |
| `action` | string \| null | The command action that failed, or null when it could not be read |
| `error` | string | Human-readable message (not stable; do not parse) |
| `code` | string | One of the codes below (added in 1.0; absent from older servers) |

| `code` | Meaning |
|---|---|
| `UnknownAction` | The `action` is not a command the handler accepts |
| `NotFound` | The entity the command names does not exist |
| `InvalidArgument` | A required field is missing or a value is invalid |
| `Conflict` | The entity's current state does not allow the command (for example an invalid status transition) |
| `Forbidden` | The caller may not run the command (WebSocket commands require a global administrator) |
| `Unavailable` | A service or data the command needs is not available on this server (for example no saved diff) |
| `InternalError` | The command failed unexpectedly |

New codes may be added in minor releases; treat an unrecognized code like `InternalError`. The .NET client reads this
reply with `Armada.Client.Socket.CommandErrorMessage.From(message)` (`ErrorCode` is the typed value).

### Unknown Actions

Sending an unrecognized `action` value returns:

```json
{
  "type": "command.error",
  "action": "bad_action",
  "error": "Unknown action: bad_action",
  "code": "UnknownAction"
}
```

### Unknown Routes

Sending a message to a route other than `subscribe` or `command` returns:

```json
{
  "type": "error",
  "message": "Unknown route: bad_route. Send a message with route 'subscribe' or 'command'"
}
```

### No Route Specified

If a message is sent without a route, or is not valid JSON:

```json
{
  "type": "error",
  "message": "Unknown route: null. Send a message with route 'subscribe' or 'command'"
}
```

---

## Data Types

### Models

The tables below list the commonly used fields. Entity payloads carry every public property of the model, camelCased
(the same models as the REST API; see [REST_API.md](REST_API.md#data-types)), and clients must ignore fields they do not
recognize.

#### ArmadaStatus

| Field | Type | Description |
|---|---|---|
| `totalCaptains` | int | Total registered captains |
| `idleCaptains` | int | Captains in Idle state |
| `workingCaptains` | int | Captains in Working state |
| `stalledCaptains` | int | Captains in Stalled state |
| `activeVoyages` | int | Number of active (Open or InProgress) voyages; page them with `list_voyage_progress` |
| `inProgressVoyages` | int | Number of InProgress voyages |
| `openVoyages` | int | Number of Open voyages |
| `memoryPressureDeferrals` | int | Cumulative dispatch attempts deferred because the host was under memory pressure |
| `missionsByStatus` | object | Map of status string to count (e.g., `{"Pending": 3, "InProgress": 2}`) |
| `recentSignals` | array | List of recent [Signal](#signal) objects |
| `remoteTunnel` | [RemoteTunnelStatus](#remotetunnelstatus) | Current outbound remote tunnel status |
| `timestampUtc` | string | ISO 8601 UTC timestamp of the snapshot |

#### RemoteTunnelStatus

| Field | Type | Description |
|---|---|---|
| `enabled` | bool | Whether the remote tunnel feature is enabled |
| `state` | string | Tunnel state (`Disabled`, `Disconnected`, `Connecting`, `Connected`, `Error`, `Stopping`) |
| `tunnelUrl` | string \| null | Configured or normalized websocket endpoint |
| `instanceId` | string \| null | Stable instance identifier advertised during handshake |
| `lastConnectAttemptUtc` | string \| null | Last connection attempt timestamp |
| `connectedUtc` | string \| null | Last successful connection timestamp |
| `lastHeartbeatUtc` | string \| null | Last heartbeat or inbound tunnel activity timestamp |
| `lastDisconnectUtc` | string \| null | Last disconnect timestamp |
| `lastError` | string \| null | Last recorded tunnel error (not stable; do not parse) |
| `lastErrorCode` | string \| null | Machine-readable code for `lastError`, or null when there is no error |
| `reconnectAttempts` | int | Consecutive reconnect attempts since the last successful connection |
| `latencyMs` | int \| null | Last successful ping/pong latency in milliseconds |
| `capabilityManifest` | object | Current handshake capability manifest |

#### VoyageProgress

| Field | Type | Description |
|---|---|---|
| `voyage` | object | [Voyage](#voyage) object |
| `totalMissions` | int | Total missions in this voyage |
| `completedMissions` | int | Missions with status Complete |
| `failedMissions` | int | Missions with status Failed |
| `inProgressMissions` | int | Missions currently in progress |
| `missionCountsByStatus` | object | Count per mission status (only statuses with missions) |
| `vesselId` | string | Vessel of the earliest mission, or null |
| `vesselCount` | int | Distinct vessels the missions target |

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
| `autoPush` | bool \| null | Legacy; ignored since 1.0.1 (use `landingMode`) |
| `autoCreatePullRequests` | bool \| null | Legacy; ignored since 1.0.1 (use `landingMode` `PullRequest`) |
| `autoMergePullRequests` | bool \| null | Override the global auto-merge setting for pull requests opened by the `PullRequest` landing mode |
| `landingMode` | string \| null | [LandingModeEnum](#landingmodeenum) -- per-voyage landing policy override |
| `sourcePlanningSessionId` | string \| null | Planning session that dispatched the voyage |
| `sourcePlanningMessageId` | string \| null | Planning message the voyage was dispatched from |

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
| `landingMode` | string \| null | [LandingModeEnum](#landingmodeenum) -- per-vessel landing policy override |
| `branchCleanupPolicy` | string \| null | [BranchCleanupPolicyEnum](#branchcleanuppolicyenum) -- per-vessel branch cleanup override |
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
| `persona` | string \| null | Persona for this mission (for example `Worker`, `Architect`, `Judge`) |
| `failureReason` | string \| null | Human-readable failure detail (not stable; do not parse) |
| `requiresReview` | bool | Whether the mission stops at a review gate |
| `failureKind` | string \| null | Structured failure classification (`MissionFailureKindEnum`: `Compile`, `TestFail`, `Timeout`, `LandingConflict`, `Crash`, `NoOp`, `Boundary`, `ScopeViolation`, `JudgeRejected`, `Infra`, `Unknown`, `ReviewDenied`, `DependencyFailed`, `MaxRuntimeExceeded`, `StallRecoveryExhausted`, `OperatorAction`, `InvalidOutput`, `Blocked`). Branch on this, not on `failureReason` |
| `createdUtc` | string | ISO 8601 creation timestamp |
| `startedUtc` | string \| null | ISO 8601 start timestamp |
| `completedUtc` | string \| null | ISO 8601 completion timestamp |
| `lastUpdateUtc` | string | ISO 8601 last update timestamp |

The REST API adds a computed `AssignmentBlocker` (why a `Pending` mission is still waiting for a captain) when one
mission or a voyage's missions are read; WebSocket commands and events do not compute it, so the field is absent here.

#### Captain

| Field | Type | Description |
|---|---|---|
| `id` | string | Captain ID (prefix `cpt_`) |
| `name` | string | Display name |
| `runtime` | string | [AgentRuntimeEnum](#agentruntimeenum) value |
| `model` | string \| null | Model override |
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
| `id` | string | Event ID (prefix `evt_`) |
| `eventType` | string | Event type string (for example `mission.status_changed`) |
| `entityType` | string \| null | Related entity type |
| `entityId` | string \| null | Related entity ID |
| `captainId` | string \| null | Related captain |
| `missionId` | string \| null | Related mission |
| `vesselId` | string \| null | Related vessel |
| `voyageId` | string \| null | Related voyage |
| `message` | string | Human-readable description (not stable; do not parse) |
| `payload` | string \| null | JSON payload string (for `mission.status_changed`: `{"Status": ..., "PreviousStatus": ...}`) |
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
| `id` | string | Merge entry ID (prefix `mrg_`) |
| `vesselId` | string | Target vessel ID |
| `missionId` | string \| null | Associated mission ID |
| `branchName` | string | Branch to merge |
| `targetBranch` | string | Branch to merge into (default `main`) |
| `status` | string | [MergeStatusEnum](#mergestatusenum) value |
| `priority` | int | Queue priority (default 0) |
| `testCommand` | string \| null | Test command run before landing |
| `testExitCode` | int \| null | Exit code of the test command |
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
| `LocalMerge` | Merge the branch into the default branch in the vessel's working directory; nothing is pushed |
| `MergeAndPush` | Merge the branch into the default branch in the vessel's working directory, then push it to the remote (the default) |
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
| `Failed` | Reached a terminal state with one or more failed missions |
| `Cancelled` | Voyage was cancelled |

#### CaptainStateEnum

| Value | Description |
|---|---|
| `Idle` | Available for assignment |
| `Working` | Actively working on a mission |
| `Planning` | Reserved for a planning session |
| `Refining` | Reserved for a backlog refinement session |
| `Stalled` | Process appears stalled (no heartbeat) |
| `Stopping` | In process of stopping |
| `Quarantined` | Excluded from dispatch until its quarantine expires (usage-limit or auth failure, or crash loop) |
| `Analyzing` | Reserved for a one-off analysis job (for example fleet categorization of imported repositories) |

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
| `Mux` | Mux CLI |
| `OpenCode` | OpenCode CLI |
| `Pi` | pi coding agent CLI |
| `ApiEndpoint` | In-process tool-calling loop driven by a configured model endpoint |
| `Custom` | Custom agent runtime |

#### MergeStatusEnum

| Value | Description |
|---|---|
| `Queued` | Waiting in the queue |
| `Testing` | Merged into the integration branch; tests running |
| `Passed` | Tests passed; ready to land |
| `Failed` | Tests or merge failed |
| `Landed` | Merged into the target branch |
| `Cancelled` | Removed from the queue (manually or due to a conflict) |

#### EnumerationOrderEnum

| Value | Description |
|---|---|
| `CreatedAscending` | Sort by creation time, oldest first |
| `CreatedDescending` | Sort by creation time, newest first |

---

## Client Examples

Every example authenticates with a token (see [Authentication](#authentication)); `ARMADA_TOKEN` stands for a bearer
credential token, a session token from `POST /api/v1/authenticate`, or the API key. The commands shown require a global
administrator; a tenant user's socket still receives its tenant's events.

### JavaScript

```javascript
const token = process.env.ARMADA_TOKEN; // in a browser, the session token from login
const ws = new WebSocket("ws://localhost:7890/ws?token=" + encodeURIComponent(token));

ws.onopen = () => {
  // Get a baseline snapshot, then send commands
  ws.send(JSON.stringify({ Route: "subscribe" }));
  sendCommands();
};

ws.onmessage = (event) => {
  const msg = JSON.parse(event.data);

  switch (msg.type) {
    case "status.snapshot":
      console.log("Initial status:", msg.data);
      break;
    case "mission.changed":
      console.log(`Mission ${msg.data.id}: ${msg.data.status}`);
      break;
    case "captain.changed":
      console.log(`Captain ${msg.data.id}: ${msg.data.state}`);
      break;
    case "command.result":
      console.log(`Command '${msg.action}' result:`, msg.data);
      break;
    case "command.error":
      console.error(`Command '${msg.action}' error:`, msg.error);
      break;
  }
};

function sendCommands() {
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
}
```

### C# / .NET

```csharp
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

string token = Environment.GetEnvironmentVariable("ARMADA_TOKEN") ?? "";
ClientWebSocket ws = new ClientWebSocket();
ws.Options.SetRequestHeader("Authorization", "Bearer " + token);
await ws.ConnectAsync(new Uri("ws://localhost:7890/ws"), CancellationToken.None);

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
            string missionId = doc.RootElement.GetProperty("data").GetProperty("id").GetString() ?? "";
            string missionStatus = doc.RootElement.GetProperty("data").GetProperty("status").GetString() ?? "";
            Console.WriteLine($"Mission {missionId}: {missionStatus}");
            break;
        case "captain.changed":
            string captainId = doc.RootElement.GetProperty("data").GetProperty("id").GetString() ?? "";
            string state = doc.RootElement.GetProperty("data").GetProperty("state").GetString() ?? "";
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
import os
import urllib.parse
import websockets

async def main():
    token = urllib.parse.quote(os.environ["ARMADA_TOKEN"], safe="")
    async with websockets.connect("ws://localhost:7890/ws?token=" + token) as ws:
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
                print(f"Mission {event['data']['id']}: {event['data']['status']}")
            elif event_type == "captain.changed":
                print(f"Captain {event['data']['id']}: {event['data']['state']}")
            elif event_type == "command.result":
                print(f"Command '{event['action']}' result: {event['data']}")
            elif event_type == "command.error":
                print(f"Command error: {event.get('error')}")

asyncio.run(main())
```
