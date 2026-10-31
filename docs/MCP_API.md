# Armada MCP API Reference

**Version:** 1.2.0
**Default URL:** `http://localhost:7891/mcp`
**Protocol:** [Model Context Protocol](https://modelcontextprotocol.io/) (MCP), Streamable HTTP transport
**Server Library:** Voltaic (McpHttpServer)
**Server Name:** `Armada`

## Remote Control Note

`v1.2.0` does not proxy Armada MCP traffic through the new remote-control tunnel or `Armada.Proxy`.

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
    - [list_voyage_progress](#list_voyage_progress)
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
    - [approve_mission_review](#approve_mission_review)
    - [deny_mission_review](#deny_mission_review)
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
  - **Workflow Profiles**
    - [get_workflow_profile](#get_workflow_profile)
    - [create_workflow_profile](#create_workflow_profile)
    - [update_workflow_profile](#update_workflow_profile)
    - [delete_workflow_profile](#delete_workflow_profile)
    - [delete_workflow_profiles](#delete_workflow_profiles)
    - [validate_workflow_profile](#validate_workflow_profile)
    - [resolve_workflow_profile](#resolve_workflow_profile)
    - [preview_workflow_profile](#preview_workflow_profile)
  - **Fleet Actions**
    - [create_fleet_action](#create_fleet_action)
    - [update_fleet_action](#update_fleet_action)
    - [delete_fleet_action](#delete_fleet_action)
    - [run_fleet_action](#run_fleet_action)
    - [fleet_action_run_status](#fleet_action_run_status)
    - [cancel_fleet_action_run](#cancel_fleet_action_run)
  - **CLI Permissions**
    - [cli_permission_prompt](#cli_permission_prompt)
    - [get_cli_permission_request](#get_cli_permission_request)
    - [decide_cli_permission_request](#decide_cli_permission_request)
    - [create_cli_permission_rule](#create_cli_permission_rule)
    - [update_cli_permission_rule](#update_cli_permission_rule)
    - [delete_cli_permission_rule](#delete_cli_permission_rule)
    - [set_captain_cli_permission_policy](#set_captain_cli_permission_policy)
  - **Stage Outcomes**
    - [list_stage_outcomes](#list_stage_outcomes)
    - [get_stage_outcome](#get_stage_outcome)
    - [classify_stage_outcome](#classify_stage_outcome)
    - [reclassify_stage_outcomes](#reclassify_stage_outcomes)
    - [draft_failure_cause_rule](#draft_failure_cause_rule)
    - [list_failure_cause_rules](#list_failure_cause_rules)
    - [get_failure_cause_rule](#get_failure_cause_rule)
    - [create_failure_cause_rule](#create_failure_cause_rule)
    - [update_failure_cause_rule](#update_failure_cause_rule)
    - [delete_failure_cause_rule](#delete_failure_cause_rule)
  - **Handoff Artifacts**
    - [get_handoff_artifact](#get_handoff_artifact)
    - [list_handoff_artifacts](#list_handoff_artifacts)
  - **Evaluate**
    - [evaluate_captain](#evaluate_captain)
    - [get_model_probe_run](#get_model_probe_run)
    - [list_model_probe_runs](#list_model_probe_runs)
    - [cancel_model_probe_run](#cancel_model_probe_run)
  - **Model Profiles**
    - [get_model_profile](#get_model_profile)
    - [list_model_profiles](#list_model_profiles)
    - [get_model_scorecard](#get_model_scorecard)
  - **Model Alerts**
    - [list_model_alerts](#list_model_alerts)
    - [get_model_alert](#get_model_alert)
    - [evaluate_model_alerts](#evaluate_model_alerts)
    - [get_model_alert_thresholds](#get_model_alert_thresholds)
    - [update_model_alert_thresholds](#update_model_alert_thresholds)
    - [apply_model_alert_action](#apply_model_alert_action)
    - [ignore_model_alert](#ignore_model_alert)
  - **Fixture Battery**
    - [list_battery_packs](#list_battery_packs)
    - [run_battery](#run_battery)
    - [get_battery_run](#get_battery_run)
    - [list_battery_results](#list_battery_results)
  - **Black Box**
    - [get_blackbox](#get_blackbox)
    - [read_blackbox_section](#read_blackbox_section)
    - [list_blackbox_objects](#list_blackbox_objects)
    - [read_blackbox_object](#read_blackbox_object)
    - [list_blackbox_entries](#list_blackbox_entries)
    - [search_blackbox](#search_blackbox)
    - [list_blackbox_notes](#list_blackbox_notes)
    - [get_blackbox_bundle](#get_blackbox_bundle)
    - [list_blackbox_hotspots](#list_blackbox_hotspots)
    - [list_blackbox_fingerprints](#list_blackbox_fingerprints)
    - [list_blackbox_resilience_gaps](#list_blackbox_resilience_gaps)
    - [get_blackbox_fingerprint_trend](#get_blackbox_fingerprint_trend)
    - [get_blackbox_retention](#get_blackbox_retention)
    - [get_blackbox_storage](#get_blackbox_storage)
    - [annotate_blackbox](#annotate_blackbox)
    - [resolve_blackbox](#resolve_blackbox)
    - [run_blackbox_recovery](#run_blackbox_recovery)
    - [apply_blackbox_recommendation](#apply_blackbox_recommendation)
    - [pin_blackbox](#pin_blackbox)
    - [delete_blackbox_entries](#delete_blackbox_entries)
    - [purge_blackbox](#purge_blackbox)
    - [run_blackbox_gc](#run_blackbox_gc)
    - [export_blackbox_fixture](#export_blackbox_fixture)
    - [reanalyze_blackbox](#reanalyze_blackbox)
    - [Resources and prompts](#resources-and-prompts)
    - [Reviewing a black box and taking action](#reviewing-a-black-box-and-taking-action)
  - **Ask Armada**
    - [list_ask_threads](#list_ask_threads)
    - [get_ask_thread](#get_ask_thread)
    - [create_ask_thread](#create_ask_thread)
    - [update_ask_thread](#update_ask_thread)
    - [delete_ask_thread](#delete_ask_thread)
    - [delete_ask_threads](#delete_ask_threads)
    - [list_ask_messages](#list_ask_messages)
    - [send_ask_message](#send_ask_message)
    - [cancel_ask_turn](#cancel_ask_turn)
    - [summarize_ask_thread](#summarize_ask_thread)
    - [mark_ask_thread_read](#mark_ask_thread_read)
    - [list_ask_quick_actions](#list_ask_quick_actions)
    - [run_ask_quick_action](#run_ask_quick_action)
    - [approve_ask_proposal](#approve_ask_proposal)
    - [reject_ask_proposal](#reject_ask_proposal)
    - [get_ask_work](#get_ask_work)
    - [list_ask_work](#list_ask_work)
    - [list_ask_work_missions](#list_ask_work_missions)
    - [list_ask_work_targets](#list_ask_work_targets)
    - [list_ask_proposals](#list_ask_proposals)
    - [list_ask_tool_calls](#list_ask_tool_calls)
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
    - [list_harbor_jobs](#list_harbor_jobs)
    - [stop_harbor_job](#stop_harbor_job)
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
    - [duplicate_model_endpoint](#duplicate_model_endpoint)
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
camelCase exceptions are the `list_prompt_templates` envelope and rows, the `delete_cli_permission_rule` result, and the `cli_permission_prompt` answer). Unlike REST, MCP results keep null-valued properties. Enums are emitted as their
names, as on REST. New tools, new optional arguments,
and new result fields may be added in minor releases. Tools whose description starts with `[Experimental]` (the Harbor
tools `get_harbor`, `get_harbor_metrics`, `create_harbor`, `update_harbor`, `delete_harbor`, `set_harbor_enabled`, `list_harbor_jobs`, `stop_harbor_job`) are excluded from the
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
- Manage workflow profiles (the build, test, release, and deploy commands stage checks run) and preview what a vessel resolves to
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

Drop `--scope user` to add it for the current project only. While the Admiral listens on localhost (the default), no token or header is required; if you changed `mcpPort`, substitute your port. When the Admiral listens on any other address (for example in Docker), every MCP call needs a credential: add `--header "Authorization: Bearer <token>"` (or `X-Api-Key`). `armada mcp install --server <url> --token <token>` (or `--profile <name>`) writes the remote URL and the header for Claude Code, Gemini CLI, Cursor, Mux, OpenCode, and pi, and configures Codex to read the token from `ARMADA_TOKEN`; without a remote target it writes the local, credential-less entries. See [REMOTE_SERVER.md](REMOTE_SERVER.md#mcp-clients-claude-code-codex-gemini-cli-cursor-mux-opencode).

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

Armada also supports an MCP stdio transport (`armada mcp stdio`) for direct process-based communication. Both transports register the same tool names via `McpToolRegistrar`. The stdio server runs standalone against the database (no Admiral process), so the tools that need the Admiral answer `ErrorCode` `Unavailable` over stdio: `stop_server`, and the fleet action tools (`create_fleet_action`, `update_fleet_action`, `delete_fleet_action`, `run_fleet_action`, `fleet_action_run_status`, `cancel_fleet_action_run`), whose runs are executed and tracked by the Admiral's runner, and the [black box](#black-box) tools (and `enumerate` `entityType` `blackbox`), which need the Admiral's black box service. The black box resources and prompts are not served over stdio. Use stdio when running Armada as a child process of an MCP client.

### Port Configuration

| Setting | Default | Description |
|---|---|---|
| `mcpPort` | `7891` | MCP HTTP server port |
| `rest.hostname` | `localhost` | Bind hostname |

Keys are the camelCase names used in `settings.json`. The MCP port can be configured in the Armada settings file. The hostname is shared with the REST API configuration.

---

## Authentication

MCP is **authenticated by default** (Decision D2 in archive/V1_READINESS.md). Present a credential with the same headers the
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
| Authenticated | Reads (`status`, `enumerate`, `get_*`, `list_*`, `mission_status`, `voyage_status`, logs, diffs, `vessel_health`, `search_memory`, `token_usage_summary`, ...), writes to caller-owned resources (memories, model endpoints, harbors), persona, pipeline, and prompt-template writes (as on REST: a regular user creates only `UserSpecific` ones and edits only their own), and `cli_permission_prompt` and `decide_cli_permission_request` (the handlers check the caller; see [CLI Permissions](#cli-permissions)), and the black box readers plus `annotate_blackbox`, `resolve_blackbox`, `pin_blackbox`, `run_blackbox_recovery` and `apply_blackbox_recommendation` (a recovery step is then authorized as its own tool; see [Black Box](#black-box)) |
| TenantAdmin | Every other write or execution (dispatch, captains, missions, voyages, docks, merge queue, signals, events, objectives, backlog, playbooks, workflow profiles (including `validate_workflow_profile`, as on REST), releases, deployments, runbooks, check runs, fleet actions, vessel import, vessel health), and the black box `get_blackbox_retention`, `delete_blackbox_entries` and `reanalyze_blackbox` |
| AdminOnly | `backup`, `restore`, `stop_server` (a credential is required even on loopback; use `X-Api-Key` from `settings.json`), and the black box `get_blackbox_storage`, `purge_blackbox`, `run_blackbox_gc` and `export_blackbox_fixture` |

A denied call returns an `McpToolError` with `ErrorCode` `Forbidden` whose message names the required level. The full per-tool list is in
[SECURITY_REVIEW.md](SECURITY_REVIEW.md#mcp-tools); a test fails when a registered tool has no declaration.

### MCP Authentication Scope

Owned (Category A) entities - fleets, vessels, captains, missions, voyages, docks, signals, events, merge queue, objectives/backlog - are scoped to the authenticated caller across `enumerate` and the entity tools. Every other `enumerate` entity type uses the same scope as its REST list: jobs, releases, deployments and check runs by tenant and owner; model endpoints, harbors, personas, prompt templates, pipelines, playbooks, workflow profiles, project profiles and skills by tenant plus their per-object visibility (`Scope`); vessel import batches, fleet actions and vessel health by tenant. Only a global admin enumerates across tenants. Per-object ownership editing of configuration entities is enforced by their services.

### Mission-Scoped Captain Calls

When the Admiral launches a captain for a mission it mints a **mission-scoped session token** (setting
`Mcp.MissionScopedTokens`, default true) and binds the captain's Armada MCP connection to it, sent as `X-Token`:

- Claude Code, Codex, OpenCode and Mux captains get the same per-invocation binding as Ask turns (see below); no client
  file is written into the repository worktree. pi captains get a `.pi/mcp.json` in the worktree (header
  `X-Token: ${ARMADA_MCP_TOKEN}`, the token only in the environment), excluded from git through the worktree's
  `info/exclude` and deleted after the run. With `IsolateCaptainLaunch` on, the isolation plan carries the token
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

- **Read-only tools** run normally: `status`, `enumerate` (including the `inbox`, `cli_permission_rules`, and
  `cli_permission_requests` entity types), `voyage_status`, `mission_status`,
  `fleet_action_run_status`, `vessel_health`, `papercut_summary`, `token_usage_summary`, `search_memory`,
  `evaluate_autoland`, every `get_*` reader (`get_backlog_item`, `get_backlog_planning_session`,
  `get_backlog_refinement_session`, `get_captain`, `get_captain_log`, `get_captain_tools`, `get_check_run`,
  `get_deployment`, `get_dock`, `get_fleet`, `get_harbor`, `get_harbor_metrics`, `get_memory`, `get_merge_entry`, `get_mission_diff`,
  `get_mission_log`, `get_model_endpoint`, `get_objective`, `get_persona`, `get_pipeline`, `get_playbook`,
  `get_prompt_template`, `get_release`, `get_runbook`, `get_runbook_execution`, `get_vessel`, `get_workflow_profile`), the
  workflow profile readers (`validate_workflow_profile`, `resolve_workflow_profile`, `preview_workflow_profile`; validate
  saves nothing), and the `list_*` readers
  (`list_backlog`, `list_backlog_refinement_sessions`, `list_backlog_refinement_session_messages`,
  `list_backlog_planning_session_messages`, `list_objectives`, `list_prompt_templates`,
  plus `get_cli_permission_request`), and the Ask Armada
  readers (`list_ask_threads`, `get_ask_thread`, `list_ask_messages`, `list_ask_quick_actions`, `get_ask_work`,
  `list_ask_work`, `list_ask_work_missions`, `list_ask_work_targets`, `list_ask_proposals`, `list_ask_tool_calls`), and the black box
  readers (`get_blackbox`, `read_blackbox_section`, `list_blackbox_objects`, `read_blackbox_object`,
  `list_blackbox_entries`, `search_blackbox`, `list_blackbox_notes`, `get_blackbox_bundle`, `list_blackbox_hotspots`,
  `list_blackbox_fingerprints`, `list_blackbox_resilience_gaps`, `get_blackbox_fingerprint_trend`, `get_blackbox_retention`, `get_blackbox_storage`). The list lives in
  `AskToolPolicy`; any tool not on it (including tools added later) is treated as state-changing.
- **`cli_permission_prompt`** (the captain CLI's own permission prompt) runs without becoming a proposal: an approver
  answers it as a [CLI permission request](#cli-permissions).
- **CLI permission decisions, rules, and policies** (`decide_cli_permission_request`, `create_cli_permission_rule`,
  `update_cli_permission_rule`, `delete_cli_permission_rule`, `set_captain_cli_permission_policy`) are refused with
  `ErrorCode` `Forbidden` and never proposed, so a captain cannot get its own prompts approved. The state-changing
  [Ask Armada tools](#ask-armada) are refused the same way, so a captain cannot approve or reject its own proposals,
  start turns, or change or delete conversations.
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

Get aggregate status of active work in Armada: captain counts by state, mission counts by status, active voyage counts
(`ActiveVoyages`, `InProgressVoyages`, `OpenVoyages`), and the 10 most recent signals. It embeds no voyage list; page the
active voyages with progress through [list_voyage_progress](#list_voyage_progress). A global administrator sees every tenant; any other caller sees only its own tenant (the
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
  "InProgressVoyages": 1,
  "OpenVoyages": 1,
  "MemoryPressureDeferrals": 0,
  "MissionsByStatus": {
    "Pending": 3,
    "InProgress": 2,
    "Complete": 10,
    "Failed": 1
  },
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
      "ArmadaVersion": "1.2.0",
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

### token_usage_summary

For daily token heatmaps, supply explicit `fromUtc`/`toUtc` and `bucketMinutes: 1440`. Optional `sourceId` isolates a synthetic batch; `syntheticCount` reports included synthetic rows. Synthetic imports are available only through the global-admin REST endpoint, not MCP.

Summarize model token usage over a time window. Returns time buckets (each with a per-model breakdown), a whole-window per-model aggregate ordered most-used first, and grand totals. Use it to answer *"how many tokens has each model used?"* or *"what's our token usage over the last week?"*.

Counts are normalized across providers: `input` covers prompt tokens, `output` covers completion tokens, `cached` is the cache-read subset of input (informational), and `total` is input + output. Counts are measured where the runtime reports usage (for example Claude Code) and estimated from text length otherwise; `EstimatedCount` reports how many aggregated records were estimated.

Bounded (see [ENUMERATION.md](ENUMERATION.md)): at most 1000 buckets, so a window and `bucketMinutes` that would produce more fail with `ErrorCode` `InvalidArgument` and `Code` `CollectionLimitExceeded` before any record is read; `bucketMinutes` is at most 527040. `ByModel` and each bucket's `Models` list the `modelLimit` most-used models; the rest are summed into `OtherModels` (model `(other)`), `OtherModelCount` says how many, and `ModelCount` is the distinct model count, so totals always add up.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "sinceHours": { "type": "integer", "description": "Only include usage newer than this many hours (default 24; ignored when fromUtc is set)" },
    "fromUtc": { "type": "string", "description": "Explicit UTC window start (ISO-8601); overrides sinceHours" },
    "toUtc": { "type": "string", "description": "Explicit UTC window end (ISO-8601; default now)" },
    "bucketMinutes": { "type": "number", "description": "Time-bucket width in minutes; fractional allowed, e.g. 0.5 for 30-second buckets (default 60, at most 527040)" },
    "modelLimit": { "type": "integer", "description": "Most models broken out by name (default 10, 1 to 100); the rest are summed into otherModels" },
    "model": { "type": "string", "description": "Filter to one model" },
    "runtime": { "type": "string", "description": "Filter to one runtime (for example claudecode, codex, mux)" },
    "source": { "type": "string", "description": "Filter to one source: mission, chat, planning, or synthetic" },
    "sourceId": { "type": "string", "description": "Filter to one source ID, such as synthetic:<batchId>" },
    "vesselId": { "type": "string", "description": "Filter to one vessel (vsl_ prefix)" },
    "captainId": { "type": "string", "description": "Filter to one captain (cpt_ prefix)" },
    "harborId": { "type": "string", "description": "Filter to work that ran on one Harbor (hbr_ prefix)" }
  }
}
```

**Response:** grand totals plus `Buckets` (time series), `ByModel` (the top `ModelLimit` models, most-used first), and `OtherModels` (the sum of the rest, or null).

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
  "ModelLimit": 10,
  "ModelCount": 2,
  "OtherModelCount": 0,
  "OtherModels": null,
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
      ],
      "OtherModelCount": 0,
      "OtherModels": null
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

Paginated enumeration of any entity type with filtering and sorting. This is the MCP equivalent of the `POST /api/v1/{entity}/enumerate` REST endpoints. Returns paginated results with total counts, page metadata, and query timing. Supports: inbox (the needs-you items, most urgent first; replaces the removed `inbox` tool), cli_permission_rules (replaces the removed `list_cli_permission_rules`), cli_permission_requests (replaces the removed `list_cli_permission_requests`), objectives (aliases `backlog`, `backlog_item`, `backlog_items`), fleets, vessels, captains, missions, voyages, docks, signals, events, merge_queue, harbors, playbooks, personas, memories, prompt_templates, pipelines, workflow_profiles, project_profiles, skills, check_runs, releases, deployments, incidents, runbooks, runbook_executions, jobs, model_endpoints, vessel_import_batch, fleet_action, fleet_action_run, fleet_action_run_target, vessel_health, blackbox (black box entry summaries, newest first; see [Black Box](#black-box)).

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "entityType": { "type": "string", "description": "Entity type to enumerate (inbox [the needs-you items, most urgent first], cli_permission_rules [vesselId and captainId filter], cli_permission_requests [newest first; status, missionId, threadId, captainId, vesselId filter], objectives [aliases backlog, backlog_item, backlog_items], fleets, vessels, captains, missions, voyages, docks, signals, events, merge_queue, harbors, playbooks, personas, memories, prompt_templates, pipelines, workflow_profiles, project_profiles, skills, check_runs, releases, deployments, incidents, runbooks, runbook_executions, jobs, model_endpoints, vessel_import_batch, fleet_action, fleet_action_run, fleet_action_run_target, vessel_health, blackbox [black box entry summaries; status filters resolution, search matches reason, summary and notes])" },
    "pageNumber": { "type": "integer", "description": "Page number (1-based, default 1)" },
    "pageSize": { "type": "integer", "description": "Results per page (default 10, normalized into 1..1000; a few entity types apply their REST route's lower maximum, and the result's PageSize reports the size used)" },
    "order": { "type": "string", "description": "Sort order: CreatedAscending, CreatedDescending" },
    "createdAfter": { "type": "string", "description": "ISO 8601 timestamp filter" },
    "createdBefore": { "type": "string", "description": "ISO 8601 timestamp filter" },
    "status": { "type": "string", "description": "Filter by status (entity-specific)" },
    "search": { "type": "string", "description": "Free-text search where supported (releases, deployments, incidents, runbooks, runbook_executions, memories, objectives; vessel name for vessel_health; reason, summary and notes for blackbox)" },
    "fleetId": { "type": "string", "description": "Filter by fleet ID (vessels)" },
    "vesselId": { "type": "string", "description": "Filter by vessel ID (missions, docks)" },
    "captainId": { "type": "string", "description": "Filter by captain ID (missions, events, signals)" },
    "voyageId": { "type": "string", "description": "Filter by voyage ID (missions, events)" },
    "missionId": { "type": "string", "description": "Filter by mission ID (events)" },
    "threadId": { "type": "string", "description": "Filter by Ask thread ID (ath_ prefix; cli_permission_requests)" },
    "eventType": { "type": "string", "description": "Filter by event type (events only)" },
    "signalType": { "type": "string", "description": "Filter by signal type (signals only)" },
    "toCaptainId": { "type": "string", "description": "Filter by recipient captain (signals only)" },
    "unreadOnly": { "type": "boolean", "description": "Unread only (signals only)" },
    "includeDescription": { "type": "boolean", "description": "Add the full Description to mission/voyage rows (default false). Returns descriptionLength hint when false. Rows stay scalar: use get_mission / voyage_status for the full model." },
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
| `inbox` | Paging only; most urgent first (see [Inbox items](#inbox-items-entitytype-inbox)) |
| `cli_permission_rules` | `vesselId`, `captainId`. Counted and paged in the database (caller's tenant plus every-tenant rules), newest first. |
| `cli_permission_requests` (alias `cli-permission-requests`) | `status` (Pending/Allowed/Denied/Expired/Cancelled), `missionId`, `threadId`, `captainId`, `vesselId`; newest first (see [CLI permission requests](#cli-permission-requests-entitytype-cli_permission_requests)) |
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
| `harbors` | `createdAfter`, `createdBefore`, `order`. Counted and paged in the database within the caller's tenant, newest first, ties by id. |
| `personas` | `createdAfter`, `createdBefore`, `order`. Visibility (tenant-wide plus own) applies in the database before counting, so `TotalRecords` is exact. |
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
| `skills` | paginated browse ordered by name; visibility (tenant-wide plus own) applies in the database before counting. |
| `memories` | `search` (content, topic, tags), `vesselId` (vessel or source vessel), `voyageId`, `missionId`, `createdAfter`, `createdBefore`, `order`. Filtered, sorted and paged in the database within the caller's visibility. Without `order`, salience descending then newest first; with `order`, by created time. |
| `jobs` | (paginated browse only) |
| `model_endpoints` | `createdAfter`, `createdBefore`, `order`. Counted and paged in the database within the caller's visibility (tenant-wide endpoints plus the caller's own), newest first, ties by id. |
| `vessel_import_batch` (aliases `vessel_import_batches`, `vessel-import-batch`, `import_batches`) | `status` (Discovered/Importing/Completed/CompletedWithFailures/Failed/Discovering), `createdAfter`, `createdBefore`, `order`. Scoped to the caller's tenant. Items are not included; read a batch with items through `GET /api/v1/vessels/import/batches/{id}`. |
| `fleet_action` | `includeInactive`, `createdAfter`, `createdBefore` |
| `fleet_action_run` | `status` (Pending/Running/Completed/CompletedWithFailures/Cancelled/Failed), `createdAfter`, `createdBefore` |
| `fleet_action_run_target` | `runId` (required), `status` (Pending/Skipped/Running/Succeeded/Failed/Cancelled/TimedOut) |
| `vessel_health` (aliases `vessel-health`, `health`) | `status` (overall status: Pass/Warn/Fail/NotApplicable/Unknown), `fleetId`, `search` (vessel name substring). Every active vessel appears, including never-evaluated ones. Rows are sorted by vessel name; for richer filters and sorting use `POST /api/v1/vessel-health/enumerate` |
| `blackbox` (aliases `black_box`, `blackbox_entries`, `blackbox-entries`) | `status` (the resolution: Open/Resolved/Duplicate/WontFix/ArmadaFixed; `InvalidArgument` otherwise), `search` (case-insensitive substring of the reason, summary and notes), `fleetId`, `vesselId`, `voyageId`, `missionId`, `captainId`, `createdAfter`, `createdBefore`, `order`. Rows are `BlackBoxEntrySummary` objects, visible to the caller exactly as with [list_blackbox_entries](#list_blackbox_entries), which takes the richer filters (phase, step, kind, triage, fingerprint, path, pinned, isCurrent). `Unavailable` when the black box is not running (always over stdio) |

**Page contract (docs/ENUMERATION.md):** every `entityType` returns the same page shape as the REST enumerate routes:
`PageNumber`, `PageSize`, `TotalPages`, `TotalRecords`, `Objects` (plus `Success` and `TotalMs`). `pageSize` defaults to
10 and is normalized into 1..1000; a few entity types apply a lower documented maximum from their REST twin, and the
result's `PageSize` always reports the size used. A page past the end returns an empty `Objects` with accurate totals.
The caller's visibility is applied before counting, so `TotalRecords` is exact for the caller: for `pipelines`,
`playbooks`, `prompt_templates`, `workflow_profiles`, `project_profiles` and `skills` a regular user sees the
tenant-wide rows plus their own, a tenant admin the whole tenant, and the filter runs in the database before the page is
cut (previously the page was cut first and invisible rows were dropped afterwards, so pages came back short and the
totals counted rows the caller could not see). Objects never embed growing child lists: `missions` and `voyages` rows
(with or without `includeDescription`) carry scalar fields only; read a mission's playbook snapshots and assignment
blocker with `get_mission` / `mission_status`, and a voyage's missions with `voyage_status`. Configuration lists on
`pipelines` (`Stages`) and `workflow_profiles` / `project_profiles` (`LanguageHints`, `RequiredInputs`, `ExpectedArtifacts`,
`Environments`, `PersonaOverrides`, `Skills`) hold at most 256 entries each (every write path rejects more).

| Include flag | Applies to | Default | Description |
|---|---|---|---|
| `includeDescription` | missions, voyages | `false` | Add the full Description to the scalar row fields. Returns `descriptionLength` hint when false. The row never embeds the full Mission or Voyage model. |
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

#### Inbox items (`entityType` `inbox`)

`enumerate` with `entityType` `inbox` returns one page of the operator's inbox: everything across the fleet that
requires a human's attention or action right now, ordered most-urgent first. Call it to answer a user asking *"Is there
anything waiting on me?"*, *"Is there anything that needs my attention?"*, or *"Do I have any action items from Armada
work?"*. It replaces the removed `inbox` tool (D-012), which returned every item in one result.

**What qualifies.** Two kinds of item appear:

- **Awaiting your decision (human-in-the-loop):** a mission in `Review` (approve or reject), a deployment in `PendingApproval`, a pending Ask Armada action proposal in one of your own conversations, or a pending CLI permission request (a captain waiting to run one of its own tools, such as a shell command).
- **Failed and needs intervention (human-out-of-the-loop):** a failed mission, a mission whose work could not be merged (landing failed), a failed merge, a failed or verification-failed deployment, or a stalled captain.

Purely informational events (completions, normal progress) are deliberately excluded -- the inbox answers *"what needs me?"*, not *"what happened?"* (use `enumerate` or the Activity log for history). An empty page (`TotalRecords` 0) means nothing currently needs the operator. Operational items are scoped like other reads (global admin: everything; tenant admin: the tenant; regular user: own items); Ask proposals are always limited to the caller's own threads, and proposals older than `Ask.ProposalExpiryMinutes` are left out (the same rule as `GET /api/v1/inbox`). Every item is returned (no category is capped), so `count` is the true number of items needing attention; only pending CLI permission requests stop at 1000, since they expire within minutes. Items are ordered by severity (`Critical` first), then title.

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
| `cli_permission` | CLI permission request waiting for an approver; visible to global admins, the tenant's tenant admins, and the owner of the thread or mission (`Href` is `/ask/<threadId>` for the caller's own conversation, otherwise `/server?tab=cli-permissions&request=<id>`; the pre-v1.2.0 `/cli-permissions?request=<id>` still redirects there) | `Warning` |

Each object on the page is an `InboxItem` with `Kind`, `Severity`, `Title`, `Detail`, `EntityType`, `EntityName`
(display name of the referenced entity; use it instead of parsing `Title`), `EntityId`, and a dashboard `Href`.
Deployment items also carry `EnvironmentName` and `DeploymentTitle`, and their `Title` reads
"Deploy to <environment>: <title>". `cli_permission` items carry `CliPermission` (the request, with the caller's
`CanDecide`) and `ExpiresUtc`. `Severity` is `Critical`, `Warning`, or `Info`.

```json
{ "entityType": "inbox", "pageNumber": 1, "pageSize": 25 }
```

```json
{
  "Success": true,
  "PageNumber": 1,
  "PageSize": 25,
  "TotalPages": 1,
  "TotalRecords": 1,
  "Objects": [
    {
      "Kind": "landing_failed",
      "Severity": "Critical",
      "Title": "Landing failed: Add JWT validation",
      "Detail": "The work could not be landed.",
      "EntityType": "mission",
      "EntityName": "Add JWT validation",
      "EntityId": "msn_1a2b3c",
      "Href": "/missions/msn_1a2b3c"
    }
  ]
}
```

> Also exposed over REST as `POST /api/v1/inbox/enumerate` and in the CLI as `armada inbox`.

#### CLI permission requests (`entityType` `cli_permission_requests`)

`enumerate` with `entityType` `cli_permission_requests` returns one page of CLI permission requests, newest first, with
`TotalRecords`. Filters: `status` (`Pending`, `Allowed`, `Denied`, `Expired`, or `Cancelled`; any other value is an
`InvalidArgument` error), `missionId`, `threadId` (Ask thread, `ath_`), `captainId`, and `vesselId`; paging is
`pageNumber` and `pageSize` (1 to 1000, default 10). Visibility is the same as `get_cli_permission_request` (see
[CLI Permissions](#cli-permissions)); each request carries the tool name, redacted `InputText`, `SummaryText`,
`SuggestedRule`, captain, vessel, mission or thread, `Status`, `ExpiresUtc`, and the caller's `CanDecide` and
`CanRemember`. It replaces the removed `list_cli_permission_requests` tool (D-017), whose `limit` stopped at 1000.
REST: `POST /api/v1/cli-permissions/requests/enumerate`.

```json
{ "entityType": "cli_permission_requests", "status": "Pending", "pageSize": 25 }
```

`entityType` `cli_permission_rules` (filters `vesselId` and `captainId`) replaces the removed
`list_cli_permission_rules` tool (D-013).

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
| `requiredTools` | string[] | No | Executable names required on the host (at most 256) |
| `inputs` | object[] | No | Typed runtime inputs (at most 256; each `allowedValues` at most 256) |

A list over its limit is `InvalidArgument` with `Code` `CollectionLimitExceeded` (also on `update_fleet_action` and
`run_fleet_action`, where `vesselIds` may hold at most 1000 entries and `inputValues` at most 256).

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

Optional `approvalOptions` uses the same fields as `dispatch`: `approveAll`, `timeoutSeconds`, and `timeoutDecision`. It applies to saved and ad hoc Mission actions and is forwarded to each target voyage. Automatic approval requires a presented administrator credential; unauthenticated loopback and scoped captain identities cannot enable it. REST/SDK callers use `Overrides.ApprovalOptions` instead of this MCP argument.

Start a run. Pass `actionId` for a saved action, or the inline definition fields for an ad hoc run.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `vesselIds` | string[] | Yes | Target vessel IDs in the caller's tenant (1-500; more than 1000 entries is `CollectionLimitExceeded`). One unknown or cross-tenant vessel rejects the whole request (404). |
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

**Response:** `{ "Run": FleetActionRun }` with the target counts (`TargetCount`, `SucceededCount`, `FailedCount`, `SkippedCount`, `CancelledCount`). Targets are not embedded: page them with `enumerate` (`entityType` `fleet_action_run_target`, `runId`, `pageNumber`, `pageSize`, optional `status`). Target summaries carry `Status`, `SkipReason`, `FailureReason`, `ExitCode`, `VoyageId`, `DurationMs`, and `OutputLength` / `ErrorLength` / `RenderedLength` hints instead of text.

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

Create a memory, or update it in place when a memory with the same `key` already exists (idempotent consolidation). Args: `content` (required); optional `type` (default `Semantic`), `topic`, `key`, `summary`, `salience` (0.0-1.0, default 0.5), `tags` (at most 256; more is `InvalidArgument` with `code` `CollectionLimitExceeded`), `sourceKind`, `sourceVoyageId`, `sourceMissionId`, `sourceVesselId`, `sourceDetail`, `vesselId`, `scope`.

### update_memory

Update an existing memory by id; only supplied fields change; increments `version`. Args: `memoryId` (required), then any of `type`, `topic`, `key`, `summary`, `content`, `salience`, `tags`, `vesselId`, `sourceDetail`, `scope`.

### delete_memory

Delete a memory that has become stale or is no longer relevant. Args: `memoryId` (required).

---

## CLI Permissions

CLI tool permissions govern a CLI captain's own tools (shell commands, web fetches, file tools outside the accepted
edits), not Armada's MCP tools. Under the `ApproveInArmada` policy a Claude Code or Mux (1.5.0 or later) captain sends each permission prompt to
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
| `enumerate` (`cli_permission_requests`), `get_cli_permission_request` | Any authenticated person: global admins see every request, tenant admins their tenant, other users requests from their own Ask threads and missions. Captain sessions get an empty page (and `NotFound` by id) |
| `decide_cli_permission_request` | A global admin, a tenant admin of the request's tenant, or the owner when `Permissions.AllowOwnerApproval` is true; `AllowAndRemember` needs an admin. Requires a presented credential: captain sessions and the unauthenticated loopback identity get `Forbidden`, so a captain can never approve its own prompt |
| `enumerate` (`cli_permission_rules`) | Any authenticated person (the caller's tenant plus rules for every tenant; captain sessions get an empty page) |
| `create_cli_permission_rule`, `update_cli_permission_rule`, `delete_cli_permission_rule` | TenantAdmin: global admins (any tenant, or every tenant), tenant admins (their own tenant) |
| `set_captain_cli_permission_policy` | TenantAdmin: a global admin or a tenant admin of the captain's tenant; never a captain session |

In an Ask conversation, `cli_permission_prompt` bypasses the proposal gate (an approver answers it instead), the readers
(`enumerate` and `get_cli_permission_request`) run like other read-only tools, and the decide, rule, and policy tools are refused outright with `Forbidden`
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
decides, the countdown expires (its snapshotted `timeoutDecision` applies: `Deny` expires it, `Approve` allows it), the turn or mission ends (`Cancelled`), or the caller
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
| `decision` | string | Yes | `AllowOnce`, `AllowAndRemember` (admins; also stores an allow rule), `AllowForConversation` (the admin owner of the Ask thread; also sets the thread to `Bypass`), or `Deny` |
| `message` | string | No | Returned to the captain with a denial; recorded with an allow |
| `rulePattern` | string | No | Rule for `AllowAndRemember`, for example `Bash(git status:*)`; defaults to the request's `SuggestedRule` |
| `ruleScope` | string | No | `Global`, `Vessel`, or `Captain` (default `Captain`) for that rule |

**Response:** the decided CliPermissionRequest. `Conflict` when it is no longer pending.

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

## Stage Outcomes

A stage outcome (`sto_`) records one stage attempt: who ran it, what was observed, what Armada did next, and the
suspected cause of a shortfall, assigned by the first enabled failure-cause rule (`fcr_`) that matches or by a person.
The REST equivalents, the outcome and rule shapes, the match semantics, and the built-in rules are in
[REST_API.md](REST_API.md#stage-outcomes-and-failure-cause-rules); the tools enforce the same access rules through the
same service. Errors are `McpToolError`s: `NotFound` (missing, or another tenant's), `Forbidden` (the caller may not
act, or a captain session), `InvalidArgument` (bad enum value, date, or an invalid rule: no name, a name over 200
characters, no criterion, cause `None`, or an invalid `textPattern`), `Conflict` (a duplicate rule name, or drafting
from an outcome without a cause), and `Unavailable` on the stdio server, which registers the tools without the service.
These tools are not on the Ask read-only list, so inside an Ask conversation each call becomes a proposal.

| Tool | Caller |
|---|---|
| `list_stage_outcomes`, `get_stage_outcome`, `classify_stage_outcome`, `draft_failure_cause_rule` | Any authenticated person: global admins every tenant, everyone else their own tenant |
| `reclassify_stage_outcomes` | TenantAdmin: global admins (any tenant, or every tenant), tenant admins (their own tenant) |
| `list_failure_cause_rules`, `get_failure_cause_rule` | Any authenticated person (the caller's tenant plus rules for every tenant; global admins see all) |
| `create_failure_cause_rule`, `update_failure_cause_rule`, `delete_failure_cause_rule` | Any authenticated person for their tenant's rules; rules for every tenant and built-in rules need a global admin |

### list_stage_outcomes

| Parameter | Type | Required | Description |
|---|---|---|---|
| `tenantId` | string | No | Tenant filter (global admins only; ignored otherwise) |
| `missionId`, `voyageId`, `vesselId`, `captainId` | string | No | Filters |
| `persona`, `model` | string | No | Exact, case-insensitive |
| `suspectedCause` | string | No | `Protocol`, `InstructionFollowing`, `ToolUse`, `Context`, `Capability`, `NotTheModel`, `Unknown`, `Mixed`, or `None` |
| `causeSource` | string | No | `None`, `Rule`, `Judge`, or `Manual` |
| `disposition` | string | No | `Completed`, `Relaunched`, `OutputRepair`, `Escalated`, `Failed`, `Interrupted`, `Cancelled`, or `Pending` |
| `isEvaluation` | boolean | No | Evaluation attempts only (true) or production only (false) |
| `fromUtc`, `toUtc` | string | No | Ended at or after / before (ISO 8601 UTC) |
| `pageNumber`, `pageSize` | integer | No | Paging (page size 1-1000, default 25) |

**Response:** `EnumerationResult<StageOutcome>` (`objects`, `totalRecords`, `totalPages`), newest first.

### get_stage_outcome

| Parameter | Type | Required | Description |
|---|---|---|---|
| `outcomeId` | string | Yes | Outcome ID (`sto_`) |

**Response:** the StageOutcome.

### classify_stage_outcome

| Parameter | Type | Required | Description |
|---|---|---|---|
| `outcomeId` | string | Yes | Outcome ID (`sto_`) |
| `cause` | string | No | The cause to set by hand (any cause except `None`); omit to clear the manual cause and re-apply the rules |
| `note` | string | No | Evidence (at most 2000 characters kept) |

**Response:** the updated StageOutcome (`causeSource` `Manual` and `classifiedByUserId` the caller when a cause was set).

### reclassify_stage_outcomes

Takes the `list_stage_outcomes` filters (paging ignored) and re-applies the enabled rules to every match; outcomes
classified by hand are left alone. **Response:** `{ "examined": 12, "changed": 3, "skippedManual": 1 }`.

### draft_failure_cause_rule

| Parameter | Type | Required | Description |
|---|---|---|---|
| `outcomeId` | string | Yes | Outcome ID (`sto_`) |

**Response:** an unsaved FailureCauseRule with the outcome's cause and tenant, priority 5, and a match on its failure
kind, runtime failure kind, stage-check status and reason, disposition, and persona. Nothing is stored; edit it and pass
it to `create_failure_cause_rule`. `Conflict` when the outcome's cause is `None`.

### list_failure_cause_rules

| Parameter | Type | Required | Description |
|---|---|---|---|
| `pageNumber`, `pageSize` | integer | No | Paging (page size 1-1000, default 100) |

**Response:** `EnumerationResult<FailureCauseRule>` in evaluation order (priority, then name, then id), built-in rules
included, paged in the database. Each `match` list holds at most 256 entries; `create_failure_cause_rule` and
`update_failure_cause_rule` reject more with `InvalidArgument`, `Code` `CollectionLimitExceeded`.

### get_failure_cause_rule

| Parameter | Type | Required | Description |
|---|---|---|---|
| `ruleId` | string | Yes | Rule ID (`fcr_`) |

### create_failure_cause_rule

| Parameter | Type | Required | Description |
|---|---|---|---|
| `name` | string | Yes | Unique per tenant, at most 200 characters |
| `cause` | string | Yes | Any cause except `None` |
| `match` | object | Yes | `personas`, `runtimes`, `failureKinds`, `runtimeFailureKinds`, `stageCheckStatuses`, `stageCheckReasons`, `dispositions` (string arrays of enum names) and `textPattern` (regular expression); at least one criterion |
| `description` | string | No | What the rule recognizes and why |
| `priority` | integer | No | Evaluation order, lowest first (default 100; built-ins use 10 to 111) |
| `enabled` | boolean | No | Default true |
| `tenantId` | string | No | Global admins: the tenant, or omit for every tenant. Everyone else's rules belong to their tenant |

**Response:** the created FailureCauseRule.

### update_failure_cause_rule

Takes `ruleId` (required) and any of `name`, `description`, `priority`, `enabled`, `cause`, `match`; omitted fields keep
their stored values, and `match` replaces the whole match. Rules for every tenant and built-in rules need a global admin;
an edited built-in is never overwritten by seeding. **Response:** the updated FailureCauseRule.

### delete_failure_cause_rule

| Parameter | Type | Required | Description |
|---|---|---|---|
| `ruleId` | string | Yes | Rule ID (`fcr_`) |

**Response:** `{ "deleted": true, "ruleId": "fcr_...", "isBuiltIn": false }`. A deleted built-in rule is stored again the
next time the Admiral starts; disable it (`enabled` false) to keep it out of classification.

---

## Handoff Artifacts

A handoff artifact (`hoa_`) is the durable, versioned record one stage attempt hands to the next stage: numbered
findings (file, line, severity, required fix), acceptance criteria, required actions, check results (passed, failed,
skipped with a reason, could not run), commit references, protocol lines, the full stage output and the full diff (or
where to regenerate it). Armada stores one per stage attempt, linked to its stage outcome. At launch the next stage
receives a budgeted rendering of it whose pointer names `get_handoff_artifact` and the artifact id when the captain has
Armada MCP tools. The shapes, the budget and the delivery order are in
[REST_API.md](REST_API.md#handoff-artifacts); the tools enforce the same access rules through the same service.

| Tool | Caller |
|---|---|
| `get_handoff_artifact`, `list_handoff_artifacts` | Any authenticated person: global admins every tenant, everyone else their own tenant. A captain session (mission-scoped token) sees its own voyage, or its own mission and the stage it depends on. Ask sessions get `Forbidden` |

Errors are `McpToolError`s: `NotFound` (missing or out of scope), `Forbidden` (an Ask session) and `InvalidArgument`
(arguments that do not parse). A missing required argument is refused by the MCP layer before the tool runs.

### get_handoff_artifact

| Parameter | Type | Required | Description |
|---|---|---|---|
| `artifactId` | string | Yes | Artifact ID (`hoa_`) |

**Response:** the complete HandoffArtifact, with `output` and `diff`. `findings`, `acceptanceCriteria` and
`completionLines` hold at most 256 entries: a longer list keeps its first 255 and ends with a marker entry naming how
many more there were (a `completionLines` marker starts with `[ARMADA:TRUNCATED]`); the full text stays in `output`.
`requiredActions` holds at most 8, `checkResults` at most 1 and `commitReferences` at most 20.

### list_handoff_artifacts

| Parameter | Type | Required | Description |
|---|---|---|---|
| `missionId` | string | Yes | Mission ID (`msn_`) |
| `pageNumber`, `pageSize` | integer | No | Paging (page size 1-1000, default 25) |

**Response:** `EnumerationResult<HandoffArtifact>` (`objects`, `totalRecords`, `totalPages`), newest first, without
`output` and `diff` (their lengths stay).

## Evaluate

Evaluate runs probe-only mechanical probes (output contract, served context, ASCII and format rules, tool calling,
speed, determinism) against a captain through its own runtime, or against a model endpoint through the API endpoint
runtime, and records a run (`mpr_`) with one result per probe: `Passed`, `Failed`, or `CouldNotRun` with the reason (a
probe a runtime cannot run is never `Failed`). Depth `Quick` (the default) runs only those probes. Depth `Full` adds a
fixture battery run (packs default to those matching the languages of the tenant's repositories; personas default to all
the captain is allowed), then an evaluator captain (default: the highest-tier idle captain of a different model family; a
same-family one only when no other is idle, with a warning in the report) grades the battery attempts that need a
subjective grade and writes strengths and weaknesses from the mechanical counts only. Every count the evaluator cites
must match; a reply that does not parse or cites a wrong count is rejected with the reason and the mechanical report is
kept. The run carries a `report` (per-persona counts, the narrative, evaluator, warnings, `complete`); a complete report
is also recorded as an advisory model profile revision. A cancelled or timed-out Full run ends `Cancelled` with a partial
report marked incomplete and its open battery mission cancelled. Full has its own token budget
(`evaluation.fullRunTokenBudget`) and timeout (`evaluation.fullRunTimeoutMinutes`) and holds the per-inference-host
evaluation slot for its whole run. Runs are tagged as evaluation, never count in stage outcomes, scorecards or alerts,
and change no routing, persona captain lists, prompts or captain settings. The
REST equivalents and the run shape are in [REST_API.md](REST_API.md#evaluate-model-probe-runs); the tools enforce the
same access rules through the same service. Errors are `McpToolError`s: `NotFound` (missing, or another tenant's),
`Forbidden` (not an admin for a captain, a Full Evaluate or a cancel, or a captain session), `InvalidArgument` (not
exactly one id, an unknown depth or status, an unknown pack, a persona the captain is not allowed, a run count outside 1
to 10), and `Unavailable` on the stdio server. `get_model_probe_run` and `list_model_probe_runs` are on the
Ask read-only list; `evaluate_captain` becomes a proposal inside an Ask conversation.

| Tool | Caller |
|---|---|
| `evaluate_captain` with `captainId` | Global admin, or a tenant admin of the captain's tenant |
| `evaluate_captain` with `modelEndpointId` | Quick: anyone who can read the endpoint; Full: a global admin or a tenant admin |
| `get_model_probe_run`, `list_model_probe_runs` | Any authenticated person: global admins every tenant, everyone else their own tenant |
| `cancel_model_probe_run` | Global admin, or a tenant admin of the run's tenant |

### evaluate_captain

| Parameter | Type | Required | Description |
|---|---|---|---|
| `captainId` | string | One of | Captain to evaluate (`cpt_`) |
| `modelEndpointId` | string | One of | Model endpoint to evaluate (`mep_`) |
| `depth` | string | No | `Quick` (default) or `Full` |
| `evaluatorCaptainId` | string | No | Full only: evaluator captain (`cpt_`, same tenant); default the highest-tier idle captain of another model family |
| `packs` | string[] | No | Full only: battery packs; default those matching the tenant's repository languages |
| `personas` | string[] | No | Full only: personas to score; default all the captain is allowed |
| `runCount` | integer | No | Full only: runs of each battery task (1-10, default 3) |

**Response:** the queued ModelProbeRun; the probes run in the background. Poll `get_model_probe_run`.

### get_model_probe_run

| Parameter | Type | Required | Description |
|---|---|---|---|
| `id` | string | Yes | Run ID (`mpr_`) |

**Response:** the ModelProbeRun with `results`.

### list_model_probe_runs

| Parameter | Type | Required | Description |
|---|---|---|---|
| `captainId`, `modelEndpointId` | string | No | Filters |
| `status` | string | No | `Queued`, `Running`, `Completed`, `Failed`, or `Cancelled` |
| `pageNumber`, `pageSize` | integer | No | Paging (page size 1-1000, default 25) |

**Response:** `EnumerationResult<ModelProbeRun>` (without results), newest first.

### cancel_model_probe_run

| Parameter | Type | Required | Description |
|---|---|---|---|
| `id` | string | Yes | Run ID (`mpr_`) |

**Response:** the ModelProbeRun after the cancellation (it may still be stopping); a finished run is returned unchanged.

---

## Model Profiles

A model profile (`mdl_`) is the model as served: runtime, provider, model and served context, shared by every captain and
Harbor that runs it. Armada creates profiles when it records a captain's runtime and model (stage outcomes and Quick
Evaluate runs) and sets the captain's `modelProfileId`. Each profile keeps immutable revisions (`mdv_`: runtime version,
model digest, sampling, tool configuration, `compatibleWith`, `firstSeenUtc`); a new runtime major version, model digest
or sampling starts a new revision and a runtime minor or patch release pools into the current one. Profiles are advisory:
they do not set a captain's tier or change routing. Updating the mutable parts (capabilities, rendering overrides, notes)
and marking revisions compatible are REST only ([REST_API.md](REST_API.md#model-profiles)). All three tools are on the
Ask read-only list. Errors are `McpToolError`s: `NotFound` (missing, or another tenant's) and `Unavailable` on the stdio
server; a non-integer page argument is refused by the schema before the tool runs.

| Tool | Caller |
|---|---|
| `get_model_profile`, `list_model_profiles`, `get_model_scorecard` | Any authenticated person: global admins every tenant, everyone else their own tenant |

### get_model_profile

| Parameter | Type | Required | Description |
|---|---|---|---|
| `id` | string | Yes | Model profile ID (`mdl_`) |

**Response:** the ModelProfile with `identity`, `capabilities`, `rendering`, `notes` and `revisions` (oldest first).

### list_model_profiles

| Parameter | Type | Required | Description |
|---|---|---|---|
| `runtime`, `provider` | string | No | Filters (case-insensitive) |
| `model` | string | No | Only models whose name contains this text (case-insensitive) |
| `pageNumber`, `pageSize` | integer | No | Paging (page size 1-1000, default 25) |

**Response:** `EnumerationResult<ModelProfile>` (without revisions), newest first.

### get_model_scorecard

The read-only scorecard of one (profile, persona), optionally one vessel, computed from stage outcomes, token usage and
timing. Same rules and shape as `GET /api/v1/model-profiles/{id}/scorecard`
([REST_API.md](REST_API.md#model-scorecard)): revision groups (pooled only when marked compatible), metrics as count of
denominator with a 95% Wilson interval, unavailable metrics with a reason, and the exclusions each attribution rule
applied. The profile is resolved before the other arguments are validated, so another tenant's profile is `NotFound`
and an out-of-range window on a visible profile is `InvalidArgument`.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `id` | string | Yes | Model profile ID (`mdl_`) |
| `persona` | string | Yes | Persona, for example `Worker` or `Judge` (case-insensitive) |
| `vesselId` | string | No | Only stages on this vessel |
| `windowDays` | integer | No | Window length in days (1-365, default 30) |
| `windowMissions` | integer | No | Window length in missions (1-500, default 50) |

**Response:** a ModelScorecard (`profileId`, `identity`, `persona`, `windowDays`, `windowMissions`, `windowLimitedBy`,
`windowStartUtc`, `windowEndUtc`, `missions`, `attempts`, `truncated`, `exclusions`, `groups`).

---

## Model Alerts

Scorecard thresholds crossed for a (model profile, revision group, persona, vessel or all vessels, rule), raised as inbox
items (kind `model_alert`) with suggested actions. Same rules, defaults and shapes as REST `/api/v1/model-alerts`
([REST_API.md](REST_API.md#model-alerts)): evaluation reads scorecard data only, alerts are deduplicated per condition and
resolve themselves when the condition no longer holds, and a suggested action changes something only when applied. The
alert is resolved before the other arguments are validated, so another tenant's alert is `NotFound`. `list_model_alerts`,
`get_model_alert` and `get_model_alert_thresholds` are on the Ask read-only list.

| Tool | Caller |
|---|---|
| `list_model_alerts`, `get_model_alert`, `get_model_alert_thresholds` | Any authenticated person: global admins every tenant, everyone else their own tenant |
| `evaluate_model_alerts`, `apply_model_alert_action`, `ignore_model_alert` | A global admin, or a tenant admin of the alert's tenant (`Forbidden` otherwise) |
| `update_model_alert_thresholds` | Global admins only (the thresholds are server settings) |

### list_model_alerts

| Parameter | Type | Required | Description |
|---|---|---|---|
| `status` | string | No | `Open` or `Resolved` (default both) |
| `profileId` | string | No | Model profile ID (`mdl_`) |
| `persona` | string | No | Persona (case-insensitive) |
| `includeIgnored` | boolean | No | Include alerts hidden by Ignore (default true) |
| `pageNumber`, `pageSize` | integer | No | Paging (page size 1-1000, default 25) |

**Response:** `EnumerationResult<ModelAlert>`, most recently raised first, each with `suggestedActions` and `actions`.

### get_model_alert

| Parameter | Type | Required | Description |
|---|---|---|---|
| `id` | string | Yes | Model alert ID (`mal_`) |

**Response:** the ModelAlert (`rule`, `status`, `title`, `detail`, `count`, `of`, `value`, `threshold`, `revisionGroup`,
`persona`, `vesselId`, `ignoredUntilUtc`, `actions`, `suggestedActions`).

### evaluate_model_alerts

No parameters. Runs one evaluation pass now (a tenant admin's tenant, or every tenant for a global admin).

**Response:** a ModelAlertEvaluation (`pairs`, `targets` with every rule's `state`, counts, threshold and reason - inactive
rules are `Unavailable`, rules below their minimum `MinimumNotMet` - `raised`, `stillOpen`, `resolved`).

### get_model_alert_thresholds

No parameters. **Response:** `defaults` (every value set) and per-persona `overrides`.

### update_model_alert_thresholds

| Parameter | Type | Required | Description |
|---|---|---|---|
| `defaults` | object | No | `firstPassMinRate`, `firstPassMinMissions`, `escalationsInARow`, `protocolRepairMaxRate`, `protocolRepairMinAttempts`, `costMedianMultiplier`, `costMinAccepted` (null uses the built-in default) |
| `overrides` | array | No | Per-persona overrides: `persona` plus any of the default fields (null keeps the default) |

**Response:** the stored thresholds. `InvalidArgument` for a value out of range or a persona listed twice.

### apply_model_alert_action

| Parameter | Type | Required | Description |
|---|---|---|---|
| `id` | string | Yes | Model alert ID (`mal_`) |
| `action` | string | Yes | `MoveDown`, `RemoveFromPersona`, `CompactTemplate` (always unavailable), `Reevaluate` or `Ignore` |
| `days` | integer | No | For `Ignore`: days to hide the alert (1-365, default 7) |

**Response:** a ModelAlertApplyResult (`record`, `alert`). `InvalidArgument` for an unknown or unavailable action.

### ignore_model_alert

| Parameter | Type | Required | Description |
|---|---|---|---|
| `id` | string | Yes | Model alert ID (`mal_`) |
| `days` | integer | No | Days to hide the alert (1-365, default 7) |

**Response:** a ModelAlertApplyResult.

---

## Fixture Battery

The fixture battery runs data-defined packs of fixed tasks (`battery/<pack>/battery-pack.json`; schema in
[battery/README.md](../battery/README.md)) against a captain. Each task goes through real dispatch on the pack's fixture
vessel under the built-in `ModelEvaluation` pipeline, after the fixture is reset, and is graded per its manifest. A task
whose toolchain the Harbor lacks is `CouldNotRun`, never `Failed`. Rubric-graded tasks are `PendingEvaluation` with the
raw output stored. Runs (`bat_`) record the seed, sampling settings, run count, battery version and configuration
revision. Battery voyages and missions are dispatched with `isEvaluation` set, so their stage outcomes are tagged
`IsEvaluation` and never count in production stage outcomes; the flag, not the pipeline, decides the tag. The REST equivalents are in
[REST_API.md](REST_API.md#fixture-battery), and the tools enforce the same rules through the same service.

Errors are `McpToolError`s:

- `NotFound`: missing, or another tenant's captain or run.
- `Forbidden`: not an admin for `run_battery`, or a captain session.
- `InvalidArgument`: no packs, an unknown or malformed pack, a bad run count, or a missing fixture vessel without
  `createFixtureVessel`.
- `Unavailable`: on the stdio server.

`list_battery_packs`, `get_battery_run` and `list_battery_results` are on the Ask read-only list. `run_battery` becomes a proposal inside an
Ask conversation.

| Tool | Caller |
|---|---|
| `list_battery_packs` | Any authenticated person |
| `run_battery` | Global admin, or a tenant admin of the captain's tenant |
| `get_battery_run`, `list_battery_results` | Any authenticated person: global admins every tenant, everyone else their own tenant |

### list_battery_packs

| Parameter | Type | Required | Description |
|---|---|---|---|
| `pageNumber`, `pageSize` | integer | No | Paging (page size 1-1000, default 100) |

**Response:** `EnumerationResult<BatteryPackInfo>`, ordered by pack directory name; only the page's manifests are read.
Malformed packs are listed with `valid: false` and their `errors` (at most 256). Every manifest list holds at most 256
entries; a pack with more is malformed and lists the first 256 tasks and toolchain entries with `taskCount` and
`toolchainCount`.

### run_battery

| Parameter | Type | Required | Description |
|---|---|---|---|
| `packs` | string[] | Yes | Pack ids, for example `["csharp"]` (at most 256) |
| `captainId` | string | Yes | Captain to run the tasks (`cpt_`) |
| `runCount` | integer | No | Runs of each task (1-10, default 3) |
| `seed` | integer | No | Seed to record (default: picked at random) |
| `createFixtureVessel` | boolean | No | Create the pack's fixture vessel if it does not exist yet (default false) |

**Response:** the queued BatteryRun. The tasks run in the background; poll `get_battery_run`.

### get_battery_run

| Parameter | Type | Required | Description |
|---|---|---|---|
| `id` | string | Yes | Run ID (`bat_`) |

**Response:** the BatteryRun with its outcome counts. The per-task results are not embedded; page them with
`list_battery_results`.

### list_battery_results

| Parameter | Type | Required | Description |
|---|---|---|---|
| `runId` | string | Yes | Run ID (`bat_`) |
| `pageNumber`, `pageSize` | integer | No | Paging (page size 1-1000, default 10) |
| `status` | string | No | Outcome filter: `Passed`, `Failed`, `CouldNotRun`, `PendingEvaluation` |
| `order` | string | No | `CreatedDescending` (default) or `CreatedAscending` |

**Response:** `EnumerationResult<BatteryTaskResult>` paged in the database: one result per task attempt, with the
grader's evidence (the first 256 false positives, with `falsePositiveCount`) and the raw output. `NotFound` for a
missing run or another tenant's run.

---

## Black Box

A black box entry (`bbx_`) records where, how and why a mission or voyage failed, with evidence and the advisor's
analysis (findings, a triage verdict, configuration recommendations and ranked recovery paths). The REST equivalents,
the entry and record shapes, and every limit are in [REST_API.md](REST_API.md#black-box); concepts are in
[BLACKBOX.md](BLACKBOX.md). Every tool calls `IBlackBoxService` as the MCP caller, so tenant scope and visibility match
REST: a global admin sees every tenant, a tenant admin their tenant, and a regular user their own entries; an entry the
caller cannot see is `NotFound` with `Code` `BlackBoxNotFound`, never `Forbidden`.

**Naming the entry.** Tools that act on one entry take `entryId` (`bbx_`), or `missionId` / `voyageId` for that mission's
or voyage's **current** entry. One of the three is required (`InvalidArgument` otherwise). `mission_status` and
`voyage_status` carry `LastBlackBoxEntryId`, so a client can find the entry from the mission.

**Errors** are `McpToolError`s with the black box code in `Code`:

| `ErrorCode` | `Code` | When |
|---|---|---|
| `NotFound` | `BlackBoxNotFound` | The entry, the mission's or voyage's entry, the object, the recovery path or the recommendation does not exist or is not visible |
| `NotFound` | `BlackBoxObjectMissing` | The object's row exists but the store lost its bytes |
| `Conflict` | `BlackBoxDeleting` | The entry is being deleted |
| `Conflict` | `BlackBoxObjectCorrupt` | The object's bytes do not match its SHA-256 |
| `Conflict` | `BlackBoxConfirmationRequired` | A confirmed recovery or recommendation run without a valid `confirmToken` (missing, expired, used, another caller's, or the mission or plan changed), or a `dataLossRisk` path without `acknowledgeDataLoss` |
| `Unavailable` | `BlackBoxStoreUnavailable` | The storage target is unreachable |
| `Unavailable` | `BlackBoxActionsUnavailable` | A confirmed run on a host with no action executor wired |
| `Unavailable` | null | The black box is not available on this server (every tool on the stdio server) |
| `InvalidArgument` | null, or `CollectionLimitExceeded` | A missing or invalid argument (format, section, resolution, order, offset, length, a note body outside 1 to 8000 characters, a search query outside 1 to 200); more than 1000 `entryIds` is `CollectionLimitExceeded` |
| `Forbidden` | null | The tool's permission level, or a recovery step whose tool the caller could not call directly |

```json
{
  "Error": "Black box entry bbx_doesnotexist_0000000000 not found.",
  "ErrorCode": "NotFound",
  "Code": "BlackBoxNotFound",
  "StatusCode": null
}
```

**Permissions** (`McpToolAuthorizationRegistry`, resource `BlackBox`):

| Tool | Level |
|---|---|
| `get_blackbox`, `read_blackbox_section`, `list_blackbox_objects`, `read_blackbox_object`, `list_blackbox_entries`, `search_blackbox`, `list_blackbox_notes`, `get_blackbox_bundle`, `list_blackbox_hotspots`, `list_blackbox_fingerprints`, `list_blackbox_resilience_gaps`, `get_blackbox_fingerprint_trend` | Authenticated |
| `annotate_blackbox`, `resolve_blackbox`, `pin_blackbox` | Authenticated |
| `run_blackbox_recovery`, `apply_blackbox_recommendation` | Authenticated; each recorded step is then authorized as its own tool (`restart_mission`, `retry_landing`, `update_vessel` and the other supported tools are TenantAdmin) |
| `get_blackbox_retention`, `delete_blackbox_entries`, `reanalyze_blackbox` | TenantAdmin |
| `get_blackbox_storage`, `purge_blackbox`, `run_blackbox_gc`, `export_blackbox_fixture` | AdminOnly |

**Ask Armada.** The 14 read tools (`get_blackbox`, `read_blackbox_section`, `list_blackbox_objects`,
`read_blackbox_object`, `list_blackbox_entries`, `search_blackbox`, `list_blackbox_notes`, `get_blackbox_bundle`,
`list_blackbox_hotspots`, `list_blackbox_fingerprints`, `list_blackbox_resilience_gaps`, `get_blackbox_fingerprint_trend`,
`get_blackbox_retention`, `get_blackbox_storage`) are on the Ask read-only allowlist (`AskToolPolicy`), so they run directly inside an Ask
conversation (still subject to their permission level). The write tools become proposals there.

**Stdio.** `armada mcp stdio` registers the same 24 tools without the service, so each answers `Unavailable`; the
resources and prompts below are served only by the Admiral's HTTP MCP server.

### get_blackbox

Start here when asked why something failed.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `entryId` / `missionId` / `voyageId` | string | One of them | The entry, or the current entry of a mission or voyage |
| `format` | string | No | `summary` (default, about 2 KB), `brief` or `json` |
| `redactHosts` | boolean | No | Strip absolute paths, host names and user names from the brief (default false) |

**Response:** `summary` returns a `BlackBoxDigest`: where it failed (phase, step, host, source file and method), how
(mechanism), status before and after, why (the top findings), the triage verdict and confidence, the Armada resilience
angle, and the top recovery paths. The resilience fields: `TriageSuspected` (the `ArmadaDefect` verdict rests on the
handoff and landing prior `BBX-PRIOR-ARMADA` alone), `ResilienceInScope` (the failure is in Armada's own handoff and
landing), `SelfHealable` (`Yes`, `No` or `Unknown`: could Armada have healed it by itself), `SelfHealingProposal` (what
Armada could have done), `DetectionGap` (why Armada did not catch it earlier), `ResilienceSeverity` (escalated by
recurrence) and `TopProposedChange` (the most impactful proposed Armada change, its `MissionBrief` left out; read the
`Resilience` section for it). When `ResilienceInScope` is true, first assess whether Armada could have handled it. `brief` returns `{ EntryId, ContentType, Brief }` (the Markdown FIX THIS brief, rendered live; there is one brief per
entry).
`json` returns the full `BlackBoxRecord`.

```json
{ "missionId": "msn_mvu1fjp0_9Wd6Uuyp262", "format": "summary" }
```

```json
{
  "EntryId": "bbx_mvu1ftcv_zcdofOBmjhY",
  "SubjectType": "Mission",
  "MissionId": "msn_mvu1fjp0_9Wd6Uuyp262",
  "VoyageId": "vyg_mvu1fjoz_xO6urNkcaHp",
  "Title": "Retry landings deferred on a dirty checkout",
  "VesselName": "demo-api",
  "Attempt": 1,
  "CaptureMode": "Backfill",
  "State": "Analyzed",
  "Phase": "Execution",
  "Step": "ProcessCrash",
  "Host": "Admiral",
  "SourceFile": null,
  "SourceLine": null,
  "Method": null,
  "Kind": "Crash",
  "Reason": "Captain process exited with code 134 after 4m12s while running dotnet test (Test.Automated aborted: Fatal error. System.AccessViolationException in SQLitePCL.raw.sqlite3_step). Retry once the test host is stable.",
  "Mechanism": null,
  "StatusBefore": null,
  "StatusAfter": "Failed",
  "Triage": "Transient",
  "TriageConfidence": "Low",
  "TriageSuspected": false,
  "ResilienceInScope": false,
  "SelfHealable": "No",
  "SelfHealingProposal": null,
  "DetectionGap": null,
  "ResilienceSeverity": "Low",
  "TopProposedChange": null,
  "Summary": "Mission \"Retry landings deferred on a dirty checkout\" (msn_mvu1fjp0_9Wd6Uuyp262) failed during Execution at step ProcessCrash (kind Crash). The captain's process exited with code unknown before finishing. Triage: Transient (low confidence). Suggested next step: restart the mission (bbx-exec-006.restart).",
  "Why": [
    { "RuleId": "BBX-EXEC-006", "Statement": "The captain's process exited with code unknown before finishing.", "Confidence": "Medium", "EvidenceRefs": [ "mechanism.exitCode", "evidence/agent-output.txt" ] }
  ],
  "RecoveryPaths": [
    {
      "Id": "bbx-exec-006.restart",
      "Rank": 1,
      "Title": "Restart the mission",
      "Preconditions": [],
      "Steps": [
        {
          "Order": 1,
          "Description": "Restart msn_mvu1fjp0_9Wd6Uuyp262.",
          "McpCommand": "restart_mission {\"missionId\": \"msn_mvu1fjp0_9Wd6Uuyp262\"}",
          "RestCommand": "POST /api/v1/missions/msn_mvu1fjp0_9Wd6Uuyp262/restart",
          "CliCommand": "armada mission restart msn_mvu1fjp0_9Wd6Uuyp262",
          "Action": { "Tool": "restart_mission", "Arguments": { "missionId": "msn_mvu1fjp0_9Wd6Uuyp262" } },
          "DataLossRisk": false
        }
      ],
      "Action": { "Tool": "restart_mission", "Arguments": { "missionId": "msn_mvu1fjp0_9Wd6Uuyp262" } },
      "Risk": "Low",
      "DataLossRisk": false,
      "ExpectedOutcome": "A fresh process runs the mission.",
      "RuleId": "BBX-EXEC-006"
    }
  ],
  "ConflictedFileTotal": 0,
  "ObjectCount": 5,
  "Resolution": "Resolved",
  "CreatedUtc": "2026-10-29T21:11:05.310433Z"
}
```

`format` `brief`:

```json
{
  "EntryId": "bbx_mvu1ftcv_zcdofOBmjhY",
  "ContentType": "text/markdown",
  "Brief": "# FIX THIS: Retry landings deferred on a dirty checkout (mission msn_mvu1fjp0_9Wd6Uuyp262)\n\nYou are asked to FIX THIS. This brief is a self-contained black box entry from Armada, a multi-agent orchestration system. ...\n\n## Summary\n\n..."
}
```

`InvalidArgument` for another `format`; `NotFound` (`BlackBoxNotFound`) when there is no entry.

### read_blackbox_section

| Parameter | Type | Required | Description |
|---|---|---|---|
| `entryId` / `missionId` / `voyageId` | string | One of them | The entry |
| `section` | string | Yes | `Subject`, `Status`, `Site`, `Mechanism`, `Classification`, `Workspace` (dirty paths, checkout), `Conflict` (conflicted files and the missions that landed them), `Scope`, `Configuration` (each setting and where it came from), `Attempts`, `Evidence` (the object index), `Analysis` (findings, triage, recovery paths, recommendations), `Resilience` (the `Analysis` with only `Resilience` and `Triage`: whether Armada could have healed it, the detection gap and invariant, code pointers at the build commit, proposed Armada changes as mission briefs, the regression test), `Handoff` (the handoff and landing trail: ordered steps, automatic recoveries Armada already tried, dock transitions, target movement), `Related`, `Voyage`, `Redaction` or `Truncation` (case-insensitive) |

**Response:** a `BlackBoxSectionResult`: the record with the header fields and the requested section; the other
sections are null (`Attempts` is an empty list, and `Redaction` and `Truncation` are always present).

```json
{ "entryId": "bbx_mvu1ftcv_zcdofOBmjhY", "section": "Mechanism" }
```

```json
{
  "EntryId": "bbx_mvu1ftcv_zcdofOBmjhY",
  "Section": "Mechanism",
  "Record": {
    "EntryId": "bbx_mvu1ftcv_zcdofOBmjhY",
    "SchemaVersion": 1,
    "RulesVersion": 1,
    "CaptureMode": "Backfill",
    "State": "Analyzed",
    "CreatedUtc": "2026-10-29T21:11:05.310433Z",
    "Subject": null,
    "Status": null,
    "Site": null,
    "Mechanism": { "ExceptionType": null, "ExceptionMessage": null, "StackFrames": [], "ExitCode": null, "RuntimeFailureKind": null, "FailingCommand": null, "CommandExitCode": null, "CommandOutputTail": null, "GitOperation": null, "GitStderrTail": null },
    "Classification": null,
    "Workspace": null,
    "Conflict": null,
    "Scope": null,
    "Configuration": null,
    "Attempts": [],
    "Evidence": null,
    "Analysis": null,
    "Related": null,
    "Voyage": null,
    "Redaction": { "RulesApplied": [], "RedactedCount": 0 },
    "Truncation": { "Fields": [], "FieldTotal": 0, "TimedOut": false, "Notes": [] }
  }
}
```

### list_blackbox_objects

| Parameter | Type | Required | Description |
|---|---|---|---|
| `entryId` / `missionId` / `voyageId` | string | One of them | The entry |
| `pageNumber`, `pageSize` | integer | No | Paging (page size 1-1000, default 10; out-of-range values are normalized) |
| `order` | string | No | `CreatedDescending` (default) or `CreatedAscending` |

**Response:** `EnumerationResult<BlackBoxObjectSummary>` (`Name`, `ContentType`, `ContentEncoding`, `SizeBytes` stored,
`UncompressedBytes`, `Sha256`, `IsMissing`, `CreatedUtc`):

```json
{
  "Success": true,
  "PageNumber": 1,
  "PageSize": 10,
  "TotalPages": 1,
  "TotalRecords": 5,
  "Objects": [
    { "Name": "brief.md", "ContentType": "text/markdown", "ContentEncoding": "gzip", "SizeBytes": 2720, "UncompressedBytes": 6450, "Sha256": "0d9b1fd5d99d9bb7a46a0f68703c085c971a22d630d2dc9c73deda2c84a7d9d0", "IsMissing": false, "CreatedUtc": "2026-10-29T21:11:39.855168Z" },
    { "Name": "evidence/events.json", "ContentType": "application/json", "ContentEncoding": "gzip", "SizeBytes": 303, "UncompressedBytes": 892, "Sha256": "5e24fe18f6c54c862503ff3203ef61ff55bd21ad5fcb5f96e5140441fa70f326", "IsMissing": false, "CreatedUtc": "2026-10-29T21:11:39.855526Z" },
    { "Name": "record.json", "ContentType": "application/json", "ContentEncoding": "gzip", "SizeBytes": 3165, "UncompressedBytes": 11635, "Sha256": "942dfae0729b7a51a2a59c67441fa499513fbd9378e060fb28b0bbe29b88aed2", "IsMissing": false, "CreatedUtc": "2026-10-29T21:11:39.854618Z" }
  ],
  "TotalMs": 0
}
```

(Trimmed to three of the five objects.) The stored `brief*.md` objects are snapshots written at capture and analysis;
`get_blackbox` `format` `brief` renders the brief live with the newest notes.

### read_blackbox_object

Read a chunk of one object without flooding context.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `entryId` / `missionId` / `voyageId` | string | One of them | The entry |
| `name` | string | Yes | Object name, unencoded (for example `evidence/git-status.txt`) |
| `offset` | integer | No | Byte offset (default 0; not negative) |
| `length` | integer | No | Bytes to read, 1-65536 (default 65536, 64 KB) |

**Response:** a `BlackBoxObjectChunk`: `Content` (UTF-8 text for text objects, cut back to a character boundary;
base64 otherwise, with `Encoding` `utf-8` or `base64`), `Offset`, `Length` (bytes returned), `TotalBytes`, `Sha256`, and
`NextOffset` while more remains (null at the end). Call again with `offset` = `NextOffset` to page through a large log.

```json
{ "entryId": "bbx_mvu1ftcv_zcdofOBmjhY", "name": "brief.md", "length": 400 }
```

```json
{
  "EntryId": "bbx_mvu1ftcv_zcdofOBmjhY",
  "Name": "brief.md",
  "ContentType": "text/markdown",
  "Offset": 0,
  "Length": 400,
  "TotalBytes": 6450,
  "NextOffset": 400,
  "Encoding": "utf-8",
  "Content": "# FIX THIS: Retry landings deferred on a dirty checkout (mission msn_mvu1fjp0_9Wd6Uuyp262)\n\nYou are asked to FIX THIS. This brief is a self-contained black box entry from Armada, a multi-agent orchestration system. It records where, how and why a mission failed, with evidence.\n\nBefore changing anything, triage the failure. Decide which one it is, and say why, quoting the evidence below:\n\n1. **User",
  "Sha256": "0d9b1fd5d99d9bb7a46a0f68703c085c971a22d630d2dc9c73deda2c84a7d9d0"
}
```

`InvalidArgument` without `name`, for a negative `offset` or a `length` outside 1-65536; `NotFound`
(`BlackBoxNotFound`, or `BlackBoxObjectMissing` when the store lost the bytes); `Conflict` (`BlackBoxObjectCorrupt`);
`Unavailable` (`BlackBoxStoreUnavailable`).

### list_blackbox_entries

| Parameter | Type | Required | Description |
|---|---|---|---|
| `vesselId`, `voyageId`, `missionId`, `captainId`, `fleetId` | string | No | Id filters |
| `subjectType` | string | No | `Mission` or `Voyage` |
| `phase` | string | No | `Dispatch`, `Provision`, `Execution`, `Completion`, `Review`, `Handoff`, `Landing`, `MergeQueue`, `Recovery`, `Voyage`, `External` |
| `step` | string | No | `BlackBoxStepEnum` name (for example `DirtyCheckout`) |
| `kind` | string | No | `MissionFailureKindEnum` name |
| `triage` | string | No | `Unknown`, `UserConfig`, `MissionBrief`, `Transient`, `ArmadaDefect`, `External` |
| `resolution` | string | No | `Open`, `Resolved`, `Duplicate`, `WontFix`, `ArmadaFixed` |
| `state` | string | No | `Captured`, `Enriched`, `Analyzed`, `EnrichmentFailed`, `ObjectMissing` |
| `fingerprint` | string | No | Entries of the same failure mode |
| `pinned`, `isCurrent` | boolean | No | Pinned or current entries only (true), or the others (false) |
| `path` | string | No | Repository path recorded in any file role |
| `resilienceScope` | boolean | No | Entries in Armada's handoff and landing scope (true) or outside it (false) |
| `createdAfter`, `createdBefore` | string | No | ISO 8601 (inclusive / exclusive) |
| `pageNumber`, `pageSize`, `order` | | No | Paging (1-1000, default 10), `CreatedDescending` (default) or `CreatedAscending` |

The `BlackBoxEnumerateRequest` fields the schema does not list (`harborId`, `captureMode`) are also accepted.
**Response:** `EnumerationResult<BlackBoxEntrySummary>`, newest first:

```json
{ "vesselId": "vsl_mvu1ffz5_4Vb0gY5fhKv", "isCurrent": true, "pageSize": 5 }
```

```json
{
  "Success": true,
  "PageNumber": 1,
  "PageSize": 5,
  "TotalPages": 1,
  "TotalRecords": 3,
  "Objects": [
    {
      "Id": "bbx_mvu1ftew_95PyT7WmtrV",
      "SubjectType": "Voyage",
      "MissionId": null,
      "VoyageId": "vyg_mvu1fjoz_xO6urNkcaHp",
      "VesselId": "vsl_mvu1ffz5_4Vb0gY5fhKv",
      "CaptainId": null,
      "MissionTitle": "Harden landing retries",
      "VesselName": "demo-api",
      "Attempt": 1,
      "IsCurrent": true,
      "Phase": "Voyage",
      "Step": "SettlementFailed",
      "FailureKind": "Crash",
      "StatusBefore": null,
      "StatusAfter": "Failed",
      "Triage": "Transient",
      "Fingerprint": "bcb7be821991076ba7d7551c9c5714b099bddaa13843c800b1bd4ee271e37329",
      "CaptureMode": "Backfill",
      "State": "Analyzed",
      "Resolution": "Open",
      "Pinned": false,
      "ReasonExcerpt": "Mission msn_mvu1fjp0_9Wd6Uuyp262 failed: Captain process exited with code 134 after 4m12s while running dotnet test ...",
      "TotalBytes": 9199,
      "ObjectCount": 5,
      "CreatedUtc": "2026-10-29T21:11:05.382585Z",
      "ExpiresUtc": "2027-04-27T21:11:05.382585Z"
    }
  ],
  "TotalMs": 0
}
```

(Trimmed to one row.)

### search_blackbox

Full-text search (case-insensitive substring) over the reason, summary and notes of the visible entries.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `query` | string | Yes | 1-200 characters |
| filters and paging | | No | As [list_blackbox_entries](#list_blackbox_entries) |

```json
{ "query": "SQLitePCL", "pageSize": 5 }
```

**Response:** `EnumerationResult<BlackBoxEntrySummary>` (rows as above). `InvalidArgument` for an empty or over-long
query.

### list_blackbox_notes

| Parameter | Type | Required | Description |
|---|---|---|---|
| `entryId` / `missionId` / `voyageId` | string | One of them | The entry |
| `pageNumber`, `pageSize`, `order` | | No | Paging (1-1000, default 10) |

**Response:** `EnumerationResult<BlackBoxNote>`, newest first. Every recovery dry run and run adds a note:

```json
{
  "Success": true,
  "PageNumber": 1,
  "PageSize": 10,
  "TotalPages": 1,
  "TotalRecords": 2,
  "Objects": [
    {
      "Id": "bbn_mvu1gjz0_6I5xZubo4wF",
      "EntryId": "bbx_mvu1ftcv_zcdofOBmjhY",
      "TenantId": "default",
      "UserId": "default",
      "AuthorKind": "Operator",
      "Body": "Ran recovery path bbx-exec-006.restart (Restart the mission): succeeded. 1. restart_mission: ok ({\"id\":\"msn_mvu1fjp0_9Wd6Uuyp262\", ...)",
      "CreatedUtc": "2026-10-29T21:11:39.804858Z"
    },
    {
      "Id": "bbn_mvu1gjri_gmnRF09pl9P",
      "EntryId": "bbx_mvu1ftcv_zcdofOBmjhY",
      "TenantId": "default",
      "UserId": "default",
      "AuthorKind": "Operator",
      "Body": "Reproduced locally: the test host crashes in SQLitePCL under parallel suites. Restarting after pinning the native library.",
      "CreatedUtc": "2026-10-29T21:11:39.5342Z"
    }
  ],
  "TotalMs": 0
}
```

### get_blackbox_bundle

A maintainer bundle: up to `max` of the newest matching entries grouped by fingerprint (highest resilience severity
first, then most frequent), each group with its brief, proposed Armada changes and regression test, to produce Armada
missions on the Armada vessel. By default (no `allFailures`, `triage` or `resilienceScope`) it holds the Armada
resilience gaps: entries in handoff and landing scope triaged `ArmadaDefect`.

| Parameter | Type | Required | Description |
|---|---|---|---|
| filters | | No | The [list_blackbox_entries](#list_blackbox_entries) filters |
| `max` | integer | No | Entries, 1-50 (default 50) |
| `format` | string | No | `Markdown` (default) or `Json` |
| `redactHosts` | boolean | No | Strip absolute paths, host names and user names (default false) |
| `allFailures` | boolean | No | Bundle every failure instead of the resilience gaps (default false) |

**Response:** `{ Format, EntryCount, FingerprintCount, Content, Truncated }`; `Content` is capped at 1 MB. With
`format` `Json`, `Content` is a camelCase JSON document (`preamble`, `entryCount`, `fingerprintCount`, `groups`; each group adds `severity`, `selfHealable`, `proposedChanges` and `regressionTest`):

```json
{
  "Format": "Json",
  "EntryCount": 3,
  "FingerprintCount": 3,
  "Content": "{\n  \"preamble\": \"These are 3 Armada black box entries in 3 failure modes, grouped by fingerprint (most frequent first). You are asked to improve Armada itself. ...\",\n  \"entryCount\": 3,\n  \"fingerprintCount\": 3,\n  \"groups\": [ ... ]\n}",
  "Truncated": false
}
```

### list_blackbox_hotspots

Directories that recur in failures, most files first.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `vesselId` | string | No | Vessel filter |
| `role` | string | No | `Conflicted` (the default when omitted), `DirtyInCheckout`, `OutOfScope`, `ChangedByMission`, `ChangedByConflictingMission`, `BoundaryViolation` |
| `createdAfter`, `createdBefore` | string | No | ISO 8601 |
| `pageNumber`, `pageSize`, `order` | | No | Paging (1-1000, default 10) |

**Response:** `EnumerationResult<BlackBoxHotspot>` (`VesselId`, `Directory`, `FileCount`, `EntryCount`, `LastSeenUtc`):

```json
{
  "Success": true,
  "PageNumber": 1,
  "PageSize": 5,
  "TotalPages": 1,
  "TotalRecords": 2,
  "Objects": [
    { "VesselId": "vsl_mvu1ffz5_4Vb0gY5fhKv", "Directory": "src/Armada.Server/Routes", "FileCount": 12, "EntryCount": 1, "LastSeenUtc": "2026-10-29T21:11:05.310433Z" },
    { "VesselId": "vsl_mvu1ffz5_4Vb0gY5fhKv", "Directory": "src/Armada.Core/Services", "FileCount": 8, "EntryCount": 1, "LastSeenUtc": "2026-10-29T21:11:05.310433Z" }
  ],
  "TotalMs": 0
}
```

### list_blackbox_fingerprints

Failure modes (entries grouped by fingerprint), by count, then last seen. Compare one mode's entries with
`list_blackbox_entries {fingerprint}`.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `vesselId` | string | No | Vessel filter |
| `resolution` | string | No | Count only entries with this resolution |
| `createdAfter`, `createdBefore` | string | No | ISO 8601 |
| `pageNumber`, `pageSize`, `order` | | No | Paging (1-1000, default 10) |

```json
{
  "Success": true,
  "PageNumber": 1,
  "PageSize": 5,
  "TotalPages": 1,
  "TotalRecords": 3,
  "Objects": [
    { "Fingerprint": "0c0d4699f8052fede78fbc067119a0dd57940124eefd9ae8b0bfc8df8d7a0149", "Count": 1, "OpenCount": 1, "FirstSeenUtc": "2026-10-29T21:11:05.377352Z", "LastSeenUtc": "2026-10-29T21:11:05.377352Z", "LatestEntryId": "bbx_mvu1ftep_b5OtDr4leub", "Phase": "Handoff", "Step": "DependencyCancelled", "FailureKind": "DependencyFailed" }
  ],
  "TotalMs": 0
}
```

(Trimmed to one row.)

### list_blackbox_resilience_gaps

Armada resilience gaps: failure modes (fingerprints) in Armada's own handoff and landing that the black box triaged
`ArmadaDefect`, most severe first within the page (then occurrences, then last seen). Use it to pick the Armada
missions that make handoff and landing more resilient.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `vesselId` | string | No | Vessel filter |
| `resolution` | string | No | Count only entries with this resolution |
| `phase` | string | No | `Handoff`, `Landing`, `MergeQueue`, `Recovery`, `Provision`, `Completion` or `Review` (default: the whole scope) |
| `createdAfter`, `createdBefore` | string | No | ISO 8601 |
| `pageNumber`, `pageSize`, `order` | | No | Paging (page size at most 50; larger values are capped) |

**Response:** `EnumerationResult<BlackBoxResilienceGap>`: `Fingerprint`, `Phase`, `Step`, `FailureKind`, `Occurrences`,
`OpenCount`, `FirstSeenUtc`, `LastSeenUtc`, `LatestEntryId`, `LatestMissionId`, `LatestMissionTitle`, `VesselName`,
`ArmadaVersions`, `Severity` (escalated by recurrence: 1 Medium, 3 High, 5 or two Armada versions Critical),
`SelfHealable`, `Suspected`, `DetectionGap`, `TopProposedChange` (its `MissionBrief` left out) and `RuleIds`. Scoped
to what the caller can see; `InvalidArgument` when `createdAfter` is after `createdBefore`.

```json
{
  "Success": true,
  "PageNumber": 1,
  "PageSize": 10,
  "TotalPages": 1,
  "TotalRecords": 1,
  "Objects": [
    {
      "Fingerprint": "9c1f0e6d2a7b4c3e8f5a1d0b6c2e7f4a9b3d8c1e5f0a2b7c4d9e6f3a8b1c5d2e",
      "Phase": "Landing",
      "Step": "TargetIntegration",
      "FailureKind": "LandingConflict",
      "Occurrences": 3,
      "OpenCount": 3,
      "FirstSeenUtc": "2026-10-27T09:14:02.110000Z",
      "LastSeenUtc": "2026-10-29T21:11:05.310433Z",
      "LatestEntryId": "bbx_mvu1ftew_95PyT7WmtrV",
      "LatestMissionId": "msn_mvu1fjp0_9Wd6Uuyp262",
      "LatestMissionTitle": "Retry landings deferred on a dirty checkout",
      "VesselName": "demo-api",
      "ArmadaVersions": [ "1.2.0" ],
      "Severity": "High",
      "SelfHealable": "Yes",
      "Suspected": false,
      "TopProposedChange": { "Rank": 1, "Title": "Recreate a missing dock worktree from the branch before landing", "Severity": "High", "RuleId": "BBX-LAND-002", "MissionBrief": null },
      "DetectionGap": "The dock was reclaimed or its directory removed while the mission still had to land ...",
      "RuleIds": [ "BBX-LAND-002", "BBX-PRIOR-ARMADA" ]
    }
  ],
  "TotalMs": 0
}
```

(`TopProposedChange` trimmed.)

### get_blackbox_fingerprint_trend

| Parameter | Type | Required | Description |
|---|---|---|---|
| `fingerprint` | string | Yes | From an entry or `list_blackbox_fingerprints` |

**Response:** occurrences per Armada version, ordered by first sighting, with open and `ArmadaFixed` counts (at most
1000 entries and 100 versions; `Truncated` when cut):

```json
{
  "Fingerprint": "bcb7be821991076ba7d7551c9c5714b099bddaa13843c800b1bd4ee271e37329",
  "Phase": "Voyage",
  "Step": "SettlementFailed",
  "Total": 1,
  "Truncated": false,
  "Versions": [
    { "ArmadaVersion": "1.2.0+5de469cff2709733b1ffb68fbc45d1303e9e41be", "Count": 1, "OpenCount": 1, "ArmadaFixedCount": 0, "FirstSeenUtc": "2026-10-29T21:11:05.382585Z", "LastSeenUtc": "2026-10-29T21:11:05.382585Z" }
  ]
}
```

### get_blackbox_retention

No arguments. TenantAdmin: a tenant admin sees their tenant's counts, a global admin every tenant.

```json
{
  "Policy": { "RetentionDays": 180, "ResolvedRetentionDays": 30, "KeepLatestPerMission": true, "MaxEntriesPerVessel": 1000, "MaxTotalBytes": 1073741824, "DeleteWithMission": false, "DeleteWithVessel": true, "GcIntervalMinutes": 60, "GcBatchSize": 200, "OrphanGraceHours": 24 },
  "TotalEntries": 3,
  "TotalBytes": 28653,
  "PinnedEntries": 0,
  "DeletingEntries": 0,
  "ExpiredEntries": 0,
  "LastRun": null,
  "NextRunUtc": "2026-10-29T22:10:34.41649Z"
}
```

### get_blackbox_storage

No arguments. AdminOnly. The configured target for new entries, its health, `FallbackToDatabase`, entries and bytes per
target (`Usage`, where existing entries are) and `ActiveMigrationId`:

```json
{
  "Target": "Database",
  "Health": { "Target": "Database", "Healthy": true, "Message": "Database store reachable.", "LatencyMs": 0, "CheckedUtc": "2026-10-29T21:12:14.102016Z" },
  "FallbackToDatabase": true,
  "Usage": [ { "Target": "Disk", "Entries": 3, "Bytes": 28653 } ],
  "ActiveMigrationId": null
}
```

(Captured after a storage migration moved every entry to `Disk` while the configured target stayed `Database`.)

### annotate_blackbox

Add a note (recorded with `AuthorKind` `Agent`). Annotate what you found and what you did, so the next reader does not
repeat it.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `entryId` / `missionId` / `voyageId` | string | One of them | The entry |
| `body` | string | Yes | Note text, 1-8000 characters |

```json
{ "entryId": "bbx_mvu1ftcv_zcdofOBmjhY", "body": "Agent: confirmed the crash is in the test host, not the mission change." }
```

```json
{
  "Id": "bbn_mvu1hafs_7Ea3fazlj3b",
  "EntryId": "bbx_mvu1ftcv_zcdofOBmjhY",
  "TenantId": "default",
  "UserId": "default",
  "AuthorKind": "Agent",
  "Body": "Agent: confirmed the crash is in the test host, not the mission change.",
  "CreatedUtc": "2026-10-29T21:12:14.104947Z"
}
```

### resolve_blackbox

| Parameter | Type | Required | Description |
|---|---|---|---|
| `entryId` / `missionId` / `voyageId` | string | One of them | The entry |
| `resolution` | string | Yes | `Open` (reopens), `Resolved`, `Duplicate`, `WontFix` or `ArmadaFixed` |
| `note` | string | No | What fixed it (up to 2000 characters) |
| `link` | string | No | The landed commit, a pull request or a mission (up to 1000 characters) |

```json
{ "missionId": "msn_mvu1fjp0_9Wd6Uuyp262", "resolution": "Resolved", "note": "Landed after restart.", "link": "msn_mvu1fjp0_9Wd6Uuyp262" }
```

**Response:** the updated `BlackBoxEntry` (`Record` null), with `Resolution`, `ResolutionNote`, `ResolutionLink`,
`ResolvedUtc` and the recomputed `ExpiresUtc`; emits `blackbox.resolved`. `InvalidArgument` for another resolution.

### run_blackbox_recovery

Run one recovery path recorded in the entry's analysis. It never takes a free-form command: only the recorded steps
run (tools `retry_landing`, `restart_mission`, `cancel_mission`, `unstick_dock`, `repair_dock`, `update_vessel`,
`update_mission`, targeting the entry's own mission, vessel or dock), each authorized as if you had called that tool.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `entryId` / `missionId` / `voyageId` | string | One of them | The entry |
| `pathId` | string | Yes | Recovery path id from the analysis (for example `bbx-land-001.stash-retry`) |
| `dryRun` | boolean | No | Default **true**: return the plan and a `confirmToken` without acting |
| `confirmToken` | string | When `dryRun` is false | The token from the dry run (`bbc_...`, 10 minutes, single use) |
| `acknowledgeDataLoss` | boolean | For a `dataLossRisk` path | `true` after the user confirmed (paths that use `cancel_mission` or `unstick_dock`) |

The token binds the entry, the path, the caller, the plan and the mission's current version. Pass `dryRun: false`
explicitly to run: a call with a `confirmToken` but no `dryRun` is another dry run. Every dry run and run is annotated on
the entry (`AuthorKind` `Agent`) and emits `blackbox.recovery_run`.

Dry run:

```json
{ "entryId": "bbx_mvu1ftcv_zcdofOBmjhY", "pathId": "bbx-exec-006.restart", "dryRun": true }
```

```json
{
  "EntryId": "bbx_mvu1ftcv_zcdofOBmjhY",
  "PathId": "bbx-exec-006.restart",
  "DryRun": true,
  "ConfirmToken": "bbc_55295476af829b0bce6827f00e3ef896",
  "ConfirmTokenExpiresUtc": "2026-10-29T21:22:14.120578Z",
  "Plan": [
    {
      "Order": 1,
      "Description": "Restart msn_mvu1fjp0_9Wd6Uuyp262.",
      "McpCommand": "restart_mission {\"missionId\": \"msn_mvu1fjp0_9Wd6Uuyp262\"}",
      "RestCommand": "POST /api/v1/missions/msn_mvu1fjp0_9Wd6Uuyp262/restart",
      "CliCommand": "armada mission restart msn_mvu1fjp0_9Wd6Uuyp262",
      "Action": { "Tool": "restart_mission", "Arguments": { "missionId": "msn_mvu1fjp0_9Wd6Uuyp262" } },
      "DataLossRisk": false
    }
  ],
  "StepResults": [],
  "Succeeded": true,
  "Refused": false,
  "UnmetPreconditions": [],
  "Message": "Would run: restart_mission {\"missionId\": \"msn_mvu1fjp0_9Wd6Uuyp262\"}. Confirm with confirmToken before 2026-10-29 21:22:14Z."
}
```

Confirm:

```json
{ "entryId": "bbx_mvu1ftcv_zcdofOBmjhY", "pathId": "bbx-exec-006.restart", "dryRun": false, "confirmToken": "bbc_55295476af829b0bce6827f00e3ef896" }
```

**Response:** `DryRun` false and `StepResults` (`Order`, `Tool`, `Succeeded`, `Message`); the first failed step stops
the run and the rest report "Not run", and a manual step is reported, not run. Step preconditions (for example
`restart_mission` needs a Failed, LandingFailed or Cancelled mission, not one already Pending) are checked on the dry
run and on the confirm: when one is not met, `Refused` is `true`, `Succeeded` `false` and `UnmetPreconditions` names the
step; a refused dry run has no `ConfirmToken` and a refused confirm runs nothing. Without a token (or with a used, expired
or foreign one):

```json
{
  "Error": "A confirmToken is required: do a dry run of this recovery path first (dryRun true) and confirm with the token it returns.",
  "ErrorCode": "Conflict",
  "Code": "BlackBoxConfirmationRequired",
  "StatusCode": null
}
```

`InvalidArgument` without `pathId`, or when a step names an unsupported tool or another subject; `NotFound` for an
unknown path; `Forbidden` when you could not call a step's tool directly; `Unavailable`
(`BlackBoxActionsUnavailable`) without an action executor.

### apply_blackbox_recommendation

Apply one configuration recommendation recorded in the analysis (`recommendationId`, for example
`bbx-land-001.landing-mode`) to prevent the failure next time. It runs only the recorded `applyAction` (for example an
`update_vessel` patch), authorized as that tool. Same flow as `run_blackbox_recovery`: dry run first, tell the user what
will change, then call with `dryRun: false` and the token.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `entryId` / `missionId` / `voyageId` | string | One of them | The entry |
| `recommendationId` | string | Yes | Recommendation id from the analysis |
| `dryRun` | boolean | No | Default true |
| `confirmToken` | string | When `dryRun` is false | The token from the dry run |

**Response:** `{ EntryId, RecommendationId, DryRun, ConfirmToken, ConfirmTokenExpiresUtc, Action, Succeeded, Message }`.
An unknown recommendation:

```json
{
  "Error": "Recommendation nope is not recorded on black box entry bbx_mvu1ftcv_zcdofOBmjhY.",
  "ErrorCode": "NotFound",
  "Code": "BlackBoxNotFound",
  "StatusCode": null
}
```

`InvalidArgument` for an advice-only recommendation (no `applyAction`); `Conflict` (`BlackBoxConfirmationRequired`)
without a valid token.

### pin_blackbox

| Parameter | Type | Required | Description |
|---|---|---|---|
| `entryId` / `missionId` / `voyageId` | string | One of them | The entry |
| `pinned` | boolean | No | `true` (default) to pin, `false` to unpin |

**Response:** the updated `BlackBoxEntry` (`Record` null). Garbage collection never deletes a pinned entry: pinning
clears `ExpiresUtc` and unpinning recomputes it.

### delete_blackbox_entries

Delete entries by id. This cannot be undone: pin entries you want to keep instead. TenantAdmin.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `entryIds` | string[] | Yes | Entries to delete (`bbx_`), at most 1000 |

```json
{ "entryIds": [ "bbx_doesnotexist_0000000000" ] }
```

```json
{ "Requested": 1, "Deleted": 0, "Pending": 0, "BytesFreed": 0, "Skipped": [ "bbx_doesnotexist_0000000000" ] }
```

Missing or invisible ids are skipped. More than 1000 ids is `InvalidArgument` with `Code` `CollectionLimitExceeded`
before anything is deleted. An entry whose store delete fails stays `Deleting` (counted in `Pending`) and is retried by
garbage collection.

### purge_blackbox

Delete every entry matching the filters, oldest first. AdminOnly. Run the dry run, show the user the counts, and only
then purge with `dryRun` false.

| Parameter | Type | Required | Description |
|---|---|---|---|
| filters | | No | The [list_blackbox_entries](#list_blackbox_entries) filters |
| `dryRun` | boolean | No | Default **true**: report without deleting |
| `includePinned` | boolean | No | Also delete pinned entries (default false) |
| `maxEntries` | integer | No | 1-100000 (default 10000) |

```json
{ "DryRun": true, "Matched": 0, "Deleted": 0, "Pending": 0, "SkippedPinned": 0, "BytesFreed": 0, "SampleEntryIds": [] }
```

### run_blackbox_gc

One garbage collection pass now (expiry, quotas, cascades, retried deletes, orphan sweep). AdminOnly. `dryRun`
(default **false**) lists the candidates without deleting.

```json
{
  "DryRun": true,
  "StartedUtc": "2026-10-29T21:12:14.136134Z",
  "FinishedUtc": "2026-10-29T21:12:14.141594Z",
  "DurationMs": 5,
  "Expired": 0,
  "Evicted": 0,
  "Cascaded": 0,
  "RetriedDeletes": 0,
  "OrphansRemoved": 0,
  "ObjectsMissing": 0,
  "BytesFreed": 0,
  "Failures": 0,
  "CandidateEntryIds": [],
  "Errors": []
}
```

### export_blackbox_fixture

Export an entry as a sanitized regression fixture (record without secrets, tenant and user ids, host paths or
repository URL, plus the rule ids and triage the advisor produces now), to add under
`src/Test.Shared/Fixtures/BlackBox` with a test. AdminOnly.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `entryId` / `missionId` / `voyageId` | string | One of them | The entry |
| `name` | string | No | Fixture name (file name without `.json`) |

```json
{
  "Name": "exec-006-process-crash",
  "Description": "Black box fixture for Execution / ProcessCrash.",
  "Source": "msn_mvu1fjp0_9Wd6Uuyp262",
  "RulesVersion": 1,
  "ExpectedRuleIds": [ "BBX-EXEC-006" ],
  "ExpectedTriage": "Transient",
  "Record": { "EntryId": "bbx_mvu1ftcv_zcdofOBmjhY", "Subject": { "TenantId": null, "UserId": null, "VesselRepoUrl": "https://example.invalid/demo-api.git" } }
}
```

(`Record` trimmed.)

### reanalyze_blackbox

Re-run enrichment and analysis with the current rules (rewrites `record.json` and the briefs). TenantAdmin. Takes
`entryId`, `missionId` or `voyageId`. **Response:** the re-analyzed `BlackBoxEntry`; emits `blackbox.analyzed`.

### Resources and prompts

The Admiral's HTTP MCP server (not `armada mcp stdio`) also serves black box **resource templates** and **prompts**,
registered when the black box is running. Every read runs as the MCP caller through `IBlackBoxService`.

| URI template | MIME type | Content |
|---|---|---|
| `armada://blackbox/{entryId}/brief.md` | `text/markdown` | The FIX THIS brief, rendered live |
| `armada://blackbox/{entryId}/record.json` | `application/json` | The record, camelCase (the `record.json` serializer) |
| `armada://blackbox/{entryId}/objects/{name}` | the object's | The first 64 KB of one object. URL-encode a name with a folder (`evidence%2Fgit-status.txt`). A text object longer than 64 KB ends with a `[first N of M bytes; read the rest with read_blackbox_object offset N]` line; a binary object is returned as a base64 `blob` |
| `armada://missions/{missionId}/blackbox` | `application/json` | The digest (what `get_blackbox` `format` `summary` returns) of the mission's current entry, camelCase |

A read of an entry, object or mission the caller cannot see (missing, or in another tenant) is the MCP resource-not-found
error, JSON-RPC code `-32002`, whose `data` carries the `uri` and the same typed error a black box tool returns
(`errorCode` `NotFound`, `code` `BlackBoxNotFound` or `BlackBoxObjectMissing`). Any other black box error (an entry being
deleted, a corrupt object, an unavailable store) is `-32602` with the same `data` shape:

```json
{ "jsonrpc": "2.0", "id": 33, "error": { "code": -32002, "message": "Black box entry bbx_missing not found.", "data": { "uri": "armada://blackbox/bbx_missing/record.json", "error": "Black box entry bbx_missing not found.", "errorCode": "NotFound", "code": "BlackBoxNotFound" } } }
```

`resources/list` is empty (there are no fixed resources); discover the templates with `resources/templates/list`:

```json
{ "jsonrpc": "2.0", "id": 32, "method": "resources/templates/list" }
```

```json
{
  "jsonrpc": "2.0",
  "result": {
    "resourceTemplates": [
      { "uriTemplate": "armada://blackbox/{entryId}/brief.md", "name": "blackbox_brief", "description": "The FIX THIS brief of a black box entry, as Markdown.", "mimeType": "text/markdown" },
      { "uriTemplate": "armada://blackbox/{entryId}/record.json", "name": "blackbox_record", "description": "The versioned record (record.json) of a black box entry.", "mimeType": "application/json" },
      { "uriTemplate": "armada://blackbox/{entryId}/objects/{name}", "name": "blackbox_object", "description": "The first 64 KB of one object of a black box entry (URL-encode a name with a folder, for example evidence%2Fgit-status.txt). Page a larger object with the read_blackbox_object tool.", "mimeType": "text/plain" },
      { "uriTemplate": "armada://missions/{missionId}/blackbox", "name": "mission_blackbox", "description": "The summary (digest) of a mission's current black box entry: where, how and why it failed, triage and the top recovery paths.", "mimeType": "application/json" }
    ]
  },
  "id": 32
}
```

`resources/read`:

```json
{ "jsonrpc": "2.0", "id": 33, "method": "resources/read", "params": { "uri": "armada://missions/msn_mvu1fjp0_9Wd6Uuyp262/blackbox" } }
```

```json
{
  "jsonrpc": "2.0",
  "result": {
    "contents": [
      {
        "uri": "armada://missions/msn_mvu1fjp0_9Wd6Uuyp262/blackbox",
        "mimeType": "application/json",
        "text": "{\n  \"entryId\": \"bbx_mvu1ftcv_zcdofOBmjhY\",\n  \"subjectType\": \"Mission\",\n  \"missionId\": \"msn_mvu1fjp0_9Wd6Uuyp262\",\n  \"phase\": \"Execution\",\n  \"step\": \"ProcessCrash\",\n  \"triage\": \"Transient\",\n  ...\n}"
      }
    ]
  },
  "id": 33
}
```

A read for an entry or object that does not exist, or is not visible, currently fails with the generic JSON-RPC error
`-32603` `Internal error`, not a typed `BlackBoxNotFound`; use the tools when the caller needs to tell the cases apart:

```json
{ "jsonrpc": "2.0", "error": { "code": -32603, "message": "Internal error" }, "id": 35 }
```

**Prompts.** `prompts/list` returns `review_blackbox` (arguments `missionId`, required, and `goal`: explain, fix,
prevent, fix Armada, compare or clean up, default explain) and `explain_blackbox` (`missionId`). Both expand to one
`user` message that carries the consumption flow below and the safety guidance.

```json
{ "jsonrpc": "2.0", "id": 37, "method": "prompts/get", "params": { "name": "review_blackbox", "arguments": { "missionId": "msn_mvu1fjp0_9Wd6Uuyp262", "goal": "fix it" } } }
```

```json
{
  "jsonrpc": "2.0",
  "result": {
    "description": "Review the black box of mission msn_mvu1fjp0_9Wd6Uuyp262",
    "messages": [
      {
        "role": "user",
        "content": {
          "type": "text",
          "text": "Review the black box for mission msn_mvu1fjp0_9Wd6Uuyp262 and fix it.\n\n1. Call get_blackbox {missionId: \"msn_mvu1fjp0_9Wd6Uuyp262\", format: \"summary\"} first.\n2. First assess whether Armada's own handoff and landing could have handled the failure: when the summary says ResilienceInScope, read read_blackbox_section {section: \"Resilience\"} (and Handoff for the step timeline and the recoveries Armada already tried), state whether Armada could have healed it, the detection gap and the proposed Armada change with its test, and offer Draft an Armada fix mission, before concluding it is the user's configuration or a transient.\n3. Read only the other sections the goal needs with read_blackbox_section (Conflict, Workspace, Analysis), and page evidence with read_blackbox_object instead of loading it whole.\n4. Map the goal to tools: explain (get_blackbox summary or brief, sections, objects); fix or recover (run_blackbox_recovery with a ranked path: dry run, then confirm); prevent (apply_blackbox_recommendation: dry run, then confirm); fix Armada (the Resilience section's top proposed change as a mission on the Armada vessel, or get_blackbox_bundle and list_blackbox_resilience_gaps for the recurring gaps); compare (list_blackbox_entries {fingerprint}, search_blackbox, list_blackbox_resilience_gaps); clean up (resolve_blackbox, pin_blackbox, delete_blackbox_entries).\n\nRead the black box summary first. Quote evidence, not guesses. State the triage verdict and whether you agree. If you act, use only recovery paths or recommendations from the entry, do a dry run, tell the user what will happen, and confirm before running anything marked dataLossRisk. Annotate the entry with what you did.\n"
        }
      }
    ]
  },
  "id": 37
}
```

`explain_blackbox` asks for what happened, the quoted evidence, the triage verdict and whether you agree, whether
Armada's handoff and landing could have handled it (the Resilience section, offering Draft an Armada fix mission), an opinion
clearly marked as such, and the recorded recovery paths and recommendations, and says "Do not run anything."

### Reviewing a black box and taking action

The flow for any MCP client asked to "review the black box for mission X and take action":

```
User:  Review the black box for mission msn_abc and fix it.
Agent: get_blackbox {missionId: "msn_abc", format: "summary"}
       -> where, how, why, triage UserConfig (High), recovery paths [bbx-land-001.stash-retry, bbx-land-001.pull-request]
Agent: read_blackbox_section {entryId, section: "Workspace"}                  (dirty paths, checkout)
Agent: read_blackbox_object {entryId, name: "evidence/git-status.txt"}        (64 KB at a time)
       -> ..., NextOffset: 65536; read_blackbox_object {entryId, name, offset: 65536} until NextOffset is null
Agent: run_blackbox_recovery {entryId, pathId: "bbx-land-001.pull-request", dryRun: true}
       -> "Would run: update_vessel {...}, then retry_landing {...}"; ConfirmToken bbc_...
Agent: (tells the user, or proceeds if told to act)
       run_blackbox_recovery {entryId, pathId: "bbx-land-001.pull-request", dryRun: false, confirmToken: "bbc_..."}
       -> per-step results; the entry is annotated automatically with what ran and the outcome
Agent: resolve_blackbox {entryId, resolution: "Resolved", note: "...", link: "msn_abc landed at <sha>"}
```

| Goal | Tools |
|---|---|
| Explain it | `get_blackbox` (summary or brief), `read_blackbox_section`, `read_blackbox_object` |
| Fix it, recover it | `run_blackbox_recovery` with a ranked path: dry run, then confirm |
| Prevent it | `apply_blackbox_recommendation` (a configuration patch): dry run, then confirm |
| Fix Armada | `read_blackbox_section {section: "Resilience"}` (the proposed Armada changes as mission briefs), `list_blackbox_resilience_gaps` or `get_blackbox_bundle`, then a mission on the Armada vessel |
| Compare | `list_blackbox_entries {fingerprint}`, `list_blackbox_fingerprints`, `list_blackbox_resilience_gaps`, `get_blackbox_fingerprint_trend`, `search_blackbox` |
| Clean up | `resolve_blackbox`, `pin_blackbox`, `delete_blackbox_entries` |

**Safety rules.** `run_blackbox_recovery` and `apply_blackbox_recommendation` run only actions recorded in the entry,
never free-form commands; each step is authorized as if the caller called that tool directly; a dry run is required
first, and the confirm token binds the entry, the path, the caller and the mission's current version, so a mission
that changed since the dry run must be re-checked; steps that can lose work are marked `dataLossRisk` and are refused
without `acknowledgeDataLoss: true`; every run is annotated on the entry and emits `blackbox.recovery_run`. The action
tool descriptions and both prompts carry this guidance (`McpBlackBoxTools.SafetyGuidance`):

> Read the black box summary first. Quote evidence, not guesses. State the triage verdict and whether you agree. If you
> act, use only recovery paths or recommendations from the entry, do a dry run, tell the user what will happen, and
> confirm before running anything marked dataLossRisk. Annotate the entry with what you did.

> **Plan differences.** MISSION_BLACKBOX_PLAN.md section 9 shows the confirmed call as
> `run_blackbox_recovery {entryId, pathId, confirmToken}`; in the code `dryRun` defaults to true even with a token, so
> the confirmed call must pass `dryRun: false`. The plan's section 8.2 also lists neither `get_blackbox_fingerprint_trend`
> nor `export_blackbox_fixture`, and treats the resources and prompts as conditional on Voltaic support; the code
> registers all of them (resources and prompts on the HTTP server only), and `get_blackbox_retention` and
> `get_blackbox_storage` require a tenant admin and a global admin even though they are on the Ask read-only list.

---


## Ask Armada

These tools manage Ask Armada conversations (`ath_`) over MCP, the same operations the dashboard, TUI, and mobile app
offer through [the REST API](REST_API.md#ask-armada-threads): list, read, start, rename, pin, archive, and delete
conversations, send messages and wait for the reply, stop or summarize a turn, run quick actions, and approve or reject
the actions Armada proposes. Every tool acts as the calling user on that user's own conversations; another user's
conversation answers `NotFound`, exactly as if it did not exist, and `delete_ask_threads` reports it under `notFound`.
Errors are `McpToolError`s: `InvalidArgument` (a required field is missing), `NotFound`, `Forbidden`, `Conflict` (a
turn is already running, or the proposal was already decided), and `Unavailable` (the turn could not be started, or the
server cannot run conversations: the stdio server lists these tools but answers every call with `Unavailable`).

A captain answering inside a conversation may call the readers (`list_ask_threads`, `get_ask_thread`,
`list_ask_messages`, `list_ask_quick_actions`, `get_ask_work`, `list_ask_work`, `list_ask_work_missions`,
`list_ask_work_targets`, `list_ask_proposals`, `list_ask_tool_calls`); every other tool here is refused with `Forbidden` and
never proposed, so a captain cannot approve its own proposals, start turns, or change conversations (see
[Ask Armada thread-scoped calls](#ask-armada-thread-scoped-calls)).

### list_ask_threads

| Parameter | Type | Required | Description |
|---|---|---|---|
| `search` | string | No | Text to match in conversation titles |
| `includeArchived` | boolean | No | Include archived conversations (default `false`) |
| `pageNumber` | integer | No | Page number, 1-based (default 1) |
| `pageSize` | integer | No | Results per page (default 25) |

**Response:** `EnumerationResult<AskThread>`, pinned first, then most recent activity, with unread counts.

### get_ask_thread

| Parameter | Type | Required | Description |
|---|---|---|---|
| `threadId` | string | Yes | Conversation ID (`ath_`) |

**Response:** `AskThreadDetail`: the conversation, `trackedWorkCount`, `pendingProposalCount`,
`pendingCliPermissionCount`, and `activeTurn` (the running turn's progress with `toolCallCount`, or null). It embeds no
collections: read them with [list_ask_work](#list_ask_work), [list_ask_proposals](#list_ask_proposals),
[list_ask_tool_calls](#list_ask_tool_calls), and [list_ask_messages](#list_ask_messages).

### create_ask_thread

| Parameter | Type | Required | Description |
|---|---|---|---|
| `title` | string | No | Title; Armada names the conversation from its first message otherwise |
| `captainId` | string | No | Captain that answers; the server's Ask captain otherwise. A captain the caller cannot see is `NotFound` |
| `autoApprove` | boolean | No | Run proposed actions without asking |

**Response:** the new `AskThread`.

### update_ask_thread

Only the fields supplied change.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `threadId` | string | Yes | Conversation ID |
| `title` | string | No | New title |
| `captainId` | string | No | Captain that answers from now on |
| `clearCaptain` | boolean | No | `true` to go back to the server's default Ask captain |
| `autoApprove` | boolean | No | Run proposed actions without asking |
| `pinned` | boolean | No | Pin or unpin |
| `archived` | boolean | No | Archive or unarchive |

**Response:** the updated `AskThread`.

### delete_ask_thread

Permanently deletes the conversation and its messages, stopping its turn if one is running. This cannot be undone.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `threadId` | string | Yes | Conversation ID |

**Response:** `{ "status": "Deleted", "threadId": "ath_..." }`.

### delete_ask_threads

| Parameter | Type | Required | Description |
|---|---|---|---|
| `threadIds` | string[] | Yes | Conversation IDs, at most 1000 |

**Response:** `{ "status": "Deleted", "deletedCount": 2, "deleted": [...], "notFound": [...] }`. IDs that do not exist
or belong to someone else are listed under `notFound` and left alone. More than 1000 IDs is `InvalidArgument` with
`code` `CollectionLimitExceeded`, and nothing is deleted.

### list_ask_messages

| Parameter | Type | Required | Description |
|---|---|---|---|
| `threadId` | string | Yes | Conversation ID |
| `beforeSequence` | integer | No | Return messages before this sequence number (pass the lowest one you have to page back) |
| `pageSize` | integer | No | Messages per page (default 50) |

**Response:** `AskMessagePage { Messages, HasMore }`, newest page first. Each message carries `toolCallCount`; read the
calls with [list_ask_tool_calls](#list_ask_tool_calls).

### send_ask_message

Sends a message; the conversation's captain answers in the background.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `threadId` | string | Yes | Conversation ID |
| `content` | string | Yes | Message text |
| `showThinking` | boolean | No | Include the captain's reasoning in the reply |
| `waitSeconds` | integer | No | Seconds to wait for the reply, 0 to 600 (default 0) |

**Response:** with `waitSeconds` 0, the started turn (`202` body of the REST call). Otherwise
`{ "completed": true, "turn": {...}, "recentMessages": AskMessagePage }` once the turn ends or the wait runs out
(`completed` is `false` then; read the reply later with `list_ask_messages`). `Conflict` while a turn is already
running.

### cancel_ask_turn

| Parameter | Type | Required | Description |
|---|---|---|---|
| `threadId` | string | Yes | Conversation ID |

Stops the running turn. **Response:** `{ "cancelled": true }`; `Conflict` when no turn is running.

### summarize_ask_thread

| Parameter | Type | Required | Description |
|---|---|---|---|
| `threadId` | string | Yes | Conversation ID |

Asks the captain to summarize the conversation so far, as a background turn. **Response:** the started turn.

### mark_ask_thread_read

| Parameter | Type | Required | Description |
|---|---|---|---|
| `threadId` | string | Yes | Conversation ID |

**Response:** the `AskThread` with `UnreadCount` 0.

### list_ask_quick_actions

No parameters. **Response:** the quick actions a conversation can run directly, each an Armada tool with its argument
form.

### run_ask_quick_action

| Parameter | Type | Required | Description |
|---|---|---|---|
| `threadId` | string | Yes | Conversation ID |
| `toolName` | string | Yes | A tool from `list_ask_quick_actions` |
| `arguments` | object | No | The tool's arguments |

The action becomes a proposal in the conversation that runs when approved, or at once when the conversation
auto-approves. **Response:** the proposal.

### approve_ask_proposal

| Parameter | Type | Required | Description |
|---|---|---|---|
| `threadId` | string | Yes | Conversation ID |
| `proposalId` | string | Yes | Proposal ID (`aap_`) |

Runs the proposed tool now, as the approving user. **Response:** the proposal with its outcome. `Conflict` when it was
already decided or has expired.

### reject_ask_proposal

| Parameter | Type | Required | Description |
|---|---|---|---|
| `threadId` | string | Yes | Conversation ID |
| `proposalId` | string | Yes | Proposal ID (`aap_`) |

The proposed tool never runs. **Response:** the rejected proposal.

### get_ask_work

| Parameter | Type | Required | Description |
|---|---|---|---|
| `threadId` | string | Yes | Conversation ID |
| `workId` | string | Yes | Tracked work ID (a voyage, mission, or fleet action run the conversation started) |

**Response:** the current snapshot of that work: status and counts, with `missionCount` and `targetCount`. It embeds no
rows; read them with [list_ask_work_missions](#list_ask_work_missions) and [list_ask_work_targets](#list_ask_work_targets).

### list_ask_work

| Parameter | Type | Required | Description |
|---|---|---|---|
| `threadId` | string | Yes | Conversation ID |
| `status` | string | No | State filter: `Active`, `Succeeded`, `Failed`, `Cancelled` (anything else is `InvalidArgument`) |
| `pageNumber` | integer | No | 1-based page number (default 1) |
| `pageSize` | integer | No | Items per page, 1 to 1000 (default 10; larger values are clamped to 1000) |
| `order` | string | No | `CreatedDescending` (newest first, default) or `CreatedAscending` |

**Response:** `EnumerationResult<AskTrackedWork>`, paged in the database; each item carries a counts-only snapshot.
`NotFound` for another user's conversation.

### list_ask_work_missions

| Parameter | Type | Required | Description |
|---|---|---|---|
| `threadId` | string | Yes | Conversation ID |
| `workId` | string | Yes | Tracked work ID (`atw_`) |
| `pageNumber` | integer | No | 1-based page number (default 1) |
| `pageSize` | integer | No | Items per page, 1 to 1000 (default 10; larger values are clamped to 1000) |
| `order` | string | No | `CreatedDescending` (newest first, default) or `CreatedAscending` |

**Response:** `EnumerationResult<AskWorkMissionSnapshot>`: a voyage's missions paged in the database, or the single row
of tracked mission work. Fleet action runs answer `InvalidArgument`; use [list_ask_work_targets](#list_ask_work_targets).

### list_ask_work_targets

| Parameter | Type | Required | Description |
|---|---|---|---|
| `threadId` | string | Yes | Conversation ID |
| `workId` | string | Yes | Tracked work ID of a fleet action run |
| `pageNumber` | integer | No | 1-based page number (default 1) |
| `pageSize` | integer | No | Items per page, 1 to 1000 (default 10; larger values are clamped to 1000) |

**Response:** `EnumerationResult<AskWorkTargetSnapshot>`, one row per vessel by vessel name. Other work types answer
`InvalidArgument`.

### list_ask_proposals

| Parameter | Type | Required | Description |
|---|---|---|---|
| `threadId` | string | Yes | Conversation ID |
| `status` | string | No | Proposal status filter, for example `Pending` (anything unknown is `InvalidArgument`) |
| `pageNumber` | integer | No | 1-based page number (default 1) |
| `pageSize` | integer | No | Items per page, 1 to 1000 (default 10; larger values are clamped to 1000) |
| `order` | string | No | `CreatedDescending` (newest first, default) or `CreatedAscending` |

**Response:** `EnumerationResult<AskActionProposal>`, paged in the database.

### list_ask_tool_calls

| Parameter | Type | Required | Description |
|---|---|---|---|
| `threadId` | string | Yes | Conversation ID |
| `messageId` | string | No | Message ID (`amg_`); omit it for the calls the running turn has made so far |
| `pageNumber` | integer | No | 1-based page number (default 1) |
| `pageSize` | integer | No | Items per page, 1 to 1000 (default 10; larger values are clamped to 1000) |
| `order` | string | No | `CreatedDescending` (newest first, default) or `CreatedAscending` |

**Response:** `EnumerationResult<AskMessageToolCall>`. `NotFound` when the message is not in the conversation.

### dispatch

Dispatch a new voyage with missions to a vessel. This is the primary way to assign work to the Armada system.

Optional `approvalOptions` has `approveAll` (default false), `timeoutSeconds` (10–3600, default 600), and `timeoutDecision` (`Deny` by default, or `Approve`). It persists per voyage and applies to tool permissions and human mission reviews, including later pipeline stages and rework. It leaves automated checks, endpoint restrictions, merge and deployment policies intact. `approveAll` and timeout `Approve` require an administrator's own credentials; captain session tokens cannot enable them. Omitting the object preserves existing behavior. See [per-run approvals](REST_API.md#per-run-approvals) for timeout and cancellation semantics.

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
| `missions` | array | No | Array of mission objects with `title` and optional `description`, `requestedCaptainId` (runs this mission, the first stage in a pipeline, overriding persona priorities), `tier` (minimum captain tier for every stage), and `persona` (single-stage missions without a pipeline; defaults to Worker). Omit for a bare voyage. |
| `pipelineId` | string | No | Pipeline ID to use for this voyage (overrides vessel/fleet default) |
| `pipeline` | string | No | Pipeline name to use (convenience alias for `pipelineId` -- resolves by name; a bad name is rejected) |
| `objectiveId` | string | No | Objective (prefix `obj_`) to link this voyage to; must exist |
| `selectedPlaybooks` | array | No | Ordered playbook selections with `playbookId` and `deliveryMode` |
| `captainAssignments` | array | No | Per-persona captain overrides. Array of `{ persona` (required)`, captainId, fallbackTier` (`Economy`/`Standard`/`Premium`)` }` -- binds a pipeline step (persona) to a preferred captain and a fallback tier. Applied before the first stage is assigned. |
| `landingMode` | string | No | `LocalMerge`, `MergeAndPush`, `PullRequest`, `MergeQueue`, or `None` for this voyage (overrides the vessel and server default) |
| `isEvaluation` | boolean | No | `true` for evaluation work (fixture battery, model evaluation): the voyage and every mission it creates are flagged `IsEvaluation`, and their stage outcomes never count as production data. Default `false` |

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

Get status of a specific voyage: the voyage with its mission counts. Missions are never embedded; read them one page at a
time with [enumerate](#enumerate) (`entityType` `missions`, `voyageId`).

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "voyageId": {
      "type": "string",
      "description": "Voyage ID (vyg_ prefix)"
    },
    "summary": {
      "type": "boolean",
      "description": "Return a trimmed voyage with the counts (default true). Set false for the full voyage object with the counts."
    }
  },
  "required": ["voyageId"]
}
```

| Parameter | Type | Required | Default | Description |
|---|---|---|---|---|
| `voyageId` | string | Yes | -- | Voyage ID (prefix `vyg_`) |
| `summary` | boolean | No | `true` | Trimmed voyage (`Id`, `Title`, `Description`, `Status`, `CreatedUtc`, `LastUpdateUtc`, `ApprovalOptions`, `IsEvaluation`, `LastBlackBoxEntryId`) with the counts; `false` returns the full voyage with the counts |

`includeMissions: true` is rejected with `ErrorCode` `InvalidArgument` naming the replacement; the former
`includeDescription`, `includeDiffs` and `includeLogs` flags are gone.

**Response:**

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

| Field | Type | Description |
|---|---|---|
| `Voyage` | object | [Voyage](#voyage) object (a subset of its fields in summary mode). Both modes carry `LastBlackBoxEntryId`, the voyage's current [black box](#black-box) entry (null until a failure is captured) |
| `TotalMissions` | int | Total number of missions in this voyage |
| `MissionCountsByStatus` | object | Map of status string to count (at most one entry per mission status) |

An unknown voyage, or one outside the caller's scope, returns `{ "Error": "Voyage not found", "ErrorCode": "NotFound" }`.

---

### list_voyage_progress

Page through voyages with their mission counts. With no `status` the view is the active voyages (the ones `status`
counts in `ActiveVoyages`): InProgress first, then Open, newest first within each. A `status` selects one voyage status.
Same scope as `enumerate` voyages.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "pageNumber": { "type": "integer", "description": "Page number (1-based, default 1)" },
    "pageSize": { "type": "integer", "description": "Page size (1 to 1000, default 10; out-of-range values are normalized)" },
    "status": { "type": "string", "description": "Open, InProgress, Complete, Failed, or Cancelled" },
    "order": { "type": "string", "description": "CreatedDescending (default) or CreatedAscending" }
  }
}
```

**Response:** `{ "PageNumber", "PageSize", "TotalPages", "TotalRecords", "Objects": [VoyageProgress] }`, each object the
voyage with `TotalMissions`, `CompletedMissions`, `FailedMissions`, `InProgressMissions`, `MissionCountsByStatus`,
`VesselId` and `VesselCount` (as `GET /api/v1/voyages/{id}`). An unknown `status` or `order` returns `InvalidArgument`.

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

**Response:** [Mission](#mission) object, or `{ "Error": "Mission not found", "ErrorCode": "NotFound" }` if the ID does not exist. `LastBlackBoxEntryId` names the mission's current [black box](#black-box) entry (`bbx_`) after a failure was captured (null otherwise); pass it, or the mission id, to `get_blackbox`.

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

Get one fleet and the number of vessels it holds in the caller's scope (`VesselCount`). The vessels are not embedded:
page them with [`enumerate`](#enumerate) (`entityType` `vessels`, `fleetId`).

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
  "VesselCount": 12
}
```

Returns `{ "Error": "Fleet not found", "ErrorCode": "NotFound" }` if the ID does not exist.

| Field | Type | Description |
|---|---|---|
| `fleet` | object | [Fleet](#fleet) object |
| `vesselCount` | number | Vessels in this fleet visible to the caller |

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
| `workerEnvelope` | object | No | Default Worker eligibility envelope: `maxFiles` (integer 1-1000), `maxSteps` (integer 1-1000), `minServedContextTokens` (integer 1-100000000), `requiredTools` (array of `tools`, `mcp`). Only captains whose model profile meets it run Worker missions on this vessel; the Architect may tighten it per mission but never loosen it. Omit or pass `{}` for no envelope. Out-of-range values or unknown tools return an `InvalidArgument` tool error |

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

**Response:** `VesselImportDiscoverResponse`: `BatchId`, `Batch` (a [VesselImportBatch](#vesselimportbatch)), `Candidates` (array of [VesselImportItem](#vesselimportitem)), `Truncated` (true when the 1,000-candidate cap was reached; `directories` and `roots` together hold at most 1,000 paths, more is `InvalidArgument` with `Code` `CollectionLimitExceeded`), and `Hints` (`{Code, Message}`; codes `PathNotVisibleToAdmiral`, `CandidateLimitReached`).

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

At most 1000 ids; more returns `ErrorCode` `InvalidArgument` with `Code` `CollectionLimitExceeded` and deletes nothing, so `Skipped` holds at most 1000 entries.

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

At most 1000 ids; more returns `ErrorCode` `InvalidArgument` with `Code` `CollectionLimitExceeded` and deletes nothing, so `Skipped` holds at most 1000 entries.

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

The mission the captain was running is set to `Cancelled` with failure kind `OperatorAction`, and its failure reason records who stopped it, through which surface, and when (for example `Stopped by operator (usr_...) via stop_captain (MCP) at 2026-10-28T02:37:59Z.`). A `mission.stopped_by_operator` event carries the same data. Before the dock is reclaimed, uncommitted work in it is committed to the mission branch as `WIP: preserved on operator stop` and pushed, best effort; the commit hash is added to the failure reason and the event. `stop_all` does the same for every captain it stops.

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

Returns the cancelled mission. Cancelling stops the mission's agent first: the captain's process is stopped through the lifecycle that owns it (on its Harbor when it runs there) and the exit is confirmed, then the captain is released when it holds no other active mission, then the mission is set to `Cancelled`. The dock is kept for inspection. When the stop is not confirmed (for example the owning Harbor is disconnected), nothing changes and the cancel is refused, so no other work is assigned to a captain whose agent may still run. Returns `{ "Error": "Mission not found", "ErrorCode": "NotFound" }` if the ID does not exist, and `ErrorCode` `Conflict` when the agent did not confirm it stopped.

**Response:** The updated [Mission](#mission) object with status `Cancelled`.

---

### restart_mission

Restart a Failed, LandingFailed, or Cancelled mission, resetting it to `Pending` for re-dispatch, or start a `Pending` mission as it is. Optionally update the title and description (instructions) before restarting. A failed or cancelled mission has the captain assignment, dock, PR URL, commit, failure, review, and timing fields cleared; a Pending one keeps every other field. A later pipeline stage keeps the previous stage's branch (a first stage starts on a fresh branch), a finished voyage is reopened, and the stages cancelled because this one failed are restored to Pending. The mission is then dispatched at once when a captain is free under the normal dispatch rules, and the result shows it `Assigned` or `InProgress`; otherwise it stays `Pending` (not an error) for the regular dispatcher. `Conflict` for any other status.

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

Cancel an entire voyage and its `Pending`, `Assigned`, and `Blocked` missions, each the same way `cancel_mission` does (a running agent is stopped first). In-progress missions and missions that are already `Complete` or `Cancelled` are not affected. A mission whose agent did not confirm it stopped is left unchanged and counted in `NotCancelledMissions`; read which missions remain with `enumerate` (missions, `voyageId`, `status`).

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
  "CancelledMissions": 3,
  "NotCancelledMissions": 0,
  "FirstNotCancelledReason": null
}
```

| Field | Type | Description |
|---|---|---|
| `Voyage` | object | The updated [Voyage](#voyage) object with status `Cancelled` |
| `CancelledMissions` | int | Number of missions that were cancelled |
| `NotCancelledMissions` | int | Number of Pending, Assigned, or Blocked missions that could not be cancelled |
| `FirstNotCancelledReason` | string | The first such mission and why, or null |

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

At most 1000 ids; more returns `ErrorCode` `InvalidArgument` with `Code` `CollectionLimitExceeded` and deletes nothing, so `Skipped` holds at most 1000 entries.

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

Returns `{ "Error": "ids is required and must not be empty", "ErrorCode": "InvalidArgument" }` if no IDs are provided. `ids` holds at most 1000 entries; a longer list returns `ErrorCode` `InvalidArgument` with `Code` `CollectionLimitExceeded` and deletes nothing. `Skipped` is bounded by the request.

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
    "definitionOfDoneTimeoutSeconds": { "type": "integer", "description": "Per-phase timeout in seconds, clamped to [30, 7200] (default 1800)" },
    "workerEnvelope": { "type": "object", "description": "Default Worker eligibility envelope (maxFiles, maxSteps, minServedContextTokens, requiredTools). The object replaces the stored envelope; pass {} to clear it; omit it to keep the stored value." }
  },
  "required": ["vesselId"]
}
```

`workerEnvelope` fields: `maxFiles` and `maxSteps` (integers 1-1000) guide the Architect's slicing and the Judge; `minServedContextTokens` (integer 1-100000000) and `requiredTools` (array of `tools`, `mcp`) restrict which captains may run Worker missions (the captain's model profile must record at least that served context and those capabilities). Out-of-range values or unknown tools return an `InvalidArgument` tool error and nothing is saved.

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
{ "Status": "deleted", "VesselId": "vsl_...", "WarningCount": 0, "Warnings": null }
```

When a cleanup step could not complete (a mission that could not be stopped or deleted, a dock that could not be purged,
a directory left in place), `WarningCount` counts them and `Warnings` lists the first 50.

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

Returns `{ "Error": "ids is required and must not be empty", "ErrorCode": "InvalidArgument" }` if no IDs are provided. `ids` holds at most 1000 entries; a longer list returns `ErrorCode` `InvalidArgument` with `Code` `CollectionLimitExceeded` and deletes nothing. `Skipped` is bounded by the request.

---

### vessel_health

Get one vessel's health: the row with effective (override-aware) statuses, the raw findings, the manual overrides, and `DependencyCount`. Dependency rows are not embedded; page them with [`list_vessel_dependencies`](#list_vessel_dependencies). The former `includeDependencies` argument was removed. The row and finding fields, enum spellings, and detail codes match `GET /api/v1/vessels/{id}/health` in [REST_API.md](REST_API.md#vessel-health). To list vessels by health, use [`enumerate`](#enumerate) with `entityType` `vessel_health`.

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

| Parameter | Type | Required | Description |
|---|---|---|---|
| `vesselId` | string | Yes | Vessel ID (prefix `vsl_`) |

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

### list_vessel_dependencies

List one page of a vessel's outdated or vulnerable dependency rows from its last health evaluation, cut in the database
and ordered by ecosystem, package name, then project path. Same rows as `GET /api/v1/vessels/{id}/health/dependencies`.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "vesselId": { "type": "string", "description": "Vessel ID (vsl_ prefix)" },
    "pageNumber": { "type": "integer", "description": "Page number (default 1)" },
    "pageSize": { "type": "integer", "description": "Page size, 1 to 1000 (default 10)" }
  },
  "required": ["vesselId"]
}
```

**Response:** the standard page shape.

```json
{
  "PageNumber": 1,
  "PageSize": 10,
  "TotalPages": 3,
  "TotalRecords": 30,
  "Objects": [ { "Ecosystem": "NuGet", "PackageName": "Newtonsoft.Json", "CurrentVersion": "12.0.1", "LatestVersion": "13.0.4", "Drift": "Major", "IsVulnerable": true, "Severity": "High", "...": "..." } ]
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
    "isEvaluation": { "type": "boolean", "description": "True for evaluation work: the mission's stage outcomes never count as production data. A mission added to an evaluation voyage is flagged regardless. Default false." },
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
The returned mission carries `isEvaluation`: true when the argument was set or `voyageId` names an evaluation voyage.

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

At most 1000 ids; more returns `ErrorCode` `InvalidArgument` with `Code` `CollectionLimitExceeded` and deletes nothing, so `Skipped` holds at most 1000 entries.

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

### approve_mission_review

Approve a mission waiting at a pipeline review gate (status `Review`). A non-final stage continues to the next stage with this stage's branch, diff, and output; a final stage continues to landing. Requires a person's credential: captain sessions and the unauthenticated loopback identity get `Forbidden`, so a captain can never pass its own gate (also refused inside Ask Armada conversations).

| Parameter | Type | Required | Description |
|---|---|---|---|
| `missionId` | string | Yes | Mission ID (`msn_`) |
| `comment` | string | No | Reviewer comment |
| `conditional` | boolean | No | Approve with conditions: the comment is added as guidance for the following stages |

**Response:** the mission. `Conflict` when it is not in `Review`.

### deny_mission_review

Deny a mission waiting at a review gate. `RetryStage` runs the stage again on the same branch with the comment added to its instructions; `FailPipeline` fails the mission and cancels the following stages. Same caller rules as `approve_mission_review`.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `missionId` | string | Yes | Mission ID (`msn_`) |
| `comment` | string | No | What to change |
| `action` | string | No | `RetryStage` or `FailPipeline`; defaults to the stage's configured action |

**Response:** the mission.

### transition_mission_status

Transition a mission to a new status with validation. The rules are `MissionStateMachine`'s, shared with
`PUT /api/v1/missions/{id}/status` and the WebSocket `transition_mission_status` command; the tool description lists them
generated from the same source.

**Valid transitions:**

| From | To |
|---|---|
| Pending | Assigned, Blocked, Cancelled |
| Blocked | Pending, Cancelled |
| Assigned | InProgress, Cancelled |
| InProgress | WorkProduced, Testing, Review, Complete, Failed, Cancelled |
| WorkProduced | PullRequestOpen, Complete, LandingFailed, Cancelled |
| PullRequestOpen | Complete, LandingFailed, Cancelled |
| Testing | Review, InProgress, Complete, Failed |
| Review | Complete, InProgress, Failed |
| LandingFailed | WorkProduced, Complete, Failed, Cancelled |

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

Get the git diff of changes made by a captain for a mission. Returns a saved diff when available; otherwise reads the live worktree on its owning Admiral or Harbor host. An offline or unsupported Harbor returns an actionable Unavailable error rather than claiming its worktree was reclaimed.

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

Named CLI models are validated on the owner's eligible Harbor under normal launch routing, with a scratch directory and a 30-second process limit. Harbor-required deployments do not need that CLI on the Admiral host. Unavailable execution hosts return `RuntimeUnavailable`; a CLI-rejected model returns `ModelRejected`. Omitting the model retains the runtime default. Mux captain validation performs real backend inference with a bounded 120-second process deadline, separate from ten-second endpoint list/show metadata reads. Timeouts remain failures without automatic retries.

Register a new captain (AI agent).

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "name": { "type": "string", "description": "Captain display name" },
    "runtime": { "type": "string", "description": "Agent runtime: ClaudeCode, Codex, Gemini, Cursor, Mux, OpenCode, Pi, ApiEndpoint, Custom" },
    "model": { "type": "string", "description": "Optional model override for this captain. When omitted, the runtime chooses automatically" },
    "modelEndpointId": { "type": "string", "description": "Managed inference endpoint ID (mep_ prefix) for an ApiEndpoint captain; persona priorities with runtime ApiEndpoint and this endpoint select it" },
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
| `runtime` | string | No | Agent runtime: `ClaudeCode`, `Codex`, `Gemini`, `Cursor`, `Mux`, `OpenCode`, `Pi`, or `Custom` |
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
| `autoApprove` | boolean | No | Whether the CLI captain runs with its auto-approve or permission-bypass flag (default `true`). `false` runs it without auto-approve where the runtime supports it (Claude Code acceptEdits, Codex workspace-write sandbox, Gemini auto_edit, Cursor without --force, OpenCode without --auto, pi --exclude-tools bash,powershell, Mux deny) |

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

Describe the tool sources (`Servers`, at most 256) and tool counts of a specific captain, with one page of its named
tools (`Tools`: an `EnumerationResult` ordered by source kind, source, and name). The runtime is probed once and the
snapshot is reused for 60 seconds so further pages do not probe again; `refresh: true` probes again. Same contract as
`GET /api/v1/captains/{id}/tools`.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "captainId": { "type": "string", "description": "Captain ID (cpt_ prefix)" },
    "pageNumber": { "type": "integer", "description": "One-based page of tools (default 1)" },
    "pageSize": { "type": "integer", "description": "Tools per page, 1 to 1000 (default 10)" },
    "sourceKind": { "type": "string", "description": "Only tools of this source kind: McpServer, RuntimeBuiltIn, or Internal" },
    "refresh": { "type": "boolean", "description": "true to probe the runtime again instead of reusing the recent snapshot" }
  },
  "required": ["captainId"]
}
```

**Response:** [CaptainToolAccessResult](#captaintoolaccessresult) object, `{ "Error": "Captain not found", "ErrorCode": "NotFound" }`, `InvalidArgument` for an unknown `sourceKind`, or `InvalidArgument` with `Code` `CollectionLimitExceeded` when the runtime reports more than 256 tool sources.

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
    "runtime": { "type": "string", "description": "New agent runtime: ClaudeCode, Codex, Gemini, Cursor, Mux, OpenCode, Pi, ApiEndpoint, Custom" },
    "model": { "type": "string", "description": "New optional model override for this captain" },
    "modelEndpointId": { "type": "string", "description": "Managed inference endpoint ID (mep_ prefix) for an ApiEndpoint captain; empty string clears it, omitting it keeps it" },
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
| `runtime` | string | No | New agent runtime: `ClaudeCode`, `Codex`, `Gemini`, `Cursor`, `Mux`, `OpenCode`, `Pi`, or `Custom` |
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

Permanently delete multiple captains from the database by ID. Captains that are Working or have active missions are skipped. Returns a summary of deleted and skipped entries. `ids` may hold at most 1000 entries; more is `InvalidArgument` with `Code` `CollectionLimitExceeded` and nothing is deleted. **This cannot be undone.**

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

Returns `{ "Error": "ids is required and must not be empty", "ErrorCode": "InvalidArgument" }` if no IDs are provided. `ids` holds at most 1000 entries; a longer list returns `ErrorCode` `InvalidArgument` with `Code` `CollectionLimitExceeded` and deletes nothing. `Skipped` is bounded by the request.

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
    "scope": { "type": "string", "enum": ["TenantWide", "UserSpecific"], "description": "Ownership: UserSpecific (only you) or TenantWide; a regular user can create only UserSpecific playbooks" },
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

### get_workflow_profile

Get a workflow profile by ID, including every command stage checks run and its environments. Requires an authenticated
caller; a regular user sees the tenant's tenant-wide profiles plus their own user-specific ones, and another tenant's
profile is `NotFound`.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "id": { "type": "string", "description": "Workflow profile ID (wfp_ prefix)" }
  },
  "required": ["id"]
}
```

**Example:** `{ "id": "wfp_abc123" }`

**Response:** [WorkflowProfile](#workflowprofile) object, or `{ "Error": "Workflow profile not found", "ErrorCode": "NotFound" }`.

---

### create_workflow_profile

Create a workflow profile in the caller's tenant (a global admin may pass `tenantId`). Requires TenantAdmin, as
`POST /api/v1/workflow-profiles` does. The definition is validated first (the same `WorkflowProfileService` validation
REST uses): at least one command is required, `Fleet` scope needs `fleetId`, `Vessel` scope needs `vesselId`, environment
names must be unique, and required inputs must name known environments. A `fleetId` or `vesselId` outside the caller's
tenant is refused as `InvalidArgument` "... not found". Setting `isDefault` clears the default flag on the other profiles
at the same scope. `languageHints`, `requiredInputs`, `expectedArtifacts` and `environments` hold at most 256
entries each; more is `InvalidArgument` with `Code` `CollectionLimitExceeded` (also on `update_workflow_profile` and
`validate_workflow_profile`).

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "tenantId": { "type": "string", "description": "Tenant to create the profile in (global admins only; ignored for everyone else)" },
    "name": { "type": "string", "description": "Human-readable profile name" },
    "description": { "type": "string", "description": "Optional description" },
    "scope": { "type": "string", "enum": ["Global", "Fleet", "Vessel"], "description": "Application scope: Global (default), Fleet (needs fleetId), or Vessel (needs vesselId)" },
    "fleetId": { "type": "string", "description": "Fleet ID for a Fleet-scoped profile (flt_ prefix)" },
    "vesselId": { "type": "string", "description": "Vessel ID for a Vessel-scoped profile (vsl_ prefix)" },
    "isDefault": { "type": "boolean", "description": "Whether this is the default profile at its scope" },
    "active": { "type": "boolean", "description": "Whether the profile is active (default true)" },
    "ownershipScope": { "type": "string", "enum": ["UserSpecific", "TenantWide"], "description": "Ownership: UserSpecific (only you) or TenantWide (default for administrators); only tenant and global admins may change it" },
    "languageHints": { "type": "array", "items": { "type": "string" } },
    "lintCommand": { "type": "string" },
    "buildCommand": { "type": "string" },
    "unitTestCommand": { "type": "string" },
    "integrationTestCommand": { "type": "string" },
    "e2eTestCommand": { "type": "string" },
    "migrationCommand": { "type": "string" },
    "securityScanCommand": { "type": "string" },
    "performanceCommand": { "type": "string" },
    "packageCommand": { "type": "string" },
    "deploymentVerificationCommand": { "type": "string" },
    "rollbackVerificationCommand": { "type": "string" },
    "publishArtifactCommand": { "type": "string" },
    "releaseVersioningCommand": { "type": "string" },
    "changelogGenerationCommand": { "type": "string" },
    "requiredInputs": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "provider": { "type": "string", "enum": ["EnvironmentVariable", "FilePath", "DirectoryPath", "AwsSecretsManager", "AzureKeyVaultSecret", "HashiCorpVault", "OnePassword"] },
          "key": { "type": "string" },
          "environmentName": { "type": "string" },
          "description": { "type": "string" }
        },
        "required": ["key"]
      }
    },
    "expectedArtifacts": { "type": "array", "items": { "type": "string" } },
    "environments": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "environmentName": { "type": "string" },
          "deployCommand": { "type": "string" },
          "rollbackCommand": { "type": "string" },
          "smokeTestCommand": { "type": "string" },
          "healthCheckCommand": { "type": "string" },
          "deploymentVerificationCommand": { "type": "string" },
          "rollbackVerificationCommand": { "type": "string" }
        },
        "required": ["environmentName"]
      }
    }
  },
  "required": ["name"]
}
```

**Example:**

```json
{
  "name": "dotnet service",
  "scope": "Vessel",
  "vesselId": "vsl_abc123",
  "buildCommand": "dotnet build",
  "unitTestCommand": "dotnet test",
  "environments": [{ "environmentName": "staging", "deployCommand": "./deploy.sh staging", "healthCheckCommand": "curl -f https://staging/health" }]
}
```

**Response:** the created [WorkflowProfile](#workflowprofile), or `{ "Error": "At least one build, test, release, deploy, verification, migration, security, or performance command is required.", "ErrorCode": "InvalidArgument" }`.

---

### update_workflow_profile

Partially update a workflow profile. Only the fields you pass change; omitted fields keep their stored values. Pass an
empty string to clear a command, `fleetId`, or `vesselId`. Lists (`languageHints`, `requiredInputs`, `expectedArtifacts`,
`environments`) replace the stored list when passed. The merged profile is validated before it is saved, so a rejected
update changes nothing; an edited profile is no longer replaced by Armada's generated defaults. Requires TenantAdmin, as
`PUT /api/v1/workflow-profiles/{id}` does; a profile in another tenant is `NotFound`, and a profile the caller may not
edit is `Forbidden`.

**Input Schema:** the [create_workflow_profile](#create_workflow_profile) properties without `tenantId`, plus
`"id": { "type": "string", "description": "Workflow profile ID (wfp_ prefix)" }`; only `id` is required.

**Example:** change only the unit test command:

```json
{ "id": "wfp_abc123", "unitTestCommand": "dotnet test --no-build" }
```

**Response:** the updated [WorkflowProfile](#workflowprofile).

---

### delete_workflow_profile

Delete a workflow profile by ID. Requires TenantAdmin. This cannot be undone.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "id": { "type": "string", "description": "Workflow profile ID (wfp_ prefix)" }
  },
  "required": ["id"]
}
```

**Response:**

```json
{ "Status": "deleted", "WorkflowProfileId": "wfp_..." }
```

---

### delete_workflow_profiles

Permanently delete several workflow profiles by ID (the MCP form of `POST /api/v1/workflow-profiles/delete/multiple`).
Requires TenantAdmin. Ids that are empty, missing, in another tenant, or not editable by the caller are skipped with a
reason instead of failing the batch. More than 1000 ids is `InvalidArgument` with `Code` `CollectionLimitExceeded` and deletes
nothing, so `Skipped` holds at most 1000 entries.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "ids": { "type": "array", "items": { "type": "string" }, "description": "List of workflow profile IDs to delete (wfp_ prefix), at most 1000" }
  },
  "required": ["ids"]
}
```

**Example:** `{ "ids": ["wfp_abc123", "wfp_def456"] }`

**Response:**

```json
{ "Status": "PartiallyDeleted", "Deleted": 1, "Skipped": [{ "Id": "wfp_def456", "Reason": "Not found" }] }
```

---

### validate_workflow_profile

Validate a workflow-profile definition without saving it. Requires TenantAdmin, as `POST /api/v1/workflow-profiles/validate`
does. Takes the same properties as [create_workflow_profile](#create_workflow_profile) (without `tenantId`); `name` is
required.

**Example:** `{ "name": "draft", "buildCommand": "make", "unitTestCommand": "make test" }`

**Response:** `WorkflowProfileValidationResult`:

```json
{
  "IsValid": true,
  "Errors": [],
  "Warnings": [],
  "AvailableCheckTypes": ["Build", "UnitTest"],
  "CommandPreviews": [{ "CheckType": "Build", "EnvironmentName": null, "Command": "make" }]
}
```

---

### resolve_workflow_profile

Resolve the workflow profile that applies to a vessel: the explicit `workflowProfileId` when given, otherwise the vessel's
assigned profile, then vessel, fleet, and global precedence (the same resolution stage checks use). Requires an
authenticated caller who can see the vessel.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "vesselId": { "type": "string", "description": "Vessel ID (vsl_ prefix)" },
    "workflowProfileId": { "type": "string", "description": "Optional explicit workflow profile override (wfp_ prefix)" }
  },
  "required": ["vesselId"]
}
```

**Example:** `{ "vesselId": "vsl_abc123" }`

**Response:** the resolved [WorkflowProfile](#workflowprofile), or `NotFound` with `Vessel not found` or `No workflow profile could be resolved for this vessel`.

---

### preview_workflow_profile

Preview the commands a vessel would run: resolves the profile as [resolve_workflow_profile](#resolve_workflow_profile)
does and returns the fully resolved command set. Same input schema and permissions as `resolve_workflow_profile`.

**Example:** `{ "vesselId": "vsl_abc123", "workflowProfileId": "wfp_abc123" }`

**Response:**

```json
{
  "ResolvedProfile": { "Id": "wfp_abc123", "Name": "dotnet service" },
  "ResolutionMode": "Explicit",
  "AvailableCheckTypes": ["Build", "UnitTest"],
  "CommandPreviews": [{ "CheckType": "UnitTest", "EnvironmentName": null, "Command": "dotnet test" }]
}
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

Automatic completion remains blocked after a confirmed Failed stage check until a later check clears it. A CouldNotRun or TimedOut check is never treated as passing: the work proceeds to landing unverified and reaches Complete only after a confirmed landing. Local landing without a diff snapshot or confirmed source branch retains WorkProduced and preserves the branch. Resume-in-flight missions are excluded from orphan completion; an unconfirmed orphan exit holds work in WorkProduced for recovery.

Add a branch to the merge queue for testing and merging. A Complete mission is refused as already landed only when it records a commit or its branch is confirmed contained in the target. Complete without landing evidence returns a Conflict describing the status mismatch; unavailable repositories do not establish landing.

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
    "entryIds": { "type": "array", "items": { "type": "string" }, "description": "List of merge entry IDs to purge (mrg_ prefix), at most 1000" }
  },
  "required": ["entryIds"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `entryIds` | string[] | Yes | List of merge entry IDs to purge (prefix `mrg_`), at most 1000; more is `InvalidArgument` with `Code` `CollectionLimitExceeded` and nothing is purged |

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
> `set_harbor_enabled`, `list_harbor_jobs`, `stop_harbor_job`) belong to Harbor split mode, which is experimental for 1.0 and excluded from the compatibility
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
for how the numbers are recorded. Visibility and token scoping are the same as over REST. Every list is bounded: per-bucket
series at most 60 entries, `LaunchSpeed` at most 16 (the 15 busiest runtimes plus one `IsOther` aggregate), token
`Series` at most 20 (the top 19 runtime and model pairs plus one `IsOther` aggregate), and `Link.Segments` at most 1000
(coalesced to bucket resolution with `SegmentsCoalesced` above that).

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

### list_harbor_jobs

One page of the captain launches recorded on a Harbor, newest first. Use `openOnly` to find an agent left running after its mission was cancelled.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "harborId": { "type": "string", "description": "Harbor ID (hbr_ prefix)" },
    "openOnly": { "type": "boolean", "description": "Only jobs that have not ended (default false: jobs launched in the last 7 days)" },
    "pageNumber": { "type": "integer", "description": "Page number (1-based, default 1)" },
    "pageSize": { "type": "integer", "description": "Results per page (1 to 1000, default 10)" }
  },
  "required": ["harborId"]
}
```

**Response:** `EnumerationResult<HarborJobRecord>`, or `ErrorCode` `NotFound` when the Harbor does not exist or is not visible.

---

### stop_harbor_job

Ask a connected Harbor to stop one job by ID (from `list_harbor_jobs`). Requires TenantAdmin.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "harborId": { "type": "string", "description": "Harbor ID (hbr_ prefix)" },
    "jobId": { "type": "string", "description": "Job ID as listed by list_harbor_jobs" }
  },
  "required": ["harborId", "jobId"]
}
```

**Response:** `HarborJobStopResult` with `Status` `Requested` (sent; the Harbor reports the exit) or `AlreadyEnded`; `ErrorCode` `NotFound` for an unknown job and `Conflict` when the Harbor is not connected.

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
| `reorder_objectives` | TenantAdmin | `items`* (`[{ objectiveId*, rank* }]`, at most 1000) | Apply explicit rank updates. Returns `ObjectiveReorderResult { updated, items: [{ objectiveId, rank }] }` (not the objectives) |
| `reorder_backlog_items` | TenantAdmin | Same as `reorder_objectives` | Preferred user-facing alias |
| `delete_objective` | TenantAdmin | `objectiveId`* | Removes the normalized row and snapshot-backed current-state chain |
| `delete_backlog_item` | TenantAdmin | `objectiveId`* | Preferred user-facing alias |

Refinement tools:

| Tool | Permission | Arguments | Notes |
|---|---|---|---|
| `list_backlog_refinement_sessions` | Authenticated | `objectiveId`*, `pageNumber` (default 1), `pageSize` (default 25, max 1000) | One page of session metadata for one backlog item, paged in the database, most recently updated first |
| `create_backlog_refinement_session` | TenantAdmin | `objectiveId`*, `captainId`*, `fleetId`, `vesselId`, `title`, `initialMessage` | Start captain-backed refinement; `vesselId` is optional |
| `get_backlog_refinement_session` | Authenticated | `sessionId`* (`ors_`) | Returns session, `messageCount`, captain, vessel, and linked backlog item (no transcript) |
| `list_backlog_refinement_session_messages` | Authenticated | `sessionId`* (`ors_`), `pageNumber`, `pageSize` (1 to 1000, default 10), `order` | One page of the transcript, newest first by default; `totalRecords` is the transcript length |
| `send_backlog_refinement_message` | TenantAdmin | `sessionId`*, `content`* | Append one user message; launches the next refinement turn |
| `summarize_backlog_refinement_session` | TenantAdmin | `sessionId`*, `messageId` | Create/select a structured summary (latest assistant turn when `messageId` is omitted) |
| `apply_backlog_refinement_summary` | TenantAdmin | `sessionId`*, `messageId`, `markMessageSelected` (default `true`), `promoteBacklogState` (default `true`), `endSession` (default `true`) | Apply the summary back to the backlog item. Ends the session and releases its captain unless `endSession` is `false`. Returns `{ Summary, Objective, Session }` |
| `stop_backlog_refinement_session` | TenantAdmin | `sessionId`* | Stop one active refinement session; releases the selected captain |

Planning handoff tools:

| Tool | Permission | Arguments | Notes |
|---|---|---|---|
| `create_backlog_planning_session` | TenantAdmin | `objectiveId`*, `captainId`*, `vesselId`*, `fleetId`, `pipelineId`, `title`, `selectedPlaybooks` | Start a repository-aware planning session from one backlog item |
| `get_backlog_planning_session` | Authenticated | `sessionId`* (`psn_`) | Returns `PlanningSessionDetail`: session, captain, vessel, `messageCount`, and `linkedObjectiveCount` (no transcript or linked items; list the items with `list_backlog` filtered by `planningSessionId`) |
| `list_backlog_planning_session_messages` | Authenticated | `sessionId`* (`psn_`), `pageNumber`, `pageSize` (1 to 1000, default 10), `order` | One page of the transcript, newest first by default |
| `dispatch_backlog_planning_session` | TenantAdmin | `sessionId`*, `messageId`, `title`, `description` | Dispatch a voyage from the planning session and link it to every linked backlog item. Returns `PlanningSessionDispatchResult { voyage, linkedObjectiveCount }` |

Shared input notes:

- `create_backlog_item` mirrors `create_objective` but uses backlog terminology in the tool name and descriptions
- list fields are bounded: `tags`, `acceptanceCriteria`, `nonGoals`, `rolloutConstraints`, `evidenceLinks`, and
  `suggestedPlaybooks` at 256 entries, every id list at 1000; a larger list is `InvalidArgument` with `code`
  `CollectionLimitExceeded` naming the field, and nothing is stored
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
    "type": { "type": "string", "description": "Check type such as Build, UnitTest, AbbreviatedUnitTest, IntegrationTest, Deploy, SmokeTest, HealthCheck, or ReleaseVersioning" },
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
- The check runs in the vessel's working directory on the Admiral host, or, when that does not exist (split mode), in the checkout of a connected Harbor that can serve the vessel. When neither exists the tool returns an `Unavailable` error with `code` `VesselCheckoutUnavailable.NoHarborConnected` or `VesselCheckoutUnavailable.NoHarborCheckout` and a message that says what to set (for example "No connected Harbor has a checkout of DocConverter; in Harbor > Settings > Repositories set its folder or add a root folder that contains it ..."). See [HARBOR.md](HARBOR.md#checkouts-outside-missions).

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

Run post-deploy verification for an existing deployment. `CheckRunIds` holds at most 1000 ids; a verification whose runs could not all be linked fails with `InvalidArgument` (`Code` `CollectionLimitExceeded`) before any check runs.

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

Create a first-class release record from linked voyages, missions, and structured checks, with derived version, notes, and artifacts when available. Each link list holds at most 1000 ids, and the voyages' missions plus the explicit missions (and the derived artifacts) at most 1000 in all; more fails with `InvalidArgument` (`Code` `CollectionLimitExceeded`).

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

Start a guided runbook execution with optional parameter overrides and deployment or incident context. `parameterValues` (with the runbook's defaults) holds at most 256 entries; more fails with `InvalidArgument` (`Code` `CollectionLimitExceeded`).

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
    "pageSize": { "type": "integer", "description": "Results per page (default 10, min 1, max 1000; out-of-range values are normalized)" }
  }
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `category` | string | No | Optional category filter |
| `pageNumber` | integer | No | 1-based page number (default 1) |
| `pageSize` | integer | No | Results per page (default 10, min 1, max 1000; out-of-range values are normalized and the result's `pageSize` reports the size used) |

**Response:** a paginated envelope `{ success, pageNumber, pageSize, totalPages, totalRecords, objects }`, where each item in `objects` carries `name`, `category`, `description`, `active`, `isBuiltIn`, `contentLength`, and `lastUpdateUtc`. The template body is intentionally omitted -- retrieve it per-template via [get_prompt_template](#get_prompt_template). Rows are ordered by category, then name, then id, and the page is cut in the database with the caller's visibility (tenant-wide templates, the caller's own, and shared templates stored without a tenant) applied before counting, so `totalRecords` is exact.

---

### create_prompt_template

Create a new prompt template.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "scope": { "type": "string", "enum": ["TenantWide", "UserSpecific"], "description": "Ownership: UserSpecific (only you) or TenantWide; a regular user can create only UserSpecific templates" },
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
    "defaultCaptainId": { "type": "string", "description": "Optional default (preferred) captain id (cpt_ prefix) for this persona" },
    "scope": { "type": "string", "enum": ["TenantWide", "UserSpecific"], "description": "Ownership: UserSpecific (only you) or TenantWide" },
    "captainPriorities": { "type": "array", "items": { "type": "object", "properties": { "runtime": { "type": "string", "enum": ["ClaudeCode", "Codex", "Gemini", "Cursor", "Mux", "OpenCode", "Pi", "ApiEndpoint", "Custom"] }, "model": { "type": "string", "minLength": 1, "maxLength": 1024 }, "endpoint": { "type": "string", "minLength": 1, "maxLength": 1024, "description": "Mux endpoint name (runtime Mux) or exact managed inference endpoint ID (runtime ApiEndpoint; matches captain ModelEndpointId)" }, "minimumTier": { "type": "string", "enum": ["Economy", "Standard", "Premium"] } } } },
    "priorityAvailability": { "type": "string", "enum": ["FallThrough", "WaitForFirstPriority"] },
    "priorityExhaustion": { "type": "string", "enum": ["Wait", "UseEligibleCaptain"] }
  },
  "required": ["name", "promptTemplateName"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `name` | string | Yes | Persona name |
| `description` | string | No | Persona description |
| `promptTemplateName` | string | Yes | Name of the prompt template to use for this persona |
| `defaultCaptainId` | string | No | Optional default (preferred) captain id (prefix `cpt_`); must be visible to the caller |
| `scope` | string | No | `UserSpecific` or `TenantWide`; a regular user always gets `UserSpecific`, an administrator defaults to `TenantWide` |

The persona is owned by the caller and is never built in; validation matches `POST /api/v1/personas`.

**Response:** The newly created [Persona](#persona) object. A taken name returns `{ "Error": "A persona named '...' already exists.", "ErrorCode": "Conflict", "Code": "DuplicateEntity" }`; a default captain that is not found or an invalid routing configuration returns `ErrorCode` `InvalidArgument`.

---


Optional `captainPriorities` supplies ordered matching classes; at least one field is required per entry. All fields in an entry must match. `priorityAvailability` defaults to `FallThrough`; `WaitForFirstPriority` waits strictly for the first entry and ignores `priorityExhaustion`. Exhaustion defaults to `Wait`; `UseEligibleCaptain` permits ordinary routing after all entries are exhausted. A nonempty hierarchy supersedes defaultCaptainId, while explicit dispatch captain assignments take precedence. Updates preserve omitted/null routing fields; an empty array clears priorities. Invalid settings return InvalidArgument without changing stored data. Persona results include the hierarchy and both policies. See [CAPTAIN_ROUTING.md](CAPTAIN_ROUTING.md).

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
    "defaultCaptainId": { "type": "string", "description": "Default (preferred) captain id (cpt_ prefix); an empty string clears it, and omitting it keeps it" },
    "scope": { "type": "string", "enum": ["TenantWide", "UserSpecific"], "description": "Ownership: UserSpecific (only you) or TenantWide" },
    "captainPriorities": { "type": "array", "items": { "type": "object", "properties": { "runtime": { "type": "string", "enum": ["ClaudeCode", "Codex", "Gemini", "Cursor", "Mux", "OpenCode", "Pi", "ApiEndpoint", "Custom"] }, "model": { "type": "string", "minLength": 1, "maxLength": 1024 }, "endpoint": { "type": "string", "minLength": 1, "maxLength": 1024, "description": "Mux endpoint name (runtime Mux) or exact managed inference endpoint ID (runtime ApiEndpoint; matches captain ModelEndpointId)" }, "minimumTier": { "type": "string", "enum": ["Economy", "Standard", "Premium"] } } } },
    "priorityAvailability": { "type": "string", "enum": ["FallThrough", "WaitForFirstPriority"] },
    "priorityExhaustion": { "type": "string", "enum": ["Wait", "UseEligibleCaptain"] }
  },
  "required": ["name"]
}
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `name` | string | Yes | Persona name |
| `description` | string | No | New persona description |
| `promptTemplateName` | string | No | New prompt template name |
| `defaultCaptainId` | string | No | Default (preferred) captain id (prefix `cpt_`); omitting it keeps it, an empty string clears it (the schema accepts only strings, so send `""` where REST would send `null`) |
| `scope` | string | No | `UserSpecific` or `TenantWide`; changed only for an administrator |

Same rules as `PUT /api/v1/personas/{name}`.

**Response:** Updated [Persona](#persona) object, or `{ "Error": "Persona not found: <name>", "ErrorCode": "NotFound" }`; `Forbidden` when the caller may not edit it; `InvalidArgument` for a default captain that is not found or invalid routing.

---


Optional `captainPriorities` supplies ordered matching classes; at least one field is required per entry. All fields in an entry must match. `priorityAvailability` defaults to `FallThrough`; `WaitForFirstPriority` waits strictly for the first entry and ignores `priorityExhaustion`. Exhaustion defaults to `Wait`; `UseEligibleCaptain` permits ordinary routing after all entries are exhausted. A nonempty hierarchy supersedes defaultCaptainId, while explicit dispatch captain assignments take precedence. Updates preserve omitted/null routing fields; an empty array clears priorities. Invalid settings return InvalidArgument without changing stored data. Persona results include the hierarchy and both policies. See [CAPTAIN_ROUTING.md](CAPTAIN_ROUTING.md).

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
Returns `{ "Error": "Built-in personas cannot be deleted", "ErrorCode": "InvalidArgument" }` if the persona is built-in (REST answers `400`), and `Forbidden` when the caller may not delete it.

---

### create_pipeline

Create a custom pipeline with stages. `stages` holds at most 256 entries (also on `update_pipeline`); more is `InvalidArgument` with
`Code` `CollectionLimitExceeded` and stores nothing.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "scope": { "type": "string", "enum": ["TenantWide", "UserSpecific"], "description": "Ownership: UserSpecific (only you) or TenantWide; a regular user can create only UserSpecific pipelines" },
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

**Ollama Cloud and xAI endpoints**

`OllamaCloud` and `XAI` support both `Inference` and `Embedding` through PolyPrompt 3.5.0. Both require an API key and accept only `ProviderDefault` or `Bearer` authentication. Credentials are write-only; omit the API key on update to preserve it.

Ollama Cloud uses the fixed URL `https://ollama.com:443` (omit the base URL or supply that address). Choose a hosted model; embedding availability depends on the cloud catalog. xAI defaults to `https://api.x.ai` when the base URL is omitted, uses the native Responses API for inference, and requires an explicit accessible embedding model for embeddings.

To use either for a captain, create an `Inference` endpoint, set the captain runtime to `ApiEndpoint`, and set `ModelEndpointId` to the endpoint ID. The chosen model must support tool calling for agent work.

CLI example: `armada captain add grok-worker --runtime api --model-endpoint <endpoint-id>`.

### create_model_endpoint

Create a model endpoint. Supply `apiKey` to store a provider key; it is write-only and is never returned on reads. Optional `inferenceOptions` configures enforced tool restrictions, generation limits and Harbor requirements; see [API captain execution](CAPTAIN_ROUTING.md#api-endpoint-captains-in-pipelines).

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "name": { "type": "string", "description": "Display name" },
    "baseUrl": { "type": "string", "description": "Provider API base URL" },
    "kind": { "type": "string", "description": "Embedding or Inference (default Inference)" },
    "provider": { "type": "string", "description": "Ollama, OpenAI, OpenAICompatible, Anthropic, Gemini, VoyageAI, AzureOpenAI, VertexAI, Bedrock, OllamaCloud, or XAI" },
    "model": { "type": "string", "description": "Model name to target" },
    "apiKey": { "type": "string", "description": "Provider API key. Write-only: accepted here, never returned on reads." },
    "inferenceOptions": { "type": "object", "description": "toolsEnabled, readOnlyTools, mcpToolsEnabled, allowedTools, maxIterations, maxTokens, temperature, topP, requireHarbor. See API captain execution for defaults and limits." },
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
| `provider` | string | No | `Ollama`, `OpenAI` (default), `OpenAICompatible`, `Anthropic`, `Gemini`, `VoyageAI`, `AzureOpenAI`, `VertexAI`, `Bedrock`, `OllamaCloud`, or `XAI` |
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

**Authentication** (accepted on create and update): `authType` is `ProviderDefault` (default; the provider's own scheme), `None`, `Bearer` (`Authorization: Bearer <apiKey>`), `Basic` (`authUsername` and `apiKey`), `Header` (`<authHeaderName>: <apiKey>`), or `QueryParameter` (`?<authQueryParameter>=<apiKey>`); `VertexAI` and `Bedrock` accept only `ProviderDefault`. `headers` is a list of extra `{ name, value }` headers sent with every request; values are write-only, and on update an entry without `value` keeps the stored value of the header with the same name. On `update_model_endpoint`, `apiKey` now replaces the stored secret (before, it was ignored).

**Provider-specific fields** (accepted on create and update): `AzureOpenAI` uses `baseUrl` (resource endpoint), `model` (deployment name), `apiKey`, and optional `apiVersion`. `VertexAI` requires `project` and `region`, with the service-account JSON supplied write-only as `apiKey`; `baseUrl` is an optional override. `Bedrock` requires `region` and `accessKeyId`, with the AWS secret access key supplied write-only as `apiKey`; `model` is the Bedrock model id and `baseUrl` is an optional override.

---

### duplicate_model_endpoint

Create a new endpoint using an edited configuration while inheriting stored credentials server-side. Requires edit permission on the source. Read-only access to a shared endpoint is insufficient. No provider request is performed; the source is unchanged and the copy receives fresh identity, ownership and health state. Credentials remain write-only.

```json
{
  "endpointId": "mep_source",
  "configuration": {
    "name": "Second cloud model",
    "kind": "Inference",
    "provider": "OllamaCloud",
    "baseUrl": "https://ollama.com:443",
    "model": "your-second-model",
    "authType": "Bearer"
  }
}
```

`endpointId` and `configuration` are required. Configuration accepts the create endpoint fields. Omit `apiKey` to inherit, send a string to replace or explicit null to clear. Omitted headers copy all; provided header lists replace the list and null values inherit by name. Omitted inference settings preserve the source policy. Invalid configuration, forbidden access and invisible sources produce the standard MCP errors. See [REST duplication semantics](REST_API.md#duplicate-an-endpoint-without-re-entering-credentials).

### update_model_endpoint

Update an existing model endpoint. Omit `apiKey` to keep the stored key; send `apiKey` to replace it. Omit `inferenceOptions` to preserve its entire policy; send an object to replace it. Its omitted members take defaults. See [API captain execution](CAPTAIN_ROUTING.md#api-endpoint-captains-in-pipelines) for all fields and limits.

**Input Schema:**

```json
{
  "type": "object",
  "properties": {
    "endpointId": { "type": "string", "description": "Model endpoint ID (mep_ prefix)" },
    "name": { "type": "string", "description": "New display name" },
    "kind": { "type": "string", "description": "Embedding or Inference" },
    "provider": { "type": "string", "description": "Ollama, OpenAI, OpenAICompatible, Anthropic, Gemini, VoyageAI, AzureOpenAI, VertexAI, Bedrock, OllamaCloud, or XAI" },
    "baseUrl": { "type": "string", "description": "New provider API base URL" },
    "model": { "type": "string", "description": "New model name to target" },
    "apiKey": { "type": "string", "description": "New provider API key. Omit to keep the stored key." },
    "inferenceOptions": { "type": "object", "description": "toolsEnabled, readOnlyTools, mcpToolsEnabled, allowedTools, maxIterations, maxTokens, temperature, topP, requireHarbor. See API captain execution for defaults and limits." },
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
| `provider` | string | No | `Ollama`, `OpenAI` (default), `OpenAICompatible`, `Anthropic`, `Gemini`, `VoyageAI`, `AzureOpenAI`, `VertexAI`, `Bedrock`, `OllamaCloud`, or `XAI` |
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
- `dispatch` accepts `captainAssignments`, an array of `{ persona, captainId, fallbackTier, strictCaptain }` that binds each pipeline step (persona) to a preferred captain and a fallback tier (`Economy` | `Standard` | `Premium`). Entries in `missions` may also carry a per-mission `requestedCaptainId` and `tier`.
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
| `ActiveVoyages` | int | Number of active (Open or InProgress) voyages; page them with `list_voyage_progress` |
| `InProgressVoyages` | int | InProgress voyages |
| `OpenVoyages` | int | Open voyages |
| `MissionsByStatus` | object | Map of status string to count (e.g., `{"Pending": 3}`) |
| `RecentSignals` | array | The 10 most recent [Signal](#signal) objects |
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
| `MissionCountsByStatus` | object | Count per mission status (only statuses with missions) |
| `VesselId` | string | Vessel of the earliest mission, or null |
| `VesselCount` | int | Distinct vessels the missions target |

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
| `WorkerEnvelope` | object \| null | Default Worker eligibility envelope (`maxFiles`, `maxSteps`, `minServedContextTokens`, `requiredTools`); null means no envelope and routing is unchanged. Returned by `get_vessel`. |
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
| `WaitForVoyageWorkers` | bool | When true, a Worker waits for other implementation pipelines, including reviews and checks, to settle (Complete, WorkProduced, Failed, Cancelled, or LandingFailed). Its own downstream stages and the Architect are excluded. Reworks inherit the barrier set from the structured architect plan (`waitForOtherMissions`). |
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
| `Tier` | string \| null | CaptainTierEnum value. Optional capability/cost tier used by dispatch to route missions of a given complexity. Null means the tier is auto-classified from the model name at selection time (8B parameters or fewer is Economy; unknown is Standard). A Claude Code, Codex or Gemini captain with no model and no tier ranks as Standard but can take Premium work. |
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

#### WorkflowProfile

| Field | Type | Description |
|---|---|---|
| `Id` | string | Workflow profile ID (prefix `wfp_`) |
| `TenantId` | string \| null | Owning tenant |
| `UserId` | string \| null | Owning user |
| `OwnershipScope` | string | `TenantWide` (visible to the tenant, editable by tenant/global admins) or `UserSpecific` (owned by `UserId`) |
| `Name` | string | Profile name |
| `Description` | string \| null | Description |
| `Scope` | string | Application scope: `Global`, `Fleet`, or `Vessel` |
| `FleetId` / `VesselId` | string \| null | Target for `Fleet` / `Vessel` scope |
| `IsDefault` | bool | Default profile at its scope (at most one) |
| `Active` | bool | Whether the profile is used for resolution |
| `SourceTemplate` | string \| null | Set when Armada generated the profile; cleared once a person edits it |
| `LanguageHints` | string[] | Language or framework hints |
| `LintCommand`, `BuildCommand`, `UnitTestCommand`, `IntegrationTestCommand`, `E2ETestCommand`, `MigrationCommand`, `SecurityScanCommand`, `PerformanceCommand`, `PackageCommand`, `DeploymentVerificationCommand`, `RollbackVerificationCommand`, `PublishArtifactCommand`, `ReleaseVersioningCommand`, `ChangelogGenerationCommand` | string \| null | Commands stage checks and delivery helpers run |
| `RequiredInputs` | object[] | `{ Provider, Key, EnvironmentName, Description }` secret or configuration references |
| `ExpectedArtifacts` | string[] | Expected build or release artifacts |
| `Environments` | object[] | `{ EnvironmentName, DeployCommand, RollbackCommand, SmokeTestCommand, HealthCheckCommand, DeploymentVerificationCommand, RollbackVerificationCommand }` |
| `CreatedUtc` / `LastUpdateUtc` | string | ISO 8601 timestamps |

#### SelectedPlaybook

| Field | Type | Description |
|---|---|---|
| `PlaybookId` | string | Selected playbook ID |
| `DeliveryMode` | [PlaybookDeliveryModeEnum](#playbookdeliverymodeenum) | How the playbook is delivered to the model; `InstructionWithReference` when omitted |

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
| `Provider` | string | `Ollama`, `OpenAI`, `OpenAICompatible`, `Anthropic`, `Gemini`, `VoyageAI`, `AzureOpenAI`, `VertexAI`, `Bedrock`, `OllamaCloud`, or `XAI` |
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
| `AuthType` | string | `ProviderDefault`, `None`, `Bearer`, `Basic`, `Header`, or `QueryParameter` |
| `AuthUsername` | string \| null | User name for `Basic` |
| `AuthHeaderName` | string \| null | Header that carries the secret for `Header` |
| `AuthQueryParameter` | string \| null | Query parameter that carries the secret for `QueryParameter` |
| `Headers` | array | Extra headers, each `{ name, value: null, hasValue }`; values are never returned |
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
| `Note` | string \| null | Validation only: set for an Admiral-local address (`localhost`, a loopback IP), saying a Harbor-hosted mission calls that address on the Harbor's computer, which the check did not reach |
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
| `MergeAndPush` | Integrate and verify in the mission dock, then push the exact verified commit to the remote target branch (the default); a dirty developer checkout is left untouched |
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
| `InstructionWithReference` | **Default.** Materialize the playbook outside the worktree and instruct the model to read the resolved path (on a Harbor, where the captain cannot read the Admiral's files, it is attached into the dock instead) |
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
| `Pi` | pi coding agent CLI (pi.dev; many providers) |
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

### Persona prompt improvements

The default persona guidance and additive replication procedure are documented in [Prompt and Handoff Improvements](../PROMPT_AND_HANDOFF_IMPROVEMENTS.md). Live prompt-template edits apply when future mission instructions are generated; they do not rewrite in-flight briefs. No request fields, output signals, plan schema, or endpoints change. Embedded defaults in upgraded builds include the guidance; startup upgrades only recognized untouched previous defaults and retains operator-edited templates.

Synthetic activity generation is an operator CLI/global-admin REST workflow (`POST /api/v1/activity/synthetic`), with no new MCP write tool. Existing enumerate/history/token-summary tools can read the generated synthetic records. See [synthetic activity guide](SYNTHETIC_ACTIVITY.md).

### Strict captain assignments

Set `captainAssignments[].strictCaptain` to `true` alongside `captainId` to wait for that selected captain instead of falling back when busy/unavailable. The default is `false`; tier fallback remains unchanged. Exact persona overrides take precedence over `*`. Strict selection also prevents automatic rework escalation to a different captain: an exhausted or stagnant chain reports remediation and preserves its work. Without a selected captain this flag has no effect. C# clients share `CaptainAssignmentOverride.StrictCaptain`. See [PIPELINES.md](PIPELINES.md#validated-slice-contracts) for plan validation and full-pipeline rework.
