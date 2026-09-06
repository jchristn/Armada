# Armada REST API Reference

**Version:** 0.9.0
**Base URL:** `http://localhost:7890`
**Content-Type:** `application/json`

Machine-readable OpenAPI is available at `/openapi.json`, and the interactive Swagger UI is available at `/swagger`. When this document and the live server ever diverge, the OpenAPI output is the canonical route and schema source.

---

## Table of Contents

- [Authentication](#authentication)
  - [Bearer Token (Recommended)](#bearer-token-recommended)
  - [Encrypted Session Token](#encrypted-session-token)
  - [API Key (Deprecated)](#api-key-deprecated)
  - [Authorization Tiers](#authorization-tiers)
  - [Authorization Matrix](#authorization-matrix)
- [Pagination](#pagination)
- [Error Responses](#error-responses)
- [Endpoints](#endpoints)
  - [Authentication Endpoints](#authentication-endpoints)
  - [Tenant Management](#tenant-management)
  - [User Management](#user-management)
  - [Credential Management](#credential-management)
  - [Status](#status)
  - [Fleets](#fleets)
  - [Vessels](#vessels)
  - [Workspace](#workspace)
  - [Voyages](#voyages)
  - [Missions](#missions)
  - [Captains](#captains)
  - [Planning Sessions](#planning-sessions)
  - [Objectives](#objectives)
  - [Signals](#signals)
  - [Events](#events)
  - [Docks](#docks)
  - [Merge Queue](#merge-queue)
  - [Harbors](#harbors)
  - [Workflow Profiles](#workflow-profiles)
  - [Check Runs](#check-runs)
  - [Environments](#environments)
  - [Deployments](#deployments)
  - [Releases](#releases)
  - [Incidents](#incidents)
  - [Runbooks](#runbooks)
  - [History](#history)
  - [Runtime Helpers](#runtime-helpers)
  - [Request History](#request-history)
  - [Token Usage](#token-usage)
  - [Playbooks](#playbooks)
  - [Prompt Templates](#prompt-templates)
  - [Personas](#personas)
  - [Pipelines](#pipelines)
  - [Model Endpoints](#model-endpoints)
  - [Backup and Restore](#backup-and-restore)
  - [OpenAPI Discovery](#openapi-discovery)
- [Data Types](#data-types)
  - [Models](#models)
  - [Enumerations](#enumerations)
  - [Request Types](#request-types)
  - [Response Wrappers](#response-wrappers)

---

## Authentication

As of v0.3.0, Armada supports multi-tenant authentication. All endpoints (except those listed as exempt) require authentication. There are three authentication methods, evaluated in the following order:

### Bearer Token (Recommended)

Bearer tokens are the canonical authentication mechanism. Each token is a 64-character random alphanumeric string stored in a `Credential` record, linked to a specific tenant and user.

```
Authorization: Bearer <token>
```

The default installation seeds a credential with bearer token `default`, so `Authorization: Bearer default` works out of the box for single-user setups.

### Encrypted Session Token

Session tokens are self-contained, AES-256-CBC encrypted tokens with a 24-hour lifetime. They are returned by `POST /api/v1/authenticate` and are intended for interactive/dashboard use. No server-side session storage is required -- the token contains the tenant ID, user ID, and expiration timestamp, validated by decryption.

```
X-Token: <encrypted-session-token>
```

### API Key (Deprecated)

The `X-Api-Key` header is retained for backward compatibility. When `ApiKey` is configured in settings, the server creates a synthetic admin tenant (`ten_system`) and user (`usr_system`) on startup. The API key resolves to this synthetic admin identity through the same `AuthContext` path as all other auth methods.

```
X-Api-Key: your-api-key-here
```

> **Deprecation notice:** `X-Api-Key` will be removed in a future version. Migrate to bearer tokens for new integrations.

### Authorization Tiers

All endpoints fall into one of three authorization levels:

| Level | Description |
|-------|-------------|
| `NoAuthRequired` | Health check, tenant lookup, onboarding, authenticate |
| `Authenticated` | All operational CRUD within the caller's tenant |
| `AdminOnly` | Tenant/user/credential management (with self-read exceptions) |

Armada distinguishes three effective caller roles:

- `IsAdmin = true`: global system admin. Can access any tenant and any object in the system.
- `IsAdmin = false`, `IsTenantAdmin = true`: tenant-scoped admin. Can access and manage any object within the caller's tenant, including users and credentials in that tenant.
- `IsAdmin = false`, `IsTenantAdmin = false`: regular user. Can read tenant-scoped operational data within the caller's tenant, but self-service routes are limited to the caller's own user account and own credentials. Server-controlled fields and protected resources cannot be modified directly.

Operational entities persist both `TenantId` and `UserId`. Those ownership columns are indexed and enforced by foreign keys across SQLite, PostgreSQL, SQL Server, and MySQL.

### Authorization Matrix

| Endpoint | Method | Permission | Notes |
|----------|--------|------------|-------|
| `/api/v1/server/stop` | POST | NoAuthRequired\*\*\* | When `RequireAuthForShutdown` is `true`, requires global admin (`IsAdmin = true`) |
| `/api/v1/status/health` | GET | NoAuthRequired | |
| `/api/v1/authenticate` | POST | NoAuthRequired | |
| `/api/v1/tenants/lookup` | POST | NoAuthRequired | Input: email, returns matching tenants |
| `/api/v1/onboarding` | POST | NoAuthRequired | Gated by `AllowSelfRegistration` setting |
| `/api/v1/whoami` | GET | Authenticated | |
| `/api/v1/status` | GET | Authenticated | Tenant-scoped |
| `/api/v1/settings` | GET | AdminOnly | Server configuration and remote-control settings |
| `/api/v1/settings` | PUT | AdminOnly | Partial update of server configuration and remote-control settings |
| `/api/v1/fleets` | ALL | Authenticated | Tenant-scoped |
| `/api/v1/vessels` | ALL | Authenticated | Tenant-scoped |
| `/api/v1/captains` | ALL | Authenticated | Tenant-scoped |
| `/api/v1/missions` | ALL | Authenticated | Tenant-scoped |
| `/api/v1/voyages` | ALL | Authenticated | Tenant-scoped |
| `/api/v1/docks` | ALL | Authenticated | Tenant-scoped |
| `/api/v1/workspace/vessels/{vesselId}/...` | ALL | Authenticated | Vessel-scoped workspace browsing/editing rooted at the vessel working directory |
| `/api/v1/planning-sessions` | GET | Authenticated | Planning-session list in caller scope |
| `/api/v1/planning-sessions` | POST | TenantAdmin | Create one planning session in caller scope |
| `/api/v1/planning-sessions/{id}` | GET | Authenticated | Read one planning session in caller scope |
| `/api/v1/planning-sessions/{id}` | DELETE | TenantAdmin | Delete one planning session in caller scope |
| `/api/v1/planning-sessions/{id}/messages` | POST | TenantAdmin | Send one planning turn |
| `/api/v1/planning-sessions/{id}/summarize` | POST | TenantAdmin | Generate a dispatch draft without launching |
| `/api/v1/planning-sessions/{id}/dispatch` | POST | TenantAdmin | Launch a voyage from planning output |
| `/api/v1/planning-sessions/{id}/stop` | POST | TenantAdmin | Stop an active planning session |
| `/api/v1/objectives` | GET | Authenticated | List scoped objective/intake records |
| `/api/v1/objectives` | POST | TenantAdmin | Create one scoped objective/intake record |
| `/api/v1/objectives/enumerate` | POST | TenantAdmin | Body/query-driven objective enumeration |
| `/api/v1/objectives/reorder` | POST | TenantAdmin | Apply explicit ranked backlog updates |
| `/api/v1/objectives/import/github` | POST | TenantAdmin | Import or refresh one objective from GitHub issue or pull-request metadata |
| `/api/v1/objectives/{id}` | GET | Authenticated | Read one objective in scope |
| `/api/v1/objectives/{id}` | PUT/DELETE | TenantAdmin | Update or delete one objective in scope |
| `/api/v1/backlog` | GET | Authenticated | List backlog items using the user-facing backlog alias |
| `/api/v1/backlog` | POST | TenantAdmin | Create one backlog item using the user-facing backlog alias |
| `/api/v1/backlog/enumerate` | POST | TenantAdmin | Body/query-driven backlog enumeration |
| `/api/v1/backlog/reorder` | POST | TenantAdmin | Apply explicit ranked backlog updates using backlog terminology |
| `/api/v1/backlog/{id}` | GET | Authenticated | Read one backlog item in scope |
| `/api/v1/backlog/{id}` | PUT/DELETE | TenantAdmin | Update or delete one backlog item in scope |
| `/api/v1/objectives/{id}/refinement-sessions` | GET | Authenticated | List refinement sessions linked to one objective |
| `/api/v1/objectives/{id}/refinement-sessions` | POST | TenantAdmin | Create one refinement session for one objective |
| `/api/v1/backlog/{id}/refinement-sessions` | GET | Authenticated | List refinement sessions linked to one backlog item |
| `/api/v1/backlog/{id}/refinement-sessions` | POST | TenantAdmin | Create one refinement session for one backlog item |
| `/api/v1/objective-refinement-sessions/{id}` | GET | Authenticated | Read one refinement session with transcript detail |
| `/api/v1/objective-refinement-sessions/{id}/messages` | POST | TenantAdmin | Append one refinement transcript message |
| `/api/v1/objective-refinement-sessions/{id}/summarize` | POST | TenantAdmin | Generate a structured refinement summary |
| `/api/v1/objective-refinement-sessions/{id}/apply` | POST | TenantAdmin | Apply a refinement summary back to the linked backlog item |
| `/api/v1/objective-refinement-sessions/{id}/stop` | POST | TenantAdmin | Stop an active refinement session |
| `/api/v1/objective-refinement-sessions/{id}` | DELETE | TenantAdmin | Delete one refinement session and its transcript |
| `/api/v1/signals` | ALL | Authenticated | Tenant-scoped |
| `/api/v1/events` | ALL | Authenticated | Tenant-scoped |
| `/api/v1/merge-queue` | ALL | Authenticated | Tenant-scoped |
| `/api/v1/harbors` | GET/POST | Authenticated | Tenant-scoped. List returns a plain array. |
| `/api/v1/harbors/{id}` | GET/PUT/DELETE | Authenticated | Read, update (`name`, `maxConcurrentJobs`, `enabled`), or delete one Harbor in scope |
| `/api/v1/harbors/{id}/enable` | POST | Authenticated | Enable one Harbor |
| `/api/v1/harbors/{id}/disable` | POST | Authenticated | Disable one Harbor; it keeps its docks but receives no new missions |
| `/api/v1/workflow-profiles` | GET/POST/PUT/DELETE | Authenticated / TenantAdmin | Reads are tenant-scoped for any authenticated user. Mutations require tenant admin. |
| `/api/v1/workflow-profiles/validate` | POST | Authenticated | Validate a workflow profile without saving it |
| `/api/v1/workflow-profiles/resolve/vessels/{vesselId}` | GET | Authenticated | Resolve the active workflow profile for one vessel |
| `/api/v1/workflow-profiles/preview/vessels/{vesselId}` | GET | Authenticated | Preview the resolved workflow commands for one vessel |
| `/api/v1/check-runs` | GET/POST | Authenticated | List or execute structured checks within caller scope |
| `/api/v1/check-runs/import` | POST | Authenticated | Import an externally executed/provider check into Armada history |
| `/api/v1/check-runs/sync/github-actions` | POST | Authenticated | Pull recent GitHub Actions workflow runs into Armada check history |
| `/api/v1/check-runs/{id}` | GET/DELETE | Authenticated | Read or delete one structured check run within scope |
| `/api/v1/check-runs/{id}/retry` | POST | Authenticated | Re-run a prior structured check |
| `/api/v1/environments` | GET | Authenticated | List deployment environments in caller scope |
| `/api/v1/environments` | POST | Authenticated | Create one deployment environment |
| `/api/v1/environments/enumerate` | POST | Authenticated | Body/query-driven environment enumeration |
| `/api/v1/environments/{id}` | GET/PUT/DELETE | Authenticated | Read, update, or delete one environment in scope |
| `/api/v1/deployments` | GET/POST | Authenticated | List or create deployment records in caller scope |
| `/api/v1/deployments/enumerate` | POST | Authenticated | Body/query-driven deployment enumeration |
| `/api/v1/deployments/{id}` | GET/PUT/DELETE | Authenticated | Read, update, or delete one deployment in scope |
| `/api/v1/deployments/{id}/approve` | POST | Authenticated | Approve a pending deployment |
| `/api/v1/deployments/{id}/deny` | POST | Authenticated | Deny a pending deployment |
| `/api/v1/deployments/{id}/verify` | POST | Authenticated | Re-run deployment verification |
| `/api/v1/deployments/{id}/rollback` | POST | Authenticated | Run the configured rollback flow |
| `/api/v1/releases` | GET | Authenticated | List release records in caller scope |
| `/api/v1/releases` | POST | AdminOnly | Tenant admin or global admin only |
| `/api/v1/releases/{id}` | GET | Authenticated | Read one release in scope |
| `/api/v1/releases/{id}/github/pull-requests` | GET | Authenticated | Read GitHub PR evidence derived from linked mission PR URLs |
| `/api/v1/releases/{id}` | PUT/DELETE | AdminOnly | Tenant admin or global admin only |
| `/api/v1/releases/{id}/refresh` | POST | AdminOnly | Tenant admin or global admin only |
| `/api/v1/incidents` | GET/POST | Authenticated | List or create incident records in caller scope |
| `/api/v1/incidents/enumerate` | POST | Authenticated | Body/query-driven incident enumeration |
| `/api/v1/incidents/{id}` | GET/PUT/DELETE | Authenticated | Read, update, or delete one incident in scope |
| `/api/v1/runbooks` | GET/POST | Authenticated | List or create playbook-backed runbooks in caller scope |
| `/api/v1/runbooks/enumerate` | POST | Authenticated | Body/query-driven runbook enumeration |
| `/api/v1/runbooks/{id}` | GET/PUT/DELETE | Authenticated | Read, update, or delete one runbook in scope |
| `/api/v1/runbook-executions` | GET | Authenticated | List runbook executions in caller scope |
| `/api/v1/runbook-executions/enumerate` | POST | Authenticated | Body/query-driven runbook-execution enumeration |
| `/api/v1/runbook-executions/{id}` | GET/PUT/DELETE | Authenticated | Read, update, or delete one runbook execution in scope |
| `/api/v1/runbooks/{id}/executions` | POST | Authenticated | Start one runbook execution |
| `/api/v1/history` | GET | Authenticated | Cross-entity timeline of current Armada lifecycle entities |
| `/api/v1/history/enumerate` | POST | Authenticated | Body/query-driven timeline enumeration |
| `/api/v1/request-history` | GET | Authenticated | Regular user: own entries. Tenant admin: tenant entries. Global admin: all entries |
| `/api/v1/request-history/{id}` | GET | Authenticated | Read one captured request within scope |
| `/api/v1/request-history/{id}` | DELETE | AdminOnly | Tenant admin or global admin only |
| `/api/v1/request-history/delete/multiple` | POST | AdminOnly | Tenant admin or global admin only |
| `/api/v1/request-history/delete/by-filter` | POST | AdminOnly | Tenant admin or global admin only |
| `/api/v1/request-history/summary` | GET | Authenticated | Summary cards/charts for visible request history |
| `/api/v1/token-usage/summary` | GET | Authenticated | Token usage aggregated into time buckets + per-model totals (dashboard charts) |
| `/api/v1/token-usage` | GET | Authenticated | Paginated token-usage records within scope |
| `/api/v1/token-usage/delete/by-filter` | POST | AdminOnly | Tenant admin or global admin only |
| `/api/v1/missions/{id}/github/pull-request` | GET | Authenticated | Read GitHub PR review, comment, and required-check evidence for one mission |
| `/api/v1/missions/{id}/evaluate-autoland` | GET | Authenticated | Dry-run the vessel's auto-land predicate against the mission diff |
| `/api/v1/runtimes/mux/endpoints` | GET | Authenticated | List saved Mux endpoints, optionally from `configDirectory` |
| `/api/v1/runtimes/mux/endpoints/{name}` | GET | Authenticated | Show one saved Mux endpoint |
| `/api/v1/playbooks` | GET/POST/PUT/DELETE | Authenticated / TenantAdmin | Reads are tenant-scoped for any authenticated user. Mutations require tenant admin. |
| `/api/v1/prompt-templates` | ALL | Authenticated | Tenant-scoped |
| `/api/v1/personas` | ALL | Authenticated | Tenant-scoped |
| `/api/v1/pipelines` | ALL | Authenticated | Tenant-scoped |
| `/api/v1/model-endpoints` | GET/POST/PUT/DELETE | Authenticated | Tenant-scoped. The stored `ApiKey` is write-only and never returned on reads. |
| `/api/v1/model-endpoints/{id}/validate` | POST | Authenticated | Issue a real embedding or completion request against one endpoint and persist its health status |
| `/api/v1/model-endpoints/health-check` | POST | Authenticated | Probe all enabled endpoints, deduplicated by base URL |
| `/api/v1/tenants` | GET (list) | AdminOnly | Global admin only |
| `/api/v1/tenants` | POST | AdminOnly | Global admin only |
| `/api/v1/tenants/{id}` | GET | Authenticated | Global admin: any; tenant admin or regular user: own tenant only |
| `/api/v1/tenants/{id}` | PUT/DELETE | AdminOnly | Global admin only |
| `/api/v1/users` | GET (list) | AdminOnly | Global admin: all users. Tenant admin: users in own tenant |
| `/api/v1/users` | POST | AdminOnly | Global admin: any tenant. Tenant admin: own tenant only |
| `/api/v1/users/{id}` | GET | Authenticated | Global admin: any. Tenant admin: users in own tenant. Regular user: self only |
| `/api/v1/users/{id}` | PUT/DELETE | AdminOnly | Global admin: any. Tenant admin: users in own tenant. Regular user: self-update only |
| `/api/v1/credentials` | GET (list) | Authenticated | Global admin: all. Tenant admin: credentials in own tenant. Regular user: own only |
| `/api/v1/credentials` | POST | Authenticated | Global admin: any tenant/user. Tenant admin: own tenant. Regular user: self only |
| `/api/v1/credentials/{id}` | GET | Authenticated | Global admin: any. Tenant admin: own tenant. Regular user: own only |
| `/api/v1/credentials/{id}` | PUT | Authenticated | Global admin: any. Tenant admin: own tenant. Regular user: own only |
| `/api/v1/credentials/{id}` | DELETE | Authenticated | Global admin: any. Tenant admin: own tenant. Regular user: own only |

**Exempt routes** (no authentication required):
- `GET /api/v1/status/health`
- `POST /api/v1/authenticate`
- `POST /api/v1/tenants/lookup`
- `POST /api/v1/onboarding` (when `AllowSelfRegistration` is enabled)
- `POST /api/v1/server/stop` (when `RequireAuthForShutdown` is `false`, the default)
- `GET /dashboard` and all `/dashboard/*` paths
- `GET /` (redirects to `/dashboard`)

---

## Pagination

All list endpoints return paginated results wrapped in `EnumerationResult<T>`. There are two ways to query:

### GET with Query String Parameters

```
GET /api/v1/missions?pageNumber=2&pageSize=25&status=InProgress&order=CreatedAscending
```

### POST /enumerate with JSON Body

```
POST /api/v1/missions/enumerate
Content-Type: application/json

{
  "PageNumber": 2,
  "PageSize": 25,
  "Status": "InProgress",
  "Order": "CreatedAscending"
}
```

Query string parameters **override** body values on POST enumerate endpoints, allowing defaults in the body with per-request overrides via URL.

### Pagination Parameters

| Parameter | Type | Default | Range | Description |
|---|---|---|---|---|
| `pageNumber` | int | 1 | >= 1 | Page number (1-based) |
| `pageSize` | int | 100 | 1 - 1000 | Results per page |
| `order` | string | `CreatedDescending` | `CreatedAscending`, `CreatedDescending` | Sort order by creation date |
| `createdAfter` | datetime | null | ISO 8601 | Filter: created after this timestamp |
| `createdBefore` | datetime | null | ISO 8601 | Filter: created before this timestamp |

### Entity-Specific Filters

| Parameter | Applies To | Description |
|---|---|---|
| `status` | missions, voyages, captains | Filter by status value |
| `fleetId` | vessels | Filter by fleet ID |
| `vesselId` | missions, docks, events | Filter by vessel ID |
| `captainId` | missions, docks, events | Filter by captain ID |
| `voyageId` | missions, events | Filter by voyage ID |
| `missionId` | events | Filter by mission ID |
| `type` | events | Filter by event type (alias for `eventType`) |
| `signalType` | signals | Filter by signal type |
| `toCaptainId` | signals | Filter by recipient captain ID |
| `unreadOnly` | signals | `true` to return only unread signals |

### Paginated Response Shape

```json
{
  "Success": true,
  "PageNumber": 1,
  "PageSize": 25,
  "TotalPages": 4,
  "TotalRecords": 87,
  "Objects": [ ... ],
  "TotalMs": 3.14
}
```

---

## Error Responses

All error responses use a consistent JSON format with `Error`, `Description`, `Message`, and `Data` fields:

```json
{
  "Error": "NotFound",
  "Description": "The requested resource was not found.",
  "Message": "Mission not found",
  "Data": {}
}
```

| Field | Type | Description |
|---|---|---|
| `Error` | string | Error code (see table below) |
| `Description` | string | Standard description for the error code |
| `Message` | string | Human-readable message with specific details |
| `Data` | object | Additional context (usually empty) |

### Error Codes

| Error Code | HTTP Status | When Used |
|---|---|---|
| `BadRequest` | 400 | Invalid input, missing required fields, malformed request body, invalid state transition |
| `DeserializationError` | 400 | Request body could not be parsed as valid JSON or does not match the expected type |
| `Unauthorized` | 401 | Missing or invalid API key / bearer token |
| `Forbidden` | 403 | Authenticated but not authorized for this operation |
| `NotFound` | 404 | Entity not found by the given ID |
| `Conflict` | 409 | Operation conflicts with current state (e.g., deleting an active voyage, retry landing failed) |
| `InternalError` | 500 | Unexpected server error |

### Notes

- The `Error` field always contains one of the error codes listed above.
- The `Message` field provides a specific, actionable description of what went wrong.
- HTTP status codes are set on the response and match the error code mapping above.
- Clients should check the HTTP status code first, then parse the response body for details.

---

## Endpoints

### Authentication Endpoints

#### POST /api/v1/authenticate

Authenticate with email and password to receive an encrypted session token. This is the login endpoint used by the dashboard.

**Permission:** NoAuthRequired

**Request Body:** [AuthenticateRequest](#authenticaterequest)

```json
{
  "TenantId": "default",
  "Email": "admin@armada",
  "Password": "password"
}
```

**Response:** `200 OK` - [AuthenticateResult](#authenticateresult)

```json
{
  "Success": true,
  "Token": "eyJhbGciOi...",
  "ExpiresUtc": "2026-03-16T12:00:00Z"
}
```

**Errors:**
- `400 Bad Request` - Missing required fields (TenantId, Email, Password)
- `401 Unauthorized` - Invalid credentials or inactive tenant/user

---

#### GET /api/v1/whoami

Returns the authenticated user's tenant and user information.

**Permission:** Authenticated

**Request Headers:** `Authorization: Bearer <token>` or `X-Token: <session-token>`

**Response:** `200 OK` - [WhoAmIResult](#whoamiresult)

```json
{
  "Tenant": {
    "Id": "default",
    "Name": "Default Tenant",
    "Active": true,
    "CreatedUtc": "2026-03-07T12:00:00Z",
    "LastUpdateUtc": "2026-03-07T12:00:00Z"
  },
  "User": {
    "Id": "default",
    "TenantId": "default",
    "Email": "admin@armada",
    "PasswordSha256": "********",
    "FirstName": null,
    "LastName": null,
    "IsAdmin": true,
    "Active": true,
    "CreatedUtc": "2026-03-07T12:00:00Z",
    "LastUpdateUtc": "2026-03-07T12:00:00Z"
  }
}
```

**Errors:**
- `401 Unauthorized` - Not authenticated

---

#### POST /api/v1/tenants/lookup

Look up which tenants a given email address belongs to. Used by the dashboard login flow to determine the tenant before authentication.

**Permission:** NoAuthRequired

**Request Body:** [TenantLookupRequest](#tenantlookuprequest)

```json
{
  "Email": "admin@armada"
}
```

**Response:** `200 OK` - [TenantLookupResult](#tenantlookupresult)

```json
{
  "Tenants": [
    {
      "Id": "default",
      "Name": "Default Tenant"
    }
  ]
}
```

**Errors:**
- `400 Bad Request` - Missing email

---

#### POST /api/v1/onboarding

Self-register a new user within an existing tenant. Requires `AllowSelfRegistration` to be enabled in settings (default: `true`).

Creates a new user and an associated bearer token credential.

**Permission:** NoAuthRequired (gated by `AllowSelfRegistration` setting)

**Request Body:** [OnboardingRequest](#onboardingrequest)

```json
{
  "TenantId": "default",
  "Email": "newuser@example.com",
  "Password": "securepassword",
  "FirstName": "Jane",
  "LastName": "Doe"
}
```

**Response:** `200 OK` - [OnboardingResult](#onboardingresult)

```json
{
  "Success": true,
  "Tenant": {
    "Id": "default",
    "Name": "Default Tenant",
    "Active": true,
    "CreatedUtc": "2026-03-07T12:00:00Z",
    "LastUpdateUtc": "2026-03-07T12:00:00Z"
  },
  "User": {
    "Id": "usr_abc123",
    "TenantId": "default",
    "Email": "newuser@example.com",
    "PasswordSha256": "********",
    "FirstName": "Jane",
    "LastName": "Doe",
    "IsAdmin": false,
    "Active": true,
    "CreatedUtc": "2026-03-07T12:00:00Z",
    "LastUpdateUtc": "2026-03-07T12:00:00Z"
  },
  "Credential": {
    "Id": "crd_abc123",
    "TenantId": "default",
    "UserId": "usr_abc123",
    "Name": null,
    "BearerToken": "aBcDeFgH...",
    "Active": true,
    "CreatedUtc": "2026-03-07T12:00:00Z",
    "LastUpdateUtc": "2026-03-07T12:00:00Z"
  },
  "ErrorMessage": null
}
```

**Errors:**
- `400 Bad Request` - Missing required fields, email already exists in tenant
- `403 Forbidden` - Self-registration is disabled
- `404 Not Found` - Tenant not found

---

### Tenant Management

> **Permission:** Global admin for list, create, update, delete. Authenticated users can read their own tenant.

#### GET /api/v1/tenants

List all tenants (paginated). Global admin only.

**Response:** `200 OK` - [EnumerationResult](#enumerationresult)\<[TenantMetadata](#tenantmetadata)\>

---

#### POST /api/v1/tenants/enumerate

Enumerate tenants with filtering and sorting via JSON body. Global admin only.

---

#### POST /api/v1/tenants

Create a new tenant. Global admin only.

**Request Body:** [TenantMetadata](#tenantmetadata)

```json
{
  "Name": "Acme Corp"
}
```

**Response:** `201 Created` - [TenantMetadata](#tenantmetadata)

---

#### GET /api/v1/tenants/{id}

Get a tenant by ID. Non-admin users can only read their own tenant.

**Response:** `200 OK` - [TenantMetadata](#tenantmetadata)

---

#### PUT /api/v1/tenants/{id}

Update a tenant. Global admin only.

`Id`, `CreatedUtc`, `LastUpdateUtc`, and `IsProtected` are preserved server-side.

**Request Body:** [TenantMetadata](#tenantmetadata)

**Response:** `200 OK` - [TenantMetadata](#tenantmetadata)

---

#### DELETE /api/v1/tenants/{id}

Delete a tenant. Global admin only.

If the tenant is protected, the server returns `403 Forbidden`.

Deleting an unprotected tenant cascades through all tenant-scoped subordinate resources, including protected users and credentials seeded for that tenant.

The delete flow is ownership-aware: direct delete of a protected tenant, user, or credential returns `403`, but protected child auth records can still be removed as part of an allowed parent delete.

**Response:** `200 OK`

---

### User Management

> **Permission:** Global admins can manage users across the system. Tenant admins can manage users within their own tenant. Regular users can read and update only their own user record.

#### GET /api/v1/users

List users (paginated). Global admins can list all users. Tenant admins can list users in their own tenant.

**Response:** `200 OK` - [EnumerationResult](#enumerationresult)\<[UserMaster](#usermaster)\>

Password fields are redacted in responses.

---

#### POST /api/v1/users/enumerate

Enumerate users with filtering and sorting via JSON body. Global admins can enumerate all users. Tenant admins are limited to their own tenant.

---

#### POST /api/v1/users

Create a new user. Global admins can create users in any tenant. Tenant admins can create users only in their own tenant and cannot grant global admin.

`IsProtected` is server-controlled and ignored if supplied by the client.
`Password` is plaintext in the request body and is hashed server-side before persistence. `PasswordSha256` is accepted only for backward compatibility.

**Request Body:** user upsert payload

```json
{
  "TenantId": "default",
  "Email": "newuser@example.com",
  "Password": "securepassword",
  "FirstName": "Jane",
  "LastName": "Doe",
  "IsAdmin": false,
  "IsTenantAdmin": false
}
```

**Response:** `201 Created` - [UserMaster](#usermaster) (password redacted)

---

#### GET /api/v1/users/{id}

Get a user by ID. Global admins can read any user. Tenant admins can read users in their own tenant. Regular users can read only their own user record.

**Response:** `200 OK` - [UserMaster](#usermaster) (password redacted)

---

#### PUT /api/v1/users/{id}

Update a user. Global admins can update any user. Tenant admins can update users in their own tenant. Regular users can update only their own user record.

`Id`, `TenantId`, `CreatedUtc`, `LastUpdateUtc`, and `IsProtected` are server-controlled and cannot be modified by API clients.
If `Password` is supplied, the server hashes and stores the new password. If `Password` is omitted or empty, the current password is preserved.

**Request Body:** user upsert payload

```json
{
  "Email": "updated@example.com",
  "Password": "newpassword",
  "FirstName": "Jane",
  "LastName": "Smith",
  "IsAdmin": false,
  "IsTenantAdmin": false,
  "Active": true
}
```

**Response:** `200 OK` - [UserMaster](#usermaster) (password redacted)

---

#### DELETE /api/v1/users/{id}

Delete a user. Global admins can delete any unprotected user. Tenant admins can delete unprotected users in their own tenant. Regular users cannot delete users directly.

If the user is protected, the server returns `403 Forbidden`.

Deleting an unprotected user cascades through that user's subordinate resources inside the tenant.

**Response:** `200 OK`

---

### Credential Management

> **Permission:** Authenticated. Global admins can manage all credentials. Tenant admins can manage credentials in their own tenant. Regular users can list, read, create, update, and delete only their own credentials.

#### GET /api/v1/credentials

List credentials (paginated). Global admin: all credentials. Tenant admin: credentials in own tenant. Regular user: own credentials only.

**Response:** `200 OK` - [EnumerationResult](#enumerationresult)\<[Credential](#credential)\>

---

#### POST /api/v1/credentials/enumerate

Enumerate credentials with filtering and sorting via JSON body. Results are scoped by role.

---

#### POST /api/v1/credentials

Create a new credential (bearer token). A bearer token is auto-generated if not provided. Admin: can create for any tenant/user. Non-admin: can create for self only.

`IsProtected` is server-controlled and ignored if supplied by the client.

**Request Body:** [Credential](#credential)

```json
{
  "TenantId": "default",
  "UserId": "default",
  "Name": "My API Token"
}
```

**Response:** `201 Created` - [Credential](#credential)

---

#### GET /api/v1/credentials/{id}

Get a credential by ID. Non-admin users can only read their own credentials.

**Response:** `200 OK` - [Credential](#credential)

---

#### PUT /api/v1/credentials/{id}

Update a credential. Global admins can update any credential. Tenant admins can update credentials inside their tenant. Regular users can update only their own credentials.

`Id`, `TenantId`, `UserId`, `CreatedUtc`, `LastUpdateUtc`, and `IsProtected` are server-controlled and cannot be modified by API clients.

**Request Body:** [Credential](#credential)

**Response:** `200 OK` - [Credential](#credential)

---

#### DELETE /api/v1/credentials/{id}

Delete a credential. Global admin: any. Tenant admin: credentials in own tenant. Regular user: own credentials only.

If the credential is protected, the server returns `403 Forbidden`.

**Response:** `200 OK`

---

### Status

#### GET /api/v1/status

Returns aggregate status including captain counts, mission breakdown, active voyages, and recent signals.

**Response:** `200 OK` - [ArmadaStatus](#armadastatus)

```json
{
  "TotalCaptains": 5,
  "IdleCaptains": 2,
  "WorkingCaptains": 3,
  "StalledCaptains": 0,
  "ActiveVoyages": 1,
  "MissionsByStatus": {
    "Pending": 3,
    "InProgress": 2,
    "Complete": 10
  },
  "Voyages": [],
  "RecentSignals": [],
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
      "ArmadaVersion": "0.9.0",
      "Features": [
        "remoteControl.handshake",
        "remoteControl.heartbeat",
        "status.health",
        "status.snapshot",
        "settings.remoteControl"
      ]
    }
  },
  "TimestampUtc": "2026-03-07T12:00:00Z"
}
```

---

#### GET /api/v1/status/health

Health check endpoint. **Does not require authentication.**

**Response:** `200 OK`

```json
{
  "Status": "healthy",
  "Timestamp": "2026-03-07T12:00:00Z",
  "StartUtc": "2026-03-07T08:00:00Z",
  "Uptime": "0.04:00:00",
  "Version": "0.9.0",
  "Ports": {
    "Admiral": 7890,
    "Mcp": 7891
  },
  "RemoteTunnel": {
    "Enabled": false,
    "State": "Disabled",
    "TunnelUrl": null,
    "InstanceId": "armada-1f2e3d4c5b6a",
    "LastError": null,
    "ReconnectAttempts": 0,
    "LatencyMs": null
  }
}
```

---

#### GET /api/v1/settings

Returns current server settings including ports, agent configuration, system paths, and remote-control tunnel configuration.

**Response:** `200 OK`

```json
{
  "AdmiralPort": 7890,
  "McpPort": 7891,
  "MaxCaptains": 0,
  "HeartbeatIntervalSeconds": 30,
  "StallThresholdMinutes": 10,
  "IdleCaptainTimeoutSeconds": 0,
  "AutoCreatePr": false,
  "DataDirectory": "C:\\Users\\joelc\\.armada",
  "DatabasePath": "C:\\Users\\joelc\\.armada\\armada.db",
  "LogDirectory": "C:\\Users\\joelc\\.armada\\logs",
  "DocksDirectory": "C:\\Users\\joelc\\.armada\\docks",
  "ReposDirectory": "C:\\Users\\joelc\\.armada\\repos",
  "RemoteControl": {
    "Enabled": false,
    "TunnelUrl": null,
    "InstanceId": null,
    "EnrollmentToken": null,
    "ConnectTimeoutSeconds": 15,
    "HeartbeatIntervalSeconds": 30,
    "ReconnectBaseDelaySeconds": 5,
    "ReconnectMaxDelaySeconds": 60,
    "AllowInvalidCertificates": false
  }
}
```

---

#### PUT /api/v1/settings

Accepts partial updates to editable server settings. When `RemoteControl` is supplied, it replaces the full `RemoteControl` settings object.

**Request Body:** partial settings object

```json
{
  "RemoteControl": {
    "Enabled": true,
    "TunnelUrl": "wss://proxy.example.com/tunnel",
    "InstanceId": null,
    "EnrollmentToken": "bootstrap-token",
    "ConnectTimeoutSeconds": 15,
    "HeartbeatIntervalSeconds": 30,
    "ReconnectBaseDelaySeconds": 5,
    "ReconnectMaxDelaySeconds": 60,
    "AllowInvalidCertificates": false
  }
}
```

**Response:** `200 OK`

Returns the updated settings payload in the same shape as `GET /api/v1/settings`.

---

#### POST /api/v1/server/stop

Initiates a graceful shutdown of the Admiral server.

**Permission:** NoAuthRequired by default. When `RequireAuthForShutdown` is `true`, requires global admin (`IsAdmin = true`).

**Response:** `200 OK`

```json
{
  "Status": "shutting_down"
}
```

---

### Fleets

A fleet is a named collection of repositories (vessels) under management.

#### GET /api/v1/fleets

List all fleets with pagination.

**Query Parameters:** [Pagination parameters](#pagination-parameters)

**Response:** `200 OK` - [EnumerationResult](#enumerationresultt)\<[Fleet](#fleet)\>

```bash
curl http://localhost:7890/api/v1/fleets?pageSize=10
```

---

#### POST /api/v1/fleets/enumerate

Paginated enumeration of fleets with optional filtering and sorting.

**Request Body:** [EnumerationQuery](#enumerationquery) (optional)

**Response:** `200 OK` - [EnumerationResult](#enumerationresultt)\<[Fleet](#fleet)\>

```bash
curl -X POST http://localhost:7890/api/v1/fleets/enumerate \
  -H "Content-Type: application/json" \
  -d '{"PageSize": 10, "Order": "CreatedAscending"}'
```

---

#### POST /api/v1/fleets

Create a new fleet.

**Request Body:** [Fleet](#fleet)

| Field | Type | Required | Description |
|---|---|---|---|
| `Name` | string | yes | Fleet name |
| `Description` | string | no | Fleet description |

**Response:** `201 Created` - [Fleet](#fleet)

```bash
curl -X POST http://localhost:7890/api/v1/fleets \
  -H "Content-Type: application/json" \
  -d '{"Name": "Production Fleet", "Description": "Production repositories"}'
```

---

#### GET /api/v1/fleets/{id}

Get a single fleet by ID, including all its vessels.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Fleet ID (`flt_` prefix) |

**Response:** `200 OK` - `{ Fleet: Fleet, Vessels: Vessel[] }`
**Error:** `404` - Fleet not found

```bash
curl http://localhost:7890/api/v1/fleets/flt_abc123
```

---

#### PUT /api/v1/fleets/{id}

Update an existing fleet.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Fleet ID (`flt_` prefix) |

**Request Body:** [Fleet](#fleet) (fields to update)

**Response:** `200 OK` - [Fleet](#fleet)
**Error:** `404` - Fleet not found

```bash
curl -X PUT http://localhost:7890/api/v1/fleets/flt_abc123 \
  -H "Content-Type: application/json" \
  -d '{"Name": "Renamed Fleet"}'
```

---

#### DELETE /api/v1/fleets/{id}

Delete a fleet. Vessels in the fleet are not deleted; their `FleetId` is set to null.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Fleet ID (`flt_` prefix) |

**Response:** `204 No Content`

```bash
curl -X DELETE http://localhost:7890/api/v1/fleets/flt_abc123
```

---

#### `POST /api/v1/fleets/delete/multiple`

Batch delete multiple fleets from the database by ID. Returns a summary of deleted and skipped entries. **This cannot be undone.**

**Request Body:**

```json
{
  "Ids": ["flt_abc123", "flt_def456"]
}
```

**Response:** `200 OK`

```json
{
  "Status": "deleted",
  "Deleted": 2,
  "Skipped": []
}
```

Skipped entries include the entity ID and the reason (e.g., "Not found" or "Empty ID").

---

### Vessels

A vessel is a git repository registered with Armada.

#### GET /api/v1/vessels

List all vessels with pagination.

**Query Parameters:** [Pagination parameters](#pagination-parameters), plus:

| Parameter | Type | Description |
|---|---|---|
| `fleetId` | string | Filter by fleet ID |

**Response:** `200 OK` - [EnumerationResult](#enumerationresultt)\<[Vessel](#vessel)\>

```bash
curl http://localhost:7890/api/v1/vessels?fleetId=flt_abc123
```

---

#### POST /api/v1/vessels/enumerate

Paginated enumeration of vessels with optional filtering and sorting.

**Request Body:** [EnumerationQuery](#enumerationquery) (optional)

**Response:** `200 OK` - [EnumerationResult](#enumerationresultt)\<[Vessel](#vessel)\>

```bash
curl -X POST http://localhost:7890/api/v1/vessels/enumerate \
  -H "Content-Type: application/json" \
  -d '{"FleetId": "flt_abc123", "PageSize": 50}'
```

---

#### POST /api/v1/vessels

Register a new vessel (git repository).

**Request Body:** [Vessel](#vessel)

| Field | Type | Required | Description |
|---|---|---|---|
| `Name` | string | yes | Vessel name |
| `RepoUrl` | string | yes | Remote repository URL |
| `FleetId` | string | no | Fleet to assign to |
| `DefaultBranch` | string | no | Default branch name (default: `"main"`) |
| `GitHubTokenOverride` | string | no | Optional per-vessel GitHub token override. Omit to inherit the global token. Accepted only on create/update and never returned on reads. |

**Response:** `201 Created` - [Vessel](#vessel)

```bash
curl -X POST http://localhost:7890/api/v1/vessels \
  -H "Content-Type: application/json" \
  -d '{"Name": "MyRepo", "RepoUrl": "https://github.com/org/repo.git", "FleetId": "flt_abc123", "GitHubTokenOverride": "ghp_example"}'
```

---

#### GET /api/v1/vessels/{id}

Get a single vessel by ID.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Vessel ID (`vsl_` prefix) |

**Response:** `200 OK` - [Vessel](#vessel)
**Error:** `404` - Vessel not found

---

#### PUT /api/v1/vessels/{id}

Update an existing vessel.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Vessel ID (`vsl_` prefix) |

**Request Body:** [Vessel](#vessel) (fields to update)

`GitHubTokenOverride` is write-only. Omit it to preserve the current override, or send `""` / `null` to clear the stored per-vessel override and fall back to the global `GitHubToken`.

**Response:** `200 OK` - [Vessel](#vessel)
**Error:** `404` - Vessel not found

---

#### DELETE /api/v1/vessels/{id}

Delete a vessel.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Vessel ID (`vsl_` prefix) |

**Response:** `204 No Content`

---

#### `POST /api/v1/vessels/delete/multiple`

Batch delete multiple vessels from the database by ID. Returns a summary of deleted and skipped entries. **This cannot be undone.**

**Request Body:**

```json
{
  "Ids": ["vsl_abc123", "vsl_def456"]
}
```

**Response:** `200 OK`

```json
{
  "Status": "deleted",
  "Deleted": 2,
  "Skipped": []
}
```

Skipped entries include the entity ID and the reason (e.g., "Not found" or "Empty ID").

---

#### PATCH /api/v1/vessels/{id}/context

Update only the `ProjectContext`, `StyleGuide`, and `ModelContext` fields of a vessel.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Vessel ID (`vsl_` prefix) |

**Request Body:**

| Field | Type | Required | Description |
|---|---|---|---|
| `ProjectContext` | string | no | Project context describing architecture, key files, and dependencies |
| `StyleGuide` | string | no | Style guide describing naming conventions, patterns, and library preferences |
| `ModelContext` | string | no | Agent-accumulated context about this repository |

```bash
curl -X PATCH http://localhost:7890/api/v1/vessels/vsl_abc123/context \
  -H "Content-Type: application/json" \
  -d '{"ProjectContext": "C# .NET 8 project with SQLite", "StyleGuide": "Use PascalCase for public members"}'
```

**Response:** `200 OK` - [Vessel](#vessel)
**Error:** `404` - Vessel not found

---

### Voyages

A voyage is a batch of related missions tracked together.

#### GET /api/v1/voyages

List all voyages with pagination.

**Query Parameters:** [Pagination parameters](#pagination-parameters), plus:

| Parameter | Type | Description |
|---|---|---|
| `status` | string | Filter by voyage status (`Open`, `InProgress`, `Complete`, `Cancelled`) |

**Response:** `200 OK` - [EnumerationResult](#enumerationresultt)\<[Voyage](#voyage)\>

```bash
curl http://localhost:7890/api/v1/voyages?status=InProgress
```

---

#### POST /api/v1/voyages/enumerate

Paginated enumeration of voyages with optional filtering and sorting.

**Request Body:** [EnumerationQuery](#enumerationquery) (optional)

**Response:** `200 OK` - [EnumerationResult](#enumerationresultt)\<[Voyage](#voyage)\>

---

#### POST /api/v1/voyages

Create a new voyage with optional missions. Missions are automatically dispatched to the target vessel.

**Request Body:** [VoyageRequest](#voyagerequest)

| Field | Type | Required | Description |
|---|---|---|---|
| `Title` | string | yes | Voyage title |
| `Description` | string | no | Voyage description |
| `VesselId` | string | yes | Target vessel ID |
| `Missions` | array | no | List of [MissionRequest](#missionrequest) objects |
| `SelectedPlaybooks` | array | no | Ordered [SelectedPlaybook](#selectedplaybook) rows to apply to all created missions |
| `PipelineId` | string | no | Pipeline ID to use for this voyage (overrides vessel/fleet default) |
| `Pipeline` | string | no | Pipeline name to use for this voyage (alternative to `PipelineId`) |

**Response:** `201 Created` - [Voyage](#voyage)

```bash
curl -X POST http://localhost:7890/api/v1/voyages \
  -H "Content-Type: application/json" \
  -d '{
    "Title": "API Hardening",
    "Description": "Security improvements",
    "VesselId": "vsl_abc123",
    "SelectedPlaybooks": [
      {"PlaybookId": "pbk_abc123", "DeliveryMode": "InlineFullContent"}
    ],
    "Missions": [
      {"Title": "Add rate limiting", "Description": "Add rate limiting middleware"},
      {"Title": "Add input validation", "Description": "Validate all POST endpoints"}
    ]
  }'
```

---

#### GET /api/v1/voyages/{id}

Get a voyage and all its associated missions.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Voyage ID (`vyg_` prefix) |

**Response:** `200 OK` - [VoyageDetail](#voyagedetail)

```json
{
  "Voyage": { ... },
  "Missions": [ ... ]
}
```

**Error:** `404` - Voyage not found

---

#### DELETE /api/v1/voyages/{id}

Cancel a voyage. Sets the voyage status to `Cancelled` and cancels all `Pending` or `Assigned` missions. In-progress missions are not affected.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Voyage ID (`vyg_` prefix) |

**Response:** `200 OK`

```json
{
  "Voyage": { "Id": "vyg_abc123", "Status": "Cancelled", "..." : "..." },
  "CancelledMissions": 3
}
```

**Error:** `404` - Voyage not found

---

#### DELETE /api/v1/voyages/{id}/purge

Permanently delete a voyage and all its associated missions from the database. **This cannot be undone.**

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Voyage ID (`vyg_` prefix) |

**Response:** `200 OK`

```json
{
  "Status": "deleted",
  "VoyageId": "vyg_abc123",
  "MissionsDeleted": 5
}
```

**Error:** `404` - Voyage not found

---

#### `POST /api/v1/voyages/delete/multiple`

Batch delete multiple voyages and their associated missions from the database by ID. Voyages that are Open/InProgress or have active missions are skipped. Returns a summary of deleted and skipped entries. **This cannot be undone.**

**Request Body:**

```json
{
  "Ids": ["vyg_abc123", "vyg_def456"]
}
```

**Response:** `200 OK`

```json
{
  "Status": "deleted",
  "Deleted": 2,
  "Skipped": []
}
```

Skipped entries include the entity ID and the reason (e.g., "Not found", "Cannot delete voyage while status is Open", or "Cannot delete voyage with N active mission(s)").

---

### Missions

A mission is an atomic unit of work assigned to a captain (AI agent).

#### GET /api/v1/missions

List all missions with pagination.

**Query Parameters:** [Pagination parameters](#pagination-parameters), plus:

| Parameter | Type | Description |
|---|---|---|
| `status` | string | Filter by mission status |
| `vesselId` | string | Filter by vessel ID |
| `captainId` | string | Filter by captain ID |
| `voyageId` | string | Filter by voyage ID |

**Response:** `200 OK` - [EnumerationResult](#enumerationresultt)\<[Mission](#mission)\>

> **Note:** The `DiffSnapshot` field is excluded from mission list responses to keep payloads compact. Use `GET /api/v1/missions/{id}/diff` to retrieve the full diff.

```bash
curl http://localhost:7890/api/v1/missions?status=InProgress&vesselId=vsl_abc123
```

---

#### POST /api/v1/missions/enumerate

Paginated enumeration of missions with optional filtering and sorting.

**Request Body:** [EnumerationQuery](#enumerationquery) (optional)

**Response:** `200 OK` - [EnumerationResult](#enumerationresultt)\<[Mission](#mission)\>

```bash
curl -X POST http://localhost:7890/api/v1/missions/enumerate \
  -H "Content-Type: application/json" \
  -d '{"Status": "InProgress", "VesselId": "vsl_abc123", "PageSize": 25}'
```

---

#### POST /api/v1/missions

Create and dispatch a new mission. If a `VesselId` is provided, the Admiral will assign a captain and set up a worktree.

**Request Body:** [Mission](#mission)

| Field | Type | Required | Description |
|---|---|---|---|
| `Title` | string | yes | Mission title |
| `Description` | string | no | Detailed instructions for the AI agent |
| `VesselId` | string | no | Target vessel (required for auto-dispatch) |
| `VoyageId` | string | no | Parent voyage ID |
| `Priority` | int | no | Priority (lower = higher priority, default: 100) |
| `Persona` | string | no | Persona for this mission (e.g. Worker, Architect, Judge) |
| `Mode` | string | no | Execution mode: `Implementation` (default), `Audit`, or `Research`. Audit and Research are read-only modes that produce a written report instead of a commit; their empty diff is treated as success rather than a no-op failure. |
| `SelectedPlaybooks` | array | no | Ordered [SelectedPlaybook](#selectedplaybook) rows for this standalone mission |

**Response:** `201 Created` - [Mission](#mission)

```bash
curl -X POST http://localhost:7890/api/v1/missions \
  -H "Content-Type: application/json" \
  -d '{"Title": "Fix login bug", "Description": "The login form does not validate email addresses", "VesselId": "vsl_abc123"}'
```

---

#### GET /api/v1/missions/{id}

Get a single mission by ID.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Mission ID (`msn_` prefix) |

**Response:** `200 OK` - [Mission](#mission)
**Error:** `404` - Mission not found

> **Note:** The `DiffSnapshot` field is excluded from responses to keep payloads compact. Use `GET /api/v1/missions/{id}/diff` to retrieve the full diff.

---

#### GET /api/v1/missions/{id}/github/pull-request

Read normalized GitHub pull-request evidence for one mission when that mission has a `PrUrl`.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Mission ID (`msn_` prefix) |

**Response:** `200 OK` - `GitHubPullRequestDetail`
**Error:** `400` - Mission is missing a PR URL or GitHub repository context
**Error:** `404` - Mission not found

> **Note:** The response includes PR state, requested reviewers, reviews, issue comments, and commit check-run evidence. Token resolution follows `GitHubTokenOverride` first, then the global `GitHubToken`.

---

#### PUT /api/v1/missions/{id}

Update mission fields (title, description, priority, etc.). Does not change status -- use the status transition endpoint for that.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Mission ID (`msn_` prefix) |

**Request Body:** [Mission](#mission) (fields to update)

**Response:** `200 OK` - [Mission](#mission)
**Error:** `404` - Mission not found

---

#### PUT /api/v1/missions/{id}/status

Transition a mission to a new status. Only valid transitions are allowed.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Mission ID (`msn_` prefix) |

**Request Body:** [StatusTransitionRequest](#statustransitionrequest)

| Field | Type | Required | Description |
|---|---|---|---|
| `Status` | string | yes | Target status name |

**Response:** `200 OK` - [Mission](#mission)
**Error:** `400` - Invalid transition or invalid status name
**Error:** `404` - Mission not found

**Valid Status Transitions:**

| From | Allowed Targets |
|---|---|
| `Pending` | `Assigned`, `Cancelled` |
| `Assigned` | `InProgress`, `Cancelled` |
| `InProgress` | `WorkProduced`, `Testing`, `Review`, `Complete`, `Failed`, `Cancelled` |
| `WorkProduced` | `PullRequestOpen`, `Complete`, `LandingFailed`, `Cancelled` |
| `PullRequestOpen` | `Complete`, `LandingFailed`, `Cancelled` |
| `Testing` | `Review`, `InProgress`, `Complete`, `Failed` |
| `Review` | `Complete`, `InProgress`, `Failed` |
| `LandingFailed` | `WorkProduced`, `Failed`, `Cancelled` |
| `Complete` | (terminal) |
| `Failed` | (terminal) |
| `Cancelled` | (terminal) |

```bash
curl -X PUT http://localhost:7890/api/v1/missions/msn_abc123/status \
  -H "Content-Type: application/json" \
  -d '{"Status": "Assigned"}'
```

---

#### DELETE /api/v1/missions/{id}

Cancel a mission by setting its status to `Cancelled`. Returns the full updated mission.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Mission ID (`msn_` prefix) |

**Response:** `200 OK` - [Mission](#mission) (with `Status: "Cancelled"`)

**Error:** `404` - Mission not found

---

#### `POST /api/v1/missions/delete/multiple`

Batch delete multiple missions from the database by ID. Returns a summary of deleted and skipped entries. **This cannot be undone.**

**Request Body:**

```json
{
  "Ids": ["msn_abc123", "msn_def456"]
}
```

**Response:** `200 OK`

```json
{
  "Status": "deleted",
  "Deleted": 2,
  "Skipped": []
}
```

Skipped entries include the entity ID and the reason (e.g., "Not found" or "Empty ID").

---

#### POST /api/v1/missions/{id}/restart

Restart a failed or cancelled mission by resetting it to `Pending` for re-dispatch. Clears captain assignment, branch, PR URL, and timing fields. Optionally update the title and description (instructions) before restarting.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Mission ID (`msn_` prefix) |

**Request Body (optional):**
```json
{
  "Title": "Updated mission title",
  "Description": "Updated instructions for the captain"
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `Title` | string | No | New mission title. Omit to keep the original. |
| `Description` | string | No | New mission description/instructions. Omit to keep the original. |

**Response:** `200 OK` - [Mission](#mission) (with `Status: "Pending"`)

**Errors:**
- `400` - Mission is not in `Failed` or `Cancelled` status
- `404` - Mission not found

---

#### GET /api/v1/missions/{id}/evaluate-autoland

Dry-run the vessel's auto-land predicate against the mission's captured diff without landing it.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Mission ID (`msn_` prefix) |

**Response:** `200 OK` -- `{ "Land": true }` or `{ "Land": false, "HoldReason": "..." }`
**Errors:** `404` mission not found; `400` mission has no associated vessel

---

#### GET /api/v1/missions/{id}/diff

Returns the git diff of changes made by a captain in the mission's worktree. Checks for a saved diff file first (captured at completion), then falls back to a live worktree diff.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Mission ID (`msn_` prefix) |

**Response:** `200 OK`

```json
{
  "MissionId": "msn_abc123",
  "Branch": "armada/msn_abc123",
  "Diff": "diff --git a/src/auth.ts b/src/auth.ts\n..."
}
```

**Error:** `404` - Mission not found or no diff available

---

#### GET /api/v1/missions/{id}/log

Returns the session log (captured stdout/stderr) for a mission. Log files are written to disk when a captain executes a mission. Supports pagination via query parameters.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Mission ID (`msn_` prefix) |

**Query Parameters:**
| Parameter | Type | Default | Description |
|---|---|---|---|
| `lines` | integer | 100 | Number of lines to return |
| `offset` | integer | 0 | Line offset (0-based, skip this many lines from start) |

**Response:** `200 OK`

```json
{
  "MissionId": "msn_abc123",
  "Log": "Starting mission...\nCloning repository...\n...",
  "Lines": 100,
  "TotalLines": 542
}
```

If the mission exists but has no log file yet, returns an empty log:

```json
{
  "MissionId": "msn_abc123",
  "Log": "",
  "Lines": 0,
  "TotalLines": 0
}
```

**Error:** `404` - Mission not found

```bash
# Get first 50 lines
curl http://localhost:8080/api/v1/missions/msn_abc123/log?lines=50 \
  -H "X-Api-Key: your-key"

# Get lines 100-200
curl http://localhost:8080/api/v1/missions/msn_abc123/log?offset=100&lines=100 \
  -H "X-Api-Key: your-key"
```

---

### Captains

A captain is an AI agent instance (Claude Code, Codex, etc.) that executes missions.

#### GET /api/v1/captains

List all captains with pagination.

**Query Parameters:** [Pagination parameters](#pagination-parameters), plus:

| Parameter | Type | Description |
|---|---|---|
| `status` | string | Filter by captain state (`Idle`, `Working`, `Stalled`, `Stopping`) |

**Response:** `200 OK` - [EnumerationResult](#enumerationresultt)\<[Captain](#captain)\>

```bash
curl http://localhost:7890/api/v1/captains?status=Working
```

---

#### POST /api/v1/captains/enumerate

Paginated enumeration of captains with optional filtering and sorting.

**Request Body:** [EnumerationQuery](#enumerationquery) (optional)

**Response:** `200 OK` - [EnumerationResult](#enumerationresultt)\<[Captain](#captain)\>

---

#### POST /api/v1/captains

Register a new captain (AI agent).

**Request Body:** [Captain](#captain)

| Field | Type | Required | Description |
|---|---|---|---|
| `Name` | string | yes | Captain name |
| `Runtime` | string | no | Agent runtime type (default: `ClaudeCode`) |
| `Model` | string | no | Optional model override for this captain. When omitted, the runtime selects its default model |

**Response:** `201 Created` - [Captain](#captain)
**Error:** `400 Bad Request` - Invalid or unavailable model

```bash
curl -X POST http://localhost:7890/api/v1/captains \
  -H "Content-Type: application/json" \
  -d '{"Name": "captain-1", "Runtime": "ClaudeCode", "Model": "claude-sonnet-4-20250514", "SystemInstructions": "You are a testing specialist. Always run tests before committing."}'
```

---

#### GET /api/v1/captains/{id}

Get a single captain by ID.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Captain ID (`cpt_` prefix) |

**Response:** `200 OK` - [Captain](#captain)
**Error:** `404` - Captain not found

---

#### GET /api/v1/captains/{id}/tools

Describe the Armada MCP tools available through a specific captain, including runtime-specific availability notes.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Captain ID (`cpt_` prefix) |

**Response:** `200 OK`

```json
{
  "captainId": "cpt_abc123",
  "captainName": "captain-1",
  "runtime": "Mux",
  "toolsAccessible": true,
  "availabilityVerified": true,
  "availabilitySource": "mux-probe",
  "summary": "Mux probe succeeded and the endpoint reported tool calling enabled.",
  "endpointName": "local-codex",
  "toolsEnabled": true,
  "effectiveToolCount": 42,
  "armadaToolCount": 37,
  "tools": [
    {
      "name": "get_status",
      "description": "Get current Admiral status snapshot",
      "inputSchemaJson": "{\"type\":\"object\",\"properties\":{}}"
    }
  ]
}
```

**Error:** `404` - Captain not found

---

#### PUT /api/v1/captains/{id}

Update a captain's name, runtime, or model. Operational fields (state, process, mission) are preserved.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Captain ID (`cpt_` prefix) |

**Request Body:**
```json
{
  "name": "captain-bravo",
  "runtime": "Codex",
  "model": "gpt-5.4",
  "systemInstructions": "Focus on code quality and always run linting before commits."
}
```

**Response:** `200 OK` - [Captain](#captain)
**Error:** `400 Bad Request` - Invalid or unavailable model

**Response:** `200 OK` - [Captain](#captain)
**Error:** `404` - Captain not found

```bash
curl -X PUT http://localhost:7890/api/v1/captains/cpt_abc123 \
  -H "Content-Type: application/json" \
  -H "x-api-key: YOUR_KEY" \
  -d '{"name": "captain-bravo", "runtime": "Codex"}'
```

---

#### POST /api/v1/captains/{id}/stop

Stop a running captain agent. Kills its OS process and recalls it to idle state.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Captain ID (`cpt_` prefix) |

**Response:** `200 OK`

```json
{
  "Status": "stopped"
}
```

**Error:** `404` - Captain not found

---

#### POST /api/v1/captains/stop-all

Emergency stop all running captains, recalling them to idle state.

**Response:** `200 OK`

```json
{
  "Status": "all_stopped"
}
```

---

#### GET /api/v1/captains/{id}/log

Returns the current session log for a captain. The captain's `.current` pointer file is resolved to find the active mission's log file. Supports pagination via query parameters.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Captain ID (`cpt_` prefix) |

**Query Parameters:**
| Parameter | Type | Default | Description |
|---|---|---|---|
| `lines` | integer | 100 | Number of lines to return |
| `offset` | integer | 0 | Line offset (0-based, skip this many lines from start) |

**Response:** `200 OK`

```json
{
  "CaptainId": "cpt_abc123",
  "Log": "[2026-03-07] Processing task...\nRunning tests...\n...",
  "Lines": 100,
  "TotalLines": 203
}
```

If the captain has no active log (no pointer file or target file missing), returns an empty log:

```json
{
  "CaptainId": "cpt_abc123",
  "Log": "",
  "Lines": 0,
  "TotalLines": 0
}
```

**Error:** `404` - Captain not found

```bash
curl http://localhost:8080/api/v1/captains/cpt_abc123/log?lines=200 \
  -H "X-Api-Key: your-key"
```

---

#### DELETE /api/v1/captains/{id}

Delete a captain. Blocked if the captain is currently working or has active missions.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Captain ID (`cpt_` prefix) |

**Response:** `204 No Content`
**Error:** `404` - Captain not found
**Error:** `409 Conflict` - Cannot delete captain while state is Working. Stop the captain first.
**Error:** `409 Conflict` - Cannot delete captain with active missions in Assigned or InProgress status. Cancel or complete them first.

---

#### `POST /api/v1/captains/delete/multiple`

Batch delete multiple captains from the database by ID. Captains that are Working or have active missions are skipped. Returns a summary of deleted and skipped entries. **This cannot be undone.**

**Request Body:**

```json
{
  "Ids": ["cpt_abc123", "cpt_def456"]
}
```

**Response:** `200 OK`

```json
{
  "Status": "deleted",
  "Deleted": 2,
  "Skipped": []
}
```

Skipped entries include the entity ID and the reason (e.g., "Not found", "Cannot delete captain while state is Working", or "Cannot delete captain with N active mission(s)").

---

### Signals

A signal is a message between the admiral and captains or between captains.

#### GET /api/v1/signals

List recent signals with pagination.

**Query Parameters:** [Pagination parameters](#pagination-parameters), plus:

| Parameter | Type | Description |
|---|---|---|
| `signalType` | string | Filter by signal type |
| `toCaptainId` | string | Filter by recipient captain ID |
| `unreadOnly` | bool | `true` to return only unread signals |

**Response:** `200 OK` - [EnumerationResult](#enumerationresultt)\<[Signal](#signal)\>

```bash
curl http://localhost:7890/api/v1/signals?toCaptainId=cpt_abc123&unreadOnly=true
```

---

#### POST /api/v1/signals/enumerate

Paginated enumeration of signals with optional filtering and sorting.

**Request Body:** [EnumerationQuery](#enumerationquery) (optional)

**Response:** `200 OK` - [EnumerationResult](#enumerationresultt)\<[Signal](#signal)\>

---

#### POST /api/v1/signals

Send a new signal (message).

**Request Body:** [Signal](#signal)

| Field | Type | Required | Description |
|---|---|---|---|
| `Type` | string | no | Signal type (default: `Nudge`) |
| `Payload` | string | no | Signal payload (message content) |
| `ToCaptainId` | string | no | Recipient captain ID (null = to Admiral) |
| `FromCaptainId` | string | no | Sender captain ID (null = from Admiral) |

**Response:** `201 Created` - [Signal](#signal)

```bash
curl -X POST http://localhost:7890/api/v1/signals \
  -H "Content-Type: application/json" \
  -d '{"Type": "Mail", "Payload": "Please check the test results", "ToCaptainId": "cpt_abc123"}'
```

---

#### `POST /api/v1/signals/delete/multiple`

Batch soft-delete multiple signals by marking them as read. Returns a summary of deleted and skipped entries.

**Request Body:**

```json
{
  "Ids": ["sig_abc123", "sig_def456"]
}
```

**Response:** `200 OK`

```json
{
  "Status": "deleted",
  "Deleted": 2,
  "Skipped": []
}
```

Skipped entries include the entity ID and the reason (e.g., "Not found" or "Empty ID").

---

### Events

System events represent state changes and audit trail entries generated automatically by the server.

#### GET /api/v1/events

List system events with pagination.

**Query Parameters:** [Pagination parameters](#pagination-parameters), plus:

| Parameter | Type | Description |
|---|---|---|
| `type` | string | Filter by event type (e.g. `mission.status_changed`) |
| `captainId` | string | Filter by captain ID |
| `missionId` | string | Filter by mission ID |
| `vesselId` | string | Filter by vessel ID |
| `voyageId` | string | Filter by voyage ID |
| `limit` | int | Alias for `pageSize` |

**Response:** `200 OK` - [EnumerationResult](#enumerationresultt)\<[ArmadaEvent](#armadaevent)\>

```bash
curl http://localhost:7890/api/v1/events?type=mission.status_changed&missionId=msn_abc123
```

---

#### POST /api/v1/events/enumerate

Paginated enumeration of events with optional filtering and sorting.

**Request Body:** [EnumerationQuery](#enumerationquery) (optional)

**Response:** `200 OK` - [EnumerationResult](#enumerationresultt)\<[ArmadaEvent](#armadaevent)\>

---

#### `DELETE /api/v1/events/{id}`

Delete a single event by ID.

**Path Parameters:**

| Parameter | Description |
|---|---|
| `id` | Event ID (`evt_` prefix) |

**Response:** `204 No Content`

**Error:** `404` - Event not found

---

#### `POST /api/v1/events/delete/multiple`

Batch delete multiple events by ID. Returns a summary of deleted and skipped entries.

**Request Body:**

```json
{
  "Ids": ["evt_abc123", "evt_def456"]
}
```

**Response:** `200 OK`

```json
{
  "Status": "deleted",
  "Deleted": 2,
  "Skipped": []
}
```

Skipped entries include the entity ID and the reason (e.g., "Not found" or "Empty ID").

---

### Docks

Docks are git worktrees provisioned for captains. These endpoints provide access to dock state and management.

#### `GET /api/v1/docks`

List all docks with optional filtering.

**Query Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `vesselId` | string | Filter by vessel ID |
| `pageNumber` | integer | Page number (1-based, default 1) |
| `pageSize` | integer | Results per page (default 100) |
| `order` | string | Sort order: `CreatedAscending`, `CreatedDescending` |

**Response:** `200 OK`

```json
{
  "Objects": [],
  "TotalRecords": 0,
  "PageSize": 100,
  "PageNumber": 1,
  "TotalPages": 0,
  "Success": true,
  "TotalMs": 0.5
}
```

---

#### `POST /api/v1/docks/enumerate`

Paginated enumeration of docks with optional filtering and sorting.

**Request Body:**

```json
{
  "PageNumber": 1,
  "PageSize": 25,
  "VesselId": "vsl_abc123"
}
```

**Response:** `200 OK` -- Same shape as `GET /api/v1/docks`.

---

#### `GET /api/v1/docks/{id}`

Get a single dock by ID.

**Path Parameters:**

| Parameter | Description |
|---|---|
| `id` | Dock ID (`dck_` prefix) |

**Response:** `200 OK` - Dock object

**Error:** `404` - Dock not found

---

#### `DELETE /api/v1/docks/{id}`

Delete a dock and clean up its git worktree. Blocked if the dock is actively in use by a captain.

**Path Parameters:**

| Parameter | Description |
|---|---|
| `id` | Dock ID (`dck_` prefix) |

**Response:** `204 No Content`

**Error:** `404` - Dock not found
**Error:** `409` - Dock is actively in use by a captain

---

#### `DELETE /api/v1/docks/{id}/purge`

Force purge a dock and its git worktree, even if a mission references it. **This cannot be undone.**

**Path Parameters:**

| Parameter | Description |
|---|---|
| `id` | Dock ID (`dck_` prefix) |

**Response:** `200 OK`

```json
{
  "Status": "purged",
  "DockId": "dck_abc123"
}
```

**Error:** `404` - Dock not found

---

#### `POST /api/v1/docks/delete/multiple`

Batch delete multiple docks and their git worktrees from the database by ID. Returns a summary of deleted and skipped entries. **This cannot be undone.**

**Request Body:**

```json
{
  "Ids": ["dck_abc123", "dck_def456"]
}
```

**Response:** `200 OK`

```json
{
  "Status": "deleted",
  "Deleted": 2,
  "Skipped": []
}
```

Skipped entries include the entity ID and the reason (e.g., "Not found" or "Empty ID").

---

### Merge Queue

A bors-style merge queue that batches branches, runs tests, and lands passing batches.

#### GET /api/v1/merge-queue

List merge queue entries with pagination.

**Query Parameters:** [Pagination parameters](#pagination-parameters)

**Response:** `200 OK` - [EnumerationResult](#enumerationresultt)\<[MergeEntry](#mergeentry)\>

```bash
curl http://localhost:7890/api/v1/merge-queue
```

---

#### POST /api/v1/merge-queue/enumerate

Paginated enumeration of merge queue entries with optional filtering and sorting.

**Request Body:** [EnumerationQuery](#enumerationquery) (optional)

**Response:** `200 OK` - [EnumerationResult](#enumerationresultt)\<[MergeEntry](#mergeentry)\>

---

#### POST /api/v1/merge-queue

Enqueue a branch for testing and merging.

**Request Body:** [MergeEntry](#mergeentry)

| Field | Type | Required | Description |
|---|---|---|---|
| `BranchName` | string | yes | Branch to merge |
| `TargetBranch` | string | no | Target branch (default: `"main"`) |
| `MissionId` | string | no | Parent mission ID |
| `VesselId` | string | no | Vessel ID |
| `Priority` | int | no | Queue priority (lower = higher, default: 0) |
| `TestCommand` | string | no | Test command for verification |

**Response:** `201 Created` - [MergeEntry](#mergeentry)

```bash
curl -X POST http://localhost:7890/api/v1/merge-queue \
  -H "Content-Type: application/json" \
  -d '{"BranchName": "armada/msn_abc123", "TargetBranch": "main", "MissionId": "msn_abc123"}'
```

---

#### GET /api/v1/merge-queue/{id}

Get a single merge queue entry by ID.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Merge entry ID (`mrg_` prefix) |

**Response:** `200 OK` - [MergeEntry](#mergeentry)
**Error:** `404` - Merge entry not found

---

#### DELETE /api/v1/merge-queue/{id}

Cancel a queued merge entry.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Merge entry ID (`mrg_` prefix) |

**Response:** `204 No Content`

---

#### POST /api/v1/merge-queue/process

Trigger processing of the merge queue. Creates integration branches, runs tests, and lands passing batches.

**Response:** `200 OK`

```json
{
  "Status": "processed"
}
```

---

#### `DELETE /api/v1/merge-queue/{id}/purge`

Permanently delete a single terminal merge queue entry from the database. Only entries in Landed, Failed, or Cancelled status can be purged. **This cannot be undone.**

**Path Parameters:**

| Parameter | Description |
|---|---|
| `id` | Merge entry ID (`mrg_` prefix) |

**Response:** `200 OK`

```json
{
  "Status": "purged",
  "EntryId": "mrg_abc123"
}
```

**Error:** `404` - Merge entry not found
**Error:** `409` - Entry is not in a terminal state

---

#### `POST /api/v1/merge-queue/purge`

Batch purge multiple terminal merge queue entries from the database by ID. Returns a summary of purged and skipped entries. **This cannot be undone.**

**Request Body:**

```json
{
  "EntryIds": ["mrg_abc123", "mrg_def456"]
}
```

**Response:** `200 OK`

```json
{
  "Status": "purged",
  "EntriesPurged": 2,
  "Skipped": []
}
```

Skipped entries include the entry ID and the reason (e.g., "Not found" or "Not in terminal state").

---

### Harbors

Harbor records register host-side runners that let the Admiral run detached (in Docker or on another host) while agent CLIs, git, and worktrees execute on a developer machine over an authenticated client-to-server link. These routes manage the Harbor registrations only -- creating, reading, updating, enabling, disabling, and deleting them. Runtime state such as `ConnectionStatus`, `LastSeenUtc`, `ProtocolVersion`, `OsPlatform`, and `Architecture` is reported by the link and is not operator-editable; the operator-editable fields are `Name`, `MaxConcurrentJobs`, and `Enabled`. The live link transport that carries host work is still emerging (see [docs/HARBOR.md](HARBOR.md) and [docs/HARBOR_PROTOCOL.md](HARBOR_PROTOCOL.md)); these management routes exist today.

#### GET /api/v1/harbors

List Harbors in the caller scope. Returns a plain array, not a paged envelope.

- Response: `200 OK` - `Harbor[]`

#### POST /api/v1/harbors

Register one Harbor. Only `name`, `maxConcurrentJobs`, and `enabled` are honored on create; all other fields are managed by the link.

```json
{
  "name": "workstation-01",
  "maxConcurrentJobs": 4,
  "enabled": true
}
```

- Response: `201 Created` - `Harbor`

#### GET /api/v1/harbors/{id}

Read one Harbor.

- Response: `200 OK` - `Harbor`
- Errors: `404 Not Found`

#### PUT /api/v1/harbors/{id}

Update one Harbor. Only `name`, `maxConcurrentJobs`, and `enabled` are updated; runtime state reported by the link is preserved server-side.

```json
{
  "name": "workstation-01",
  "maxConcurrentJobs": 8,
  "enabled": true
}
```

- Response: `200 OK` - `Harbor`
- Errors: `404 Not Found`

#### DELETE /api/v1/harbors/{id}

Delete one Harbor registration.

- Response: `204 No Content`
- Errors: `404 Not Found`

#### POST /api/v1/harbors/{id}/enable

Enable one Harbor so the router may route new missions to it.

- Response: `200 OK` - `Harbor`
- Errors: `404 Not Found`

#### POST /api/v1/harbors/{id}/disable

Disable one Harbor. A disabled Harbor keeps its docks but receives no new missions.

- Response: `200 OK` - `Harbor`
- Errors: `404 Not Found`

---

### Playbooks

Playbooks are tenant-scoped markdown documents that can be attached to voyages or standalone missions. Each selection carries its own delivery mode so the model receives either the full content inline or a file path it should read.

#### GET /api/v1/playbooks

List playbooks with pagination.

**Response:** `200 OK` - [EnumerationResult](#enumerationresultt)\<[Playbook](#playbook)\>

#### POST /api/v1/playbooks/enumerate

Paginated enumeration of playbooks with optional filtering and sorting.

**Request Body:** [EnumerationQuery](#enumerationquery) (optional)

#### POST /api/v1/playbooks

Create a playbook.

**Request Body:** [Playbook](#playbook)

#### GET /api/v1/playbooks/{id}

Return a single playbook by ID.

**Response:** `200 OK` - [Playbook](#playbook)

#### PUT /api/v1/playbooks/{id}

Update a playbook's file name, description, content, or active state.

**Request Body:** [Playbook](#playbook)

#### DELETE /api/v1/playbooks/{id}

Delete a playbook. Existing mission snapshots remain immutable.

**Response:** `200 OK`

---

### Prompt Templates

Prompt templates define the instruction text used when generating captain mission briefs. Armada ships with built-in templates that can be customized. Custom templates can also be created per tenant.

The built-in **`ask.system`** template (category `ask`) is the system prompt prepended to every **Ask Armada** dashboard chat turn. It is seeded automatically on first run and is editable exactly like any other template — through **Configuration > Prompts** in the dashboard, through these REST endpoints (`GET`/`PUT /api/v1/prompt-templates/ask.system`, `POST /api/v1/prompt-templates/ask.system/reset`), or through the MCP tools (`get_prompt_template`, `update_prompt_template`, `reset_prompt_template` with `name = "ask.system"`).

#### GET /api/v1/prompt-templates

List all prompt templates with pagination.

**Query Parameters:** [Pagination parameters](#pagination-parameters)

**Response:** `200 OK` - [EnumerationResult](#enumerationresultt)\<PromptTemplate\>

```bash
curl http://localhost:7890/api/v1/prompt-templates
```

---

#### POST /api/v1/prompt-templates/enumerate

Paginated enumeration of prompt templates with optional filtering and sorting.

**Request Body:** [EnumerationQuery](#enumerationquery) (optional)

**Response:** `200 OK` - [EnumerationResult](#enumerationresultt)\<PromptTemplate\>

```bash
curl -X POST http://localhost:7890/api/v1/prompt-templates/enumerate \
  -H "Content-Type: application/json" \
  -d '{"PageSize": 10}'
```

---

#### POST /api/v1/prompt-templates

Create a prompt template.

**Request Body:**

| Field | Type | Required | Description |
|---|---|---|---|
| `Name` | string | yes | Unique template name |
| `Category` | string | yes | Template category such as `persona`, `mission`, or `structure` |
| `Content` | string | yes | Template content text |
| `Description` | string | no | Template description |
| `Active` | bool | no | Whether the template is active |

**Response:** `201 Created` - PromptTemplate

```bash
curl -X POST http://localhost:7890/api/v1/prompt-templates \
  -H "Content-Type: application/json" \
  -d '{"Name": "persona.product_manager.copy", "Category": "persona", "Content": "You are...", "Description": "Derived persona template"}'
```

---

#### GET /api/v1/prompt-templates/{name}

Get a prompt template by name.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `name` | Template name |

**Response:** `200 OK` - PromptTemplate
**Error:** `404` - Template not found

```bash
curl http://localhost:7890/api/v1/prompt-templates/default
```

---

#### PUT /api/v1/prompt-templates/{name}

Update a prompt template's content. Built-in templates can be customized by updating their content.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `name` | Template name |

**Request Body:**

| Field | Type | Required | Description |
|---|---|---|---|
| `Content` | string | yes | Template content text |
| `Description` | string | no | Template description |

**Response:** `200 OK` - PromptTemplate
**Error:** `404` - Template not found

```bash
curl -X PUT http://localhost:7890/api/v1/prompt-templates/default \
  -H "Content-Type: application/json" \
  -d '{"Content": "You are a captain. Follow these instructions...", "Description": "Custom default template"}'
```

---

#### POST /api/v1/prompt-templates/{name}/reset

Reset a prompt template to its built-in default content. Only applicable to built-in templates that have been customized.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `name` | Template name |

**Response:** `200 OK` - PromptTemplate
**Error:** `404` - Template not found

```bash
curl -X POST http://localhost:7890/api/v1/prompt-templates/default/reset
```

---

### Personas

A persona associates a name and description with a prompt template. Personas are used to configure the behavior of captains within a pipeline. Armada ships with built-in personas that cannot be deleted.

#### GET /api/v1/personas

List all personas with pagination.

**Query Parameters:** [Pagination parameters](#pagination-parameters)

**Response:** `200 OK` - [EnumerationResult](#enumerationresultt)\<Persona\>

```bash
curl http://localhost:7890/api/v1/personas
```

---

#### POST /api/v1/personas/enumerate

Paginated enumeration of personas with optional filtering and sorting.

**Request Body:** [EnumerationQuery](#enumerationquery) (optional)

**Response:** `200 OK` - [EnumerationResult](#enumerationresultt)\<Persona\>

```bash
curl -X POST http://localhost:7890/api/v1/personas/enumerate \
  -H "Content-Type: application/json" \
  -d '{"PageSize": 10}'
```

---

#### GET /api/v1/personas/{name}

Get a persona by name.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `name` | Persona name |

**Response:** `200 OK` - Persona
**Error:** `404` - Persona not found

```bash
curl http://localhost:7890/api/v1/personas/default
```

---

#### POST /api/v1/personas

Create a new persona.

**Request Body:**

| Field | Type | Required | Description |
|---|---|---|---|
| `Name` | string | yes | Persona name |
| `Description` | string | no | Persona description |
| `PromptTemplateName` | string | yes | Name of the prompt template to use |

**Response:** `201 Created` - Persona

```bash
curl -X POST http://localhost:7890/api/v1/personas \
  -H "Content-Type: application/json" \
  -d '{"Name": "reviewer", "Description": "Code review specialist", "PromptTemplateName": "default"}'
```

---

#### PUT /api/v1/personas/{name}

Update an existing persona.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `name` | Persona name |

**Request Body:**

| Field | Type | Required | Description |
|---|---|---|---|
| `Description` | string | no | Updated description |
| `PromptTemplateName` | string | no | Updated prompt template name |

**Response:** `200 OK` - Persona
**Error:** `404` - Persona not found

```bash
curl -X PUT http://localhost:7890/api/v1/personas/reviewer \
  -H "Content-Type: application/json" \
  -d '{"Description": "Updated reviewer persona", "PromptTemplateName": "review-template"}'
```

---

#### DELETE /api/v1/personas/{name}

Delete a persona. Built-in personas cannot be deleted.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `name` | Persona name |

**Response:** `204 No Content`
**Error:** `404` - Persona not found
**Error:** `403` - Built-in persona cannot be deleted

```bash
curl -X DELETE http://localhost:7890/api/v1/personas/reviewer
```

---

### Pipelines

A pipeline defines an ordered sequence of stages, each associated with a persona. Pipelines control the multi-stage workflow that missions progress through. Armada ships with built-in pipelines that cannot be deleted.

#### GET /api/v1/pipelines

List all pipelines with pagination. Response includes the stages for each pipeline.

**Query Parameters:** [Pagination parameters](#pagination-parameters)

**Response:** `200 OK` - [EnumerationResult](#enumerationresultt)\<Pipeline\>

```bash
curl http://localhost:7890/api/v1/pipelines
```

---

#### POST /api/v1/pipelines/enumerate

Paginated enumeration of pipelines with optional filtering and sorting.

**Request Body:** [EnumerationQuery](#enumerationquery) (optional)

**Response:** `200 OK` - [EnumerationResult](#enumerationresultt)\<Pipeline\>

```bash
curl -X POST http://localhost:7890/api/v1/pipelines/enumerate \
  -H "Content-Type: application/json" \
  -d '{"PageSize": 10}'
```

---

#### GET /api/v1/pipelines/{name}

Get a pipeline by name, including its stages.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `name` | Pipeline name |

**Response:** `200 OK` - Pipeline
**Error:** `404` - Pipeline not found

```bash
curl http://localhost:7890/api/v1/pipelines/default
```

---

#### POST /api/v1/pipelines

Create a new pipeline with stages.

**Request Body:**

| Field | Type | Required | Description |
|---|---|---|---|
| `Name` | string | yes | Pipeline name |
| `Description` | string | no | Pipeline description |
| `Stages` | array | yes | Ordered list of pipeline stages |

**Stage fields:**

| Field | Type | Required | Description |
|---|---|---|---|
| `PersonaName` | string | yes | Name of the persona for this stage |
| `IsOptional` | bool | no | Whether this stage can be skipped (default: false) |
| `Description` | string | no | Stage description |

**Response:** `201 Created` - Pipeline

```bash
curl -X POST http://localhost:7890/api/v1/pipelines \
  -H "Content-Type: application/json" \
  -d '{"Name": "review-pipeline", "Description": "Code with review", "Stages": [{"PersonaName": "default", "Description": "Implementation"}, {"PersonaName": "reviewer", "IsOptional": false, "Description": "Code review"}]}'
```

---

#### PUT /api/v1/pipelines/{name}

Update an existing pipeline.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `name` | Pipeline name |

**Request Body:**

| Field | Type | Required | Description |
|---|---|---|---|
| `Description` | string | no | Updated description |
| `Stages` | array | no | Updated ordered list of pipeline stages (replaces all existing stages) |

**Response:** `200 OK` - Pipeline
**Error:** `404` - Pipeline not found

```bash
curl -X PUT http://localhost:7890/api/v1/pipelines/review-pipeline \
  -H "Content-Type: application/json" \
  -d '{"Description": "Updated pipeline", "Stages": [{"PersonaName": "default"}, {"PersonaName": "reviewer"}]}'
```

---

#### DELETE /api/v1/pipelines/{name}

Delete a pipeline. Built-in pipelines cannot be deleted.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `name` | Pipeline name |

**Response:** `204 No Content`
**Error:** `404` - Pipeline not found
**Error:** `403` - Built-in pipeline cannot be deleted

```bash
curl -X DELETE http://localhost:7890/api/v1/pipelines/review-pipeline
```

---

### Model Endpoints

A model endpoint is a managed reference to an external embedding or inference (chat/completion) model behind a provider API. Armada stores the endpoint, health-checks it (deduplicated by base URL), and can validate one with a real request. The stored `ApiKey` is write-only: it is accepted on create and update but is never returned on reads. Reads expose `HasApiKey` instead.

Two capability constraints are enforced:

- `Anthropic` cannot be used with `Kind` `Embedding` (no embeddings API).
- `VoyageAI` cannot be used with `Kind` `Inference` (embeddings only).

Requests that violate either constraint are rejected with `400 Bad Request`.

#### GET /api/v1/model-endpoints

List all model endpoints in the caller scope. This route returns a plain array, **not** a paginated `EnumerationResult` envelope.

**Response:** `200 OK` - [ModelEndpoint](#modelendpoint)[]

```json
[
  {
    "Id": "mep_abc123",
    "TenantId": "default",
    "UserId": "default",
    "Name": "Primary embeddings",
    "Kind": "Embedding",
    "Provider": "OpenAI",
    "BaseUrl": "https://api.openai.com/v1",
    "Model": "text-embedding-3-small",
    "Dimensionality": 1536,
    "TimeoutMs": 120000,
    "Enabled": true,
    "HasApiKey": true,
    "HealthStatus": "Healthy",
    "LastHealthCheckUtc": "2026-03-07T12:00:00Z",
    "LastHealthError": null,
    "LastLatencyMs": 84,
    "CreatedUtc": "2026-03-07T12:00:00Z",
    "LastUpdateUtc": "2026-03-07T12:00:00Z"
  }
]
```

```bash
curl -H "Authorization: Bearer default" http://localhost:7890/api/v1/model-endpoints
```

---

#### POST /api/v1/model-endpoints

Create a model endpoint. Supply `ApiKey` to store a provider key; it is write-only and is never returned on subsequent reads.

**Request Body:** [ModelEndpoint](#modelendpoint)

| Field | Type | Required | Description |
|---|---|---|---|
| `Name` | string | yes | Display name |
| `BaseUrl` | string | yes | Provider API base URL |
| `Kind` | string | no | `Embedding` (default) or `Inference` |
| `Provider` | string | no | `Ollama`, `OpenAI`, `OpenAICompatible`, `Anthropic`, `Gemini`, or `VoyageAI` |
| `Model` | string | no | Model name to target |
| `Dimensionality` | int | no | Embedding dimensionality (default 0) |
| `TimeoutMs` | int | no | Request timeout in milliseconds (default 120000, clamped to 1000..600000) |
| `Enabled` | bool | no | Whether the endpoint participates in health sweeps (default true) |
| `ApiKey` | string | no | Provider API key. Write-only: accepted here, never returned on reads. |

```json
{
  "Name": "Primary embeddings",
  "Kind": "Embedding",
  "Provider": "OpenAI",
  "BaseUrl": "https://api.openai.com/v1",
  "Model": "text-embedding-3-small",
  "Dimensionality": 1536,
  "TimeoutMs": 120000,
  "Enabled": true,
  "ApiKey": "sk-example-key"
}
```

**Response:** `201 Created` - [ModelEndpoint](#modelendpoint) (note `HasApiKey: true`, and no `ApiKey` field)

```json
{
  "Id": "mep_abc123",
  "Name": "Primary embeddings",
  "Kind": "Embedding",
  "Provider": "OpenAI",
  "BaseUrl": "https://api.openai.com/v1",
  "Model": "text-embedding-3-small",
  "Dimensionality": 1536,
  "TimeoutMs": 120000,
  "Enabled": true,
  "HasApiKey": true,
  "HealthStatus": "Unknown",
  "LastHealthCheckUtc": null,
  "LastHealthError": null,
  "LastLatencyMs": null,
  "CreatedUtc": "2026-03-07T12:00:00Z",
  "LastUpdateUtc": "2026-03-07T12:00:00Z"
}
```

**Errors:**
- `400 Bad Request` - Missing `Name` or `BaseUrl`, or a rejected provider/kind combination (`Anthropic` + `Embedding`, or `VoyageAI` + `Inference`)

---

#### GET /api/v1/model-endpoints/{id}

Get a single model endpoint by ID.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Model endpoint ID (`mep_` prefix) |

**Response:** `200 OK` - [ModelEndpoint](#modelendpoint)
**Error:** `404` - Model endpoint not found

---

#### PUT /api/v1/model-endpoints/{id}

Update a model endpoint. Omit `ApiKey` to keep the stored key; send `ApiKey` to replace it.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Model endpoint ID (`mep_` prefix) |

**Request Body:** [ModelEndpoint](#modelendpoint) (fields to update)

```json
{
  "Name": "Primary embeddings (updated)",
  "Model": "text-embedding-3-large",
  "Dimensionality": 3072,
  "Enabled": true
}
```

**Response:** `200 OK` - [ModelEndpoint](#modelendpoint)
**Error:** `400` - Rejected provider/kind combination
**Error:** `404` - Model endpoint not found

---

#### DELETE /api/v1/model-endpoints/{id}

Delete a model endpoint.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Model endpoint ID (`mep_` prefix) |

**Response:** `204 No Content`
**Error:** `404` - Model endpoint not found

---

#### POST /api/v1/model-endpoints/{id}/validate

Validate one endpoint by issuing a real request against the provider: an embedding request for `Embedding` endpoints, or a completion request for `Inference` endpoints. The resulting health status, timestamp, latency, and any error are persisted on the endpoint.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Model endpoint ID (`mep_` prefix) |

**Response:** `200 OK` - [ModelEndpointProbeResult](#modelendpointproberesult)

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

**Error:** `404` - Model endpoint not found

---

#### POST /api/v1/model-endpoints/health-check

Probe all enabled model endpoints, deduplicated by base URL, and persist each endpoint's health status. Returns the number of distinct base URLs that were probed.

**Response:** `200 OK` - [ModelEndpointHealthSweepResponse](#modelendpointhealthsweepresponse)

```json
{
  "DistinctBaseUrlsProbed": 3
}
```

---

### Backup and Restore

#### GET /api/v1/backup

Create and download a ZIP backup of the Armada database and settings.

**Response:** `200 OK` — Binary ZIP file stream with `Content-Disposition: attachment; filename="armada-backup-{timestamp}.zip"` header.

**ZIP Contents:**

| File | Description |
|---|---|
| `armada.db` | SQLite database snapshot created via the SQLite online backup API |
| `settings.json` | Current Armada server configuration, including `GitHubToken` when configured |
| `manifest.json` | Backup metadata: timestamp, schema version, Armada version, record counts per table |

**Example:**

```bash
curl -H "X-Api-Key: your-key" http://localhost:7890/api/v1/backup -o backup.zip
```

---

#### POST /api/v1/restore

Restore Armada from a previously created backup ZIP file.

**Request:** Binary ZIP file in the request body (`Content-Type: application/zip`).

**Headers:**

| Header | Required | Description |
|---|---|---|
| `Content-Type` | Yes | `application/zip` |
| `X-Original-Filename` | No | Original filename of the uploaded backup (used in the response message). If omitted, the server's temp filename is used. |

**Validation:**
- ZIP must contain `armada.db` with a valid `schema_migrations` table
- A safety backup is automatically created before overwriting the current database

**Response:** `200 OK`

```json
{
  "Status": "restored",
  "SafetyBackupPath": "~/.armada/backups/armada-safety-backup-20260311T120000Z.zip",
  "SchemaVersion": 9,
  "Message": "Database restored from backup.zip. Restart the server to reload the restored data."
}
```

**Example:**

```bash
curl -X POST -H "X-Api-Key: your-key" \
  -H "Content-Type: application/zip" \
  -H "X-Original-Filename: backup.zip" \
  --data-binary @backup.zip \
  http://localhost:7890/api/v1/restore
```

> **Note:** Restart the server after restoring to ensure all in-memory state is refreshed.

---

### Workspace

Workspace is a first-class REST surface for browsing and editing a vessel working tree. All paths are repository-relative, normalized to forward slashes, and constrained to the vessel `workingDirectory`. Armada blocks traversal outside that root and reserves `.git` internals.

#### GET /api/v1/workspace/vessels/{vesselId}/tree

List one directory in the vessel workspace.

- Query: optional `path`
- Response: `200 OK` - `WorkspaceTreeResult`

#### GET /api/v1/workspace/vessels/{vesselId}/file

Read one file in the vessel workspace.

- Query: required `path`
- Response: `200 OK` - `WorkspaceFileResponse`
- Errors: `400` when `path` is missing, `404` when the file is not found

#### PUT /api/v1/workspace/vessels/{vesselId}/file

Save one text file with optimistic concurrency validation.

```json
{
  "Path": "src/Armada.Server/Routes/RequestHistoryRoutes.cs",
  "Content": "// updated file content",
  "ExpectedHash": "sha256:previous-hash"
}
```

- Response: `200 OK` - `WorkspaceSaveResult`
- Errors: `409 Conflict` when the on-disk hash no longer matches `ExpectedHash`

#### POST /api/v1/workspace/vessels/{vesselId}/directory

Create a directory inside the vessel workspace.

```json
{
  "Path": "docs/new-folder"
}
```

- Response: `201 Created` - `WorkspaceOperationResult`

#### POST /api/v1/workspace/vessels/{vesselId}/rename

Rename or move one file or directory.

```json
{
  "Path": "docs/old-name.md",
  "NewPath": "docs/new-name.md"
}
```

- Response: `200 OK` - `WorkspaceOperationResult`

#### DELETE /api/v1/workspace/vessels/{vesselId}/entry

Delete one file or directory.

- Query: required `path`
- Response: `200 OK` - `WorkspaceOperationResult`

#### GET /api/v1/workspace/vessels/{vesselId}/search

Search text files in the vessel workspace.

- Query: required `q`, optional `maxResults`
- Response: `200 OK` - `WorkspaceSearchResult`

#### GET /api/v1/workspace/vessels/{vesselId}/changes

Return branch state and changed files.

- Response: `200 OK` - `WorkspaceChangesResult`

#### GET /api/v1/workspace/vessels/{vesselId}/status

Return high-level workspace health, git state, and active-mission overlap context.

- Response: `200 OK` - `WorkspaceStatusResult`

---

### Planning Sessions

Planning sessions back the dashboard’s captain chat flow and transcript-to-dispatch handoff. These routes are implemented for SQLite first; other database backends return `501 Not Supported`.

#### GET /api/v1/planning-sessions

List planning sessions visible to the authenticated caller.

- Response: `200 OK` - `PlanningSession[]`

#### POST /api/v1/planning-sessions

Create a planning session, reserve the selected captain, and provision a planning dock.

```json
{
  "Title": "Refactor request history filters",
  "CaptainId": "cpt_abc123",
  "VesselId": "vsl_def456",
  "FleetId": "flt_xyz789",
  "PipelineId": "pln_fullpipeline",
  "SelectedPlaybooks": []
}
```

- Response: `201 Created`
- Response shape:

```json
{
  "Session": { "...": "PlanningSession" },
  "Messages": [],
  "Captain": { "...": "Captain" },
  "Vessel": { "...": "Vessel" }
}
```

#### GET /api/v1/planning-sessions/{id}

Read one planning session with transcript, captain, and vessel context.

- Response: `200 OK`
- Errors: `404 Not Found`

#### POST /api/v1/planning-sessions/{id}/messages

Append one user message and launch the next planning turn.

```json
{
  "Content": "Summarize the changes and propose a safe rollout."
}
```

- Response: `200 OK` - same detail shape as `GET /api/v1/planning-sessions/{id}`

#### POST /api/v1/planning-sessions/{id}/summarize

Generate a dispatch-ready draft from a selected or inferred assistant message without launching the voyage.

```json
{
  "MessageId": "psm_abc123",
  "Title": "Refresh request history docs"
}
```

- Response: `200 OK` - `PlanningSessionSummaryResponse`

#### POST /api/v1/planning-sessions/{id}/dispatch

Create a voyage directly from planning output.

```json
{
  "MessageId": "psm_abc123",
  "Title": "Refresh request history docs",
  "Description": "Update docs and validation assets for the shipped request-history feature."
}
```

- Response: `200 OK` - `Voyage`

#### POST /api/v1/planning-sessions/{id}/stop

Stop an active planning session and release its resources.

- Response: `200 OK` - same detail shape as `GET /api/v1/planning-sessions/{id}`

#### DELETE /api/v1/planning-sessions/{id}

Delete a planning session and its transcript. Active sessions are stopped first.

- Response: `204 No Content`

---

### Objectives

Backlog is the primary user-facing label for future work in Armada. The persisted domain record remains `Objective`, so the legacy `/api/v1/objectives/...` routes remain supported while `/api/v1/backlog/...` exposes the same record shape using backlog-first terminology.

Important shared `Objective` fields now include:

- `Kind`, `Category`, `Priority`, `Rank`, `BacklogState`, `Effort`
- `TargetVersion`, `DueUtc`
- `ParentObjectiveId`, `BlockedByObjectiveIds`
- `RefinementSummary`, `SuggestedPipelineId`, `RefinementSessionIds`
- all existing linkage fields for fleets, vessels, planning sessions, voyages, missions, checks, releases, deployments, and incidents

#### GET /api/v1/objectives and GET /api/v1/backlog

List objective or backlog records in the caller scope.

- Query: `pageNumber`, `pageSize`, `owner`, `category`, `parentObjectiveId`, `vesselId`, `fleetId`, `planningSessionId`, `voyageId`, `missionId`, `checkRunId`, `releaseId`, `deploymentId`, `incidentId`, `tag`, `status`, `backlogState`, `kind`, `priority`, `effort`, `targetVersion`, `search`, `fromUtc`, `toUtc`
- Response: `200 OK` - `EnumerationResult<Objective>`

#### POST /api/v1/objectives/enumerate and POST /api/v1/backlog/enumerate

Enumerate objectives or backlog items using a JSON body and optional querystring overrides.

- Request body: `ObjectiveQuery`
- Response: `200 OK` - `EnumerationResult<Objective>`

#### POST /api/v1/objectives and POST /api/v1/backlog

Create one scoped objective or backlog item.

```json
{
  "Title": "Stabilize May rollout",
  "Description": "Track the full rollout objective across the API and dashboard vessels.",
  "Status": "Scoped",
  "Kind": "Feature",
  "Category": "Delivery",
  "Priority": "P1",
  "Rank": 10,
  "BacklogState": "ReadyForPlanning",
  "Effort": "M",
  "Owner": "Delivery",
  "TargetVersion": "0.9.0",
  "AcceptanceCriteria": ["Deployment verified in staging", "Incident rollback documented"],
  "NonGoals": ["Do not change release cadence"],
  "RolloutConstraints": ["Keep existing rollback path intact"],
  "VesselIds": ["vsl_abc123"],
  "ReleaseIds": ["rel_def456"]
}
```

- Request body: `ObjectiveUpsertRequest`
- Response: `201 Created` - `Objective`
- Notes: `POST /api/v1/backlog` is the preferred user-facing alias; both routes persist the same underlying `Objective`

#### POST /api/v1/objectives/reorder and POST /api/v1/backlog/reorder

Apply one or more explicit backlog rank updates.

```json
{
  "Items": [
    { "ObjectiveId": "obj_abc123", "Rank": 10 },
    { "ObjectiveId": "obj_def456", "Rank": 20 }
  ]
}
```

- Request body: `ObjectiveReorderRequest`
- Response: `200 OK` - `List<Objective>`

#### POST /api/v1/objectives/import/github

Import or refresh one objective from GitHub issue or pull-request metadata using the selected vessel's GitHub repository mapping.

Credential resolution order:

1. `Vessel.GitHubTokenOverride`
2. global `GitHubToken` from Armada configuration

```json
{
  "VesselId": "vsl_abc123",
  "SourceType": "Issue",
  "Number": 123
}
```

- Request body: `GitHubObjectiveImportRequest`
- Response: `201 Created` when a new objective is created, `200 OK` when an existing objective is refreshed
- Errors: `400 Bad Request`, `404 Not Found`
- Notes:
  - Set `SourceType` to `Issue` or `PullRequest`
  - Include `ObjectiveId` to refresh an already-linked objective in place
  - Reads never return the resolved GitHub token value

#### GET /api/v1/objectives/{id} and GET /api/v1/backlog/{id}

Read one objective or backlog item.

- Response: `200 OK` - `Objective`
- Errors: `404 Not Found`

#### PUT /api/v1/objectives/{id} and PUT /api/v1/backlog/{id}

Update one objective or backlog item and its linked lifecycle scope.

- Request body: `ObjectiveUpsertRequest`
- Response: `200 OK` - `Objective`
- Errors: `400 Bad Request`, `404 Not Found`

#### DELETE /api/v1/objectives/{id} and DELETE /api/v1/backlog/{id}

Delete one objective or backlog item.

- Response: `204 No Content`
- Errors: `404 Not Found`

#### GET /api/v1/objectives/{id}/refinement-sessions and GET /api/v1/backlog/{id}/refinement-sessions

List refinement sessions linked to one objective or backlog item.

- Response: `200 OK` - `List<ObjectiveRefinementSession>`
- Notes: backlog refinement reads are authenticated and scoped like other backlog reads

#### POST /api/v1/objectives/{id}/refinement-sessions and POST /api/v1/backlog/{id}/refinement-sessions

Create one captain-backed refinement session for an objective or backlog item.

```json
{
  "CaptainId": "cpt_abc123",
  "VesselId": "vsl_abc123",
  "Title": "Refine rollout stabilization plan"
}
```

- Request body: `ObjectiveRefinementSessionCreateRequest`
- Response: `201 Created` - `ObjectiveRefinementSessionDetail`
- Errors: `400 Bad Request`, `404 Not Found`, `409 Conflict`, `501 Not Implemented`
- Notes:
  - `CaptainId` is required
  - `VesselId` is optional for refinement and falls back to the first linked vessel when available
  - refinement is lighter than planning and does not provision a dock/worktree by default

#### GET /api/v1/objective-refinement-sessions/{id}

Read one refinement session with transcript, captain, vessel, and linked objective/backlog detail.

- Response: `200 OK` - `ObjectiveRefinementSessionDetail`
- Errors: `404 Not Found`, `501 Not Implemented`

#### POST /api/v1/objective-refinement-sessions/{id}/messages

Append one user message to the refinement transcript and launch the next captain turn.

```json
{
  "Content": "Focus on rollback safety and the release verification sequence."
}
```

- Request body: `ObjectiveRefinementMessageRequest`
- Response: `200 OK` - `ObjectiveRefinementSessionDetail`
- Errors: `400 Bad Request`, `404 Not Found`, `409 Conflict`, `501 Not Implemented`

#### POST /api/v1/objective-refinement-sessions/{id}/summarize

Generate a structured refinement summary from a selected or inferred assistant message.

```json
{
  "MessageId": "orm_abc123"
}
```

- Request body: `ObjectiveRefinementSummaryRequest`
- Response: `200 OK` - `ObjectiveRefinementSummaryResponse`
- Errors: `404 Not Found`, `409 Conflict`, `501 Not Implemented`

#### POST /api/v1/objective-refinement-sessions/{id}/apply

Summarize and apply a refinement result back to the linked backlog item.

```json
{
  "MessageId": "orm_abc123",
  "MarkMessageSelected": true,
  "PromoteBacklogState": true
}
```

- Request body: `ObjectiveRefinementApplyRequest`
- Response: `200 OK` - `ObjectiveRefinementApplyResponse`
- Errors: `404 Not Found`, `409 Conflict`, `501 Not Implemented`

#### POST /api/v1/objective-refinement-sessions/{id}/stop

Stop an active refinement session and release the selected captain.

- Response: `200 OK` - `ObjectiveRefinementSessionDetail`
- Errors: `404 Not Found`, `501 Not Implemented`

#### DELETE /api/v1/objective-refinement-sessions/{id}

Delete a refinement session and its transcript. Active sessions are stopped first.

- Response: `204 No Content`
- Errors: `404 Not Found`, `409 Conflict`, `501 Not Implemented`

---

### Workflow Profiles

Workflow profiles define how a vessel or fleet builds, tests, versions, deploys, rolls back, and verifies itself. These routes back `Configuration > Workflow Profiles` and the preflight/resolution logic used by structured checks.

#### GET /api/v1/workflow-profiles

List workflow profiles in the caller scope.

- Query: `pageNumber`, `pageSize`, `scope`, `fleetId`, `vesselId`, `search`, `active`
- Response: `200 OK` - `EnumerationResult<WorkflowProfile>`

#### POST /api/v1/workflow-profiles/enumerate

Enumerate workflow profiles using a JSON body and optional querystring overrides.

- Request body: `WorkflowProfileQuery`
- Response: `200 OK` - `EnumerationResult<WorkflowProfile>`

#### POST /api/v1/workflow-profiles/validate

Validate a workflow profile without saving it.

- Request body: `WorkflowProfile`
- Response: `200 OK` - `WorkflowProfileValidationResult`
- Notes: validation returns errors, warnings, resolved command previews, available check types, and required input issues

#### GET /api/v1/workflow-profiles/preview/vessels/{vesselId}

Preview the resolved workflow commands for one vessel.

- Query: optional `workflowProfileId`
- Response: `200 OK` - `WorkflowProfileResolutionPreviewResult`
- Errors: `404 Not Found` when the vessel does not exist or no workflow profile can be resolved

#### GET /api/v1/workflow-profiles/resolve/vessels/{vesselId}

Resolve the active workflow profile for one vessel.

- Query: optional `workflowProfileId`
- Response: `200 OK` - `WorkflowProfile`
- Errors: `404 Not Found`

#### POST /api/v1/workflow-profiles

Create a workflow profile.

- Request body: `WorkflowProfile`
- Response: `201 Created` - `WorkflowProfile`
- Notes: reads are available to any authenticated caller in scope; create/update/delete require tenant admin or global admin

#### GET /api/v1/workflow-profiles/{id}

Read one workflow profile by ID.

- Response: `200 OK` - `WorkflowProfile`
- Errors: `404 Not Found`

#### PUT /api/v1/workflow-profiles/{id}

Update one workflow profile.

- Request body: `WorkflowProfile`
- Response: `200 OK` - `WorkflowProfile`
- Errors: `400 Bad Request`, `404 Not Found`

#### DELETE /api/v1/workflow-profiles/{id}

Delete one workflow profile.

- Response: `204 No Content`
- Errors: `404 Not Found`

---

### Check Runs

Structured check runs are the delivery-memory record for build, test, packaging, publish, deploy, rollback, smoke-test, health-check, release-versioning, and other workflow-profile-backed commands.

#### GET /api/v1/check-runs

List structured check runs in the caller scope.

- Query: `pageNumber`, `pageSize`, `workflowProfileId`, `vesselId`, `missionId`, `voyageId`, `type`, `status`, `source`, `providerName`, `environmentName`, `externalId`
- Response: `200 OK` - `EnumerationResult<CheckRun>`

#### POST /api/v1/check-runs/enumerate

Enumerate structured check runs using a JSON body and optional querystring overrides.

- Request body: `CheckRunQuery`
- Response: `200 OK` - `EnumerationResult<CheckRun>`

#### POST /api/v1/check-runs

Execute one structured check run.

```json
{
  "VesselId": "vsl_abc123",
  "WorkflowProfileId": "wfp_def456",
  "Type": "UnitTest",
  "Label": "Unit tests",
  "EnvironmentName": null
}
```

- Response: `201 Created` - `CheckRun`
- Errors: `400 Bad Request` when readiness, workflow resolution, or command validation fails

#### POST /api/v1/check-runs/import

Import an externally executed or provider-hosted check run into Armada history.

```json
{
  "VesselId": "vsl_abc123",
  "Type": "Build",
  "Status": "Passed",
  "Source": "External",
  "ProviderName": "GitHub Actions",
  "Label": "CI build",
  "StartedUtc": "2026-05-04T16:00:00Z",
  "CompletedUtc": "2026-05-04T16:02:15Z"
}
```

- Response: `201 Created` - `CheckRun`

#### POST /api/v1/check-runs/sync/github-actions

Pull recent GitHub Actions workflow runs for one vessel into Armada check history. This is an on-demand pull surface; it does not require a webhook listener.

Credential resolution order:

1. `Vessel.GitHubTokenOverride`
2. global `GitHubToken` from Armada configuration

```json
{
  "VesselId": "vsl_abc123",
  "WorkflowProfileId": "wfp_def456",
  "DeploymentId": "dep_ghi789",
  "EnvironmentName": "staging",
  "BranchName": "main",
  "RunCount": 10
}
```

- Request body: `GitHubActionsSyncRequest`
- Response: `200 OK` - `GitHubActionsSyncResult`
- Notes:
  - Imported runs are normalized into `CheckRun` records with `Source = External` and `ProviderName = GitHubActions`
  - When `DeploymentId` is supplied, synced runs are linked back to that deployment
  - Sync is idempotent by provider external run ID

#### GET /api/v1/check-runs/{id}

Read one structured check run.

- Response: `200 OK` - `CheckRun`
- Errors: `404 Not Found`

#### POST /api/v1/check-runs/{id}/retry

Retry a prior check run using the same resolved scope and command context.

- Response: `201 Created` - `CheckRun`
- Errors: `404 Not Found`

#### DELETE /api/v1/check-runs/{id}

Delete one structured check run.

- Response: `204 No Content`
- Errors: `404 Not Found`

---

### Environments

Environment records define named rollout targets such as `Development`, `Staging`, or `Production`, including verification definitions, approval requirements, and rollout monitoring settings.

#### GET /api/v1/environments

List environments in the caller scope.

- Query: `pageNumber`, `pageSize`, `vesselId`, `kind`, `isDefault`, `active`, `search`
- Response: `200 OK` - `EnumerationResult<DeploymentEnvironment>`

#### POST /api/v1/environments/enumerate

Enumerate environments using a JSON body and optional querystring overrides.

- Request body: `DeploymentEnvironmentQuery`
- Response: `200 OK` - `EnumerationResult<DeploymentEnvironment>`

#### POST /api/v1/environments

Create one environment.

```json
{
  "VesselId": "vsl_abc123",
  "Name": "Staging",
  "Kind": "Staging",
  "BaseUrl": "https://staging.example.com",
  "HealthEndpoint": "/api/v1/status/health",
  "RequiresApproval": true,
  "IsDefault": false
}
```

- Response: `201 Created` - `DeploymentEnvironment`

#### GET /api/v1/environments/{id}

Read one environment.

- Response: `200 OK` - `DeploymentEnvironment`
- Errors: `404 Not Found`

#### PUT /api/v1/environments/{id}

Update one environment.

- Request body: `DeploymentEnvironmentUpsertRequest`
- Response: `200 OK` - `DeploymentEnvironment`
- Errors: `400 Bad Request`, `404 Not Found`

#### DELETE /api/v1/environments/{id}

Delete one environment.

- Response: `204 No Content`
- Errors: `404 Not Found`

---

### Deployments

Deployment records track rollout approval, execution, verification, rollback, linked checks, and request-history evidence for a named environment.

#### GET /api/v1/deployments

List deployments in the caller scope.

- Query: `pageNumber`, `pageSize`, `vesselId`, `workflowProfileId`, `environmentId`, `environmentName`, `releaseId`, `missionId`, `voyageId`, `checkRunId`, `status`, `verificationStatus`, `search`, `fromUtc`, `toUtc`
- Response: `200 OK` - `EnumerationResult<Deployment>`

#### POST /api/v1/deployments/enumerate

Enumerate deployments using a JSON body and optional querystring overrides.

- Request body: `DeploymentQuery`
- Response: `200 OK` - `EnumerationResult<Deployment>`

#### POST /api/v1/deployments

Create one deployment. When approval is not required and `AutoExecute` is `true`, the deployment begins immediately.

```json
{
  "VesselId": "vsl_abc123",
  "EnvironmentId": "env_def456",
  "ReleaseId": "rel_ghi789",
  "Title": "Deploy May release to staging",
  "SourceRef": "refs/tags/v1.4.2",
  "AutoExecute": true
}
```

- Response: `201 Created` - `Deployment`

#### GET /api/v1/deployments/{id}

Read one deployment.

- Response: `200 OK` - `Deployment`
- Errors: `404 Not Found`

#### PUT /api/v1/deployments/{id}

Update one deployment and its mutable metadata.

- Request body: `DeploymentUpsertRequest`
- Response: `200 OK` - `Deployment`
- Errors: `400 Bad Request`, `404 Not Found`

#### POST /api/v1/deployments/{id}/approve

Approve one pending deployment and begin execution.

- Request body: optional `{ "Comment": "Ship it" }`
- Response: `200 OK` - `Deployment`
- Errors: `400 Bad Request`, `404 Not Found`

#### POST /api/v1/deployments/{id}/deny

Deny one pending deployment without executing it.

- Request body: optional `{ "Comment": "Need one more verification run" }`
- Response: `200 OK` - `Deployment`
- Errors: `400 Bad Request`, `404 Not Found`

#### POST /api/v1/deployments/{id}/verify

Re-run the configured post-deploy verification for an existing deployment.

- Response: `200 OK` - `Deployment`
- Errors: `400 Bad Request`, `404 Not Found`

#### POST /api/v1/deployments/{id}/rollback

Run the configured rollback flow for an existing deployment.

- Response: `200 OK` - `Deployment`
- Errors: `400 Bad Request`, `404 Not Found`

#### DELETE /api/v1/deployments/{id}

Delete one deployment record.

- Response: `204 No Content`
- Errors: `404 Not Found`

---

### Releases

Release records group versions, notes, artifacts, and linked work so a user can inspect what is shipping and what evidence exists that it is ready.

#### GET /api/v1/releases

List release records in the caller scope.

- Query: `pageNumber`, `pageSize`, `vesselId`, `workflowProfileId`, `voyageId`, `missionId`, `checkRunId`, `status`, `search`, `fromUtc`, `toUtc`
- Response: `200 OK` - `EnumerationResult<Release>`

#### POST /api/v1/releases/enumerate

Enumerate releases using a JSON body and optional querystring overrides.

- Request body: `ReleaseQuery`
- Response: `200 OK` - `EnumerationResult<Release>`

#### POST /api/v1/releases

Create a release from linked voyages, missions, and check runs.

```json
{
  "VesselId": "vsl_abc123",
  "Title": "May release",
  "Version": "1.4.2",
  "Status": "Candidate",
  "VoyageIds": ["vyg_abc123"],
  "MissionIds": ["msn_def456"],
  "CheckRunIds": ["chk_ghi789"]
}
```

- Response: `201 Created` - `Release`
- Notes: draft/candidate/shipped state, derived artifacts, notes, and version inference are all supported by the current internal release surface

#### GET /api/v1/releases/{id}

Read one release.

- Response: `200 OK` - `Release`
- Errors: `404 Not Found`

#### GET /api/v1/releases/{id}/github/pull-requests

Read normalized GitHub pull-request evidence derived from the `PrUrl` values on missions linked to one release.

- Response: `200 OK` - `List<GitHubPullRequestDetail>`
- Errors: `400 Bad Request`, `404 Not Found`
- Notes:
  - Duplicate repository/PR combinations are de-duplicated before the response is returned
  - This is evidence-only today; Armada does not yet persist first-class PR entities

#### PUT /api/v1/releases/{id}

Update one release and revalidate its linked work.

- Request body: `ReleaseUpsertRequest`
- Response: `200 OK` - `Release`
- Errors: `400 Bad Request`, `404 Not Found`

#### POST /api/v1/releases/{id}/refresh

Refresh one release from its current linked work.

- Response: `200 OK` - `Release`
- Errors: `400 Bad Request`, `404 Not Found`

#### DELETE /api/v1/releases/{id}

Delete one release.

- Response: `204 No Content`
- Errors: `404 Not Found`

---

### Incidents

Incident records capture operational failures, hotfix handoff, rollback linkage, and postmortem context tied to current deployment entities.

#### GET /api/v1/incidents

List incidents in the caller scope.

- Query: `pageNumber`, `pageSize`, `vesselId`, `environmentId`, `deploymentId`, `releaseId`, `missionId`, `voyageId`, `status`, `severity`, `search`
- Response: `200 OK` - `EnumerationResult<Incident>`

#### POST /api/v1/incidents/enumerate

Enumerate incidents using a JSON body and optional querystring overrides.

- Request body: `IncidentQuery`
- Response: `200 OK` - `EnumerationResult<Incident>`

#### POST /api/v1/incidents

Create one incident.

- Request body: `IncidentUpsertRequest`
- Response: `201 Created` - `Incident`
- Errors: `400 Bad Request`

#### GET /api/v1/incidents/{id}

Read one incident.

- Response: `200 OK` - `Incident`
- Errors: `404 Not Found`

#### PUT /api/v1/incidents/{id}

Update one incident, including status, impact, recovery, and postmortem details.

- Request body: `IncidentUpsertRequest`
- Response: `200 OK` - `Incident`
- Errors: `400 Bad Request`, `404 Not Found`

#### DELETE /api/v1/incidents/{id}

Delete one incident.

- Response: `204 No Content`
- Errors: `404 Not Found`

---

### Runbooks

Runbooks extend playbooks into parameterized operational procedures with step tracking, deployment and incident linkage, and event-backed execution history.

#### GET /api/v1/runbooks

List runbooks in the caller scope.

- Query: `pageNumber`, `pageSize`, `workflowProfileId`, `environmentId`, `defaultCheckType`, `active`, `search`
- Response: `200 OK` - `EnumerationResult<Runbook>`

#### POST /api/v1/runbooks/enumerate

Enumerate runbooks using a JSON body and optional querystring overrides.

- Request body: `RunbookQuery`
- Response: `200 OK` - `EnumerationResult<Runbook>`

#### POST /api/v1/runbooks

Create one runbook backed by a playbook and optional explicit steps/parameters.

- Request body: `RunbookUpsertRequest`
- Response: `201 Created` - `Runbook`
- Errors: `400 Bad Request`

#### GET /api/v1/runbooks/{id}

Read one runbook.

- Response: `200 OK` - `Runbook`
- Errors: `404 Not Found`

#### PUT /api/v1/runbooks/{id}

Update one runbook.

- Request body: `RunbookUpsertRequest`
- Response: `200 OK` - `Runbook`
- Errors: `400 Bad Request`, `404 Not Found`

#### DELETE /api/v1/runbooks/{id}

Delete one runbook.

- Response: `204 No Content`
- Errors: `404 Not Found`

#### GET /api/v1/runbook-executions

List runbook executions in the caller scope.

- Query: `pageNumber`, `pageSize`, `runbookId`, `deploymentId`, `incidentId`, `status`, `search`
- Response: `200 OK` - `EnumerationResult<RunbookExecution>`

#### POST /api/v1/runbook-executions/enumerate

Enumerate runbook executions using a JSON body and optional querystring overrides.

- Request body: `RunbookExecutionQuery`
- Response: `200 OK` - `EnumerationResult<RunbookExecution>`

#### POST /api/v1/runbooks/{id}/executions

Start one runbook execution.

- Request body: `RunbookExecutionStartRequest`
- Response: `201 Created` - `RunbookExecution`
- Errors: `400 Bad Request`, `404 Not Found`

#### GET /api/v1/runbook-executions/{id}

Read one runbook execution.

- Response: `200 OK` - `RunbookExecution`
- Errors: `404 Not Found`

#### PUT /api/v1/runbook-executions/{id}

Update one runbook execution, including step completion and notes.

- Request body: `RunbookExecutionUpdateRequest`
- Response: `200 OK` - `RunbookExecution`
- Errors: `400 Bad Request`, `404 Not Found`

#### DELETE /api/v1/runbook-executions/{id}

Delete one runbook execution.

- Response: `204 No Content`
- Errors: `404 Not Found`

---

### History

`Activity` (All Activity) is backed by a cross-entity timeline that spans current Armada lifecycle entities such as objectives, releases, deployments, incidents, runbook executions, missions, voyages, planning sessions, merge entries, check runs, events, and request history.

#### GET /api/v1/history

List historical timeline entries in the caller scope.

- Query: `pageNumber`, `pageSize`, `vesselId`, `missionId`, `voyageId`, `objectiveId`, `environmentId`, `deploymentId`, `incidentId`, `actor`, `text`, `sourceType`, `fromUtc`, `toUtc`
- Response: `200 OK` - `EnumerationResult<HistoricalTimelineEntry>`

#### POST /api/v1/history/enumerate

Enumerate historical timeline entries using a JSON body and optional querystring overrides.

- Request body: `HistoricalTimelineQuery`
- Response: `200 OK` - `EnumerationResult<HistoricalTimelineEntry>`

---

### Runtime Helpers

These helper routes support runtime-specific UX and validation. As of `v0.9.0`, the shipped runtime-helper surface is focused on Mux endpoint discovery for captain setup and editing.

#### GET /api/v1/runtimes/mux/endpoints

List saved Mux endpoints, optionally from an explicit config directory.

- Query: optional `configDirectory`
- Response: `200 OK` - `MuxEndpointListResult`

#### GET /api/v1/runtimes/mux/endpoints/{name}

Inspect one saved Mux endpoint with redacted secret values.

- Query: optional `configDirectory`
- Response: `200 OK` - `MuxEndpointShowResult`
- Errors: `404 Not Found` when the named endpoint does not exist

---

### Request History

Armada captures sanitized REST request and response metadata for authenticated and unauthenticated traffic, excluding the request-history routes themselves. Secret-bearing headers and request bodies are redacted before persistence.

#### GET /api/v1/request-history

List captured request-history entries in the caller’s scope.

- Query: `pageNumber`, `pageSize`, `method`, `route`, `statusCode`, `principal`, `tenantId`, `userId`, `credentialId`, `isSuccess`, `fromUtc`, `toUtc`
- Response: `200 OK` - `EnumerationResult<RequestHistoryEntry>`

#### GET /api/v1/request-history/summary

Return aggregate counts and time buckets for matching request-history entries.

- Query: `bucketMinutes`, `fromUtc`, `toUtc`, `method`, `route`, `statusCode`, `principal`
- Defaults: last 24 hours, 15-minute buckets
- Response: `200 OK` - `RequestHistorySummaryResult`

#### GET /api/v1/request-history/{id}

Read one captured request, including expanded headers, params, and body snapshots.

- Response: `200 OK` - `RequestHistoryRecord`
- Errors: `404 Not Found`

#### DELETE /api/v1/request-history/{id}

Delete one captured request-history entry within the caller’s scope.

- Response: `204 No Content`

#### POST /api/v1/request-history/delete/multiple

Delete multiple request-history entries by identifier.

```json
{
  "Ids": ["req_abc123", "req_def456"]
}
```

- Response: `200 OK` - `DeleteMultipleResult`

#### POST /api/v1/request-history/delete/by-filter

Delete all request-history entries matching the supplied filters within the caller’s scope.

```json
{
  "Method": "GET",
  "Route": "/api/v1/status",
  "FromUtc": "2026-05-01T00:00:00Z",
  "ToUtc": "2026-05-02T00:00:00Z"
}
```

- Response: `200 OK` - `DeleteMultipleResult`

---

### Token Usage

Armada records per-model token usage for every mission run, Ask Armada turn, and planning turn. Counts are normalized across providers: `input` covers prompt tokens, `output` covers completion tokens, `cached` is the cache-read subset of input (informational), and `total` is input + output. Counts are measured where the runtime reports usage (for example Claude Code's `output_tokens`) and estimated from text length otherwise; estimated records set `estimated: true` and are counted in the summary's `estimatedCount`.

#### GET /api/v1/token-usage/summary

Return token usage aggregated into time buckets (each with a per-model breakdown), a whole-window per-model aggregate ordered most-used first, and grand totals. This is the data behind the dashboard Token Usage charts.

- Query: `fromUtc`, `toUtc`, `bucketMinutes`, `model`, `runtime`, `source`, `vesselId`, `captainId`, `tenantId`, `userId`
- Defaults: last 24 hours, 15-minute buckets
- `source` is one of `mission`, `chat`, `planning`
- Response: `200 OK` - `TokenUsageSummaryResult` (fields: `fromUtc`, `toUtc`, `bucketMinutes`, `recordCount`, `estimatedCount`, `inputTokens`, `outputTokens`, `cachedTokens`, `totalTokens`, `buckets[]`, `byModel[]`)

#### GET /api/v1/token-usage

List token-usage records in the caller's scope.

- Query: `pageNumber`, `pageSize`, `model`, `runtime`, `source`, `vesselId`, `captainId`, `fromUtc`, `toUtc`
- Response: `200 OK` - `EnumerationResult<TokenUsageRecord>`

#### POST /api/v1/token-usage/delete/by-filter

Delete all token-usage records matching the supplied filters within the caller's scope.

```json
{
  "Model": "claude-sonnet-4",
  "Source": "mission",
  "FromUtc": "2026-05-01T00:00:00Z",
  "ToUtc": "2026-05-02T00:00:00Z"
}
```

- Response: `200 OK` - `DeleteMultipleResult`

---

### OpenAPI Discovery

Armada publishes live REST metadata for both human and machine consumers.

#### GET /openapi.json

Return the live OpenAPI document used by the dashboard API Explorer.

#### GET /swagger

Return the interactive Swagger UI for the same OpenAPI surface.

---

## Per-Step Captain Selection

Personas can carry a default captain, and dispatch can dictate which captain runs each pipeline step, with a capability-tier fallback when that captain is busy. See [CAPTAIN_ROUTING.md](CAPTAIN_ROUTING.md) for the full model.

- `POST` / `PUT /api/v1/personas` accept and return `defaultCaptainId` (a `cpt_` id). An invalid captain id is rejected with `400`. `PUT` sets the field to the request value, so an omitted/null value clears the default.
- `POST /api/v1/voyages` accepts `captainAssignments`, an array of `{ persona, captainId, fallbackTier }` binding each pipeline step (persona) to a preferred captain and a fallback tier (`Economy` | `Standard` | `Premium`). Each entry in `missions` may also carry `requestedCaptainId` and `tier`. The overrides are stored on the voyage and resolved at assignment time, including for fan-out missions.
- Mission reads (`GET /api/v1/missions/{id}`, enumerate, summaries) include both `requestedCaptainId` (the preferred captain) and `captainId` (the captain that actually ran).

## Data Types

### Models

#### TenantMetadata

A tenant in the multi-tenant system.

```json
{
  "Id": "ten_abc123",
  "Name": "Acme Corp",
  "Active": true,
  "CreatedUtc": "2026-03-07T12:00:00Z",
  "LastUpdateUtc": "2026-03-07T12:00:00Z"
}
```

| Field | Type | Default | Description |
|---|---|---|---|
| `Id` | string | auto-generated | Unique ID with `ten_` prefix |
| `Name` | string | `"My Tenant"` | Tenant name |
| `Active` | bool | true | Whether tenant is active |
| `IsProtected` | bool | false | Protected tenants cannot be deleted directly |
| `CreatedUtc` | datetime | now | Creation timestamp (UTC) |
| `LastUpdateUtc` | datetime | now | Last update timestamp (UTC) |

---

#### UserMaster

A user in the multi-tenant system. Passwords are stored as SHA256 hashes and redacted in API responses.

```json
{
  "Id": "usr_abc123",
  "TenantId": "default",
  "Email": "admin@armada",
  "PasswordSha256": "********",
  "FirstName": "Jane",
  "LastName": "Doe",
  "IsAdmin": false,
  "IsTenantAdmin": false,
  "IsProtected": false,
  "Active": true,
  "CreatedUtc": "2026-03-07T12:00:00Z",
  "LastUpdateUtc": "2026-03-07T12:00:00Z"
}
```

| Field | Type | Default | Description |
|---|---|---|---|
| `Id` | string | auto-generated | Unique ID with `usr_` prefix |
| `TenantId` | string | `"default"` | Parent tenant |
| `Email` | string | `"admin@armada"` | Email address (unique within tenant) |
| `PasswordSha256` | string | SHA256("password") | SHA256 hash of password (redacted in responses) |
| `FirstName` | string? | null | First name |
| `LastName` | string? | null | Last name |
| `IsAdmin` | bool | false | Global system admin privileges |
| `IsTenantAdmin` | bool | false | Tenant-scoped admin privileges |
| `IsProtected` | bool | false | Protected users cannot be deleted directly |
| `Active` | bool | true | Whether user is active |
| `CreatedUtc` | datetime | now | Creation timestamp (UTC) |
| `LastUpdateUtc` | datetime | now | Last update timestamp (UTC) |

---

#### Credential

A bearer token credential for API authentication.

```json
{
  "Id": "crd_abc123",
  "TenantId": "default",
  "UserId": "default",
  "Name": "My API Token",
  "BearerToken": "aBcDeFgHiJkLmNoPqRsTuVwXyZ0123456789...",
  "IsProtected": false,
  "Active": true,
  "CreatedUtc": "2026-03-07T12:00:00Z",
  "LastUpdateUtc": "2026-03-07T12:00:00Z"
}
```

| Field | Type | Default | Description |
|---|---|---|---|
| `Id` | string | auto-generated | Unique ID with `crd_` prefix |
| `TenantId` | string | `"default"` | Parent tenant |
| `UserId` | string | `"default"` | Owning user |
| `Name` | string? | null | Friendly name |
| `BearerToken` | string | auto-generated | 64-character random alphanumeric token |
| `IsProtected` | bool | false | Protected credentials cannot be deleted directly |
| `Active` | bool | true | Whether credential is active |
| `CreatedUtc` | datetime | now | Creation timestamp (UTC) |
| `LastUpdateUtc` | datetime | now | Last update timestamp (UTC) |

---

#### AuthContext

Represents the authenticated identity context resolved from any authentication method.

```json
{
  "IsAuthenticated": true,
  "TenantId": "default",
  "UserId": "default",
  "IsAdmin": true,
  "IsTenantAdmin": true,
  "AuthMethod": "Bearer"
}
```

| Field | Type | Description |
|---|---|---|
| `IsAuthenticated` | bool | Whether the request is authenticated |
| `TenantId` | string? | Tenant identifier |
| `UserId` | string? | User identifier |
| `IsAdmin` | bool | Global system admin privileges |
| `IsTenantAdmin` | bool | Tenant-scoped admin privileges |
| `AuthMethod` | string? | `"Bearer"`, `"Session"`, `"ApiKey"`, or null |

---

Role semantics:

- `IsAdmin = true`: global system-wide admin with access to every tenant and object.
- `IsAdmin = false`, `IsTenantAdmin = true`: tenant-scoped admin with full access inside that tenant.
- `IsAdmin = false`, `IsTenantAdmin = false`: regular user limited to read-only tenant visibility plus self-service on their own user account and credentials.

Immutable update fields:

- `Id`, creation timestamps, ownership identifiers (`TenantId`, `UserId` where applicable), and `IsProtected` are preserved server-side on update routes.

---

#### WhoAmIResult

Result of `GET /api/v1/whoami`.

```json
{
  "Tenant": { ... },
  "User": { ... }
}
```

| Field | Type | Description |
|---|---|---|
| `Tenant` | [TenantMetadata](#tenantmetadata) | Tenant information |
| `User` | [UserMaster](#usermaster) | User information (password redacted) |

---

#### AuthenticateRequest

Request body for `POST /api/v1/authenticate`.

| Field | Type | Required | Description |
|---|---|---|---|
| `TenantId` | string | Yes | Tenant identifier |
| `Email` | string | Yes | User email address |
| `Password` | string | Yes | Plaintext password |

---

#### TenantLookupRequest

Request body for `POST /api/v1/tenants/lookup`.

| Field | Type | Required | Description |
|---|---|---|---|
| `Email` | string | Yes | Email address to look up |

---

#### TenantLookupResult

Result of `POST /api/v1/tenants/lookup`.

| Field | Type | Description |
|---|---|---|
| `Tenants` | array | List of `{ TenantId, TenantName }` entries matching the email |

---

#### OnboardingRequest

Request body for `POST /api/v1/onboarding`.

| Field | Type | Required | Description |
|---|---|---|---|
| `TenantId` | string | Yes | Tenant to join |
| `Email` | string | Yes | Email address |
| `Password` | string | Yes | Plaintext password |
| `FirstName` | string? | No | First name |
| `LastName` | string? | No | Last name |

---

#### OnboardingResult

Result of `POST /api/v1/onboarding`.

| Field | Type | Description |
|---|---|---|
| `Success` | bool | Whether onboarding succeeded |
| `Tenant` | [TenantMetadata](#tenantmetadata)? | Created/joined tenant |
| `User` | [UserMaster](#usermaster)? | Created user (password redacted) |
| `Credential` | [Credential](#credential)? | Created credential with bearer token |
| `ErrorMessage` | string? | Error message if failed |

---

#### Fleet

A named collection of repositories under management.

```json
{
  "Id": "flt_abc123",
  "Name": "Production Fleet",
  "Description": "Production repositories",
  "Active": true,
  "CreatedUtc": "2026-03-07T12:00:00Z",
  "LastUpdateUtc": "2026-03-07T12:00:00Z"
}
```

| Field | Type | Default | Description |
|---|---|---|---|
| `Id` | string | auto-generated | Unique ID with `flt_` prefix |
| `Name` | string | `"My Fleet"` | Fleet name |
| `Description` | string? | null | Fleet description |
| `Active` | bool | true | Whether fleet is active |
| `CreatedUtc` | datetime | now | Creation timestamp (UTC) |
| `LastUpdateUtc` | datetime | now | Last update timestamp (UTC) |

---

#### Vessel

A git repository registered with Armada.

```json
{
  "Id": "vsl_abc123",
  "FleetId": "flt_abc123",
  "Name": "MyRepo",
  "RepoUrl": "https://github.com/org/repo.git",
  "LocalPath": "/home/user/.armada/repos/MyRepo",
  "WorkingDirectory": null,
  "DefaultBranch": "main",
  "ProjectContext": null,
  "StyleGuide": null,
  "EnableModelContext": false,
  "ModelContext": null,
  "HasGitHubTokenOverride": false,
  "LandingMode": null,
  "BranchCleanupPolicy": null,
  "Active": true,
  "CreatedUtc": "2026-03-07T12:00:00Z",
  "LastUpdateUtc": "2026-03-07T12:00:00Z"
}
```

| Field | Type | Default | Description |
|---|---|---|---|
| `Id` | string | auto-generated | Unique ID with `vsl_` prefix |
| `FleetId` | string? | null | Parent fleet ID |
| `Name` | string | `"My Vessel"` | Vessel name |
| `RepoUrl` | string? | null | Remote repository URL |
| `LocalPath` | string? | null | Local path to bare repository clone |
| `WorkingDirectory` | string? | null | Local working directory for merge on completion |
| `DefaultBranch` | string | `"main"` | Default branch name |
| `ProjectContext` | string? | null | Project context describing architecture, key files, and dependencies |
| `StyleGuide` | string? | null | Style guide describing naming conventions, patterns, and library preferences |
| `EnableModelContext` | bool | false | Whether model context accumulation is enabled |
| `ModelContext` | string? | null | Agent-accumulated context about this repository |
| `HasGitHubTokenOverride` | bool | false | Indicates whether a per-vessel GitHub token override is stored. The raw override value is never returned by the API. |
| `LandingMode` | [LandingModeEnum](#landingmodeenum)? | null | Per-vessel landing policy override (null = use global setting) |
| `BranchCleanupPolicy` | [BranchCleanupPolicyEnum](#branchcleanuppolicyenum)? | null | Per-vessel branch cleanup policy override (null = use global setting) |
| `DefinitionOfDoneEnabled` | bool | false | When true, the build and unit-test commands below run inside a mission's own checkout before acceptance; a failure blocks landing with a classified reason (Compile/TestFail/Timeout/Infra) |
| `DefinitionOfDoneBuildCommand` | string? | null | Shell command that builds the project inside the mission checkout (e.g. `dotnet build`); a non-zero exit classifies as Compile |
| `DefinitionOfDoneTestCommand` | string? | null | Shell command that runs unit tests inside the mission checkout (e.g. `dotnet test`); a non-zero exit classifies as TestFail |
| `DefinitionOfDoneTimeoutSeconds` | int | 1800 | Per-phase timeout in seconds (clamped to [30, 7200]); exceeding it classifies as Timeout |
| `Active` | bool | true | Whether vessel is active |
| `CreatedUtc` | datetime | now | Creation timestamp (UTC) |
| `LastUpdateUtc` | datetime | now | Last update timestamp (UTC) |

---

#### Voyage

A batch of related missions tracked together.

```json
{
  "Id": "vyg_abc123",
  "Title": "API Hardening",
  "Description": "Security improvements across the API",
  "Status": "InProgress",
  "SelectedPlaybooks": [
    {
      "PlaybookId": "pbk_abc123",
      "DeliveryMode": "InlineFullContent"
    }
  ],
  "CreatedUtc": "2026-03-07T12:00:00Z",
  "CompletedUtc": null,
  "LastUpdateUtc": "2026-03-07T12:00:00Z",
  "AutoPush": null,
  "AutoCreatePullRequests": null,
  "AutoMergePullRequests": null,
  "LandingMode": null
}
```

| Field | Type | Default | Description |
|---|---|---|---|
| `Id` | string | auto-generated | Unique ID with `vyg_` prefix |
| `Title` | string | `"New Voyage"` | Voyage title |
| `Description` | string? | null | Voyage description |
| `Status` | [VoyageStatusEnum](#voyagestatusenum) | `Open` | Current status |
| `SelectedPlaybooks` | array\<[SelectedPlaybook](#selectedplaybook)\> | `[]` | Ordered playbook selections recorded on the voyage |
| `CreatedUtc` | datetime | now | Creation timestamp (UTC) |
| `CompletedUtc` | datetime? | null | Completion timestamp (UTC) |
| `LastUpdateUtc` | datetime | now | Last update timestamp (UTC) |
| `AutoPush` | bool? | null | Per-voyage auto-push override (null = use global setting) |
| `AutoCreatePullRequests` | bool? | null | Per-voyage auto-create PRs override |
| `AutoMergePullRequests` | bool? | null | Per-voyage auto-merge PRs override |
| `LandingMode` | [LandingModeEnum](#landingmodeenum)? | null | Per-voyage landing policy override (null = use vessel/global setting) |

---

#### Mission

An atomic unit of work assigned to a captain.

```json
{
  "Id": "msn_abc123",
  "VoyageId": "vyg_abc123",
  "VesselId": "vsl_abc123",
  "CaptainId": "cpt_abc123",
  "Title": "Fix login bug",
  "Description": "The login form does not validate email addresses",
  "Status": "InProgress",
  "Mode": "Implementation",
  "Priority": 100,
  "SelectedPlaybooks": [
    {
      "PlaybookId": "pbk_abc123",
      "DeliveryMode": "InstructionWithReference"
    }
  ],
  "PlaybookSnapshots": [
    {
      "PlaybookId": "pbk_abc123",
      "FileName": "CSHARP_BACKEND_ARCHITECTURE.md",
      "DeliveryMode": "InstructionWithReference",
      "ResolvedPath": "C:\\Armada\\runtime\\playbooks\\msn_abc123\\01_CSHARP_BACKEND_ARCHITECTURE.md"
    }
  ],
  "ParentMissionId": null,
  "BranchName": "armada/msn_abc123",
  "DockId": null,
  "ProcessId": null,
  "PrUrl": null,
  "CommitHash": null,
  "DiffSnapshot": null,
  "CreatedUtc": "2026-03-07T12:00:00Z",
  "StartedUtc": "2026-03-07T12:05:00Z",
  "CompletedUtc": null,
  "LastUpdateUtc": "2026-03-07T12:10:00Z"
}
```

| Field | Type | Default | Description |
|---|---|---|---|
| `Id` | string | auto-generated | Unique ID with `msn_` prefix |
| `VoyageId` | string? | null | Parent voyage ID |
| `VesselId` | string? | null | Target vessel (repository) ID |
| `CaptainId` | string? | null | Assigned captain (agent) ID |
| `Title` | string | `"New Mission"` | Mission title |
| `Description` | string? | null | Detailed instructions for the AI agent |
| `Status` | [MissionStatusEnum](#missionstatusenum) | `Pending` | Current status |
| `Mode` | [MissionModeEnum](#missionmodeenum) | `Implementation` | Execution mode. `Audit` and `Research` are read-only modes whose empty diff is treated as success |
| `Priority` | int | 100 | Priority (lower number = higher priority) |
| `SelectedPlaybooks` | array\<[SelectedPlaybook](#selectedplaybook)\> | `[]` | Ordered playbook selections requested for the mission |
| `PlaybookSnapshots` | array\<[MissionPlaybookSnapshot](#missionplaybooksnapshot)\> | `[]` | Immutable playbook materialization used for execution |
| `ParentMissionId` | string? | null | Parent mission ID for sub-tasks |
| `BranchName` | string? | null | Git branch name |
| `DockId` | string? | null | Dock identifier for the mission's worktree |
| `ProcessId` | int? | null | OS process ID of the agent working on the mission |
| `PrUrl` | string? | null | Pull request URL if created |
| `CommitHash` | string? | null | Git commit hash captured on completion |
| `DiffSnapshot` | string? | null | Always `null` in list/status responses to keep payloads compact. Use `GET /api/v1/missions/{id}/diff` to retrieve the full diff. |
| `CreatedUtc` | datetime | now | Creation timestamp (UTC) |
| `StartedUtc` | datetime? | null | Work start timestamp (UTC) |
| `CompletedUtc` | datetime? | null | Completion timestamp (UTC) |
| `TotalRuntimeMs` | long? | null | Total execution runtime in milliseconds |
| `LastUpdateUtc` | datetime | now | Last update timestamp (UTC) |

---

#### Captain

A worker AI agent instance executing missions.

```json
{
  "Id": "cpt_abc123",
  "Name": "captain-1",
  "Runtime": "ClaudeCode",
  "Model": null,
  "SystemInstructions": null,
  "State": "Idle",
  "CurrentMissionId": null,
  "CurrentDockId": null,
  "ProcessId": null,
  "RecoveryAttempts": 0,
  "LastHeartbeatUtc": null,
  "CreatedUtc": "2026-03-07T12:00:00Z",
  "LastUpdateUtc": "2026-03-07T12:00:00Z"
}
```

| Field | Type | Default | Description |
|---|---|---|---|
| `Id` | string | auto-generated | Unique ID with `cpt_` prefix |
| `Name` | string | `"Captain"` | Captain name |
| `Runtime` | [AgentRuntimeEnum](#agentruntimeenum) | `ClaudeCode` | Agent runtime type |
| `Model` | string? | null | Optional model override for this captain. When null, the runtime chooses its default model |
| `SystemInstructions` | string? | null | Per-captain system instructions injected into every mission prompt |
| `State` | [CaptainStateEnum](#captainstateenum) | `Idle` | Current state |
| `CurrentMissionId` | string? | null | Currently assigned mission ID |
| `CurrentDockId` | string? | null | Currently assigned dock (worktree) ID |
| `ProcessId` | int? | null | OS process ID |
| `RecoveryAttempts` | int | 0 | Auto-recovery attempts for current mission |
| `LastHeartbeatUtc` | datetime? | null | Last heartbeat timestamp (UTC) |
| `CreatedUtc` | datetime | now | Creation timestamp (UTC) |
| `LastUpdateUtc` | datetime | now | Last update timestamp (UTC) |

---

#### CaptainToolAccessResult

Captain-scoped Armada MCP tool availability and catalog metadata.

```json
{
  "captainId": "cpt_abc123",
  "captainName": "captain-1",
  "runtime": "Mux",
  "toolsAccessible": true,
  "availabilityVerified": true,
  "availabilitySource": "mux-probe",
  "summary": "Mux probe succeeded and the endpoint reported tool calling enabled.",
  "endpointName": "local-codex",
  "toolsEnabled": true,
  "effectiveToolCount": 42,
  "armadaToolCount": 37,
  "tools": [
    {
      "name": "get_status",
      "description": "Get current Admiral status snapshot",
      "inputSchemaJson": "{\"type\":\"object\",\"properties\":{}}"
    }
  ]
}
```

| Field | Type | Description |
|---|---|---|
| `captainId` | string | Captain ID |
| `captainName` | string | Captain display name |
| `runtime` | [AgentRuntimeEnum](#agentruntimeenum) | Captain runtime |
| `toolsAccessible` | bool | Whether Armada currently considers the catalog reachable through this captain |
| `availabilityVerified` | bool | Whether Armada actively verified availability instead of inferring it |
| `availabilitySource` | string | Machine-readable availability source such as `mux-probe` or `runtime-assumption` |
| `summary` | string | Human-readable explanation of availability and caveats |
| `endpointName` | string? | Mux endpoint name when applicable |
| `toolsEnabled` | bool? | Whether the runtime reported tool calling enabled when applicable |
| `effectiveToolCount` | int? | Runtime-reported total tool count when applicable |
| `armadaToolCount` | int | Number of Armada MCP tools in the returned catalog |
| `tools` | [CaptainToolSummary](#captaintoolsummary)[] | Ordered tool list |

---

#### CaptainToolSummary

One Armada MCP tool entry in a captain catalog response.

| Field | Type | Description |
|---|---|---|
| `name` | string | Tool name |
| `description` | string | Human-readable tool description |
| `inputSchemaJson` | string? | Serialized JSON input schema when available |

---

#### Signal

A message between the admiral and captains.

```json
{
  "Id": "sig_abc123",
  "FromCaptainId": "cpt_abc123",
  "ToCaptainId": null,
  "Type": "Progress",
  "Payload": "Mission msn_abc123 transitioned to Testing",
  "Read": false,
  "CreatedUtc": "2026-03-07T12:00:00Z"
}
```

| Field | Type | Default | Description |
|---|---|---|---|
| `Id` | string | auto-generated | Unique ID with `sig_` prefix |
| `FromCaptainId` | string? | null | Sender captain ID (null = from Admiral) |
| `ToCaptainId` | string? | null | Recipient captain ID (null = to Admiral) |
| `Type` | [SignalTypeEnum](#signaltypeenum) | `Nudge` | Signal type |
| `Payload` | string? | null | Message payload |
| `Read` | bool | false | Whether signal has been read |
| `CreatedUtc` | datetime | now | Creation timestamp (UTC) |

---

#### ArmadaEvent

A recorded event representing a state change in the system.

```json
{
  "Id": "evt_abc123",
  "EventType": "mission.status_changed",
  "EntityType": "mission",
  "EntityId": "msn_abc123",
  "CaptainId": "cpt_abc123",
  "MissionId": "msn_abc123",
  "VesselId": "vsl_abc123",
  "VoyageId": "vyg_abc123",
  "Message": "Mission msn_abc123 transitioned to Complete",
  "Payload": null,
  "CreatedUtc": "2026-03-07T12:00:00Z"
}
```

| Field | Type | Default | Description |
|---|---|---|---|
| `Id` | string | auto-generated | Unique ID with `evt_` prefix |
| `EventType` | string | `""` | Event type identifier |
| `EntityType` | string? | null | Related entity type |
| `EntityId` | string? | null | Related entity ID |
| `CaptainId` | string? | null | Related captain ID |
| `MissionId` | string? | null | Related mission ID |
| `VesselId` | string? | null | Related vessel ID |
| `VoyageId` | string? | null | Related voyage ID |
| `Message` | string | `""` | Human-readable event message |
| `Payload` | string? | null | JSON payload with additional details |
| `CreatedUtc` | datetime | now | Event timestamp (UTC) |

**Known Event Types:**
- `mission.created` - Mission was created
- `mission.status_changed` - Mission status transitioned
- `mission.completed` - Mission completed successfully
- `mission.failed` - Mission failed
- `captain.launched` - Captain agent process started
- `captain.stopped` - Captain agent process stopped
- `captain.stalled` - Captain detected as stalled
- `voyage.created` - Voyage was created
- `voyage.completed` - All missions in voyage completed
- `voyage.deleted` - Voyage permanently deleted

---

#### MergeEntry

An entry in the merge queue representing a branch to be tested and merged.

```json
{
  "Id": "mrg_abc123",
  "MissionId": "msn_abc123",
  "VesselId": "vsl_abc123",
  "BranchName": "armada/msn_abc123",
  "TargetBranch": "main",
  "Status": "Queued",
  "Priority": 0,
  "BatchId": null,
  "TestCommand": "dotnet test",
  "TestOutput": null,
  "TestExitCode": null,
  "CreatedUtc": "2026-03-07T12:00:00Z",
  "LastUpdateUtc": "2026-03-07T12:00:00Z",
  "TestStartedUtc": null,
  "CompletedUtc": null
}
```

| Field | Type | Default | Description |
|---|---|---|---|
| `Id` | string | auto-generated | Unique ID with `mrg_` prefix |
| `MissionId` | string? | null | Parent mission ID |
| `VesselId` | string? | null | Vessel ID |
| `BranchName` | string | `"unknown"` | Branch to merge |
| `TargetBranch` | string | `"main"` | Target branch |
| `Status` | [MergeStatusEnum](#mergestatusenum) | `Queued` | Current status |
| `Priority` | int | 0 | Queue priority (lower = higher) |
| `BatchId` | string? | null | Batch ID during batch testing |
| `TestCommand` | string? | null | Test command for verification |
| `TestOutput` | string? | null | Test output or error message |
| `TestExitCode` | int? | null | Test process exit code |
| `CreatedUtc` | datetime | now | Creation timestamp (UTC) |
| `LastUpdateUtc` | datetime | now | Last update timestamp (UTC) |
| `TestStartedUtc` | datetime? | null | Test start timestamp (UTC) |
| `CompletedUtc` | datetime? | null | Completion timestamp (UTC) |

---

#### ArmadaStatus

Aggregate status summary returned by the status endpoint.

```json
{
  "TotalCaptains": 5,
  "IdleCaptains": 2,
  "WorkingCaptains": 3,
  "StalledCaptains": 0,
  "ActiveVoyages": 1,
  "MissionsByStatus": {
    "Pending": 3,
    "InProgress": 2,
    "Complete": 10
  },
  "Voyages": [
    {
      "Voyage": { ... },
      "TotalMissions": 5,
      "CompletedMissions": 3,
      "FailedMissions": 0,
      "InProgressMissions": 2
    }
  ],
  "RecentSignals": [],
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
      "ArmadaVersion": "0.9.0",
      "Features": [
        "remoteControl.handshake",
        "remoteControl.heartbeat",
        "status.health",
        "status.snapshot",
        "settings.remoteControl"
      ]
    }
  },
  "TimestampUtc": "2026-03-07T12:00:00Z"
}
```

| Field | Type | Description |
|---|---|---|
| `TotalCaptains` | int | Total registered captains |
| `IdleCaptains` | int | Number of idle captains |
| `WorkingCaptains` | int | Number of working captains |
| `StalledCaptains` | int | Number of stalled captains |
| `ActiveVoyages` | int | Total active voyages |
| `MissionsByStatus` | dict\<string, int\> | Mission counts grouped by status |
| `Voyages` | array | Active [VoyageProgress](#voyageprogress) objects |
| `RecentSignals` | array | Recent [Signal](#signal) objects |
| `RemoteTunnel` | [RemoteTunnelStatus](#remotetunnelstatus) | Current outbound remote tunnel status |
| `TimestampUtc` | datetime | Snapshot timestamp (UTC) |

---

#### RemoteTunnelStatus

Current outbound remote tunnel status and telemetry.

| Field | Type | Description |
|---|---|---|
| `Enabled` | bool | Whether the remote tunnel feature is enabled |
| `State` | string | Tunnel state (`Disabled`, `Disconnected`, `Connecting`, `Connected`, `Error`, `Stopping`) |
| `TunnelUrl` | string? | Configured or normalized websocket endpoint |
| `InstanceId` | string? | Stable instance identifier advertised during handshake |
| `LastConnectAttemptUtc` | datetime? | Most recent connection attempt |
| `ConnectedUtc` | datetime? | Timestamp when the current/last successful connection was established |
| `LastHeartbeatUtc` | datetime? | Last heartbeat or inbound tunnel activity timestamp |
| `LastDisconnectUtc` | datetime? | Most recent disconnect timestamp |
| `LastError` | string? | Last recorded tunnel error |
| `ReconnectAttempts` | int | Consecutive reconnect attempts since the last successful connection |
| `LatencyMs` | int? | Round-trip latency from the last successful ping/pong |
| `CapabilityManifest` | object | Current handshake capability manifest |

---

#### VoyageProgress

Progress information for an active voyage, nested in ArmadaStatus.

| Field | Type | Description |
|---|---|---|
| `Voyage` | [Voyage](#voyage) | Voyage details |
| `TotalMissions` | int | Total missions in voyage |
| `CompletedMissions` | int | Number of completed missions |
| `FailedMissions` | int | Number of failed missions |
| `InProgressMissions` | int | Number of in-progress missions |

---

#### Dock

A git worktree provisioned for a captain. Docks are managed internally by the Admiral and are not directly created/deleted via API.

| Field | Type | Default | Description |
|---|---|---|---|
| `Id` | string | auto-generated | Unique ID with `dck_` prefix |
| `VesselId` | string | `""` | Vessel ID |
| `CaptainId` | string? | null | Captain currently using dock |
| `WorktreePath` | string? | null | Local filesystem path to worktree |
| `BranchName` | string? | null | Branch name checked out |
| `Active` | bool | true | Whether dock is active/usable |
| `CreatedUtc` | datetime | now | Creation timestamp (UTC) |
| `LastUpdateUtc` | datetime | now | Last update timestamp (UTC) |

---

#### Harbor

A registered host-side runner (see the [Harbors](#harbors) endpoints). Only `Name`, `MaxConcurrentJobs`, and `Enabled` are operator-editable; the remaining runtime fields are reported by the link and preserved server-side.

```json
{
  "id": "hbr_abc123",
  "tenantId": "default",
  "userId": "default",
  "name": "workstation-01",
  "capabilities": [
    { "name": "claude", "available": true, "detail": "claude-code 1.0" },
    { "name": "git", "available": true, "detail": null }
  ],
  "connectionStatus": "Connected",
  "maxConcurrentJobs": 4,
  "enabled": true,
  "protocolVersion": "1.0",
  "osPlatform": "Windows",
  "architecture": "X64",
  "lastSeenUtc": "2026-03-07T12:00:00Z",
  "lastConnectedUtc": "2026-03-07T11:30:00Z",
  "createdUtc": "2026-03-07T11:00:00Z",
  "lastUpdateUtc": "2026-03-07T12:00:00Z"
}
```

| Field | Type | Default | Description |
|---|---|---|---|
| `id` | string | auto-generated | Unique ID with `hbr_` prefix |
| `tenantId` | string? | null | Owning tenant ID |
| `userId` | string? | null | Owning user ID |
| `name` | string | `"New Harbor"` | Human-facing Harbor name (operator-editable) |
| `capabilities` | array | [] | Advertised [HarborCapability](#harborcapability) entries (runtimes and host tools) |
| `connectionStatus` | [HarborConnectionStatusEnum](#harborconnectionstatusenum) | `Unknown` | Connection state as seen by the Admiral |
| `maxConcurrentJobs` | int | 4 | Maximum concurrent jobs the Harbor accepts (clamped to a minimum of 1, operator-editable) |
| `enabled` | bool | true | Whether the Harbor is enabled for routing (operator-editable). A disabled Harbor keeps its docks but receives no new missions. |
| `protocolVersion` | string? | null | Protocol version reported at handshake |
| `osPlatform` | string? | null | Operating-system platform reported at handshake (e.g. `Windows`, `Linux`, `macOS`) |
| `architecture` | string? | null | Processor architecture reported at handshake (e.g. `X64`, `Arm64`) |
| `lastSeenUtc` | datetime? | null | Last heartbeat or message timestamp (UTC) |
| `lastConnectedUtc` | datetime? | null | Last link-establishment timestamp (UTC) |
| `createdUtc` | datetime | now | Creation timestamp (UTC) |
| `lastUpdateUtc` | datetime | now | Last update timestamp (UTC) |

---

#### HarborCapability

One capability a Harbor advertises at handshake.

| Field | Type | Description |
|---|---|---|
| `name` | string | Capability name (e.g. a runtime like `claude` or a host tool like `git`) |
| `available` | bool | Whether the capability is currently available on the host |
| `detail` | string? | Optional human-readable detail (e.g. a version string), or null |

---

#### Playbook

A reusable tenant-scoped markdown document selected during dispatch.

| Field | Type | Description |
|---|---|---|
| `Id` | string | Playbook ID (prefix `pbk_`) |
| `TenantId` | string \| null | Owning tenant |
| `UserId` | string \| null | Owning user |
| `FileName` | string | Markdown file name, typically ending in `.md` |
| `Description` | string \| null | Human-readable description |
| `Content` | string | Markdown body |
| `Active` | bool | Whether the playbook is available for new selections |
| `CreatedUtc` | datetime | Creation timestamp |
| `LastUpdateUtc` | datetime | Last update timestamp |

---

#### SelectedPlaybook

Playbook selection metadata stored on a voyage or mission request.

| Field | Type | Description |
|---|---|---|
| `PlaybookId` | string | Selected playbook ID |
| `DeliveryMode` | [PlaybookDeliveryModeEnum](#playbookdeliverymodeenum) | How the playbook is delivered to the model |

---

#### MissionPlaybookSnapshot

Immutable mission-time snapshot of a selected playbook.

| Field | Type | Description |
|---|---|---|
| `PlaybookId` | string \| null | Source playbook ID |
| `FileName` | string | Source file name |
| `Description` | string \| null | Source description |
| `Content` | string | Frozen markdown body used for this mission |
| `DeliveryMode` | [PlaybookDeliveryModeEnum](#playbookdeliverymodeenum) | Resolved delivery mode |
| `ResolvedPath` | string \| null | Absolute runtime path when the playbook is materialized as a file |
| `WorktreeRelativePath` | string \| null | Relative dock path when attached into the worktree |
| `SourceLastUpdateUtc` | datetime | Source playbook update timestamp captured into the snapshot |

---

#### ModelEndpoint

A managed reference to an external embedding or inference model behind a provider API.

```json
{
  "Id": "mep_abc123",
  "TenantId": "default",
  "UserId": "default",
  "Name": "Primary embeddings",
  "Kind": "Embedding",
  "Provider": "OpenAI",
  "BaseUrl": "https://api.openai.com/v1",
  "Model": "text-embedding-3-small",
  "Dimensionality": 1536,
  "TimeoutMs": 120000,
  "Enabled": true,
  "HasApiKey": true,
  "HealthStatus": "Healthy",
  "LastHealthCheckUtc": "2026-03-07T12:00:00Z",
  "LastHealthError": null,
  "LastLatencyMs": 84,
  "CreatedUtc": "2026-03-07T12:00:00Z",
  "LastUpdateUtc": "2026-03-07T12:00:00Z"
}
```

| Field | Type | Default | Description |
|---|---|---|---|
| `Id` | string | auto-generated | Unique ID with `mep_` prefix |
| `TenantId` | string? | null | Owning tenant ID |
| `UserId` | string? | null | Owning user ID |
| `Name` | string | required | Display name |
| `Kind` | string | `Embedding` | `Embedding` or `Inference` |
| `Provider` | string | `Ollama` | `Ollama`, `OpenAI`, `OpenAICompatible`, `Anthropic`, `Gemini`, or `VoyageAI` |
| `BaseUrl` | string | required | Provider API base URL |
| `Model` | string? | null | Model name to target |
| `Dimensionality` | int | 0 | Embedding dimensionality |
| `TimeoutMs` | int | 120000 | Request timeout in milliseconds (clamped to [1000, 600000]) |
| `Enabled` | bool | true | Whether the endpoint participates in health sweeps |
| `ApiKey` | string | -- | Write-only input. Accepted on create/update; never returned on reads. |
| `HasApiKey` | bool | false | Read-only. Indicates whether a provider key is stored. |
| `HealthStatus` | string | `Unknown` | `Unknown`, `Healthy`, or `Unhealthy` |
| `LastHealthCheckUtc` | datetime? | null | Timestamp of the last probe (UTC) |
| `LastHealthError` | string? | null | Error text from the last failed probe |
| `LastLatencyMs` | int? | null | Latency of the last probe in milliseconds |
| `HealthHistory` | array | [] | Rolling series of recent probes (oldest first), each `{ TimestampUtc, Success }`, capped at 500 records. Drives the dashboard health-history bar. |
| `UptimePercentage` | double | 0 | Read-only. Percentage of retained probes that succeeded (0-100), derived from `HealthHistory`. |
| `ConsecutiveSuccesses` | int | 0 | Read-only. Trailing run of successful probes, derived from `HealthHistory`. |
| `ConsecutiveFailures` | int | 0 | Read-only. Trailing run of failed probes, derived from `HealthHistory`. |
| `FirstHealthCheckUtc` | datetime? | null | Read-only. Timestamp of the earliest retained probe, derived from `HealthHistory`. |
| `LastHealthyUtc` | datetime? | null | Read-only. Timestamp of the most recent successful probe, derived from `HealthHistory`. |
| `LastUnhealthyUtc` | datetime? | null | Read-only. Timestamp of the most recent failed probe, derived from `HealthHistory`. |
| `CreatedUtc` | datetime | now | Creation timestamp (UTC) |
| `LastUpdateUtc` | datetime | now | Last update timestamp (UTC) |

The `Anthropic` provider cannot be paired with `Kind` `Embedding`, and the `VoyageAI` provider cannot be paired with `Kind` `Inference`; both combinations are rejected with `400 Bad Request`.

Each health probe (from the base-URL-deduplicated background sweep or from `/validate`) appends a `{ TimestampUtc, Success }` record to `HealthHistory`; the derived fields above are computed from that series.

---

### Enumerations

All enumerations serialize as strings in JSON (e.g., `"InProgress"`, not `2`).

#### MissionStatusEnum

| Value | Description |
|---|---|
| `Pending` | Created but not yet assigned to a captain |
| `Assigned` | Assigned to a captain, awaiting work start |
| `InProgress` | Captain is actively working |
| `WorkProduced` | Agent exited successfully; work ready for landing |
| `PullRequestOpen` | Pull request created, awaiting merge confirmation |
| `Testing` | Work complete, under automated testing |
| `Review` | Awaiting human review |
| `Complete` | Successfully completed — code landed (terminal) |
| `Failed` | Mission failed (terminal) |
| `LandingFailed` | Landing (merge/PR) failed; may be retried |
| `Cancelled` | Mission cancelled (terminal) |

---

#### MissionModeEnum

| Value | Description |
|---|---|
| `Implementation` | Standard write mission: the captain changes the repository and the result lands or holds for review. An empty diff is treated as a no-op failure. This is the default. |
| `Audit` | Read-only audit: the captain inspects the repository and reports findings without modifying files. The landing gate treats "no commit" as success. |
| `Research` | Read-only research: the captain investigates a question and reports its conclusions without modifying files. The landing gate treats "no commit" as success. |

---

#### LandingModeEnum

| Value | Description |
|---|---|
| `LocalMerge` | Merge branch into default branch locally and push |
| `PullRequest` | Create a pull request and poll for merge confirmation |
| `MergeQueue` | Enqueue the branch into Armada's merge queue |
| `None` | No automated landing; leave work on the branch |

---

#### BranchCleanupPolicyEnum

| Value | Description |
|---|---|
| `LocalOnly` | Delete the local branch after landing |
| `LocalAndRemote` | Delete both local and remote branches after landing |
| `None` | Do not delete branches after landing |

---

#### VoyageStatusEnum

| Value | Description |
|---|---|
| `Open` | Created, missions being set up |
| `InProgress` | Has active missions in progress |
| `Complete` | All missions completed |
| `Cancelled` | Voyage was cancelled |

---

#### PlaybookDeliveryModeEnum

| Value | Description |
|---|---|
| `InlineFullContent` | Include the entire markdown body directly in the rendered mission instructions |
| `InstructionWithReference` | Materialize the playbook outside the worktree and instruct the model to read the resolved path |
| `AttachIntoWorktree` | Materialize the playbook under the dock worktree and instruct the model to read it there |

---

#### CaptainStateEnum

| Value | Description |
|---|---|
| `Idle` | Available for assignment |
| `Working` | Actively working on a mission |
| `Stalled` | Process appears stalled (no heartbeat) |
| `Stopping` | In the process of stopping |

---

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

---

#### MergeStatusEnum

| Value | Description |
|---|---|
| `Queued` | Waiting to be picked up |
| `Testing` | Currently being tested |
| `Passed` | Tests passed, ready to land |
| `Failed` | Tests failed |
| `Landed` | Successfully merged into target branch |
| `Cancelled` | Removed from queue |

---

#### HarborConnectionStatusEnum

| Value | Description |
|---|---|
| `Unknown` | No link has been established yet, or the state is not yet known |
| `Connected` | The Harbor currently has a live link to the Admiral |
| `Degraded` | The link is present but impaired (e.g. missed heartbeats) |
| `Disconnected` | The Harbor is registered but has no live link |

---

#### EnumerationOrderEnum

| Value | Description |
|---|---|
| `CreatedAscending` | Sort by creation date, oldest first |
| `CreatedDescending` | Sort by creation date, newest first (default) |

---

### Request Types

#### EnumerationQuery

Query parameters for paginated enumeration. Used as the POST body for all `/enumerate` endpoints.

```json
{
  "PageNumber": 1,
  "PageSize": 25,
  "Order": "CreatedDescending",
  "CreatedAfter": "2026-03-01T00:00:00Z",
  "CreatedBefore": null,
  "Status": "InProgress",
  "FleetId": null,
  "VesselId": "vsl_abc123",
  "CaptainId": null,
  "VoyageId": null,
  "MissionId": null,
  "EventType": null,
  "SignalType": null,
  "ToCaptainId": null,
  "UnreadOnly": null
}
```

All fields are optional. Omitted fields use defaults. See [Pagination](#pagination) for full details.

---

#### VoyageRequest

Request body for creating a voyage with missions.

```json
{
  "Title": "API Hardening",
  "Description": "Security improvements",
  "VesselId": "vsl_abc123",
  "SelectedPlaybooks": [
    {"PlaybookId": "pbk_abc123", "DeliveryMode": "InlineFullContent"}
  ],
  "Pipeline": "FullPipeline",
  "Missions": [
    {"Title": "Add rate limiting", "Description": "Add rate limiting middleware"},
    {"Title": "Add input validation", "Description": "Validate all POST endpoints"}
  ]
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `Title` | string | yes | Voyage title |
| `Description` | string | no | Voyage description |
| `VesselId` | string | yes | Target vessel ID |
| `Missions` | array | no | List of MissionRequest objects |
| `SelectedPlaybooks` | array | no | Ordered [SelectedPlaybook](#selectedplaybook) rows to apply to the voyage |
| `PipelineId` | string | no | Pipeline ID override |
| `Pipeline` | string | no | Pipeline name override |

---

#### MissionRequest

A mission within a VoyageRequest.

| Field | Type | Required | Description |
|---|---|---|---|
| `Title` | string | yes | Mission title |
| `Description` | string | no | Mission description/instructions |

---

#### StatusTransitionRequest

Request body for transitioning a mission status.

```json
{
  "Status": "InProgress"
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `Status` | string | yes | Target status name (case-insensitive) |

---

### Response Wrappers

#### EnumerationResult\<T\>

Paginated result wrapper returned by all list and enumerate endpoints.

```json
{
  "Success": true,
  "PageNumber": 1,
  "PageSize": 25,
  "TotalPages": 4,
  "TotalRecords": 87,
  "Objects": [ ... ],
  "TotalMs": 3.14
}
```

| Field | Type | Description |
|---|---|---|
| `Success` | bool | Whether the operation succeeded |
| `PageNumber` | int | Current page number (1-based) |
| `PageSize` | int | Number of items per page |
| `TotalPages` | int | Total number of pages |
| `TotalRecords` | long | Total records matching the query |
| `Objects` | array\<T\> | Result objects for this page |
| `TotalMs` | double | Query execution time in milliseconds |

---

#### VoyageDetail

Response from `GET /api/v1/voyages/{id}`.

```json
{
  "Voyage": { ... },
  "Missions": [ ... ]
}
```

| Field | Type | Description |
|---|---|---|
| `Voyage` | [Voyage](#voyage) | Voyage details |
| `Missions` | array\<[Mission](#mission)\> | All missions in this voyage |

---

#### MissionDiff

Response from `GET /api/v1/missions/{id}/diff`.

```json
{
  "MissionId": "msn_abc123",
  "Branch": "armada/msn_abc123",
  "Diff": "diff --git ..."
}
```

| Field | Type | Description |
|---|---|---|
| `MissionId` | string | Mission ID |
| `Branch` | string | Branch name |
| `Diff` | string | Git diff output |

---

#### MissionLog

Response from `GET /api/v1/missions/{id}/log`.

```json
{
  "MissionId": "msn_abc123",
  "Log": "line1\nline2\n...",
  "Lines": 100,
  "TotalLines": 542
}
```

| Field | Type | Description |
|---|---|---|
| `MissionId` | string | Mission ID |
| `Log` | string | Log content (newline-delimited) |
| `Lines` | integer | Number of lines returned |
| `TotalLines` | integer | Total lines in log file |

---

#### CaptainLog

Response from `GET /api/v1/captains/{id}/log`.

```json
{
  "CaptainId": "cpt_abc123",
  "Log": "line1\nline2\n...",
  "Lines": 100,
  "TotalLines": 203
}
```

| Field | Type | Description |
|---|---|---|
| `CaptainId` | string | Captain ID |
| `Log` | string | Log content (newline-delimited) |
| `Lines` | integer | Number of lines returned |
| `TotalLines` | integer | Total lines in log file |

---

#### ModelEndpointProbeResult

Result of `POST /api/v1/model-endpoints/{id}/validate`. Describes the outcome of a single real request against the provider.

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

| Field | Type | Description |
|---|---|---|
| `Success` | bool | Whether the probe request succeeded |
| `BaseUrl` | string \| null | Base URL that was probed |
| `LatencyMs` | int | Round-trip latency in milliseconds |
| `StatusCode` | int \| null | HTTP status code returned by the provider, when available |
| `Error` | string \| null | Error text when the probe failed |
| `EmbeddingDimensions` | int \| null | Dimensionality returned by an embedding probe |
| `SampleText` | string \| null | Sample completion text returned by an inference probe |
| `TimestampUtc` | datetime | When the probe ran (UTC) |

---

#### ModelEndpointHealthSweepResponse

Result of `POST /api/v1/model-endpoints/health-check`.

```json
{
  "DistinctBaseUrlsProbed": 3
}
```

| Field | Type | Description |
|---|---|---|
| `DistinctBaseUrlsProbed` | int | Number of distinct base URLs probed during the sweep |

---

## Quick Endpoint Summary

This table is a quick route index, not the canonical exhaustive contract. Use `/openapi.json` or `/swagger` for the live complete REST surface, including Workspace, planning-session, request-history, runtime-helper, and newer system routes.

| # | Method | URL | Description | Auth |
|---|---|---|---|---|
| 1 | POST | `/api/v1/authenticate` | Authenticate (get session token) | No |
| 2 | GET | `/api/v1/whoami` | Get current identity | Yes |
| 3 | POST | `/api/v1/tenants/lookup` | Lookup tenants by email | No |
| 4 | POST | `/api/v1/onboarding` | Self-register new user | No* |
| 5 | GET | `/api/v1/tenants` | List tenants (paginated) | Admin |
| 6 | POST | `/api/v1/tenants/enumerate` | Enumerate tenants | Admin |
| 7 | POST | `/api/v1/tenants` | Create tenant | Admin |
| 8 | GET | `/api/v1/tenants/{id}` | Get tenant | Yes** |
| 9 | PUT | `/api/v1/tenants/{id}` | Update tenant | Admin |
| 10 | DELETE | `/api/v1/tenants/{id}` | Delete tenant | Admin |
| 11 | GET | `/api/v1/users` | List users (paginated) | Admin |
| 12 | POST | `/api/v1/users/enumerate` | Enumerate users | Admin |
| 13 | POST | `/api/v1/users` | Create user | Admin |
| 14 | GET | `/api/v1/users/{id}` | Get user | Yes** |
| 15 | PUT | `/api/v1/users/{id}` | Update user | Admin |
| 16 | DELETE | `/api/v1/users/{id}` | Delete user | Admin |
| 17 | GET | `/api/v1/credentials` | List credentials (paginated) | Yes** |
| 18 | POST | `/api/v1/credentials/enumerate` | Enumerate credentials | Yes** |
| 19 | POST | `/api/v1/credentials` | Create credential | Yes** |
| 20 | GET | `/api/v1/credentials/{id}` | Get credential | Yes** |
| 21 | PUT | `/api/v1/credentials/{id}` | Update credential | Yes** |
| 22 | DELETE | `/api/v1/credentials/{id}` | Delete credential | Yes** |
| 23 | GET | `/api/v1/status` | System status dashboard | Yes |
| 24 | GET | `/api/v1/status/health` | Health check | No |
| 25 | POST | `/api/v1/server/stop` | Graceful shutdown | \*\*\* |
| 26 | GET | `/api/v1/fleets` | List fleets (paginated) | Yes |
| 27 | POST | `/api/v1/fleets/enumerate` | Enumerate fleets | Yes |
| 28 | POST | `/api/v1/fleets` | Create fleet | Yes |
| 29 | GET | `/api/v1/fleets/{id}` | Get fleet | Yes |
| 30 | PUT | `/api/v1/fleets/{id}` | Update fleet | Yes |
| 31 | DELETE | `/api/v1/fleets/{id}` | Delete fleet | Yes |
| 32 | GET | `/api/v1/vessels` | List vessels (paginated) | Yes |
| 33 | POST | `/api/v1/vessels/enumerate` | Enumerate vessels | Yes |
| 34 | POST | `/api/v1/vessels` | Create vessel | Yes |
| 35 | GET | `/api/v1/vessels/{id}` | Get vessel | Yes |
| 36 | PUT | `/api/v1/vessels/{id}` | Update vessel | Yes |
| 37 | DELETE | `/api/v1/vessels/{id}` | Delete vessel | Yes |
| 38 | GET | `/api/v1/voyages` | List voyages (paginated) | Yes |
| 39 | POST | `/api/v1/voyages/enumerate` | Enumerate voyages | Yes |
| 40 | POST | `/api/v1/voyages` | Create voyage with missions | Yes |
| 41 | GET | `/api/v1/voyages/{id}` | Get voyage with missions | Yes |
| 42 | DELETE | `/api/v1/voyages/{id}` | Cancel voyage | Yes |
| 43 | DELETE | `/api/v1/voyages/{id}/purge` | Permanently delete voyage | Yes |
| 44 | GET | `/api/v1/missions` | List missions (paginated) | Yes |
| 45 | POST | `/api/v1/missions/enumerate` | Enumerate missions | Yes |
| 46 | POST | `/api/v1/missions` | Create mission | Yes |
| 47 | GET | `/api/v1/missions/{id}` | Get mission | Yes |
| 48 | PUT | `/api/v1/missions/{id}` | Update mission | Yes |
| 49 | PUT | `/api/v1/missions/{id}/status` | Transition mission status | Yes |
| 50 | DELETE | `/api/v1/missions/{id}` | Cancel mission | Yes |
| 51 | POST | `/api/v1/missions/{id}/restart` | Restart failed/cancelled mission | Yes |
| 52 | GET | `/api/v1/missions/{id}/diff` | Get mission diff | Yes |
| 53 | GET | `/api/v1/missions/{id}/log` | Get mission log | Yes |
| 54 | GET | `/api/v1/captains` | List captains (paginated) | Yes |
| 55 | POST | `/api/v1/captains/enumerate` | Enumerate captains | Yes |
| 56 | POST | `/api/v1/captains` | Create captain | Yes |
| 57 | GET | `/api/v1/captains/{id}` | Get captain | Yes |
| 58 | PUT | `/api/v1/captains/{id}` | Update captain | Yes |
| 59 | POST | `/api/v1/captains/{id}/stop` | Stop captain | Yes |
| 60 | POST | `/api/v1/captains/stop-all` | Stop all captains | Yes |
| 61 | GET | `/api/v1/captains/{id}/log` | Get captain current log | Yes |
| 62 | DELETE | `/api/v1/captains/{id}` | Delete captain | Yes |
| 63 | GET | `/api/v1/signals` | List signals (paginated) | Yes |
| 64 | POST | `/api/v1/signals/enumerate` | Enumerate signals | Yes |
| 65 | POST | `/api/v1/signals` | Send signal | Yes |
| 66 | GET | `/api/v1/events` | List events (paginated) | Yes |
| 67 | POST | `/api/v1/events/enumerate` | Enumerate events | Yes |
| 68 | GET | `/api/v1/merge-queue` | List merge queue (paginated) | Yes |
| 69 | POST | `/api/v1/merge-queue/enumerate` | Enumerate merge queue | Yes |
| 70 | POST | `/api/v1/merge-queue` | Enqueue branch | Yes |
| 71 | GET | `/api/v1/merge-queue/{id}` | Get merge entry | Yes |
| 72 | DELETE | `/api/v1/merge-queue/{id}` | Cancel merge entry | Yes |
| 73 | POST | `/api/v1/merge-queue/process` | Process merge queue | Yes |

\* Gated by `AllowSelfRegistration` setting.
\*\* Non-admin users are scoped to their own records only.
\*\*\* NoAuthRequired by default; requires global admin (`IsAdmin = true`) when `RequireAuthForShutdown` is `true`.

---

## Additional Ports

| Service | Default Port | Description |
|---|---|---|
| Admiral REST API | 7890 | This API (WebSocket available at /ws on the same port) |
| MCP Server | 7891 | Model Context Protocol (Voltaic) for AI tool use |

## CORS

All responses include permissive CORS headers:
```
Access-Control-Allow-Origin: *
Access-Control-Allow-Methods: GET, POST, PUT, DELETE, OPTIONS
Access-Control-Allow-Headers: Content-Type, Authorization, X-Token, X-Api-Key
```
