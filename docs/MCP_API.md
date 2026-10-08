# Armada MCP API Reference

**Version:** 1.0.0
**Default URL:** `http://localhost:7891/mcp`
**Protocol:** [Model Context Protocol](https://modelcontextprotocol.io/) (MCP), Streamable HTTP transport
**Server Library:** Voltaic (McpHttpServer)
**Server Name:** `Armada`

## Remote Control Note

`v1.0.0` does not proxy Armada MCP traffic through the new remote-control tunnel or `Armada.Proxy`.

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
  - [Ask Armada Thread-Scoped Calls](#ask-armada-thread-scoped-calls)
- [Tools](#tools)
  - **Status**
    - [status](#status)
    - [inbox](#inbox)
    - [token_usage_summary](#token_usage_summary)
    - [papercut_summary](#papercut_summary)
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
  - **Vessel Import**
    - [discover_vessels](#discover_vessels)
    - [import_vessels](#import_vessels)
    - [categorize_vessel_import](#categorize_vessel_import)
    - [apply_fleet_recommendations](#apply_fleet_recommendations)
  - **Vessel Health**
    - [vessel_health](#vessel_health)
    - [evaluate_vessel_health](#evaluate_vessel_health)
    - [set_vessel_health_override](#set_vessel_health_override)
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
    - [retry_landing](#retry_landing)
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
    - [repair_dock](#repair_dock)
    - [unstick_dock](#unstick_dock)
    - [delete_docks](#delete_docks)
  - **Playbooks**
    - [get_playbook](#get_playbook)
    - [create_playbook](#create_playbook)
    - [update_playbook](#update_playbook)
    - [delete_playbook](#delete_playbook)
  - **Fleet Actions**
    - [create_fleet_action](#create_fleet_action)
    - [update_fleet_action](#update_fleet_action)
    - [delete_fleet_action](#delete_fleet_action)
    - [run_fleet_action](#run_fleet_action)
    - [fleet_action_run_status](#fleet_action_run_status)
    - [cancel_fleet_action_run](#cancel_fleet_action_run)
  - **CLI Permissions**
    - [cli_permission_prompt](#cli_permission_prompt)
    - [list_cli_permission_requests](#list_cli_permission_requests)
    - [get_cli_permission_request](#get_cli_permission_request)
    - [decide_cli_permission_request](#decide_cli_permission_request)
    - [list_cli_permission_rules](#list_cli_permission_rules)
    - [create_cli_permission_rule](#create_cli_permission_rule)
    - [update_cli_permission_rule](#update_cli_permission_rule)
    - [delete_cli_permission_rule](#delete_cli_permission_rule)
    - [set_captain_cli_permission_policy](#set_captain_cli_permission_policy)
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
    - [get_harbor_metrics](#get_harbor_metrics)
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

**Contract.** Tool names, argument names and types, and which arguments are required are frozen for 1.0 and listed in
[API_SURFACE_1.0.md](API_SURFACE_1.0.md) (rules: [COMPATIBILITY.md](COMPATIBILITY.md)). Arguments are camelCase (matched
case-insensitively); tool results carry the same entity shapes as the REST API, with PascalCase property names (the
camelCase exceptions are the `inbox` envelope, `count`, `criticalCount`, `warningCount`, and `items`, the
`list_prompt_templates` envelope and rows, the `list_cli_permission_requests` and `list_cli_permission_rules`
envelopes, the `delete_cli_permission_rule` result, and the `cli_permission_prompt` answer). Unlike REST, MCP results keep null-valued properties. Enums are emitted as their
names, as on REST. New tools, new optional arguments,
and new result fields may be added in minor releases. Tools whose description starts with `[Experimental]` (the Harbor
tools `get_harbor`, `get_harbor_metrics`, `create_harbor`, `update_harbor`, `delete_harbor`, `set_harbor_enabled`) are excluded from the
promise.

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
- Answer CLI captains' permission prompts (`cli_permission_prompt`), and list and decide CLI permission requests, manage their rules, and set a captain's CLI tool permission policy
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

Drop `--scope user` to add it for the current project only. While the Admiral listens on localhost (the default), no token or header is required; if you changed `mcpPort`, substitute your port. When the Admiral listens on any other address (for example in Docker), every MCP call needs a credential: add `--header "Authorization: Bearer <token>"` (or `X-Api-Key`). `armada mcp install --server <url> --token <token>` (or `--profile <name>`) writes the remote URL and the header for Claude Code, Gemini CLI, Cursor, Mux, and OpenCode, and configures Codex to read the token from `ARMADA_TOKEN`; without a remote target it writes the local, credential-less entries. See [REMOTE_SERVER.md](REMOTE_SERVER.md#mcp-clients-claude-code-codex-gemini-cli-cursor-mux-opencode).

**Enterprise-managed Claude Code.** If the add is rejected with `Cannot add MCP server 'armada': not allowed by enterprise policy`, your organization's Claude Code managed settings restrict which MCP servers may be added (via `allowedMcpServers` / `managed-mcp.json`). This is enforced by IT and **cannot** be overridden by a user, a project `.mcp.json`, or `--mcp-config`. Ask your Claude Code administrator to allow the Armada endpoint by adding it to `allowedMcpServers` in the managed settings - on Windows `C:\Program Files\ClaudeCode\managed-settings.json` (or, higher priority, the Claude.ai admin console at Admin Settings > Claude Code > Managed settings):

```json
{ "allowedMcpServers": [ { "serverUrl": "http://localhost:7891/mcp" } ] }
```

Run `/status` in Claude Code to see the active setting sources. If Claude Code stays locked down, the same standard HTTP endpoint works from any other MCP client that is not under that policy - `armada mcp install` also configures Codex, Gemini, and Cursor.

The legacy `/rpc` + `/events` (separate SSE) endpoints remain served for older clients but `/mcp` is preferred. MCP clients communicate using the standard MCP JSON-RPC protocol over HTTP. The server supports the full MCP tool-calling lifecycle:

1. **Initialize** - Client discovers server capabilities and available tools
2. **Call Tool** - Client invokes a tool with arguments
3. **Response** - Server returns the tool result

### Stdio Transport

Armada also supports an MCP stdio transport (`armada mcp stdio`) for direct process-based communication. Both transports register the same tool names via `McpToolRegistrar`. The stdio server runs standalone against the database (no Admiral process), so the tools that need the Admiral answer `ErrorCode` `Unavailable` over stdio: `stop_server`, and the fleet action tools (`create_fleet_action`, `update_fleet_action`, `delete_fleet_action`, `run_fleet_action`, `fleet_action_run_status`, `cancel_fleet_action_run`), whose runs are executed and tracked by the Admiral's runner. Use stdio when running Armada as a child process of an MCP client.

### Port Configuration

| Setting | Default | Description |
|---|---|---|
| `mcpPort` | `7891` | MCP HTTP server port |
| `rest.hostname` | `localhost` | Bind hostname |

Keys are the camelCase names used in `settings.json`. The MCP port can be configured in the Armada settings file. The hostname is shared with the REST API configuration.

---

## Authentication

MCP is **authenticated by default** (Decision D2 in V1_READINESS.md). Present a credential with the same headers the
REST API accepts: `Authorization: Bearer <token>`, `X-Token: <session token>`, or `X-Api-Key: <api key>`.

- A presented credential must be valid; an invalid or expired one gets HTTP `401` (it no longer falls back to an
  anonymous caller).
- A request **without** a credential is accepted only when all of these hold: `mcp.allowUnauthenticatedLoopback` is
  true (the default), the MCP listener's hostname (`rest.hostname`) is a loopback name (`localhost`, `127.0.0.1`,
  `::1`), and the caller connects from loopback. Such a call runs as the default tenant's tenant admin, which is
  what the local Claude Code setup in the README relies on. Otherwise it gets `401` with a `WWW-Authenticate` header.
  A reverse proxy on the same host connects from loopback, so behind one set `mcp.allowUnauthenticatedLoopback` to
  false (see [REMOTE_SERVER.md](REMOTE_SERVER.md#close-the-mcp-loopback-exemption-when-anything-proxies-to-it)).
- With a valid credential, the caller's tenant, user, and role flow into every tool handler through Voltaic's ambient
  `RpcCallContext` (`McpToolHelpers.ResolveCallerContext()`).

### Tool permissions

Every tool declares a requirement in `Armada.Core/Authorization/McpToolAuthorizationRegistry.cs`, checked on every
call against the caller (including Ask Armada proposals executed after approval):

| Level | Tools |
|---|---|
| Authenticated | Reads (`status`, `enumerate`, `get_*`, `list_*`, `mission_status`, `voyage_status`, logs, diffs, `inbox`, `vessel_health`, `search_memory`, `token_usage_summary`, ...), writes to caller-owned resources (memories, model endpoints, harbors), and `cli_permission_prompt` and `decide_cli_permission_request` (the handlers check the caller; see [CLI Permissions](#cli-permissions)) |
| TenantAdmin | Every other write or execution (dispatch, captains, missions, voyages, docks, merge queue, signals, events, objectives, backlog, personas, pipelines, prompt templates, playbooks, releases, deployments, runbooks, check runs, fleet actions, vessel import, vessel health) |
| AdminOnly | `backup`, `restore`, `stop_server` (a credential is required even on loopback; use `X-Api-Key` from `settings.json`) |

A denied call returns an `McpToolError` with `ErrorCode` `Forbidden` whose message names the required level. The full per-tool list is in
[SECURITY_REVIEW.md](SECURITY_REVIEW.md#mcp-tools); a test fails when a registered tool has no declaration.

### MCP Authentication Scope

Owned (Category A) entities - fleets, vessels, captains, missions, voyages, docks, signals, events, merge queue, objectives/backlog - are scoped to the authenticated caller across `enumerate` and the entity tools. Every other `enumerate` entity type uses the same scope as its REST list: jobs, releases, deployments and check runs by tenant and owner; model endpoints, harbors, personas, prompt templates, pipelines, playbooks, workflow profiles, project profiles and skills by tenant plus their per-object visibility (`Scope`); vessel import batches, fleet actions and vessel health by tenant. Only a global admin enumerates across tenants. Per-object ownership editing of configuration entities is enforced by their services.

### Mission-Scoped Captain Calls

When the Admiral launches a captain for a mission it mints a **mission-scoped session token** (setting
`Mcp.MissionScopedTokens`, default true) and binds the captain's Armada MCP connection to it, sent as `X-Token`:

- Claude Code, Codex, OpenCode and Mux captains get the same per-invocation binding as Ask turns (see below); no client
  file is written into the repository worktree. With `IsolateCaptainLaunch` on, the isolation plan carries the token
  for every CLI runtime (Gemini and Cursor included).
- API-endpoint captains receive `ARMADA_MCP_URL` / `ARMADA_MCP_TOKEN`.
- Harbor launches carry the token in `HarborLaunchRequest.McpSessionToken`; the Harbor binds it against the MCP URL the
  Admiral advertised in the handshake (a plain `http://host:port/mcp` URL; other forms get the token in the environment
  only).

The token authenticates as the mission's owner (tenant and user, with that user's role), only while the mission is
`Assigned` or `InProgress` on the captain it was minted for; after that, or when another captain takes the mission, the
MCP server answers `401`. Its one addition to the owner's permissions is `update_vessel_context` for the mission's own
vessel. Mission tokens are refused by the REST API, `/ws`, and the Harbor link. Gemini and Cursor mission captains
without `IsolateCaptainLaunch`, and every captain when `Mcp.MissionScopedTokens` is false, keep using their host MCP
configuration (the loopback identity above when it is allowed).

---

### Ask Armada Thread-Scoped Calls

When a captain answers in an Ask Armada conversation, the server mints a short-lived **thread-scoped session token** for
the thread owner (lifetime `ask.turnTimeoutMinutes` + 5 minutes) and gives it to the captain's MCP connection:
ApiEndpoint captains receive it through `ARMADA_MCP_URL` / `ARMADA_MCP_TOKEN` (sent as `X-Token`), and Claude Code
captains are launched with `--strict-mcp-config --mcp-config <per-launch file>` whose `armada` server entry carries
`"headers": { "X-Token": "<token>" }` (the file is written to a per-launch directory and deleted when the process exits).
Other CLI runtimes keep their host MCP configuration in thread turns and are not gated.

The MCP authentication handler validates the token like any session token and adds an `askThreadId` claim. Every tool
handler is registered through the Ask gate, which checks that claim:

- **Read-only tools** run normally: `status`, `enumerate`, `inbox`, `voyage_status`, `mission_status`,
  `fleet_action_run_status`, `vessel_health`, `papercut_summary`, `token_usage_summary`, `search_memory`,
  `evaluate_autoland`, every `get_*` reader (`get_backlog_item`, `get_backlog_planning_session`,
  `get_backlog_refinement_session`, `get_captain`, `get_captain_log`, `get_captain_tools`, `get_check_run`,
  `get_deployment`, `get_dock`, `get_fleet`, `get_harbor`, `get_harbor_metrics`, `get_memory`, `get_merge_entry`, `get_mission_diff`,
  `get_mission_log`, `get_model_endpoint`, `get_objective`, `get_persona`, `get_pipeline`, `get_playbook`,
  `get_prompt_template`, `get_release`, `get_runbook`, `get_runbook_execution`, `get_vessel`), and the `list_*` readers
  (`list_backlog`, `list_backlog_refinement_sessions`, `list_objectives`, `list_prompt_templates`,
  `list_cli_permission_requests`, `list_cli_permission_rules`, plus `get_cli_permission_request`). The list lives in
  `AskToolPolicy`; any tool not on it (including tools added later) is treated as state-changing.
- **`cli_permission_prompt`** (the captain CLI's own permission prompt) runs without becoming a proposal: an approver
  answers it as a [CLI permission request](#cli-permissions).
- **CLI permission decisions, rules, and policies** (`decide_cli_permission_request`, `create_cli_permission_rule`,
  `update_cli_permission_rule`, `delete_cli_permission_rule`, `set_captain_cli_permission_policy`) are refused with
  `ErrorCode` `Forbidden` and never proposed, so a captain cannot get its own prompts approved.
- **Any other tool**, when the thread's `AutoApprove` is off, is **not executed**. A `Pending` proposal and an
  `ActionProposal` message are created, `ask.proposal` is sent to the owner, and the tool returns this text to the
  captain:

  ```
  Proposed as aap_<id> and waiting for the user's approval in this conversation. Do not retry; tell the user what you proposed.
  ```

- With `AutoApprove` on, the tool runs immediately (its real result goes back to the captain) and is recorded as an
  `Executed` proposal with an `ActionResult` message.

Approving a proposal (`POST /api/v1/ask/threads/{id}/proposals/{pid}/approve`) executes the stored tool call in-process
through the same registered handler, under an ambient caller context for the approving user, so validation and tenant
scoping are identical to a direct MCP call by that user. A thread-scoped token whose thread no longer exists, or belongs
to a different user, gets `{ "Error": "The conversation for this session no longer exists.", "ErrorCode": "NotFound" }`. Thread-scoped tokens are
refused by the REST API and `/ws`. Calls without the claim (normal MCP clients, `armada mcp stdio`) are unaffected.

## Error Responses

When an MCP tool encounters an error, it returns a JSON object with a machine-readable `ErrorCode` and an English `Error` message. Branch on `ErrorCode`; the message is for people and models and may change.

```json
{
  "Error": "Vessel not found",
  "ErrorCode": "NotFound",
  "Code": null,
  "StatusCode": null
}
```

| `ErrorCode` | Meaning |
|---|---|
| `NotFound` | The referenced entity does not exist or is not visible to the caller (including another tenant's entities) |
| `InvalidArgument` | An argument is missing, malformed, or out of range |
| `Conflict` | The entity's state does not allow the operation (for example deleting a working captain), or it already exists (a value that must be unique is taken: `Code` `DuplicateEntity`, see below) |
| `Forbidden` | The caller lacks the permission the operation needs |
| `Unavailable` | A service the operation needs is not configured or not available (for example no saved diff) |
| `Failed` | Any other failure |

`Code` carries a feature-specific detail code where a tool has one (for example the vessel import codes such as `BatchNotFound` or `PathNotAllowed`), and `StatusCode` keeps the HTTP-equivalent status the fleet action tools have always returned. When a tool's service signals a missing entity, bad input, a state conflict, a missing permission, or an unavailable feature, the server maps the exception by type to the same JSON object (`NotFound`, `InvalidArgument`, `Conflict`, `Forbidden`, `Unavailable`). A call the tool authorization gate refuses (the caller lacks the tool's declared permission level) returns the same object with `ErrorCode` `Forbidden`. Any other unexpected handler error and calls refused by the per-client rate limit (`mcp.toolCallsPerSecond`, default 100 per second, 0 for no limit) come back as MCP tool results with `isError: true` instead.

### Duplicate entities

A create or update whose name, email, or file name is already taken returns `ErrorCode` `Conflict` with `Code`
`DuplicateEntity` and a message naming the field:

```json
{
  "Error": "A captain named 'claude-1' already exists.",
  "ErrorCode": "Conflict",
  "Code": "DuplicateEntity"
}
```

The tools check before writing: `create_fleet` / `update_fleet`, `add_vessel` / `update_vessel`, `create_captain` /
`update_captain` (names unique within the caller's tenant), `create_persona`, `create_pipeline` (names unique within the
tenant), `create_prompt_template` (names unique on the server, built-in names included), and `create_playbook` /
`update_playbook` (file names unique within the tenant). Any other unique-constraint violation the database reports on
any tool (for example two concurrent creates of one name) is the same `Conflict` / `DuplicateEntity` with a generic
message such as `A persona with the same name or ID already exists.`; database provider text is never returned. The
REST equivalent is `409 Conflict` with `Data.Code` `DuplicateEntity` (see
[REST_API.md](REST_API.md#duplicate-entities)).

MCP tools do not return HTTP status codes (MCP uses JSON-RPC, not HTTP). The presence of an `ErrorCode` field (or a result with `isError: true`) indicates failure. On success, the response contains the requested data (entity object, status, list, etc.) without an `Error` field.

The stdio transport has no network attack surface -- the only caller is the parent process that spawned Armada -- so it runs anonymously under the default tenant-admin context. The HTTP MCP transport requires a credential except for loopback callers of a loopback-bound listener (see [Authentication](#authentication)).

---

## Tools

### status

Get aggregate status of active work in Armada: captain counts by state, mission counts by status, active voyages with
progress, and recent signals. A global administrator sees every tenant; any other caller sees only its own tenant (the
same scoping as `GET /api/v1/status`).

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
  "TotalCaptains": 4,
  "IdleCaptains": 1,
  "WorkingCaptains": 2,
  "StalledCaptains": 1,
  "ActiveVoyages": 2,
  "MemoryPressureDeferrals": 0,
  "MissionsByStatus": {
    "Pending": 3,
    "InProgress": 2,
    "Complete": 10,
    "Failed": 1
  },
  "Voyages": [
    {
      "Voyage": { "Id": "vyg_...", "Title": "Feature batch 1", "...": "..." },
      "TotalMissions": 5,
      "CompletedMissions": 3,
      "FailedMissions": 0,
      "InProgressMissions": 2,
      "VesselIds": ["vsl_..."]
    }
  ],
  "RecentSignals": [],
  "TimestampUtc": "2026-03-07T12:34:56.789Z",
  "RemoteTunnel": {
    "Enabled": false,
    "State": "Disabled",
    "TunnelUrl": null,
    "InstanceId": "armada-1f2e3d4c5b6a",
    "LastError": null,
    "ReconnectAttempts": 0,
    "LatencyMs": null,
    "CapabilityManifest": {
      "ProtocolVersion": "2026-04-03",
      "ArmadaVersion": "1.0.0",
      "Features": [
        "remoteControl.handshake",
        "remoteControl.heartbeat",
        "status.health",
        "status.snapshot",
        "settings.remoteControl"
      ]
    }
  }
}
```

---

### inbox

Return the operator's inbox: everything across the fleet that requires a human's attention or action right now, ordered most-urgent first. Call this to answer a user asking *"Is there anything waiting on me?"*, *"Is there anything that needs my attention?"*, or *"Do I have any action items from Armada work?"*.

**What qualifies.** Two kinds of item appear:

- **Awaiting your decision (human-in-the-loop):** a mission in `Review` (approve or reject), a deployment in `PendingApproval`, a pending Ask Armada action proposal in one of your own conversations, or a pending CLI permission request (a captain waiting to run one of its own tools, such as a shell command).
- **Failed and needs intervention (human-out-of-the-loop):** a failed mission, a mission whose work could not be merged (landing failed), a failed merge, a failed or verification-failed deployment, or a stalled captain.

Purely informational events (completions, normal progress) are deliberately excluded -- the inbox answers *"what needs me?"*, not *"what happened?"* (use `enumerate` or the Activity log for history). An empty `items` list means nothing currently needs the operator. Operational items are scoped like other reads (global admin: everything; tenant admin: the tenant; regular user: own items); Ask proposals are always limited to the caller's own threads, and proposals older than `Ask.ProposalExpiryMinutes` are left out (the same rule as `GET /api/v1/inbox`). Each category is capped at 100 items, and items are ordered by severity (`Critical` first), then title.

**Item kinds and severity:**

| `Kind` | Meaning | Severity |
|---|---|---|
| `review` | Mission awaiting your review/approval | `Warning`, or `Critical` if the review deadline has passed |
| `landing_failed` | Mission produced work that could not be merged | `Critical` |
| `failed` | Mission failed | `Warning` |
| `merge_failed` | Queued merge failed testing or landing | `Critical` |
| `deployment_approval` | Deployment waiting for your approval before it runs | `Warning` |
| `deployment_failed` | Deployment failed or failed verification | `Critical` |
| `stalled_captain` | Captain is stalled and may need recovery or a dock reclaim | `Warning` |
| `ask_proposal` | Ask Armada action proposal waiting for your approval (`Href` is the conversation, `/ask/<threadId>`) | `Warning` |
| `cli_permission` | CLI permission request waiting for an approver; visible to global admins, the tenant's tenant admins, and the owner of the thread or mission (`Href` is `/ask/<threadId>` for the caller's own conversation, otherwise `/cli-permissions?request=<id>`) | `Warning` |

**Input Schema:**

```json
{
  "type": "object",
  "properties": {}
}
```

No parameters required.

**Response:** counts plus the ordered item list. The wrapper fields are camelCase (`count`, `criticalCount`,
`warningCount`, `items`); each item is an `InboxItem` with `Kind`, `Severity`, `Title`, `Detail`, `EntityType`,
`EntityName` (display name of the referenced entity; use it instead of parsing `Title`), `EntityId`, and a dashboard
`Href`. Deployment items also carry `EnvironmentName` and `DeploymentTitle`, and their `Title` reads
"Deploy to <environment>: <title>". `cli_permission` items carry `CliPermission` (the request, with the caller's
`CanDecide`) and `ExpiresUtc`. `Severity` is `Critical`, `Warning`, or `Info`.

```json
{
  "count": 2,
  "criticalCount": 1,
  "warningCount": 1,
  "items": [
    {
      "Kind": "landing_failed",
      "Severity": "Critical",
      "Title": "Landing failed: Add JWT validation",
      "Detail": "The work could not be landed.",
      "EntityType": "mission",
      "EntityName": "Add JWT validation",
      "EntityId": "msn_1a2b3c",
      "Href": "/missions/msn_1a2b3c"
    },
    {
      "Kind": "review",
      "Severity": "Warning",
      "Title": "Review: Refactor auth service",
      "Detail": "Awaiting your review.",
      "EntityType": "mission",
      "EntityName": "Refactor auth service",
      "EntityId": "msn_4d5e6f",
      "Href": "/missions/msn_4d5e6f"
    }
  ]
}
```

> Also exposed over REST as `GET /api/v1/inbox` and in the CLI as `armada inbox`.

---

### token_usage_summary

Summarize model token usage over a time window. Returns time buckets (each with a per-model breakdown), a whole-window per-model aggregate ordered most-used first, and grand totals. Use it to answer *"how many tokens has each model used?"* or *"what's our token usage over the last week?"*.

Counts are normalized across providers: `input` covers prompt tokens, `output` covers completion tokens, `cached` is the cache-read subset of input (informational), and `total` is input + output. Counts are measured where the runtime reports usage (for example Claude Code) and estimated from text length otherwise; `EstimatedCount` reports how many aggregated records were estimated.

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
    "captainId": { "type": "string", "description": "Filter to one captain (cpt_ prefix)" },
    "harborId": { "type": "string", "description": "Filter to work that ran on one Harbor (hbr_ prefix)" }
  }
}
```

**Response:** grand totals plus `Buckets` (time series) and `ByModel` (most-used first).

```json
{
  "FromUtc": "2026-05-01T00:00:00Z",
  "ToUtc": "2026-05-08T00:00:00Z",
  "BucketMinutes": 60,
  "RecordCount": 42,
  "EstimatedCount": 18,
  "InputTokens": 1200000,
  "OutputTokens": 340000,
  "CachedTokens": 90000,
  "TotalTokens": 1540000,
  "ByModel": [
    { "Model": "claude-sonnet-4", "InputTokens": 900000, "OutputTokens": 250000, "CachedTokens": 80000, "TotalTokens": 1150000 },
    { "Model": "gpt-5", "InputTokens": 300000, "OutputTokens": 90000, "CachedTokens": 10000, "TotalTokens": 390000 }
  ],
  "Buckets": [
    {
      "BucketStartUtc": "2026-05-01T00:00:00Z",
      "BucketEndUtc": "2026-05-01T01:00:00Z",
      "InputTokens": 20000, "OutputTokens": 5000, "CachedTokens": 1000, "TotalTokens": 25000,
      "Models": [
        { "Model": "claude-sonnet-4", "InputTokens": 20000, "OutputTokens": 5000, "CachedTokens": 1000, "TotalTokens": 25000 }
      ]
    }
  ]
}
```

> Also exposed over REST as `GET /api/v1/token-usage/summary`.

---

### papercut_summary

List friction that captains reported during missions ("papercuts"), collapsed into groups of the same vessel, category, and problem. Use the count and distinct-captain count to tell a one-off from a real defect, then promote a group to a backlog item or objective. All parameters are optional.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "vesselId": { "type": "string", "description": "Filter to one vessel (vsl_ prefix)" },
    "category": { "type": "string", "description": "Filter to one category: BriefContradiction, ToolFailure, MissingDoc, BrokenLink, RepoFriction, TestFlake, EnvSetup, PlatformBug, Other" },
    "minSeverity": { "type": "string", "description": "Minimum severity: Low, Medium, or High" },
    "sinceHours": { "type": "integer", "description": "Only include reports newer than this many hours" },
    "ungrouped": { "type": "boolean", "description": "Return every report instead of groups (default false)" },
    "limit": { "type": "integer", "description": "Maximum rows returned (default 25, maximum 200)" },
    "scanLimit": { "type": "integer", "description": "Stored reports to scan before filtering (default 500, maximum 5000)" }
  }
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `vesselId` | string | No | Filter to one vessel (prefix `vsl_`) |
| `category` | string | No | Filter to one category: `BriefContradiction`, `ToolFailure`, `MissingDoc`, `BrokenLink`, `RepoFriction`, `TestFlake`, `EnvSetup`, `PlatformBug`, `Other`. An unknown name returns an error. |
| `minSeverity` | string | No | Minimum severity to include: `Low`, `Medium`, or `High`. An unknown name returns an error. |
| `sinceHours` | integer | No | Only include reports newer than this many hours |
| `ungrouped` | boolean | No | Return every matching report instead of collapsing repeats into groups (default `false`) |
| `limit` | integer | No | Maximum rows returned (default 25, clamped to [1, 200]) |
| `scanLimit` | integer | No | Number of stored reports to scan before filtering (default 500, clamped to [1, 5000]) |

**Response (grouped, default):** `Scanned`, `Matched`, `GroupCount`, and a `Groups` array.

```json
{
  "Scanned": 500,
  "Matched": 42,
  "GroupCount": 7,
  "Groups": [ { "...": "..." } ]
}
```

**Response (with `ungrouped: true`):** `Scanned`, `Matched`, and a `Papercuts` array (most-recently-reported first).

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

> **Note:** Over stdio (`armada mcp stdio`) the tool is listed but answers `ErrorCode` `Unavailable`: there is no Admiral process to stop (use `armada server stop`). Over HTTP it requires an admin credential (for example `X-Api-Key`), even on loopback.

---

### enumerate

Paginated enumeration of any entity type with filtering and sorting. This is the MCP equivalent of the `POST /api/v1/{entity}/enumerate` REST endpoints. Returns paginated results with total counts, page metadata, and query timing. Supports: objectives (aliases `backlog`, `backlog_item`, `backlog_items`), fleets, vessels, captains, missions, voyages, docks, signals, events, merge_queue, harbors, playbooks, personas, memories, prompt_templates, pipelines, workflow_profiles, project_profiles, skills, check_runs, releases, deployments, incidents, runbooks, runbook_executions, jobs, model_endpoints, vessel_import_batch, fleet_action, fleet_action_run, fleet_action_run_target, vessel_health.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "entityType": { "type": "string", "description": "Entity type to enumerate (objectives [aliases backlog, backlog_item, backlog_items], fleets, vessels, captains, missions, voyages, docks, signals, events, merge_queue, harbors, playbooks, personas, memories, prompt_templates, pipelines, workflow_profiles, project_profiles, skills, check_runs, releases, deployments, incidents, runbooks, runbook_executions, jobs, model_endpoints, vessel_import_batch, fleet_action, fleet_action_run, fleet_action_run_target, vessel_health)" },
    "pageNumber": { "type": "integer", "description": "Page number (1-based, default 1)" },
    "pageSize": { "type": "integer", "description": "Results per page (default 10, max 1000)" },
    "order": { "type": "string", "description": "Sort order: CreatedAscending, CreatedDescending" },
    "createdAfter": { "type": "string", "description": "ISO 8601 timestamp filter" },
    "createdBefore": { "type": "string", "description": "ISO 8601 timestamp filter" },
    "status": { "type": "string", "description": "Filter by status (entity-specific)" },
    "search": { "type": "string", "description": "Free-text search where supported (releases, deployments, incidents, runbooks, runbook_executions, memories, objectives; vessel name for vessel_health)" },
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
    "includeMessage": { "type": "boolean", "description": "Include full Message on signals (default false). Returns messageLength hint when false." },
    "runId": { "type": "string", "description": "Fleet action run ID (far_ prefix); required for fleet_action_run_target" },
    "includeOutput": { "type": "boolean", "description": "Include RenderedText, OutputText and ErrorText on fleet_action_run_target (default false). Returns outputLength, errorLength and renderedLength hints when false." },
    "includeInactive": { "type": "boolean", "description": "Include soft-deleted built-in actions for fleet_action (default false)" }
  },
  "required": ["entityType"]
}
```

| `entityType` value | Supported filters |
|---|---|
| `objectives` (aliases `backlog`, `backlog_item`, `backlog_items`) | `status`, `vesselId`, `voyageId`, `missionId`, `search` |
| `fleets` | `createdAfter`, `createdBefore` |
| `vessels` | `fleetId`, `createdAfter`, `createdBefore` |
| `captains` | `status` (Idle/Working/Planning/Refining/Stalled/Stopping/Quarantined/Analyzing), `createdAfter`, `createdBefore` |
| `missions` | `status`, `vesselId`, `captainId`, `voyageId`, `createdAfter`, `createdBefore` |
| `voyages` | `status` (Open/InProgress/Complete/Failed/Cancelled), `createdAfter`, `createdBefore` |
| `docks` | `vesselId`, `createdAfter`, `createdBefore` |
| `signals` | `signalType`, `captainId`, `toCaptainId`, `unreadOnly`, `createdAfter`, `createdBefore` |
| `events` | `eventType`, `captainId`, `missionId`, `vesselId`, `voyageId`, `createdAfter`, `createdBefore` |
| `merge_queue` | `status` (Queued/Testing/Passed/Failed/Landed/Cancelled), `createdAfter`, `createdBefore` |
| `harbors` | paginated browse only (no filters) |
| `personas` | `createdAfter`, `createdBefore` |
| `playbooks` | `createdAfter`, `createdBefore` |
| `prompt_templates` | `createdAfter`, `createdBefore` |
| `pipelines` | `createdAfter`, `createdBefore` |
| `workflow_profiles` | `createdAfter`, `createdBefore` (current MCP enumeration is primarily paginated browse) |
| `check_runs` | `createdAfter`, `createdBefore` (current MCP enumeration is primarily paginated browse) |
| `releases` | `status`, `vesselId`, `search`, `createdAfter`, `createdBefore` |
| `deployments` | `status`, `vesselId`, `missionId`, `voyageId`, `search`, `createdAfter`, `createdBefore` |
| `incidents` | `vesselId`, `missionId`, `voyageId`, `search` |
| `runbooks` | `search` (current MCP enumeration is primarily paginated browse) |
| `runbook_executions` (aliases `runbook-executions`, `runbookexecution`) | `search` (current MCP enumeration is primarily paginated browse) |
| `project_profiles` | `createdAfter`, `createdBefore` (current MCP enumeration is primarily paginated browse) |
| `skills` | `createdAfter`, `createdBefore` (current MCP enumeration is primarily paginated browse) |
| `memories` | `search` (plus paginated browse) |
| `jobs` | (paginated browse only) |
| `model_endpoints` | `createdAfter`, `createdBefore` (current MCP enumeration is primarily paginated browse) |
| `vessel_import_batch` (aliases `vessel_import_batches`, `vessel-import-batch`, `import_batches`) | `status` (Discovered/Importing/Completed/CompletedWithFailures/Failed/Discovering), `createdAfter`, `createdBefore`, `order`. Scoped to the caller's tenant. Items are not included; read a batch with items through `GET /api/v1/vessels/import/batches/{id}`. |
| `fleet_action` | `includeInactive`, `createdAfter`, `createdBefore` |
| `fleet_action_run` | `status` (Pending/Running/Completed/CompletedWithFailures/Cancelled/Failed), `createdAfter`, `createdBefore` |
| `fleet_action_run_target` | `runId` (required), `status` (Pending/Skipped/Running/Succeeded/Failed/Cancelled/TimedOut) |
| `vessel_health` (aliases `vessel-health`, `health`) | `status` (overall status: Pass/Warn/Fail/NotApplicable/Unknown), `fleetId`, `search` (vessel name substring). Every active vessel appears, including never-evaluated ones. Rows are sorted by vessel name; for richer filters and sorting use `POST /api/v1/vessel-health/enumerate` |

| Include flag | Applies to | Default | Description |
|---|---|---|---|
| `includeDescription` | missions, voyages | `false` | Include full Description. Returns `descriptionLength` hint when false. |
| `includeContext` | vessels | `false` | Include ProjectContext and StyleGuide. Returns length hints when false. |
| `includeTestOutput` | merge_queue | `false` | Include TestOutput. Returns `testOutputLength` hint when false. |
| `includePayload` | events | `false` | Include full Payload. Returns `payloadLength` hint when false. |
| `includeMessage` | signals | `false` | Include full Message. Returns `messageLength` hint when false. |
| `includeOutput` | fleet_action_run_target | `false` | Include RenderedText, OutputText and ErrorText. Returns `outputLength`, `errorLength` and `renderedLength` hints when false. |

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

## Fleet Actions

Fleet action tools apply one action across many vessels. A **Command** action runs `commandText` in each vessel's working directory (tenant admin only); a **Mission** action dispatches one voyage per vessel from `promptTemplate`. All tools act in the caller's tenant. Errors come back as `{ "Error": "...", "ErrorCode": "...", "StatusCode": 400|403|404|409 }` rather than as thrown tool errors (`ErrorCode` `InvalidArgument`, `Forbidden`, `NotFound`, or `Conflict` respectively; `Unavailable` without a `StatusCode` when fleet actions are not available on the server). These tools never return captured output; use `enumerate` with `entityType` `fleet_action_run_target`, a `runId`, and `includeOutput: true` when you need it. See [FLEET_ACTIONS.md](FLEET_ACTIONS.md) for behavior and the REST equivalents in [REST_API.md](REST_API.md#fleet-actions).

Templates may use `{{vessel.name}}`, `{{vessel.id}}`, `{{vessel.defaultBranch}}`, `{{vessel.workingDirectory}}`, `{{vessel.buildCommand}}` and `{{health.summary}}`. Any other `{{name}}` is rejected and the error names it.

### create_fleet_action

Create a reusable action.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `name` | string | Yes | Display name (1-200 characters) |
| `description` | string | No | Description |
| `kind` | string | No | `Command` (default) or `Mission` |
| `commandText` | string | Command | Shell command template |
| `promptTemplate` | string | Mission | Prompt template |
| `pipelineId` | string | No | Pipeline for Mission dispatch |
| `persona` | string | No | Persona name (stored; not yet applied at dispatch) |
| `timeoutSeconds` | int | No | Per-target timeout, 5-7200 (default from settings) |
| `defaultConcurrency` | int | No | 1-32, default 4 |
| `requiresCleanWorkingTree` | bool | No | Skip dirty working trees; default true for Command |

**Response:** the created [FleetAction](REST_API.md#fleet-action-models).

### update_fleet_action

Partial update; only supplied fields change. Same parameters as `create_fleet_action` plus required `actionId` (`fac_`). An empty string clears `description`, `pipelineId` and `persona`.

**Response:** the updated FleetAction.

### delete_fleet_action

| Parameter | Type | Required | Description |
|---|---|---|---|
| `actionId` | string | Yes | Fleet action ID (`fac_`) |

Built-in actions are soft-deleted and never re-seeded. **Response:** `{ "Deleted": true, "ActionId": "fac_..." }`.

### run_fleet_action

Start a run. Pass `actionId` for a saved action, or the inline definition fields for an ad hoc run.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `vesselIds` | string[] | Yes | Target vessel IDs in the caller's tenant (1-500). One unknown or cross-tenant vessel rejects the whole request (404). |
| `actionId` | string | No | Saved action ID (`fac_`). Omit for ad hoc. |
| `concurrency` | int | No | 1-32; defaults to the action's `defaultConcurrency` |
| `name`, `kind`, `commandText`, `promptTemplate` | string | Ad hoc | Inline definition |
| `pipelineId`, `timeoutSeconds`, `requiresCleanWorkingTree` | | No | Overrides for a saved action, or part of the ad hoc definition |

**Response:**

```json
{ "RunId": "far_...", "ActionId": "fac_...", "Kind": "Mission", "Status": "Pending", "TargetCount": 5, "Concurrency": 2 }
```

### fleet_action_run_status

| Parameter | Type | Required | Description |
|---|---|---|---|
| `runId` | string | Yes | Run ID (`far_`) |

**Response:** `{ "Run": FleetActionRun, "Targets": [FleetActionRunTargetSummary, ...] }`. Target summaries carry `Status`, `SkipReason`, `FailureReason`, `ExitCode`, `VoyageId`, `DurationMs`, and `OutputLength` / `ErrorLength` / `RenderedLength` hints instead of text.

### cancel_fleet_action_run

| Parameter | Type | Required | Description |
|---|---|---|---|
| `runId` | string | Yes | Run ID (`far_`) |

Pending targets are cancelled, running commands are killed, and unlanded voyages of a Mission run are cancelled. **Response:** the updated FleetActionRun. Returns an error with `StatusCode` 409 when the run already finished.

---

## Memory

Durable agent memory. The Recorder persona uses these tools to persist and consolidate what a voyage produced; other personas use `search_memory` to recall it. Memories are classified as **Episodic** (what happened), **Semantic** (standalone facts), or **Procedural** (how-to). Working memory is never stored. All memory tools are scoped to the authenticated caller.

### search_memory

Search memories (use before writing, to consolidate against existing ones). Ordered by salience, newest first.

- `search` (string) -- substring across content, topic, tags
- `type` (string) -- `Episodic`, `Semantic`, or `Procedural`
- `topic` (string) -- exact topic/grouping
- `vesselId` (string) -- associated or originating vessel
- `pageNumber`, `pageSize` (integer)

Returns a paged `EnumerationResult` of memories.

### get_memory

Read one memory (full content). Args: `memoryId` (required).

### create_memory

Create a memory, or update it in place when a memory with the same `key` already exists (idempotent consolidation). Args: `content` (required); optional `type` (default `Semantic`), `topic`, `key`, `summary`, `salience` (0.0-1.0, default 0.5), `tags`, `sourceKind`, `sourceVoyageId`, `sourceMissionId`, `sourceVesselId`, `sourceDetail`, `vesselId`, `scope`.

### update_memory

Update an existing memory by id; only supplied fields change; increments `version`. Args: `memoryId` (required), then any of `type`, `topic`, `key`, `summary`, `content`, `salience`, `tags`, `vesselId`, `sourceDetail`, `scope`.

### delete_memory

Delete a memory that has become stale or is no longer relevant. Args: `memoryId` (required).

---

## CLI Permissions

CLI tool permissions govern a CLI captain's own tools (shell commands, web fetches, file tools outside the accepted
edits), not Armada's MCP tools. Under the `ApproveInArmada` policy a Claude Code captain sends each permission prompt to
`cli_permission_prompt`, which creates a CLI permission request (`cpr_`) that an approver allows or denies; a matching
rule (`cpl_`) decides without asking. The REST equivalents, the request and rule shapes, the rule syntax, and the
access rules are in [REST_API.md](REST_API.md#cli-permissions); the policy resolution and per-runtime flags are in
[CAPTAINS.md](CAPTAINS.md#cli-tool-permissions). Errors are `McpToolError`s: `NotFound` (missing, or not visible to the
caller), `Forbidden` (the caller may not act), `InvalidArgument` (bad enum value or rule pattern), `Conflict` (the
request is no longer pending), and `Unavailable` on the stdio server, which registers the tools without the service.

Who may call what:

| Tool | Caller |
|---|---|
| `cli_permission_prompt` | Only a captain's own mission- or Ask-thread-scoped session (a presented session token); every other caller gets `Forbidden` |
| `list_cli_permission_requests`, `get_cli_permission_request` | Any authenticated person: global admins see every request, tenant admins their tenant, other users requests from their own Ask threads and missions. Captain sessions get an empty list (and `NotFound` by id) |
| `decide_cli_permission_request` | A global admin, a tenant admin of the request's tenant, or the owner when `Permissions.AllowOwnerApproval` is true; `AllowAndRemember` needs an admin. Requires a presented credential: captain sessions and the unauthenticated loopback identity get `Forbidden`, so a captain can never approve its own prompt |
| `list_cli_permission_rules` | Any authenticated person (the caller's tenant plus rules for every tenant; captain sessions get an empty list) |
| `create_cli_permission_rule`, `update_cli_permission_rule`, `delete_cli_permission_rule` | TenantAdmin: global admins (any tenant, or every tenant), tenant admins (their own tenant) |
| `set_captain_cli_permission_policy` | TenantAdmin: a global admin or a tenant admin of the captain's tenant; never a captain session |

In an Ask conversation, `cli_permission_prompt` bypasses the proposal gate (an approver answers it instead), the three
readers run like other read-only tools, and the decide, rule, and policy tools are refused outright with `Forbidden`
(never proposed), so a captain cannot get its own prompts approved through a proposal.

### cli_permission_prompt

The permission prompt tool for CLI captains: the target of Claude Code's `--permission-prompt-tool
mcp__armada__cli_permission_prompt`. Armada adds that flag itself on `ApproveInArmada` launches; people never call this
tool.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `tool_name` | string | Yes | Tool that needs permission (for example `Bash`); at most 256 characters |
| `input` | object | Yes | The tool's input as the CLI sent it |
| `tool_use_id` | string | No | Tool use identifier |

The call resolves the session (the Ask thread, checked against the token's owner, or the mission with its voyage,
vessel, and dock) and evaluates the applicable rules (the tenant's `Global` rules and rules for every tenant, the
mission's `Vessel` rules, the captain's `Captain` rules). A matching deny rule denies and a matching allow rule allows
at once; both are recorded. Otherwise a `Pending` request is stored (input redacted), a `CliPermission` card is posted
when the session is an Ask thread, `cli_permission.requested` is announced, and the call waits until an approver
decides, `Permissions.PromptTimeoutSeconds` passes (`Expired`), the turn or mission ends (`Cancelled`), or the caller
cancels the call (`Cancelled` at once: the server cancels the tool's handler when the client sends
`notifications/cancelled` for it, as Claude Code does when a tool call is interrupted). A connection that drops without a
cancel is resolved when the turn or mission ends, or by the timeout.

**Response:** the answer in the shape Claude Code reads from the tool result text. Allowed (the original input,
unchanged):

```json
{ "behavior": "allow", "updatedInput": { "command": "npm test", "description": "Run the tests" } }
```

Denied (by an approver, a rule, expiry, or cancellation):

```json
{ "behavior": "deny", "message": "An approver in Armada denied this Bash call (cpr_abc123). Reason: not on this branch. Do not retry the same call; continue without it or explain what you need." }
```

A session whose mission or thread no longer exists gets `NotFound`.

### list_cli_permission_requests

| Parameter | Type | Required | Description |
|---|---|---|---|
| `status` | string | No | `Pending`, `Allowed`, `Denied`, `Expired`, or `Cancelled` |
| `missionId` | string | No | Mission filter (`msn_`) |
| `threadId` | string | No | Ask thread filter (`ath_`) |
| `captainId` | string | No | Captain filter (`cpt_`) |
| `vesselId` | string | No | Vessel filter (`vsl_`) |
| `limit` | int | No | 1-1000, default 100 |

**Response:** `{ "count": 1, "requests": [CliPermissionRequest, ...] }`, newest first. Each request carries the tool
name, redacted `InputText`, `SummaryText`, `SuggestedRule`, captain, vessel, mission or thread, `Status`, `ExpiresUtc`,
and the caller's `CanDecide` and `CanRemember`.

### get_cli_permission_request

| Parameter | Type | Required | Description |
|---|---|---|---|
| `requestId` | string | Yes | Request ID (`cpr_`) |

**Response:** the [CliPermissionRequest](REST_API.md#clipermissionrequest).

### decide_cli_permission_request

Allow once, allow and remember, or deny a pending request; the waiting captain continues with the answer.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `requestId` | string | Yes | Request ID (`cpr_`) |
| `decision` | string | Yes | `AllowOnce`, `AllowAndRemember` (admins; also stores an allow rule), or `Deny` |
| `message` | string | No | Returned to the captain with a denial; recorded with an allow |
| `rulePattern` | string | No | Rule for `AllowAndRemember`, for example `Bash(git status:*)`; defaults to the request's `SuggestedRule` |
| `ruleScope` | string | No | `Global`, `Vessel`, or `Captain` (default `Captain`) for that rule |

**Response:** the decided CliPermissionRequest. `Conflict` when it is no longer pending.

### list_cli_permission_rules

| Parameter | Type | Required | Description |
|---|---|---|---|
| `scope` | string | No | `Global`, `Vessel`, or `Captain` |
| `vesselId` | string | No | Vessel filter (`vsl_`) |
| `captainId` | string | No | Captain filter (`cpt_`) |

**Response:** `{ "count": 1, "rules": [CliPermissionRule, ...] }`.

### create_cli_permission_rule

| Parameter | Type | Required | Description |
|---|---|---|---|
| `pattern` | string | Yes | Rule in Claude Code permission rule syntax, for example `Bash(npm run test:*)`, `WebFetch(domain:example.com)`, `Edit(src/**)` |
| `action` | string | Yes | `Allow` or `Deny` (deny rules win) |
| `scope` | string | No | `Global` (default; every captain of the tenant), `Vessel`, or `Captain` |
| `vesselId` | string | Vessel | Vessel for a `Vessel` rule |
| `captainId` | string | Captain | Captain for a `Captain` rule |
| `tenantId` | string | No | Tenant for a `Global` rule (global admins); omit for every tenant. A tenant admin's rules always belong to their tenant |
| `description` | string | No | Optional note |

A vessel or captain outside the caller's scope answers `NotFound`. **Response:** the created
[CliPermissionRule](REST_API.md#clipermissionrule).

### update_cli_permission_rule

| Parameter | Type | Required | Description |
|---|---|---|---|
| `ruleId` | string | Yes | Rule ID (`cpl_`) |
| `pattern` | string | Yes | Rule pattern |
| `action` | string | Yes | `Allow` or `Deny` |
| `description` | string | No | Optional note (omit to clear) |

Scope and target cannot change (delete and recreate). Rules for every tenant need a global admin. **Response:** the
updated CliPermissionRule.

### delete_cli_permission_rule

| Parameter | Type | Required | Description |
|---|---|---|---|
| `ruleId` | string | Yes | Rule ID (`cpl_`) |

**Response:** `{ "deleted": true, "ruleId": "cpl_..." }`.

### set_captain_cli_permission_policy

Set or clear a captain's CLI tool permission policy. `Bypass` runs the CLI with its permission-bypass flag (for example
`--dangerously-skip-permissions`): the captain can run any command as the Admiral's user. For Ask turns a captain-level
`Bypass` applies only when `Ask.CaptainAutoApprove` is true.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `captainId` | string | Yes | Captain ID (`cpt_`) |
| `policy` | string | No | `Refuse`, `ApproveInArmada`, or `Bypass`; omit to clear (inherit) |

**Response:** the [Captain](REST_API.md#captain) with `CliPermissionPolicy`.

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
    },
    "objectiveId": {
      "type": "string",
      "description": "Optional objective (obj_ prefix) to link this voyage to; must exist. Parity with REST dispatch."
    },
    "captainAssignments": {
      "type": "array",
      "description": "Optional per-persona captain overrides. Each entry binds a pipeline step (persona) to a preferred captain and a fallback tier.",
      "items": {
        "type": "object",
        "properties": {
          "persona": { "type": "string", "description": "Persona name the override applies to (e.g. Worker, Architect, Judge)" },
          "captainId": { "type": "string", "description": "Preferred captain id (cpt_ prefix), or omit for tier-only routing" },
          "fallbackTier": { "type": "string", "description": "Fallback tier when the preferred captain is busy: Economy, Standard, or Premium" }
        },
        "required": ["persona"]
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
| `captainAssignments` | array | No | Per-persona captain overrides. Array of `{ persona` (required)`, captainId, fallbackTier` (`Economy`/`Standard`/`Premium`)` }` -- binds a pipeline step (persona) to a preferred captain and a fallback tier. |

> **Parity with REST.** This tool and `POST /api/v1/voyages` funnel through the same validation: a linked
> objective must exist, a pipeline name must resolve, and a request with no vessel or no missions is created
> as a **bare voyage** rather than dispatched. Both surfaces accept and reject the same inputs; only the error
> shape differs (structured `{ "Error": ..., "ErrorCode": ... }` here, HTTP status codes over REST).

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
| `summary` | boolean | No | `true` | Return the summary shape with mission counts by status. Set `false` for the full voyage |
| `includeMissions` | boolean | No | `false` | Include full mission objects. Only used when `summary` is `false` |
| `includeDescription` | boolean | No | `false` | Include `Description` on the embedded missions |
| `includeDiffs` | boolean | No | `false` | Include `DiffSnapshot` on the embedded missions |
| `includeLogs` | boolean | No | `false` | Reserved; currently has no effect (logs live in files, use `get_mission_log`) |

**Response (default summary mode):** the voyage's `Id`, `Title`, `Description`, `Status`, `CreatedUtc`, and
`LastUpdateUtc`, the mission total, and counts by mission status.

```json
{
  "Voyage": {
    "Id": "vyg_abc123def456ghi789jk",
    "Title": "Implement authentication",
    "Description": null,
    "Status": "InProgress",
    "CreatedUtc": "2026-03-07T12:00:00Z",
    "LastUpdateUtc": "2026-03-07T12:30:00Z"
  },
  "TotalMissions": 5,
  "MissionCountsByStatus": {
    "Pending": 1,
    "InProgress": 2,
    "Complete": 2
  }
}
```

**Response (`summary: false`):** `{ "Voyage": <full Voyage>, "TotalMissions": 5 }`.

**Response (`summary: false, includeMissions: true`):**

```json
{
  "Voyage": {
    "Id": "vyg_abc123def456ghi789jk",
    "Title": "Implement authentication",
    "Status": "InProgress",
    "...": "..."
  },
  "Missions": [
    {
      "Id": "msn_abc123def456ghi789jk",
      "Title": "Add JWT middleware",
      "Status": "Complete",
      "...": "..."
    },
    {
      "Id": "msn_def456ghi789jkl012mn",
      "Title": "Add login endpoint",
      "Status": "InProgress",
      "...": "..."
    }
  ]
}
```

| Field | Type | Description |
|---|---|---|
| `Voyage` | object | [Voyage](#voyage) object (a subset of its fields in summary mode) |
| `TotalMissions` | int | Total number of missions in this voyage (absent when `Missions` is returned) |
| `MissionCountsByStatus` | object | Map of status string to count (summary mode only) |
| `Missions` | array | List of [Mission](#mission) objects (only with `summary: false` and `includeMissions: true`) |

An unknown voyage, or one outside the caller's scope, returns `{ "Error": "Voyage not found", "ErrorCode": "NotFound" }`.

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

**Response:** [Mission](#mission) object, or `{ "Error": "Mission not found", "ErrorCode": "NotFound" }` if the ID does not exist.

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

**Response:** `{ "Land": true }` or `{ "Land": false, "HoldReason": "..." }`, or `{ "Error": "Mission not found", "ErrorCode": "NotFound" }` / `{ "Error": "Mission does not have an associated vessel", "ErrorCode": "Conflict" }`.

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
  "Fleet": {
    "Id": "flt_abc123def456ghi789jk",
    "Name": "Backend Services",
    "...": "..."
  },
  "Vessels": [
    {
      "Id": "vsl_abc123def456ghi789jk",
      "Name": "auth-service",
      "RepoUrl": "git@github.com:org/auth-service.git",
      "...": "..."
    }
  ]
}
```

Returns `{ "Error": "Fleet not found", "ErrorCode": "NotFound" }` if the ID does not exist.

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
      "description": "Optional local directory where completed mission changes will be pulled after merge. When repoUrl is a local clone (a file:// URL or an existing local path) and this is omitted, it is set automatically to that local clone so the vessel is immediately usable (e.g. for Rebuild Armada)."
    },
    "gitHubTokenOverride": {
      "type": "string",
      "description": "Optional per-vessel GitHub token override. Leave unset to use the global configured token."
    },
    "allowConcurrentMissions": {
      "type": "boolean",
      "description": "Allow multiple concurrent missions on this vessel (default false)"
    },
    "autoApprove": {
      "type": "boolean",
      "description": "Per-vessel auto-approve override for missions on this vessel: true or false wins over the captain's setting; omit to use the captain's setting"
    },
    "enableModelContext": {
      "type": "boolean",
      "description": "Enable model context accumulation -- agents will update context with key information discovered during missions (default true)"
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
| `workingDirectory` | string | No | Optional local directory where completed mission changes will be pulled after merge. When `repoUrl` is a local clone (a `file://` URL or an existing local path) and this is omitted, it is set automatically to that local clone so the vessel is immediately usable (e.g. for Rebuild Armada). |
| `gitHubTokenOverride` | string | No | Optional per-vessel GitHub token override. The raw token is accepted on create but never returned by MCP reads. |
| `allowConcurrentMissions` | boolean | No | Allow multiple concurrent missions on this vessel (default false) |
| `autoApprove` | boolean | No | Per-vessel auto-approve override for missions on this vessel: true or false wins over the captain's setting; omit to use the captain's setting |
| `enableModelContext` | boolean | No | Enable model context accumulation (default true) |
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

**Response:** The newly created [Vessel](#vessel) object. `repoUrl` is required by the input schema. An unknown or invisible `fleetId` returns `{ "Error": "Fleet not found", "ErrorCode": "NotFound" }`. `LocalPath` is never set by this tool. A taken name returns `{ "Error": "A vessel named '...' already exists.", "ErrorCode": "Conflict", "Code": "DuplicateEntity" }`.

---

### discover_vessels

Discover git repositories on the Admiral host to onboard as vessels. Scans the given directories and roots breadth-first (skipping excluded and dot-prefixed folders and symbolic links, never descending into a repository), classifies each candidate, and saves the result as an import batch with status `Discovered`. Creates no vessels; call [import_vessels](#import_vessels) with the returned `BatchId`.

Requires a tenant admin caller. Every path must lie inside `import.allowedRoots` (or the user profile directory when none are configured). The rules, candidate statuses, and response shape match `POST /api/v1/vessels/import/discover` in [REST_API.md](REST_API.md#vessel-import).

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "directories": { "type": "array", "items": { "type": "string" }, "description": "Absolute directories. A git repository becomes one candidate; any other directory is scanned like a root." },
    "roots": { "type": "array", "items": { "type": "string" }, "description": "Absolute roots to scan for git repositories." },
    "maxDepth": { "type": "integer", "description": "Maximum scan depth below each root (1-16, default Import.MaxDepth)" },
    "runInBackground": { "type": "boolean", "description": "Run discovery as a background job: returns jobId and the batch in status Discovering; poll the batch until it is Discovered or Failed" }
  }
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `directories` | string[] | One of the two | Absolute directories (`~` is expanded) |
| `roots` | string[] | One of the two | Absolute roots to scan |
| `maxDepth` | integer | No | Levels below each root to search, 1-16 |
| `runInBackground` | boolean | No | Default `false`. When `true`, returns at once with `RunsInBackground: true`, `JobId`, and the batch in status `Discovering` (no candidates); poll the batch until `Discovered` or `Failed` |

**Example Input:**

```json
{ "roots": ["/Users/alex/Code"], "maxDepth": 3 }
```

**Response:** `VesselImportDiscoverResponse`: `BatchId`, `Batch` (a [VesselImportBatch](#vesselimportbatch)), `Candidates` (array of [VesselImportItem](#vesselimportitem)), `Truncated` (true when the 5,000-candidate cap was reached), and `Hints` (`{Code, Message}`; codes `PathNotVisibleToAdmiral`, `CandidateLimitReached`).

On failure returns `{ "Error": "...", "ErrorCode": "...", "Code": "..." }` with `Code` one of `InvalidRequest` (no paths, too many paths, or a relative path), `PathNotAllowed` (`ErrorCode` `Forbidden`), or `HarborNotSupported`, or `{ "Error": "discover_vessels requires a tenant admin", "ErrorCode": "Forbidden" }`.

---

### import_vessels

Import candidates from a [discover_vessels](#discover_vessels) batch as vessels. Each vessel gets `RepoUrl` = the origin URL (or the local path when there is no origin), `WorkingDirectory` = the discovered path, `DefaultBranch` = the inferred default branch, and no `LocalPath`, so deleting the vessel never removes the checkout. Paths that already have a vessel are recorded as `SkippedExisting`, so repeating an import is safe. Unselected candidates are recorded as `SkippedNotSelected`.

Selections at or below `import.inlineBatchLimit` (default 25) run inline and return every item; larger selections run as a background job and return immediately with `RunsInBackground: true` and a `JobId`. Poll the batch with `enumerate` (`entityType: "vessel_import_batch"`) or `GET /api/v1/vessels/import/batches/{id}`.

Requires a tenant admin caller.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "batchId": { "type": "string", "description": "Batch ID (vib_ prefix) from discover_vessels" },
    "paths": { "type": "array", "items": { "type": "string" }, "description": "Candidate paths to import, exactly as returned by discover_vessels" },
    "allNew": { "type": "boolean", "description": "Import every candidate with status New (paths is then ignored)" },
    "fleetId": { "type": "string", "description": "Fleet ID (flt_ prefix) to assign the vessels to" },
    "defaultPipelineId": { "type": "string", "description": "Default pipeline ID (ppl_ prefix) for the vessels" },
    "landingMode": { "type": "string", "description": "Landing mode for the vessels: LocalMerge (merge into the working directory, no push), MergeAndPush (merge, then push), PullRequest, MergeQueue, or None" },
    "categorize": { "type": "boolean", "description": "After the import, have a captain analyze the selected repositories and recommend fleets (FleetCategorization job). Requires captainId" },
    "captainId": { "type": "string", "description": "Captain ID (cpt_ prefix) that recommends fleets; must exist in your tenant and should be Idle" },
    "prompt": { "type": "string", "description": "Categorization instructions; omit to use the import.fleet_categorization prompt template. The output-format contract is always appended" },
    "applyFleetsAutomatically": { "type": "boolean", "description": "Apply the recommended fleets automatically when categorization completes (default false: review, then call apply_fleet_recommendations)" }
  },
  "required": ["batchId"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `batchId` | string | Yes | Batch ID (`vib_` prefix) |
| `paths` | string[] | Unless `allNew` | Candidate paths to import |
| `allNew` | boolean | No | Import every candidate with status `New` |
| `fleetId` | string | No | Fleet to assign the vessels to (must exist in the tenant) |
| `defaultPipelineId` | string | No | Default pipeline for the vessels |
| `landingMode` | string | No | `LocalMerge`, `MergeAndPush`, `PullRequest`, `MergeQueue`, or `None` |
| `categorize` | boolean | No | Recommend fleets with a captain after the import (see [REST_API.md](REST_API.md#vessel-import), Fleet categorization) |
| `captainId` | string | With `categorize` | Captain that recommends fleets; must exist in the tenant |
| `prompt` | string | No | Instructions for the captain; default is the `import.fleet_categorization` prompt template |
| `applyFleetsAutomatically` | boolean | No | Apply the recommendations as soon as the captain finishes |

**Example Input:**

```json
{ "batchId": "vib_mut1abcd_Rp7EygrpeVo", "allNew": true, "fleetId": "flt_abc123" }
```

**Response:** `VesselImportResponse`: `BatchId`, `JobId` (null when inline), `RunsInBackground`, `Batch` (with `Status`, `CreatedCount`, `SkippedCount`, `FailedCount`), and `Items` (every [VesselImportItem](#vesselimportitem) with its `Outcome`, `OutcomeReason`, and `VesselId`; empty for a background import).

On failure returns `{ "Error": "...", "ErrorCode": "...", "Code": "..." }` with `Code` one of `InvalidRequest` (no paths, a path not in the batch, an unknown fleet or landing mode, or `allNew` with no New candidates), `BatchNotFound`, or `BatchBusy` (the batch is already being imported, is still discovering, or has a categorization running). A categorization with a missing or unknown captain returns `InvalidRequest` before any vessel is created.

---

### categorize_vessel_import

Run or retry fleet categorization for an import batch whose import finished: a captain reads a generated manifest of the batch's selected repositories in a scratch directory and recommends fleets (a `FleetCategorization` background job). Omitted arguments reuse the batch's previous captain, prompt, and auto-apply choice. Poll the batch until `CategorizationStatus` is `Completed`, `Failed`, or `Applied`; cancel with the job cancel endpoint. Requires a tenant admin caller.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "batchId": { "type": "string", "description": "Batch ID (vib_ prefix)" },
    "captainId": { "type": "string", "description": "Captain ID (cpt_ prefix); omit to reuse the previous captain" },
    "prompt": { "type": "string", "description": "Instructions; omit to reuse the previous run's instructions" },
    "applyFleetsAutomatically": { "type": "boolean", "description": "Apply the recommendations automatically when the run completes" }
  },
  "required": ["batchId"]
}
```

**Response:** the [VesselImportBatch](#vesselimportbatch) with `CategorizationStatus: "Pending"` and `CategorizationJobId`. On failure `{ "Error", "ErrorCode", "Code" }` with `BatchNotFound`, `BatchBusy` (import not finished or categorization already running), or `InvalidRequest` (no captain known, or the captain does not exist).

---

### apply_fleet_recommendations

Apply fleet recommendations to an import batch. Pass `fleets` to apply an edited list, or omit it to apply the captain's stored recommendations unchanged. Each fleet is reused when the tenant already has a fleet with the same name (case-insensitive) and created otherwise; each listed vessel is assigned to it. A fleet named `Uncategorized` is never created (its vessels keep their fleet). Requires a tenant admin caller.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "batchId": { "type": "string", "description": "Batch ID (vib_ prefix)" },
    "fleets": {
      "type": "array",
      "description": "Fleets to apply; omit to apply the stored recommendations",
      "items": {
        "type": "object",
        "properties": {
          "name": { "type": "string", "description": "Fleet name" },
          "description": { "type": "string", "description": "Description for a newly created fleet" },
          "vesselIds": { "type": "array", "items": { "type": "string" }, "description": "Vessel IDs (vsl_ prefix) from the batch" }
        },
        "required": ["name", "vesselIds"]
      }
    }
  },
  "required": ["batchId"]
}
```

**Example Input:**

```json
{ "batchId": "vib_mut1abcd_Rp7EygrpeVo", "fleets": [ { "name": "Payments Platform", "vesselIds": ["vsl_mut1b000_Q2w3e4r5t6y"] } ] }
```

**Response:** `FleetRecommendationApplyResult`: `BatchId`, `Fleets` (created or reused), `CreatedFleetIds`, `Assignments` (`VesselId`, `VesselName`, `FleetId`, `FleetName`, `PreviousFleetId`), and `Batch`. On failure `{ "Error", "ErrorCode", "Code" }` with `BatchNotFound`, `BatchBusy` (discovery, import, or categorization still running), or `InvalidRequest` (unnamed fleet, a vessel listed twice or not in the batch, or no stored recommendations).

#### VesselImportBatch

| Field | Type | Description |
|---|---|---|
| `Id` | string | `vib_` prefix |
| `TenantId`, `UserId` | string | Owner |
| `Status` | string | `Discovering`, `Discovered`, `Importing`, `Completed`, `CompletedWithFailures`, `Failed` |
| `HarborId`, `FleetId`, `JobId` | string | Discovery host (null = Admiral), assigned fleet, background import job |
| `DiscoveryJobId`, `Truncated`, `ErrorMessage` | string, bool, string | Background discovery job, candidate cap reached, and why discovery or the import failed |
| `CategorizationStatus` | string | `None`, `Pending`, `Running`, `Completed`, `Failed`, `Applied` |
| `CategorizationCaptainId`, `CategorizationJobId` | string | Captain and job of the latest categorization run |
| `CategorizationPrompt`, `CategorizationApplyAutomatically`, `CategorizationError` | string, bool, string | Instructions used, auto-apply choice, and failure reason |
| `CategorizationStartedUtc`, `CategorizationCompletedUtc` | datetime | Categorization timestamps |
| `RequestedPathCount`, `CandidateCount`, `CreatedCount`, `SkippedCount`, `FailedCount` | int | Counts from discovery and the latest import |
| `CreatedUtc`, `LastUpdateUtc`, `CompletedUtc` | datetime | Timestamps |

#### VesselImportItem

| Field | Type | Description |
|---|---|---|
| `Id` | string | `vii_` prefix |
| `BatchId` | string | Owning batch |
| `Path` | string | Normalized absolute path with on-disk casing |
| `ProposedName` | string | Folder name made unique within the tenant (`-2`, `-3`, ...) |
| `RemoteUrl`, `DefaultBranch` | string | Origin URL (null when none) and inferred default branch |
| `CandidateStatus` | string | `New`, `AlreadyOnboarded`, `Worktree`, `ArmadaManaged`, `NotFound`, `NotGit`, `AccessDenied` |
| `ExistingVesselId` | string | Matching existing vessel, or null |
| `Outcome` | string | `Pending`, `Created`, `SkippedExisting`, `SkippedNotSelected`, `Failed` |
| `OutcomeReason` | string | `VesselAlreadyExists`, `NotSelected`, `NotImportable`, `PathMissing`, `CreateFailed`, `Cancelled`, or null |
| `OutcomeMessage` | string | Diagnostic text, or null |
| `VesselId` | string | Vessel created for this item, or null |
| `Selected` | bool | The operator selected this candidate in the latest import |

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

Returns `{ "Error": "Event not found", "ErrorCode": "NotFound" }` if the event does not exist.

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

Returns `{ "Error": "ids is required and must not be empty", "ErrorCode": "InvalidArgument" }` if no IDs are provided.

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

Returns `{ "Error": "ids is required and must not be empty", "ErrorCode": "InvalidArgument" }` if no IDs are provided.

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
  "Status": "stopped",
  "CaptainId": "cpt_abc123def456ghi789jk"
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

**Response:** The updated [Captain](#captain) object, or `{ "Status": "not_quarantined", "CaptainId": "..." }` when the captain was not quarantined, or `{ "Error": "Captain not found", "ErrorCode": "NotFound" }`.

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
  "Status": "all_stopped"
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

Sets the mission status to `Cancelled`. Returns `{ "Error": "Mission not found", "ErrorCode": "NotFound" }` if the ID does not exist.

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

Only `Failed` or `Cancelled` missions can be restarted. Returns `{ "Error": "Mission not found", "ErrorCode": "NotFound" }` if the mission does not exist, or `{ "Error": "Only Failed or Cancelled missions can be restarted (current: ...)", "ErrorCode": "Conflict" }` for any other status.

**Response:** The updated [Mission](#mission) object with status `Pending`.

---

### retry_landing

Retry landing for a mission in `LandingFailed` status. Rebases the mission branch onto the current target and re-attempts landing.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "missionId": { "type": "string", "description": "Mission ID (msn_ prefix) to retry landing for" }
  },
  "required": ["missionId"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `missionId` | string | Yes | Mission ID to retry landing for (prefix `msn_`) |

**Response:** `{ "Success": true, "Mission": { "...": "..." } }` where `Success` reports whether the landing succeeded and `Mission` is the refreshed [Mission](#mission) object. Returns `{ "Error": "Landing service not configured", "ErrorCode": "Unavailable" }` when the landing service is unavailable.

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

Returns `{ "Error": "Voyage not found", "ErrorCode": "NotFound" }` if the ID does not exist.

**Response:**

```json
{
  "Voyage": {
    "Id": "vyg_abc123def456ghi789jk",
    "Title": "Implement authentication",
    "Status": "Cancelled",
    "...": "..."
  },
  "CancelledMissions": 3
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

Returns `{ "Error": "ids is required and must not be empty", "ErrorCode": "InvalidArgument" }` if no IDs are provided.

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

**Response:** [Fleet](#fleet) object. A taken name returns `{ "Error": "A fleet named '...' already exists.", "ErrorCode": "Conflict", "Code": "DuplicateEntity" }`.

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

**Response:** Updated [Fleet](#fleet) object, or `{ "Error": "Fleet not found", "ErrorCode": "NotFound" }`. A taken name returns `{ "Error": "A fleet named '...' already exists.", "ErrorCode": "Conflict", "Code": "DuplicateEntity" }`.

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

Returns `{ "Error": "ids is required and must not be empty", "ErrorCode": "InvalidArgument" }` if no IDs are provided.

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

**Response:** [Vessel](#vessel) object, or `{ "Error": "Vessel not found", "ErrorCode": "NotFound" }`.

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
    "allowConcurrentMissions": { "type": "boolean", "description": "Allow multiple concurrent missions on this vessel" },
    "autoApprove": { "type": "boolean", "description": "Per-vessel auto-approve override for missions on this vessel: true or false wins over the captain's setting" },
    "clearAutoApprove": { "type": "boolean", "description": "Remove the per-vessel auto-approve override so the captain's own setting applies" },
    "enableModelContext": { "type": "boolean", "description": "Enable or disable model context accumulation" },
    "modelContext": { "type": "string", "description": "Agent-accumulated context about this repository" },
    "defaultPipelineId": { "type": "string", "description": "Default pipeline ID for voyages dispatched to this vessel" },
    "autoLandEnabled": { "type": "boolean", "description": "Whether the auto-land predicate gates unattended landing on this vessel" },
    "autoLandMaxFiles": { "type": "integer", "description": "Maximum number of changed files that may auto-land unattended (0 = no limit)" },
    "autoLandMaxLines": { "type": "integer", "description": "Maximum number of changed lines that may auto-land unattended (0 = no limit)" },
    "autoLandPathAllowGlobs": { "type": "array", "items": { "type": "string" }, "description": "Glob patterns a changed path must match to be auto-landable" },
    "autoLandPathDenyGlobs": { "type": "array", "items": { "type": "string" }, "description": "Glob patterns that force a hold: a change touching any matching path never auto-lands" },
    "definitionOfDoneEnabled": { "type": "boolean", "description": "Run the in-dock build + unit tests before acceptance; a failure blocks landing with a classified reason (Compile/TestFail/Timeout/Infra)" },
    "definitionOfDoneBuildCommand": { "type": "string", "description": "Shell command that builds the project inside the mission checkout (e.g. dotnet build)" },
    "definitionOfDoneTestCommand": { "type": "string", "description": "Shell command that runs unit tests inside the mission checkout (e.g. dotnet test)" },
    "definitionOfDoneTimeoutSeconds": { "type": "integer", "description": "Per-phase timeout in seconds, clamped to [30, 7200] (default 1800)" }
  },
  "required": ["vesselId"]
}
```

**Response:** Updated [Vessel](#vessel) object, or `{ "Error": "Vessel not found", "ErrorCode": "NotFound" }`. A taken name returns `{ "Error": "A vessel named '...' already exists.", "ErrorCode": "Conflict", "Code": "DuplicateEntity" }`.

The `autoLand*` and `definitionOfDone*` arguments are accepted by the handler but are not declared in the tool's advertised input schema, so they are not part of the frozen 1.0 surface in [API_SURFACE_1.0.md](API_SURFACE_1.0.md); prefer the REST vessel routes for those fields.

`autoApprove` sets the per-vessel override (true or false wins over the captain's setting for missions on this vessel); `clearAutoApprove: true` removes it so the captain's own setting applies again. Omitting both keeps the stored value.

When `gitHubTokenOverride` is omitted, MCP preserves the current stored override. Send `""` to clear the override and fall back to the global `gitHubToken` from Armada configuration.

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

**Response:** Updated [Vessel](#vessel) object, or `{ "Error": "Vessel not found", "ErrorCode": "NotFound" }`.

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

Returns `{ "Error": "ids is required and must not be empty", "ErrorCode": "InvalidArgument" }` if no IDs are provided.

---

### vessel_health

Get one vessel's health: the row with effective (override-aware) statuses, the raw findings, and the manual overrides. Dependency rows are excluded by default to conserve context; `DependencyCount` is always returned. The row and finding fields, enum spellings, and detail codes match `GET /api/v1/vessels/{id}/health` in [REST_API.md](REST_API.md#vessel-health). To list vessels by health, use [`enumerate`](#enumerate) with `entityType` `vessel_health`.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "vesselId": { "type": "string", "description": "Vessel ID (vsl_ prefix)" },
    "includeDependencies": { "type": "boolean", "description": "Include outdated/vulnerable dependency rows (default false)" }
  },
  "required": ["vesselId"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `vesselId` | string | Yes | Vessel ID (prefix `vsl_`) |
| `includeDependencies` | boolean | No | Include the `Dependencies` list (default `false`) |

**Response:**

```json
{
  "Health": { "VesselId": "vsl_...", "OverallStatus": "Fail", "DependencyStatus": "Fail", "OutdatedCount": 23, "...": "..." },
  "Findings": [ { "Criterion": "Dependencies", "Status": "Fail", "DetailCode": "OutdatedPackages", "ValueA": 23, "ValueB": 5, "...": "..." } ],
  "Overrides": [],
  "DependencyCount": 30
}
```

Returns `{ "Error": "Vessel not found", "ErrorCode": "NotFound" }` when the vessel is not in the caller's tenant.

---

### evaluate_vessel_health

Start a background vessel health evaluation job for specific vessels, a fleet, or every active vessel in the tenant. Only one evaluation runs per tenant at a time; when one is already running, nothing starts and `AlreadyRunning` is `true` with the running job's ID. Requires a tenant admin (or global admin) caller.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "vesselIds": { "type": "array", "items": { "type": "string" }, "description": "Vessel IDs to evaluate (default: all active vessels)" },
    "fleetId": { "type": "string", "description": "Evaluate the active vessels of this fleet (ignored when vesselIds is set)" },
    "force": { "type": "boolean", "description": "Force dependency and vulnerability checks even when fresh (default true)" }
  }
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `vesselIds` | string[] | No | Evaluate exactly these vessels |
| `fleetId` | string | No | Evaluate the fleet's active vessels |
| `force` | boolean | No | Run dependency checks even when the manifests are unchanged and fresh (default `true`) |

**Response:**

```json
{ "JobId": "job_...", "AlreadyRunning": false, "VesselCount": 12 }
```

Poll the job with `enumerate` (`entityType` `jobs`) or `GET /api/v1/jobs/{id}`. Returns `{ "Error": "...", "ErrorCode": "NotFound" }` for an unknown vessel or fleet.

---

### set_vessel_health_override

Set, or with `remove` set to `true` remove, a manual status override for one criterion or for `Overall`. Effective statuses are recomputed immediately from the stored findings. Requires a tenant admin (or global admin) caller.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "vesselId": { "type": "string", "description": "Vessel ID (vsl_ prefix)" },
    "criterion": { "type": "string", "description": "GitDivergence, WorkingTree, Branches, CommitRecency, Dependencies, Vulnerabilities, TestInfrastructure, ContinuousIntegration, ArmadaReadiness, MissionOutcomes, or Overall" },
    "status": { "type": "string", "description": "Pass, Warn, Fail, NotApplicable, or Unknown (required unless remove is true)" },
    "note": { "type": "string", "description": "Optional note explaining the override" },
    "remove": { "type": "boolean", "description": "Remove the override instead of setting it (default false)" }
  },
  "required": ["vesselId", "criterion"]
}
```

**Response:**

```json
{
  "Health": { "VesselId": "vsl_...", "OverallStatus": "Pass", "DependencyStatus": "Pass", "...": "..." },
  "Overrides": [ { "Criterion": "Dependencies", "Status": "Pass", "Note": "Pinned on purpose", "...": "..." } ]
}
```

Returns an error object with `ErrorCode` `InvalidArgument` for an unknown criterion or a missing or invalid status, `Forbidden` for a non-admin caller, and `NotFound` for a vessel outside the caller's tenant.

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
    "tier": { "type": "string", "description": "Optional minimum captain tier for this mission (Economy, Standard, or Premium); only captains at or above it are assigned" },
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

**Response:** [Mission](#mission) object. Without `tier` the mission routes to any idle captain (treated as `Standard`).

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

**Response:** Updated [Mission](#mission) object, or `{ "Error": "Mission not found", "ErrorCode": "NotFound" }`.

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

Returns `{ "Error": "Mission not found", "ErrorCode": "NotFound" }` if the ID does not exist.

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

Returns `{ "Error": "ids is required and must not be empty", "ErrorCode": "InvalidArgument" }` if no IDs are provided.

---

### transition_mission_status

Transition a mission to a new status with validation. The rules are `MissionStateMachine`'s, shared with
`PUT /api/v1/missions/{id}/status` and the WebSocket `transition_mission_status` command; the tool description lists them
generated from the same source.

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
    "runtime": { "type": "string", "description": "Agent runtime: ClaudeCode, Codex, Gemini, Cursor, Mux, OpenCode, ApiEndpoint, Custom" },
    "model": { "type": "string", "description": "Optional model override for this captain. When omitted, the runtime chooses automatically" },
    "reasoningEffort": { "type": "string", "description": "Reasoning effort: Off, Minimal, Low, Medium, or High. Translated per runtime (Claude thinking budget, Codex reasoning effort, Mux --effort). Ignored by runtimes without a control." },
    "tier": { "type": "string", "description": "Capability tier for dispatch routing: Economy, Standard, or Premium. Empty auto-classifies from the model name." },
    "systemInstructions": { "type": "string", "description": "System instructions for this captain -- injected into every mission prompt to specialize behavior" },
    "allowedPersonas": { "type": "string", "description": "JSON array of persona names this captain is allowed to use" },
    "preferredPersona": { "type": "string", "description": "Preferred persona name for this captain" },
    "muxConfigDirectory": { "type": "string", "description": "Optional Mux config directory override" },
    "muxEndpoint": { "type": "string", "description": "Named Mux endpoint for this captain" },
    "muxBaseUrl": { "type": "string", "description": "Optional Mux base URL override" },
    "muxAdapterType": { "type": "string", "description": "Optional Mux adapter type override" },
    "muxTemperature": { "type": "number", "description": "Optional Mux temperature override" },
    "muxMaxTokens": { "type": "integer", "description": "Optional Mux max tokens override" },
    "muxSystemPromptPath": { "type": "string", "description": "Optional Mux system prompt file path" },
    "muxApprovalPolicy": { "type": "string", "description": "Optional Mux approval policy override" },
    "autoApprove": { "type": "boolean", "description": "Whether the CLI captain runs with its auto-approve or permission-bypass flag (default true). False runs it without auto-approve where the runtime supports it." }
  },
  "required": ["name"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `name` | string | Yes | Captain display name |
| `runtime` | string | No | Agent runtime: `ClaudeCode`, `Codex`, `Gemini`, `Cursor`, `Mux`, `OpenCode`, or `Custom` |
| `model` | string | No | Optional model override. When omitted, the runtime chooses automatically |
| `reasoningEffort` | string | No | Reasoning effort: `Off`, `Minimal`, `Low`, `Medium`, or `High`. Translated to each runtime's native control at launch |
| `tier` | string | No | Capability tier for dispatch routing: `Economy`, `Standard`, or `Premium`. Empty/null auto-classifies from the model name |
| `systemInstructions` | string | No | System instructions injected into every mission prompt for this captain |
| `allowedPersonas` | string | No | JSON array of persona names this captain is allowed to use, for example `["Worker","Judge"]` |
| `preferredPersona` | string | No | Preferred persona name for this captain |
| `muxConfigDirectory` | string | No | Optional Mux config directory override (Mux runtime only) |
| `muxEndpoint` | string | No | Named Mux endpoint to use for this captain (Mux runtime only) |
| `muxBaseUrl` | string | No | Optional Mux base URL override (Mux runtime only) |
| `muxAdapterType` | string | No | Optional Mux adapter type override (Mux runtime only) |
| `muxTemperature` | number | No | Optional Mux temperature override (Mux runtime only) |
| `muxMaxTokens` | integer | No | Optional Mux max tokens override (Mux runtime only) |
| `muxSystemPromptPath` | string | No | Optional Mux system prompt file path (Mux runtime only) |
| `muxApprovalPolicy` | string | No | Optional Mux approval policy override (Mux runtime only) |
| `autoApprove` | boolean | No | Whether the CLI captain runs with its auto-approve or permission-bypass flag (default `true`). `false` runs it without auto-approve where the runtime supports it (Claude Code acceptEdits, Codex workspace-write sandbox, Gemini auto_edit, Cursor without --force, OpenCode without --auto, Mux deny) |

**Response:** [Captain](#captain) object. Invalid or unavailable models are returned as MCP tool errors. A taken name returns `{ "Error": "A captain named '...' already exists.", "ErrorCode": "Conflict", "Code": "DuplicateEntity" }`. The Mux options apply only when `runtime` is `Mux`.

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

**Response:** [Captain](#captain) object, or `{ "Error": "Captain not found", "ErrorCode": "NotFound" }`.

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

**Response:** [CaptainToolAccessResult](#captaintoolaccessresult) object, or `{ "Error": "Captain not found", "ErrorCode": "NotFound" }`.

---

### update_captain

Update a captain's properties (name, runtime, model, tier, personas, Mux options, auto-approve). Operational fields (state, process, mission) are preserved.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "captainId": { "type": "string", "description": "Captain ID (cpt_ prefix)" },
    "name": { "type": "string", "description": "New display name" },
    "runtime": { "type": "string", "description": "New agent runtime: ClaudeCode, Codex, Gemini, Cursor, Mux, OpenCode, ApiEndpoint, Custom" },
    "model": { "type": "string", "description": "New optional model override for this captain" },
    "reasoningEffort": { "type": "string", "description": "Reasoning effort: Off, Minimal, Low, Medium, or High. Empty string clears it. Translated per runtime (Claude thinking budget, Codex reasoning effort, Mux --effort)." },
    "tier": { "type": "string", "description": "Capability tier: Economy, Standard, or Premium. Empty string clears it (auto-classify from model)." },
    "systemInstructions": { "type": "string", "description": "New system instructions for this captain" },
    "allowedPersonas": { "type": "string", "description": "New JSON array of persona names this captain is allowed to use" },
    "preferredPersona": { "type": "string", "description": "New preferred persona name for this captain" },
    "muxConfigDirectory": { "type": "string", "description": "Optional Mux config directory override; empty string clears it" },
    "muxEndpoint": { "type": "string", "description": "Named Mux endpoint; empty string clears it" },
    "muxBaseUrl": { "type": "string", "description": "Optional Mux base URL override; empty string clears it" },
    "muxAdapterType": { "type": "string", "description": "Optional Mux adapter type override; empty string clears it" },
    "muxTemperature": { "type": "number", "description": "Optional Mux temperature override" },
    "muxMaxTokens": { "type": "integer", "description": "Optional Mux max tokens override" },
    "muxSystemPromptPath": { "type": "string", "description": "Optional Mux system prompt file path; empty string clears it" },
    "muxApprovalPolicy": { "type": "string", "description": "Optional Mux approval policy override; empty string clears it" },
    "autoApprove": { "type": "boolean", "description": "Whether the CLI captain runs with its auto-approve or permission-bypass flag. Omit to keep the current value." }
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
| `reasoningEffort` | string | No | Reasoning effort: `Off`, `Minimal`, `Low`, `Medium`, or `High`. Empty string clears it |
| `tier` | string | No | Capability tier: `Economy`, `Standard`, or `Premium`. Empty string clears it (auto-classify from model) |
| `systemInstructions` | string | No | New system instructions for this captain |
| `allowedPersonas` | string | No | New JSON array of persona names this captain is allowed to use, for example `["Worker","Judge"]` |
| `preferredPersona` | string | No | New preferred persona name for this captain |
| `muxConfigDirectory` | string | No | Optional Mux config directory override; empty string clears it (Mux runtime only) |
| `muxEndpoint` | string | No | Named Mux endpoint; empty string clears it (Mux runtime only) |
| `muxBaseUrl` | string | No | Optional Mux base URL override; empty string clears it (Mux runtime only) |
| `muxAdapterType` | string | No | Optional Mux adapter type override; empty string clears it (Mux runtime only) |
| `muxTemperature` | number | No | Optional Mux temperature override (Mux runtime only) |
| `muxMaxTokens` | integer | No | Optional Mux max tokens override (Mux runtime only) |
| `muxSystemPromptPath` | string | No | Optional Mux system prompt file path; empty string clears it (Mux runtime only) |
| `muxApprovalPolicy` | string | No | Optional Mux approval policy override; empty string clears it (Mux runtime only) |
| `autoApprove` | boolean | No | Whether the CLI captain runs with its auto-approve or permission-bypass flag. Omit to keep the current value |

**Response:** Updated [Captain](#captain) object, or `{ "Error": "Captain not found", "ErrorCode": "NotFound" }`. A taken name returns `{ "Error": "A captain named '...' already exists.", "ErrorCode": "Conflict", "Code": "DuplicateEntity" }`. Invalid or unavailable models are returned as MCP tool errors. The Mux options apply only when the captain's `runtime` is `Mux`.

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

Returns `{ "Error": "ids is required and must not be empty", "ErrorCode": "InvalidArgument" }` if no IDs are provided.

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

**Response:** Dock object, or `{ "Error": "Dock not found", "ErrorCode": "NotFound" }`.

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

Returns `{ "Error": "Dock not found", "ErrorCode": "NotFound" }` if the ID does not exist.
Returns `{ "Error": "Cannot delete dock while it is actively in use by a captain", "ErrorCode": "Conflict" }` if the dock is active.

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

Returns `{ "Error": "Dock not found", "ErrorCode": "NotFound" }` if the ID does not exist.

---

### repair_dock

Repair a dock's git worktree to fix a corrupted or relocated registration. Non-destructive: no work is removed.

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
| `dockId` | string | Yes | Dock ID to repair (prefix `dck_`) |

**Response:**

```json
{ "Status": "repaired", "DockId": "dck_..." }
```

Returns `{ "Error": "Dock not found", "ErrorCode": "NotFound" }` if the ID does not exist.

---

### unstick_dock

Unstick a wedged dock: release any captain still holding it back to Idle and reclaim its worktree so it stops pinning capacity. Committed branch history is preserved.

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
| `dockId` | string | Yes | Dock ID to unstick (prefix `dck_`) |

**Response:**

```json
{ "Status": "unstuck", "DockId": "dck_..." }
```

Returns `{ "Error": "Dock not found", "ErrorCode": "NotFound" }` if the ID does not exist.

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

Returns `{ "Error": "ids is required and must not be empty", "ErrorCode": "InvalidArgument" }` if no IDs are provided.

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

**Response:** [Playbook](#playbook) object, or `{ "Error": "Playbook not found: pbk_...", "ErrorCode": "NotFound" }`.

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

**Response:** [Playbook](#playbook) object, or an error such as `{ "Error": "A playbook with file name 'x.md' already exists.", "ErrorCode": "Conflict", "Code": "DuplicateEntity" }`.

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

**Response:** Updated [Playbook](#playbook) object, or `{ "Error": "Playbook not found: pbk_...", "ErrorCode": "NotFound" }`. A taken file name returns `ErrorCode` `Conflict` with `Code` `DuplicateEntity`.

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

**Response:** [MergeEntry](#mergeentry) object, or `{ "Error": "Merge entry not found", "ErrorCode": "NotFound" }`.

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

**Response:** [MergeEntry](#mergeentry) object, or `{ "Error": "Merge entry not found or not in Queued status", "ErrorCode": "NotFound" }`.

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

Returns `{ "Error": "Merge entry not found", "ErrorCode": "NotFound" }` if the ID does not exist.
Returns `{ "Error": "Cannot delete merge entry in non-terminal status ...", "ErrorCode": "Conflict" }` if the entry is not in a terminal state.

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

Returns `{ "Error": "Invalid status. Must be one of: Landed, Failed, Cancelled", "ErrorCode": "InvalidArgument" }` if an invalid status is provided.

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

Returns `{ "Error": "Merge entry not found", "ErrorCode": "NotFound" }` if the ID does not exist.
Returns `{ "Error": "Cannot purge merge entry in non-terminal status ...", "ErrorCode": "Conflict" }` if the entry is not in a terminal state.

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

Returns `{ "Error": "entryIds is required and must not be empty", "ErrorCode": "InvalidArgument" }` if no IDs are provided.

---

### get_harbor

> **Experimental.** The Harbor tools below (`get_harbor`, `get_harbor_metrics`, `create_harbor`, `update_harbor`, `delete_harbor`,
> `set_harbor_enabled`) belong to Harbor split mode, which is experimental for 1.0 and excluded from the compatibility
> promise. Their descriptions start with `[Experimental]`.

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

**Response:** [Harbor](#harbor) object, or `{ "Error": "Harbor not found", "ErrorCode": "NotFound" }`.

---

### get_harbor_metrics

Charts for one Harbor over a window, the same `HarborMetrics` that `GET /api/v1/harbors/{id}/metrics` returns: jobs
finished and failed per bucket (missions and other launches apart), slot usage (peak and average concurrent jobs
against capacity), launch speed per runtime (median and p95 time to first output and total runtime), link health
(connected, reconnecting, and down stretches, and heartbeat round trips), and token usage by runtime and model. See
[REST_API.md](REST_API.md#get-apiv1harborsidmetrics) for the response shape and [HARBOR.md](HARBOR.md#harbor-metrics)
for how the numbers are recorded. Visibility and token scoping are the same as over REST.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "harborId": { "type": "string", "description": "Harbor ID (hbr_ prefix)" },
    "range": { "type": "string", "description": "Window: 1h, 24h (default), or 7d", "enum": ["1h", "24h", "7d"] }
  },
  "required": ["harborId"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `harborId` | string | Yes | Harbor ID (prefix `hbr_`) |
| `range` | string | No | `1h` (1-minute buckets), `24h` (30-minute buckets, default), or `7d` (3-hour buckets) |

**Response:** `HarborMetrics`, or `{ "Error": "Harbor not found", "ErrorCode": "NotFound" }`, or an `InvalidArgument`
error for an unknown range.

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

**Response:** The newly created [Harbor](#harbor) object, or `{ "Error": "...", "ErrorCode": "InvalidArgument" }` on invalid input.

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

**Response:** The updated [Harbor](#harbor) object, or `{ "Error": "Harbor not found", "ErrorCode": "NotFound" }`.

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

Returns `{ "Error": "...", "ErrorCode": "NotFound" }` if the Harbor does not exist.

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

**Response:** The updated [Harbor](#harbor) object, or an error object (`ErrorCode` `InvalidArgument` when `harborId` is missing, `NotFound` when the Harbor does not exist).

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

**Response:** serialized `Objective` object with the expanded backlog fields (`Kind`, `Category`, `Priority`, `Rank`, `BacklogState`, `Effort`, `TargetVersion`, `DueUtc`, `ParentObjectiveId`, `BlockedByObjectiveIds`, `RefinementSummary`, `SuggestedPipelineId`, `RefinementSessionIds`), or `{ "Error": "Objective not found", "ErrorCode": "NotFound" }`.

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

Core CRUD and reorder tools (`*` marks a required argument):

| Tool | Permission | Arguments | Notes |
|---|---|---|---|
| `list_objectives` | Authenticated | `owner`, `category`, `parentObjectiveId`, `vesselId`, `fleetId`, `status`, `backlogState`, `kind`, `priority`, `effort`, `targetVersion`, `search`, `pageNumber`, `pageSize` | Enumerate objective/backlog records |
| `list_backlog` | Authenticated | Same as `list_objectives` | Preferred user-facing alias |
| `get_objective` | Authenticated | `objectiveId`* | Returns the expanded backlog shape |
| `get_backlog_item` | Authenticated | `objectiveId`* | Alias over the same objective-backed record |
| `create_objective` | TenantAdmin | `title`* plus the expanded backlog fields (see [create_objective](#create_objective)) | |
| `create_backlog_item` | TenantAdmin | Same as `create_objective` | Preferred user-facing alias |
| `update_objective` | TenantAdmin | `objectiveId`* plus any `create_objective` field to change (`title` is optional here) | Only supplied fields change |
| `update_backlog_item` | TenantAdmin | Same as `update_objective` | Preferred user-facing alias; identical schema |
| `reorder_objectives` | TenantAdmin | `items`* (`[{ objectiveId*, rank* }]`) | Apply explicit rank updates |
| `reorder_backlog_items` | TenantAdmin | Same as `reorder_objectives` | Preferred user-facing alias |
| `delete_objective` | TenantAdmin | `objectiveId`* | Removes the normalized row and snapshot-backed current-state chain |
| `delete_backlog_item` | TenantAdmin | `objectiveId`* | Preferred user-facing alias |

Refinement tools:

| Tool | Permission | Arguments | Notes |
|---|---|---|---|
| `list_backlog_refinement_sessions` | Authenticated | `objectiveId`*, `pageNumber` (default 1), `pageSize` (default 25, max 100) | Lightweight, paginated list for one backlog item |
| `create_backlog_refinement_session` | TenantAdmin | `objectiveId`*, `captainId`*, `fleetId`, `vesselId`, `title`, `initialMessage` | Start captain-backed refinement; `vesselId` is optional |
| `get_backlog_refinement_session` | Authenticated | `sessionId`* (`ors_`) | Returns session, messages, captain, vessel, and linked backlog item |
| `send_backlog_refinement_message` | TenantAdmin | `sessionId`*, `content`* | Append one user message; launches the next refinement turn |
| `summarize_backlog_refinement_session` | TenantAdmin | `sessionId`*, `messageId` | Create/select a structured summary (latest assistant turn when `messageId` is omitted) |
| `apply_backlog_refinement_summary` | TenantAdmin | `sessionId`*, `messageId`, `markMessageSelected` (default `true`), `promoteBacklogState` (default `true`), `endSession` (default `true`) | Apply the summary back to the backlog item. Ends the session and releases its captain unless `endSession` is `false`. Returns `{ Summary, Objective, Session }` |
| `stop_backlog_refinement_session` | TenantAdmin | `sessionId`* | Stop one active refinement session; releases the selected captain |

Planning handoff tools:

| Tool | Permission | Arguments | Notes |
|---|---|---|---|
| `create_backlog_planning_session` | TenantAdmin | `objectiveId`*, `captainId`*, `vesselId`*, `fleetId`, `pipelineId`, `title`, `selectedPlaybooks` | Start a repository-aware planning session from one backlog item |
| `get_backlog_planning_session` | Authenticated | `sessionId`* (`psn_`) | Returns transcript plus linked backlog items |
| `dispatch_backlog_planning_session` | TenantAdmin | `sessionId`*, `messageId`, `title`, `description` | Dispatch a voyage from the planning session; keeps the backlog/objective linkage on the resulting voyage |

Shared input notes:

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
  "pipelineId": "ppl_abc123",
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

**Response:** serialized `CheckRun` object, or `{ "Error": "Check run not found", "ErrorCode": "NotFound" }`.

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

**Response:** serialized `Deployment` object, or `{ "Error": "Deployment not found", "ErrorCode": "NotFound" }`.

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

**Response:** serialized `Release` object, or `{ "Error": "Release not found", "ErrorCode": "NotFound" }`.

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

**Response:** serialized `Runbook` object, or `{ "Error": "Runbook not found", "ErrorCode": "NotFound" }`.

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

**Response:** serialized `RunbookExecution` object, or `{ "Error": "Runbook execution not found", "ErrorCode": "NotFound" }`.

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

**Response:** Created [PromptTemplate](#prompttemplate) object, or `{ "Error": "A prompt template named '...' already exists.", "ErrorCode": "Conflict", "Code": "DuplicateEntity" }`.

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

**Response:** [PromptTemplate](#prompttemplate) object, or `{ "Error": "Template not found: <name>", "ErrorCode": "NotFound" }`.

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

**Response:** Reset [PromptTemplate](#prompttemplate) object with default content restored, or `{ "Error": "No embedded default exists for template: <name>", "ErrorCode": "NotFound" }`.

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
    "promptTemplateName": { "type": "string", "description": "Name of the prompt template to use for this persona" },
    "defaultCaptainId": { "type": "string", "description": "Optional default (preferred) captain id (cpt_ prefix) for this persona" }
  },
  "required": ["name", "promptTemplateName"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `name` | string | Yes | Persona name |
| `description` | string | No | Persona description |
| `promptTemplateName` | string | Yes | Name of the prompt template to use for this persona |
| `defaultCaptainId` | string | No | Optional default (preferred) captain id (prefix `cpt_`) for this persona |

**Response:** The newly created [Persona](#persona) object. A taken name returns `{ "Error": "A persona named '...' already exists.", "ErrorCode": "Conflict", "Code": "DuplicateEntity" }`.

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

**Response:** [Persona](#persona) object, or `{ "Error": "Persona not found: <name>", "ErrorCode": "NotFound" }`.

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
    "promptTemplateName": { "type": "string", "description": "New prompt template name" },
    "defaultCaptainId": { "type": "string", "description": "Default (preferred) captain id (cpt_ prefix) for this persona; empty string clears it" }
  },
  "required": ["name"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `name` | string | Yes | Persona name |
| `description` | string | No | New persona description |
| `promptTemplateName` | string | No | New prompt template name |
| `defaultCaptainId` | string | No | Default (preferred) captain id (prefix `cpt_`); empty string clears it |

**Response:** Updated [Persona](#persona) object, or `{ "Error": "Persona not found: <name>", "ErrorCode": "NotFound" }`.

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

Returns `{ "Error": "Persona not found: <name>", "ErrorCode": "NotFound" }` if the name does not exist.
Returns `{ "Error": "Cannot delete built-in persona", "ErrorCode": "Conflict" }` if the persona is built-in.

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
          "description": { "type": "string", "description": "Stage description" },
          "requiresReview": { "type": "boolean", "description": "Whether the stage's mission waits for human review before the pipeline advances (default false)" },
          "reviewDenyAction": { "type": "string", "description": "What a review denial does: RetryStage, FailPipeline (default RetryStage)" }
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
| `stages` | array | Yes | Ordered list of pipeline stages, each with `personaName` (required), `isOptional` (optional, default false), `description` (optional), `requiresReview` (optional, default false), and `reviewDenyAction` (optional, `RetryStage` or `FailPipeline`, default `RetryStage`; any other value is `InvalidArgument`) |

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

**Response:** The newly created [Pipeline](#pipeline) object with stages. A taken name returns `{ "Error": "A pipeline named '...' already exists.", "ErrorCode": "Conflict", "Code": "DuplicateEntity" }`.

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

**Response:** [Pipeline](#pipeline) object with stages, or `{ "Error": "Pipeline not found: <name>", "ErrorCode": "NotFound" }`.

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
          "description": { "type": "string", "description": "Stage description" },
          "requiresReview": { "type": "boolean", "description": "Whether the stage's mission waits for human review before the pipeline advances (default false)" },
          "reviewDenyAction": { "type": "string", "description": "What a review denial does: RetryStage, FailPipeline (default RetryStage)" }
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
| `stages` | array | No | New ordered list of pipeline stages (replaces all existing stages if provided); each stage takes the same fields as in `create_pipeline`, including `requiresReview` and `reviewDenyAction` |

**Response:** Updated [Pipeline](#pipeline) object with stages, or `{ "Error": "Pipeline not found: <name>", "ErrorCode": "NotFound" }`.

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

Returns `{ "Error": "Pipeline not found: <name>", "ErrorCode": "NotFound" }` if the name does not exist.
Returns `{ "Error": "Cannot delete built-in pipeline", "ErrorCode": "Conflict" }` if the pipeline is built-in.

---

### get_model_endpoint

Get details of a specific model endpoint (managed embedding or inference provider reference). The stored API key is never returned; `HasApiKey` indicates whether one is set.

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

**Response:** [ModelEndpoint](#modelendpoint) object, or `{ "Error": "Model endpoint not found", "ErrorCode": "NotFound" }`.

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
    "kind": { "type": "string", "description": "Embedding or Inference (default Inference)" },
    "provider": { "type": "string", "description": "Ollama, OpenAI, OpenAICompatible, Anthropic, Gemini, VoyageAI, AzureOpenAI, VertexAI, or Bedrock" },
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
| `kind` | string | No | `Embedding` or `Inference` (default `Inference`) |
| `provider` | string | No | `Ollama`, `OpenAI` (default), `OpenAICompatible`, `Anthropic`, `Gemini`, `VoyageAI`, `AzureOpenAI`, `VertexAI`, or `Bedrock` |
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

**Response:** The newly created [ModelEndpoint](#modelendpoint) object (with `HasApiKey: true` and no API key field). Returns `{ "Error": "...", "ErrorCode": "InvalidArgument" }` when `Anthropic` is paired with `Embedding`, or `VoyageAI` is paired with `Inference`.

**Provider-specific fields** (accepted on create and update): `AzureOpenAI` uses `baseUrl` (resource endpoint), `model` (deployment name), `apiKey`, and optional `apiVersion`. `VertexAI` requires `project` and `region`, with the service-account JSON supplied write-only as `apiKey`; `baseUrl` is an optional override. `Bedrock` requires `region` and `accessKeyId`, with the AWS secret access key supplied write-only as `apiKey`; `model` is the Bedrock model id and `baseUrl` is an optional override.

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
    "provider": { "type": "string", "description": "Ollama, OpenAI, OpenAICompatible, Anthropic, Gemini, VoyageAI, AzureOpenAI, VertexAI, or Bedrock" },
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
| `provider` | string | No | `Ollama`, `OpenAI` (default), `OpenAICompatible`, `Anthropic`, `Gemini`, `VoyageAI`, `AzureOpenAI`, `VertexAI`, or `Bedrock` |
| `baseUrl` | string | No | New provider API base URL |
| `model` | string | No | New model name to target |
| `apiKey` | string | No | New provider API key. Omit to keep the stored key. |
| `dimensionality` | integer | No | Embedding dimensionality |
| `timeoutMs` | integer | No | Request timeout in milliseconds (clamped to [1000, 600000]) |
| `enabled` | boolean | No | Whether the endpoint participates in health sweeps |

**Response:** Updated [ModelEndpoint](#modelendpoint) object, or `{ "Error": "Model endpoint not found", "ErrorCode": "NotFound" }`. A rejected provider/kind combination returns `{ "Error": "...", "ErrorCode": "InvalidArgument" }`.

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

Returns `{ "Error": "Model endpoint not found", "ErrorCode": "NotFound" }` if the ID does not exist.

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
  "Success": true,
  "BaseUrl": "https://api.openai.com/v1",
  "LatencyMs": 92,
  "StatusCode": 200,
  "Error": null,
  "EmbeddingDimensions": 1536,
  "SampleText": null,
  "TimestampUtc": "2026-03-07T12:00:00Z"
}
```

Returns `{ "Error": "Model endpoint not found", "ErrorCode": "NotFound" }` if the ID does not exist.

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
{ "DistinctBaseUrlsProbed": 3 }
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
| `settings.json` | Current Armada server configuration, including `gitHubToken` when configured |
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
| `TotalCaptains` | int | Total registered captains |
| `IdleCaptains` | int | Captains in Idle state |
| `WorkingCaptains` | int | Captains in Working state |
| `StalledCaptains` | int | Captains in Stalled state |
| `ActiveVoyages` | int | Number of active (non-complete) voyages |
| `MissionsByStatus` | object | Map of status string to count (e.g., `{"Pending": 3}`) |
| `Voyages` | array | List of [VoyageProgress](#voyageprogress) objects |
| `RecentSignals` | array | List of recent [Signal](#signal) objects |
| `RemoteTunnel` | [RemoteTunnelStatus](#remotetunnelstatus) | Current outbound remote tunnel status |
| `TimestampUtc` | string | ISO 8601 UTC timestamp |
| `MemoryPressureDeferrals` | long | Cumulative number of dispatch attempts deferred because the host was under memory pressure (the resource-pressure admission gate declined to launch a captain). |

#### RemoteTunnelStatus

| Field | Type | Description |
|---|---|---|
| `Enabled` | bool | Whether the remote tunnel feature is enabled |
| `State` | string | Tunnel state (`Disabled`, `Disconnected`, `Connecting`, `Connected`, `Error`, `Stopping`) |
| `TunnelUrl` | string \| null | Configured or normalized websocket endpoint |
| `InstanceId` | string \| null | Stable instance identifier advertised during handshake |
| `LastConnectAttemptUtc` | string \| null | Last connection attempt timestamp |
| `ConnectedUtc` | string \| null | Last successful connection timestamp |
| `LastHeartbeatUtc` | string \| null | Last heartbeat or inbound tunnel activity timestamp |
| `LastDisconnectUtc` | string \| null | Last disconnect timestamp |
| `LastError` | string \| null | Last recorded error |
| `ReconnectAttempts` | int | Consecutive reconnect attempts since the last successful connection |
| `LatencyMs` | int \| null | Last successful ping/pong latency in milliseconds |
| `CapabilityManifest` | object | Current handshake capability manifest |
| `LastErrorCode` | string \| null | Machine-readable code for `LastError`: a `RemoteTunnelErrorCodes` value for errors the Admiral detects, or the proxy's envelope ErrorCode (for example invalid_handshake). Null when there is no error. |

#### VoyageProgress

| Field | Type | Description |
|---|---|---|
| `Voyage` | object | [Voyage](#voyage) object |
| `TotalMissions` | int | Total missions in this voyage |
| `CompletedMissions` | int | Missions with status Complete |
| `FailedMissions` | int | Missions with status Failed |
| `InProgressMissions` | int | Missions currently in progress |
| `VesselIds` | array | Distinct vessel identifiers referenced by this voyage's missions. |

#### EnumerationResult

Paginated result wrapper returned by `enumerate`.

| Field | Type | Description |
|---|---|---|
| `Success` | bool | Whether the query succeeded |
| `PageNumber` | int | Current page number (1-based) |
| `PageSize` | int | Items per page |
| `TotalPages` | int | Total number of pages |
| `TotalRecords` | long | Total matching records |
| `Objects` | array | Array of entity objects for this page |
| `TotalMs` | double | Query execution time in milliseconds |

#### Fleet

| Field | Type | Description |
|---|---|---|
| `Id` | string | Fleet ID (prefix `flt_`) |
| `Name` | string | Fleet display name |
| `Description` | string \| null | Fleet description |
| `Active` | bool | Whether the fleet is active |
| `CreatedUtc` | string | ISO 8601 creation timestamp |
| `LastUpdateUtc` | string | ISO 8601 last update timestamp |
| `TenantId` | string \| null | Tenant identifier. |
| `UserId` | string \| null | Owning user identifier. |
| `DefaultPipelineId` | string \| null | Default pipeline to use for dispatches to this fleet. Vessel setting overrides fleet setting. Null uses WorkerOnly pipeline. |

#### Vessel

| Field | Type | Description |
|---|---|---|
| `Id` | string | Vessel ID (prefix `vsl_`) |
| `FleetId` | string \| null | Parent fleet ID |
| `Name` | string | Display name |
| `RepoUrl` | string \| null | Git repository URL |
| `LocalPath` | string \| null | Bare repository clone path |
| `WorkingDirectory` | string \| null | User checkout path for merge operations |
| `DefaultBranch` | string | Default branch name (default: `"main"`) |
| `ProjectContext` | string \| null | Project context describing architecture, key files, and dependencies |
| `StyleGuide` | string \| null | Style guide describing naming conventions, patterns, and library preferences |
| `HasGitHubTokenOverride` | bool | Indicates whether a per-vessel GitHub token override is stored. MCP never returns the raw token value. |
| `LandingMode` | string \| null | [LandingModeEnum](#landingmodeenum) - per-vessel landing policy override |
| `BranchCleanupPolicy` | string \| null | [BranchCleanupPolicyEnum](#branchcleanuppolicyenum) - per-vessel branch cleanup override |
| `Active` | bool | Whether the vessel is active |
| `CreatedUtc` | string | ISO 8601 creation timestamp |
| `LastUpdateUtc` | string | ISO 8601 last update timestamp |
| `TenantId` | string \| null | Tenant identifier. |
| `UserId` | string \| null | Owning user identifier. |
| `PreferredHarborId` | string \| null | Optional preferred Harbor (host runner) identifier for this vessel's missions. Honored by the router when the Harbor is otherwise eligible; null lets the router choose. |
| `RequiredCapabilities` | string \| null | Optional comma-separated list of capabilities a Harbor must advertise to run this vessel's missions (for example "claude,gh"). Null or empty imposes no capability requirement. |
| `gitHubTokenOverride` | string \| null | Write-only JSON input for the GitHub token override. This allows create and update requests to supply the token without Armada ever returning it. |
| `EnableModelContext` | bool | Whether model context accumulation is enabled for this vessel. When true, captains are instructed to update the model context with key information discovered during missions. |
| `ModelContext` | string \| null | Agent-accumulated context about this repository. Contains key information discovered by AI agents during missions, such as architectural insights, testing patterns, build quirks, and other knowledge useful for future missions. Updated by agents via update_vessel_context when EnableModelContext is true. |
| `AllowConcurrentMissions` | bool | Whether this vessel allows multiple concurrent missions. Default false. When false, only one mission may be in an active state (Assigned, InProgress, WorkProduced, PullRequestOpen) at a time. |
| `AutoApprove` | bool \| null | Per-vessel override of the captain auto-approve setting for missions on this vessel. Null (the default) leaves the captain's own setting in effect; true or false wins over the captain setting, so a vessel can require CLI captains to run without their auto-approve or permission-bypass flags (or allow them). |
| `RequirePassingChecksToLand` | bool | Whether successful landing requires at least one passing structured check for the current branch or mission context. |
| `ProtectedBranchPatterns` | array | Optional protected-branch glob or exact-match patterns. |
| `SecretScanEnabled` | bool | Whether the pre-land dock-boundary scanner runs built-in secret detection for this vessel. |
| `ProtectedPathPatterns` | array | Protected file-path globs the pre-land scanner blocks a mission from touching (e.g. ".github/**"). |
| `PrivateIdentifierDenylist` | array | Private identifiers (company/domain strings) the pre-land scanner blocks from leaking into added diff lines for public repos. |
| `AutoLandEnabled` | bool | Whether the auto-land predicate gates unattended landing on this vessel. When false, a passing mission lands per the usual review/landing-mode rules; when true, a mission must also satisfy the file/line/path rules below to land without review. |
| `AutoLandMaxFiles` | int | Maximum number of changed files that may auto-land unattended; 0 means no file-count limit. Clamped to non-negative. |
| `AutoLandMaxLines` | int | Maximum number of changed lines (added + removed) that may auto-land unattended; 0 means no line-count limit. Clamped to non-negative. |
| `AutoLandPathAllowGlobs` | array | Glob patterns a changed path must match to be auto-landable. When non-empty, a mission touching any path outside this allow-list holds for review. |
| `AutoLandPathDenyGlobs` | array | Glob patterns that force a hold: a mission touching any matching path never auto-lands. |
| `DefinitionOfDoneEnabled` | bool | Whether the in-dock Definition-of-Done gate runs before a mission is accepted. When true, the build and unit-test commands below run inside the mission's own checkout before landing; a failure blocks acceptance with a classified reason (Compile/TestFail/Timeout/Infra). When false, no gate runs and acceptance follows the usual rules. |
| `DefinitionOfDoneBuildCommand` | string \| null | Shell command that builds the project inside the mission's checkout (e.g. "dotnet build"). A non-zero exit classifies as Compile. Null or empty skips the build phase. |
| `DefinitionOfDoneTestCommand` | string \| null | Shell command that runs unit tests inside the mission's checkout (e.g. "dotnet test"). A non-zero exit classifies as TestFail. Null or empty skips the test phase. |
| `DefinitionOfDoneTimeoutSeconds` | int | Per-phase timeout, in seconds, for each Definition-of-Done command. Exceeding it classifies as Timeout. Clamped to [30, 7200]; defaults to `DefaultDefinitionOfDoneTimeoutSeconds`. |
| `ReleaseBranchPrefix` | string | Prefix used to classify release branches. |
| `HotfixBranchPrefix` | string | Prefix used to classify hotfix branches. |
| `RequirePullRequestForProtectedBranches` | bool | Whether protected branches must land via PR-oriented flow. |
| `RequireMergeQueueForReleaseBranches` | bool | Whether release branches must land via merge queue. |
| `DefaultPipelineId` | string \| null | Default pipeline to use for dispatches to this vessel. Vessel setting overrides fleet setting. Null uses WorkerOnly pipeline. |

#### Voyage

| Field | Type | Description |
|---|---|---|
| `Id` | string | Voyage ID (prefix `vyg_`) |
| `Title` | string | Voyage title |
| `Description` | string \| null | Voyage description |
| `Status` | string | [VoyageStatusEnum](#voyagestatusenum) value |
| `SelectedPlaybooks` | array\<[SelectedPlaybook](#selectedplaybook)\> | Ordered playbook selections recorded on the voyage |
| `CreatedUtc` | string | ISO 8601 creation timestamp |
| `CompletedUtc` | string \| null | ISO 8601 completion timestamp |
| `LastUpdateUtc` | string | ISO 8601 last update timestamp |
| `AutoPush` | bool \| null | Legacy; ignored since 1.0.1 (use `LandingMode`) |
| `AutoCreatePullRequests` | bool \| null | Legacy; ignored since 1.0.1 (use `LandingMode` `PullRequest`) |
| `AutoMergePullRequests` | bool \| null | Override the global auto-merge setting for pull requests opened by the `PullRequest` landing mode |
| `LandingMode` | string \| null | [LandingModeEnum](#landingmodeenum) - per-voyage landing policy override |
| `TenantId` | string \| null | Tenant identifier. |
| `UserId` | string \| null | Owning user identifier. |
| `SourcePlanningSessionId` | string \| null | Source planning session identifier, if this voyage originated from planning. |
| `SourcePlanningMessageId` | string \| null | Source planning message identifier, if dispatch was created from a specific transcript entry. |
| `CaptainOverridesJson` | string \| null | Serialized per-persona captain overrides selected at dispatch, as a JSON array of `CaptainAssignmentOverride`. Resolves the preferred captain and fallback tier for every mission of a given persona in this voyage, including fan-out missions created later. Null or empty means no per-voyage overrides (fall back to persona defaults / normal routing). |

#### Mission

| Field | Type | Description |
|---|---|---|
| `Id` | string | Mission ID (prefix `msn_`) |
| `VoyageId` | string \| null | Parent voyage ID |
| `VesselId` | string \| null | Target vessel ID |
| `CaptainId` | string \| null | Assigned captain ID |
| `Title` | string | Mission title |
| `Description` | string \| null | Mission description |
| `Status` | string | [MissionStatusEnum](#missionstatusenum) value |
| `Priority` | int | Priority (lower = higher priority, default 100) |
| `SelectedPlaybooks` | array\<[SelectedPlaybook](#selectedplaybook)\> | Ordered playbook selections requested for the mission |
| `PlaybookSnapshots` | array\<[MissionPlaybookSnapshot](#missionplaybooksnapshot)\> | Immutable playbook materialization used for execution |
| `ParentMissionId` | string \| null | Parent mission ID for sub-tasks |
| `BranchName` | string \| null | Git branch name created for this mission |
| `DockId` | string \| null | Assigned dock (worktree) ID |
| `ProcessId` | int \| null | OS process ID of the agent working this mission |
| `PrUrl` | string \| null | Pull request URL |
| `CommitHash` | string \| null | Git commit hash (HEAD) captured at mission completion |
| `DiffSnapshot` | string \| null | Always `null` in list/status responses. Use `get_mission_diff` to retrieve the full diff. |
| `CreatedUtc` | string | ISO 8601 creation timestamp |
| `StartedUtc` | string \| null | ISO 8601 start timestamp |
| `CompletedUtc` | string \| null | ISO 8601 completion timestamp |
| `TotalRuntimeMs` | long \| null | Total execution runtime in milliseconds |
| `LastUpdateUtc` | string | ISO 8601 last update timestamp |
| `TenantId` | string \| null | Tenant identifier. |
| `UserId` | string \| null | Owning user identifier. |
| `RequestedCaptainId` | string \| null | Optional preferred (dictated) captain identifier for this mission, referenced by captain id (cpt_ prefix). Resolved at creation from the dispatch payload, the voyage override, or the persona default. When set and that captain is idle, dispatch assigns it (bypassing the persona fence); when it is busy, dispatch falls back to an idle captain at or above `Tier`. Null means no preference (normal persona/tier routing). |
| `AssignedHarborId` | string \| null | Identifier of the Harbor (host runner) this mission was routed to when its dock was provisioned, or null when it runs on the Admiral's own host (Local mode) or has not yet been routed. |
| `Mode` | string | MissionModeEnum value. Execution mode. Implementation (default) is a write mission that lands its diff; Audit and Research are read-only modes that produce a written report and whose empty diff is treated as success rather than a no-op failure. |
| `RedispatchAttempts` | int | Number of times this mission has been automatically re-dispatched after a detected no-op completion. Bounds the auto-retry before the mission is failed and surfaced to the operator. |
| `Tier` | string \| null | CaptainTierEnum value. Optional required capability tier. Dispatch routes the mission to an idle captain at or above this tier (preferring the lowest eligible tier). Null means Standard. |
| `AgentOutput` | string \| null | Accumulated agent stdout output captured during mission execution. Used by architect missions for [ARMADA:MISSION] marker parsing and by pipeline handoff to pass context to the next stage. |
| `Persona` | string \| null | Persona assigned to this mission (e.g. "Worker", "Architect", "Judge"). Null defaults to "Worker" for backward compatibility. |
| `DependsOnMissionId` | string \| null | Mission ID that this mission depends on. When set, this mission cannot be assigned until the dependency completes successfully. Used by pipelines to chain persona stages. |
| `FailureReason` | string \| null | Human-readable reason for failure or landing failure. Set when a mission transitions to Failed or LandingFailed status. |
| `FailureKind` | string \| null | MissionFailureKindEnum value. Structured classification of the failure, set at the point the mission fails (null while the mission has not failed, or for failures recorded before the column existed). Recovery decisions switch on this value; `FailureReason` is human-readable text only and is never parsed. |
| `WaitForVoyageWorkers` | bool | When true, a Worker mission is not assigned while any other Worker mission in the same voyage is still unsettled (not Complete, WorkProduced, Failed, Cancelled, or LandingFailed). Set from the structured architect plan (waitForOtherMissions) so that "run after the other implementation missions" sequencing does not depend on description wording. |
| `RequiresReview` | bool | Whether this mission requires an explicit review approval before the pipeline may continue. Copied from the owning pipeline stage when the mission is created. |
| `ReviewDenyAction` | string | ReviewDenyActionEnum value. Action to take if the review gate for this mission is denied. |
| `ReviewComment` | string \| null | Reviewer comment from the most recent review decision. |
| `ReviewedByUserId` | string \| null | User identifier for the most recent reviewer. |
| `ReviewRequestedUtc` | string \| null | Timestamp when this mission most recently entered the review gate. |
| `ReviewDeadlineUtc` | string \| null | UTC deadline by which a mission parked in Review must be actioned. When elapsed, the review watchdog escalates and frees the retained dock and captain so a forgotten review cannot pin capacity indefinitely. Null when the mission is not awaiting review. |
| `ReviewedUtc` | string \| null | Timestamp when this mission's most recent review decision was made. |

#### Captain

| Field | Type | Description |
|---|---|---|
| `Id` | string | Captain ID (prefix `cpt_`) |
| `Name` | string | Display name |
| `Runtime` | string | [AgentRuntimeEnum](#agentruntimeenum) value |
| `Model` | string \| null | Optional model override for this captain |
| `State` | string | [CaptainStateEnum](#captainstateenum) value |
| `CurrentMissionId` | string \| null | Currently assigned mission ID |
| `CurrentDockId` | string \| null | Currently assigned dock (worktree) ID |
| `ProcessId` | int \| null | OS process ID of the agent |
| `RecoveryAttempts` | int | Number of recovery attempts after stalls |
| `LastHeartbeatUtc` | string \| null | ISO 8601 last heartbeat timestamp |
| `CreatedUtc` | string | ISO 8601 creation timestamp |
| `LastUpdateUtc` | string | ISO 8601 last update timestamp |
| `TenantId` | string \| null | Tenant identifier. |
| `UserId` | string \| null | Owning user identifier. |
| `SupportsPlanningSessions` | bool | Whether this captain's runtime is currently supported by Armada planning sessions. |
| `PlanningSessionSupportReason` | string \| null | Reason the captain cannot be used for planning sessions, if any. |
| `ModelEndpointId` | string \| null | Identifier of the inference ModelEndpoint this captain drives, when `Runtime` is `ApiEndpoint`. Null for CLI-harness runtimes. Must reference a configured Inference endpoint. |
| `SystemInstructions` | string \| null | User-supplied system instructions for this captain. Injected into every mission's instructions before vessel context and mission details. Use this to specialize captain behavior, add guardrails, or provide persistent context. |
| `AllowedPersonas` | string \| null | JSON array of persona names this captain is allowed to fill. Null means the captain can take on any persona. Example: ["Worker", "Judge"] |
| `PreferredPersona` | string \| null | Preferred persona for dispatch routing priority. The Admiral prefers to assign work matching this persona to this captain. |
| `ReasoningEffort` | string \| null | ReasoningEffortEnum value. Optional reasoning-effort level for this captain, translated to each runtime's native control at launch (Claude Code thinking budget, Codex model_reasoning_effort, Mux --effort). Runtimes without a native control ignore it. Null means "use the runtime default". |
| `Tier` | string \| null | CaptainTierEnum value. Optional capability/cost tier used by dispatch to route missions of a given complexity. Null means the tier is auto-classified from the model name at selection time (defaulting to Standard). |
| `RuntimeOptionsJson` | string \| null | Runtime-specific configuration serialized as JSON. Use this for settings that should not be promoted into generic captain fields. |
| `CliPermissionPolicy` | string \| null | CliPermissionPolicyEnum value (`Refuse`, `ApproveInArmada`, `Bypass`) for the captain's own CLI tools, or null to inherit (the legacy `autoApprove` option, then the server default). Changed only with `set_captain_cli_permission_policy`; `update_captain` keeps it. See [CAPTAINS.md](CAPTAINS.md#cli-tool-permissions). |
| `QuarantineUntilUtc` | string \| null | UTC time until which the captain is quarantined and excluded from dispatch selection. Null when the captain is not quarantined; a time in the past means the quarantine has expired and will be lifted on the next health tick. |
| `QuarantineReason` | string \| null | Why the captain was quarantined (e.g. "provider usage limit", "auth failure", "crash loop"). Null when not quarantined. |
| `LastProcessAliveUtc` | string \| null | UTC time the captain's OS process was last observed alive by the supervisor. This is distinct from `LastHeartbeatUtc`, which advances only on real agent output: stall detection compares output-heartbeat age so a process that is alive but silent is still detected as stalled, while liveness telemetry stays fresh here. |

#### CaptainToolAccessResult

| Field | Type | Description |
|---|---|---|
| `CaptainId` | string | Captain ID |
| `CaptainName` | string | Captain display name |
| `Runtime` | string | [AgentRuntimeEnum](#agentruntimeenum) value |
| `ToolsAccessible` | bool | Whether Armada currently considers the catalog reachable through this captain |
| `AvailabilityVerified` | bool | Whether Armada actively verified availability instead of inferring it |
| `AvailabilitySource` | string | Machine-readable availability source such as `mux-probe` or `runtime-assumption` |
| `Summary` | string | Human-readable explanation of availability and caveats |
| `EndpointName` | string \| null | Mux endpoint name when applicable |
| `ToolsEnabled` | bool \| null | Whether the runtime reported tool calling enabled when applicable |
| `EffectiveToolCount` | int \| null | Runtime-reported total tool count when applicable |
| `ArmadaToolCount` | int | Number of Armada MCP tools in the returned catalog |
| `Tools` | array | Ordered list of [CaptainToolSummary](#captaintoolsummary) objects |
| `ConfiguredServerCount` | int | Number of configured external or internal tool sources Armada inspected. |
| `AskApprovalGated` | bool | True when Ask Armada thread turns run by this captain connect to Armada's MCP server with a thread-scoped token, so mutating Armada tool calls are held as approval cards. False means the captain's Armada actions in an Ask thread run without approval cards. |
| `ReachableServerCount` | int | Number of configured sources that Armada successfully reached. |
| `Servers` | array | Source summaries discovered for this captain runtime. |

#### CaptainToolSummary

| Field | Type | Description |
|---|---|---|
| `Name` | string | Tool name |
| `Description` | string | Human-readable tool description |
| `InputSchemaJson` | string \| null | Serialized JSON input schema when available |
| `RegistrationSource` | string | Registration origin for the tool, such as an MCP server name or the internal runtime. |
| `SourceKind` | string | CaptainToolSourceKindEnum value. Source kind for the tool, such as MCP server or internal runtime support. |

#### Signal

| Field | Type | Description |
|---|---|---|
| `Id` | string | Signal ID (prefix `sig_`) |
| `FromCaptainId` | string \| null | Sender captain ID (null = Admiral) |
| `ToCaptainId` | string \| null | Recipient captain ID (null = Admiral) |
| `Type` | string | [SignalTypeEnum](#signaltypeenum) value |
| `Payload` | string \| null | JSON payload string |
| `Read` | bool | Whether the signal has been read |
| `CreatedUtc` | string | ISO 8601 creation timestamp |
| `TenantId` | string \| null | Tenant identifier. |
| `UserId` | string \| null | Owning user identifier. |

#### ArmadaEvent

| Field | Type | Description |
|---|---|---|
| `Id` | string | Event ID (prefix `evt_`) |
| `EventType` | string | Event type (e.g., `"mission.created"`, `"captain.stalled"`) |
| `EntityType` | string \| null | Entity type (e.g., `"mission"`, `"captain"`, `"voyage"`) |
| `EntityId` | string \| null | Entity ID |
| `CaptainId` | string \| null | Related captain ID |
| `MissionId` | string \| null | Related mission ID |
| `VesselId` | string \| null | Related vessel ID |
| `VoyageId` | string \| null | Related voyage ID |
| `Message` | string | Human-readable event description |
| `Payload` | string \| null | Optional JSON payload |
| `CreatedUtc` | string | ISO 8601 creation timestamp |
| `TenantId` | string \| null | Tenant identifier. |
| `UserId` | string \| null | Owning user identifier. |

#### Dock

| Field | Type | Description |
|---|---|---|
| `Id` | string | Dock ID (prefix `dck_`) |
| `VesselId` | string | Parent vessel ID |
| `CaptainId` | string \| null | Assigned captain ID |
| `BranchName` | string \| null | Git branch name |
| `WorktreePath` | string \| null | Filesystem path to the worktree |
| `Active` | bool | Whether the dock is active |
| `CreatedUtc` | string | ISO 8601 creation timestamp |
| `LastUpdateUtc` | string | ISO 8601 last update timestamp |
| `TenantId` | string \| null | Tenant identifier. |
| `UserId` | string \| null | Owning user identifier. |
| `HarborId` | string \| null | Identifier of the Harbor (host runner) that owns this dock, or null when the dock is on the Admiral's own host (Local mode). A dock's worktree lives on exactly one host, so this pins the mission's later host operations to that Harbor (dock affinity). |
| `State` | string | DockStateEnum value. Authoritative lifecycle state of the dock. Occupancy is tracked here rather than inferred from captain/mission rows, so a stuck or orphaned dock can be detected and reclaimed deterministically. |
| `LeaseExpiresUtc` | string \| null | UTC time at which the current lease expires. A leased dock whose lease has elapsed without renewal is eligible for reclamation even in a multi-instance deployment. Null when the dock is not leased. |
| `OwnerToken` | string \| null | Opaque token identifying the current lease holder (typically the owning captain plus a generation stamp). Used for compare-and-swap lease acquisition and renewal so two instances cannot both claim the same dock. Null when the dock is not leased. |
| `GitAnchorsJson` | string \| null | Resolved git anchors captured at dock provisioning, serialized as JSON (start commit, target branch, working branch, recent-commit and subject-term summaries). This is a documented, intentional raw-JSON snapshot for the dashboard and for a resuming captain -- not a general data blob. Null when anchors were not resolved. |

#### Playbook

| Field | Type | Description |
|---|---|---|
| `Id` | string | Playbook ID (prefix `pbk_`) |
| `TenantId` | string \| null | Owning tenant |
| `UserId` | string \| null | Owning user |
| `FileName` | string | Markdown file name |
| `Description` | string \| null | Human-readable description |
| `Content` | string | Markdown body |
| `Active` | bool | Whether the playbook is available for new selections |
| `CreatedUtc` | string | ISO 8601 creation timestamp |
| `LastUpdateUtc` | string | ISO 8601 last update timestamp |
| `Scope` | string | Armada.Core.Enums.ScopeEnum value. Ownership scope: tenant-wide playbooks are visible to everyone in the tenant but editable only by tenant/global admins; user-specific playbooks are owned by `UserId`. Defaults tenant-wide. |

#### SelectedPlaybook

| Field | Type | Description |
|---|---|---|
| `PlaybookId` | string | Selected playbook ID |
| `DeliveryMode` | [PlaybookDeliveryModeEnum](#playbookdeliverymodeenum) | How the playbook is delivered to the model |

#### MissionPlaybookSnapshot

| Field | Type | Description |
|---|---|---|
| `PlaybookId` | string \| null | Source playbook ID |
| `FileName` | string | Source file name |
| `Description` | string \| null | Source description |
| `Content` | string | Frozen markdown body used for execution |
| `DeliveryMode` | [PlaybookDeliveryModeEnum](#playbookdeliverymodeenum) | Resolved delivery mode |
| `ResolvedPath` | string \| null | Absolute runtime path when materialized outside the worktree |
| `WorktreeRelativePath` | string \| null | Relative dock path when attached into the worktree |
| `SourceLastUpdateUtc` | string | ISO 8601 source update timestamp captured into the snapshot |

#### MergeEntry

| Field | Type | Description |
|---|---|---|
| `Id` | string | Merge entry ID (prefix `mrg_`) |
| `MissionId` | string \| null | Associated mission ID |
| `VesselId` | string | Target vessel ID |
| `BranchName` | string | Branch to merge |
| `TargetBranch` | string | Target branch (default `"main"`) |
| `Status` | string | [MergeStatusEnum](#mergestatusenum) value |
| `Priority` | int | Queue priority (lower = higher) |
| `BatchId` | string \| null | Batch identifier during testing |
| `TestCommand` | string \| null | Custom test command |
| `TestOutput` | string \| null | Test output/error |
| `TestExitCode` | int \| null | Test exit code |
| `CreatedUtc` | string | ISO 8601 creation timestamp |
| `LastUpdateUtc` | string | ISO 8601 last update timestamp |
| `TestStartedUtc` | string \| null | ISO 8601 test start timestamp |
| `CompletedUtc` | string \| null | ISO 8601 completion timestamp |
| `TenantId` | string \| null | Tenant identifier. |
| `UserId` | string \| null | Owning user identifier. |
| `RetryCount` | int | Number of times processing this entry has been attempted. Bounds automatic retries so a persistently failing entry is not retried forever. |
| `LeaseExpiresUtc` | string \| null | UTC time at which the current processing lease on this entry expires. A non-terminal entry (e.g. stuck in Testing) whose lease has elapsed is recovered by the queue driver rather than blocking the queue head indefinitely. Null when not being processed. |

#### Harbor

A registered host-side runner. Only `name`, `maxConcurrentJobs`, and `enabled` are operator-editable via MCP; the remaining runtime fields are reported by the link.

| Field | Type | Description |
|---|---|---|
| `Id` | string | Harbor ID (prefix `hbr_`) |
| `TenantId` | string \| null | Owning tenant ID |
| `UserId` | string \| null | Owning user ID |
| `Name` | string | Human-facing Harbor name |
| `Capabilities` | array | Advertised [HarborCapability](#harborcapability) entries |
| `ConnectionStatus` | string | [HarborConnectionStatusEnum](#harborconnectionstatusenum) value |
| `MaxConcurrentJobs` | int | Maximum concurrent jobs the Harbor accepts (default 4, minimum 1) |
| `Enabled` | bool | Whether the Harbor is enabled for routing (default true) |
| `ProtocolVersion` | string \| null | Protocol version reported at handshake |
| `OsPlatform` | string \| null | OS platform reported at handshake (e.g. `Windows`, `Linux`, `macOS`) |
| `Architecture` | string \| null | Processor architecture reported at handshake (e.g. `X64`, `Arm64`) |
| `LastSeenUtc` | string \| null | ISO 8601 last heartbeat or message timestamp |
| `LastConnectedUtc` | string \| null | ISO 8601 last link-establishment timestamp |
| `CreatedUtc` | string | ISO 8601 creation timestamp |
| `LastUpdateUtc` | string | ISO 8601 last update timestamp |

#### HarborCapability

| Field | Type | Description |
|---|---|---|
| `Name` | string | Capability name (e.g. a runtime like `claude` or a host tool like `git`) |
| `Available` | bool | Whether the capability is currently available on the host |
| `Detail` | string \| null | Optional human-readable detail (e.g. a version string) |

#### PromptTemplate

| Field | Type | Description |
|---|---|---|
| `Id` | string | Prompt template ID |
| `Name` | string | Template name (e.g. `"mission.rules"`, `"persona.worker"`) |
| `Description` | string \| null | Template description |
| `Category` | string \| null | Template category |
| `Content` | string | Template content |
| `IsBuiltIn` | bool | Whether this is a built-in template |
| `Active` | bool | Whether the template is active |
| `TenantId` | string \| null | Tenant identifier. |
| `UserId` | string \| null | Owning user identifier (null for tenant-wide objects). |
| `Scope` | string | Armada.Core.Enums.ScopeEnum value. Ownership scope: tenant-wide objects are visible to everyone in the tenant but editable only by tenant/global admins; user-specific objects are owned by `UserId`. Defaults tenant-wide. |
| `CreatedUtc` | string | Creation timestamp in UTC. |
| `LastUpdateUtc` | string | Last update timestamp in UTC. |

#### Persona

| Field | Type | Description |
|---|---|---|
| `Id` | string | Persona ID |
| `Name` | string | Persona name |
| `Description` | string \| null | Persona description |
| `PromptTemplateName` | string | Name of the prompt template used by this persona |
| `IsBuiltIn` | bool | Whether this is a built-in persona |
| `Active` | bool | Whether the persona is active |
| `TenantId` | string \| null | Tenant identifier. |
| `UserId` | string \| null | Owning user identifier (null for tenant-wide objects). |
| `Scope` | string | Armada.Core.Enums.ScopeEnum value. Ownership scope: tenant-wide objects are visible to everyone in the tenant but editable only by tenant/global admins; user-specific objects are owned by `UserId`. Defaults tenant-wide. |
| `DefaultCaptainId` | string \| null | Optional default (preferred) captain for this persona, referenced by captain id (cpt_ prefix). At dispatch, each pipeline step for this persona is pre-filled with this captain, and any mission created for this persona inherits it as its preferred captain when none is explicitly dictated (including fan-out missions produced by an Architect stage). Null means no default; a dangling id (captain later deleted) resolves to "no default" at assignment time and falls back to normal persona/tier routing. |
| `CreatedUtc` | string | Creation timestamp in UTC. |
| `LastUpdateUtc` | string | Last update timestamp in UTC. |

#### Pipeline

| Field | Type | Description |
|---|---|---|
| `Id` | string | Pipeline ID |
| `Name` | string | Pipeline name |
| `Description` | string \| null | Pipeline description |
| `Stages` | array | Ordered list of [PipelineStage](#pipelinestage) objects |
| `IsBuiltIn` | bool | Whether this is a built-in pipeline |
| `Active` | bool | Whether the pipeline is active |
| `TenantId` | string \| null | Tenant identifier. |
| `UserId` | string \| null | Owning user identifier (null for tenant-wide objects). |
| `Scope` | string | Armada.Core.Enums.ScopeEnum value. Ownership scope: tenant-wide objects are visible to everyone in the tenant but editable only by tenant/global admins; user-specific objects are owned by `UserId`. Defaults tenant-wide. |
| `CreatedUtc` | string | Creation timestamp in UTC. |
| `LastUpdateUtc` | string | Last update timestamp in UTC. |

#### PipelineStage

| Field | Type | Description |
|---|---|---|
| `PersonaName` | string | Persona name for this stage |
| `IsOptional` | bool | Reserved: stored and returned, but dispatch runs every stage regardless |
| `Description` | string \| null | Stage description |
| `Id` | string | Unique identifier. |
| `PipelineId` | string \| null | Pipeline identifier this stage belongs to. |
| `Order` | int | Execution order within the pipeline (1-based). |
| `RequiresReview` | bool | Whether this stage requires an explicit review approval before the pipeline may continue. |
| `ReviewDenyAction` | string | ReviewDenyActionEnum value. Action to take when the review gate is denied. |

#### ModelEndpoint

A managed reference to an external embedding or inference model behind a provider API. The `apiKey` is write-only: it is accepted on create/update but is never returned on reads. Reads expose `HasApiKey` instead.

| Field | Type | Description |
|---|---|---|
| `Id` | string | Model endpoint ID (prefix `mep_`) |
| `TenantId` | string \| null | Owning tenant ID |
| `UserId` | string \| null | Owning user ID |
| `Name` | string | Display name |
| `Kind` | string | `Embedding` or `Inference` |
| `Provider` | string | `Ollama`, `OpenAI`, `OpenAICompatible`, `Anthropic`, `Gemini`, `VoyageAI`, `AzureOpenAI`, `VertexAI`, or `Bedrock` |
| `BaseUrl` | string | Provider API base URL. For Azure OpenAI this is the resource endpoint; for Vertex AI and Bedrock it is an optional override (the endpoint is derived from `region`). |
| `Model` | string \| null | Model name to target. For Azure OpenAI this is the deployment name; for Bedrock, the Bedrock model id. |
| `Region` | string \| null | Cloud region. Required for `VertexAI` and `Bedrock`. |
| `Project` | string \| null | GCP project id. Required for `VertexAI`. |
| `ApiVersion` | string \| null | API version for `AzureOpenAI` (defaults to the provider's current GA version when omitted). |
| `AccessKeyId` | string \| null | AWS access key id for `Bedrock`. The paired secret access key is supplied write-only via `apiKey`. |
| `Dimensionality` | int | Embedding dimensionality (default 0) |
| `TimeoutMs` | int | Request timeout in milliseconds (default 120000, clamped to [1000, 600000]) |
| `Enabled` | bool | Whether the endpoint participates in health sweeps (default true) |
| `HasApiKey` | bool | Read-only. Whether a provider key/credential is stored. For `AzureOpenAI` this is the API key, for `VertexAI` the service-account JSON, for `Bedrock` the AWS secret access key. |
| `HealthStatus` | string | `Unknown`, `Healthy`, or `Unhealthy` |
| `LastHealthCheckUtc` | string \| null | ISO 8601 timestamp of the last probe |
| `LastHealthError` | string \| null | Error text from the last failed probe |
| `LastLatencyMs` | int \| null | Latency of the last probe in milliseconds |
| `HealthHistory` | array | Rolling series of recent probes (oldest first), each `{ timestampUtc, success }`, capped at 500 |
| `UptimePercentage` | double | Read-only. Percentage of retained probes that succeeded (0-100), derived from `healthHistory` |
| `ConsecutiveSuccesses` | int | Read-only. Trailing run of successful probes |
| `ConsecutiveFailures` | int | Read-only. Trailing run of failed probes |
| `FirstHealthCheckUtc` | string \| null | Read-only. Earliest retained probe timestamp |
| `LastHealthyUtc` | string \| null | Read-only. Most recent successful probe timestamp |
| `LastUnhealthyUtc` | string \| null | Read-only. Most recent failed probe timestamp |
| `CreatedUtc` | string | ISO 8601 creation timestamp |
| `LastUpdateUtc` | string | ISO 8601 last update timestamp |
| `Scope` | string | ScopeEnum value. Ownership scope: a tenant-wide endpoint is visible to everyone in the tenant but editable only by tenant/global admins; a user-specific endpoint is owned by `UserId`. Defaults to tenant-wide (existing rows and admin-created rows); regular users create user-specific endpoints. |
| `apiKey` | string \| null | Write-only JSON input for the API key. Lets create/update supply the key without Armada ever returning it. |

`Anthropic` cannot be paired with `kind` `Embedding`, and `VoyageAI` cannot be paired with `kind` `Inference`; both combinations are rejected with an error.

#### ModelEndpointProbeResult

Returned by `validate_model_endpoint`.

| Field | Type | Description |
|---|---|---|
| `Success` | bool | Whether the probe request succeeded |
| `BaseUrl` | string \| null | Base URL that was probed |
| `LatencyMs` | int | Round-trip latency in milliseconds |
| `StatusCode` | int \| null | HTTP status code returned by the provider, when available |
| `Error` | string \| null | Error text when the probe failed |
| `EmbeddingDimensions` | int \| null | Dimensionality returned by an embedding probe |
| `SampleText` | string \| null | Sample completion text returned by an inference probe |
| `TimestampUtc` | string | ISO 8601 timestamp of when the probe ran |

#### ModelEndpointHealthSweepResponse

Returned by `health_check_model_endpoints`.

| Field | Type | Description |
|---|---|---|
| `DistinctBaseUrlsProbed` | int | Number of distinct base URLs probed during the sweep |

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
| `None` | No automated landing: the mission stops at `WorkProduced` with its work on the branch, to be merged by hand (the dashboard and TUI offer Merge in Manage Branches). The Admiral moves it to `Complete` once its commit is contained in the target branch, checked right after a Manage Branches merge and on every health check |

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
        "text": "{\"TotalCaptains\":4,\"IdleCaptains\":1,...}"
      }
    ]
  }
}
```
