# Changelog

All notable changes to Armada are documented in this file.

---

## v0.5.0

### Added
- Per-captain model selection: captains can now specify an AI model that overrides the runtime default
- Model validation on create/update: Armada briefly starts the agent to verify the model exists
- Mission total runtime tracking: totalRuntimeSeconds field calculated at mission completion
- Database migrations for captains.model and missions.total_runtime_seconds across all four backends
- Migration script: migrate_v0.4.0_to_v0.5.0.sh / .bat

### Changed
- Dashboard: Captain detail page shows and edits Model field
- Dashboard: Mission detail page shows Total Runtime
- Dashboard: Removed duplicate task preview from dispatch page
- Bumped version to 0.5.0 across all components

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
