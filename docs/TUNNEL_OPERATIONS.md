# Tunnel Operations

**Version:** 1.0.0

This guide covers the remote-control tunnel and the `Armada.Proxy` service shipped in Armada 1.0.0.

For a step-by-step operator setup path, see [REMOTE_MGMT.md](REMOTE_MGMT.md).

---

## Scope

Armada 1.0.0 includes:

- the Armada-side outbound websocket tunnel client
- remote tunnel configuration in Armada settings and the dashboard's Server page
- `Armada.Proxy`, which terminates tunnels at `/tunnel` and keeps an in-memory instance registry
- a proxy portal at `/` with a password login, instance selection, and the shared dashboard
- generic relay of dashboard REST (`/api/v1/*`) and live updates (`/ws`) from the proxy into the selected instance

Still not included:

- user-facing SaaS auth
- delegated identity or local-session brokerage
- notification delivery
- persistent proxy storage
- chunked or streamed relay bodies (a relayed request or response body is capped at 8 MiB)

Treat the current proxy as an implementation-stage operator service, not a hardened public SaaS surface.

---

## Armada Instance Configuration

Armada stores remote tunnel configuration in `settings.json`:

```json
{
  "remoteControl": {
    "enabled": false,
    "tunnelUrl": "http://proxy.armadago.ai:7893/tunnel",
    "instanceId": null,
    "enrollmentToken": null,
    "password": "replace-with-a-strong-shared-secret",
    "connectTimeoutSeconds": 15,
    "heartbeatIntervalSeconds": 30,
    "reconnectBaseDelaySeconds": 5,
    "reconnectMaxDelaySeconds": 60,
    "allowInvalidCertificates": false
  }
}
```

### Recommendations

- Leave `enabled` off unless you are actively testing the tunnel.
- An empty or null `tunnelUrl` falls back to the default `http://proxy.armadago.ai:7893/tunnel`; set it to your own
  proxy. `http`/`https` URLs are converted to `ws`/`wss`, and a URL with no path gets `/tunnel`.
- Prefer `wss://` endpoints outside local development.
- Leave `instanceId` empty unless you want an operator-friendly override.
- Keep `remoteControl.password` aligned with `ArmadaProxy.password`. The built-in default (`armadaadmin`) is refused by
  the proxy unless it sets `allowDefaultPassword`, and the Admiral logs a warning while it is in use.
- Use `allowInvalidCertificates = true` only for local development with self-signed certificates.

---

## Proxy Configuration

`Armada.Proxy` reads configuration from `ArmadaProxy`:

```json
{
  "ArmadaProxy": {
    "dataDirectory": "/app/data",
    "logDirectory": "/app/data/logs",
    "hostname": "localhost",
    "port": 7893,
    "syslogServers": [
      {
        "hostname": "127.0.0.1",
        "port": 514
      }
    ],
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

### Key Fields

- `dataDirectory`
- `logDirectory`
- `hostname`
- `port`
- `syslogServers`
- `requireEnrollmentToken`
- `enrollmentTokens`
- `password` (required; `ARMADA_PROXY_PASSWORD` overrides it; the proxy refuses to start with the default `armadaadmin`)
- `allowDefaultPassword` (default false; local testing only)
- `loginMaxFailures`, `loginFailureWindowSeconds`, `loginLockoutSeconds` (defaults 10 / 900 / 900)
- `trustForwardedHeaders` (default false), `secureCookie` (default false)
- `handshakeTimeoutSeconds`
- `staleAfterSeconds`
- `requestTimeoutSeconds`
- `maxRecentEvents`

A tunnel that fails its handshake repeatedly (wrong password) locks its source address out for `loginLockoutSeconds`;
the Admiral then sees handshake responses with status 429 (`too_many_attempts`). Fix the password, then wait out the
lockout or restart the proxy (lockouts are in memory).

---

## Starting The Proxy

From the repo root:

```powershell
$env:ARMADA_PROXY_PASSWORD = "replace-with-a-strong-shared-secret"
dotnet run --project src/Armada.Proxy/Armada.Proxy.csproj --framework net10.0
```

Without a configured password the proxy exits with an error naming `ARMADA_PROXY_PASSWORD` and `allowDefaultPassword`.

Default endpoints:

- health: `http://localhost:7893/proxy-api/v1/status/health`
- instance list (requires a proxy session; 401 otherwise): `http://localhost:7893/proxy-api/v1/instances`
- remote shell: `http://localhost:7893/`
- tunnel websocket: `ws://localhost:7893/tunnel`

