# Changelog

All notable changes to Armada are documented in this file.

---

## v0.6.0

Focus: remote access.

### Remote Access
- Added an experimental outbound remote-control tunnel foundation in `Armada.Server`
- New `RemoteControl` settings are persisted in `settings.json` and exposed through `GET/PUT /api/v1/settings`
- Health and status responses now expose `RemoteTunnel` telemetry including state, instance ID, latency, and last error
- React dashboard, legacy dashboard, and `armada status` now surface remote tunnel configuration and live state
- Added request/response handling and server event forwarding on the tunnel contract
- Added `Armada.Proxy` with websocket tunnel termination, instance summaries, recent-event inspection, and live `armada.status.snapshot` / `armada.status.health` forwarding
- Added focused tunnel-backed remote inspection routes for recent activity, missions, voyages, captains, logs, and diffs
- Added bounded tunnel-backed management routes for fleets, vessels, voyages, missions, and captain stop
- Added a proxy-hosted remote operations shell at `/` for mobile-first remote triage, fleet and vessel management, voyage dispatch, mission editing, and captain control
- Added `docs/TUNNEL_PROTOCOL.md`, `docs/PROXY_API.md`, and `docs/TUNNEL_OPERATIONS.md` for the shipped tunnel and proxy contract

### Release and Docs
- Updated shared release metadata, docker tags, Postman examples, REST docs, MCP docs, and WebSocket docs to `v0.6.0`
- Added no-op `v0.5.0 -> v0.6.0` migration scripts to reflect the release even though no database schema change is required

---

## v0.5.0

Focus: dispatch and pipeline stability.

### Dispatch and Pipeline Stability
- Hardened architect-to-worker handoff behavior, mission status freshness, branch cleanup, worktree cleanup, and landing paths
- Improved mission and voyage telemetry so active work reports current state more reliably
- Tightened dock/worktree safety to prevent dirty fresh docks and stale branch leakage

### Captains and Runtime Selection
- Added optional `Model` on captains across SQLite, MySQL, PostgreSQL, and SQL Server
- Captain model selection is exposed through dashboard, REST, MCP, and Postman examples
- Runtime launches now pass the configured model where supported, otherwise the runtime chooses its default
- Captain create/update now validates configured models before saving and returns a user-facing error when the model is invalid or unavailable

### Missions and Pipeline Reliability
- Added `TotalRuntimeMs` on missions, surfaced in API responses and the mission detail dashboard
- Mission create/update now touch parent voyage `LastUpdateUtc` so active voyages report fresh status
- Architect handoff text now strips trailing `[ARMADA:*]` control markers before passing instructions downstream
- Worktree creation now fails fast if a fresh dock is dirty, preventing unrelated tracked-file contamination
- Dock and mission branch cleanup was hardened across no-op landing and published-server worktree reclamation paths

### Git and Landing
- Worktree branch creation now creates the branch ref before attaching the worktree and keeps existing-branch docks on the named branch
- Merge handling now retries with `--allow-unrelated-histories` when needed
- Diff capture now falls back cleanly when there is no merge base instead of producing an empty snapshot
- Architect-only branches are cleaned up after successful fan-out instead of lingering indefinitely

### Dashboard and Docs
- Captain detail now supports editing and displaying the configured model
- Mission detail now uses a four-column layout and shows total runtime
- Login secret inputs now support a press-and-hold reveal control
- Dispatch page no longer shows the redundant detected-task UI or stale task-splitting guidance
- README, REST API, MCP API, compose.yaml, and release metadata are updated for `v0.5.0`

---

## v0.4.0

### Personas and Pipelines
- Added personas: named agent roles (Worker, Architect, Judge, TestEngineer) with custom persona support
- Added pipelines: ordered sequences of persona stages (WorkerOnly, Reviewed, Tested, FullPipeline) with custom pipeline support
- Pipeline resolution: dispatch param > vessel default > fleet default > WorkerOnly
- Architect stage special handling: parses [ARMADA:MISSION] markers to create multiple Worker missions
- Stage handoff: injects prior stage output (agent stdout + diff) into next stage description
- Persona-aware captain routing: AllowedPersonas and PreferredPersona on captains
- Mission dependency chain: DependsOnMissionId gates assignment until predecessor completes

### Prompt Templates
- Every prompt is now template-driven and user-editable (18 built-in templates)
- Categories: mission, persona, structure, commit, landing, agent
- Dashboard two-column editor with parameter reference panel
- MCP tools: armada_get/update/reset_prompt_template
- REST endpoints: /api/v1/prompt-templates CRUD

### Dashboard
- Personas, Pipelines, Prompt Templates pages
- Pipeline dropdown on Dispatch, Voyage Create, Vessel, Fleet
- Mission detail: persona badge, depends-on link, failure reason display
- Captain detail: AllowedPersonas, PreferredPersona fields
- Vessel edit: 95% width, 3-column layout
- Log viewer: LIVE/DONE indicators, follow mode
- Toast notifications instead of layout-shifting banners
- Consistent CopyButton component across all pages
- Version display on login and sidebar

