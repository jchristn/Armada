# Armada Remote Management

**Version:** 0.7.0

This guide is the operator-focused setup path for connecting an Armada instance to `Armada.Proxy` and then using the proxy app for remote management.

If you want protocol details, see:

- [TUNNEL_PROTOCOL.md](TUNNEL_PROTOCOL.md)
- [TUNNEL_OPERATIONS.md](TUNNEL_OPERATIONS.md)
- [PROXY_API.md](PROXY_API.md)

## Scope

`v0.7.0` supports:

- outbound Armada-to-proxy tunnel connection with shared-password handshake and optional enrollment-token validation
- challenge-based proxy browser login and connected-deployment discovery
- remote fleet, vessel, and playbook management, including default pipeline selection
- remote voyage dispatch and cancellation, including playbook selection with delivery-mode control
- remote mission create, update, cancel, and restart
- remote captain stop
- remote recent activity, mission logs, diffs, and focused detail views

`v0.7.0` does not yet provide:

- SaaS user accounts
- delegated identity or local-session brokerage
- notification inbox/read state
- server-side remote action policy beyond current UI confirmation prompts

Treat the current proxy as an operator service, not a hardened internet-facing SaaS product.

## Default Ports

- Armada API: `7890` (WebSocket available at `/ws` on the same port)
- Armada MCP: `7891`
- Armada.Proxy: `7893`

## 1. Start The Proxy

From the Armada repo root:

```powershell
dotnet run --project src/Armada.Proxy/Armada.Proxy.csproj --framework net10.0
```

By default, the proxy listens on:

- health: `http://localhost:7893/api/v1/status/health`
- instance list: `http://localhost:7893/api/v1/instances` (requires proxy login)
- remote shell: `http://localhost:7893/`
- tunnel endpoint: `ws://localhost:7893/tunnel`

Optional proxy configuration lives under `ArmadaProxy`, for example:

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
    "password": "armadaadmin",
    "handshakeTimeoutSeconds": 15,
    "staleAfterSeconds": 90,
    "requestTimeoutSeconds": 20,
    "maxRecentEvents": 50
  }
}
```

If you enable enrollment-token enforcement, the same token must be configured on the Armada instance.

The proxy now also enforces a shared password for:

- browser login to the proxy app
- Armada tunnel handshake proof validation
- opening the selected deployment in the current proxy UI flow

If `password` is missing or blank on either side, it defaults to `armadaadmin`.

## 2. Point Armada At The Proxy

You can configure the tunnel in any of these places:

- React dashboard: `Server`
- legacy dashboard: `Server Settings`
- `settings.json`
- `PUT /api/v1/settings`

### Option A: React Dashboard

Open the Armada dashboard and go to `Server`.

Set:

- `Enable Remote Tunnel`: `true`
- `Tunnel URL`: for example `ws://localhost:7893/tunnel`
- `Instance ID Override`: optional
- `Enrollment Token`: optional unless required by the proxy
- `Shared Password`: defaults to `armadaadmin` when blank
- timeout and reconnect values as needed

Then click `Save Remote Control Settings`.

### Option B: Legacy Dashboard

Open `Server Settings` and fill in the same `Remote Control` fields:

- enable remote tunnel
- tunnel URL
- optional instance ID override
- optional enrollment token
- shared password
- timeout and reconnect settings

Then click `Save Remote Control Settings`.

### Option C: settings.json

Update your Armada settings file with:

```json
{
  "remoteControl": {
    "enabled": true,
    "tunnelUrl": "ws://localhost:7893/tunnel",
    "instanceId": null,
    "enrollmentToken": null,
    "password": "armadaadmin",
    "connectTimeoutSeconds": 15,
    "heartbeatIntervalSeconds": 30,
    "reconnectBaseDelaySeconds": 5,
    "reconnectMaxDelaySeconds": 60,
    "allowInvalidCertificates": false
  }
}
```

Recommendations:

- use `wss://.../tunnel` outside local development
- leave `instanceId` empty unless you want a stable operator-facing override
- use `allowInvalidCertificates = true` only for local testing with self-signed certificates

## 3. Restart Or Reload Armada

If you saved settings through the dashboards or `PUT /api/v1/settings`, Armada will reload the tunnel settings.

If you edited `settings.json` directly, restart the Armada server.

## 4. Verify The Tunnel On The Armada Side

Check one of these:

- React dashboard `Server`
- legacy dashboard `Server Settings`
- `GET /api/v1/status/health`
- `GET /api/v1/settings`
- `armada status`

Key fields to verify:

- `remoteTunnel.enabled = true`
- `remoteTunnel.state = Connected`
- `remoteTunnel.tunnelUrl` matches the proxy endpoint
- `remoteTunnel.instanceId` is populated
- `remoteTunnel.lastError` is empty
- `remoteTunnel.latencyMs` is reasonable

Example health response fragment:

```json
{
  "remoteTunnel": {
    "enabled": true,
    "state": "Connected",
    "tunnelUrl": "ws://localhost:7893/tunnel",
    "instanceId": "armada-1f2e3d4c5b6a",
    "lastError": null,
    "reconnectAttempts": 0,
    "latencyMs": 42
  }
}
```

## 5. Verify The Tunnel On The Proxy

Check:

- `http://localhost:7893/api/v1/status/health`
- after proxy login, `http://localhost:7893/api/v1/instances`
- or the deployment list in the proxy shell

You should see your Armada instance listed as `connected`.

## 6. Open The Remote Management App

Open:

```text
http://localhost:7893/
```

The current proxy shell login flow is:

1. Enter the proxy shared password.
2. Choose a connected deployment.
3. Enter the deployment password prompt and open the deployment.

In `v0.7.0`, that deployment password prompt reuses the same shared password used by the proxy and the connected Armada instance. It is not yet a distinct per-deployment user-auth model.

If your Armada instance uses a custom remote-control password, the proxy `ArmadaProxy.password` value must match it or the tunnel handshake will fail and the deployment will not appear as connected.

The shell is organized around:

- instances
- recent activity
- missions
- voyages
- captains
- fleets
- vessels
- playbooks
- focused detail
- management forms

## 7. Use The Proxy To Manage Armada

### Manage Fleets

Use `Fleet Studio` to:

- create a new fleet
- edit the selected fleet
- set name, description, default pipeline, and active state

### Manage Vessels

Use `Vessel Studio` to:

- register a vessel
- edit the selected vessel
- set repo URL, working directory, default branch, default pipeline, and concurrency

### Dispatch Voyages

Use `Voyage Dispatch` to:

- choose a vessel ID
- provide a voyage title and description
- optionally set `pipelineId` or `pipeline`
- optionally attach ordered playbook selections with per-selection delivery mode
- provide one mission per line

Mission lines support:

```text
Title :: Description
```

If you omit `::`, the full line is used as both title and description.

### Manage Playbooks

Use `Playbook Studio` to:

- create a markdown playbook
- edit file name, description, content, and active state
- delete a playbook when it is no longer needed
- prepare reusable instruction sets before attaching them during dispatch

### Manage Missions

Use `Mission Studio` to:

- create a standalone mission
- edit a selected mission
- change title, description, vessel, voyage, persona, and priority

From focused mission detail you can also:

- load mission log
- load mission diff
- restart mission
- cancel mission

### Manage Voyages

From focused voyage detail you can:

- inspect the mission chain
- cancel the voyage

### Manage Captains

From focused captain detail you can:

- inspect the captain log
- stop the captain

## 8. Browse Beyond Recent Items

The remote shell includes browse forms for:

- missions
- voyages

Use those to filter by:

- status
- limit
- voyage ID
- vessel ID

This lets you manage remote work even when it no longer appears in the small recent-summary lists.

## 9. Common Local-Dev Configuration

For a local Armada instance and a local proxy:

- proxy URL in Armada: `ws://localhost:7893/tunnel`
- proxy app URL in browser: `http://localhost:7893/`

For a remote proxy with TLS:

- proxy URL in Armada: `wss://your-proxy.example.com/tunnel`
- proxy app URL in browser: `https://your-proxy.example.com/`

## 10. Troubleshooting

### Armada shows `Enabled` but never connects

Check:

- the proxy is running
- the tunnel URL ends in `/tunnel`
- the scheme is `ws`, `wss`, `http`, or `https`
- firewall and DNS allow outbound connectivity

### Proxy rejects the handshake

Check:

- the instance has a non-empty `instanceId`
- the proxy enrollment-token requirement
- the Armada `enrollmentToken` value
- the Armada `remoteControl.password` value matches `ArmadaProxy.password`

### Proxy shows the instance as `stale`

Check:

- Armada is still running
- the host is not asleep
- the network is stable
- `staleAfterSeconds` is appropriate for the environment

### TLS errors

For real deployments:

- use a valid certificate
- prefer `wss://`

For local-only development:

- temporarily enable `allowInvalidCertificates`

## Operator Note

The current remote shell already supports real remote management flows, but it is still intentionally bounded. It is meant for focused operational control, not full remote parity with every local Armada dashboard surface.