Point Armada at the proxy by setting:

```json
{
  "remoteControl": {
    "enabled": true,
    "tunnelUrl": "ws://localhost:7893/tunnel",
    "enrollmentToken": null,
    "password": "replace-with-a-strong-shared-secret"
  }
}
```

---

## Where To Inspect State

### On The Armada Instance

- Server dashboard -> `Server`
- Legacy dashboard -> `Server Settings`
- `GET /api/v1/status`
- `GET /api/v1/status/health`
- `GET /api/v1/settings`
- `armada status`

Key fields:

- `state`
- `tunnelUrl`
- `instanceId`
- `lastConnectAttemptUtc`
- `connectedUtc`
- `lastHeartbeatUtc`
- `lastDisconnectUtc`
- `lastError`
- `reconnectAttempts`
- `latencyMs`

### On The Proxy

- `GET /proxy-api/v1/status/health`
- `GET /proxy-api/v1/instances` (requires a proxy session)
- `GET /proxy-api/v1/session/context`
- `POST /proxy-api/v1/session/instance`
- `POST /proxy-api/v1/session/logout-instance`
- `ALL /api/v1/*` and the `/ws` WebSocket: relayed to the selected deployment over the tunnel (`401` without a proxy
  session, `409` without a selected deployment)

The older per-instance routes (`/api/v1/instances/{instanceId}/...`) were removed with the feature-specific tunnel
methods; see [PROXY_API.md](PROXY_API.md) and [TUNNEL_PROTOCOL.md](TUNNEL_PROTOCOL.md).

Proxy instance states:

- `connected`
- `stale`
- `offline`

---

## Common Failure Modes

### Armada pointed at the default proxy

Symptoms:

- Armada tunnel state cycles between `Connecting` and `Error` although you never set a tunnel URL

Fix:

- an empty `remoteControl.tunnelUrl` falls back to `http://proxy.armadago.ai:7893/tunnel`; set it to your proxy's
  `/tunnel` URL, or disable `remoteControl.enabled`

### Invalid tunnel scheme

Symptoms:

- Armada tunnel state becomes `Error`
- `lastError` says the URL must use `ws`, `wss`, `http`, or `https`

Fix:

- correct the URL scheme

### Proxy rejects handshake

Symptoms:

- websocket connects and then closes quickly
- the proxy logs a handshake rejection
- Armada eventually reports a disconnect/error cycle

Fix:

- verify `remoteControl.password` matches `ArmadaProxy.password` (a mismatch fails the password proof)
- verify the Admiral clock is close to the proxy clock (the password proof is timestamped)
- verify `ArmadaProxy.requireEnrollmentToken`
- verify the instance `remoteControl.enrollmentToken`
- verify the token exists in `ArmadaProxy.enrollmentTokens`

### TLS validation failure

Symptoms:

- Armada tunnel state becomes `Error`
- connection attempts keep retrying

Fix:

- use a valid server certificate
- for local-only development, temporarily enable `allowInvalidCertificates`

### Unreachable proxy

Symptoms:

- Armada cycles through `Connecting` -> `Error`
- `reconnectAttempts` increases
- proxy instance list never shows the Armada instance

Fix:

- verify DNS and network reachability
- verify the websocket endpoint path
- inspect firewall and egress rules

### Stale proxy instance

Symptoms:

- proxy instance state becomes `stale`
- detail endpoints still show the instance, but live activity has stopped

Fix:

- inspect Armada tunnel heartbeats
- inspect process/network sleep or captive-network interruptions
- verify `staleAfterSeconds` is appropriate for the environment

---

## Live Request Notes

The Admiral serves only the generic relay methods over the tunnel:

- `armada.http.request` (dashboard REST traffic for `/api/v1/*`)
- `armada.ws.open`, `armada.ws.message`, `armada.ws.close` (the dashboard `/ws` WebSocket)

The older feature-specific methods (`armada.instance.*`, `armada.fleets.list`, `armada.mission.create`, and the rest)
were removed; any other method returns `404` with error code `unsupported_method`. See
[TUNNEL_PROTOCOL.md](TUNNEL_PROTOCOL.md#unsupported-methods).