### Infrastructure
- Schema migrations 19-23 across SQLite, MySQL, PostgreSQL, SQL Server
- FailureReason field on missions (surfaced in dashboard)
- Vessel deletion cleanup: cancels missions, deletes docks, bare repo
- Empty repo auto-seed: creates README.md on first dispatch to empty GitHub repo
- Process.Dispose on agent exit to release Windows directory handles
- Crash logging: AppDomain.UnhandledException + TaskScheduler.UnobservedTaskException
- Case-insensitive email login
- CLAUDE.md auto-gitignored in worktrees

### API
- 11 new MCP tools (persona, pipeline, prompt template CRUD)
- 17 new REST endpoints
- 12 new WebSocket commands
- armada_enumerate supports personas, prompt_templates, pipelines
- armada_dispatch accepts pipelineId and pipeline (name) parameters
- Voyage status considers LandingFailed, WorkProduced, PullRequestOpen as terminal

### Documentation
- PIPELINES.md: complete implementation reference
- PERSONAS_GUIDE.md: user-facing guide
- TESTING_PIPELINES.md: 6 end-to-end test examples
- OLLAMA_AS_CAPTAIN.md: implementation plan for Ollama runtime
- VLLM_AS_CAPTAIN.md: implementation plan for vLLM runtime

---

## v0.3.0

### Added

- **Multi-tenant support** -- all operational data (fleets, vessels, captains, missions, voyages, docks, signals, events, merge entries) is scoped by tenant
- **Tenant, user, and credential models** -- `TenantMetadata` (`ten_` prefix), `UserMaster` (`usr_` prefix), `Credential` (`crd_` prefix)
- **Bearer token authentication** -- 64-character random alphanumeric tokens linked to a specific tenant and user, sent via `Authorization: Bearer <token>` header
- **Encrypted session tokens** -- AES-256-CBC self-contained tokens with 24-hour lifetime, sent via `X-Token` header. No server-side session storage required
- **Authentication endpoints** -- `POST /api/v1/authenticate`, `GET /api/v1/whoami`, `POST /api/v1/tenants/lookup`
- **Onboarding endpoint** -- `POST /api/v1/onboarding` for self-registration (gated by `AllowSelfRegistration` setting)
- **Tenant CRUD endpoints** -- `GET/POST/PUT/DELETE /api/v1/tenants` (admin only, with self-read for non-admins)
- **User CRUD endpoints** -- `GET/POST/PUT/DELETE /api/v1/users` (admin only, with self-read for non-admins)
- **Credential CRUD endpoints** -- `GET/POST/PUT/DELETE /api/v1/credentials` (admins: all; non-admins: own credentials)
- **Admin vs non-admin access patterns** -- three-tier authorization: `NoAuthRequired`, `Authenticated`, `AdminOnly`
- **Default data seeding** -- on first boot, creates default tenant, user (`admin@armada` / `password`), and credential (bearer token `default`)
- **React dashboard** -- standalone React dashboard (`Armada.Dashboard`) as a separate deployment option for Docker/production
- **Docker Compose with dashboard** -- `compose.yaml` runs `armada-server` and `armada-dashboard` containers together
- **SQL Server support** -- added SQL Server as a database backend option alongside SQLite, PostgreSQL, and MySQL
- **`AllowSelfRegistration` setting** -- controls whether `POST /api/v1/onboarding` is enabled (default: `true`)
- **`SessionTokenEncryptionKey` setting** -- AES-256 key for session token encryption (auto-generated if not provided)

### Changed

- All REST API endpoints now require authentication (except health check, authenticate, tenant lookup, onboarding, and dashboard routes)
- All operational database queries are tenant-scoped for non-admin users
- Admin users see all data across all tenants
- CORS headers now include `Authorization` and `X-Token` in `Access-Control-Allow-Headers`

### Deprecated

- **`X-Api-Key` header** -- retained for backward compatibility but deprecated. When configured, the server creates a synthetic admin tenant (`ten_system`) and user (`usr_system`). Migrate to bearer tokens for new integrations

---

## v0.2.0

### Added

- Multi-database support (SQLite, PostgreSQL, MySQL) with connection pooling
- Structured `database` object in `settings.json` replacing flat `databasePath`
- Migration scripts for v0.1.0 to v0.2.0 settings conversion
- Merge queue system for automated branch merging
- WebSocket hub for real-time event streaming
- Embedded dashboard at `/dashboard`
- MCP server with full API parity (18 tools)
- Batch delete operations for all entity types
- Enumeration (POST) endpoints with JSON body filtering
- Mission diff and log retrieval endpoints
- Captain log streaming
- Dock (worktree) management endpoints
- Signal system for Admiral-captain communication
- Event audit trail

### Changed

- Settings format: `databasePath` string replaced with `database` object (breaking change)

---

## v0.1.0

### Added

- Initial release
- Core orchestration: fleets, vessels, captains, missions, voyages
- Git worktree isolation for parallel agent work
- Multi-runtime support: Claude Code, Codex, Gemini, Cursor
- Auto-recovery for crashed agents
- REST API on port 7890
- CLI (`armada`) with Spectre.Console
- SQLite database backend
- Zero-config startup with auto-detection
