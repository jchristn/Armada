# Armada REST API Reference

**Version:** 0.4.0
**Base URL:** `http://localhost:7890`
**Content-Type:** `application/json`

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
  - [Voyages](#voyages)
  - [Missions](#missions)
  - [Captains](#captains)
  - [Signals](#signals)
  - [Events](#events)
  - [Docks](#docks)
  - [Merge Queue](#merge-queue)
  - [Prompt Templates](#prompt-templates)
  - [Personas](#personas)
  - [Pipelines](#pipelines)
  - [Backup and Restore](#backup-and-restore)
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
| `/api/v1/fleets` | ALL | Authenticated | Tenant-scoped |
| `/api/v1/vessels` | ALL | Authenticated | Tenant-scoped |
| `/api/v1/captains` | ALL | Authenticated | Tenant-scoped |
| `/api/v1/missions` | ALL | Authenticated | Tenant-scoped |
| `/api/v1/voyages` | ALL | Authenticated | Tenant-scoped |
| `/api/v1/docks` | ALL | Authenticated | Tenant-scoped |
| `/api/v1/signals` | ALL | Authenticated | Tenant-scoped |
| `/api/v1/events` | ALL | Authenticated | Tenant-scoped |
| `/api/v1/merge-queue` | ALL | Authenticated | Tenant-scoped |
| `/api/v1/prompt-templates` | ALL | Authenticated | Tenant-scoped |
| `/api/v1/personas` | ALL | Authenticated | Tenant-scoped |
| `/api/v1/pipelines` | ALL | Authenticated | Tenant-scoped |
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
  "Version": "0.2.0",
  "Ports": {
    "Admiral": 7890,
    "Mcp": 7891,
    "WebSocket": 7892
  }
}
```

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

**Response:** `201 Created` - [Vessel](#vessel)

```bash
curl -X POST http://localhost:7890/api/v1/vessels \
  -H "Content-Type: application/json" \
  -d '{"Name": "MyRepo", "RepoUrl": "https://github.com/org/repo.git", "FleetId": "flt_abc123"}'
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

**Response:** `201 Created` - [Captain](#captain)

```bash
curl -X POST http://localhost:7890/api/v1/captains \
  -H "Content-Type: application/json" \
  -d '{"Name": "captain-1", "Runtime": "ClaudeCode", "SystemInstructions": "You are a testing specialist. Always run tests before committing."}'
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

#### PUT /api/v1/captains/{id}

Update a captain's name or runtime. Operational fields (state, process, mission) are preserved.

**Path Parameters:**
| Parameter | Description |
|---|---|
| `id` | Captain ID (`cpt_` prefix) |

**Request Body:**
```json
{
  "name": "captain-bravo",
  "runtime": "Codex",
  "systemInstructions": "Focus on code quality and always run linting before commits."
}
```

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

### Prompt Templates

Prompt templates define the instruction text used when generating captain mission briefs. Armada ships with built-in templates that can be customized. Custom templates can also be created per tenant.

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

### Backup and Restore

#### GET /api/v1/backup

Create and download a ZIP backup of the Armada database and settings.

**Response:** `200 OK` — Binary ZIP file stream with `Content-Disposition: attachment; filename="armada-backup-{timestamp}.zip"` header.

**ZIP Contents:**

| File | Description |
|---|---|
| `armada.db` | SQLite database snapshot created via the SQLite online backup API |
| `settings.json` | Current Armada server configuration |
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
| `LandingMode` | [LandingModeEnum](#landingmodeenum)? | null | Per-vessel landing policy override (null = use global setting) |
| `BranchCleanupPolicy` | [BranchCleanupPolicyEnum](#branchcleanuppolicyenum)? | null | Per-vessel branch cleanup policy override (null = use global setting) |
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
  "Priority": 100,
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
| `Priority` | int | 100 | Priority (lower number = higher priority) |
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
| `LastUpdateUtc` | datetime | now | Last update timestamp (UTC) |

---

#### Captain

A worker AI agent instance executing missions.

```json
{
  "Id": "cpt_abc123",
  "Name": "captain-1",
  "Runtime": "ClaudeCode",
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
| `TimestampUtc` | datetime | Snapshot timestamp (UTC) |

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

## Endpoint Summary

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
| Admiral REST API | 7890 | This API |
| MCP Server | 7891 | Model Context Protocol (Voltaic) for AI tool use |
| WebSocket Hub | 7892 | Real-time event streaming and command interface |

## CORS

All responses include permissive CORS headers:
```
Access-Control-Allow-Origin: *
Access-Control-Allow-Methods: GET, POST, PUT, DELETE, OPTIONS
Access-Control-Allow-Headers: Content-Type, Authorization, X-Token, X-Api-Key
```
