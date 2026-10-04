# Armada Security Review

> **Type:** security review and surface inventory (V1_READINESS W1.1). Living document: update the findings table
> and the inventory in the same change that adds, removes, or fixes an entry point.
>
> **Scope:** the Admiral (REST, MCP, WebSocket, Harbor link, remote tunnel and dashboard relay), Armada.Proxy,
> the Helm CLI's stdio MCP server, and every place that touches the host file system or runs a process.
>
> **Last updated:** 2026-10-04 (W1 security branch). **External review (W1.8):** not yet performed.

## How to read this document

1. [Security model](#security-model) explains who can do what and how it is enforced.
2. [Findings](#findings) lists every gap found, its severity, its status (Fixed in W1, or Open with an owner), and the
   entry points it applies to. Finding ids (F-nn fixed, O-nn open) are referenced from the inventory tables.
3. [Running agents safely](#running-agents-safely) covers the captain auto-approve flags.
4. The inventory: [REST](#rest-routes), [MCP tools](#mcp-tools), [WebSocket](#websocket-routes-and-commands), and
   [other entry points](#other-entry-points) (Harbor link, proxy, tunnel, stdio MCP, static files, file system and
   process entry points). The REST and MCP tables are generated from `RouteAuthorizationRegistry` and
   `McpToolAuthorizationRegistry`; the coverage suite (`E2E.AuthorizationCoverage`) proves those registries match
   what the server registers, so the tables cannot silently miss a route or tool.

Severity: **Critical** (unauthenticated remote code execution or full takeover), **High** (privilege escalation,
cross-tenant access, or code execution by a low-privilege user), **Medium** (information disclosure, missing audit,
defense in depth), **Low** (hardening).

## Security model

### Identities

| Identity | How it authenticates | Scope |
|----------|----------------------|-------|
| User session | `POST /api/v1/authenticate` (email + password, or any valid credential header) returns an encrypted session token sent as `X-Token` | The user's tenant; `IsAdmin` users are global admins, `IsTenantAdmin` users administer their tenant |
| Bearer credential | `Authorization: Bearer <token>`; tokens are server-generated, shown once at creation, redacted on every read | The owning user |
| Local API key | `X-Api-Key: <ApiKey from settings.json>`; generated on first start, read by the Helm CLI | The synthetic global admin `system@armada` (tenant `ten_system`), which can never log in with a password |
| Ask thread token | Session token bound to one Ask thread, minted for a captain's MCP connection | MCP only; refused on REST and WebSocket; state-changing tool calls become proposals |
| Loopback MCP caller | No credential, MCP listener bound to loopback, caller on loopback, `Mcp.AllowUnauthenticatedLoopback` true (default) | The default tenant's tenant admin (the local Claude Code setup) |
| Harbor | Link upgrade with `x-access-key` = an Armada credential (bearer token, session token, or API key) | The credential's tenant and user; unauthenticated links only from loopback to a loopback-bound Admiral |
| Proxy user | Shared proxy password (challenge-response) | Reaches only the relay; every relayed call still needs Armada credentials (no per-user identity at the proxy, see O-11) |

### Authorization

Every REST route and every MCP tool declares an explicit requirement: a resource type, an operation
(`Read`, `Create`, `Update`, `Delete`, `Execute`, `Admin`, per AUTHENTICATION.md), and a permission level
(`NoAuthRequired`, `Authenticated`, `TenantAdmin`, `AdminOnly`).

- **REST:** `RouteAuthorizationRegistry` holds one line per HTTP method and route template. The Admiral checks it
  centrally in Watson's PreRouting hook, after resolving the request to the exact template Watson will dispatch to,
  and before any route handler runs. Handlers additionally check the same registry (by concrete path) and apply
  tenant and owner scoping to the data they read and write.
- **MCP:** `McpToolAuthorizationRegistry` holds one line per tool name. Every registered tool is wrapped so the
  requirement is checked against the caller of each call: the MCP request's credential, the loopback default
  context, or the Ask Armada caller an approved proposal runs as.
- **Fail closed:** a route or tool without a declaration is treated as `AdminOnly`, logged at startup and at use,
  and fails `E2E.AuthorizationCoverage` (which also fails on declarations the server no longer registers).
- **WebSocket `/ws`:** the upgrade requires a credential; every command requires a global admin; events reach only
  sockets of the entity's tenant (global admins can opt in to all tenants).
- **Bypass rules:** global admins pass every level; tenant admins pass `TenantAdmin` within their tenant; tenant
  admins cannot modify, delete, or mint credentials for a global admin account.

### Safe defaults

- The Admiral refuses to listen on a non-loopback hostname while default credentials are in use
  (`admin@armada` with the password `password`, or the seeded `default` bearer token), unless
  `AllowDefaultCredentialsOnNetwork` is true. Headless installs set `ARMADA_INITIAL_ADMIN_PASSWORD` before the first
  start; Docker compose requires it.
- A dashboard session for an `admin@armada` account that still has the default password can call only whoami,
  status, and `PUT /api/v1/account/password` until the password changes; the dashboard shows a password change
  screen.
- The seeded `default` bearer token stops working, and is deactivated, once its owner's default password is changed
  (chosen over "once another credential exists" because the password change is the explicit "this install is now
  secured" step, and it keeps the README's local `Bearer default` workflow working until then).
- The dashboard shows a persistent warning banner to admins and tenant admins while defaults are in use.
- Self-registration (`POST /api/v1/onboarding`) is off by default.
- MCP is authenticated by default; unauthenticated calls are accepted only on a loopback-bound listener (D2).

### Accounting

- Every shell command or process Armada runs on a user's behalf writes an `audit.command` event before it starts:
  workspace exec (`WorkspaceExec`), fleet action Command runs (`FleetAction`), check runs including deployment and
  verification commands (`CheckRun`), Harbor probes (`HarborProbe`), and merge queue test commands
  (`MergeQueueTest`). The payload records source, command (secret-shaped values redacted, 4,000 characters max),
  working directory, host (Admiral or Harbor id), tenant, user, vessel, and the owning record. Only a global admin can
  delete `audit.*` events.
- Request history records REST calls with redacted headers, bodies, and query strings.
- Not yet covered: authentication successes and failures, authorization denials as persisted events, and actor ids on
  operational events (O-16).

### Secrets

Read endpoints never return API keys, bearer tokens (except once, in the create response), passwords, password
hashes, session encryption keys, GitHub tokens, model endpoint API keys, or remote tunnel passwords and enrollment
tokens. Request history redacts secret-bearing headers, any JSON or form key whose name contains `password` or
`secret` or ends with `token`, `apikey`, `accesskey`, `secretkey`, `privatekey`, or `encryptionkey`, secret-shaped
values under any key (`ghp_...`, `sk-...`, `AKIA...`), the raw query string, and the same patterns in non-JSON text.
Request logs omit query strings. `E2E.SecretsAndAudit` seeds known secrets and greps responses, request history,
and the server log for them.

## Findings

### Fixed in W1

| Id | Severity | Surface | Finding | Fix |
|----|----------|---------|---------|-----|
| F-01 | Critical | REST | `POST /api/v1/server/stop`, `restart`, `rebuild`, `rollback` ran without authentication unless `RequireAuthForShutdown` was set (default false). Rebuild accepts a `SourcePath` and runs `dotnet publish` and `npm run build` from it: unauthenticated remote code execution wherever the REST port was reachable, including through the proxy relay (which blocks only `server/stop`). | Always `AdminOnly`, enforced centrally; `RequireAuthForShutdown` is deprecated and ignored; the CLI sends the local API key. |
| F-02 | Critical | MCP | Unauthenticated MCP calls were accepted on any binding and ran as the default tenant's tenant admin; a presented but invalid credential silently fell back to that anonymous context. | Authenticated by default per D2; invalid credentials get 401; unauthenticated only on loopback-bound listeners with `Mcp.AllowUnauthenticatedLoopback`. |
| F-03 | Critical | MCP | No MCP tool checked permissions. `backup` (overwrites any `outputPath`), `restore` (replaces the database and settings from any zip on the host), and `stop_server` were callable by any MCP caller; regular users could call every write tool. | Per-tool requirements; `backup`, `restore`, `stop_server` are `AdminOnly` (a credential is required even on loopback); writes are `TenantAdmin`. |
| F-04 | High | REST, MCP | `POST /api/v1/check-runs` with `CommandOverride`, and `retry`, let any authenticated user run arbitrary shell commands on the Admiral host. | `TenantAdmin` for every check-run write (REST and `run_check` / `retry_check_run`), plus audit records. |
| F-05 | High | Harbor link | With `Harbor.RequireAuth` off (default) any host could open a Harbor link; with it on, only the presence of `x-access-key` was checked. The tenant came from the unauthenticated `x-tenant-guid` header, so a rogue Harbor could register under any tenant and receive prompts, environment, and model endpoint API keys. | The access key is validated as an Armada credential and determines tenant and user (a global admin may name a tenant); unauthenticated links only from loopback to a loopback-bound Admiral. |
| F-06 | High | REST | `POST /api/v1/harbors/{id}/probe` (runs any executable on the Harbor host) was open to any authenticated user who could see the Harbor. | `TenantAdmin` plus audit records. |
| F-07 | High | Install defaults | Default credentials (`admin@armada` / `password`, `Bearer default`) worked on every binding; the Docker config bound `0.0.0.0`. | Non-loopback bind refused while defaults are in use; forced password change; seeded token retired after the change; `ARMADA_INITIAL_ADMIN_PASSWORD`; dashboard banner. |
| F-08 | High | REST | The synthetic API-key identity `system@armada` (tenant `ten_system`, global admin) was seeded with the password `system`, and `POST /api/v1/authenticate` accepted it. | Password login is refused for the system tenant and user; the stored password is random. |
| F-09 | High | REST | A tenant admin could take over a global admin sharing its tenant: change the global admin's password (`PUT /api/v1/users/{id}`), mint a credential for it (`POST /api/v1/credentials`), or set a chosen token on its credential (`PUT /api/v1/credentials/{id}`). | Only a global admin may modify, delete, or mint credentials for a global admin; tokens are always server-generated and never changed by update. |
| F-10 | High | REST | Self-registration was on by default: anyone who could reach the Admiral could create a user in any tenant whose id they knew (`default` is well known), then use every `Authenticated` route, including Ask threads (O-02). | `AllowSelfRegistration` defaults to false (new installs and the Docker configs). |
| F-11 | High | REST, MCP, WebSocket | Deleting a vessel recursively deleted `vessel.LocalPath` and `docks/<vessel.Name>`, both caller-controlled: a tenant admin could delete any directory the Admiral could write. Dock provisioning force-removed a non-repository `LocalPath`. | Directories are removed only when strictly inside `ReposDirectory` / `DocksDirectory` (symlinks refused); otherwise left in place with a warning. |
| F-12 | Medium | REST | Credential list and read returned every bearer token in full (a tenant admin saw every user's token); a client could choose its own token. | Tokens shown once at creation and masked (`****abcd`) on every read; server-generated only. |
| F-13 | Medium | REST | `GET`/`PUT /api/v1/settings` returned the remote tunnel password and enrollment token. | Returned as `********`; a masked value sent back keeps the stored secret. |
| F-14 | Medium | Request history | Captured bodies kept the enrollment token and other keys not on an exact list, passwords in non-JSON bodies (the loose-text path only inserted a marker before the value), secret-shaped values under innocuous keys, and the raw query string. | Pattern-based key detection, value replacement in loose text, `SecretRedactor` on JSON string values, redacted query string, more header names. |
| F-15 | Medium | Logs | Debug request logging wrote full query strings (including `/ws?token=`) in the Admiral and the proxy. | Logs the path without the query string. |
| F-16 | Medium | Accounting | No audit trail for shell commands run for users; events were deletable by tenant admins. | `audit.command` events for workspace exec, fleet action commands, check runs, Harbor probes, merge queue tests; only global admins can delete `audit.*` events. |
| F-17 | Medium | REST | Nineteen `POST .../enumerate` routes required `TenantAdmin` while their `GET` list counterparts (same scoped query) were `Authenticated`, so regular users could not enumerate. | `Authenticated`, scoping verified per route (see the permission changes). |
| F-18 | Low | Authentication | Password verification compared hashes with `==`. | Constant-time comparison. |
| F-19 | Medium | MCP | `delete_event` / `delete_events` deleted any tenant's events by id. | Caller tenant and owner scoping; audit events need a global admin. |
| F-20 | Low | Docker | With the Docker config's `0.0.0.0` hostname, the MCP listener (HttpListener) never bound, so MCP was unreachable in the container. | `0.0.0.0` is mapped to `*` for the MCP listener. |
| F-21 | Medium | Containers | Images ran as root and used floating base tags. | Non-root (`APP_UID` 1654; nginx-unprivileged UID 101 on 8080) and pinned patch tags. |
| F-22 | Medium | Supply chain | No dependency scanning; the dashboard had a high npm advisory (undici, dev dependency). | `.github/workflows/security.yml` (NuGet vulnerable including transitive, `npm audit --audit-level=high`); lockfile updated. |
| F-23 | High (mitigated) | Captains | CLI captains always ran with auto-approve flags (`--dangerously-skip-permissions`, `--full-auto` or the bypass flag on Windows, `--approval-mode yolo`, `--force`, `--auto`, `--yolo`). | Per-captain `autoApprove` switch honored by every CLI runtime; default unchanged (on); documented in [Running agents safely](#running-agents-safely). |
| F-24 | Low | REST | The keyword `POST /api/v1/ask` responder shipped beside Ask threads (D3). | Removed with `AskArmadaService` and the `armada ask` CLI command. |

### Open

| Id | Severity | Surface | Finding | Owner |
|----|----------|---------|---------|-------|
| O-01 | High (multi-tenant only) | MCP | A static scan finds 62 non-admin MCP tools whose handlers read or change entities by id without a caller tenant check (marked in the [MCP table](#mcp-tools)). A tenant admin of one tenant can act on another tenant's entities through MCP; regular users are limited to the read tools. Single-tenant installs are unaffected. | W1 follow-up: MCP tenancy pass (resolve every entity through the caller's scope, as the REST handlers do). |
| O-02 | High | REST (Ask, chat, planning) | Any authenticated user can start an Ask thread turn (`POST /api/v1/ask/threads/{id}/messages`), which runs a CLI captain on the Admiral host with that captain's flags. Ask approval gates Armada's MCP tools, not the CLI's own shell tool, so with auto-approve on this is a shell on the host for every user. Chat and planning are `TenantAdmin`. | W6.3 / W1 follow-up. Mitigation now: set `autoApprove` false on captains used for Ask, keep self-registration off, and grant accounts deliberately. |
| O-03 | Medium | REST, WebSocket | `GET /api/v1/status` and the WebSocket `subscribe` snapshot are server-wide: captain and mission counts, active voyage titles, and the last signals of every tenant reach any authenticated user. | W2.3 (scope the status shape per caller). |
| O-04 | Medium | Harbor | Split-mode captains (experimental, D3) receive no MCP credential, so with authenticated MCP on a non-loopback Admiral their call-home tool calls are refused. A credentialed Harbor can also take over another Harbor's connection by reusing its id. | Harbor split mode: mint a per-launch session token into the launch's MCP config; bind Harbor ids to the credential that registered them. |
| O-05 | Medium | Authentication | Passwords use unsalted SHA-256; there is no rate limiting or lockout on `/authenticate`, `/tenants/lookup` (maps an email to its tenants), or `/onboarding`. | W1 follow-up (AUTHENTICATION.md password security: salted adaptive hash, rate limiting). |
| O-06 | Medium | REST | Creating a tenant seeds `admin@armada` with the default password in that tenant. It is caught by the banner and the forced password change, but not by the startup bind guard once the server is running. | W1 follow-up: seed a random password and require the creator to set one. |
| O-07 | Medium | REST, MCP (TenantAdmin) | Vessel `RepoUrl`, `LocalPath`, and `WorkingDirectory` are not validated: `file://` and local paths clone any repository the Admiral can read, a `RepoUrl` starting with `-` is a possible git option injection, and a prepared bare repository's hooks run during worktree operations. Vessel import browse defaults to the Admiral user's home directory and does not resolve symlinks in the requested path. | W1 follow-up (path and URL allow-lists, `--` before user arguments to git). |
| O-08 | Medium | Fleet actions (TenantAdmin) | Template variables (`vessel.name`, `vessel.defaultBranch`, `vessel.workingDirectory`, `vessel.buildCommand`, `health.summary`) are substituted into shell text unescaped. | Fleet Actions owner (shell-quote substitutions). |
| O-09 | Medium | Deployments (TenantAdmin) | Environment health and verification URLs make server-side HTTP requests (SSRF to internal addresses). | Delivery owner (URL allow-list, block link-local and metadata addresses). |
| O-10 | Low | REST | `/openapi.json` and `/swagger` are public. | Accepted: documentation only, no data. |
| O-11 | Medium | Proxy | One shared password and no per-user identity; `GET /proxy-api/v1/instances` lists every instance without authentication; no rate limiting; the session cookie lacks `Secure`; `X-Forwarded-For` is trusted; the default tunnel password is `armadaadmin`; `AllowInvalidCertificates` disables TLS validation; the route policy blocks paths by exact string. Relayed calls still need Armada credentials. | Proxy owner (W1 follow-up). |
| O-12 | Low | MCP, WebSocket (AdminOnly) | `backup` and `restore` take arbitrary host paths. | Accepted: global admins are host operators; documented. |
| O-13 | Low | WebSocket | Planning-session tool output and check-run output are broadcast to every socket in the tenant without secret scrubbing. | W1 follow-up (run broadcasts through `SecretRedactor`, owner-only delivery). |
| O-14 | Low | Docker | The observability stack publishes Prometheus, Loki, and Grafana ports, Grafana uses `admin` / `admin`, and the Admiral's `/metrics` port 9464 is unauthenticated. | W5 / operations. |
| O-15 | Medium | Docker (upgrade) | The Admiral container now runs as UID 1654: existing bind-mounted `db` and `logs` directories created by root must be made writable (`chown -R 1654:1654`). | W5 / operations (upgrade notes in DOCKER.md). |
| O-16 | Low | Accounting | Operational events from `EmitEventAsync` carry no tenant or actor; authentication failures and authorization denials are not persisted events. | W1 follow-up (AUTHENTICATION.md accounting). |
| O-17 | Low | REST | Self-service password change through `PUT /api/v1/users/{id}` does not ask for the current password (`PUT /api/v1/account/password` does). | W1 follow-up. |
| O-18 | Low | CLI | `armada mcp stdio` has no authentication: it opens the database directly as the OS user. | Accepted: trust boundary is the OS account that can read `~/.armada`. |
| O-19 | Low | Dashboard | The new security strings (password change screen, banner, auto-approve toggle, token-shown-once dialog) are English in every locale. | W6.6. |

### Permission changes in W1

| Change | Before | After |
|--------|--------|-------|
| 19 `POST .../enumerate` routes (captains, deployments, docks, environments, events, fleets, incidents, merge-queue, missions, missions/summaries, objectives, backlog, playbooks, releases, runbooks, runbook-executions, signals, vessels, voyages, workflow-profiles) | TenantAdmin | Authenticated (handlers scope to the caller exactly as the `GET` list does) |
| `POST /api/v1/check-runs`, `/check-runs/import`, `/check-runs/sync/github-actions`, `/check-runs/{id}/retry`, `DELETE /check-runs/{id}` | Authenticated | TenantAdmin |
| `POST /api/v1/harbors/{id}/probe` | Authenticated | TenantAdmin |
| `POST /api/v1/server/stop`, `restart`, `rebuild`, `rollback` | No authentication unless `RequireAuthForShutdown` | AdminOnly, always |
| `POST /api/v1/workspace/vessels/{vesselId}/exec` | Matrix said Authenticated; the handler already required a tenant admin | Declared TenantAdmin (no effective change) |
| Any route without a declaration | Authenticated | AdminOnly (fail closed; none exist) |
| MCP tools | No checks: any MCP caller (including unauthenticated, as the default tenant's tenant admin) could call every tool | Reads and caller-owned writes (memories, model endpoints, harbors) Authenticated; other writes and executions TenantAdmin; `backup`, `restore`, `stop_server` AdminOnly |
| MCP persona, pipeline, prompt template, playbook writes | Any MCP caller | TenantAdmin (the tools are not owner-scoped like the REST routes, which stay Authenticated with handler checks) |
| Unauthenticated MCP on a non-loopback listener | Allowed | Refused (401) |
| Tenant admin editing, deleting, or minting credentials for a global admin | Allowed | 403 |
| Deleting `audit.*` events (REST and MCP) | Tenant admin (own tenant) | Global admin only |
| `POST /api/v1/onboarding` | Enabled by default | Disabled by default (`AllowSelfRegistration`) |
| Dashboard session of an `admin@armada` account with the default password | Full access | whoami, status, and password change only |
| `PUT /api/v1/account/password` | (new) | Authenticated |
| `POST /api/v1/ask` | Authenticated | Removed (D3) |

## Running agents safely

Captains are AI coding agents that run as CLI processes on the Admiral host (or on a Harbor) with the operating
system permissions of the account that runs Armada. By default each runtime is launched with its auto-approve or
permission-bypass flag so missions can run unattended:

| Runtime | Default flag | With `autoApprove` false |
|---------|--------------|--------------------------|
| Claude Code | `--dangerously-skip-permissions` | `--permission-mode acceptEdits` (file edits allowed; shell and other tools need allow rules in the project's Claude Code settings) |
| Codex | `--full-auto` (`--dangerously-bypass-approvals-and-sandbox` on Windows) | `--sandbox workspace-write` (no approval bypass; writes confined to the workspace) |
| Gemini | `--approval-mode yolo` | `--approval-mode auto_edit` |
| Cursor | `--force` | no `--force` |
| OpenCode | `--auto` | no `--auto` |
| Mux | `--yolo` | `--approval-policy deny` (an explicit Mux approval policy on the captain wins) |
| ApiEndpoint | not applicable (tools run in-process through Armada) | not applicable |

With auto-approve on, a captain can run any command the Armada account can run: read files outside the dock,
use credentials in the environment, and reach the network. Treat every mission prompt, repository, and Ask
message as able to trigger that.

Recommendations:

1. Run Armada under a dedicated, unprivileged OS account (or in the container), not your daily account, and give it
   only the repository and cloud credentials the missions need.
2. Turn auto-approve off for captains whose missions do not need unattended shell access, and for every captain
   used by Ask threads (O-02). Set it per captain in the dashboard (Captains, edit, "Auto-approve agent tool use"),
   through MCP (`create_captain` / `update_captain` with `autoApprove`), or through REST (`runtimeOptionsJson`
   containing `{"autoApprove": false}`). Without auto-approve, unattended missions that need shell commands stall or
   fail unless the runtime's own configuration allows those commands.
3. Prefer Harbors on separate machines or VMs for untrusted repositories, and keep `RequireHarborForLaunch` on when
   captains must never run on the Admiral host.
4. Keep the Admiral on localhost unless you need remote access; when you expose it, change the default password
   first (the Admiral refuses otherwise) and put TLS in front of it.
5. Review `audit.command` events (`enumerate` with entityType `events`, or the Events page) for commands run
   through workspace exec, fleet actions, check runs, Harbor probes, and merge queue tests. Commands a captain runs
   inside its own CLI session are visible in the mission log, not as audit events.

## REST routes

Generated from `RouteAuthorizationRegistry` (350 declarations: 348 API routes plus the OpenAPI document and Swagger
UI). Columns: requirement (`Resource:Operation`), permission level, tenant scoping (how the handler limits data to the
caller), input (how the request is parsed; every typed body is deserialized into a model, unknown fields ignored), and
the findings that apply. "caller tenant/user (handler)" means the handler or the service it calls reads and writes
only within the caller's tenant (tenant admins) or the caller's own records (regular users); global admins see all.

| Method | Route | Registrar | Requirement | Level | Tenant scoping | Input | Findings |
|--------|-------|-----------|-------------|-------|----------------|-------|----------|
| GET | `/openapi.json` | Documentation | ApiDocumentation:Read | None (public) | none | path | - |
| GET | `/swagger` | Documentation | ApiDocumentation:Read | None (public) | none | path | - |
| PUT | `/api/v1/account/password` | AuthRoutes | User:Update | Authenticated | caller tenant/user (handler) | typed JSON body | F-07 Fixed |
| POST | `/api/v1/captains/{id}/chat` | AskRoutes | Captain:Execute | TenantAdmin | caller tenant/user (handler) | typed JSON body | O-02 Open |
| POST | `/api/v1/ask/threads/enumerate` | AskRoutes | AskThread:Read | Authenticated | caller tenant/user (handler) | no body / path | - |
| POST | `/api/v1/ask/threads` | AskRoutes | AskThread:Create | Authenticated | caller tenant/user (handler) | no body / path | - |
| GET | `/api/v1/ask/threads/{id}` | AskRoutes | AskThread:Read | Authenticated | caller tenant/user (handler) | path | - |
| PUT | `/api/v1/ask/threads/{id}` | AskRoutes | AskThread:Update | Authenticated | caller tenant/user (handler) | no body / path | - |
| DELETE | `/api/v1/ask/threads/{id}` | AskRoutes | AskThread:Delete | Authenticated | caller tenant/user (handler) | path | - |
| POST | `/api/v1/ask/threads/{id}/messages/enumerate` | AskRoutes | AskThread:Read | Authenticated | caller tenant/user (handler) | no body / path | - |
| POST | `/api/v1/ask/threads/{id}/messages` | AskRoutes | AskThread:Execute | Authenticated | caller tenant/user (handler) | no body / path | O-02 Open |
| POST | `/api/v1/ask/threads/{id}/cancel` | AskRoutes | AskThread:Execute | Authenticated | caller tenant/user (handler) | no body / path | - |
| POST | `/api/v1/ask/threads/{id}/summarize` | AskRoutes | AskThread:Execute | Authenticated | caller tenant/user (handler) | no body / path | - |
| POST | `/api/v1/ask/threads/{id}/read` | AskRoutes | AskThread:Execute | Authenticated | caller tenant/user (handler) | no body / path | - |
| POST | `/api/v1/ask/threads/{id}/actions` | AskRoutes | AskThread:Execute | Authenticated | caller tenant/user (handler) | no body / path | - |
| POST | `/api/v1/ask/threads/{id}/proposals/{pid}/approve` | AskRoutes | AskThread:Execute | Authenticated | caller tenant/user (handler) | no body / path | - |
| POST | `/api/v1/ask/threads/{id}/proposals/{pid}/reject` | AskRoutes | AskThread:Execute | Authenticated | caller tenant/user (handler) | no body / path | - |
| GET | `/api/v1/ask/threads/{id}/work/{workId}` | AskRoutes | AskThread:Read | Authenticated | caller tenant/user (handler) | path | - |
| GET | `/api/v1/ask/quick-actions` | AskRoutes | AskThread:Read | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| POST | `/api/v1/authenticate` | AuthRoutes | Session:Execute | None (public) | none | typed JSON body | F-08, O-05 Fixed / Open |
| GET | `/api/v1/whoami` | AuthRoutes | Session:Read | Authenticated | caller tenant/user (handler) | path | - |
| POST | `/api/v1/tenants/lookup` | AuthRoutes | Session:Read | None (public) | none | typed JSON body | O-05 Open |
| POST | `/api/v1/onboarding` | AuthRoutes | User:Execute | None (public) | none | typed JSON body | F-10, O-05 Fixed / Open |
| GET | `/api/v1/backup` | BackupRoutes | Backup:Admin | AdminOnly | server-wide (admin) | path | O-12 Open |
| POST | `/api/v1/restore` | BackupRoutes | Backup:Admin | AdminOnly | server-wide (admin) | no body / path | O-12 Open |
| GET | `/api/v1/captains` | CaptainRoutes | Captain:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| POST | `/api/v1/captains/enumerate` | CaptainRoutes | Captain:Read | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| POST | `/api/v1/captains` | CaptainRoutes | Captain:Create | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| GET | `/api/v1/captains/{id}` | CaptainRoutes | Captain:Read | Authenticated | caller tenant/user (handler) | path | - |
| GET | `/api/v1/captains/{id}/tools` | CaptainRoutes | Captain:Read | Authenticated | caller tenant/user (handler) | path | - |
| PUT | `/api/v1/captains/{id}` | CaptainRoutes | Captain:Update | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| POST | `/api/v1/captains/{id}/unquarantine` | CaptainRoutes | Captain:Execute | TenantAdmin | caller tenant/user (handler) | no body / path | - |
| POST | `/api/v1/captains/{id}/stop` | CaptainRoutes | Captain:Execute | TenantAdmin | caller tenant/user (handler) | no body / path | - |
| POST | `/api/v1/captains/stop-all` | CaptainRoutes | Captain:Execute | TenantAdmin | caller tenant/user (handler) | no body / path | - |
| GET | `/api/v1/captains/{id}/log` | CaptainRoutes | Captain:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| DELETE | `/api/v1/captains/{id}` | CaptainRoutes | Captain:Delete | TenantAdmin | caller tenant/user (handler) | path | - |
| POST | `/api/v1/captains/delete/multiple` | CaptainRoutes | Captain:Delete | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| GET | `/api/v1/check-runs` | CheckRunRoutes | CheckRun:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| POST | `/api/v1/check-runs/enumerate` | CheckRunRoutes | CheckRun:Read | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| POST | `/api/v1/check-runs` | CheckRunRoutes | CheckRun:Create | TenantAdmin | caller tenant/user (handler) | typed JSON body | F-04 Fixed |
| POST | `/api/v1/check-runs/import` | CheckRunRoutes | CheckRun:Execute | TenantAdmin | caller tenant/user (handler) | typed JSON body | F-04 Fixed |
| POST | `/api/v1/check-runs/sync/github-actions` | CheckRunRoutes | CheckRun:Create | TenantAdmin | caller tenant/user (handler) | typed JSON body | F-04 Fixed |
| GET | `/api/v1/check-runs/{id}` | CheckRunRoutes | CheckRun:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| POST | `/api/v1/check-runs/{id}/retry` | CheckRunRoutes | CheckRun:Execute | TenantAdmin | caller tenant/user (handler) | no body / path | F-04 Fixed |
| DELETE | `/api/v1/check-runs/{id}` | CheckRunRoutes | CheckRun:Delete | TenantAdmin | caller tenant/user (handler) | path + query | F-04 Fixed |
| GET | `/api/v1/deployments` | DeploymentRoutes | Deployment:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| POST | `/api/v1/deployments/enumerate` | DeploymentRoutes | Deployment:Read | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| POST | `/api/v1/deployments` | DeploymentRoutes | Deployment:Create | TenantAdmin | caller tenant/user (handler) | typed JSON body | O-09 Open |
| GET | `/api/v1/deployments/{id}` | DeploymentRoutes | Deployment:Read | Authenticated | caller tenant/user (handler) | path | - |
| PUT | `/api/v1/deployments/{id}` | DeploymentRoutes | Deployment:Update | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| POST | `/api/v1/deployments/{id}/approve` | DeploymentRoutes | Deployment:Execute | TenantAdmin | caller tenant/user (handler) | no body / path | - |
| POST | `/api/v1/deployments/{id}/deny` | DeploymentRoutes | Deployment:Execute | TenantAdmin | caller tenant/user (handler) | no body / path | - |
| POST | `/api/v1/deployments/{id}/verify` | DeploymentRoutes | Deployment:Execute | TenantAdmin | caller tenant/user (handler) | no body / path | - |
| POST | `/api/v1/deployments/{id}/rollback` | DeploymentRoutes | Deployment:Execute | TenantAdmin | caller tenant/user (handler) | no body / path | - |
| DELETE | `/api/v1/deployments/{id}` | DeploymentRoutes | Deployment:Delete | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| GET | `/api/v1/docks` | DockRoutes | Dock:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| POST | `/api/v1/docks/enumerate` | DockRoutes | Dock:Read | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| GET | `/api/v1/docks/{id}` | DockRoutes | Dock:Read | Authenticated | caller tenant/user (handler) | path | - |
| DELETE | `/api/v1/docks/{id}` | DockRoutes | Dock:Delete | TenantAdmin | caller tenant/user (handler) | path | - |
| DELETE | `/api/v1/docks/{id}/purge` | DockRoutes | Dock:Delete | TenantAdmin | caller tenant/user (handler) | path | - |
| POST | `/api/v1/docks/{id}/repair` | DockRoutes | Dock:Execute | TenantAdmin | caller tenant/user (handler) | no body / path | - |
| POST | `/api/v1/docks/{id}/unstick` | DockRoutes | Dock:Execute | TenantAdmin | caller tenant/user (handler) | no body / path | - |
| POST | `/api/v1/docks/delete/multiple` | DockRoutes | Dock:Delete | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| GET | `/api/v1/environments` | EnvironmentRoutes | Environment:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| POST | `/api/v1/environments/enumerate` | EnvironmentRoutes | Environment:Read | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| POST | `/api/v1/environments` | EnvironmentRoutes | Environment:Create | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| GET | `/api/v1/environments/{id}` | EnvironmentRoutes | Environment:Read | Authenticated | caller tenant/user (handler) | path | - |
| PUT | `/api/v1/environments/{id}` | EnvironmentRoutes | Environment:Update | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| DELETE | `/api/v1/environments/{id}` | EnvironmentRoutes | Environment:Delete | TenantAdmin | caller tenant/user (handler) | path + query | - |
| GET | `/api/v1/events` | EventRoutes | Event:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| POST | `/api/v1/events/enumerate` | EventRoutes | Event:Read | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| DELETE | `/api/v1/events/{id}` | EventRoutes | Event:Delete | TenantAdmin | caller tenant/user (handler) | path | F-16 Fixed |
| POST | `/api/v1/events/delete/multiple` | EventRoutes | Event:Delete | TenantAdmin | caller tenant/user (handler) | typed JSON body | F-16 Fixed |
| POST | `/api/v1/fleet-actions/enumerate` | FleetActionRoutes | FleetAction:Read | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| POST | `/api/v1/fleet-actions` | FleetActionRoutes | FleetAction:Create | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| POST | `/api/v1/fleet-actions/run` | FleetActionRoutes | FleetAction:Execute | TenantAdmin | caller tenant/user (handler) | typed JSON body | F-16, O-08 Fixed / Open |
| GET | `/api/v1/fleet-actions/{id}` | FleetActionRoutes | FleetAction:Read | Authenticated | caller tenant/user (handler) | path | - |
| PUT | `/api/v1/fleet-actions/{id}` | FleetActionRoutes | FleetAction:Update | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| DELETE | `/api/v1/fleet-actions/{id}` | FleetActionRoutes | FleetAction:Delete | TenantAdmin | caller tenant/user (handler) | path | - |
| POST | `/api/v1/fleet-actions/{id}/run` | FleetActionRoutes | FleetAction:Execute | TenantAdmin | caller tenant/user (handler) | typed JSON body | F-16, O-08 Fixed / Open |
| POST | `/api/v1/fleet-action-runs/enumerate` | FleetActionRoutes | FleetActionRun:Read | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| GET | `/api/v1/fleet-action-runs/{id}` | FleetActionRoutes | FleetActionRun:Read | Authenticated | caller tenant/user (handler) | path | - |
| POST | `/api/v1/fleet-action-runs/{id}/targets/enumerate` | FleetActionRoutes | FleetActionRun:Read | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| GET | `/api/v1/fleet-action-runs/{id}/targets/{targetId}` | FleetActionRoutes | FleetActionRun:Read | Authenticated | caller tenant/user (handler) | path | - |
| POST | `/api/v1/fleet-action-runs/{id}/cancel` | FleetActionRoutes | FleetActionRun:Execute | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| GET | `/api/v1/fleets` | FleetRoutes | Fleet:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| POST | `/api/v1/fleets/enumerate` | FleetRoutes | Fleet:Read | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| POST | `/api/v1/fleets` | FleetRoutes | Fleet:Create | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| GET | `/api/v1/fleets/{id}` | FleetRoutes | Fleet:Read | Authenticated | caller tenant/user (handler) | path | - |
| PUT | `/api/v1/fleets/{id}` | FleetRoutes | Fleet:Update | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| DELETE | `/api/v1/fleets/{id}` | FleetRoutes | Fleet:Delete | TenantAdmin | caller tenant/user (handler) | path | - |
| POST | `/api/v1/fleets/delete/multiple` | FleetRoutes | Fleet:Delete | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| GET | `/api/v1/harbors` | HarborRoutes | Harbor:Read | Authenticated | caller tenant/user (handler) | path | - |
| POST | `/api/v1/harbors` | HarborRoutes | Harbor:Create | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| GET | `/api/v1/harbors/{id}` | HarborRoutes | Harbor:Read | Authenticated | caller tenant/user (handler) | path | - |
| PUT | `/api/v1/harbors/{id}` | HarborRoutes | Harbor:Update | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| DELETE | `/api/v1/harbors/{id}` | HarborRoutes | Harbor:Delete | Authenticated | caller tenant/user (handler) | path | - |
| POST | `/api/v1/harbors/{id}/enable` | HarborRoutes | Harbor:Execute | Authenticated | caller tenant/user (handler) | no body / path | - |
| POST | `/api/v1/harbors/{id}/disable` | HarborRoutes | Harbor:Execute | Authenticated | caller tenant/user (handler) | no body / path | - |
| POST | `/api/v1/harbors/{id}/probe` | HarborRoutes | Harbor:Execute | TenantAdmin | caller tenant/user (handler) | typed JSON body | F-06 Fixed |
| GET | `/api/v1/history` | HistoryRoutes | History:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| POST | `/api/v1/history/enumerate` | HistoryRoutes | History:Read | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| GET | `/api/v1/inbox` | InboxRoutes | Inbox:Read | Authenticated | caller tenant/user (handler) | path | - |
| GET | `/api/v1/incidents` | IncidentRoutes | Incident:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| POST | `/api/v1/incidents/enumerate` | IncidentRoutes | Incident:Read | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| GET | `/api/v1/incidents/{id}` | IncidentRoutes | Incident:Read | Authenticated | caller tenant/user (handler) | path | - |
| POST | `/api/v1/incidents` | IncidentRoutes | Incident:Create | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| PUT | `/api/v1/incidents/{id}` | IncidentRoutes | Incident:Update | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| DELETE | `/api/v1/incidents/{id}` | IncidentRoutes | Incident:Delete | TenantAdmin | caller tenant/user (handler) | path + query | - |
| GET | `/api/v1/jobs` | JobRoutes | Job:Read | Authenticated | caller tenant/user (handler) | path | - |
| GET | `/api/v1/jobs/{id}` | JobRoutes | Job:Read | Authenticated | caller tenant/user (handler) | path | - |
| POST | `/api/v1/jobs/{id}/cancel` | JobRoutes | Job:Execute | Authenticated | caller tenant/user (handler) | no body / path | - |
| GET | `/api/v1/memories` | MemoryRoutes | Memory:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| POST | `/api/v1/memories` | MemoryRoutes | Memory:Create | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| GET | `/api/v1/memories/{id}` | MemoryRoutes | Memory:Read | Authenticated | caller tenant/user (handler) | path | - |
| PUT | `/api/v1/memories/{id}` | MemoryRoutes | Memory:Update | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| DELETE | `/api/v1/memories/{id}` | MemoryRoutes | Memory:Delete | Authenticated | caller tenant/user (handler) | path | - |
| GET | `/api/v1/merge-queue` | MergeQueueRoutes | MergeQueue:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| POST | `/api/v1/merge-queue/enumerate` | MergeQueueRoutes | MergeQueue:Read | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| POST | `/api/v1/merge-queue` | MergeQueueRoutes | MergeQueue:Create | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| GET | `/api/v1/merge-queue/{id}` | MergeQueueRoutes | MergeQueue:Read | Authenticated | caller tenant/user (handler) | path | - |
| DELETE | `/api/v1/merge-queue/{id}` | MergeQueueRoutes | MergeQueue:Delete | TenantAdmin | caller tenant/user (handler) | path | - |
| POST | `/api/v1/merge-queue/{id}/process` | MergeQueueRoutes | MergeQueue:Execute | TenantAdmin | caller tenant/user (handler) | no body / path | F-16 Fixed |
| POST | `/api/v1/merge-queue/process` | MergeQueueRoutes | MergeQueue:Execute | TenantAdmin | caller tenant/user (handler) | no body / path | F-16 Fixed |
| DELETE | `/api/v1/merge-queue/{id}/purge` | MergeQueueRoutes | MergeQueue:Delete | TenantAdmin | caller tenant/user (handler) | path | - |
| POST | `/api/v1/merge-queue/purge` | MergeQueueRoutes | MergeQueue:Delete | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| GET | `/api/v1/missions` | MissionRoutes | Mission:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| POST | `/api/v1/missions/enumerate` | MissionRoutes | Mission:Read | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| GET | `/api/v1/missions/summaries` | MissionRoutes | Mission:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| POST | `/api/v1/missions/summaries/enumerate` | MissionRoutes | Mission:Read | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| GET | `/api/v1/missions/history` | MissionRoutes | Mission:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| POST | `/api/v1/missions` | MissionRoutes | Mission:Create | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| GET | `/api/v1/missions/{id}` | MissionRoutes | Mission:Read | Authenticated | caller tenant/user (handler) | path | - |
| GET | `/api/v1/missions/{id}/github/pull-request` | MissionRoutes | Mission:Read | Authenticated | caller tenant/user (handler) | path | - |
| GET | `/api/v1/missions/{id}/landing-preview` | MissionRoutes | Mission:Read | Authenticated | caller tenant/user (handler) | path | - |
| GET | `/api/v1/missions/{id}/evaluate-autoland` | MissionRoutes | Mission:Read | Authenticated | caller tenant/user (handler) | path | - |
| PUT | `/api/v1/missions/{id}` | MissionRoutes | Mission:Update | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| PUT | `/api/v1/missions/{id}/status` | MissionRoutes | Mission:Update | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| POST | `/api/v1/missions/{id}/review/approve` | MissionRoutes | Mission:Execute | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| POST | `/api/v1/missions/{id}/review/deny` | MissionRoutes | Mission:Execute | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| DELETE | `/api/v1/missions/{id}` | MissionRoutes | Mission:Delete | TenantAdmin | caller tenant/user (handler) | path | - |
| DELETE | `/api/v1/missions/{id}/purge` | MissionRoutes | Mission:Delete | TenantAdmin | caller tenant/user (handler) | path | - |
| POST | `/api/v1/missions/delete/multiple` | MissionRoutes | Mission:Delete | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| POST | `/api/v1/missions/{id}/restart` | MissionRoutes | Mission:Execute | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| POST | `/api/v1/missions/{id}/retry-landing` | MissionRoutes | Mission:Execute | TenantAdmin | caller tenant/user (handler) | no body / path | - |
| GET | `/api/v1/missions/{id}/diff` | MissionRoutes | Mission:Read | Authenticated | caller tenant/user (handler) | path | - |
| GET | `/api/v1/missions/{id}/log` | MissionRoutes | Mission:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| GET | `/api/v1/missions/{id}/instructions` | MissionRoutes | Mission:Read | Authenticated | caller tenant/user (handler) | path | - |
| GET | `/api/v1/model-endpoints` | ModelEndpointRoutes | ModelEndpoint:Read | Authenticated | caller tenant/user (handler) | path | - |
| POST | `/api/v1/model-endpoints` | ModelEndpointRoutes | ModelEndpoint:Create | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| GET | `/api/v1/model-endpoints/{id}` | ModelEndpointRoutes | ModelEndpoint:Read | Authenticated | caller tenant/user (handler) | path | - |
| PUT | `/api/v1/model-endpoints/{id}` | ModelEndpointRoutes | ModelEndpoint:Update | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| DELETE | `/api/v1/model-endpoints/{id}` | ModelEndpointRoutes | ModelEndpoint:Delete | Authenticated | caller tenant/user (handler) | path | - |
| POST | `/api/v1/model-endpoints/{id}/validate` | ModelEndpointRoutes | ModelEndpoint:Read | Authenticated | caller tenant/user (handler) | no body / path | - |
| POST | `/api/v1/model-endpoints/health-check` | ModelEndpointRoutes | ModelEndpoint:Execute | Authenticated | caller tenant/user (handler) | no body / path | - |
| GET | `/api/v1/objectives/{id}/refinement-sessions` | ObjectiveRefinementRoutes | ObjectiveRefinementSession:Read | Authenticated | caller tenant/user (handler) | path | - |
| POST | `/api/v1/objectives/{id}/refinement-sessions` | ObjectiveRefinementRoutes | ObjectiveRefinementSession:Create | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| GET | `/api/v1/backlog/{id}/refinement-sessions` | ObjectiveRefinementRoutes | ObjectiveRefinementSession:Read | Authenticated | caller tenant/user (handler) | path | - |
| POST | `/api/v1/backlog/{id}/refinement-sessions` | ObjectiveRefinementRoutes | ObjectiveRefinementSession:Create | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| GET | `/api/v1/objective-refinement-sessions/{id}` | ObjectiveRefinementRoutes | ObjectiveRefinementSession:Read | Authenticated | caller tenant/user (handler) | path | - |
| POST | `/api/v1/objective-refinement-sessions/{id}/messages` | ObjectiveRefinementRoutes | ObjectiveRefinementSession:Execute | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| POST | `/api/v1/objective-refinement-sessions/{id}/summarize` | ObjectiveRefinementRoutes | ObjectiveRefinementSession:Execute | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| POST | `/api/v1/objective-refinement-sessions/{id}/apply` | ObjectiveRefinementRoutes | ObjectiveRefinementSession:Execute | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| POST | `/api/v1/objective-refinement-sessions/{id}/stop` | ObjectiveRefinementRoutes | ObjectiveRefinementSession:Execute | TenantAdmin | caller tenant/user (handler) | no body / path | - |
| DELETE | `/api/v1/objective-refinement-sessions/{id}` | ObjectiveRefinementRoutes | ObjectiveRefinementSession:Delete | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| GET | `/api/v1/objectives` | ObjectiveRoutes | Objective:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| POST | `/api/v1/objectives/enumerate` | ObjectiveRoutes | Objective:Read | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| POST | `/api/v1/objectives/reorder` | ObjectiveRoutes | Objective:Execute | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| GET | `/api/v1/objectives/{id}` | ObjectiveRoutes | Objective:Read | Authenticated | caller tenant/user (handler) | path | - |
| POST | `/api/v1/objectives` | ObjectiveRoutes | Objective:Create | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| POST | `/api/v1/objectives/import/github` | ObjectiveRoutes | Objective:Create | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| PUT | `/api/v1/objectives/{id}` | ObjectiveRoutes | Objective:Update | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| DELETE | `/api/v1/objectives/{id}` | ObjectiveRoutes | Objective:Delete | TenantAdmin | caller tenant/user (handler) | path | - |
| GET | `/api/v1/backlog` | ObjectiveRoutes | Objective:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| POST | `/api/v1/backlog/enumerate` | ObjectiveRoutes | Objective:Read | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| POST | `/api/v1/backlog/reorder` | ObjectiveRoutes | Objective:Execute | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| GET | `/api/v1/backlog/{id}` | ObjectiveRoutes | Objective:Read | Authenticated | caller tenant/user (handler) | path | - |
| POST | `/api/v1/backlog` | ObjectiveRoutes | Objective:Create | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| PUT | `/api/v1/backlog/{id}` | ObjectiveRoutes | Objective:Update | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| DELETE | `/api/v1/backlog/{id}` | ObjectiveRoutes | Objective:Delete | TenantAdmin | caller tenant/user (handler) | path + query | - |
| GET | `/api/v1/personas` | PersonaRoutes | Persona:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| POST | `/api/v1/personas/enumerate` | PersonaRoutes | Persona:Read | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| GET | `/api/v1/personas/{name}` | PersonaRoutes | Persona:Read | Authenticated | caller tenant/user (handler) | path | - |
| POST | `/api/v1/personas` | PersonaRoutes | Persona:Create | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| PUT | `/api/v1/personas/{name}` | PersonaRoutes | Persona:Update | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| DELETE | `/api/v1/personas/{name}` | PersonaRoutes | Persona:Delete | Authenticated | caller tenant/user (handler) | path | - |
| GET | `/api/v1/pipelines` | PipelineRoutes | Pipeline:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| POST | `/api/v1/pipelines/enumerate` | PipelineRoutes | Pipeline:Read | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| GET | `/api/v1/pipelines/{name}` | PipelineRoutes | Pipeline:Read | Authenticated | caller tenant/user (handler) | path | - |
| POST | `/api/v1/pipelines` | PipelineRoutes | Pipeline:Create | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| PUT | `/api/v1/pipelines/{name}` | PipelineRoutes | Pipeline:Update | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| DELETE | `/api/v1/pipelines/{name}` | PipelineRoutes | Pipeline:Delete | Authenticated | caller tenant/user (handler) | path | - |
| GET | `/api/v1/planning-sessions` | PlanningSessionRoutes | PlanningSession:Read | Authenticated | caller tenant/user (handler) | path | - |
| POST | `/api/v1/planning-sessions` | PlanningSessionRoutes | PlanningSession:Create | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| GET | `/api/v1/planning-sessions/{id}` | PlanningSessionRoutes | PlanningSession:Read | Authenticated | caller tenant/user (handler) | path | - |
| POST | `/api/v1/planning-sessions/{id}/messages` | PlanningSessionRoutes | PlanningSession:Execute | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| POST | `/api/v1/planning-sessions/{id}/dispatch` | PlanningSessionRoutes | PlanningSession:Execute | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| POST | `/api/v1/planning-sessions/{id}/summarize` | PlanningSessionRoutes | PlanningSession:Execute | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| POST | `/api/v1/planning-sessions/{id}/stop` | PlanningSessionRoutes | PlanningSession:Execute | TenantAdmin | caller tenant/user (handler) | no body / path | - |
| POST | `/api/v1/planning-sessions/{id}/stop-turn` | PlanningSessionRoutes | PlanningSession:Execute | TenantAdmin | caller tenant/user (handler) | no body / path | - |
| DELETE | `/api/v1/planning-sessions/{id}` | PlanningSessionRoutes | PlanningSession:Delete | TenantAdmin | caller tenant/user (handler) | path | - |
| GET | `/api/v1/playbooks` | PlaybookRoutes | Playbook:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| POST | `/api/v1/playbooks/enumerate` | PlaybookRoutes | Playbook:Read | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| POST | `/api/v1/playbooks` | PlaybookRoutes | Playbook:Create | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| GET | `/api/v1/playbooks/{id}` | PlaybookRoutes | Playbook:Read | Authenticated | caller tenant/user (handler) | path | - |
| PUT | `/api/v1/playbooks/{id}` | PlaybookRoutes | Playbook:Update | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| DELETE | `/api/v1/playbooks/{id}` | PlaybookRoutes | Playbook:Delete | TenantAdmin | caller tenant/user (handler) | path | - |
| GET | `/api/v1/project-profiles` | ProjectProfileRoutes | ProjectProfile:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| POST | `/api/v1/project-profiles/enumerate` | ProjectProfileRoutes | ProjectProfile:Read | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| POST | `/api/v1/project-profiles/validate` | ProjectProfileRoutes | ProjectProfile:Read | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| GET | `/api/v1/project-profiles/resolve/vessels/{vesselId}` | ProjectProfileRoutes | ProjectProfile:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| GET | `/api/v1/project-profiles/{id}/persona-preview/{persona}` | ProjectProfileRoutes | ProjectProfile:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| POST | `/api/v1/project-profiles` | ProjectProfileRoutes | ProjectProfile:Create | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| GET | `/api/v1/project-profiles/{id}` | ProjectProfileRoutes | ProjectProfile:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| PUT | `/api/v1/project-profiles/{id}` | ProjectProfileRoutes | ProjectProfile:Update | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| DELETE | `/api/v1/project-profiles/{id}` | ProjectProfileRoutes | ProjectProfile:Delete | Authenticated | caller tenant/user (handler) | path + query | - |
| GET | `/api/v1/prompt-templates` | PromptTemplateRoutes | PromptTemplate:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| POST | `/api/v1/prompt-templates/enumerate` | PromptTemplateRoutes | PromptTemplate:Read | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| POST | `/api/v1/prompt-templates` | PromptTemplateRoutes | PromptTemplate:Create | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| GET | `/api/v1/prompt-templates/{name}` | PromptTemplateRoutes | PromptTemplate:Read | Authenticated | caller tenant/user (handler) | path | - |
| PUT | `/api/v1/prompt-templates/{name}` | PromptTemplateRoutes | PromptTemplate:Update | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| POST | `/api/v1/prompt-templates/{name}/reset` | PromptTemplateRoutes | PromptTemplate:Execute | Authenticated | caller tenant/user (handler) | no body / path | - |
| GET | `/api/v1/releases` | ReleaseRoutes | Release:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| POST | `/api/v1/releases/enumerate` | ReleaseRoutes | Release:Read | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| POST | `/api/v1/releases` | ReleaseRoutes | Release:Create | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| GET | `/api/v1/releases/{id}` | ReleaseRoutes | Release:Read | Authenticated | caller tenant/user (handler) | path | - |
| GET | `/api/v1/releases/{id}/github/pull-requests` | ReleaseRoutes | Release:Read | Authenticated | caller tenant/user (handler) | path | - |
| PUT | `/api/v1/releases/{id}` | ReleaseRoutes | Release:Update | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| POST | `/api/v1/releases/{id}/refresh` | ReleaseRoutes | Release:Execute | TenantAdmin | caller tenant/user (handler) | no body / path | - |
| DELETE | `/api/v1/releases/{id}` | ReleaseRoutes | Release:Delete | TenantAdmin | caller tenant/user (handler) | path + query | - |
| GET | `/api/v1/request-history` | RequestHistoryRoutes | RequestHistory:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| GET | `/api/v1/request-history/summary` | RequestHistoryRoutes | RequestHistory:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| GET | `/api/v1/request-history/{id}` | RequestHistoryRoutes | RequestHistory:Read | Authenticated | caller tenant/user (handler) | path | - |
| DELETE | `/api/v1/request-history/{id}` | RequestHistoryRoutes | RequestHistory:Delete | TenantAdmin | caller tenant/user (handler) | path | - |
| POST | `/api/v1/request-history/delete/multiple` | RequestHistoryRoutes | RequestHistory:Delete | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| POST | `/api/v1/request-history/delete/by-filter` | RequestHistoryRoutes | RequestHistory:Delete | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| GET | `/api/v1/runbooks` | RunbookRoutes | Runbook:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| POST | `/api/v1/runbooks/enumerate` | RunbookRoutes | Runbook:Read | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| GET | `/api/v1/runbooks/{id}` | RunbookRoutes | Runbook:Read | Authenticated | caller tenant/user (handler) | path | - |
| POST | `/api/v1/runbooks` | RunbookRoutes | Runbook:Create | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| PUT | `/api/v1/runbooks/{id}` | RunbookRoutes | Runbook:Update | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| DELETE | `/api/v1/runbooks/{id}` | RunbookRoutes | Runbook:Delete | TenantAdmin | caller tenant/user (handler) | path | - |
| GET | `/api/v1/runbook-executions` | RunbookRoutes | RunbookExecution:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| POST | `/api/v1/runbook-executions/enumerate` | RunbookRoutes | RunbookExecution:Read | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| GET | `/api/v1/runbook-executions/{id}` | RunbookRoutes | RunbookExecution:Read | Authenticated | caller tenant/user (handler) | path | - |
| POST | `/api/v1/runbooks/{id}/executions` | RunbookRoutes | RunbookExecution:Execute | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| PUT | `/api/v1/runbook-executions/{id}` | RunbookRoutes | RunbookExecution:Update | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| DELETE | `/api/v1/runbook-executions/{id}` | RunbookRoutes | RunbookExecution:Delete | TenantAdmin | caller tenant/user (handler) | path + query | - |
| GET | `/api/v1/runtimes/mux/endpoints` | RuntimeRoutes | Runtime:Read | Authenticated | server-wide | path + query | - |
| GET | `/api/v1/runtimes/mux/endpoints/{name}` | RuntimeRoutes | Runtime:Read | Authenticated | server-wide | path + query | - |
| GET | `/api/v1/signals` | SignalRoutes | Signal:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| POST | `/api/v1/signals/enumerate` | SignalRoutes | Signal:Read | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| POST | `/api/v1/signals` | SignalRoutes | Signal:Create | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| GET | `/api/v1/signals/recent` | SignalRoutes | Signal:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| GET | `/api/v1/signals/{id}` | SignalRoutes | Signal:Read | Authenticated | caller tenant/user (handler) | path | - |
| PUT | `/api/v1/signals/{id}/read` | SignalRoutes | Signal:Update | TenantAdmin | caller tenant/user (handler) | no body / path | - |
| GET | `/api/v1/signals/recipient/{captainId}` | SignalRoutes | Signal:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| DELETE | `/api/v1/signals/{id}` | SignalRoutes | Signal:Delete | TenantAdmin | caller tenant/user (handler) | path | - |
| POST | `/api/v1/signals/delete/multiple` | SignalRoutes | Signal:Delete | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| GET | `/api/v1/skills` | SkillRoutes | Skill:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| POST | `/api/v1/skills/enumerate` | SkillRoutes | Skill:Read | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| POST | `/api/v1/skills` | SkillRoutes | Skill:Create | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| GET | `/api/v1/skills/{id}` | SkillRoutes | Skill:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| PUT | `/api/v1/skills/{id}` | SkillRoutes | Skill:Update | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| DELETE | `/api/v1/skills/{id}` | SkillRoutes | Skill:Delete | Authenticated | caller tenant/user (handler) | path + query | - |
| GET | `/api/v1/status` | StatusRoutes | Status:Read | Authenticated | server-wide | path | O-03 Open |
| GET | `/api/v1/status/health` | StatusRoutes | Status:Read | None (public) | none | path | - |
| GET | `/api/v1/doctor` | StatusRoutes | Status:Read | Authenticated | server-wide | path | - |
| POST | `/api/v1/server/stop` | StatusRoutes | Server:Admin | AdminOnly | server-wide (admin) | no body / path | F-01 Fixed |
| POST | `/api/v1/server/restart` | StatusRoutes | Server:Admin | AdminOnly | server-wide (admin) | no body / path | F-01 Fixed |
| POST | `/api/v1/server/rebuild` | StatusRoutes | Server:Admin | AdminOnly | server-wide (admin) | typed JSON body | F-01 Fixed |
| GET | `/api/v1/server/rebuild/status` | StatusRoutes | Server:Read | AdminOnly | server-wide (admin) | path | - |
| POST | `/api/v1/server/rollback` | StatusRoutes | Server:Admin | AdminOnly | server-wide (admin) | no body / path | F-01 Fixed |
| GET | `/api/v1/settings` | StatusRoutes | Settings:Read | AdminOnly | server-wide (admin) | path | F-13 Fixed |
| PUT | `/api/v1/settings` | StatusRoutes | Settings:Admin | AdminOnly | server-wide (admin) | typed JSON body | F-13 Fixed |
| POST | `/api/v1/server/reset` | StatusRoutes | Server:Admin | AdminOnly | server-wide (admin) | typed JSON body | - |
| GET | `/api/v1/tenants` | TenantRoutes | Tenant:Read | AdminOnly | tenant (handler) | path + query | - |
| POST | `/api/v1/tenants` | TenantRoutes | Tenant:Create | AdminOnly | tenant (handler) | typed JSON body | O-06 Open |
| GET | `/api/v1/tenants/{id}` | TenantRoutes | Tenant:Read | Authenticated | tenant (handler) | path | - |
| PUT | `/api/v1/tenants/{id}` | TenantRoutes | Tenant:Update | AdminOnly | tenant (handler) | typed JSON body | - |
| DELETE | `/api/v1/tenants/{id}` | TenantRoutes | Tenant:Delete | AdminOnly | tenant (handler) | path | - |
| GET | `/api/v1/users` | TenantRoutes | User:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| POST | `/api/v1/users` | TenantRoutes | User:Create | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| GET | `/api/v1/users/{id}` | TenantRoutes | User:Read | Authenticated | caller tenant/user (handler) | path | - |
| PUT | `/api/v1/users/{id}` | TenantRoutes | User:Update | Authenticated | caller tenant/user (handler) | typed JSON body | F-09 Fixed |
| DELETE | `/api/v1/users/{id}` | TenantRoutes | User:Delete | Authenticated | caller tenant/user (handler) | path | F-09 Fixed |
| GET | `/api/v1/credentials` | TenantRoutes | Credential:Read | Authenticated | caller tenant/user (handler) | path + query | F-12 Fixed |
| POST | `/api/v1/credentials` | TenantRoutes | Credential:Create | Authenticated | caller tenant/user (handler) | typed JSON body | F-09, F-12 Fixed |
| GET | `/api/v1/credentials/{id}` | TenantRoutes | Credential:Read | Authenticated | caller tenant/user (handler) | path | F-12 Fixed |
| PUT | `/api/v1/credentials/{id}` | TenantRoutes | Credential:Update | Authenticated | caller tenant/user (handler) | typed JSON body | F-09, F-12 Fixed |
| DELETE | `/api/v1/credentials/{id}` | TenantRoutes | Credential:Delete | Authenticated | caller tenant/user (handler) | path | - |
| GET | `/api/v1/token-usage/summary` | TokenUsageRoutes | TokenUsage:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| GET | `/api/v1/token-usage` | TokenUsageRoutes | TokenUsage:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| POST | `/api/v1/token-usage/delete/by-filter` | TokenUsageRoutes | TokenUsage:Delete | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| POST | `/api/v1/vessel-health/enumerate` | VesselHealthRoutes | VesselHealth:Read | Authenticated | caller tenant/user (handler) | no body / path | - |
| GET | `/api/v1/vessel-health/summary` | VesselHealthRoutes | VesselHealth:Read | Authenticated | caller tenant/user (handler) | path | - |
| POST | `/api/v1/vessel-health/evaluate` | VesselHealthRoutes | VesselHealth:Execute | TenantAdmin | caller tenant/user (handler) | no body / path | - |
| GET | `/api/v1/vessels/{id}/health` | VesselHealthRoutes | VesselHealth:Read | Authenticated | caller tenant/user (handler) | path | - |
| PUT | `/api/v1/vessels/{id}/health/overrides/{criterion}` | VesselHealthRoutes | VesselHealth:Update | TenantAdmin | caller tenant/user (handler) | no body / path | - |
| DELETE | `/api/v1/vessels/{id}/health/overrides/{criterion}` | VesselHealthRoutes | VesselHealth:Delete | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| GET | `/api/v1/vessels/import/browse` | VesselImportRoutes | VesselImport:Read | TenantAdmin | caller tenant/user (handler) | path + query | O-07 Open |
| POST | `/api/v1/vessels/import/discover` | VesselImportRoutes | VesselImport:Execute | TenantAdmin | caller tenant/user (handler) | typed JSON body | O-07 Open |
| POST | `/api/v1/vessels/import` | VesselImportRoutes | VesselImport:Execute | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| POST | `/api/v1/vessels/import/batches/enumerate` | VesselImportRoutes | VesselImport:Read | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| GET | `/api/v1/vessels/import/batches/{id}` | VesselImportRoutes | VesselImport:Read | Authenticated | caller tenant/user (handler) | path | - |
| GET | `/api/v1/vessels/import/categorization/default-prompt` | VesselImportRoutes | VesselImport:Read | TenantAdmin | caller tenant/user (handler) | path | - |
| POST | `/api/v1/vessels/import/batches/{id}/categorize` | VesselImportRoutes | VesselImport:Execute | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| POST | `/api/v1/vessels/import/batches/{id}/fleet-recommendations/apply` | VesselImportRoutes | VesselImport:Execute | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| GET | `/api/v1/vessels` | VesselRoutes | Vessel:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| POST | `/api/v1/vessels/enumerate` | VesselRoutes | Vessel:Read | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| POST | `/api/v1/vessels` | VesselRoutes | Vessel:Create | TenantAdmin | caller tenant/user (handler) | typed JSON body | O-07 Open |
| GET | `/api/v1/vessels/{id}` | VesselRoutes | Vessel:Read | Authenticated | caller tenant/user (handler) | path | - |
| PUT | `/api/v1/vessels/{id}` | VesselRoutes | Vessel:Update | TenantAdmin | caller tenant/user (handler) | typed JSON body | O-07 Open |
| PATCH | `/api/v1/vessels/{id}/context` | VesselRoutes | Vessel:Update | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| GET | `/api/v1/vessels/{id}/git-status` | VesselRoutes | Vessel:Read | Authenticated | caller tenant/user (handler) | path | - |
| GET | `/api/v1/vessels/{id}/branches` | VesselRoutes | Vessel:Read | Authenticated | caller tenant/user (handler) | path | - |
| POST | `/api/v1/vessels/{id}/branches/push` | VesselRoutes | Vessel:Execute | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| POST | `/api/v1/vessels/{id}/branches/merge` | VesselRoutes | Vessel:Execute | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| GET | `/api/v1/vessels/{id}/readiness` | VesselRoutes | Vessel:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| GET | `/api/v1/vessels/{id}/landing-preview` | VesselRoutes | Vessel:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| POST | `/api/v1/vessels/{id}/build-context` | VesselRoutes | Vessel:Execute | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| DELETE | `/api/v1/vessels/{id}` | VesselRoutes | Vessel:Delete | TenantAdmin | caller tenant/user (handler) | path | F-11 Fixed |
| POST | `/api/v1/vessels/delete/multiple` | VesselRoutes | Vessel:Delete | TenantAdmin | caller tenant/user (handler) | typed JSON body | F-11 Fixed |
| GET | `/api/v1/voyages` | VoyageRoutes | Voyage:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| POST | `/api/v1/voyages/enumerate` | VoyageRoutes | Voyage:Read | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| POST | `/api/v1/voyages` | VoyageRoutes | Voyage:Create | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| GET | `/api/v1/voyages/{id}` | VoyageRoutes | Voyage:Read | Authenticated | caller tenant/user (handler) | path | - |
| DELETE | `/api/v1/voyages/{id}` | VoyageRoutes | Voyage:Delete | TenantAdmin | caller tenant/user (handler) | path | - |
| DELETE | `/api/v1/voyages/{id}/purge` | VoyageRoutes | Voyage:Delete | TenantAdmin | caller tenant/user (handler) | path | - |
| POST | `/api/v1/voyages/delete/multiple` | VoyageRoutes | Voyage:Delete | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| GET | `/api/v1/workflow-profiles` | WorkflowProfileRoutes | WorkflowProfile:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| POST | `/api/v1/workflow-profiles/enumerate` | WorkflowProfileRoutes | WorkflowProfile:Read | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| POST | `/api/v1/workflow-profiles/validate` | WorkflowProfileRoutes | WorkflowProfile:Read | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| GET | `/api/v1/workflow-profiles/preview/vessels/{vesselId}` | WorkflowProfileRoutes | WorkflowProfile:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| GET | `/api/v1/workflow-profiles/resolve/vessels/{vesselId}` | WorkflowProfileRoutes | WorkflowProfile:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| POST | `/api/v1/workflow-profiles` | WorkflowProfileRoutes | WorkflowProfile:Create | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| GET | `/api/v1/workflow-profiles/{id}` | WorkflowProfileRoutes | WorkflowProfile:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| PUT | `/api/v1/workflow-profiles/{id}` | WorkflowProfileRoutes | WorkflowProfile:Update | TenantAdmin | caller tenant/user (handler) | typed JSON body | - |
| DELETE | `/api/v1/workflow-profiles/{id}` | WorkflowProfileRoutes | WorkflowProfile:Delete | TenantAdmin | caller tenant/user (handler) | path + query | - |
| GET | `/api/v1/workspace/vessels/{vesselId}/tree` | WorkspaceRoutes | Workspace:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| GET | `/api/v1/workspace/vessels/{vesselId}/diff` | WorkspaceRoutes | Workspace:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| GET | `/api/v1/workspace/vessels/{vesselId}/file` | WorkspaceRoutes | Workspace:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| PUT | `/api/v1/workspace/vessels/{vesselId}/file` | WorkspaceRoutes | Workspace:Update | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| POST | `/api/v1/workspace/vessels/{vesselId}/exec` | WorkspaceRoutes | Workspace:Execute | TenantAdmin | caller tenant/user (handler) | typed JSON body | F-16 Fixed |
| POST | `/api/v1/workspace/vessels/{vesselId}/directory` | WorkspaceRoutes | Workspace:Create | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| POST | `/api/v1/workspace/vessels/{vesselId}/rename` | WorkspaceRoutes | Workspace:Create | Authenticated | caller tenant/user (handler) | typed JSON body | - |
| DELETE | `/api/v1/workspace/vessels/{vesselId}/entry` | WorkspaceRoutes | Workspace:Delete | Authenticated | caller tenant/user (handler) | path + query | - |
| GET | `/api/v1/workspace/vessels/{vesselId}/search` | WorkspaceRoutes | Workspace:Read | Authenticated | caller tenant/user (handler) | path + query | - |
| GET | `/api/v1/workspace/vessels/{vesselId}/changes` | WorkspaceRoutes | Workspace:Read | Authenticated | caller tenant/user (handler) | path | - |
| GET | `/api/v1/workspace/vessels/{vesselId}/status` | WorkspaceRoutes | Workspace:Read | Authenticated | caller tenant/user (handler) | path | - |
## MCP tools

Generated from `McpToolAuthorizationRegistry` (146 tools). Authentication: credential (`Authorization: Bearer`,
`X-Token`, `X-Api-Key`) or, on a loopback-bound listener with `Mcp.AllowUnauthenticatedLoopback`, no credential
(acts as the default tenant's tenant admin). Input: every tool deserializes its arguments into a typed `*Args` class.
"Caller scoping" comes from a static scan of each handler for a caller-context check; "no caller check found (by id)"
means the handler reads or writes the entity by id in any tenant (O-01).

| Tool | Requirement | Level | Caller scoping | Findings |
|------|-------------|-------|----------------|----------|
| `enumerate` | All:Read | Authenticated | caller tenant/user | - |
| `status` | Status:Read | Authenticated | no caller check found (by id) | O-01 Open |
| `get_backlog_item` | Objective:Read | Authenticated | caller tenant/user | - |
| `get_backlog_planning_session` | PlanningSession:Read | Authenticated | caller tenant/user | - |
| `get_backlog_refinement_session` | ObjectiveRefinementSession:Read | Authenticated | caller tenant/user | - |
| `get_captain_log` | Captain:Read | Authenticated | no caller check found (by id) | O-01 Open |
| `get_captain_tools` | Captain:Read | Authenticated | no caller check found (by id) | O-01 Open |
| `get_captain` | Captain:Read | Authenticated | no caller check found (by id) | O-01 Open |
| `get_check_run` | CheckRun:Read | Authenticated | no caller check found (by id) | O-01 Open |
| `get_deployment` | Deployment:Read | Authenticated | caller tenant/user | - |
| `get_dock` | Dock:Read | Authenticated | no caller check found (by id) | O-01 Open |
| `get_fleet` | Fleet:Read | Authenticated | caller tenant/user | - |
| `get_harbor` | Harbor:Read | Authenticated | caller tenant/user | - |
| `get_memory` | Memory:Read | Authenticated | caller tenant/user | - |
| `get_merge_entry` | MergeQueue:Read | Authenticated | no caller check found (by id) | O-01 Open |
| `get_mission_diff` | Mission:Read | Authenticated | no caller check found (by id) | O-01 Open |
| `get_mission_log` | Mission:Read | Authenticated | no caller check found (by id) | O-01 Open |
| `get_model_endpoint` | ModelEndpoint:Read | Authenticated | caller tenant/user | - |
| `get_objective` | Objective:Read | Authenticated | caller tenant/user | - |
| `get_persona` | Persona:Read | Authenticated | no caller check found (by id) | O-01 Open |
| `get_pipeline` | Pipeline:Read | Authenticated | no caller check found (by id) | O-01 Open |
| `get_playbook` | Playbook:Read | Authenticated | no caller check found (by id) | O-01 Open |
| `get_prompt_template` | PromptTemplate:Read | Authenticated | no caller check found (by id) | O-01 Open |
| `get_release` | Release:Read | Authenticated | caller tenant/user | - |
| `get_runbook_execution` | Runbook:Read | Authenticated | caller tenant/user | - |
| `get_runbook` | Runbook:Read | Authenticated | caller tenant/user | - |
| `get_vessel` | Vessel:Read | Authenticated | no caller check found (by id) | O-01 Open |
| `inbox` | Inbox:Read | Authenticated | caller tenant/user | - |
| `list_backlog_refinement_sessions` | ObjectiveRefinementSession:Read | Authenticated | caller tenant/user | - |
| `list_backlog` | Objective:Read | Authenticated | caller tenant/user | - |
| `list_objectives` | Objective:Read | Authenticated | caller tenant/user | - |
| `list_prompt_templates` | PromptTemplate:Read | Authenticated | no caller check found (by id) | O-01 Open |
| `mission_status` | Mission:Read | Authenticated | no caller check found (by id) | O-01 Open |
| `papercut_summary` | Mission:Read | Authenticated | no caller check found (by id) | O-01 Open |
| `search_memory` | Memory:Read | Authenticated | caller tenant/user | - |
| `token_usage_summary` | TokenUsage:Read | Authenticated | no caller check found (by id) | O-01 Open |
| `vessel_health` | VesselHealth:Read | Authenticated | caller tenant/user | - |
| `voyage_status` | Voyage:Read | Authenticated | no caller check found (by id) | O-01 Open |
| `fleet_action_run_status` | FleetActionRun:Read | Authenticated | caller tenant/user | - |
| `evaluate_autoland` | Mission:Read | Authenticated | no caller check found (by id) | O-01 Open |
| `create_memory` | Memory:Create | Authenticated | caller tenant/user | - |
| `update_memory` | Memory:Update | Authenticated | caller tenant/user | - |
| `delete_memory` | Memory:Delete | Authenticated | caller tenant/user | - |
| `create_model_endpoint` | ModelEndpoint:Create | Authenticated | caller tenant/user | - |
| `update_model_endpoint` | ModelEndpoint:Update | Authenticated | caller tenant/user | - |
| `delete_model_endpoint` | ModelEndpoint:Delete | Authenticated | caller tenant/user | - |
| `validate_model_endpoint` | ModelEndpoint:Execute | Authenticated | caller tenant/user | - |
| `health_check_model_endpoints` | ModelEndpoint:Execute | Authenticated | no caller check found (by id) | O-01 Open |
| `create_harbor` | Harbor:Create | Authenticated | caller tenant/user | - |
| `update_harbor` | Harbor:Update | Authenticated | caller tenant/user | - |
| `delete_harbor` | Harbor:Delete | Authenticated | caller tenant/user | - |
| `set_harbor_enabled` | Harbor:Update | Authenticated | caller tenant/user | - |
| `add_vessel` | Vessel:Create | TenantAdmin | caller tenant/user | - |
| `update_vessel` | Vessel:Update | TenantAdmin | no caller check found (by id) | O-01 Open |
| `delete_vessel` | Vessel:Delete | TenantAdmin | no caller check found (by id) | O-01 Open |
| `delete_vessels` | Vessel:Delete | TenantAdmin | no caller check found (by id) | O-01 Open |
| `update_vessel_context` | Vessel:Update | TenantAdmin | caller tenant/user | - |
| `create_fleet` | Fleet:Create | TenantAdmin | caller tenant/user | - |
| `update_fleet` | Fleet:Update | TenantAdmin | caller tenant/user | - |
| `delete_fleet` | Fleet:Delete | TenantAdmin | caller tenant/user | - |
| `delete_fleets` | Fleet:Delete | TenantAdmin | caller tenant/user | - |
| `create_captain` | Captain:Create | TenantAdmin | caller tenant/user | - |
| `update_captain` | Captain:Update | TenantAdmin | no caller check found (by id) | O-01 Open |
| `delete_captain` | Captain:Delete | TenantAdmin | no caller check found (by id) | O-01 Open |
| `delete_captains` | Captain:Delete | TenantAdmin | no caller check found (by id) | O-01 Open |
| `stop_captain` | Captain:Execute | TenantAdmin | no caller check found (by id) | O-01 Open |
| `stop_all` | Captain:Execute | TenantAdmin | no caller check found (by id) | O-01 Open |
| `release_captain` | Captain:Execute | TenantAdmin | no caller check found (by id) | O-01 Open |
| `dispatch` | Voyage:Execute | TenantAdmin | caller tenant/user | - |
| `create_mission` | Mission:Create | TenantAdmin | caller tenant/user | - |
| `update_mission` | Mission:Update | TenantAdmin | no caller check found (by id) | O-01 Open |
| `cancel_mission` | Mission:Execute | TenantAdmin | no caller check found (by id) | O-01 Open |
| `restart_mission` | Mission:Execute | TenantAdmin | no caller check found (by id) | O-01 Open |
| `retry_landing` | Mission:Execute | TenantAdmin | no caller check found (by id) | O-01 Open |
| `transition_mission_status` | Mission:Update | TenantAdmin | no caller check found (by id) | O-01 Open |
| `delete_missions` | Mission:Delete | TenantAdmin | no caller check found (by id) | O-01 Open |
| `purge_mission` | Mission:Delete | TenantAdmin | no caller check found (by id) | O-01 Open |
| `cancel_voyage` | Voyage:Execute | TenantAdmin | no caller check found (by id) | O-01 Open |
| `delete_voyages` | Voyage:Delete | TenantAdmin | no caller check found (by id) | O-01 Open |
| `purge_voyage` | Voyage:Delete | TenantAdmin | no caller check found (by id) | O-01 Open |
| `delete_dock` | Dock:Delete | TenantAdmin | no caller check found (by id) | O-01 Open |
| `delete_docks` | Dock:Delete | TenantAdmin | no caller check found (by id) | O-01 Open |
| `purge_dock` | Dock:Delete | TenantAdmin | no caller check found (by id) | O-01 Open |
| `repair_dock` | Dock:Execute | TenantAdmin | no caller check found (by id) | O-01 Open |
| `unstick_dock` | Dock:Execute | TenantAdmin | no caller check found (by id) | O-01 Open |
| `send_signal` | Signal:Execute | TenantAdmin | caller tenant/user | - |
| `delete_signals` | Signal:Delete | TenantAdmin | no caller check found (by id) | O-01 Open |
| `delete_event` | Event:Delete | TenantAdmin | caller tenant/user | F-19 Fixed |
| `delete_events` | Event:Delete | TenantAdmin | caller tenant/user | F-19 Fixed |
| `enqueue_merge` | MergeQueue:Create | TenantAdmin | caller tenant/user | - |
| `cancel_merge` | MergeQueue:Execute | TenantAdmin | no caller check found (by id) | O-01 Open |
| `delete_merge` | MergeQueue:Delete | TenantAdmin | no caller check found (by id) | O-01 Open |
| `process_merge_entry` | MergeQueue:Execute | TenantAdmin | no caller check found (by id) | O-01 Open |
| `process_merge_queue` | MergeQueue:Execute | TenantAdmin | no caller check found (by id) | O-01 Open |
| `purge_merge_entries` | MergeQueue:Delete | TenantAdmin | no caller check found (by id) | O-01 Open |
| `purge_merge_entry` | MergeQueue:Delete | TenantAdmin | no caller check found (by id) | O-01 Open |
| `purge_merge_queue` | MergeQueue:Delete | TenantAdmin | no caller check found (by id) | O-01 Open |
| `create_objective` | Objective:Create | TenantAdmin | caller tenant/user | - |
| `update_objective` | Objective:Update | TenantAdmin | caller tenant/user | - |
| `delete_objective` | Objective:Delete | TenantAdmin | caller tenant/user | - |
| `reorder_objectives` | Objective:Update | TenantAdmin | caller tenant/user | - |
| `create_backlog_item` | Objective:Create | TenantAdmin | caller tenant/user | - |
| `update_backlog_item` | Objective:Update | TenantAdmin | caller tenant/user | - |
| `delete_backlog_item` | Objective:Delete | TenantAdmin | caller tenant/user | - |
| `reorder_backlog_items` | Objective:Update | TenantAdmin | caller tenant/user | - |
| `create_backlog_planning_session` | PlanningSession:Create | TenantAdmin | caller tenant/user | - |
| `dispatch_backlog_planning_session` | PlanningSession:Execute | TenantAdmin | caller tenant/user | - |
| `create_backlog_refinement_session` | ObjectiveRefinementSession:Create | TenantAdmin | caller tenant/user | - |
| `send_backlog_refinement_message` | ObjectiveRefinementSession:Execute | TenantAdmin | caller tenant/user | - |
| `stop_backlog_refinement_session` | ObjectiveRefinementSession:Execute | TenantAdmin | caller tenant/user | - |
| `summarize_backlog_refinement_session` | ObjectiveRefinementSession:Execute | TenantAdmin | caller tenant/user | - |
| `apply_backlog_refinement_summary` | ObjectiveRefinementSession:Execute | TenantAdmin | caller tenant/user | - |
| `create_playbook` | Playbook:Create | TenantAdmin | caller tenant/user | - |
| `update_playbook` | Playbook:Update | TenantAdmin | no caller check found (by id) | O-01 Open |
| `delete_playbook` | Playbook:Delete | TenantAdmin | no caller check found (by id) | O-01 Open |
| `create_persona` | Persona:Create | TenantAdmin | caller tenant/user | - |
| `update_persona` | Persona:Update | TenantAdmin | no caller check found (by id) | O-01 Open |
| `delete_persona` | Persona:Delete | TenantAdmin | no caller check found (by id) | O-01 Open |
| `create_pipeline` | Pipeline:Create | TenantAdmin | caller tenant/user | - |
| `update_pipeline` | Pipeline:Update | TenantAdmin | no caller check found (by id) | O-01 Open |
| `delete_pipeline` | Pipeline:Delete | TenantAdmin | no caller check found (by id) | O-01 Open |
| `create_prompt_template` | PromptTemplate:Create | TenantAdmin | no caller check found (by id) | O-01 Open |
| `update_prompt_template` | PromptTemplate:Update | TenantAdmin | no caller check found (by id) | O-01 Open |
| `reset_prompt_template` | PromptTemplate:Execute | TenantAdmin | no caller check found (by id) | O-01 Open |
| `create_release` | Release:Create | TenantAdmin | caller tenant/user | - |
| `create_deployment` | Deployment:Create | TenantAdmin | caller tenant/user | - |
| `approve_deployment` | Deployment:Execute | TenantAdmin | caller tenant/user | - |
| `verify_deployment` | Deployment:Execute | TenantAdmin | caller tenant/user | - |
| `rollback_deployment` | Deployment:Execute | TenantAdmin | caller tenant/user | - |
| `start_runbook_execution` | Runbook:Execute | TenantAdmin | caller tenant/user | - |
| `run_check` | CheckRun:Execute | TenantAdmin | caller tenant/user | F-04 Fixed |
| `retry_check_run` | CheckRun:Execute | TenantAdmin | caller tenant/user | F-04 Fixed |
| `create_fleet_action` | FleetAction:Create | TenantAdmin | caller tenant/user | - |
| `update_fleet_action` | FleetAction:Update | TenantAdmin | caller tenant/user | - |
| `delete_fleet_action` | FleetAction:Delete | TenantAdmin | caller tenant/user | - |
| `run_fleet_action` | FleetAction:Execute | TenantAdmin | caller tenant/user | F-16 Fixed; O-08 |
| `cancel_fleet_action_run` | FleetActionRun:Execute | TenantAdmin | caller tenant/user | - |
| `discover_vessels` | VesselImport:Execute | TenantAdmin | caller tenant/user | - |
| `import_vessels` | VesselImport:Execute | TenantAdmin | caller tenant/user | - |
| `categorize_vessel_import` | VesselImport:Execute | TenantAdmin | caller tenant/user | - |
| `apply_fleet_recommendations` | VesselImport:Execute | TenantAdmin | caller tenant/user | - |
| `evaluate_vessel_health` | VesselHealth:Execute | TenantAdmin | caller tenant/user | - |
| `set_vessel_health_override` | VesselHealth:Update | TenantAdmin | caller tenant/user | - |
| `backup` | Backup:Admin | AdminOnly | server-wide (admin) | F-03 Fixed; O-12 |
| `restore` | Backup:Admin | AdminOnly | server-wide (admin) | F-03 Fixed; O-12 |
| `stop_server` | Server:Admin | AdminOnly | server-wide (admin) | F-03 Fixed; O-12 |
## WebSocket routes and commands

The dashboard socket is `/ws` on the REST port. The upgrade is authenticated in PreRouting before the handshake
(401 otherwise) with a session token as `?token=` or in `Sec-WebSocket-Protocol` (`armada-token.<base64url>`), or
the REST credential headers; Ask thread tokens are refused. Query strings are no longer written to request logs
(F-15).

| Route / command | Requirement | Tenant scoping | Input | Findings |
|-----------------|-------------|----------------|-------|----------|
| upgrade `/ws` | Authenticated | n/a | token in query, subprotocol, or headers | F-15 Fixed |
| route `subscribe` (optional `allTenants`) | Authenticated; `allTenants` honored for global admins only | Events: entity's tenant only; status snapshot is server-wide | typed message | O-03 Open |
| route `command` | Global admin (every command) | None (global admin acts on any tenant by id) | typed message, `action` + `data` | - |
| commands `status`, `stop_captain`, `stop_all`, `stop_server` | Global admin | server-wide | typed | - |
| commands `list_fleets`, `get_fleet`, `create_fleet`, `update_fleet`, `delete_fleet` | Global admin | any tenant by id | typed | - |
| commands `list_vessels`, `get_vessel`, `create_vessel`, `update_vessel`, `update_vessel_context`, `delete_vessel` | Global admin | any tenant by id | typed | F-11 Fixed (delete), O-07 Open |
| commands `list_voyages`, `get_voyage`, `create_voyage`, `cancel_voyage`, `purge_voyage` | Global admin | any tenant by id | typed | - |
| commands `list_missions`, `list_missions_summary`, `get_mission`, `create_mission`, `update_mission`, `transition_mission_status`, `cancel_mission`, `purge_mission`, `restart_mission`, `get_mission_diff`, `get_mission_log` | Global admin | any tenant by id | typed | - |
| commands `list_captains`, `get_captain`, `create_captain`, `update_captain`, `delete_captain`, `get_captain_log` | Global admin | any tenant by id | typed | - |
| commands `list_signals`, `send_signal`, `list_events` | Global admin | any tenant | typed | - |
| commands `list_docks`, `list_merge_queue`, `get_merge_entry`, `enqueue_merge`, `cancel_merge`, `process_merge_queue` | Global admin | any tenant by id | typed | - |
| command `enumerate` (fleets, vessels, captains, missions, voyages, docks, signals, events, merge_queue) | Global admin | all tenants | typed query | - |
| commands `backup`, `restore` | Global admin | server-wide; arbitrary host paths | typed | O-12 Accepted |
| commands `get_persona`, `create_persona`, `update_persona`, `delete_persona`, `get_prompt_template`, `update_prompt_template`, `get_pipeline`, `create_pipeline`, `update_pipeline`, `delete_pipeline` | Global admin | any tenant by name | typed | - |
| server broadcasts (mission, voyage, captain, check-run, objective, deployment, incident, runbook-execution, approval-needed, Ask `ask.*`) | n/a | entity's tenant; `ask.*` to the owning user only | n/a | O-13 Open (unscrubbed output) |

## Other entry points

| Entry point | Authentication | Authorization | Tenant scoping | Input validation | Findings |
|-------------|----------------|---------------|----------------|------------------|----------|
| Harbor link (WebSocket at `Harbor.LinkPath`, default `/v1.0/harbor/connect`) | `x-access-key` (or `Authorization`) validated as an Armada credential; no credential only from loopback to a loopback-bound Admiral with `Harbor.RequireAuth` off | Any valid credential may register a Harbor for its own tenant and user; a global admin may name a tenant (`x-tenant-guid`) | Harbor registered under the credential's tenant and user | First message must be a handshake; messages parsed by `HarborProtocol` (malformed dropped) | F-05 Fixed; O-04 Open |
| Server to Harbor messages (`launch`, `git`, `kill`, `stdin`, `deferredLaunch`) | Link already authenticated | Routed only to Harbors eligible for the mission's user or tenant | Harbor ownership | `git` carries an executable and arguments run on the Harbor host | O-04 Open |
| Remote tunnel (Admiral dials `RemoteControl.TunnelUrl`) | Admiral proves the shared tunnel password (SHA-256 challenge with timestamp and nonce) and optional enrollment token | n/a | One instance id per Admiral | Envelope deserialization | O-11 Open (default password, `AllowInvalidCertificates`) |
| Dashboard relay through the tunnel (`RemoteDashboardRelayService`) | Relayed requests replay to the loopback REST port with the browser's own `Authorization`, `X-Token`, `X-Api-Key`; cookies and proxy session headers stripped | Normal REST authorization (F-01 closes the unauthenticated server-control routes the relay used to reach) | Normal REST scoping | Only `/api/v1/*` and `/ws` are relayed | F-01 Fixed |
| Armada.Proxy listener (port 7893): `/proxy-api/v1/auth/*`, `/proxy-api/v1/instances`, `/session/*`, `/tunnel`, browser `/ws`, `/api/v1/*` relay, static files | Shared proxy password (challenge-response, in-memory session cookie); `/tunnel` by tunnel password proof; `/instances` and health are public | Proxy route policy blocks a few paths by exact match; Armada credentials still required on the Admiral | None at the proxy (no per-user identity) | Exact-string route blocks | O-11 Open |
| `armada mcp stdio` (Helm) | None (local process) | Same tool handlers; no MCP authorization wrapper | Default tenant | Typed tool args | O-18 Accepted |
| Static files: `/`, `/dashboard/*`, `/assets/*`, `/img/*` | None | None (no data) | n/a | Embedded files only | - |
| `/openapi.json`, `/swagger` | None | NoAuthRequired | n/a | n/a | O-10 Accepted |
| MCP health (`GET /` on the MCP port) | None | n/a | n/a | n/a | - |
| Vessel import browse and discover (`GET /vessels/import/browse`, `POST /vessels/import/discover`, MCP `discover_vessels`) | Credential | TenantAdmin | Allowed roots from `Import.AllowedRoots`, default the Admiral user's home | Absolute paths, normalized, prefix-checked; reparse points skipped in listings; runs `git remote get-url` and `git symbolic-ref` | O-07 Open |
| Vessel import and categorization (`POST /vessels/import`, batch categorize and apply, MCP equivalents) | Credential | TenantAdmin | Caller tenant | Typed bodies | O-07 Open |
| Fleet action Command runs (`/bin/sh -s` or PowerShell locally, `/bin/sh -c` or PowerShell on a Harbor) | Credential | TenantAdmin (route and service) | Caller tenant | Command text from the action; template variables unescaped | F-16 Fixed (audit); O-08 Open |
| Workspace file operations (tree, file read and write, directory, rename, delete, search, diff, changes) | Credential | Authenticated, vessel visibility | Vessel visible to the caller (owner or tenant admin) | Path guard: full-path normalization, root prefix, no `.git`, reparse points refused on every segment | - |
| Workspace exec (`/bin/sh -c` or `cmd.exe /c` in the vessel working tree, 1-600 s) | Credential | TenantAdmin | Vessel visible to the caller | Command text | F-16 Fixed (audit) |
| Check runs (workflow profile or `CommandOverride`, `/bin/sh -c` or `cmd.exe /c`) | Credential | TenantAdmin | Vessel visible to the caller | Typed request | F-04, F-16 Fixed |
| Deployment, verification, rollback commands (through check runs) and environment health probes (HTTP) | Credential | TenantAdmin | Caller tenant | Commands from workflow profiles; URLs from environments | F-16 Fixed (audit); O-09 Open |
| Definition-of-done build and test commands (landing) | n/a (system, configured by a tenant admin on the vessel) | TenantAdmin to configure | Vessel tenant | Commands from the vessel | - |
| Merge queue test command (`/bin/sh`) | Credential (to enqueue or process) | TenantAdmin | Caller tenant | Command from the entry or settings | F-16 Fixed (audit) |
| Vessel health evaluation (`dotnet list package`, `npm outdated`/`audit`, git in the working tree) | Credential | TenantAdmin | Caller tenant | Fixed commands; repository-controlled MSBuild props can run during `dotnet list` | O-07 Open (untrusted repositories) |
| Docks and git (provision, worktrees, push, merge, purge, repair) | Credential | TenantAdmin for writes | Caller tenant | Branch names passed as git arguments (ArgumentList) | F-11 Fixed |
| Vessel delete (bare repository and dock directory removal) | Credential | TenantAdmin | Caller tenant | Managed-root guard | F-11 Fixed |
| Self-rebuild (`POST /server/rebuild`: git worktree, `dotnet publish`, `npm run build`, cutover), restart, rollback | Credential | AdminOnly | Server-wide | `SourcePath` must exist; `Ref` passed to git | F-01 Fixed |
| Backup and restore (REST download and upload; MCP and WebSocket with host paths) | Credential | AdminOnly | Server-wide (backups include `settings.json` with secrets) | Restore replaces the database and settings from the zip | F-03 Fixed; O-12 Accepted |
| Factory reset (`POST /server/reset`: deletes logs, docks, repos, and the database) | Credential | AdminOnly | Server-wide | none | - |
| Status doctor (`GET /doctor`: `git --version`, `command -v` for fixed names) | Credential | Authenticated | Server-wide | Fixed inputs | - |
| Captain processes (mission, chat, planning, Ask turns) | n/a (launched by the Admiral) | Launching requires the route's level (dispatch TenantAdmin; Ask Authenticated) | Captain and mission tenant | Prompt text; runtime flags per [Running agents safely](#running-agents-safely) | F-23 Mitigated; O-02 Open |

## Verification

- `E2E.AuthorizationCoverage`: every registered route and tool is declared, nothing stale is declared, concrete paths
  resolve to their own template, undeclared paths fail closed, sensitive levels are pinned, and unauthenticated
  server control calls get 401.
- `E2E.SecurityDefaults`: loopback MCP without credentials works; invalid credentials get 401; the loopback opt-out
  setting; non-loopback MCP requires a credential; non-loopback start is refused with default credentials and allowed
  with the override; the full forced password change flow including `Bearer default` retirement; system identity
  password login refused; admin-only MCP tools refused for the loopback default caller.
- `E2E.SecretsAndAudit`: seeded secrets absent from responses, request history, and logs; workspace exec audited;
  audit events protected; tenant admin cannot take over a global admin.
- `Services.SecurityHardening`: runtime flags with auto-approve off, managed-path guard, secret key detection,
  credential redaction, default password detection, loopback hostname detection.
