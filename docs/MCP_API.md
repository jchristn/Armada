# Armada MCP API Reference

**Version:** 0.9.0
**Default URL:** `http://localhost:7891/mcp`
**Protocol:** [Model Context Protocol](https://modelcontextprotocol.io/) (MCP), Streamable HTTP transport
**Server Library:** Voltaic (McpHttpServer)
**Server Name:** `Armada`

## Remote Control Note

`v0.9.0` does not proxy Armada MCP traffic through the new remote-control tunnel or `Armada.Proxy`.

Remote control in this release is limited to:

- Armada-side tunnel configuration and status surfaces
- proxy websocket tunnel termination
- proxy REST instance summary/detail endpoints

If/when MCP-over-tunnel is added, this document will gain explicit routed-tool semantics and capability rules.

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
    - [status](#status)
    - [inbox](#inbox)
    - [token_usage_summary](#token_usage_summary)
    - [stop_server](#stop_server)
  - **Enumeration**
    - [enumerate](#enumerate)
  - **Fleets**
    - [get_fleet](#get_fleet)
    - [create_fleet](#create_fleet)
    - [update_fleet](#update_fleet)
    - [delete_fleet](#delete_fleet)
    - [delete_fleets](#delete_fleets)
  - **Vessels**
    - [get_vessel](#get_vessel)
    - [add_vessel](#add_vessel)
    - [update_vessel](#update_vessel)
    - [update_vessel_context](#update_vessel_context)
    - [delete_vessel](#delete_vessel)
    - [delete_vessels](#delete_vessels)
  - **Voyages**
    - [dispatch](#dispatch)
    - [voyage_status](#voyage_status)
    - [cancel_voyage](#cancel_voyage)
    - [purge_voyage](#purge_voyage)
    - [delete_voyages](#delete_voyages)
  - **Missions**
    - [mission_status](#mission_status)
    - [evaluate_autoland](#evaluate_autoland)
    - [create_mission](#create_mission)
    - [update_mission](#update_mission)
    - [cancel_mission](#cancel_mission)
    - [restart_mission](#restart_mission)
    - [purge_mission](#purge_mission)
    - [delete_missions](#delete_missions)
    - [transition_mission_status](#transition_mission_status)
    - [get_mission_diff](#get_mission_diff)
    - [get_mission_log](#get_mission_log)
  - **Captains**
    - [get_captain](#get_captain)
    - [create_captain](#create_captain)
    - [update_captain](#update_captain)
    - [stop_captain](#stop_captain)
    - [release_captain](#release_captain)
    - [stop_all](#stop_all)
    - [delete_captain](#delete_captain)
    - [delete_captains](#delete_captains)
    - [get_captain_log](#get_captain_log)
  - **Signals**
    - [send_signal](#send_signal)
    - [delete_signals](#delete_signals)
  - **Events**
    - [delete_event](#delete_event)
    - [delete_events](#delete_events)
  - **Docks**
    - [get_dock](#get_dock)
    - [delete_dock](#delete_dock)
    - [purge_dock](#purge_dock)
    - [delete_docks](#delete_docks)
  - **Playbooks**
    - [get_playbook](#get_playbook)
    - [create_playbook](#create_playbook)
    - [update_playbook](#update_playbook)
    - [delete_playbook](#delete_playbook)
  - **Merge Queue**
    - [get_merge_entry](#get_merge_entry)
    - [enqueue_merge](#enqueue_merge)
    - [cancel_merge](#cancel_merge)
    - [process_merge_entry](#process_merge_entry)
    - [process_merge_queue](#process_merge_queue)
    - [delete_merge](#delete_merge)
    - [purge_merge_queue](#purge_merge_queue)
    - [purge_merge_entry](#purge_merge_entry)
    - [purge_merge_entries](#purge_merge_entries)
  - **Harbors**
    - [get_harbor](#get_harbor)
    - [create_harbor](#create_harbor)
    - [update_harbor](#update_harbor)
    - [delete_harbor](#delete_harbor)
    - [set_harbor_enabled](#set_harbor_enabled)
  - **Objectives**
    - [get_objective](#get_objective)
    - [create_objective](#create_objective)
    - [backlog and objective tools](#backlog-and-objective-tools)
  - **Delivery**
    - [get_check_run](#get_check_run)
    - [run_check](#run_check)
    - [retry_check_run](#retry_check_run)
    - [get_deployment](#get_deployment)
    - [create_deployment](#create_deployment)
    - [approve_deployment](#approve_deployment)
    - [verify_deployment](#verify_deployment)
    - [rollback_deployment](#rollback_deployment)
    - [get_release](#get_release)
    - [create_release](#create_release)
  - **Runbooks**
    - [get_runbook](#get_runbook)
    - [get_runbook_execution](#get_runbook_execution)
    - [start_runbook_execution](#start_runbook_execution)
  - **Prompt Template Management**
    - [get_prompt_template](#get_prompt_template)
    - [update_prompt_template](#update_prompt_template)
    - [reset_prompt_template](#reset_prompt_template)
  - **Persona Management**
    - [create_persona](#create_persona)
    - [get_persona](#get_persona)
    - [update_persona](#update_persona)
    - [delete_persona](#delete_persona)
  - **Pipeline Management**
    - [create_pipeline](#create_pipeline)
    - [get_pipeline](#get_pipeline)
    - [update_pipeline](#update_pipeline)
    - [delete_pipeline](#delete_pipeline)
  - **Model Endpoints**
    - [get_model_endpoint](#get_model_endpoint)
    - [create_model_endpoint](#create_model_endpoint)
    - [update_model_endpoint](#update_model_endpoint)
    - [delete_model_endpoint](#delete_model_endpoint)
    - [validate_model_endpoint](#validate_model_endpoint)
    - [health_check_model_endpoints](#health_check_model_endpoints)
  - **Backup and Restore**
    - [backup](#backup)
    - [restore](#restore)
- [Data Types](#data-types)
  - [Models](#models)
  - [Enumerations](#enumerations)
- [Client Configuration](#client-configuration)
  - [Claude Desktop](#claude-desktop)
  - [Claude Code](#claude-code)
  - [Generic MCP Client](#generic-mcp-client)

---

## Overview

Armada exposes a full MCP server that allows AI agents and MCP-compatible clients to interact with the Admiral orchestrator. MCP covers Armada's core orchestration and management surfaces directly from tool-calling clients:

- Query system status, stop the server
- Full CRUD on fleets, vessels, captains
- Dispatch voyages and standalone missions, cancel, purge
- Transition mission status with validation
- Read mission diffs and session logs (with pagination)
- Read captain session logs (with pagination)
- List and inspect docks (worktrees)
- Manage reusable markdown playbooks and attach them to dispatches
- Run and inspect structured delivery checks
- Manage backlog/objective records, reorder them, refine them with explicit captain selection, and hand them off into planning and dispatch
- Inspect, create, approve, verify, and roll back deployments
- Draft and inspect release records linked to voyages, missions, and checks
- Inspect and execute guided runbooks
- Send signals to captains
- Stop individual captains or all captains (emergency stop)
- Manage the merge queue (enqueue, cancel, process, inspect)
- Register and manage Harbors (host runners): inspect, create, update, enable/disable, and delete them
- Manage model endpoints (external embedding/inference providers), validate them with a real request, and sweep their health

MCP does **not** currently expose the newer dashboard/system helper REST surfaces such as:

- vessel Workspace file browsing and editing
- standalone planning-session transcript workflows outside the backlog handoff surface
- request-history capture and replay
- GitHub objective import, GitHub Actions sync, or GitHub PR evidence helper routes
- Mux runtime endpoint discovery helpers
- OpenAPI and Swagger discovery endpoints

The MCP server shares the same tool implementations as the stdio transport, registered via `McpToolRegistrar.RegisterAll()`.

---

## Connection

### HTTP Transport

The primary MCP transport is HTTP, served by `McpHttpServer` from the Voltaic library. The server listens on a dedicated port and exposes the modern MCP **Streamable HTTP** endpoint at `/mcp` (POST for JSON-RPC requests, GET for the SSE notification stream, DELETE to terminate the session). Point HTTP MCP clients at:

```
http://localhost:7891/mcp
```

To register Armada with Claude Code manually, add it as an HTTP MCP server:

```bash
claude mcp add --transport http --scope user armada http://localhost:7891/mcp
```

Drop `--scope user` to add it for the current project only. The MCP server is unauthenticated on localhost, so no token or header is required; if you changed `McpPort`, substitute your port. Armada's `armada mcp install` (or `scripts/*/install-mcp`) configures this automatically for Claude Code and the other supported runtimes.

**Enterprise-managed Claude Code.** If the add is rejected with `Cannot add MCP server 'armada': not allowed by enterprise policy`, your organization's Claude Code managed settings restrict which MCP servers may be added (via `allowedMcpServers` / `managed-mcp.json`). This is enforced by IT and **cannot** be overridden by a user, a project `.mcp.json`, or `--mcp-config`. Ask your Claude Code administrator to allow the Armada endpoint by adding it to `allowedMcpServers` in the managed settings — on Windows `C:\Program Files\ClaudeCode\managed-settings.json` (or, higher priority, the Claude.ai admin console at Admin Settings > Claude Code > Managed settings):

```json
{ "allowedMcpServers": [ { "serverUrl": "http://localhost:7891/mcp" } ] }
```

Run `/status` in Claude Code to see the active setting sources. If Claude Code stays locked down, the same standard HTTP endpoint works from any other MCP client that is not under that policy — `armada mcp install` also configures Codex, Gemini, and Cursor.

The legacy `/rpc` + `/events` (separate SSE) endpoints remain served for older clients but `/mcp` is preferred. MCP clients communicate using the standard MCP JSON-RPC protocol over HTTP. The server supports the full MCP tool-calling lifecycle:

1. **Initialize** — Client discovers server capabilities and available tools
2. **Call Tool** — Client invokes a tool with arguments
3. **Response** — Server returns the tool result

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

---

## Error Responses

When an MCP tool encounters an error, it returns a JSON object with `Error` and optionally `Message` fields:

```json
{
  "Error": "Vessel not found"
}
```

Common error responses across tools:

| Error | When |
|---|---|
| `"Vessel not found"` | Invalid or nonexistent vessel ID |
| `"Captain not found"` | Invalid or nonexistent captain ID |
| `"Mission not found"` | Invalid or nonexistent mission ID |
| `"Dock not found"` | Invalid or nonexistent dock ID |
| `"Merge entry not found"` | Invalid or nonexistent merge queue entry ID |
| `"ids is required and must not be empty"` | Bulk delete called with empty ID list |

MCP tools do not return HTTP status codes (MCP uses JSON-RPC, not HTTP). The presence of an `Error` field in the response indicates failure. On success, the response contains the requested data (entity object, status, list, etc.) without an `Error` field.

This is a deliberate architectural decision, not a missing feature. The stdio transport has no network attack surface -- the only caller is the parent process that spawned Armada. Adding authentication to stdio would add complexity without meaningful security benefit. The HTTP MCP transport inherits the same unauthenticated model for consistency, but should be bound to `localhost` or protected by a firewall in production.

---

## Tools

### status

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
  "remoteTunnel": {
    "enabled": false,
    "state": "Disabled",
    "tunnelUrl": null,
    "instanceId": "armada-1f2e3d4c5b6a",
    "lastError": null,
    "reconnectAttempts": 0,
    "latencyMs": null,
    "capabilityManifest": {
      "protocolVersion": "2026-04-03",
      "armadaVersion": "0.9.0",
      "features": [
        "remoteControl.handshake",
        "remoteControl.heartbeat",
        "status.health",
        "status.snapshot",
        "settings.remoteControl"
      ]
    }
  },
  "timestampUtc": "2026-03-07T12:34:56.789Z"
}
```

---

### inbox

Return the operator's inbox: everything across the fleet that requires a human's attention or action right now, ordered most-urgent first. Call this to answer a user asking *"Is there anything waiting on me?"*, *"Is there anything that needs my attention?"*, or *"Do I have any action items from Armada work?"*.

**What qualifies.** Two kinds of item appear:

- **Awaiting your decision (human-in-the-loop):** a mission in `Review` (approve or reject), or a deployment in `PendingApproval`.
- **Failed and needs intervention (human-out-of-the-loop):** a failed mission, a mission whose work could not be merged (landing failed), a failed merge, a failed or verification-failed deployment, or a stalled captain.

Purely informational events (completions, normal progress) are deliberately excluded -- the inbox answers *"what needs me?"*, not *"what happened?"* (use `enumerate` or the Activity log for history). An empty `items` list means nothing currently needs the operator.

**Item kinds and severity:**

| `kind` | Meaning | Severity |
|---|---|---|
| `review` | Mission awaiting your review/approval | `Warning`, or `Critical` if the review deadline has passed |
| `landing_failed` | Mission produced work that could not be merged | `Critical` |
| `failed` | Mission failed | `Warning` |
| `merge_failed` | Queued merge failed testing or landing | `Critical` |
| `deployment_approval` | Deployment waiting for your approval before it runs | `Warning` |
| `deployment_failed` | Deployment failed or failed verification | `Critical` |
| `stalled_captain` | Captain is stalled and may need recovery or a dock reclaim | `Warning` |

**Input Schema:**

```json
{
  "type": "object",
  "properties": {}
}
```

No parameters required.

**Response:** counts plus the ordered item list. Each item has `kind`, `severity` (`Critical`, `Warning`, or `Info`), `title`, `detail`, `entityType`, `entityId`, and a dashboard `href`.

```json
{
  "count": 2,
  "criticalCount": 1,
  "warningCount": 1,
  "items": [
    {
      "kind": "landing_failed",
      "severity": "Critical",
      "title": "Landing failed: Add JWT validation",
      "detail": "The work could not be landed.",
      "entityType": "mission",
      "entityId": "msn_1a2b3c",
      "href": "/missions/msn_1a2b3c"
    },
    {
      "kind": "review",
      "severity": "Warning",
      "title": "Review: Refactor auth service",
      "detail": "Awaiting your review.",
      "entityType": "mission",
      "entityId": "msn_4d5e6f",
      "href": "/missions/msn_4d5e6f"
    }
  ]
}
```

> Also exposed over REST as `GET /api/v1/inbox` and in the CLI as `armada inbox`.

---

### token_usage_summary

Summarize model token usage over a time window. Returns time buckets (each with a per-model breakdown), a whole-window per-model aggregate ordered most-used first, and grand totals. Use it to answer *"how many tokens has each model used?"* or *"what's our token usage over the last week?"*.

Counts are normalized across providers: `input` covers prompt tokens, `output` covers completion tokens, `cached` is the cache-read subset of input (informational), and `total` is input + output. Counts are measured where the runtime reports usage (for example Claude Code) and estimated from text length otherwise; `estimatedCount` reports how many aggregated records were estimated.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "sinceHours": { "type": "integer", "description": "Only include usage newer than this many hours (default 24; ignored when fromUtc is set)" },
    "fromUtc": { "type": "string", "description": "Explicit UTC window start (ISO-8601); overrides sinceHours" },
    "toUtc": { "type": "string", "description": "Explicit UTC window end (ISO-8601; default now)" },
    "bucketMinutes": { "type": "number", "description": "Time-bucket width in minutes; fractional allowed, e.g. 0.5 for 30-second buckets (default 60)" },
    "model": { "type": "string", "description": "Filter to one model" },
    "runtime": { "type": "string", "description": "Filter to one runtime (for example claudecode, codex, mux)" },
    "source": { "type": "string", "description": "Filter to one source: mission, chat, or planning" },
    "vesselId": { "type": "string", "description": "Filter to one vessel (vsl_ prefix)" },
    "captainId": { "type": "string", "description": "Filter to one captain (cpt_ prefix)" }
  }
}
```

**Response:** grand totals plus `buckets` (time series) and `byModel` (most-used first).

```json
{
  "fromUtc": "2026-05-01T00:00:00Z",
  "toUtc": "2026-05-08T00:00:00Z",
  "bucketMinutes": 60,
  "recordCount": 42,
  "estimatedCount": 18,
  "inputTokens": 1200000,
  "outputTokens": 340000,
  "cachedTokens": 90000,
  "totalTokens": 1540000,
  "byModel": [
    { "model": "claude-sonnet-4", "inputTokens": 900000, "outputTokens": 250000, "cachedTokens": 80000, "totalTokens": 1150000 },
    { "model": "gpt-5", "inputTokens": 300000, "outputTokens": 90000, "cachedTokens": 10000, "totalTokens": 390000 }
  ],
  "buckets": [
    {
      "bucketStartUtc": "2026-05-01T00:00:00Z",
      "bucketEndUtc": "2026-05-01T01:00:00Z",
      "inputTokens": 20000, "outputTokens": 5000, "cachedTokens": 1000, "totalTokens": 25000,
      "models": [
        { "model": "claude-sonnet-4", "inputTokens": 20000, "outputTokens": 5000, "cachedTokens": 1000, "totalTokens": 25000 }
      ]
    }
  ]
}
```

> Also exposed over REST as `GET /api/v1/token-usage/summary`.

---

### stop_server

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

### enumerate

Paginated enumeration of any entity type with filtering and sorting. This is the MCP equivalent of the `POST /api/v1/{entity}/enumerate` REST endpoints. Returns paginated results with total counts, page metadata, and query timing. Supports: fleets, vessels, captains, missions, voyages, docks, signals, events, merge_queue, harbors, playbooks, personas, prompt_templates, pipelines, workflow_profiles, check_runs, releases, model_endpoints.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "entityType": { "type": "string", "description": "Entity type to enumerate (fleets, vessels, captains, missions, voyages, docks, signals, events, merge_queue, harbors, playbooks, personas, prompt_templates, pipelines, workflow_profiles, check_runs, releases, model_endpoints)" },
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
| `harbors` | `createdAfter`, `createdBefore` (current MCP enumeration is primarily paginated browse) |
| `personas` | `createdAfter`, `createdBefore` |
| `playbooks` | `createdAfter`, `createdBefore` |
| `prompt_templates` | `createdAfter`, `createdBefore` |
| `pipelines` | `createdAfter`, `createdBefore` |
| `workflow_profiles` | `createdAfter`, `createdBefore` (current MCP enumeration is primarily paginated browse) |
| `check_runs` | `createdAfter`, `createdBefore` (current MCP enumeration is primarily paginated browse) |
| `releases` | `status`, `vesselId`, `search`, `createdAfter`, `createdBefore` |
| `model_endpoints` | `createdAfter`, `createdBefore` (current MCP enumeration is primarily paginated browse) |

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

> **Note:** When enumerating `missions`, the `DiffSnapshot` field is excluded from results to keep payloads compact. Use `get_mission_diff` to retrieve the full diff for a specific mission.

---

### dispatch

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
    },
    "pipelineId": {
      "type": "string",
      "description": "Optional pipeline ID to use for this voyage (overrides vessel/fleet default)"
    },
    "pipeline": {
      "type": "string",
      "description": "Optional pipeline name to use for this voyage (convenience alias for pipelineId -- resolves by name)"
    },
    "selectedPlaybooks": {
      "type": "array",
      "description": "Optional ordered playbook selections to apply to every created mission",
      "items": {
        "type": "object",
        "properties": {
          "playbookId": { "type": "string" },
          "deliveryMode": { "type": "string" }
        },
        "required": ["playbookId", "deliveryMode"]
      }
    }
  },
  "required": ["title"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `title` | string | Yes | Voyage title |
| `description` | string | No | Voyage description |
| `vesselId` | string | No | Target vessel ID (prefix `vsl_`). Omit (with no `missions`) to create a bare voyage; missions are added later. |
| `missions` | array | No | Array of mission objects with `title` and optional `description`. Omit for a bare voyage. |
| `pipelineId` | string | No | Pipeline ID to use for this voyage (overrides vessel/fleet default) |
| `pipeline` | string | No | Pipeline name to use (convenience alias for `pipelineId` -- resolves by name; a bad name is rejected) |
| `objectiveId` | string | No | Objective (prefix `obj_`) to link this voyage to; must exist |
| `selectedPlaybooks` | array | No | Ordered playbook selections with `playbookId` and `deliveryMode` |
| `captainAssignments` | array | No | Per-persona captain overrides (preferred captain + fallback tier) |

> **Parity with REST.** This tool and `POST /api/v1/voyages` funnel through the same validation: a linked
> objective must exist, a pipeline name must resolve, and a request with no vessel or no missions is created
> as a **bare voyage** rather than dispatched. Both surfaces accept and reject the same inputs; only the error
> shape differs (structured `{ "Error": ..., "Code": ... }` here, HTTP status codes over REST).

**Example Input:**

```json
{
  "title": "Implement authentication",
  "description": "Add JWT auth to the API",
  "vesselId": "vsl_abc123def456ghi789jk",
  "selectedPlaybooks": [
    {
      "playbookId": "pbk_abc123",
      "deliveryMode": "InlineFullContent"
    }
  ],
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

### voyage_status

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

### mission_status

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

> **Note:** The `DiffSnapshot` field is excluded from status responses to keep payloads compact. Use `get_mission_diff` to retrieve the full diff.

---

### evaluate_autoland

Dry-run the vessel's auto-land predicate against a mission's captured diff without landing it. Returns whether the change would auto-land unattended and, if not, the hold reason.

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

**Response:** `{ "Land": true }` or `{ "Land": false, "HoldReason": "..." }`, or `{ "Error": "Mission not found" }` / `{ "Error": "Mission does not have an associated vessel" }`.

---

### get_fleet

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

### add_vessel

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
    "gitHubTokenOverride": {
      "type": "string",
      "description": "Optional per-vessel GitHub token override. Leave unset to use the global configured token."
    },
    "enableModelContext": {
      "type": "boolean",
      "description": "Enable model context accumulation -- agents will update context with key information discovered during missions (default false)"
    },
    "defaultPipelineId": {
      "type": "string",
      "description": "Default pipeline ID for voyages dispatched to this vessel"
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
| `gitHubTokenOverride` | string | No | Optional per-vessel GitHub token override. The raw token is accepted on create but never returned by MCP reads. |
| `enableModelContext` | boolean | No | Enable model context accumulation (default false) |
| `defaultPipelineId` | string | No | Default pipeline ID for voyages dispatched to this vessel |

**Example Input:**

```json
{
  "name": "payment-service",
  "repoUrl": "git@github.com:org/payment-service.git",
  "fleetId": "flt_abc123def456ghi789jk",
  "defaultBranch": "main",
  "gitHubTokenOverride": "ghp_example"
}
```

**Response:** The newly created [Vessel](#vessel) object.

---

### delete_event

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

### delete_events

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

### send_signal

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

### delete_signals

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

### stop_captain

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

### release_captain

Lift a captain's quarantine, returning it to the Idle pool so tier selection can hand it work again. Captains are auto-quarantined on provider usage-limit / auth failures (until the parsed reset time or a configured backoff) and on crash loops; this manually clears that state.

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

**Response:** The updated [Captain](#captain) object, or `{ "Status": "not_quarantined", "CaptainId": "..." }` when the captain was not quarantined, or `{ "Error": "Captain not found" }`.

---

### stop_all

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

### cancel_mission

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

### restart_mission

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

### cancel_voyage

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

### purge_voyage

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

### delete_voyages

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

### create_fleet

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

### update_fleet

Update an existing fleet's name or description.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "fleetId": { "type": "string", "description": "Fleet ID (flt_ prefix)" },
    "name": { "type": "string", "description": "New fleet name" },
    "description": { "type": "string", "description": "New fleet description" },
    "defaultPipelineId": { "type": "string", "description": "Default pipeline ID for voyages dispatched to vessels in this fleet" }
  },
  "required": ["fleetId"]
}
```

**Response:** Updated [Fleet](#fleet) object, or `{ "Error": "Fleet not found" }`.

---

### delete_fleet

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

### delete_fleets

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

### get_vessel

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

### update_vessel

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
    "gitHubTokenOverride": { "type": "string", "description": "Optional per-vessel GitHub token override. Empty string clears the existing override." },
    "enableModelContext": { "type": "boolean", "description": "Enable or disable model context accumulation" },
    "modelContext": { "type": "string", "description": "Agent-accumulated context about this repository" },
    "defaultPipelineId": { "type": "string", "description": "Default pipeline ID for voyages dispatched to this vessel" },
    "definitionOfDoneEnabled": { "type": "boolean", "description": "Run the in-dock build + unit tests before acceptance; a failure blocks landing with a classified reason (Compile/TestFail/Timeout/Infra)" },
    "definitionOfDoneBuildCommand": { "type": "string", "description": "Shell command that builds the project inside the mission checkout (e.g. dotnet build)" },
    "definitionOfDoneTestCommand": { "type": "string", "description": "Shell command that runs unit tests inside the mission checkout (e.g. dotnet test)" },
    "definitionOfDoneTimeoutSeconds": { "type": "integer", "description": "Per-phase timeout in seconds, clamped to [30, 7200] (default 1800)" }
  },
  "required": ["vesselId"]
}
```

**Response:** Updated [Vessel](#vessel) object, or `{ "Error": "Vessel not found" }`.

When `gitHubTokenOverride` is omitted, MCP preserves the current stored override. Send `""` to clear the override and fall back to the global `GitHubToken` from Armada configuration.

---

### update_vessel_context

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

### delete_vessel

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

### delete_vessels

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

### create_mission

Create and dispatch a standalone mission to a vessel. The Admiral assigns a captain and sets up a worktree.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "title": { "type": "string", "description": "Mission title" },
    "description": { "type": "string", "description": "Mission description/instructions" },
    "vesselId": { "type": "string", "description": "Target vessel ID (vsl_ prefix)" },
    "voyageId": { "type": "string", "description": "Optional voyage ID to associate with (vyg_ prefix)" },
    "persona": { "type": "string", "description": "Persona for this mission (e.g. Worker, Architect, Judge, Test Engineer)" },
    "mode": { "type": "string", "description": "Execution mode: Implementation (default), Audit, or Research. Audit and Research are read-only modes that produce a written report instead of a commit; their empty diff is treated as success, not a no-op failure." },
    "selectedPlaybooks": {
      "type": "array",
      "description": "Optional ordered playbook selections for this mission",
      "items": {
        "type": "object",
        "properties": {
          "playbookId": { "type": "string" },
          "deliveryMode": { "type": "string" }
        },
        "required": ["playbookId", "deliveryMode"]
      }
    }
  },
  "required": ["title", "description", "vesselId"]
}
```

**Response:** [Mission](#mission) object.

---

### update_mission

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
    "parentMissionId": { "type": "string", "description": "Parent mission ID for sub-tasks (msn_ prefix)" },
    "persona": { "type": "string", "description": "Persona for this mission (e.g. Worker, Architect, Judge, Test Engineer)" },
    "mode": { "type": "string", "description": "Execution mode: Implementation, Audit, or Research (read-only report modes)" }
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
| `persona` | No | Persona for this mission |
| `mode` | No | Execution mode: Implementation, Audit, or Research |

**Response:** Updated [Mission](#mission) object, or `{ "Error": "Mission not found" }`.

---

### purge_mission

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

### delete_missions

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

### transition_mission_status

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

### get_mission_diff

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

### get_mission_log

Get the session log for a mission. Supports pagination.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "missionId": { "type": "string", "description": "Mission ID (msn_ prefix)" },
    "lines": { "type": "integer", "description": "Number of lines to return (default 100)" },
    "offset": { "type": "integer", "description": "Line offset to start from (default 0)" },
    "formatted": { "type": "boolean", "description": "Apply the readable runtime-log formatter (resolve tool names per runtime, redact secret-shaped values, drop noise) instead of returning raw lines (default false)" }
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

### create_captain

Register a new captain (AI agent).

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "name": { "type": "string", "description": "Captain display name" },
    "runtime": { "type": "string", "description": "Agent runtime: ClaudeCode, Codex, Gemini, Cursor, Mux, OpenCode, or Custom" },
    "model": { "type": "string", "description": "Optional model override for this captain. When omitted, the runtime chooses automatically" },
    "systemInstructions": { "type": "string", "description": "System instructions for this captain -- injected into every mission prompt to specialize behavior" },
    "allowedPersonas": { "type": "string", "description": "JSON array of persona names this captain is allowed to use" },
    "preferredPersona": { "type": "string", "description": "Preferred persona name for this captain" }
  },
  "required": ["name"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `name` | string | Yes | Captain display name |
| `runtime` | string | No | Agent runtime: `ClaudeCode`, `Codex`, `Gemini`, `Cursor`, `Mux`, `OpenCode`, or `Custom` |
| `model` | string | No | Optional model override. When omitted, the runtime chooses automatically |
| `systemInstructions` | string | No | System instructions injected into every mission prompt for this captain |
| `allowedPersonas` | string | No | JSON array of persona names this captain is allowed to use, for example `["Worker","Judge"]` |
| `preferredPersona` | string | No | Preferred persona name for this captain |

**Response:** [Captain](#captain) object. Invalid or unavailable models are returned as MCP tool errors.

---

### get_captain

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

### get_captain_tools

Describe the Armada MCP tools available to a specific captain.

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

**Response:** [CaptainToolAccessResult](#captaintoolaccessresult) object, or `{ "Error": "Captain not found" }`.

---

### update_captain

Update a captain's name or runtime. Operational fields (state, process, mission) are preserved.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "captainId": { "type": "string", "description": "Captain ID (cpt_ prefix)" },
    "name": { "type": "string", "description": "New display name" },
    "runtime": { "type": "string", "description": "New agent runtime: ClaudeCode, Codex, Gemini, Cursor, Mux, OpenCode, or Custom" },
    "model": { "type": "string", "description": "New optional model override for this captain" },
    "systemInstructions": { "type": "string", "description": "New system instructions for this captain" },
    "allowedPersonas": { "type": "string", "description": "New JSON array of persona names this captain is allowed to use" },
    "preferredPersona": { "type": "string", "description": "New preferred persona name for this captain" }
  },
  "required": ["captainId"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `captainId` | string | Yes | Captain ID (prefix `cpt_`) |
| `name` | string | No | New display name |
| `runtime` | string | No | New agent runtime: `ClaudeCode`, `Codex`, `Gemini`, `Cursor`, `Mux`, `OpenCode`, or `Custom` |
| `model` | string | No | New optional model override. When omitted, the existing value is preserved |
| `systemInstructions` | string | No | New system instructions for this captain |
| `allowedPersonas` | string | No | New JSON array of persona names this captain is allowed to use, for example `["Worker","Judge"]` |
| `preferredPersona` | string | No | New preferred persona name for this captain |

**Response:** Updated [Captain](#captain) object, or `{ "Error": "Captain not found" }`. Invalid or unavailable models are returned as MCP tool errors.

---

### delete_captain

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

### delete_captains

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

### get_captain_log

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

### get_dock

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

### delete_dock

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

### purge_dock

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

### delete_docks

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

### get_playbook

Get a playbook by ID.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "id": { "type": "string", "description": "Playbook ID (pbk_ prefix)" }
  },
  "required": ["id"]
}
```

**Response:** [Playbook](#playbook) object, or `{ "Error": "Playbook not found: pbk_..." }`.

---

### create_playbook

Create a new markdown playbook in the default tenant context used by MCP.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "fileName": { "type": "string", "description": "Markdown filename (must end with .md)" },
    "description": { "type": "string", "description": "Optional human-readable description" },
    "content": { "type": "string", "description": "Markdown content" },
    "active": { "type": "boolean", "description": "Whether the playbook is active" }
  },
  "required": ["fileName", "content"]
}
```

**Response:** [Playbook](#playbook) object, or an error such as `{ "Error": "A playbook with that file name already exists." }`.

---

### update_playbook

Update an existing playbook by ID.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "id": { "type": "string", "description": "Playbook ID (pbk_ prefix)" },
    "fileName": { "type": "string", "description": "Markdown filename (must end with .md)" },
    "description": { "type": "string", "description": "Optional human-readable description" },
    "content": { "type": "string", "description": "Markdown content" },
    "active": { "type": "boolean", "description": "Whether the playbook is active" }
  },
  "required": ["id"]
}
```

**Response:** Updated [Playbook](#playbook) object, or `{ "Error": "Playbook not found: pbk_..." }`.

---

### delete_playbook

Delete a playbook by ID. Existing mission snapshots remain immutable.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "id": { "type": "string", "description": "Playbook ID (pbk_ prefix)" }
  },
  "required": ["id"]
}
```

**Response:**

```json
{ "Status": "deleted", "PlaybookId": "pbk_..." }
```

---

### get_merge_entry

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

### enqueue_merge

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

### cancel_merge

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

### process_merge_entry

Process a single queued merge entry by ID. Armada creates the integration branch, runs the configured test flow, and lands the branch if the entry passes.

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

**Response:** [MergeEntry](#mergeentry) object, or `{ "Error": "Merge entry not found or not in Queued status" }`.

---

### process_merge_queue

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

### delete_merge

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

### purge_merge_queue

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

### purge_merge_entry

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

### purge_merge_entries

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

### get_harbor

Inspect one registered Harbor (host runner) by ID, including its advertised capabilities and connection status.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "harborId": { "type": "string", "description": "Harbor ID (hbr_ prefix)" }
  },
  "required": ["harborId"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `harborId` | string | Yes | Harbor ID (prefix `hbr_`) |

**Response:** [Harbor](#harbor) object, or `{ "Error": "Harbor not found" }`.

---

### create_harbor

Pre-register a Harbor. A Harbor also self-registers on first handshake; use this to reserve a name and capacity before it connects. Only `name`, `maxConcurrentJobs`, and `enabled` are honored; all other fields are managed by the link.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "name": { "type": "string", "description": "Human-facing Harbor name" },
    "maxConcurrentJobs": { "type": "integer", "description": "Maximum concurrent jobs (default 4)" },
    "enabled": { "type": "boolean", "description": "Whether the Harbor is enabled for routing (default true)" }
  },
  "required": ["name"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `name` | string | Yes | Human-facing Harbor name |
| `maxConcurrentJobs` | int | No | Maximum concurrent jobs (default 4, clamped to a minimum of 1) |
| `enabled` | bool | No | Whether the Harbor is enabled for routing (default true) |

**Response:** The newly created [Harbor](#harbor) object, or `{ "Error": "..." }` on invalid input.

---

### update_harbor

Update a Harbor's operator-editable fields (`name`, `maxConcurrentJobs`, `enabled`). Runtime state reported by the link is preserved server-side.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "harborId": { "type": "string", "description": "Harbor ID (hbr_ prefix)" },
    "name": { "type": "string", "description": "Human-facing Harbor name" },
    "maxConcurrentJobs": { "type": "integer", "description": "Maximum concurrent jobs" },
    "enabled": { "type": "boolean", "description": "Whether the Harbor is enabled for routing" }
  },
  "required": ["harborId"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `harborId` | string | Yes | Harbor ID (prefix `hbr_`) |
| `name` | string | No | New Harbor name. Omit to keep the current value. |
| `maxConcurrentJobs` | int | No | New concurrency cap. Omit to keep the current value. |
| `enabled` | bool | No | New enabled flag. Omit to keep the current value. |

**Response:** The updated [Harbor](#harbor) object, or `{ "Error": "Harbor not found" }`.

---

### delete_harbor

Delete a Harbor registration by ID.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "harborId": { "type": "string", "description": "Harbor ID (hbr_ prefix)" }
  },
  "required": ["harborId"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `harborId` | string | Yes | Harbor ID (prefix `hbr_`) |

**Response:**

```json
{ "Deleted": true, "HarborId": "hbr_abc123" }
```

Returns `{ "Error": "..." }` if the Harbor does not exist.

---

### set_harbor_enabled

Enable or disable a Harbor for routing. A disabled Harbor keeps its docks but receives no new missions.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "harborId": { "type": "string", "description": "Harbor ID (hbr_ prefix)" },
    "enabled": { "type": "boolean", "description": "Whether the Harbor is enabled for routing" }
  },
  "required": ["harborId", "enabled"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `harborId` | string | Yes | Harbor ID (prefix `hbr_`) |
| `enabled` | bool | Yes | `true` to enable, `false` to disable |

**Response:** The updated [Harbor](#harbor) object, or `{ "Error": "..." }`.

---

### get_objective

Inspect one scoped objective or intake-style record, including linked vessels, planning sessions, refinement sessions, voyages, checks, releases, deployments, incidents, and acceptance criteria.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "objectiveId": { "type": "string", "description": "Objective ID (obj_ prefix)" }
  },
  "required": ["objectiveId"]
}
```

**Response:** serialized `Objective` object with the expanded backlog fields (`kind`, `category`, `priority`, `rank`, `backlogState`, `effort`, `targetVersion`, `dueUtc`, `parentObjectiveId`, `blockedByObjectiveIds`, `refinementSummary`, `suggestedPipelineId`, `refinementSessionIds`), or `{ "Error": "Objective not found" }`.

---

### create_objective

Create an internal-first objective or intake record that can link repositories, planning, delivery evidence, and incidents.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "title": { "type": "string", "description": "Objective title" },
    "description": { "type": "string", "description": "Optional long-form description" },
    "status": { "type": "string", "description": "Optional objective status such as Draft, Scoped, Planned, or InProgress" },
    "kind": { "type": "string", "description": "Optional backlog kind such as Feature, Bug, Refactor, Research, Chore, or Initiative" },
    "category": { "type": "string", "description": "Optional category such as Frontend, Backend, DevEx, or Ops" },
    "priority": { "type": "string", "description": "Optional priority such as P0, P1, P2, or P3" },
    "rank": { "type": "integer", "description": "Optional deterministic backlog rank" },
    "backlogState": { "type": "string", "description": "Optional backlog state such as Inbox, ReadyForPlanning, or ReadyForDispatch" },
    "effort": { "type": "string", "description": "Optional effort such as XS, S, M, L, or XL" },
    "owner": { "type": "string", "description": "Optional owner display label" },
    "targetVersion": { "type": "string", "description": "Optional target release version" },
    "dueUtc": { "type": "string", "description": "Optional due timestamp in UTC" },
    "parentObjectiveId": { "type": "string", "description": "Optional parent objective identifier" },
    "blockedByObjectiveIds": { "type": "array", "items": { "type": "string" }, "description": "Blocking objective identifiers" },
    "refinementSummary": { "type": "string", "description": "Optional captain-generated refinement summary" },
    "suggestedPipelineId": { "type": "string", "description": "Optional suggested pipeline identifier" },
    "refinementSessionIds": { "type": "array", "items": { "type": "string" }, "description": "Linked refinement-session IDs" },
    "tags": { "type": "array", "items": { "type": "string" }, "description": "Optional tags" },
    "acceptanceCriteria": { "type": "array", "items": { "type": "string" }, "description": "Acceptance criteria" },
    "nonGoals": { "type": "array", "items": { "type": "string" }, "description": "Explicit non-goals" },
    "rolloutConstraints": { "type": "array", "items": { "type": "string" }, "description": "Rollout constraints" },
    "evidenceLinks": { "type": "array", "items": { "type": "string" }, "description": "Evidence or source links" },
    "fleetIds": { "type": "array", "items": { "type": "string" }, "description": "Linked fleet IDs" },
    "vesselIds": { "type": "array", "items": { "type": "string" }, "description": "Linked vessel IDs" },
    "planningSessionIds": { "type": "array", "items": { "type": "string" }, "description": "Linked planning-session IDs" },
    "voyageIds": { "type": "array", "items": { "type": "string" }, "description": "Linked voyage IDs" },
    "missionIds": { "type": "array", "items": { "type": "string" }, "description": "Linked mission IDs" },
    "checkRunIds": { "type": "array", "items": { "type": "string" }, "description": "Linked check-run IDs" },
    "releaseIds": { "type": "array", "items": { "type": "string" }, "description": "Linked release IDs" },
    "deploymentIds": { "type": "array", "items": { "type": "string" }, "description": "Linked deployment IDs" },
    "incidentIds": { "type": "array", "items": { "type": "string" }, "description": "Linked incident IDs" }
  },
  "required": ["title"]
}
```

**Response:** serialized `Objective` object.

---

### Backlog And Objective Tools

Backlog is the user-facing label, but MCP keeps both objective-compatible and backlog-named tools for the same normalized `Objective` entity.

Core CRUD and reorder tools:

| Tool | Purpose | Notes |
|---|---|---|
| `list_objectives` | Enumerate objective/backlog records | Same filters as `list_backlog` |
| `list_backlog` | Enumerate backlog items | Preferred user-facing alias |
| `get_objective` | Read one objective | Returns the expanded backlog shape |
| `get_backlog_item` | Read one backlog item | Alias over the same objective-backed record |
| `create_objective` | Create one objective | Accepts the expanded backlog metadata fields |
| `create_backlog_item` | Create one backlog item | Preferred user-facing alias |
| `update_objective` | Update one objective/backlog entry | Requires `objectiveId` plus any fields to mutate |
| `reorder_objectives` | Apply explicit rank updates | Uses `{ items: [{ objectiveId, rank }] }` |
| `reorder_backlog_items` | Apply explicit rank updates | Preferred user-facing alias |
| `delete_objective` | Delete one objective | Removes the normalized row and snapshot-backed current-state chain |
| `delete_backlog_item` | Delete one backlog item | Preferred user-facing alias |

Refinement tools:

| Tool | Purpose | Notes |
|---|---|---|
| `list_backlog_refinement_sessions` | List refinement sessions for one backlog item (lightweight, paginated) | Requires `objectiveId`; optional `pageNumber`, `pageSize` (default 25, max 100) |
| `create_backlog_refinement_session` | Start captain-backed refinement | Requires explicit `captainId`; `vesselId` is optional |
| `get_backlog_refinement_session` | Read one refinement transcript | Returns session, messages, captain, vessel, and linked backlog item |
| `send_backlog_refinement_message` | Append one user message | Launches the next refinement turn |
| `summarize_backlog_refinement_session` | Create/select a structured summary | Optional `messageId` |
| `apply_backlog_refinement_summary` | Apply the summary back to the backlog item | Optional `markMessageSelected` and `promoteBacklogState` |
| `stop_backlog_refinement_session` | Stop one active refinement session | Releases the selected captain |

Planning handoff tools:

| Tool | Purpose | Notes |
|---|---|---|
| `create_backlog_planning_session` | Start a repository-aware planning session from one backlog item | Requires `objectiveId`, `captainId`, and `vesselId` |
| `get_backlog_planning_session` | Inspect one planning session created from backlog work | Returns transcript plus linked backlog items |
| `dispatch_backlog_planning_session` | Dispatch a voyage from the planning session | Keeps the backlog/objective linkage on the resulting voyage |

Shared input notes:

- `update_objective` accepts the same expanded backlog fields as `create_objective`, plus the required `objectiveId`
- `create_backlog_item` mirrors `create_objective` but uses backlog terminology in the tool name and descriptions
- refinement sessions are lighter than planning and do not provision a dock/worktree by default
- planning handoff tools are repository-aware and require an explicit vessel

Example `reorder_backlog_items` input:

```json
{
  "items": [
    { "objectiveId": "obj_abc123", "rank": 10 },
    { "objectiveId": "obj_def456", "rank": 20 }
  ]
}
```

Example `create_backlog_refinement_session` input:

```json
{
  "objectiveId": "obj_abc123",
  "captainId": "cpt_abc123",
  "vesselId": "vsl_abc123",
  "title": "Refine release rollback work"
}
```

Example `create_backlog_planning_session` input:

```json
{
  "objectiveId": "obj_abc123",
  "captainId": "cpt_abc123",
  "vesselId": "vsl_abc123",
  "pipelineId": "pln_abc123",
  "title": "Plan release rollback implementation"
}
```

---

### get_check_run

Inspect one structured check run, including status, logs, parsed test summary, coverage summary, artifacts, and linked mission/voyage/release context when available.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "checkRunId": { "type": "string", "description": "Check run ID (chk_ prefix)" }
  },
  "required": ["checkRunId"]
}
```

**Response:** serialized `CheckRun` object, or `{ "Error": "Check run not found" }`.

---

### run_check

Start a structured check run for a vessel using the resolved workflow profile or an explicit command override.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "vesselId": { "type": "string", "description": "Target vessel ID (vsl_ prefix)" },
    "workflowProfileId": { "type": "string", "description": "Optional workflow profile override (wfp_ prefix)" },
    "missionId": { "type": "string", "description": "Optional linked mission ID (msn_ prefix)" },
    "voyageId": { "type": "string", "description": "Optional linked voyage ID (vyg_ prefix)" },
    "type": { "type": "string", "description": "Check type such as Build, UnitTest, IntegrationTest, Deploy, SmokeTest, HealthCheck, or ReleaseVersioning" },
    "environmentName": { "type": "string", "description": "Optional environment name for deploy/rollback/verification checks" },
    "label": { "type": "string", "description": "Optional display label" },
    "branchName": { "type": "string", "description": "Optional branch association" },
    "commitHash": { "type": "string", "description": "Optional commit-hash association" },
    "commandOverride": { "type": "string", "description": "Optional raw shell command override" }
  },
  "required": ["vesselId", "type"]
}
```

**Response:** serialized `CheckRun` object.

**Notes:**

- This uses the default MCP tenant-admin context.
- Workflow readiness and command validation still apply, so invalid vessel/workflow/input combinations are returned as MCP tool errors.

---

### retry_check_run

Retry a previously completed structured check run using the same resolved scope and command context.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "checkRunId": { "type": "string", "description": "Check run ID (chk_ prefix)" }
  },
  "required": ["checkRunId"]
}
```

**Response:** serialized `CheckRun` object.

---

### get_deployment

Inspect one deployment including approval status, verification state, rollback state, linked checks, and request-history evidence.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "deploymentId": { "type": "string", "description": "Deployment ID (dpl_ prefix)" }
  },
  "required": ["deploymentId"]
}
```

**Response:** serialized `Deployment` object, or `{ "Error": "Deployment not found" }`.

---

### create_deployment

Create a bounded deployment record and execute it immediately when approval is not required.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "vesselId": { "type": "string", "description": "Optional vessel ID (vsl_ prefix)" },
    "workflowProfileId": { "type": "string", "description": "Optional workflow profile override (wfp_ prefix)" },
    "environmentId": { "type": "string", "description": "Optional environment ID (env_ prefix)" },
    "environmentName": { "type": "string", "description": "Optional environment name" },
    "releaseId": { "type": "string", "description": "Optional linked release ID (rel_ prefix)" },
    "missionId": { "type": "string", "description": "Optional linked mission ID (msn_ prefix)" },
    "voyageId": { "type": "string", "description": "Optional linked voyage ID (vyg_ prefix)" },
    "title": { "type": "string", "description": "Optional deployment title" },
    "sourceRef": { "type": "string", "description": "Optional source ref such as a branch, tag, or commit" },
    "summary": { "type": "string", "description": "Optional short summary" },
    "notes": { "type": "string", "description": "Optional operator notes" },
    "autoExecute": { "type": "boolean", "description": "Whether to execute immediately when approval is not required" }
  }
}
```

**Response:** serialized `Deployment` object.

---

### approve_deployment

Approve a pending deployment and begin execution.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "deploymentId": { "type": "string", "description": "Deployment ID (dpl_ prefix)" },
    "comment": { "type": "string", "description": "Optional approval comment" }
  },
  "required": ["deploymentId"]
}
```

**Response:** serialized `Deployment` object.

---

### verify_deployment

Run post-deploy verification for an existing deployment.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "deploymentId": { "type": "string", "description": "Deployment ID (dpl_ prefix)" }
  },
  "required": ["deploymentId"]
}
```

**Response:** serialized `Deployment` object.

---

### rollback_deployment

Run the configured rollback flow for an existing deployment.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "deploymentId": { "type": "string", "description": "Deployment ID (dpl_ prefix)" }
  },
  "required": ["deploymentId"]
}
```

**Response:** serialized `Deployment` object.

---

### get_release

Inspect one release record, including linked voyages, missions, checks, versions, tags, notes, and derived artifacts.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "releaseId": { "type": "string", "description": "Release ID (rel_ prefix)" }
  },
  "required": ["releaseId"]
}
```

**Response:** serialized `Release` object, or `{ "Error": "Release not found" }`.

---

### create_release

Create a first-class release record from linked voyages, missions, and structured checks, with derived version, notes, and artifacts when available.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "vesselId": { "type": "string", "description": "Optional vessel ID (vsl_ prefix)" },
    "workflowProfileId": { "type": "string", "description": "Optional workflow profile override (wfp_ prefix)" },
    "title": { "type": "string", "description": "Optional release title override" },
    "version": { "type": "string", "description": "Optional version label" },
    "tagName": { "type": "string", "description": "Optional git tag or image tag" },
    "summary": { "type": "string", "description": "Optional short release summary" },
    "notes": { "type": "string", "description": "Optional long-form release notes" },
    "status": { "type": "string", "description": "Optional release status such as Draft, Candidate, or Shipped" },
    "voyageIds": { "type": "array", "items": { "type": "string" }, "description": "Linked voyage IDs (vyg_ prefix)" },
    "missionIds": { "type": "array", "items": { "type": "string" }, "description": "Linked mission IDs (msn_ prefix)" },
    "checkRunIds": { "type": "array", "items": { "type": "string" }, "description": "Linked check-run IDs (chk_ prefix)" }
  }
}
```

**Response:** serialized `Release` object.

---

### get_runbook

Inspect one runbook including parameters, bound workflow profile, environment, and step structure.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "runbookId": { "type": "string", "description": "Runbook ID (same as playbook ID)" }
  },
  "required": ["runbookId"]
}
```

**Response:** serialized `Runbook` object, or `{ "Error": "Runbook not found" }`.

---

### get_runbook_execution

Inspect one runbook execution including completed steps, notes, and deployment or incident linkage.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "runbookExecutionId": { "type": "string", "description": "Runbook execution ID (rbx_ prefix)" }
  },
  "required": ["runbookExecutionId"]
}
```

**Response:** serialized `RunbookExecution` object, or `{ "Error": "Runbook execution not found" }`.

---

### start_runbook_execution

Start a guided runbook execution with optional parameter overrides and deployment or incident context.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "runbookId": { "type": "string", "description": "Runbook ID (same as playbook ID)" },
    "title": { "type": "string", "description": "Optional execution title override" },
    "workflowProfileId": { "type": "string", "description": "Optional workflow profile override (wfp_ prefix)" },
    "environmentId": { "type": "string", "description": "Optional environment ID (env_ prefix)" },
    "environmentName": { "type": "string", "description": "Optional environment name override" },
    "checkType": { "type": "string", "description": "Optional check type override" },
    "deploymentId": { "type": "string", "description": "Optional related deployment ID (dpl_ prefix)" },
    "incidentId": { "type": "string", "description": "Optional related incident ID (inc_ prefix)" },
    "notes": { "type": "string", "description": "Optional execution notes" },
    "parameterValues": {
      "type": "object",
      "additionalProperties": { "type": "string" },
      "description": "Optional parameter-value map"
    }
  },
  "required": ["runbookId"]
}
```

**Response:** serialized `RunbookExecution` object.

---

### list_prompt_templates

List prompt templates (lightweight, paginated), optionally filtered by category. Returns metadata only -- name, category, description, active flag, and a `contentLength` hint -- not the template body. Fetch a single template's full content with [get_prompt_template](#get_prompt_template).

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "category": { "type": "string", "description": "Optional category filter such as 'persona' or 'mission'" },
    "pageNumber": { "type": "integer", "description": "1-based page number (default 1)" },
    "pageSize": { "type": "integer", "description": "Results per page (default 25, max 100)" }
  }
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `category` | string | No | Optional category filter |
| `pageNumber` | integer | No | 1-based page number (default 1) |
| `pageSize` | integer | No | Results per page (default 25, max 100) |

**Response:** a paginated envelope `{ success, pageNumber, pageSize, totalPages, totalRecords, objects }`, where each item in `objects` carries `name`, `category`, `description`, `active`, `isBuiltIn`, `contentLength`, and `lastUpdateUtc`. The template body is intentionally omitted -- retrieve it per-template via [get_prompt_template](#get_prompt_template).

---

### create_prompt_template

Create a new prompt template.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "name": { "type": "string", "description": "Template name" },
    "category": { "type": "string", "description": "Template category such as 'persona' or 'mission'" },
    "content": { "type": "string", "description": "Template content" },
    "description": { "type": "string", "description": "Template description" },
    "active": { "type": "boolean", "description": "Whether the template is active" }
  },
  "required": ["name", "category", "content"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `name` | string | Yes | Template name |
| `category` | string | Yes | Template category |
| `content` | string | Yes | Template content |
| `description` | string | No | Template description |
| `active` | bool | No | Whether the template is active |

**Response:** Created [PromptTemplate](#prompttemplate) object, or `{ "Error": "Template already exists: ..." }`.

---

### get_prompt_template

Get a prompt template by name.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "name": { "type": "string", "description": "Template name (e.g. 'mission.rules', 'persona.worker')" }
  },
  "required": ["name"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `name` | string | Yes | Template name (e.g. `"mission.rules"`, `"persona.worker"`) |

**Response:** [PromptTemplate](#prompttemplate) object, or `{ "Error": "Prompt template not found" }`.

---

### update_prompt_template

Update a prompt template's content. If the template does not already exist, this tool creates it.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "name": { "type": "string", "description": "Template name" },
    "content": { "type": "string", "description": "New template content" },
    "description": { "type": "string", "description": "New template description" }
  },
  "required": ["name", "content"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `name` | string | Yes | Template name |
| `content` | string | Yes | New template content |
| `description` | string | No | New template description |

**Response:** Updated or created [PromptTemplate](#prompttemplate) object.

---

### reset_prompt_template

Reset a prompt template to its built-in default content.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "name": { "type": "string", "description": "Template name" }
  },
  "required": ["name"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `name` | string | Yes | Template name to reset |

**Response:** Reset [PromptTemplate](#prompttemplate) object with default content restored, or `{ "Error": "No built-in default exists for this template" }`.

---

### create_persona

Create a custom persona.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "name": { "type": "string", "description": "Persona name" },
    "description": { "type": "string", "description": "Persona description" },
    "promptTemplateName": { "type": "string", "description": "Name of the prompt template to use for this persona" }
  },
  "required": ["name", "promptTemplateName"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `name` | string | Yes | Persona name |
| `description` | string | No | Persona description |
| `promptTemplateName` | string | Yes | Name of the prompt template to use for this persona |

**Response:** The newly created [Persona](#persona) object.

---

### get_persona

Get a persona by name.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "name": { "type": "string", "description": "Persona name" }
  },
  "required": ["name"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `name` | string | Yes | Persona name |

**Response:** [Persona](#persona) object, or `{ "Error": "Persona not found" }`.

---

### update_persona

Update persona properties.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "name": { "type": "string", "description": "Persona name" },
    "description": { "type": "string", "description": "New persona description" },
    "promptTemplateName": { "type": "string", "description": "New prompt template name" }
  },
  "required": ["name"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `name` | string | Yes | Persona name |
| `description` | string | No | New persona description |
| `promptTemplateName` | string | No | New prompt template name |

**Response:** Updated [Persona](#persona) object, or `{ "Error": "Persona not found" }`.

---

### delete_persona

Delete a custom persona. Built-in personas cannot be deleted.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "name": { "type": "string", "description": "Persona name" }
  },
  "required": ["name"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `name` | string | Yes | Persona name to delete |

**Response:**

```json
{ "Status": "deleted", "Name": "my-custom-persona" }
```

Returns `{ "Error": "Persona not found" }` if the name does not exist.
Returns `{ "Error": "Cannot delete built-in persona" }` if the persona is built-in.

---

### create_pipeline

Create a custom pipeline with stages.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "name": { "type": "string", "description": "Pipeline name" },
    "description": { "type": "string", "description": "Pipeline description" },
    "stages": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "personaName": { "type": "string", "description": "Persona name for this stage" },
          "isOptional": { "type": "boolean", "description": "Whether this stage is optional (default false)" },
          "description": { "type": "string", "description": "Stage description" }
        },
        "required": ["personaName"]
      },
      "description": "Ordered list of pipeline stages"
    }
  },
  "required": ["name", "stages"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `name` | string | Yes | Pipeline name |
| `description` | string | No | Pipeline description |
| `stages` | array | Yes | Ordered list of pipeline stages, each with `personaName` (required), `isOptional` (optional, default false), and `description` (optional) |

**Example Input:**

```json
{
  "name": "code-review-pipeline",
  "description": "Implement, test, and review",
  "stages": [
    { "personaName": "worker", "description": "Implement the feature" },
    { "personaName": "tester", "description": "Write and run tests", "isOptional": true },
    { "personaName": "reviewer", "description": "Code review" }
  ]
}
```

**Response:** The newly created [Pipeline](#pipeline) object with stages.

---

### get_pipeline

Get a pipeline by name.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "name": { "type": "string", "description": "Pipeline name" }
  },
  "required": ["name"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `name` | string | Yes | Pipeline name |

**Response:** [Pipeline](#pipeline) object with stages, or `{ "Error": "Pipeline not found" }`.

---

### update_pipeline

Update pipeline properties and stages. If `stages` is provided, it replaces all existing stages.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "name": { "type": "string", "description": "Pipeline name" },
    "description": { "type": "string", "description": "New pipeline description" },
    "stages": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "personaName": { "type": "string", "description": "Persona name for this stage" },
          "isOptional": { "type": "boolean", "description": "Whether this stage is optional (default false)" },
          "description": { "type": "string", "description": "Stage description" }
        },
        "required": ["personaName"]
      },
      "description": "New ordered list of pipeline stages (replaces all existing stages)"
    }
  },
  "required": ["name"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `name` | string | Yes | Pipeline name |
| `description` | string | No | New pipeline description |
| `stages` | array | No | New ordered list of pipeline stages (replaces all existing stages if provided) |

**Response:** Updated [Pipeline](#pipeline) object with stages, or `{ "Error": "Pipeline not found" }`.

---

### delete_pipeline

Delete a custom pipeline. Built-in pipelines cannot be deleted.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "name": { "type": "string", "description": "Pipeline name" }
  },
  "required": ["name"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `name` | string | Yes | Pipeline name to delete |

**Response:**

```json
{ "Status": "deleted", "Name": "my-custom-pipeline" }
```

Returns `{ "Error": "Pipeline not found" }` if the name does not exist.
Returns `{ "Error": "Cannot delete built-in pipeline" }` if the pipeline is built-in.

---

### get_model_endpoint

Get details of a specific model endpoint (managed embedding or inference provider reference). The stored API key is never returned; `hasApiKey` indicates whether one is set.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "endpointId": { "type": "string", "description": "Model endpoint ID (mep_ prefix)" }
  },
  "required": ["endpointId"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `endpointId` | string | Yes | Model endpoint ID (prefix `mep_`) |

**Response:** [ModelEndpoint](#modelendpoint) object, or `{ "Error": "Model endpoint not found" }`.

---

### create_model_endpoint

Create a model endpoint. Supply `apiKey` to store a provider key; it is write-only and is never returned on reads.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "name": { "type": "string", "description": "Display name" },
    "baseUrl": { "type": "string", "description": "Provider API base URL" },
    "kind": { "type": "string", "description": "Embedding (default) or Inference" },
    "provider": { "type": "string", "description": "Ollama, OpenAI, OpenAICompatible, Anthropic, Gemini, or VoyageAI" },
    "model": { "type": "string", "description": "Model name to target" },
    "apiKey": { "type": "string", "description": "Provider API key. Write-only: accepted here, never returned on reads." },
    "dimensionality": { "type": "integer", "description": "Embedding dimensionality (default 0)" },
    "timeoutMs": { "type": "integer", "description": "Request timeout in milliseconds (default 120000, clamped to [1000, 600000])" },
    "enabled": { "type": "boolean", "description": "Whether the endpoint participates in health sweeps (default true)" }
  },
  "required": ["name", "baseUrl"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `name` | string | Yes | Display name |
| `baseUrl` | string | Yes | Provider API base URL |
| `kind` | string | No | `Embedding` (default) or `Inference` |
| `provider` | string | No | `Ollama`, `OpenAI`, `OpenAICompatible`, `Anthropic`, `Gemini`, or `VoyageAI` |
| `model` | string | No | Model name to target |
| `apiKey` | string | No | Provider API key. Write-only: accepted here, never returned on reads. |
| `dimensionality` | integer | No | Embedding dimensionality (default 0) |
| `timeoutMs` | integer | No | Request timeout in milliseconds (default 120000, clamped to [1000, 600000]) |
| `enabled` | boolean | No | Whether the endpoint participates in health sweeps (default true) |

**Example Input:**

```json
{
  "name": "Primary embeddings",
  "kind": "Embedding",
  "provider": "OpenAI",
  "baseUrl": "https://api.openai.com/v1",
  "model": "text-embedding-3-small",
  "dimensionality": 1536,
  "apiKey": "sk-example-key"
}
```

**Response:** The newly created [ModelEndpoint](#modelendpoint) object (with `hasApiKey: true`, no `apiKey` field). Returns `{ "Error": "..." }` when `Anthropic` is paired with `Embedding`, or `VoyageAI` is paired with `Inference`.

---

### update_model_endpoint

Update an existing model endpoint. Omit `apiKey` to keep the stored key; send `apiKey` to replace it.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "endpointId": { "type": "string", "description": "Model endpoint ID (mep_ prefix)" },
    "name": { "type": "string", "description": "New display name" },
    "kind": { "type": "string", "description": "Embedding or Inference" },
    "provider": { "type": "string", "description": "Ollama, OpenAI, OpenAICompatible, Anthropic, Gemini, or VoyageAI" },
    "baseUrl": { "type": "string", "description": "New provider API base URL" },
    "model": { "type": "string", "description": "New model name to target" },
    "apiKey": { "type": "string", "description": "New provider API key. Omit to keep the stored key." },
    "dimensionality": { "type": "integer", "description": "Embedding dimensionality" },
    "timeoutMs": { "type": "integer", "description": "Request timeout in milliseconds (clamped to [1000, 600000])" },
    "enabled": { "type": "boolean", "description": "Whether the endpoint participates in health sweeps" }
  },
  "required": ["endpointId"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `endpointId` | string | Yes | Model endpoint ID (prefix `mep_`) |
| `name` | string | No | New display name |
| `kind` | string | No | `Embedding` or `Inference` |
| `provider` | string | No | `Ollama`, `OpenAI`, `OpenAICompatible`, `Anthropic`, `Gemini`, or `VoyageAI` |
| `baseUrl` | string | No | New provider API base URL |
| `model` | string | No | New model name to target |
| `apiKey` | string | No | New provider API key. Omit to keep the stored key. |
| `dimensionality` | integer | No | Embedding dimensionality |
| `timeoutMs` | integer | No | Request timeout in milliseconds (clamped to [1000, 600000]) |
| `enabled` | boolean | No | Whether the endpoint participates in health sweeps |

**Response:** Updated [ModelEndpoint](#modelendpoint) object, or `{ "Error": "Model endpoint not found" }`. A rejected provider/kind combination returns `{ "Error": "..." }`.

When `apiKey` is omitted, MCP preserves the current stored key.

---

### delete_model_endpoint

Delete a model endpoint by ID.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "endpointId": { "type": "string", "description": "Model endpoint ID (mep_ prefix)" }
  },
  "required": ["endpointId"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `endpointId` | string | Yes | Model endpoint ID (prefix `mep_`) |

**Response:**

```json
{ "Status": "deleted", "EndpointId": "mep_abc123" }
```

Returns `{ "Error": "Model endpoint not found" }` if the ID does not exist.

---

### validate_model_endpoint

Validate one endpoint by issuing a real request against the provider: an embedding request for `Embedding` endpoints, or a completion request for `Inference` endpoints. The resulting health status, timestamp, latency, and any error are persisted on the endpoint.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "endpointId": { "type": "string", "description": "Model endpoint ID (mep_ prefix)" }
  },
  "required": ["endpointId"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `endpointId` | string | Yes | Model endpoint ID (prefix `mep_`) |

**Response:** [ModelEndpointProbeResult](#modelendpointproberesult) object.

```json
{
  "success": true,
  "baseUrl": "https://api.openai.com/v1",
  "latencyMs": 92,
  "statusCode": 200,
  "error": null,
  "embeddingDimensions": 1536,
  "sampleText": null,
  "timestampUtc": "2026-03-07T12:00:00Z"
}
```

Returns `{ "Error": "Model endpoint not found" }` if the ID does not exist.

---

### health_check_model_endpoints

Probe all enabled model endpoints, deduplicated by base URL, and persist each endpoint's health status. Returns the number of distinct base URLs that were probed.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {}
}
```

No parameters required.

**Response:** [ModelEndpointHealthSweepResponse](#modelendpointhealthsweepresponse) object.

```json
{ "distinctBaseUrlsProbed": 3 }
```

---

### backup

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
| `settings.json` | Current Armada server configuration, including `GitHubToken` when configured |
| `manifest.json` | Backup metadata: timestamp, schema version, Armada version, record counts per table |

---

### restore

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

## Per-Step Captain Selection

Personas can carry a default captain, and a dispatch can dictate which captain runs each pipeline step, with a capability-tier fallback when that captain is busy. See [CAPTAIN_ROUTING.md](CAPTAIN_ROUTING.md) for the full model.

- `create_persona` / `update_persona` accept `defaultCaptainId` (a `cpt_` id, validated against an existing captain). `update_persona` clears it with an empty string. `get_persona` returns it.
- `dispatch` accepts `captainAssignments`, an array of `{ persona, captainId, fallbackTier }` that binds each pipeline step (persona) to a preferred captain and a fallback tier (`Economy` | `Standard` | `Premium`). Entries in `missions` may also carry a per-mission `requestedCaptainId` and `tier`.
- Mission responses (`mission_status`, `get_mission_diff`, `get_mission_log`, `enumerate` for missions) include both `requestedCaptainId` (the preferred captain) and `captainId` (the captain that actually ran).

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
| `remoteTunnel` | [RemoteTunnelStatus](#remotetunnelstatus) | Current outbound remote tunnel status |
| `timestampUtc` | string | ISO 8601 UTC timestamp |

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
| `lastError` | string \| null | Last recorded error |
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

#### EnumerationResult

Paginated result wrapper returned by `enumerate`.

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
| `hasGitHubTokenOverride` | bool | Indicates whether a per-vessel GitHub token override is stored. MCP never returns the raw token value. |
| `landingMode` | string \| null | [LandingModeEnum](#landingmodeenum) — per-vessel landing policy override |
| `branchCleanupPolicy` | string \| null | [BranchCleanupPolicyEnum](#branchcleanuppolicyenum) — per-vessel branch cleanup override |
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
| `selectedPlaybooks` | array\<[SelectedPlaybook](#selectedplaybook)\> | Ordered playbook selections recorded on the voyage |
| `createdUtc` | string | ISO 8601 creation timestamp |
| `completedUtc` | string \| null | ISO 8601 completion timestamp |
| `lastUpdateUtc` | string | ISO 8601 last update timestamp |
| `autoPush` | bool \| null | Override global auto-push setting |
| `autoCreatePullRequests` | bool \| null | Override global auto-create PR setting |
| `autoMergePullRequests` | bool \| null | Override global auto-merge PR setting |
| `landingMode` | string \| null | [LandingModeEnum](#landingmodeenum) — per-voyage landing policy override |

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
| `selectedPlaybooks` | array\<[SelectedPlaybook](#selectedplaybook)\> | Ordered playbook selections requested for the mission |
| `playbookSnapshots` | array\<[MissionPlaybookSnapshot](#missionplaybooksnapshot)\> | Immutable playbook materialization used for execution |
| `parentMissionId` | string \| null | Parent mission ID for sub-tasks |
| `branchName` | string \| null | Git branch name created for this mission |
| `dockId` | string \| null | Assigned dock (worktree) ID |
| `processId` | int \| null | OS process ID of the agent working this mission |
| `prUrl` | string \| null | Pull request URL |
| `commitHash` | string \| null | Git commit hash (HEAD) captured at mission completion |
| `diffSnapshot` | string \| null | Always `null` in list/status responses. Use `get_mission_diff` to retrieve the full diff. |
| `createdUtc` | string | ISO 8601 creation timestamp |
| `startedUtc` | string \| null | ISO 8601 start timestamp |
| `completedUtc` | string \| null | ISO 8601 completion timestamp |
| `totalRuntimeMs` | long \| null | Total execution runtime in milliseconds |
| `lastUpdateUtc` | string | ISO 8601 last update timestamp |

#### Captain

| Field | Type | Description |
|---|---|---|
| `id` | string | Captain ID (prefix `cpt_`) |
| `name` | string | Display name |
| `runtime` | string | [AgentRuntimeEnum](#agentruntimeenum) value |
| `model` | string \| null | Optional model override for this captain |
| `state` | string | [CaptainStateEnum](#captainstateenum) value |
| `currentMissionId` | string \| null | Currently assigned mission ID |
| `currentDockId` | string \| null | Currently assigned dock (worktree) ID |
| `processId` | int \| null | OS process ID of the agent |
| `recoveryAttempts` | int | Number of recovery attempts after stalls |
| `lastHeartbeatUtc` | string \| null | ISO 8601 last heartbeat timestamp |
| `createdUtc` | string | ISO 8601 creation timestamp |
| `lastUpdateUtc` | string | ISO 8601 last update timestamp |

#### CaptainToolAccessResult

| Field | Type | Description |
|---|---|---|
| `captainId` | string | Captain ID |
| `captainName` | string | Captain display name |
| `runtime` | string | [AgentRuntimeEnum](#agentruntimeenum) value |
| `toolsAccessible` | bool | Whether Armada currently considers the catalog reachable through this captain |
| `availabilityVerified` | bool | Whether Armada actively verified availability instead of inferring it |
| `availabilitySource` | string | Machine-readable availability source such as `mux-probe` or `runtime-assumption` |
| `summary` | string | Human-readable explanation of availability and caveats |
| `endpointName` | string \| null | Mux endpoint name when applicable |
| `toolsEnabled` | bool \| null | Whether the runtime reported tool calling enabled when applicable |
| `effectiveToolCount` | int \| null | Runtime-reported total tool count when applicable |
| `armadaToolCount` | int | Number of Armada MCP tools in the returned catalog |
| `tools` | array | Ordered list of [CaptainToolSummary](#captaintoolsummary) objects |

#### CaptainToolSummary

| Field | Type | Description |
|---|---|---|
| `name` | string | Tool name |
| `description` | string | Human-readable tool description |
| `inputSchemaJson` | string \| null | Serialized JSON input schema when available |

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

#### Playbook

| Field | Type | Description |
|---|---|---|
| `id` | string | Playbook ID (prefix `pbk_`) |
| `tenantId` | string \| null | Owning tenant |
| `userId` | string \| null | Owning user |
| `fileName` | string | Markdown file name |
| `description` | string \| null | Human-readable description |
| `content` | string | Markdown body |
| `active` | bool | Whether the playbook is available for new selections |
| `createdUtc` | string | ISO 8601 creation timestamp |
| `lastUpdateUtc` | string | ISO 8601 last update timestamp |

#### SelectedPlaybook

| Field | Type | Description |
|---|---|---|
| `playbookId` | string | Selected playbook ID |
| `deliveryMode` | [PlaybookDeliveryModeEnum](#playbookdeliverymodeenum) | How the playbook is delivered to the model |

#### MissionPlaybookSnapshot

| Field | Type | Description |
|---|---|---|
| `playbookId` | string \| null | Source playbook ID |
| `fileName` | string | Source file name |
| `description` | string \| null | Source description |
| `content` | string | Frozen markdown body used for execution |
| `deliveryMode` | [PlaybookDeliveryModeEnum](#playbookdeliverymodeenum) | Resolved delivery mode |
| `resolvedPath` | string \| null | Absolute runtime path when materialized outside the worktree |
| `worktreeRelativePath` | string \| null | Relative dock path when attached into the worktree |
| `sourceLastUpdateUtc` | string | ISO 8601 source update timestamp captured into the snapshot |

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

#### Harbor

A registered host-side runner. Only `name`, `maxConcurrentJobs`, and `enabled` are operator-editable via MCP; the remaining runtime fields are reported by the link.

| Field | Type | Description |
|---|---|---|
| `id` | string | Harbor ID (prefix `hbr_`) |
| `tenantId` | string \| null | Owning tenant ID |
| `userId` | string \| null | Owning user ID |
| `name` | string | Human-facing Harbor name |
| `capabilities` | array | Advertised [HarborCapability](#harborcapability) entries |
| `connectionStatus` | string | [HarborConnectionStatusEnum](#harborconnectionstatusenum) value |
| `maxConcurrentJobs` | int | Maximum concurrent jobs the Harbor accepts (default 4, minimum 1) |
| `enabled` | bool | Whether the Harbor is enabled for routing (default true) |
| `protocolVersion` | string \| null | Protocol version reported at handshake |
| `osPlatform` | string \| null | OS platform reported at handshake (e.g. `Windows`, `Linux`, `macOS`) |
| `architecture` | string \| null | Processor architecture reported at handshake (e.g. `X64`, `Arm64`) |
| `lastSeenUtc` | string \| null | ISO 8601 last heartbeat or message timestamp |
| `lastConnectedUtc` | string \| null | ISO 8601 last link-establishment timestamp |
| `createdUtc` | string | ISO 8601 creation timestamp |
| `lastUpdateUtc` | string | ISO 8601 last update timestamp |

#### HarborCapability

| Field | Type | Description |
|---|---|---|
| `name` | string | Capability name (e.g. a runtime like `claude` or a host tool like `git`) |
| `available` | bool | Whether the capability is currently available on the host |
| `detail` | string \| null | Optional human-readable detail (e.g. a version string) |

#### PromptTemplate

| Field | Type | Description |
|---|---|---|
| `id` | string | Prompt template ID |
| `name` | string | Template name (e.g. `"mission.rules"`, `"persona.worker"`) |
| `description` | string \| null | Template description |
| `category` | string \| null | Template category |
| `content` | string | Template content |
| `isBuiltIn` | bool | Whether this is a built-in template |
| `active` | bool | Whether the template is active |

#### Persona

| Field | Type | Description |
|---|---|---|
| `id` | string | Persona ID |
| `name` | string | Persona name |
| `description` | string \| null | Persona description |
| `promptTemplateName` | string | Name of the prompt template used by this persona |
| `isBuiltIn` | bool | Whether this is a built-in persona |
| `active` | bool | Whether the persona is active |

#### Pipeline

| Field | Type | Description |
|---|---|---|
| `id` | string | Pipeline ID |
| `name` | string | Pipeline name |
| `description` | string \| null | Pipeline description |
| `stages` | array | Ordered list of [PipelineStage](#pipelinestage) objects |
| `isBuiltIn` | bool | Whether this is a built-in pipeline |
| `active` | bool | Whether the pipeline is active |

#### PipelineStage

| Field | Type | Description |
|---|---|---|
| `personaName` | string | Persona name for this stage |
| `isOptional` | bool | Whether this stage is optional |
| `description` | string \| null | Stage description |

#### ModelEndpoint

A managed reference to an external embedding or inference model behind a provider API. The `apiKey` is write-only: it is accepted on create/update but is never returned on reads. Reads expose `hasApiKey` instead.

| Field | Type | Description |
|---|---|---|
| `id` | string | Model endpoint ID (prefix `mep_`) |
| `tenantId` | string \| null | Owning tenant ID |
| `userId` | string \| null | Owning user ID |
| `name` | string | Display name |
| `kind` | string | `Embedding` or `Inference` |
| `provider` | string | `Ollama`, `OpenAI`, `OpenAICompatible`, `Anthropic`, `Gemini`, or `VoyageAI` |
| `baseUrl` | string | Provider API base URL |
| `model` | string \| null | Model name to target |
| `dimensionality` | int | Embedding dimensionality (default 0) |
| `timeoutMs` | int | Request timeout in milliseconds (default 120000, clamped to [1000, 600000]) |
| `enabled` | bool | Whether the endpoint participates in health sweeps (default true) |
| `hasApiKey` | bool | Read-only. Whether a provider key is stored. |
| `healthStatus` | string | `Unknown`, `Healthy`, or `Unhealthy` |
| `lastHealthCheckUtc` | string \| null | ISO 8601 timestamp of the last probe |
| `lastHealthError` | string \| null | Error text from the last failed probe |
| `lastLatencyMs` | int \| null | Latency of the last probe in milliseconds |
| `healthHistory` | array | Rolling series of recent probes (oldest first), each `{ timestampUtc, success }`, capped at 500 |
| `uptimePercentage` | double | Read-only. Percentage of retained probes that succeeded (0-100), derived from `healthHistory` |
| `consecutiveSuccesses` | int | Read-only. Trailing run of successful probes |
| `consecutiveFailures` | int | Read-only. Trailing run of failed probes |
| `firstHealthCheckUtc` | string \| null | Read-only. Earliest retained probe timestamp |
| `lastHealthyUtc` | string \| null | Read-only. Most recent successful probe timestamp |
| `lastUnhealthyUtc` | string \| null | Read-only. Most recent failed probe timestamp |
| `createdUtc` | string | ISO 8601 creation timestamp |
| `lastUpdateUtc` | string | ISO 8601 last update timestamp |

`Anthropic` cannot be paired with `kind` `Embedding`, and `VoyageAI` cannot be paired with `kind` `Inference`; both combinations are rejected with an error.

#### ModelEndpointProbeResult

Returned by `validate_model_endpoint`.

| Field | Type | Description |
|---|---|---|
| `success` | bool | Whether the probe request succeeded |
| `baseUrl` | string \| null | Base URL that was probed |
| `latencyMs` | int | Round-trip latency in milliseconds |
| `statusCode` | int \| null | HTTP status code returned by the provider, when available |
| `error` | string \| null | Error text when the probe failed |
| `embeddingDimensions` | int \| null | Dimensionality returned by an embedding probe |
| `sampleText` | string \| null | Sample completion text returned by an inference probe |
| `timestampUtc` | string | ISO 8601 timestamp of when the probe ran |

#### ModelEndpointHealthSweepResponse

Returned by `health_check_model_endpoints`.

| Field | Type | Description |
|---|---|---|
| `distinctBaseUrlsProbed` | int | Number of distinct base URLs probed during the sweep |

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

#### PlaybookDeliveryModeEnum

| Value | Description |
|---|---|
| `InlineFullContent` | Include the full markdown body directly in the rendered mission instructions |
| `InstructionWithReference` | Materialize the playbook outside the worktree and instruct the model to read the resolved path |
| `AttachIntoWorktree` | Materialize the playbook under the dock worktree and instruct the model to read it there |

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

#### HarborConnectionStatusEnum

| Value | Description |
|---|---|
| `Unknown` | No link established yet, or the state is not yet known |
| `Connected` | The Harbor currently has a live link to the Admiral |
| `Degraded` | The link is present but impaired (e.g. missed heartbeats) |
| `Disconnected` | The Harbor is registered but has no live link |

#### AgentRuntimeEnum

| Value | Description |
|---|---|
| `ClaudeCode` | Anthropic Claude Code CLI |
| `Codex` | OpenAI Codex CLI |
| `Gemini` | Google Gemini CLI |
| `Cursor` | Cursor agent CLI |
| `Mux` | Mux CLI |
| `OpenCode` | OpenCode CLI (OpenAI-compatible providers) |
| `Custom` | Custom agent runtime |

---

## Client Configuration

### Claude Desktop

Add Armada as an MCP server in your Claude Desktop configuration (`claude_desktop_config.json`):

```json
{
  "mcpServers": {
    "armada": {
      "url": "http://localhost:7891/mcp"
    }
  }
}
```

### Claude Code

Add Armada as an MCP server via the CLI:

```bash
claude mcp add --transport http armada http://localhost:7891/mcp
```

Or add to your project's `.mcp.json`:

```json
{
  "mcpServers": {
    "armada": {
      "type": "http",
      "url": "http://localhost:7891/mcp"
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
    "name": "status",
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
