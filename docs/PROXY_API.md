# Proxy API

**Version:** 1.0.0

`Armada.Proxy` is a portal and relay for the real Armada dashboard. It has no remote operations UI or feature API of its
own: the browser uses the normal Armada dashboard, and the proxy relays its traffic to a connected Armada instance.

The proxy's responsibilities are:

- browser authentication to the proxy
- connected-instance discovery and selection
- serving the shared React dashboard bundle at `/dashboard`
- relaying Armada REST traffic at `/api/v1/*`
- relaying the dashboard websocket at `/ws`
- terminating the outbound Armada tunnel at `/tunnel`
- enforcing explicit remote-access policy for blocked routes

## Default Bind

By default, the proxy binds to:

- host: `localhost`
- port: `7893`
- portal: `http://localhost:7893/`
- shared dashboard: `http://localhost:7893/dashboard`
- health: `http://localhost:7893/proxy-api/v1/status/health`
- tunnel: `ws://localhost:7893/tunnel`

Configuration is read from the first file found among: the path passed on the command line, `ARMADA_PROXY_SETTINGS_FILE`,
`proxysettings.json` then `appsettings.json` next to the executable and in the current directory, and
`~/.armada/proxysettings.json`. Keys may sit at the root of the file or under an `ArmadaProxy` section:

```json
{
  "ArmadaProxy": {
    "dataDirectory": "/app/data",
    "logDirectory": "/app/data/logs",
    "hostname": "localhost",
    "port": 7893,
    "requireEnrollmentToken": false,
    "enrollmentTokens": [],
    "password": "replace-with-a-strong-shared-secret",
    "handshakeTimeoutSeconds": 15,
    "staleAfterSeconds": 90,
    "requestTimeoutSeconds": 20,
    "maxRecentEvents": 50
  }
}
```

| Setting | Default | Range |
| --- | --- | --- |
| `dataDirectory` | `~/.armada` | |
| `logDirectory` | `<dataDirectory>/logs` | |
| `hostname` | `localhost` | |
| `port` | `7893` | 1-65535 |
| `requireEnrollmentToken` | `false` | When true, a tunnel handshake must present one of `enrollmentTokens` |
| `handshakeTimeoutSeconds` | `15` | 1-300 (browser login challenges live at least 30 seconds) |
| `staleAfterSeconds` | `90` | 5-86400; a connected instance with no traffic for this long is reported `stale` |
| `requestTimeoutSeconds` | `20` | 1-300; relayed request timeout |
| `maxRecentEvents` | `50` | 1-500; recent tunnel events kept per instance |
| `syslogServers` | none | Optional syslog targets for proxy logging |

Security settings (security review O-11):

| Setting | Default | Meaning |
| --- | --- | --- |
| `password` (env `ARMADA_PROXY_PASSWORD`, which wins over the file) | none | Shared secret for browser login and the tunnel handshake. The proxy refuses to start while it is blank or the built-in default `armadaadmin`. Use the same value as `remoteControl.password` on each Armada instance. |
| `allowDefaultPassword` (env `ARMADA_PROXY_ALLOW_DEFAULT_PASSWORD`) | `false` | Start anyway with the default password. For a throwaway local test only; the proxy logs a warning. |
| `loginMaxFailures` | `10` | Failed browser logins or tunnel handshakes from one client address, within the window, that trigger a lockout (1-1000). |
| `loginFailureWindowSeconds` | `900` | Window over which failures are counted (10-86400). |
| `loginLockoutSeconds` | `900` | Lockout length; logins and handshakes from that address get `429` with `Retry-After` until it ends (1-86400). |
| `trustForwardedHeaders` | `false` | Use `X-Forwarded-For` / `Forwarded` for the client address (rate limiting and the requester address relayed to Armada) and `X-Forwarded-Proto` for the cookie `Secure` flag. Enable only behind a reverse proxy that overwrites these headers. |
| `secureCookie` | `false` | Always mark the session cookie `Secure`. Enable when the proxy is served over HTTPS. |

The Docker compose file (`docker/proxy/compose.yaml`) requires `ARMADA_PROXY_PASSWORD` to be set before it starts.

## Browser Model

The remote browser flow is:

1. Open `/`.
2. Request a login challenge from `/proxy-api/v1/auth/challenge`.
3. Submit a SHA-256 proof to `/proxy-api/v1/auth/login`.
4. Receive an authenticated proxy session.
5. List connected deployments from `/proxy-api/v1/instances`.
6. Store the selected deployment with `/proxy-api/v1/session/instance`.
7. Open `/dashboard`.
8. Use the normal Armada dashboard against same-origin `/api/v1/*` and `/ws`.

