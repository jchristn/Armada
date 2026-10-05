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
  - [mission.status_changed](#missionstatus_changed)
  - [voyage.changed](#voyagechanged)
  - [captain.changed](#captainchanged)
  - [objective.changed](#objectivechanged)
  - [objective-refinement-session.changed](#objective-refinement-sessionchanged)
  - [objective-refinement-session.message.created](#objective-refinement-sessionmessagecreated)
  - [objective-refinement-session.message.updated](#objective-refinement-sessionmessageupdated)
  - [objective-refinement-session.summary.created](#objective-refinement-sessionsummarycreated)
  - [objective-refinement-session.applied](#objective-refinement-sessionapplied)
  - [check-run.changed](#check-runchanged)
  - [deployment.changed](#deploymentchanged)
  - [deployment.progress](#deploymentprogress)
  - [environment.health](#environmenthealth)
  - [approval-needed](#approval-needed)
  - [Ask Armada thread events](#ask-armada-thread-events)
  - [Planning session events](#planning-session-events)
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
  - [Backup and Restore Actions](#backup-and-restore-actions)
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

Thread-scoped Ask Armada tokens (minted for a captain's MCP connection) are refused on `/ws` and on REST.

Each socket keeps the identity that authenticated it (tenant, user, admin flags) and receives only what that identity is
entitled to:

| Event family | Delivered to |
|---|---|
| Entity changes (`mission.changed`, `voyage.changed`, `captain.changed`, `check-run.changed`, `objective.changed`, `deployment.*`, `incident.changed`, `runbook-execution.changed`, `approval-needed`, planning and objective-refinement session events, generic events) | Sockets of the entity's tenant. Global admins may opt in to every tenant with `{ "Route": "subscribe", "AllTenants": true }`. Events whose tenant cannot be resolved go to global admins only. |
| Ask Armada events (`ask.*`) | Only the sockets of the thread owner (same tenant and user). The all-tenants opt-in does not apply. |

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
(UTC); command replies use `type` (`command.result` or `command.error`), `action`, `data`, and `error`:

```json
{
  "type": "mission.changed",
  "data": {
    "id": "msn_abc123",
    "status": "Complete",
    "title": "Implement feature X"
  },
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

A global administrator may add `"AllTenants": true` to receive entity events of every tenant (the snapshot echoes
`"allTenants": true`). The flag is ignored for everyone else. Ask Armada events stay owner-only either way.

**Server responds with:** a [`status.snapshot`](#statussnapshot) message.

After the initial snapshot, the client will receive all broadcast events ([`mission.changed`](#missionchanged), [`voyage.changed`](#voyagechanged), [`captain.changed`](#captainchanged), [`objective.changed`](#objectivechanged), [`objective-refinement-session.changed`](#objective-refinement-sessionchanged), [`objective-refinement-session.message.created`](#objective-refinement-sessionmessagecreated), [`objective-refinement-session.message.updated`](#objective-refinement-sessionmessageupdated), [`objective-refinement-session.summary.created`](#objective-refinement-sessionsummarycreated), [`objective-refinement-session.applied`](#objective-refinement-sessionapplied), [`check-run.changed`](#check-runchanged), [`deployment.changed`](#deploymentchanged), [`deployment.progress`](#deploymentprogress), [`environment.health`](#environmenthealth), [`approval-needed`](#approval-needed), [planning session events](#planning-session-events), and [generic events](#generic-events)) as they occur. [Ask Armada thread events](#ask-armada-thread-events) go only to the thread owner's sockets.

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

These events are delivered to the connected clients **entitled to them** (see [Authentication](#authentication): the entity's tenant, plus opted-in global admins) whenever state changes occur in the Armada system. Clients do not need to request these -- they are pushed automatically after subscribing.

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
  "data": {
    "id": "msn_abc123def456ghi789jk",
    "status": "InProgress",
    "title": "Add input validation to signup form"
  },
  "timestamp": "2026-03-07T12:35:00.000Z"
}
```

| Field | Type | Description |
|---|---|---|
| `type` | string | Always `"mission.changed"` |
| `data.id` | string | Mission ID (prefix `msn_`) |
| `data.status` | string | New [MissionStatusEnum](#missionstatusenum) value |
| `data.title` | string \| null | Mission title |
| `timestamp` | string | ISO 8601 UTC timestamp |

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
| `data.status` | string | Voyage status value |
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

Broadcast when Armada applies a refinement summary back to the linked backlog item.

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

### deployment.changed

Broadcast when a deployment record changes, including approval, execution, verification, and rollback state.

```json
{
  "type": "deployment.changed",
  "data": {
    "id": "dpl_abc123def456ghi789jk",
    "status": "Running",
    "environmentName": "Staging",
    "verificationStatus": "Pending"
  },
  "timestamp": "2026-03-07T12:35:00.000Z"
}
```

| Field | Type | Description |
|---|---|---|
| `type` | string | Always `"deployment.changed"` |
| `data` | object | Full serialized `Deployment` payload with enum values emitted as strings |
| `timestamp` | string | ISO 8601 UTC timestamp |

---

### deployment.progress

Broadcast when deployment progress or operator-facing execution messaging changes.

```json
{
  "type": "deployment.progress",
  "data": {
    "id": "dpl_abc123def456ghi789jk",
    "status": "Running",
    "message": "Running deploy command for Staging"
  },
  "timestamp": "2026-03-07T12:35:00.000Z"
}
```

| Field | Type | Description |
|---|---|---|
| `type` | string | Always `"deployment.progress"` |
| `data.id` | string | Deployment ID (prefix `dpl_`) |
| `data.status` | string | Current deployment status |
| `data.message` | string | Human-readable progress message |
| `timestamp` | string | ISO 8601 UTC timestamp |

---

### environment.health

Broadcast when rollout monitoring or verification updates health-related evidence for an environment deployment.

```json
{
  "type": "environment.health",
  "data": {
    "deploymentId": "dpl_abc123def456ghi789jk",
    "environmentId": "env_abc123def456ghi789jk",
    "environmentName": "Staging",
    "verificationStatus": "Passed",
    "message": "Health endpoint returned 200 OK"
  },
  "timestamp": "2026-03-07T12:35:00.000Z"
}
```

| Field | Type | Description |
|---|---|---|
| `type` | string | Always `"environment.health"` |
| `data.deploymentId` | string \| null | Deployment ID tied to the health update |
| `data.environmentId` | string \| null | Environment ID tied to the health update |
| `data.environmentName` | string \| null | Environment name |
| `data.verificationStatus` | string \| null | Current deployment verification status |
| `data.message` | string \| null | Human-readable health/verification message |
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
| `ask.tool` | `{ threadId, turnId, phase, id, name, arguments, ok, elapsedMs, result }` | A tool call started (`phase: "started"`) or completed (`"completed"`) |
| `ask.message` | `{ threadId, message }` | Any persisted message (user, reply, proposal card, action result, work update, summary, error) with `toolCalls`, `proposal`, and `trackedWork` embedded; also re-sent for a confirm card when its proposal is decided |
| `ask.proposal` | `{ threadId, proposal }` | A proposal was created or changed status (`expiresUtc` set while pending) |
| `ask.work` | `{ threadId, trackedWorkId, snapshot, trackedWork }` | The snapshot of tracked work changed (see `AskWorkSnapshot` in REST_API.md) |
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
| `objective-refinement-session.deleted` | `{ sessionId, objectiveId }` | A refinement session was deleted |

`planning-session.created` and `planning-session.stopped` (and the refinement-session `created` and `stopped`
events) are generic events (see below).

---

### Generic Events

Broadcast when the Admiral records an entity event that has no dedicated payload (deletions, purges, review
decisions, landing outcomes, captain launches, planning and refinement session lifecycle). Every generic event has the
same payload; the full list of generic event types is in [API_SURFACE_1.0.md](API_SURFACE_1.0.md#events). New types may
be added in minor releases.

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
| **Mission** | `list_missions` | List/enumerate full mission objects | optional `query` |
| | `list_missions_summary` | List/enumerate lightweight mission summaries | optional `query` |
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
| | `process_merge_queue` | Process the merge queue | - |
| **Persona** | `get_persona` | Get a persona by name | `id` (persona name) |
| | `create_persona` | Create a persona | `data` (Persona object) |
| | `update_persona` | Update persona properties | `id` (persona name), `data` (partial Persona) |
| | `delete_persona` | Delete a custom persona (blocked for built-in) | `id` (persona name) |
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
| **Status & Control** | `stop_server` | Stop the Admiral | - |

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
| `createdUtc` | string | Creation timestamp |
| `lastUpdateUtc` | string | Last update timestamp |
| `startedUtc` | string \| null | Start timestamp |
| `completedUtc` | string \| null | Completion timestamp |
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

### Backup and Restore Actions

#### backup

Create a backup of the Armada database and settings as a ZIP archive.

**Request:**

```json
{
  "Route": "command",
  "action": "backup",
  "data": {
    "OutputPath": "~/.armada/backups/my-backup.zip"
  }
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `"backup"` |
| `data.OutputPath` | string | No | File path for the backup ZIP. Defaults to `~/.armada/backups/armada-backup-{timestamp}.zip` |

**Response:**

```json
{
  "type": "command.result",
  "action": "backup",
  "data": {
    "Path": "~/.armada/backups/armada-backup-20260311T120000Z.zip",
    "Timestamp": "2026-03-11T12:00:00Z",
    "SchemaVersion": 9,
    "SizeBytes": 245760,
    "RecordCounts": {
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
  "data": {
    "FilePath": "~/.armada/backups/armada-backup-20260311T120000Z.zip"
  }
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `"restore"` |
| `data.FilePath` | string | Yes | Path to the backup ZIP file to restore from |

**Response:**

```json
{
  "type": "command.result",
  "action": "restore",
  "data": {
    "Status": "restored",
    "SafetyBackupPath": "~/.armada/backups/armada-safety-backup-20260311T120000Z.zip",
    "SchemaVersion": 9,
    "Message": "Database restored from armada-backup-20260311T120000Z.zip. Restart the server to reload the restored data."
  }
}
```

> **Note:** A safety backup is automatically created before overwriting. Restart the server after restoring.

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
| `InProgress` | `WorkProduced`, `Testing`, `Review`, `Complete`, `Failed`, `Cancelled` |
| `WorkProduced` | `PullRequestOpen`, `Complete`, `LandingFailed`, `Cancelled` |
| `PullRequestOpen` | `Complete`, `LandingFailed`, `Cancelled` |
| `Testing` | `Review`, `InProgress`, `Complete`, `Failed` |
| `Review` | `Complete`, `InProgress`, `Failed` |
| `LandingFailed` | `WorkProduced`, `Failed`, `Cancelled` |

Invalid transitions will return a `command.error` response.

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

If the command body cannot be parsed or an exception occurs, `action` is null and `code` comes from the exception type:

```json
{
  "type": "command.error",
  "action": null,
  "error": "Unexpected character encountered while parsing value",
  "code": "InvalidArgument"
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
| `InvalidArgument` | A required field is missing or a value is invalid (including an unparseable body) |
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
| `lastError` | string \| null | Last recorded tunnel error |
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
| `landingMode` | string \| null | [LandingModeEnum](#landingmodeenum) -- per-voyage landing policy override |

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
| `failureKind` | string \| null | Structured failure classification (`MissionFailureKindEnum`: `Compile`, `TestFail`, `Timeout`, `LandingConflict`, `Crash`, `NoOp`, `Boundary`, `ScopeViolation`, `JudgeRejected`, `Infra`, `Unknown`, `ReviewDenied`, `DependencyFailed`, `MaxRuntimeExceeded`, `StallRecoveryExhausted`, `OperatorAction`, `InvalidOutput`). Branch on this, not on `failureReason` |
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

#### AgentRuntimeEnum

| Value | Description |
|---|---|
| `ClaudeCode` | Anthropic Claude Code CLI |
| `Codex` | OpenAI Codex CLI |
| `Gemini` | Google Gemini CLI |
| `Cursor` | Cursor agent CLI |
| `Mux` | Mux CLI |
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
const ws = new WebSocket("ws://localhost:7890/ws");

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
    async with websockets.connect("ws://localhost:7890/ws") as ws:
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
