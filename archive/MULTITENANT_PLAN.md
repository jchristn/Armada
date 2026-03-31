# Armada Multi-Tenant Overhaul Plan

**Vessel:** `vsl_mmit8chk_PTvi4HamUKb` (Armada)
**Version Target:** 0.3.0
**Status:** Planning

This document is the actionable implementation plan for making Armada multi-tenant and scalable to many users. Each task has a checkbox for tracking progress. Tasks are ordered by dependency -- complete them top-to-bottom within each phase.

> **Future consideration (out of scope):** Roles-based access control (RBAC) and fine-grained permissioning on credentials. The data model should not preclude adding these later.

> **Out of scope:** MCP server and WebSocket hub authentication. These remain unauthenticated for now.

---

## Table of Contents

- [Phase 0: Authorization Matrix](#phase-0-authorization-matrix)
- [Phase 1: Core Data Model](#phase-1-core-data-model)
- [Phase 2: Database Layer](#phase-2-database-layer)
- [Phase 3: Authentication and Authorization](#phase-3-authentication-and-authorization)
- [Phase 4: API Layer - New Endpoints](#phase-4-api-layer---new-endpoints)
- [Phase 5: API Layer - Tenant Fencing on Existing Endpoints](#phase-5-api-layer---tenant-fencing-on-existing-endpoints)
- [Phase 6: Dashboard - React App](#phase-6-dashboard---react-app)
- [Phase 7: Docker](#phase-7-docker)
- [Phase 8: Test Projects](#phase-8-test-projects)
- [Phase 9: Documentation](#phase-9-documentation)

---

## Design Decisions

### Tenant Isolation Model

- Every operational database row (fleets, vessels, captains, missions, voyages, docks, signals, events, merge entries) is fenced by `tenant_id` only
- `UserId` is NOT added to operational tables — Armada is an orchestrator, not a personal-workspace app; users within a tenant collaborate on shared operational data
- `UserId` exists only on identity-owned records: `UserMaster`, `Credential`
- A non-admin authenticated user can see and modify all entities within their tenant
- Admin users (`IsAdmin = true`) can see all data across all tenants
- The same email address can exist in multiple tenants (separate `UserMaster` rows)
- Within a single tenant, an email address must be unique

### Authentication Model

- **Bearer tokens** (in `Credential` objects): the canonical auth mechanism for all API access. Sent via `Authorization: Bearer {token}` header. A credential lookup resolves to `AuthContext(TenantId, UserId, IsAdmin)`
- **Self-contained encrypted session tokens**: 24-hour lifetime, for interactive/dashboard use. Sent via `X-Token` header. AES-256-CBC encrypted, containing `TenantId + UserId + ExpiresUtc` — no server-side storage needed (survives restarts, no cleanup timer). Validated by decryption, not dictionary lookup. Modeled after LiteGraph's `SecurityToken` pattern
- Both token types resolve to the same `AuthContext(TenantId, UserId, IsAdmin)` through a single `AuthenticationService.Authenticate()` method
- **`X-Api-Key` is deprecated.** If a settings-level admin API key is configured, the server generates a synthetic admin tenant (`ten_system`) and user (`usr_system`) with `IsAdmin = true` on startup. The `X-Api-Key` header resolves to this synthetic identity through the same `AuthContext` path as all other auth methods. This eliminates special-casing throughout the codebase

### Authorization Tiers

Three authorization levels (modeled after Conductor's `AuthorizationConfig`):

| Level | Description |
|-------|-------------|
| `NoAuthRequired` | Health check, tenant lookup, onboarding |
| `Authenticated` | All operational CRUD within caller's tenant |
| `AdminOnly` | Tenant/user/credential management (with self-read exceptions) |

### Single-User Workflow Preservation

**Design principle: Zero-config single-user mode.** Today a user runs `armada` and gets a working server with embedded dashboard. Post-multi-tenancy, this must remain nearly as simple:

- **On first boot with no settings file**: Server creates `~/.armada/settings.json` with defaults, initializes SQLite at `~/.armada/armada.db`, seeds `default` tenant + `default` user (admin, `admin@armada` / `password`) + `default` credential (bearer token `default`)
- **The embedded dashboard stays.** Do NOT remove the embedded `wwwroot` dashboard from `Armada.Server`. The React dashboard (Phase 6) is an **additional** deployment option for Docker/production, not a replacement. Single-user mode keeps the embedded dashboard at `http://localhost:7890/dashboard` exactly as today
- **Embedded dashboard gets the login flow** — add the tenant lookup → password → session token flow to the existing embedded JS dashboard. This is lighter than requiring a separate React app for basic use
- **`X-Api-Key` continues to work** — the synthetic admin identity maps it seamlessly. Existing scripts/integrations don't break
- **Bearer token `default` works out of the box** — `Authorization: Bearer default` resolves to the default admin user. No credential generation needed for single-user
- **MCP and WebSocket remain unauthenticated** — no change to the agent-facing protocols
- **`AllowSelfRegistration` defaults to `true`** — single-user operators don't need to think about it

What changes for the single user:
1. First dashboard visit shows a login screen (email: `admin@armada`, password: `password`)
2. After login, session token keeps them authenticated for 24 hours
3. Everything else works exactly as before

### SQLite as Default Backend

- SQLite is the **default database** — currently hardcoded in `ArmadaServer.cs` (line 111-112: `new SqliteDatabaseDriver(connectionString, _Logging)`)
- The server currently **bypasses `DatabaseDriverFactory` entirely**. This must be refactored as a Phase 2 prerequisite to unblock all other database backends
- SQLite uses WAL mode and a `SemaphoreSlim(1,1)` for write serialization — tenant-scoped queries must respect this existing pattern
- Default path: `~/.armada/armada.db` — single-file, zero-config, perfect for single-user workflow
- SQLite migration is v13 (existing databases at v12) — this is the **only backend with real migration concerns** for existing users

### ID Prefixes

| Entity     | Prefix  |
|------------|---------|
| Tenant     | `ten_`  |
| User       | `usr_`  |
| Credential | `crd_`  |

### Default Data (First Boot)

On startup, if no tenants exist in the database:
- Tenant: ID `default`, Name "Default Tenant"
- User: ID `default`, TenantId `default`, Email `admin@armada`, Password (SHA256 of `password`), IsAdmin `true`
- Credential: ID `default`, TenantId `default`, UserId `default`, BearerToken `default`

### Synthetic Admin Identity (API Key)

If `Settings.ApiKey` is configured:
- On startup, ensure a synthetic tenant exists: ID `ten_system`, Name "System"
- Ensure a synthetic user exists: ID `usr_system`, TenantId `ten_system`, Email `system@armada`, IsAdmin `true`
- `X-Api-Key` header authentication resolves to this synthetic user's `AuthContext`
- This synthetic tenant/user is hidden from normal enumeration (excluded from `GET /api/v1/tenants` and tenant lookup)

---

## Phase 0: Authorization Matrix

Define the authorization requirements for every API endpoint before implementation. This matrix is the single source of truth for access control decisions.

### 0.1 Authorization Configuration

- [ ] Create `src/Armada.Core/Authorization/AuthorizationConfig.cs`
  - Define an enum `PermissionLevel`: `NoAuthRequired`, `Authenticated`, `AdminOnly`
  - Create a static mapping of every API endpoint to its required `PermissionLevel`
  - Model after Conductor's `AuthorizationConfig.cs` pattern

### 0.2 Endpoint Authorization Matrix

- [ ] Document and implement the following matrix:

| Endpoint | Method | Permission | Notes |
|----------|--------|------------|-------|
| `/api/v1/status/health` | GET | NoAuthRequired | |
| `/api/v1/authenticate` | POST | NoAuthRequired | |
| `/api/v1/tenants/lookup` | POST | NoAuthRequired | Input: email, returns matching tenants only |
| `/api/v1/onboarding` | POST | NoAuthRequired | Gated by `AllowSelfRegistration` setting |
| `/api/v1/whoami` | GET | Authenticated | |
| `/api/v1/fleets` | ALL | Authenticated | Tenant-scoped |
| `/api/v1/vessels` | ALL | Authenticated | Tenant-scoped |
| `/api/v1/captains` | ALL | Authenticated | Tenant-scoped |
| `/api/v1/missions` | ALL | Authenticated | Tenant-scoped |
| `/api/v1/voyages` | ALL | Authenticated | Tenant-scoped |
| `/api/v1/docks` | ALL | Authenticated | Tenant-scoped |
| `/api/v1/signals` | ALL | Authenticated | Tenant-scoped |
| `/api/v1/events` | ALL | Authenticated | Tenant-scoped |
| `/api/v1/merge` | ALL | Authenticated | Tenant-scoped |
| `/api/v1/tenants` | GET (list) | AdminOnly | |
| `/api/v1/tenants` | POST | AdminOnly | |
| `/api/v1/tenants/{id}` | GET | Authenticated | Admin: any; non-admin: own tenant only |
| `/api/v1/tenants/{id}` | PUT/DELETE | AdminOnly | |
| `/api/v1/users` | GET (list) | AdminOnly | |
| `/api/v1/users` | POST | AdminOnly | |
| `/api/v1/users/{id}` | GET | Authenticated | Admin: any; non-admin: own user only |
| `/api/v1/users/{id}` | PUT/DELETE | AdminOnly | |
| `/api/v1/credentials` | GET (list) | Authenticated | Admin: all; non-admin: own credentials only |
| `/api/v1/credentials` | POST | Authenticated | Admin: any tenant/user; non-admin: self only |
| `/api/v1/credentials/{id}` | GET | Authenticated | Admin: any; non-admin: own only |
| `/api/v1/credentials/{id}` | PUT | AdminOnly | |
| `/api/v1/credentials/{id}` | DELETE | Authenticated | Admin: any; non-admin: own only |

### 0.3 Background Service Authorization Audit

- [ ] Audit all background services and classify each as tenant-scoped or system-scoped:
  - `DataExpiryService` — **system-scoped**: iterates across all tenants, uses admin-level unscoped queries
  - `AdmiralService.HealthCheckAsync` — **system-scoped**: health checks all captains across all tenants
  - `AdmiralService.CleanupStaleCaptainsAsync` — **system-scoped**: cleans up across all tenants
  - `MergeQueueService.ProcessQueueAsync` — **system-scoped**: processes merge entries across all tenants (entries themselves are tenant-scoped in the queue)
  - `CaptainService` lifecycle (onboard, release) — **tenant-scoped**: operates on captains belonging to a specific tenant
  - `LandingService` — **tenant-scoped**: landing operations belong to a specific tenant context
  - `VoyageService` — **tenant-scoped**: voyage operations belong to a specific tenant context
  - `MissionService` — **tenant-scoped**: mission operations belong to a specific tenant context
- [ ] Document which services run with admin context (cross-tenant) vs tenant context

---

## Phase 1: Core Data Model

New models in `src/Armada.Core/Models/`, new enums in `src/Armada.Core/Enums/`, constants in `src/Armada.Core/Constants.cs`.

### 1.1 Constants

- [ ] Add to `Constants.cs`:
  - `TenantIdPrefix = "ten_"`
  - `UserIdPrefix = "usr_"`
  - `CredentialIdPrefix = "crd_"`
  - `SessionTokenHeader = "X-Token"` (header name for session tokens)
  - `DefaultTenantId = "default"`
  - `DefaultTenantName = "Default Tenant"`
  - `DefaultUserEmail = "admin@armada"`
  - `DefaultUserPassword = "password"`
  - `DefaultUserId = "default"`
  - `DefaultCredentialId = "default"`
  - `DefaultBearerToken = "default"`
  - `SessionTokenLifetimeHours = 24`
  - `SystemTenantId = "ten_system"`
  - `SystemTenantName = "System"`
  - `SystemUserId = "usr_system"`
  - `SystemUserEmail = "system@armada"`

### 1.2 Settings Updates

- [ ] Add to settings model:
  - `AllowSelfRegistration` (bool, default true) — controls whether `POST /api/v1/onboarding` is enabled
  - `SessionTokenEncryptionKey` (string, auto-generated if not provided) — AES-256 key for session token encryption
  - Deprecation note on existing `ApiKey` setting — retained for backward compatibility, maps to synthetic admin identity

### 1.3 TenantMetadata Model

- [ ] Create `src/Armada.Core/Models/TenantMetadata.cs`
  - `Id` (string, prefix `ten_`, required, null-check on set)
  - `Name` (string, required, null-check on set, default "My Tenant")
  - `Active` (bool, default true)
  - `CreatedUtc` (DateTime, default UtcNow)
  - `LastUpdateUtc` (DateTime, default UtcNow)
  - Follow Fleet.cs patterns exactly: `#region` blocks, XML docs, private backing fields with `_PascalCase`, parameterless constructor + constructor with name

### 1.4 UserMaster Model

- [ ] Create `src/Armada.Core/Models/UserMaster.cs`
  - `Id` (string, prefix `usr_`, required, null-check on set)
  - `TenantId` (string, required, null-check on set)
  - `Email` (string, required, null-check on set)
  - `PasswordSha256` (string, required, null-check on set) — store SHA256 hex hash, never plaintext
  - `FirstName` (string?, nullable, default null)
  - `LastName` (string?, nullable, default null)
  - `IsAdmin` (bool, default false)
  - `Active` (bool, default true)
  - `CreatedUtc` (DateTime, default UtcNow)
  - `LastUpdateUtc` (DateTime, default UtcNow)
  - Static method: `string ComputePasswordHash(string plainText)` — SHA256, hex, lowercase
  - Static method: `UserMaster Redact(UserMaster user)` — returns copy with PasswordSha256 set to `"********"`
  - Instance method: `bool VerifyPassword(string plainText)` — compares hash

### 1.5 Credential Model

- [ ] Create `src/Armada.Core/Models/Credential.cs`
  - `Id` (string, prefix `crd_`, required, null-check on set)
  - `TenantId` (string, required, null-check on set)
  - `UserId` (string, required, null-check on set)
  - `Name` (string?, nullable, default null) — friendly name
  - `BearerToken` (string, required, null-check on set) — 64-char random alphanumeric, generated in default constructor
  - `Active` (bool, default true)
  - `CreatedUtc` (DateTime, default UtcNow)
  - `LastUpdateUtc` (DateTime, default UtcNow)
  - Static method or constructor helper to generate a cryptographically random 64-char bearer token

### 1.6 Add TenantId to All Existing Models

- [ ] Add `TenantId` (string?, nullable, default null) property to:
  - `Fleet.cs`
  - `Vessel.cs`
  - `Captain.cs`
  - `Mission.cs`
  - `Voyage.cs`
  - `Dock.cs`
  - `Signal.cs`
  - `ArmadaEvent.cs`
  - `MergeEntry.cs`
- Properties should be nullable to support the migration path (existing data has no tenant). XML docs on each.
- Do NOT add `UserId` to these tables — tenant-level fencing only.

### 1.7 DTO Models

- [ ] Create `src/Armada.Core/Models/AuthenticateRequest.cs`
  - `TenantId` (string?, nullable) — used when tenant is known (after tenant selection)
  - `Email` (string?, nullable)
  - `Password` (string?, nullable)
  - (If email/password null, authentication falls back to bearer token from Authorization header or session token from X-Token header)
- [ ] Create `src/Armada.Core/Models/AuthenticateResult.cs`
  - `Success` (bool)
  - `Token` (string?) — the encrypted session token
  - `ExpiresUtc` (DateTime?)
- [ ] Create `src/Armada.Core/Models/WhoAmIResult.cs`
  - `Tenant` (TenantMetadata?)
  - `User` (UserMaster?) — password redacted
- [ ] Create `src/Armada.Core/Models/TenantLookupRequest.cs`
  - `Email` (string, required)
- [ ] Create `src/Armada.Core/Models/TenantLookupResult.cs`
  - `Tenants` (List<TenantListEntry>) — empty list if email not found (don't distinguish "no user" from "no tenants")
- [ ] Create `src/Armada.Core/Models/TenantListEntry.cs`
  - `Id` (string)
  - `Name` (string)
- [ ] Create `src/Armada.Core/Models/OnboardingRequest.cs`
  - `TenantId` (string, required)
  - `Email` (string, required)
  - `Password` (string, required)
  - `FirstName` (string?, nullable)
  - `LastName` (string?, nullable)
- [ ] Create `src/Armada.Core/Models/OnboardingResult.cs`
  - `Success` (bool)
  - `Tenant` (TenantMetadata?)
  - `User` (UserMaster?) — password redacted
  - `Credential` (Credential?)
  - `ErrorMessage` (string?)

### 1.8 AuthContext Model

- [ ] Create `src/Armada.Core/Models/AuthContext.cs` (populated by auth service, passed to handlers)
  - `IsAuthenticated` (bool, default false)
  - `TenantId` (string?, nullable)
  - `UserId` (string?, nullable)
  - `IsAdmin` (bool, default false)
  - `AuthMethod` (string?) — "Bearer", "Session", "ApiKey", or null

---

## Phase 2: Database Layer

### 2.1 New Database Interfaces

- [ ] Create `src/Armada.Core/Database/Interfaces/ITenantMethods.cs`
  - `CreateAsync(TenantMetadata tenant, CancellationToken token = default) -> Task<TenantMetadata>`
  - `ReadAsync(string id, CancellationToken token = default) -> Task<TenantMetadata?>`
  - `ReadByNameAsync(string name, CancellationToken token = default) -> Task<TenantMetadata?>`
  - `UpdateAsync(TenantMetadata tenant, CancellationToken token = default) -> Task<TenantMetadata>`
  - `DeleteAsync(string id, CancellationToken token = default) -> Task`
  - `EnumerateAsync(CancellationToken token = default) -> Task<List<TenantMetadata>>`
  - `EnumerateAsync(EnumerationQuery query, CancellationToken token = default) -> Task<EnumerationResult<TenantMetadata>>`
  - `ExistsAsync(string id, CancellationToken token = default) -> Task<bool>`
  - `ExistsAnyAsync(CancellationToken token = default) -> Task<bool>` — used for first-boot check
- [ ] Create `src/Armada.Core/Database/Interfaces/IUserMethods.cs`
  - `CreateAsync(UserMaster user, CancellationToken token = default) -> Task<UserMaster>`
  - `ReadAsync(string tenantId, string id, CancellationToken token = default) -> Task<UserMaster?>` — tenant-scoped
  - `ReadByIdAsync(string id, CancellationToken token = default) -> Task<UserMaster?>` — admin, no tenant filter
  - `ReadByEmailAsync(string tenantId, string email, CancellationToken token = default) -> Task<UserMaster?>` — tenant-scoped
  - `ReadByEmailAnyTenantAsync(string email, CancellationToken token = default) -> Task<List<UserMaster>>` — for login flow, returns all users across tenants matching email
  - `UpdateAsync(UserMaster user, CancellationToken token = default) -> Task<UserMaster>`
  - `DeleteAsync(string tenantId, string id, CancellationToken token = default) -> Task`
  - `EnumerateAsync(string tenantId, CancellationToken token = default) -> Task<List<UserMaster>>` — tenant-scoped
  - `EnumerateAsync(string tenantId, EnumerationQuery query, CancellationToken token = default) -> Task<EnumerationResult<UserMaster>>`
  - `ExistsAsync(string tenantId, string id, CancellationToken token = default) -> Task<bool>`
- [ ] Create `src/Armada.Core/Database/Interfaces/ICredentialMethods.cs`
  - `CreateAsync(Credential credential, CancellationToken token = default) -> Task<Credential>`
  - `ReadAsync(string tenantId, string id, CancellationToken token = default) -> Task<Credential?>` — tenant-scoped
  - `ReadByIdAsync(string id, CancellationToken token = default) -> Task<Credential?>` — admin, no tenant filter
  - `ReadByBearerTokenAsync(string bearerToken, CancellationToken token = default) -> Task<Credential?>` — global lookup for auth
  - `UpdateAsync(Credential credential, CancellationToken token = default) -> Task<Credential>`
  - `DeleteAsync(string tenantId, string id, CancellationToken token = default) -> Task`
  - `EnumerateAsync(string tenantId, CancellationToken token = default) -> Task<List<Credential>>`
  - `EnumerateAsync(string tenantId, EnumerationQuery query, CancellationToken token = default) -> Task<EnumerationResult<Credential>>`
  - `EnumerateByUserAsync(string tenantId, string userId, CancellationToken token = default) -> Task<List<Credential>>` — for non-admin users to see only their own

### 2.2 Update DatabaseDriver Base Class

- [ ] Add to `DatabaseDriver.cs`:
  - `public ITenantMethods Tenants { get; protected set; } = null!;`
  - `public IUserMethods Users { get; protected set; } = null!;`
  - `public ICredentialMethods Credentials { get; protected set; } = null!;`

### 2.3 Update Existing Database Interfaces for Tenant Fencing

For each existing interface (`IFleetMethods`, `IVesselMethods`, `ICaptainMethods`, `IMissionMethods`, `IVoyageMethods`, `IDockMethods`, `ISignalMethods`, `IEventMethods`, `IMergeEntryMethods`):

- [ ] Add tenant-scoped overloads to `IFleetMethods`:
  - `ReadAsync(string tenantId, string id, CancellationToken token)` — tenant-scoped read
  - `DeleteAsync(string tenantId, string id, CancellationToken token)` — tenant-scoped delete
  - `EnumerateAsync(string tenantId, CancellationToken token)` — tenant-scoped list
  - `EnumerateAsync(string tenantId, EnumerationQuery query, CancellationToken token)` — tenant-scoped paginated list
  - Existing un-scoped methods remain for admin/system use
- [ ] Repeat the same pattern for `IVesselMethods`
- [ ] Repeat the same pattern for `ICaptainMethods`
- [ ] Repeat the same pattern for `IMissionMethods`
- [ ] Repeat the same pattern for `IVoyageMethods`
- [ ] Repeat the same pattern for `IDockMethods`
- [ ] Repeat the same pattern for `ISignalMethods`
- [ ] Repeat the same pattern for `IEventMethods`
- [ ] Repeat the same pattern for `IMergeEntryMethods`

### 2.4 Schema Migration (All Database Backends)

> **Backend status:** SQLite is at migration v12, MySQL is at v2. PostgreSQL `InitializeAsync` is a no-op placeholder with no `TableQueries.cs`. SQL Server has 9 stub method files in `Database/SqlServer/Implementations/` but no `SqlServerDatabaseDriver` class — the factory throws `NotSupportedException`.

### 2.0 Prerequisite: Refactor Database Initialization

- [ ] **Refactor `ArmadaServer.StartAsync()`** to use `DatabaseDriverFactory.Create(settings.Database, _Logging)` instead of hardcoding `new SqliteDatabaseDriver(...)`. This unblocks all other database backends and is required before SQL Server, MySQL, or PostgreSQL can work from the server entrypoint.
  - Update `DatabaseSettings` if needed so `settings.Database.Type` defaults to `Sqlite`
  - Ensure `settings.Database.GetConnectionString()` produces the same `Data Source=~/.armada/armada.db` connection string for SQLite
  - The test runner (`Armada.Test.Database`) already uses the factory correctly — follow that pattern

#### 2.4.1 SQLite Migration (v13)

- [ ] **SQLite** (`src/Armada.Core/Database/Sqlite/Queries/TableQueries.cs`):
  - Add `SchemaMigration(13, "Multi-tenant: add tenants, users, credentials tables and tenant_id columns", ...)`
  - New tables:
    ```sql
    CREATE TABLE IF NOT EXISTS tenants (
        id TEXT PRIMARY KEY,
        name TEXT NOT NULL,
        active INTEGER NOT NULL DEFAULT 1,
        created_utc TEXT NOT NULL,
        last_update_utc TEXT NOT NULL
    );

    CREATE TABLE IF NOT EXISTS users (
        id TEXT PRIMARY KEY,
        tenant_id TEXT NOT NULL,
        email TEXT NOT NULL,
        password_sha256 TEXT NOT NULL,
        first_name TEXT,
        last_name TEXT,
        is_admin INTEGER NOT NULL DEFAULT 0,
        active INTEGER NOT NULL DEFAULT 1,
        created_utc TEXT NOT NULL,
        last_update_utc TEXT NOT NULL,
        FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE
    );
    CREATE UNIQUE INDEX IF NOT EXISTS idx_users_tenant_email ON users(tenant_id, email);
    CREATE INDEX IF NOT EXISTS idx_users_tenant ON users(tenant_id);
    CREATE INDEX IF NOT EXISTS idx_users_email ON users(email);

    CREATE TABLE IF NOT EXISTS credentials (
        id TEXT PRIMARY KEY,
        tenant_id TEXT NOT NULL,
        user_id TEXT NOT NULL,
        name TEXT,
        bearer_token TEXT NOT NULL UNIQUE,
        active INTEGER NOT NULL DEFAULT 1,
        created_utc TEXT NOT NULL,
        last_update_utc TEXT NOT NULL,
        FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE,
        FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
    );
    CREATE INDEX IF NOT EXISTS idx_credentials_tenant ON credentials(tenant_id);
    CREATE INDEX IF NOT EXISTS idx_credentials_user ON credentials(user_id);
    CREATE INDEX IF NOT EXISTS idx_credentials_bearer ON credentials(bearer_token);
    ```
  - Alter existing tables to add `tenant_id` column only (no `user_id`):
    ```sql
    ALTER TABLE fleets ADD COLUMN tenant_id TEXT;
    ALTER TABLE vessels ADD COLUMN tenant_id TEXT;
    ALTER TABLE captains ADD COLUMN tenant_id TEXT;
    ALTER TABLE voyages ADD COLUMN tenant_id TEXT;
    ALTER TABLE missions ADD COLUMN tenant_id TEXT;
    ALTER TABLE docks ADD COLUMN tenant_id TEXT;
    ALTER TABLE signals ADD COLUMN tenant_id TEXT;
    ALTER TABLE events ADD COLUMN tenant_id TEXT;
    ALTER TABLE merge_entries ADD COLUMN tenant_id TEXT;
    ```
  - Backfill existing rows with default tenant (runs once, inside migration transaction):
    ```sql
    UPDATE fleets SET tenant_id = 'default' WHERE tenant_id IS NULL;
    UPDATE vessels SET tenant_id = 'default' WHERE tenant_id IS NULL;
    UPDATE captains SET tenant_id = 'default' WHERE tenant_id IS NULL;
    UPDATE voyages SET tenant_id = 'default' WHERE tenant_id IS NULL;
    UPDATE missions SET tenant_id = 'default' WHERE tenant_id IS NULL;
    UPDATE docks SET tenant_id = 'default' WHERE tenant_id IS NULL;
    UPDATE signals SET tenant_id = 'default' WHERE tenant_id IS NULL;
    UPDATE events SET tenant_id = 'default' WHERE tenant_id IS NULL;
    UPDATE merge_entries SET tenant_id = 'default' WHERE tenant_id IS NULL;
    ```
  - Add simple and compound indexes based on actual query patterns:
    ```sql
    -- tenants: active tenant enumeration
    CREATE INDEX IF NOT EXISTS idx_tenants_active ON tenants(active);

    -- users: compound indexes for auth lookups
    -- idx_users_tenant_email already created as UNIQUE above
    -- idx_users_tenant already created above
    -- idx_users_email already created above

    -- credentials: compound indexes for auth lookups
    -- idx_credentials_tenant already created above
    -- idx_credentials_user already created above
    -- idx_credentials_bearer already created above
    CREATE INDEX IF NOT EXISTS idx_credentials_tenant_user ON credentials(tenant_id, user_id);
    CREATE INDEX IF NOT EXISTS idx_credentials_active ON credentials(active);

    -- fleets: tenant-scoped name lookups, date pagination
    CREATE INDEX IF NOT EXISTS idx_fleets_tenant ON fleets(tenant_id);
    CREATE INDEX IF NOT EXISTS idx_fleets_tenant_name ON fleets(tenant_id, name);
    CREATE INDEX IF NOT EXISTS idx_fleets_created_utc ON fleets(created_utc);

    -- vessels: tenant-scoped fleet + name lookups, date pagination
    CREATE INDEX IF NOT EXISTS idx_vessels_tenant ON vessels(tenant_id);
    CREATE INDEX IF NOT EXISTS idx_vessels_tenant_fleet ON vessels(tenant_id, fleet_id);
    CREATE INDEX IF NOT EXISTS idx_vessels_tenant_name ON vessels(tenant_id, name);
    CREATE INDEX IF NOT EXISTS idx_vessels_created_utc ON vessels(created_utc);

    -- captains: tenant-scoped state filtering (most common query), date pagination
    CREATE INDEX IF NOT EXISTS idx_captains_tenant ON captains(tenant_id);
    CREATE INDEX IF NOT EXISTS idx_captains_tenant_state ON captains(tenant_id, state);
    CREATE INDEX IF NOT EXISTS idx_captains_created_utc ON captains(created_utc);

    -- missions: tenant-scoped status, vessel, voyage, and work queue ordering
    CREATE INDEX IF NOT EXISTS idx_missions_tenant ON missions(tenant_id);
    CREATE INDEX IF NOT EXISTS idx_missions_tenant_status ON missions(tenant_id, status);
    CREATE INDEX IF NOT EXISTS idx_missions_tenant_vessel ON missions(tenant_id, vessel_id);
    CREATE INDEX IF NOT EXISTS idx_missions_tenant_voyage ON missions(tenant_id, voyage_id);
    CREATE INDEX IF NOT EXISTS idx_missions_tenant_captain ON missions(tenant_id, captain_id);
    CREATE INDEX IF NOT EXISTS idx_missions_tenant_status_priority ON missions(tenant_id, status, priority ASC, created_utc ASC);

    -- voyages: tenant-scoped status filtering, date pagination
    CREATE INDEX IF NOT EXISTS idx_voyages_tenant ON voyages(tenant_id);
    CREATE INDEX IF NOT EXISTS idx_voyages_tenant_status ON voyages(tenant_id, status);
    CREATE INDEX IF NOT EXISTS idx_voyages_created_utc ON voyages(created_utc);

    -- docks: tenant-scoped vessel lookups, available dock query, captain filtering
    CREATE INDEX IF NOT EXISTS idx_docks_tenant ON docks(tenant_id);
    CREATE INDEX IF NOT EXISTS idx_docks_tenant_vessel ON docks(tenant_id, vessel_id);
    CREATE INDEX IF NOT EXISTS idx_docks_tenant_vessel_available ON docks(tenant_id, vessel_id, active, captain_id);
    CREATE INDEX IF NOT EXISTS idx_docks_tenant_captain ON docks(tenant_id, captain_id);
    CREATE INDEX IF NOT EXISTS idx_docks_created_utc ON docks(created_utc);

    -- signals: tenant-scoped recipient and unread lookups
    CREATE INDEX IF NOT EXISTS idx_signals_tenant ON signals(tenant_id);
    CREATE INDEX IF NOT EXISTS idx_signals_tenant_to_captain ON signals(tenant_id, to_captain_id);
    CREATE INDEX IF NOT EXISTS idx_signals_tenant_to_captain_read ON signals(tenant_id, to_captain_id, read);
    CREATE INDEX IF NOT EXISTS idx_signals_tenant_created ON signals(tenant_id, created_utc DESC);

    -- events: tenant-scoped type, entity, and chronological lookups
    CREATE INDEX IF NOT EXISTS idx_events_tenant ON events(tenant_id);
    CREATE INDEX IF NOT EXISTS idx_events_tenant_type ON events(tenant_id, event_type);
    CREATE INDEX IF NOT EXISTS idx_events_tenant_entity ON events(tenant_id, entity_type, entity_id);
    CREATE INDEX IF NOT EXISTS idx_events_tenant_vessel ON events(tenant_id, vessel_id);
    CREATE INDEX IF NOT EXISTS idx_events_tenant_voyage ON events(tenant_id, voyage_id);
    CREATE INDEX IF NOT EXISTS idx_events_tenant_captain ON events(tenant_id, captain_id);
    CREATE INDEX IF NOT EXISTS idx_events_tenant_mission ON events(tenant_id, mission_id);
    CREATE INDEX IF NOT EXISTS idx_events_tenant_created ON events(tenant_id, created_utc DESC);

    -- merge_entries: tenant-scoped status, queue processing, vessel/mission lookups
    CREATE INDEX IF NOT EXISTS idx_merge_entries_tenant ON merge_entries(tenant_id);
    CREATE INDEX IF NOT EXISTS idx_merge_entries_tenant_status ON merge_entries(tenant_id, status);
    CREATE INDEX IF NOT EXISTS idx_merge_entries_tenant_status_priority ON merge_entries(tenant_id, status, priority ASC, created_utc ASC);
    CREATE INDEX IF NOT EXISTS idx_merge_entries_tenant_vessel ON merge_entries(tenant_id, vessel_id);
    CREATE INDEX IF NOT EXISTS idx_merge_entries_tenant_mission ON merge_entries(tenant_id, mission_id);
    ```

#### 2.4.2 MySQL Migration (v3)

- [ ] **MySQL** (`src/Armada.Core/Database/Mysql/Queries/TableQueries.cs`):
  - Add `SchemaMigration(3, "Multi-tenant: add tenants, users, credentials tables and tenant_id columns", ...)`
  - Note: MySQL is at v2, not v12 — migration numbering is per-backend
  - New tables (MySQL syntax):
    ```sql
    CREATE TABLE IF NOT EXISTS tenants (
        id VARCHAR(450) NOT NULL PRIMARY KEY,
        name VARCHAR(450) NOT NULL,
        active TINYINT(1) NOT NULL DEFAULT 1,
        created_utc DATETIME(6) NOT NULL,
        last_update_utc DATETIME(6) NOT NULL
    );

    CREATE TABLE IF NOT EXISTS users (
        id VARCHAR(450) NOT NULL PRIMARY KEY,
        tenant_id VARCHAR(450) NOT NULL,
        email VARCHAR(450) NOT NULL,
        password_sha256 VARCHAR(450) NOT NULL,
        first_name VARCHAR(450),
        last_name VARCHAR(450),
        is_admin TINYINT(1) NOT NULL DEFAULT 0,
        active TINYINT(1) NOT NULL DEFAULT 1,
        created_utc DATETIME(6) NOT NULL,
        last_update_utc DATETIME(6) NOT NULL,
        FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE
    );
    CREATE UNIQUE INDEX idx_users_tenant_email ON users(tenant_id, email);
    CREATE INDEX idx_users_tenant ON users(tenant_id);
    CREATE INDEX idx_users_email ON users(email);

    CREATE TABLE IF NOT EXISTS credentials (
        id VARCHAR(450) NOT NULL PRIMARY KEY,
        tenant_id VARCHAR(450) NOT NULL,
        user_id VARCHAR(450) NOT NULL,
        name VARCHAR(450),
        bearer_token VARCHAR(450) NOT NULL UNIQUE,
        active TINYINT(1) NOT NULL DEFAULT 1,
        created_utc DATETIME(6) NOT NULL,
        last_update_utc DATETIME(6) NOT NULL,
        FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE,
        FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
    );
    CREATE INDEX idx_credentials_tenant ON credentials(tenant_id);
    CREATE INDEX idx_credentials_user ON credentials(user_id);
    CREATE INDEX idx_credentials_bearer ON credentials(bearer_token);
    ```
  - Alter existing tables to add `tenant_id`:
    ```sql
    ALTER TABLE fleets ADD COLUMN tenant_id VARCHAR(450);
    ALTER TABLE vessels ADD COLUMN tenant_id VARCHAR(450);
    ALTER TABLE captains ADD COLUMN tenant_id VARCHAR(450);
    ALTER TABLE voyages ADD COLUMN tenant_id VARCHAR(450);
    ALTER TABLE missions ADD COLUMN tenant_id VARCHAR(450);
    ALTER TABLE docks ADD COLUMN tenant_id VARCHAR(450);
    ALTER TABLE signals ADD COLUMN tenant_id VARCHAR(450);
    ALTER TABLE events ADD COLUMN tenant_id VARCHAR(450);
    ALTER TABLE merge_entries ADD COLUMN tenant_id VARCHAR(450);
    ```
  - Backfill existing rows with default tenant:
    ```sql
    UPDATE fleets SET tenant_id = 'default' WHERE tenant_id IS NULL;
    UPDATE vessels SET tenant_id = 'default' WHERE tenant_id IS NULL;
    UPDATE captains SET tenant_id = 'default' WHERE tenant_id IS NULL;
    UPDATE voyages SET tenant_id = 'default' WHERE tenant_id IS NULL;
    UPDATE missions SET tenant_id = 'default' WHERE tenant_id IS NULL;
    UPDATE docks SET tenant_id = 'default' WHERE tenant_id IS NULL;
    UPDATE signals SET tenant_id = 'default' WHERE tenant_id IS NULL;
    UPDATE events SET tenant_id = 'default' WHERE tenant_id IS NULL;
    UPDATE merge_entries SET tenant_id = 'default' WHERE tenant_id IS NULL;
    ```
  - Add simple and compound indexes based on actual query patterns:
    ```sql
    -- tenants
    CREATE INDEX idx_tenants_active ON tenants(active);

    -- credentials: compound indexes
    CREATE INDEX idx_credentials_tenant_user ON credentials(tenant_id, user_id);
    CREATE INDEX idx_credentials_active ON credentials(active);

    -- fleets
    CREATE INDEX idx_fleets_tenant ON fleets(tenant_id);
    CREATE INDEX idx_fleets_tenant_name ON fleets(tenant_id, name);
    CREATE INDEX idx_fleets_created_utc ON fleets(created_utc);

    -- vessels
    CREATE INDEX idx_vessels_tenant ON vessels(tenant_id);
    CREATE INDEX idx_vessels_tenant_fleet ON vessels(tenant_id, fleet_id);
    CREATE INDEX idx_vessels_tenant_name ON vessels(tenant_id, name);
    CREATE INDEX idx_vessels_created_utc ON vessels(created_utc);

    -- captains
    CREATE INDEX idx_captains_tenant ON captains(tenant_id);
    CREATE INDEX idx_captains_tenant_state ON captains(tenant_id, state);
    CREATE INDEX idx_captains_created_utc ON captains(created_utc);

    -- missions
    CREATE INDEX idx_missions_tenant ON missions(tenant_id);
    CREATE INDEX idx_missions_tenant_status ON missions(tenant_id, status);
    CREATE INDEX idx_missions_tenant_vessel ON missions(tenant_id, vessel_id);
    CREATE INDEX idx_missions_tenant_voyage ON missions(tenant_id, voyage_id);
    CREATE INDEX idx_missions_tenant_captain ON missions(tenant_id, captain_id);
    CREATE INDEX idx_missions_tenant_status_priority ON missions(tenant_id, status, priority ASC, created_utc ASC);

    -- voyages
    CREATE INDEX idx_voyages_tenant ON voyages(tenant_id);
    CREATE INDEX idx_voyages_tenant_status ON voyages(tenant_id, status);
    CREATE INDEX idx_voyages_created_utc ON voyages(created_utc);

    -- docks
    CREATE INDEX idx_docks_tenant ON docks(tenant_id);
    CREATE INDEX idx_docks_tenant_vessel ON docks(tenant_id, vessel_id);
    CREATE INDEX idx_docks_tenant_vessel_available ON docks(tenant_id, vessel_id, active, captain_id);
    CREATE INDEX idx_docks_tenant_captain ON docks(tenant_id, captain_id);
    CREATE INDEX idx_docks_created_utc ON docks(created_utc);

    -- signals
    CREATE INDEX idx_signals_tenant ON signals(tenant_id);
    CREATE INDEX idx_signals_tenant_to_captain ON signals(tenant_id, to_captain_id);
    CREATE INDEX idx_signals_tenant_to_captain_read ON signals(tenant_id, to_captain_id, `read`);
    CREATE INDEX idx_signals_tenant_created ON signals(tenant_id, created_utc DESC);

    -- events
    CREATE INDEX idx_events_tenant ON events(tenant_id);
    CREATE INDEX idx_events_tenant_type ON events(tenant_id, event_type);
    CREATE INDEX idx_events_tenant_entity ON events(tenant_id, entity_type, entity_id);
    CREATE INDEX idx_events_tenant_vessel ON events(tenant_id, vessel_id);
    CREATE INDEX idx_events_tenant_voyage ON events(tenant_id, voyage_id);
    CREATE INDEX idx_events_tenant_captain ON events(tenant_id, captain_id);
    CREATE INDEX idx_events_tenant_mission ON events(tenant_id, mission_id);
    CREATE INDEX idx_events_tenant_created ON events(tenant_id, created_utc DESC);

    -- merge_entries
    CREATE INDEX idx_merge_entries_tenant ON merge_entries(tenant_id);
    CREATE INDEX idx_merge_entries_tenant_status ON merge_entries(tenant_id, status);
    CREATE INDEX idx_merge_entries_tenant_status_priority ON merge_entries(tenant_id, status, priority ASC, created_utc ASC);
    CREATE INDEX idx_merge_entries_tenant_vessel ON merge_entries(tenant_id, vessel_id);
    CREATE INDEX idx_merge_entries_tenant_mission ON merge_entries(tenant_id, mission_id);
    ```

#### 2.4.3 PostgreSQL — Create Migration Infrastructure

- [ ] **PostgreSQL**: Create `src/Armada.Core/Database/Postgresql/Queries/TableQueries.cs`
  - PostgreSQL currently has no `TableQueries.cs` and `InitializeAsync` is a no-op placeholder
  - Create the file following the SQLite/MySQL pattern with a `GetMigrations()` method
  - Include a single v1 migration containing the **full schema** (all tables including tenants, users, credentials, and `tenant_id` on operational tables from the start) — since there are no existing PostgreSQL databases to migrate
  - Use PostgreSQL-native types: `TEXT` for strings, `BOOLEAN` for booleans, `TIMESTAMP` for dates, `INTEGER` for ints
  - Update `PostgresqlDatabaseDriver.InitializeAsync()` to call the migration system (currently a no-op placeholder)
  - Include the full compound index set matching SQLite/MySQL (adapted for PostgreSQL syntax):
    - `idx_tenants_active`, `idx_credentials_tenant_user`, `idx_credentials_active`
    - `idx_{table}_tenant` for all 9 operational tables
    - `idx_fleets_tenant_name`, `idx_vessels_tenant_fleet`, `idx_vessels_tenant_name`
    - `idx_captains_tenant_state`
    - `idx_missions_tenant_status`, `idx_missions_tenant_vessel`, `idx_missions_tenant_voyage`, `idx_missions_tenant_captain`, `idx_missions_tenant_status_priority`
    - `idx_voyages_tenant_status`
    - `idx_docks_tenant_vessel`, `idx_docks_tenant_vessel_available`, `idx_docks_tenant_captain`
    - `idx_signals_tenant_to_captain`, `idx_signals_tenant_to_captain_read`, `idx_signals_tenant_created`
    - `idx_events_tenant_type`, `idx_events_tenant_entity`, `idx_events_tenant_vessel`, `idx_events_tenant_voyage`, `idx_events_tenant_captain`, `idx_events_tenant_mission`, `idx_events_tenant_created`
    - `idx_merge_entries_tenant_status`, `idx_merge_entries_tenant_status_priority`, `idx_merge_entries_tenant_vessel`, `idx_merge_entries_tenant_mission`
    - `idx_{table}_created_utc` for fleets, vessels, captains, voyages, docks (pagination support)

#### 2.4.4 SQL Server — Full Support

- [ ] **SQL Server**: Create `src/Armada.Core/Database/SqlServer/Queries/TableQueries.cs`
  - Like PostgreSQL, this is a fresh database with no existing users — write a single v1 migration containing the **full schema** (all tables including tenants, users, credentials, and `tenant_id` on operational tables from the start)
  - Use SQL Server types: `NVARCHAR(450)` for strings, `BIT` for booleans, `DATETIME2` for timestamps, `INT` for integers
  - Include the full compound index set matching other backends:
    - `idx_tenants_active`, `idx_users_tenant_email` (UNIQUE), `idx_users_tenant`, `idx_users_email`
    - `idx_credentials_tenant`, `idx_credentials_user`, `idx_credentials_bearer` (UNIQUE), `idx_credentials_tenant_user`, `idx_credentials_active`
    - `idx_{table}_tenant` for all 9 operational tables
    - All compound indexes: `idx_fleets_tenant_name`, `idx_vessels_tenant_fleet`, `idx_vessels_tenant_name`, `idx_captains_tenant_state`
    - `idx_missions_tenant_status`, `idx_missions_tenant_vessel`, `idx_missions_tenant_voyage`, `idx_missions_tenant_captain`, `idx_missions_tenant_status_priority`
    - `idx_voyages_tenant_status`
    - `idx_docks_tenant_vessel`, `idx_docks_tenant_vessel_available`, `idx_docks_tenant_captain`
    - `idx_signals_tenant_to_captain`, `idx_signals_tenant_to_captain_read`, `idx_signals_tenant_created`
    - `idx_events_tenant_type`, `idx_events_tenant_entity`, `idx_events_tenant_vessel`, `idx_events_tenant_voyage`, `idx_events_tenant_captain`, `idx_events_tenant_mission`, `idx_events_tenant_created`
    - `idx_merge_entries_tenant_status`, `idx_merge_entries_tenant_status_priority`, `idx_merge_entries_tenant_vessel`, `idx_merge_entries_tenant_mission`
    - `idx_{table}_created_utc` for fleets, vessels, captains, voyages, docks
- [ ] **Create `SqlServerDatabaseDriver.cs`** (`src/Armada.Core/Database/SqlServer/SqlServerDatabaseDriver.cs`)
  - Inherit from `DatabaseDriver`, following the pattern of `SqliteDatabaseDriver.cs`, `MysqlDatabaseDriver.cs`, and `PostgresqlDatabaseDriver.cs`
  - Wire up all 9 existing stub method classes + the 3 new ones (Tenant, User, Credential) in the constructor
  - Implement `InitializeAsync()` to run the migration system from `TableQueries.cs`
- [ ] **Remove `throw new NotSupportedException()`** from `DatabaseDriverFactory.cs` and replace with `return new SqlServerDatabaseDriver(settings, logging);`
- [ ] **Create SQL Server implementations** for `TenantMethods.cs`, `UserMethods.cs`, `CredentialMethods.cs` in `src/Armada.Core/Database/SqlServer/Implementations/`
- [ ] **Update all 9 existing SQL Server stub method files** to include `tenant_id` in their queries (they currently don't reference it) and add tenant-scoped overloads

### 2.5 Implement New Database Methods

For each database backend (SQLite, PostgreSQL, SQL Server, MySQL):

- [ ] **SQLite Tenant methods**: Create `src/Armada.Core/Database/Sqlite/Implementations/TenantMethods.cs`
  - Implement `ITenantMethods`
  - Add `TenantFromReader()` to `SqliteDatabaseDriver.cs`
  - Follow pattern from `FleetMethods.cs`
- [ ] **SQLite User methods**: Create `src/Armada.Core/Database/Sqlite/Implementations/UserMethods.cs`
  - Implement `IUserMethods`
  - Add `UserFromReader()` to `SqliteDatabaseDriver.cs`
  - `ReadByEmailAnyTenantAsync` queries `WHERE email = @email` without tenant filter
- [ ] **SQLite Credential methods**: Create `src/Armada.Core/Database/Sqlite/Implementations/CredentialMethods.cs`
  - Implement `ICredentialMethods`
  - Add `CredentialFromReader()` to `SqliteDatabaseDriver.cs`
  - `ReadByBearerTokenAsync` queries `WHERE bearer_token = @token` globally (for auth)
- [ ] Wire up in `SqliteDatabaseDriver` constructor: `Tenants = new TenantMethods(...)`, `Users = new UserMethods(...)`, `Credentials = new CredentialMethods(...)`
- [ ] **PostgreSQL**: Repeat for PostgreSQL implementations
- [ ] **SQL Server**: Repeat for SQL Server implementations
- [ ] **MySQL**: Repeat for MySQL implementations

### 2.6 Update Existing Database Method Implementations

For each existing entity methods class across all backends, add the tenant-scoped overloads:

- [ ] **SQLite**: Update all 9 entity method files to add tenant-scoped query methods
  - Every scoped read includes `WHERE tenant_id = @tenantId AND id = @id`
  - Every scoped enumerate includes `WHERE tenant_id = @tenantId`
  - Every scoped delete includes `WHERE tenant_id = @tenantId AND id = @id`
  - `CreateAsync` should set `tenant_id` from the model object
  - Existing un-scoped methods remain for admin/system use
- [ ] Update `XxxFromReader()` methods in `SqliteDatabaseDriver.cs` to read `tenant_id` column (with null handling for pre-migration data)
- [ ] **PostgreSQL**: Repeat
- [ ] **SQL Server**: Repeat
- [ ] **MySQL**: Repeat

### 2.7 Default Data Seeding

- [ ] Update `InitializeAsync()` in each database driver to seed default data **after** migrations complete:
  - Call `Tenants.ExistsAnyAsync()`
  - If false, create default tenant, user, and credential (see Design Decisions above)
  - Note: the `tenant_id = 'default'` backfill on existing operational rows is handled **inside the migration itself** (section 2.4), NOT here — this ensures the backfill runs exactly once, atomically within the migration transaction
  - Do NOT backfill a fake `user_id` — operational tables do not have this column

### 2.8 Synthetic Admin Identity Seeding

- [ ] If `Settings.ApiKey` is configured, ensure synthetic admin identity exists on startup:
  - Create tenant `ten_system` / "System" if not exists
  - Create user `usr_system` / `system@armada` with `IsAdmin = true` if not exists
  - Create credential linking `ten_system` + `usr_system` with the configured API key as bearer token
  - This tenant/user pair is excluded from normal enumeration queries (filter by `id != 'ten_system'`)

---

## Phase 3: Authentication and Authorization

### 3.1 Session Token Encryption Service

- [ ] Create `src/Armada.Core/Services/SessionTokenService.cs`
  - No in-memory state — tokens are self-contained encrypted payloads
  - `CreateToken(string tenantId, string userId) -> AuthenticateResult` — AES-256-CBC encrypts `{ TenantId, UserId, ExpiresUtc }` into a base64 token string, returns result with token and expiry
  - `ValidateToken(string encryptedToken) -> AuthContext?` — decrypts, checks expiry, returns AuthContext or null
  - Encryption key sourced from `Settings.SessionTokenEncryptionKey`
  - Modeled after LiteGraph's `SecurityToken` pattern
- [ ] Create `src/Armada.Core/Services/Interfaces/ISessionTokenService.cs`

### 3.2 Authentication Service

- [ ] Create `src/Armada.Core/Services/AuthenticationService.cs`
  - Constructor takes `DatabaseDriver`, `ISessionTokenService`, `Settings`
  - `AuthenticateAsync(HttpContext ctx, CancellationToken token) -> Task<AuthContext>`
    1. Check `Authorization: Bearer {token}` header → look up credential by bearer token → validate credential.Active, user.Active, tenant.Active → return AuthContext
    2. Check `X-Token` header → decrypt and validate session token → look up user to verify still active → return AuthContext
    3. Check `X-Api-Key` header → match against `Settings.ApiKey` → resolve to synthetic admin AuthContext
    4. If none, return unauthenticated AuthContext
  - `AuthenticateWithCredentialsAsync(string tenantId, string email, string password, CancellationToken token) -> Task<AuthContext>`
    - Tenant-scoped login: look up user by email in tenant, verify password, check active flags
  - Bearer token is checked FIRST — it is the canonical auth path
- [ ] Create `src/Armada.Core/Services/Interfaces/IAuthenticationService.cs`

### 3.3 Authorization Service

- [ ] Create `src/Armada.Core/Services/AuthorizationService.cs`
  - Uses the authorization matrix from Phase 0
  - `IsAuthorized(AuthContext ctx, string endpoint, string method) -> bool`
  - `RequireAuth(AuthContext ctx) -> void` — throws/returns 401 if not authenticated
  - `RequireAdmin(AuthContext ctx) -> void` — throws/returns 403 if not admin
  - Centralizes all permission checks — no scattered `if (isAdmin)` throughout server code
- [ ] Create `src/Armada.Core/Services/Interfaces/IAuthorizationService.cs`

### 3.4 Integrate Authentication into ArmadaServer

- [ ] Add `ISessionTokenService`, `IAuthenticationService`, and `IAuthorizationService` as private members in `ArmadaServer.cs`
- [ ] Instantiate in constructor/initialization
- [ ] Create a helper method `AuthenticateRequestAsync(HttpContext ctx)` that calls `IAuthenticationService.AuthenticateAsync()` and returns the `AuthContext`
- [ ] For endpoints that require auth: call `AuthenticateRequestAsync()`, then `IAuthorizationService` to check permission level, return 401/403 as appropriate
- [ ] Remove direct `X-Api-Key` checking from existing code — all auth now flows through `AuthenticationService`

---

## Phase 4: API Layer - New Endpoints

All new endpoints in `ArmadaServer.cs` (or refactored into separate handler classes if the refactoring plan from REFACTORING_PLAN.md is executed first).

### 4.1 POST /api/v1/authenticate

- [ ] Accept `AuthenticateRequest` body (`TenantId` + `Email` + `Password`) OR bearer token from Authorization header OR session token from X-Token header
- [ ] On success: create encrypted session token via `ISessionTokenService.CreateToken()`, return `AuthenticateResult` with `{ Success: true, Token: "{token}", ExpiresUtc: "..." }`
- [ ] On failure: return `{ Success: false }` with 401

### 4.2 GET /api/v1/whoami

- [ ] Requires authentication (X-Token or Bearer)
- [ ] Returns `WhoAmIResult` with `{ Tenant: {...}, User: {...} }` where user password is redacted
- [ ] 401 if not authenticated

### 4.3 POST /api/v1/tenants/lookup

- [ ] No authentication required
- [ ] Accept `TenantLookupRequest` body: `{ Email: "..." }`
- [ ] Returns `TenantLookupResult` with list of `TenantListEntry` (Id + Name) for tenants that have a user with that email
- [ ] Returns empty list if email not found — do not distinguish "no user" from "no tenants" (prevents email enumeration)
- [ ] Excludes `ten_system` from results

### 4.4 Tenant CRUD (Admin Only)

- [ ] `POST /api/v1/tenants` — create tenant (admin only)
- [ ] `GET /api/v1/tenants/{id}` — read tenant
  - Admin: any tenant
  - Non-admin: only if `{id}` matches the caller's own tenant ID
- [ ] `PUT /api/v1/tenants/{id}` — update tenant (admin only)
- [ ] `DELETE /api/v1/tenants/{id}` — delete tenant (admin only, cascading delete)
- [ ] `GET /api/v1/tenants` with query params (paginated) — admin only, returns full objects, excludes `ten_system`

### 4.5 User CRUD (Admin Only, with exceptions)

- [ ] `POST /api/v1/users` — create user (admin only)
- [ ] `GET /api/v1/users/{id}` — read user (password redacted)
  - Admin: any user
  - Non-admin: only if `{id}` matches the caller's own user ID
- [ ] `PUT /api/v1/users/{id}` — update user (admin only)
- [ ] `DELETE /api/v1/users/{id}` — delete user (admin only)
- [ ] `GET /api/v1/users` with query params (paginated) — admin only

### 4.6 Credential CRUD (Admin + Self-Service)

- [ ] `POST /api/v1/credentials` — create credential
  - Admin: any tenant/user
  - Non-admin: only for self (enforced: TenantId = caller's tenant, UserId = caller's user)
- [ ] `GET /api/v1/credentials/{id}` — read credential
  - Admin: any credential
  - Non-admin: only credentials belonging to the caller
- [ ] `PUT /api/v1/credentials/{id}` — update credential (admin only)
- [ ] `DELETE /api/v1/credentials/{id}` — delete credential
  - Admin: any credential
  - Non-admin: only own credentials
- [ ] `GET /api/v1/credentials` with query params — list credentials
  - Admin: all credentials
  - Non-admin: only own credentials (filtered by tenant + user)

### 4.7 POST /api/v1/onboarding

- [ ] Gated by `Settings.AllowSelfRegistration` — returns 403 with message if disabled
- [ ] No authentication required when enabled (self-service)
- [ ] Accept `OnboardingRequest` body: `{ TenantId, Email, Password, FirstName?, LastName? }`
- [ ] Validate:
  - Tenant exists and is active
  - Email not already taken in that tenant
  - Password meets minimum requirements (non-empty)
- [ ] Create `UserMaster` with `IsAdmin = false`
- [ ] Create `Credential` with auto-generated bearer token
- [ ] Return `OnboardingResult` with tenant, redacted user, and credential

---

## Phase 5: API Layer - Tenant Fencing on Existing Endpoints

Every existing REST endpoint must be updated to inject tenant context.

### 5.1 Authentication Gate

- [ ] Add authentication check to ALL existing entity endpoints (fleets, vessels, captains, missions, voyages, docks, signals, events, merge entries)
- [ ] Extract `AuthContext` from request via `AuthenticateRequestAsync()`
- [ ] Return 401 if not authenticated

### 5.2 Tenant Injection on Create

For each entity's POST (create) endpoint:

- [ ] **Fleets**: Set `fleet.TenantId = authContext.TenantId` before saving
- [ ] **Vessels**: Same pattern
- [ ] **Captains**: Same pattern
- [ ] **Missions**: Same pattern (including dispatch/create-mission)
- [ ] **Voyages**: Same pattern (including voyage request)
- [ ] **Docks**: Same pattern
- [ ] **Signals**: Same pattern
- [ ] **MergeEntries**: Same pattern

### 5.3 Tenant Filtering on Read/Enumerate

For each entity's GET endpoints:

- [ ] **Admin callers**: Use existing un-scoped database methods (see all data across tenants)
- [ ] **Non-admin callers**: Use tenant-scoped database methods (see all data within own tenant)
- [ ] Apply to both single-read (`GET /{id}`) and enumerate (`GET /`) endpoints
- [ ] Apply to the `armada_enumerate` MCP-adjacent REST endpoint if it exists

### 5.4 Tenant Validation on Update/Delete

For each entity's PUT/DELETE endpoints:

- [ ] **Admin callers**: Can update/delete any entity
- [ ] **Non-admin callers**: Can only update/delete entities within their tenant
- [ ] Return 404 (not 403) if entity exists but belongs to different tenant (don't leak existence)

### 5.5 Background Service Updates

- [ ] Update `DataExpiryService` to iterate across all tenants using admin-level unscoped queries
- [ ] Update `AdmiralService.HealthCheckAsync` to operate cross-tenant with admin context
- [ ] Update `AdmiralService.CleanupStaleCaptainsAsync` to operate cross-tenant with admin context
- [ ] Update `MergeQueueService.ProcessQueueAsync` to process merge entries cross-tenant with admin context
- [ ] Update `CaptainService` to pass tenant context from the captain's `TenantId` during lifecycle operations
- [ ] Update `LandingService` to pass tenant context through landing operations
- [ ] Update `VoyageService` to pass tenant context through voyage operations
- [ ] Update `MissionService` to pass tenant context through mission operations
- [ ] Update `DockService` to pass tenant context through dock operations

### 5.6 Embedded Dashboard Login Flow

- [ ] **Update embedded dashboard** (`src/Armada.Server/wwwroot/`) to add authentication:
  - Add a login overlay/screen to `index.html` (similar to the existing API key auth overlay)
  - Login flow: email entry → `POST /api/v1/tenants/lookup` → tenant selection (if multiple) → password entry → `POST /api/v1/authenticate`
  - On successful authentication, store session token in Alpine.js reactive state
  - All subsequent API calls from `js/modules/data-loaders.js` include `X-Token: {sessionToken}` header
  - On 401 response from any API call, redirect back to login overlay
  - Add logout button to header that clears session token and shows login overlay
  - Add tenant name + user email badges to dashboard header (from `GET /api/v1/whoami`)
  - If only one tenant exists for the email, skip tenant selection (single-user fast path)
  - This keeps the embedded dashboard functional for single-user and development workflows without requiring the React dashboard

### 5.7 MCP Tool Handler Updates

- [x] MCP tools remain unauthenticated by design (stdio transport assumes trusted local caller). All MCP operations use default tenant context. See `docs/MCP_API.md` for rationale.

---

## Phase 6: Dashboard - React App (Optional — for Docker/Production Deployments)

> **This phase is optional.** The embedded dashboard (updated in Phase 5.6) covers single-user and development workflows. The React dashboard is an additional deployment option for Docker, multi-node, and production environments. It does NOT replace the embedded dashboard.

### 6.1 Project Setup

- [ ] Create `src/Armada.Dashboard/` directory
- [ ] Initialize React app (Vite + React + TypeScript recommended)
- [ ] Add `Dockerfile` for the dashboard:
  - Build stage: Node.js, `npm install`, `npm run build`
  - Runtime stage: nginx (or similar lightweight web server) serving the built static files
  - Environment variable `ARMADA_SERVER_URL` injected at build time or runtime (for API base URL)
  - Image: `jchristn77/armada-dashboard`
- [ ] Port existing dashboard CSS and visual style from `src/Armada.Server/wwwroot/` into the React app
- [ ] Set up API client module pointing to `ARMADA_SERVER_URL`

### 6.2 Login Flow

- [ ] **Email entry view**: Text box for email, "Login" button
- [ ] On submit, call `POST /api/v1/tenants/lookup` with `{ Email: "..." }` to find tenants associated with that email
- [ ] **User not found** (empty list returned): Show error, return to email entry
- [ ] **One tenant found**: Skip tenant selection, proceed to password entry with that tenant pre-selected
- [ ] **Multiple tenants found**: Show "Choose your tenant" view with radio buttons or dropdown, "Continue" button
- [ ] **Password entry view**: Password field, "Sign In" button
  - Call `POST /api/v1/authenticate` with `{ TenantId, Email, Password }`
  - On success: store session token in memory (React state or context, NOT localStorage for security), navigate to main dashboard
  - On failure: show error, return to email entry view

### 6.3 Main Dashboard Page - Badges

- [ ] Add a header/badge bar at the top of the main dashboard showing:
  - **Tenant name** badge
  - **User email** badge
  - **Admin** badge (shown only when `IsAdmin = true`)
- [ ] Populate from `GET /api/v1/whoami` on dashboard load (after login)

### 6.4 Port Existing Views

Port all existing dashboard views from the embedded HTML/JS to React components:

- [ ] Home / Status view (calls `GET /api/v1/status/health`)
- [ ] Fleets view (list, create, edit, delete)
- [ ] Vessels view
- [ ] Captains view
- [ ] Missions view (list, detail, dispatch, log, diff)
- [ ] Voyages view (list, detail, create)
- [ ] Docks view
- [ ] Events view
- [ ] Merge Queue view
- [ ] Server/Settings view
- [ ] Doctor/Diagnostics view

### 6.5 Administration Section

- [ ] Show "Administration" nav section only when `IsAdmin = true` (from whoami response)
- [ ] **Tenants view**: List, create, edit, deactivate/activate tenants
  - Show Active toggle prominently
  - When Active = false, display visual indicator (grayed out, warning badge)
- [ ] **Users view**: List, create, edit, deactivate/activate users
  - Password field on create (hashed server-side)
  - Show IsAdmin toggle
  - Show Active toggle
  - Password redacted on read
- [ ] **Credentials view**: List, create, edit, deactivate/activate credentials
  - Show bearer token (copyable)
  - Show Active toggle

### 6.6 API Token Handling

- [ ] All API calls from dashboard include `X-Token: {sessionToken}` header
- [ ] On 401 response from any API call, redirect to login view
- [ ] On session expiry (24h), redirect to login view

### 6.7 Logout

- [ ] Add logout button to header
- [ ] Clear session token from memory
- [ ] Navigate to login view

---

## Phase 7: Docker

### 7.1 Update compose.yaml

- [ ] Update `docker/compose.yaml`:
  ```yaml
  services:
    armada-server:
      image: jchristn77/armada-server:v0.3.0
      ports:
        - "7890:7890"
        - "7891:7891"
        - "7892:7892"
      volumes:
        - ./server/armada.json:/app/data/armada.json
        - ./armada/db:/app/data/db
        - ./armada/logs:/app/data/logs

    armada-dashboard:
      image: jchristn77/armada-dashboard:v0.3.0
      ports:
        - "3000:80"
      environment:
        - ARMADA_SERVER_URL=http://armada-server:7890
      depends_on:
        - armada-server
  ```
- [ ] Create `docker/armada/db/.gitkeep`
- [ ] Create `docker/armada/logs/.gitkeep`
- [ ] Ensure `docker/server/armada.json` is a valid default settings file

### 7.2 Factory Reset Scripts

- [ ] Create `docker/factory/` directory
- [ ] Create `docker/factory/reset.bat`:
  - Confirmation prompt ("Type 'RESET' to confirm")
  - `docker compose down`
  - Delete `docker/armada/db/*` (database files)
  - Delete `docker/armada/logs/*` (log files)
  - Preserve `docker/server/armada.json` (configuration)
  - Print instructions to restart with `docker compose up -d`
  - Follow pattern from `C:\Code\AssistantHub\docker\factory\reset.bat`
- [ ] Create `docker/factory/reset.sh`:
  - Same logic as reset.bat, bash syntax
  - `set -e`, confirmation prompt, step-by-step with echo output
  - Follow pattern from `C:\Code\AssistantHub\docker\factory\reset.sh`

### 7.3 Dashboard Dockerfile

- [ ] Create `src/Armada.Dashboard/Dockerfile`:
  - Multi-stage build
  - Stage 1: `node:20-alpine`, copy package.json, `npm ci`, copy source, `npm run build`
  - Stage 2: `nginx:alpine`, copy built files to `/usr/share/nginx/html`
  - Configure nginx to serve SPA (fallback all routes to index.html)
  - Expose port 80
  - Runtime env var injection for `ARMADA_SERVER_URL`

### 7.4 Build Scripts

- [ ] Update `build-server.bat` / `build-server.sh` to tag as v0.3.0
- [ ] Create `build-dashboard.bat` / `build-dashboard.sh` for building the dashboard Docker image

---

## Phase 8: Test Projects

### 8.1 Unit Tests

- [ ] Add unit tests for `TenantMetadata` model (validation, defaults)
- [ ] Add unit tests for `UserMaster` model (password hashing, verification, redaction)
- [ ] Add unit tests for `Credential` model (bearer token generation, validation)
- [ ] Add unit tests for `SessionTokenService` (create, validate, expired token rejection, tamper detection)
- [ ] Add unit tests for `AuthContext` model
- [ ] Add unit tests for `AuthorizationConfig` (permission matrix correctness)

### 8.2 Database Tests

- [ ] Add tests for `ITenantMethods` (CRUD, exists, enumerate)
- [ ] Add tests for `IUserMethods` (CRUD, email lookup, cross-tenant lookup, tenant-scoped operations)
- [ ] Add tests for `ICredentialMethods` (CRUD, bearer token lookup, user-scoped enumerate)
- [ ] Add tests for tenant-scoped operations on existing entities (verify fencing works — user in Tenant A cannot read Tenant B data)
- [ ] Add test for default data seeding on fresh database
- [ ] Add test for synthetic admin identity seeding when API key is configured

### 8.3 Integration Tests

- [ ] Add tests for `POST /api/v1/authenticate` (email+password, bearer token, session token)
- [ ] Add tests for `GET /api/v1/whoami`
- [ ] Add tests for `POST /api/v1/onboarding` (enabled and disabled via AllowSelfRegistration)
- [ ] Add tests for `POST /api/v1/tenants/lookup`
- [ ] Add tests for tenant CRUD endpoints (admin vs non-admin)
- [ ] Add tests for user CRUD endpoints (admin vs non-admin, self-read)
- [ ] Add tests for credential CRUD endpoints (admin vs non-admin, self-service)
- [ ] Add tests verifying tenant fencing on existing entity endpoints:
  - User in Tenant A cannot see/modify any entities in Tenant B
  - Non-admin user CAN see all entities within their own tenant (collaboration model)
  - Admin can see all entities across all tenants
- [ ] Add tests for 401/403 response codes on unauthorized access
- [ ] Add tests for `X-Api-Key` resolving to synthetic admin identity

### 8.4 Database Backend Test Coverage

- [ ] **SQL Server in test matrix**: Once `SqlServerDatabaseDriver` exists and `DatabaseDriverFactory` no longer throws `NotSupportedException`, the test runner (`Armada.Test.Database`) works for SQL Server automatically via `--type sqlserver`
- [ ] Add tenant/user/credential CRUD tests to `DatabaseTestRunner` for all 4 backends (SQLite, MySQL, PostgreSQL, SQL Server)
- [ ] Verify compound index performance: run enumeration tests with tenant-scoped queries and confirm index usage on each backend
- [ ] Test the default data seeding path on each backend (fresh database → default tenant/user/credential created)

### 8.5 Update Existing Tests

- [ ] Update existing test helpers to create default tenant/user/credential before running
- [ ] Update existing API tests to include authentication headers
- [ ] Ensure all existing tests pass with the new auth requirements

---

## Phase 9: Documentation

### 9.1 REST API Documentation

- [ ] Update `docs/REST_API.md`:
  - Add Authentication section (bearer tokens, encrypted session tokens, X-Token header, deprecated X-Api-Key)
  - Document `POST /api/v1/authenticate`
  - Document `GET /api/v1/whoami`
  - Document `POST /api/v1/onboarding`
  - Document `POST /api/v1/tenants/lookup`
  - Document tenant CRUD endpoints
  - Document user CRUD endpoints
  - Document credential CRUD endpoints
  - Update all existing endpoint docs to note authentication requirement
  - Document admin vs non-admin access patterns
  - Include the authorization matrix from Phase 0

### 9.2 MCP API Documentation

- [ ] Update `docs/MCP_API.md`:
  - Note that MCP remains unauthenticated
  - Note that MCP operations run in default tenant context
  - Add future plans note for MCP authentication

### 9.3 Postman Collection

- [ ] Update `Armada.postman_collection.json`:
  - Add authentication folder with authenticate, whoami, tenant lookup requests
  - Add tenant, user, credential CRUD requests
  - Add onboarding request
  - Add X-Token header variable to collection
  - Add pre-request script to auto-authenticate and set token variable
  - Update all existing requests to include authentication headers (Bearer or X-Token)

### 9.4 README and CHANGELOG

- [ ] Update `README.md`:
  - Update architecture section to mention multi-tenancy
  - Update quick start to mention default credentials
  - Add section on authentication (bearer tokens, session tokens)
  - Note deprecation of X-Api-Key in favor of bearer tokens
  - Update Docker section with new compose.yaml (server + dashboard)
  - Note dashboard is now a separate React app
- [ ] Update `CHANGELOG.md`:
  - Add v0.3.0 section
  - List: multi-tenant support, tenant/user/credential models, bearer token authentication, encrypted session tokens, React dashboard, Docker compose with dashboard, onboarding, admin views, X-Api-Key deprecation

---

## Execution Order Summary

The phases MUST be executed in strict backend-first order:

1. **Phase 0** (Authorization Matrix) — prerequisite, defines the access control contract
2. **Phase 1** (Core Data Model) — all types and constants
3. **Phase 2** (Database Layer) — starts with 2.0 factory refactor prerequisite, then schema migrations (all 4 backends: SQLite v13, MySQL v3, PostgreSQL v1, SQL Server v1), interfaces, implementations, seeding
4. **Phase 3** (Authentication and Authorization) — bearer auth, session tokens, API key migration
5. **Phase 4** (New API Endpoints) — auth, whoami, tenant lookup, CRUD, onboarding
6. **Phase 5** (Tenant Fencing on Existing Endpoints + Embedded Dashboard Login) — can partially parallel with Phase 4; includes 5.6 embedded dashboard login flow
7. **Phase 6** (React Dashboard) — **OPTIONAL**, for Docker/production deployments only; the embedded dashboard (Phase 5.6) covers single-user workflows
8. **Phase 7** (Docker) — depends on Phase 6 only if React dashboard is being built; server-only Docker works without it
9. **Phase 8** (Tests) — can be written incrementally alongside each phase; covers all 4 database backends
10. **Phase 9** (Documentation) — last, after all code is stable

**Key principles:**
- Do not start React dashboard work (Phase 6) until the backend auth system is stable and tested (Phases 3-5)
- The embedded dashboard stays in `Armada.Server` permanently — Phase 6 is additive, not a replacement
- All 4 database backends (SQLite, MySQL, PostgreSQL, SQL Server) are first-class citizens with comprehensive compound indexes
- Single-user workflow must remain zero-config: run `armada`, visit dashboard, login with `admin@armada` / `password`

**Estimated mission count for Armada voyages:** 15-25 missions depending on granularity, with natural groupings by phase.