The proxy session is separate from the Armada application session. After opening `/dashboard`, the user may still need to sign into the selected Armada deployment.

## Authentication And Session Handling

The proxy browser session is primarily cookie-backed:

- cookie name: `armada_proxy_session`
- attributes: `Path=/; HttpOnly; SameSite=Lax; Max-Age=<session lifetime>`, plus `Secure` when `secureCookie` is on or when `trustForwardedHeaders`
  is on and the request arrived with `X-Forwarded-Proto: https`

For non-browser callers, the proxy still accepts `X-Armada-Proxy-Session`.

The browser never sends the raw shared password. It first requests a nonce and then submits a derived proof, the
lowercase hex SHA-256 of `proxy-browser-login:proxy:<nonce>:<sha256hex(password)>` (the password is trimmed and
`sha256hex` is lowercase hex). A challenge can be used once. Proxy browser sessions last 24 hours.

### `GET /proxy-api/v1/auth/challenge`

Returns a one-time login challenge:

```json
{
  "nonce": "4f3a0c7a8f6c49d9b6711d2c1a7b5e90",
  "expiresUtc": "2026-05-16T18:30:00Z"
}
```

### `POST /proxy-api/v1/auth/login`

Request:

```json
{
  "nonce": "4f3a0c7a8f6c49d9b6711d2c1a7b5e90",
  "proofSha256": "8f5c4e1e1d7b5d8b2f6c6c987bfb76f5d55a75b8b940f882c817d39de42d83cc"
}
```

Response:

```json
{
  "token": "0f34455311b54e719f50927df5ecdfd798f8f27ed4ae45e2a73c0c3b2d194f73",
  "expiresUtc": "2026-05-17T18:30:00Z",
  "selectedInstanceId": null
}
```

The response body still includes the token for compatibility, but browser callers normally rely on the `Set-Cookie` header instead.

A wrong proof returns `401`. After `loginMaxFailures` failures from one client address within
`loginFailureWindowSeconds`, every login from that address (even a correct one) returns `429 Too Many Requests` with a
`Retry-After` header (seconds) until `loginLockoutSeconds` pass. Failed tunnel handshakes count against the same limit;
a locked-out tunnel client receives a handshake response with status `429` and error code `too_many_attempts`.

A missing nonce or proof, an unknown, used, or expired challenge, or a wrong proof returns `401` with `{ "error": "..." }`;
a body that is not a JSON object returns `400`.

### `POST /proxy-api/v1/auth/logout`

Invalidates the current proxy browser session, if any, and clears the session cookie. Returns `{ "success": true }`.

### `GET /proxy-api/v1/status/health`

```json
{
  "product": "Armada.Proxy",
  "version": "1.0.0",
  "status": "ok",
  "startUtc": "2026-05-16T18:00:00Z",
  "uptimeSeconds": 1800,
  "connectedInstances": 1,
  "staleInstances": 0,
  "instanceCount": 1
}
```

### `GET /proxy-api/v1/instances`

Requires a proxy session (`401` otherwise). Returns `{ "count": 1, "instances": [ ... ] }`, where each instance is:

```json
{
  "instanceId": "armada-1f2e3d4c5b6a",
  "state": "connected",
  "armadaVersion": "1.0.0",
  "protocolVersion": "2026-04-04",
  "capabilities": ["dashboard.http.relay", "dashboard.websocket.relay"],
  "remoteAddress": "203.0.113.10",
  "firstSeenUtc": "2026-05-16T18:00:00Z",
  "connectedUtc": "2026-05-16T18:00:00Z",
  "lastSeenUtc": "2026-05-16T18:29:50Z",
  "lastEventUtc": "2026-05-16T18:29:50Z",
  "lastDisconnectUtc": null,
  "lastError": null,
  "recentEventCount": 12,
  "pendingRequestCount": 0
}
```

`state` is `connected`, `stale`, or `offline`.

### `POST /proxy-api/v1/session/instance`

Request: `{ "instanceId": "armada-1f2e3d4c5b6a" }`. Returns the session context (below). Errors: `400` when the body is
not a JSON object or `instanceId` is missing, `404` for an unknown instance, `409` when the instance cannot be selected
(not connected, or it does not advertise the dashboard relay capabilities), `401` without a proxy session.

### `POST /proxy-api/v1/session/logout-instance`

Clears the selected deployment and returns the session context. `401` without a proxy session.

## Proxy-Local Routes

These routes belong to the proxy itself and are never relayed to Armada:

