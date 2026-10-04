# Remote Management

**Version:** `0.9.0`

This guide describes the current remote-access model for Armada:

- `Armada.Server` owns the product API, websocket, and dashboard behavior
- `Armada.Proxy` provides browser login, deployment selection, tunnel termination, and relay
- the remote user ultimately uses the same React dashboard served from the proxy at `/dashboard`

The proxy is no longer the primary product UI. It is the remote-access broker for the product UI.

## What Ships Now

The shipped remote-access path includes:

- outbound Armada-to-proxy websocket tunnel at `/tunnel`
- shared-password tunnel handshake with optional enrollment-token validation
- proxy browser login using a challenge/response proof
- deployment selection from currently connected Armada instances
- the shared React dashboard served from the proxy at `/dashboard`
- same-origin dashboard REST relay at `/api/v1/*`
- same-origin dashboard websocket relay at `/ws`
- explicit proxy-side route policy for blocked local-only actions

Not shipped in this cut:

- proxy relay for the MCP endpoint
- delegated identity or SSO between proxy auth and Armada auth
- resumable tunnel subscriptions or large-body streaming
- unrestricted remote access to every local-only admin route

## Default URLs

For a local Armada server and a local proxy:

- Armada dashboard: `http://localhost:7890/dashboard`
- Armada websocket: `ws://localhost:7890/ws`
- proxy portal: `http://localhost:7893/`
- proxy-served dashboard: `http://localhost:7893/dashboard`
- proxy health: `http://localhost:7893/proxy-api/v1/status/health`
- proxy tunnel endpoint: `ws://localhost:7893/tunnel`

## Configure The Proxy

Proxy settings live under `ArmadaProxy`:

```json
{
  "ArmadaProxy": {
    "hostname": "localhost",
    "port": 7893,
    "password": "replace-with-a-strong-shared-secret",
    "requireEnrollmentToken": false,
    "enrollmentTokens": [],
    "handshakeTimeoutSeconds": 15,
    "staleAfterSeconds": 90,
    "requestTimeoutSeconds": 20,
    "maxRecentEvents": 50
  }
}
```

Important settings:

- `password`: shared secret used by both the Armada tunnel handshake and proxy browser login. Required: the proxy
  refuses to start while it is blank or the built-in default `armadaadmin` unless `allowDefaultPassword` is true (local
  testing only). `ARMADA_PROXY_PASSWORD` overrides the file; the Docker compose file requires it. Set the same value as
  `remoteControl.password` on each Armada instance.
- `loginMaxFailures`, `loginFailureWindowSeconds`, `loginLockoutSeconds`: per-address lockout for failed logins and
  tunnel handshakes (defaults 10 failures in 900 seconds lock out for 900 seconds; locked-out callers get 429 with
  `Retry-After`)
- `trustForwardedHeaders` (default false) and `secureCookie` (default false): see [PROXY_API.md](PROXY_API.md)
- `requireEnrollmentToken` and `enrollmentTokens`: optional extra admission control for instances
- `staleAfterSeconds`: when a connected instance is treated as stale without activity
- `requestTimeoutSeconds`: timeout for live tunnel request/response relay calls

## Configure Armada

Armada outbound tunnel settings live under `remoteControl` in the Armada server settings:

```json
{
  "remoteControl": {
    "enabled": true,
    "tunnelUrl": "ws://localhost:7893/tunnel",
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

You can configure those values through:

- the Armada dashboard `Server` page
- `GET /api/v1/settings` and `PUT /api/v1/settings`
- local settings files

Notes:

- `http://` and `https://` tunnel URLs are normalized to websocket form automatically
- if the path is omitted, Armada normalizes it to `/tunnel`
- outside local development, prefer `wss://.../tunnel`
- the Armada `remoteControl.password` must match `ArmadaProxy.password`

## Bring The Tunnel Up

1. Start `Armada.Proxy`.
2. Start `Armada.Server`.
3. Enable the Armada remote tunnel and save settings.
4. Wait for the outbound Armada tunnel to connect to the proxy.

You can verify connection state from the Armada side through:

- `GET /api/v1/status`
- `GET /api/v1/status/health`
- the local Armada dashboard `Server` page

Healthy indicators include:

- `remoteTunnel.enabled = true`
- `remoteTunnel.state = Connected`
- `remoteTunnel.lastError` is empty
- `remoteTunnel.tunnelUrl` matches the proxy endpoint

You can verify the proxy side through:

- `GET /proxy-api/v1/status/health`
- `GET /proxy-api/v1/instances` after proxy login
- the portal deployment list at `/`

## Use The Remote Dashboard

The current remote browser flow is:

1. Open the proxy root URL.
2. Enter the proxy shared password.
3. Select a connected deployment.
4. Open the shared dashboard at `/dashboard`.
5. Sign into the selected Armada deployment inside that dashboard if required.
6. Use the dashboard normally through proxy-backed `/api/v1/*` and `/ws`.

This is intentionally a double-auth flow today:

- proxy auth proves access to the relay service
- Armada auth proves access to the selected deployment

That split is acceptable for the current cut and is explicitly surfaced in the portal and dashboard.

## Remote Policy

The proxy relays the real dashboard transport, but it still blocks selected local-only actions.

Always blocked:

- shutdown
- factory reset
- restore

Write-blocked administrative families:

- settings writes
- tenant writes
- user writes
- credential writes

If the dashboard reaches one of those routes through the proxy, the user gets an explicit policy-denied response instead of a proxy-specific substitute workflow.

## Troubleshooting

### Instance does not appear in the proxy

Check:

- the proxy is running
- the tunnel URL ends in `/tunnel`
- the shared passwords match
- the enrollment token is present if the proxy requires one
- the Armada server reports `remoteTunnel.state = Connected` or a useful `lastError`

### `/dashboard` keeps returning to `/`

Check:

- the proxy browser session is still valid
- a deployment is selected in the proxy session
- the selected deployment is still connected

The proxy redirects `/dashboard` back to the portal when either the session or the selected deployment is missing.

### Dashboard loads but some actions fail with policy errors

That is expected for local-only administration routes. Use a direct Armada dashboard session for:

- settings writes
- restore
- shutdown
- factory reset
- tenant, user, and credential administration

### MCP bootstrap details look wrong in remote mode

Also expected. The proxy does not relay the MCP endpoint. Use direct Armada connectivity for MCP client bootstrap or for local server administration.

## Security Notes

Treat the current proxy as an operator service, not a hardened multi-tenant SaaS product.

Current constraints:

- authentication is shared-password based
- proxy auth and Armada auth are separate
- some dangerous routes are blocked entirely rather than re-authenticated
- TLS should be used for any non-local deployment

If the proxy is exposed beyond a trusted network, raise the bar before broad rollout: TLS, stronger auth, better authorization policy, and audit expectations all need to be treated as first-class work.