| Route | Auth | Purpose |
| --- | --- | --- |
| `GET /` | no | Minimal login-and-selection portal |
| `GET /app.css`, `GET /app.js`, `GET /img/logo-dark-grey.png`, `GET /img/logo-light-grey.png`, `GET /img/logo.ico` | no | Portal assets |
| `GET /proxy-api/v1/status/health` | no | Proxy process health and instance counts |
| `GET /proxy-api/v1/auth/challenge` | no | Browser login challenge |
| `POST /proxy-api/v1/auth/login` | no | Browser login |
| `POST /proxy-api/v1/auth/logout` | no (clears the session when present) | Proxy logout |
| `GET /proxy-api/v1/instances` | yes (`401` without a proxy session) | Connected deployment summaries |
| `GET /proxy-api/v1/session/context` | yes | Current proxy session and selected deployment metadata |
| `POST /proxy-api/v1/session/instance` | yes | Set selected deployment |
| `POST /proxy-api/v1/session/logout-instance` | yes | Clear selected deployment |
| `GET /dashboard` and `GET /dashboard/*` | yes + selected instance | Shared React dashboard bundle |
| `WS /tunnel` | instance auth | Armada outbound tunnel |

`GET /proxy-api/v1/session/context` (`401` without a proxy session) returns the selected deployment summary (the same
shape as an `instances` entry, abbreviated below) plus relay capability flags. `relay.dashboard` and `relay.api` are true
when the instance advertises `dashboard.http.relay`, and `relay.websocket` when it advertises `dashboard.websocket.relay`:

```json
{
  "isAuthenticated": true,
  "expiresUtc": "2026-05-17T18:30:00Z",
  "selectedInstanceId": "armada-1f2e3d4c5b6a",
  "selectedInstance": {
    "instanceId": "armada-1f2e3d4c5b6a",
    "state": "connected"
  },
  "relay": {
    "dashboard": true,
    "api": true,
    "websocket": true
  }
}
```

## Relayed Dashboard Routes

Once a deployment is selected, the proxy exposes the same origin shape expected by `Armada.Dashboard`:

| Route | Purpose |
| --- | --- |
| `ALL /api/v1/*` | Relay to the selected Armada deployment over the tunnel |
| `WS /ws` | Relay the dashboard websocket to the selected Armada deployment |

Behavior when session state is missing:

- `/dashboard*` without a proxy session or selected deployment redirects to `/`
- `/api/v1/*` without a proxy session returns `401`
- `/api/v1/*` without a selected deployment returns `409`
- `/api/v1/*` when the selected deployment is no longer connected, or does not advertise `dashboard.http.relay`,
  returns `409`
- `/ws` requires both an authenticated proxy session and a selected deployment
- a request path that is not in canonical form returns `400`
- a relayed request body larger than 8 MiB returns `413`
- a relay that times out (`requestTimeoutSeconds`) returns `504`; an instance that disconnected returns `503`
- any other path returns `404` with `{ "error": "Not found" }`

The dashboard bundle served from `/dashboard` is the same built output from `src/Armada.Dashboard/dist`, plus the shared `i18n/armada.json` catalog.

## Relay Policy

The relay transport is generic, but remote policy is still explicit.

### Always blocked

These paths are blocked for every method:

- `/api/v1/server/stop`
- `/api/v1/server/reset`
- `/api/v1/restore`
- `/api/v1/status/shutdown` and `/api/v1/status/factory-reset` (legacy paths the Admiral no longer serves; still
  blocked defensively)

### Write-blocked administrative families

Non-`GET` and non-`HEAD` requests are blocked for:

- `/api/v1/settings*`
- `/api/v1/tenants*`
- `/api/v1/users*`
- `/api/v1/credentials*`

`POST /api/v1/tenants/lookup` (and `POST /api/v1/authenticate`) stay allowed so users can sign in to the selected
deployment through the proxy.

Blocked requests return `403` with an explicit policy message. The transport does not silently fall back to a proxy-specific workaround.

### Still allowed

- normal dashboard reads across `/api/v1/*`
- normal dashboard writes outside the blocked families
- file downloads such as `GET /api/v1/backup`
- websocket traffic at `/ws`

## Tunnel Compatibility Notes

The proxy relays dashboard traffic with the generic methods:

- `armada.http.request`
- `armada.ws.open`
- `armada.ws.message`
- `armada.ws.close`

The Admiral serves only these generic methods; the older feature-specific tunnel methods were removed and now return `404 unsupported_method` (see [TUNNEL_PROTOCOL.md](TUNNEL_PROTOCOL.md#unsupported-methods)).
