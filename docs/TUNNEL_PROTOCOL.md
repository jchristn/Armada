# Tunnel Protocol

**Version:** 0.7.0

This document describes the shipped tunnel contract between `Armada.Server` and `Armada.Proxy` in `v0.7.0`.

`v0.7.0` now ships:

- outbound websocket tunnel initiation from Armada
- proxy websocket termination at `/tunnel`
- handshake with protocol version, instance ID, shared-password proof, optional enrollment token, and capability manifest
- request/response correlation IDs
- event forwarding from Armada to the proxy
- bounded proxy management routing for fleets, vessels, voyages, missions, and captain control
- `ping` / `pong` heartbeat handling
- reconnect with capped exponential backoff and jitter
- proxy stale/offline instance semantics

Not yet shipped:

- subscription lifecycle management
- resumable subscriptions
- chunked streaming for large payloads
- delegated remote identity
- general-purpose remote action routing or policy evaluation

---

## Envelope Shape

All tunnel messages use the same JSON envelope:

```json
{
  "type": "request",
  "correlationId": "5a9b9ed0cc4343e5882e5f4abaf9d0e0",
  "method": "armada.tunnel.handshake",
  "timestampUtc": "2026-04-03T18:30:00Z",
  "statusCode": null,
  "success": null,
  "errorCode": null,
  "message": null,
  "payload": {}
}
```

Recognized `type` values:

- `request`
- `response`
- `event`
- `ping`
- `pong`
- `error`

### Field Rules

- `correlationId` is required for request/response pairing.
- `method` is required for `request` and `event`.
- `statusCode` and `success` are used on `response` and `error`.
- `payload` is optional and JSON-typed.
- `timestampUtc` is optional but included by shipped emitters.

---

## Handshake

The first message from Armada must be:

```json
{
  "type": "request",
  "correlationId": "5a9b9ed0cc4343e5882e5f4abaf9d0e0",
  "method": "armada.tunnel.handshake",
  "timestampUtc": "2026-04-04T18:30:00Z",
  "payload": {
    "protocolVersion": "2026-04-04",
    "armadaVersion": "0.7.0",
    "instanceId": "armada-1f2e3d4c5b6a",
    "enrollmentToken": "optional-token",
    "passwordProofSha256": "9c9be4bdc9b3d11f2c4a9a482d0f36d93fb7357db10ee3119af7c0a0c38e4d54",
    "passwordNonce": "4f3a0c7a8f6c49d9b6711d2c1a7b5e90",
    "passwordTimestampUtc": "2026-04-04T18:30:00Z",
    "capabilities": [
      "remoteControl.handshake",
      "remoteControl.heartbeat",
      "remoteControl.events",
      "remoteControl.requests",
      "instance.summary",
      "fleets.list",
      "fleet.detail",
      "fleet.create",
      "fleet.update",
      "vessels.list",
      "vessel.detail",
      "vessel.create",
      "vessel.update",
      "activity.recent",
      "missions.recent",
      "missions.list",
      "mission.create",
      "mission.update",
      "mission.cancel",
      "mission.restart",
      "voyages.recent",
      "voyages.list",
      "voyage.dispatch",
      "voyage.cancel",
      "captains.recent",
      "captain.stop",
      "mission.detail",
      "mission.log",
      "mission.diff",
      "voyage.detail",
      "captain.detail",
      "captain.log",
      "status.health",
      "status.snapshot",
      "settings.remoteControl"
    ]
  }
}
```

The proxy validates:

- `instanceId` is present
- `protocolVersion` is present
- `passwordProofSha256`, `passwordNonce`, and `passwordTimestampUtc` are present
- the shared-password proof matches the configured `ArmadaProxy.password`
- the password-proof timestamp is fresh enough to reject stale or replayed handshakes
- enrollment token rules from `ArmadaProxy`, when enabled

If either side leaves the password blank, it defaults to `armadaadmin`.

The tunnel does not send the raw shared password. The proof is a SHA-256 value derived from the instance ID, timestamp, nonce, and the password hash.

Accepted handshake response:

```json
{
  "type": "response",
  "correlationId": "5a9b9ed0cc4343e5882e5f4abaf9d0e0",
  "timestampUtc": "2026-04-04T18:30:00Z",
  "statusCode": 200,
  "success": true,
  "message": "Handshake accepted.",
  "payload": {
    "accepted": true,
    "proxyVersion": "0.7.0",
    "protocolVersion": "2026-04-04",
    "instanceId": "armada-1f2e3d4c5b6a",
    "message": "Handshake accepted.",
    "capabilities": [
      "instances.summary",
      "instances.detail",
      "instances.shell.summary",
      "instances.fleets.list",
      "instances.fleet.detail",
      "instances.fleet.create",
      "instances.fleet.update",
      "instances.vessels.list",
      "instances.vessel.detail",
      "instances.vessel.create",
      "instances.vessel.update",
      "instances.activity",
      "instances.missions.list",
      "instances.missions.recent",
      "instances.mission.create",
      "instances.mission.update",
      "instances.mission.cancel",
      "instances.mission.restart",
      "instances.voyages.list",
      "instances.voyages.recent",
      "instances.voyage.dispatch",
      "instances.voyage.cancel",
      "instances.captains.recent",
      "instances.mission.detail",
      "instances.mission.log",
      "instances.mission.diff",
      "instances.voyage.detail",
      "instances.captain.detail",
      "instances.captain.log",
      "instances.captain.stop",
      "armada.status.snapshot",
      "armada.status.health"
    ]
  }
}
```

Rejected handshake response:

```json
{
  "type": "response",
  "correlationId": "5a9b9ed0cc4343e5882e5f4abaf9d0e0",
  "timestampUtc": "2026-04-04T18:30:00Z",
  "statusCode": 401,
  "success": false,
  "errorCode": "handshake_rejected",
  "message": "Handshake shared password proof is invalid."
}
```

---

## Heartbeats

Armada periodically sends:

```json
{
  "type": "ping",
  "correlationId": "0a8ce9bdb1ea4857a97d8bbd6d388df0",
  "timestampUtc": "2026-04-03T18:31:00Z"
}
```

The proxy answers:

```json
{
  "type": "pong",
  "correlationId": "0a8ce9bdb1ea4857a97d8bbd6d388df0",
  "timestampUtc": "2026-04-03T18:31:00Z"
}
```

Armada records round-trip latency from matching `pong` envelopes.

Armada also responds to inbound `ping` messages with a matching `pong`.

---

## Routed Requests

The proxy currently issues these live requests:

- `armada.instance.summary`
- `armada.fleets.list`
- `armada.fleet.detail`
- `armada.fleet.create`
- `armada.fleet.update`
- `armada.vessels.list`
- `armada.vessel.detail`
- `armada.vessel.create`
- `armada.vessel.update`
- `armada.activity.recent`
- `armada.missions.list`
- `armada.missions.recent`
- `armada.mission.create`
- `armada.mission.update`
- `armada.mission.cancel`
- `armada.mission.restart`
- `armada.voyages.list`
- `armada.voyages.recent`
- `armada.voyage.dispatch`
- `armada.voyage.cancel`
- `armada.captains.recent`
- `armada.captain.stop`
- `armada.mission.detail`
- `armada.mission.log`
- `armada.mission.diff`
- `armada.voyage.detail`
- `armada.captain.detail`
- `armada.captain.log`
- `armada.status.snapshot`
- `armada.status.health`

Example request:

```json
{
  "type": "request",
  "correlationId": "62cf28fa232f49a5aab48debe031eb89",
  "method": "armada.status.snapshot",
  "timestampUtc": "2026-04-03T18:32:00Z"
}
```

Example response:

```json
{
  "type": "response",
  "correlationId": "62cf28fa232f49a5aab48debe031eb89",
  "timestampUtc": "2026-04-03T18:32:00Z",
  "statusCode": 200,
  "success": true,
  "message": "Armada status snapshot captured.",
  "payload": {
    "totalCaptains": 2,
    "workingCaptains": 1,
    "activeVoyages": 1
  }
}
```

Unsupported requests return:

```json
{
  "type": "response",
  "correlationId": "62cf28fa232f49a5aab48debe031eb89",
  "timestampUtc": "2026-04-03T18:32:00Z",
  "statusCode": 404,
  "success": false,
  "errorCode": "unsupported_method",
  "message": "Unsupported tunnel method armada.unknown."
}
```

---

## Forwarded Events

Armada forwards server-side events to the proxy as `event` envelopes.

Example:

```json
{
  "type": "event",
  "correlationId": "b0f3d61d59d74e5a855f6b2e3953c64f",
  "method": "mission.completed",
  "timestampUtc": "2026-04-03T18:33:00Z",
  "payload": {
    "message": "Mission completed: Update prompt templates",
    "missionId": "msn_abc123",
    "voyageId": "vyg_abc123"
  }
}
```

The proxy stores recent inbound events per instance for detail inspection.

---

## URL Handling

`remoteControl.tunnelUrl` accepts:

- `ws://...`
- `wss://...`
- `http://...`
- `https://...`

`http` is normalized to `ws`, and `https` is normalized to `wss`.

Any other scheme is rejected and surfaced through `RemoteTunnel.LastError`.

---

## Reconnect Behavior

When the tunnel is enabled and a connection attempt fails, Armada:

1. records the failure in `RemoteTunnel.LastError`
2. increments `RemoteTunnel.ReconnectAttempts`
3. waits using capped exponential backoff with jitter
4. retries until the server stops or the feature is disabled

The timing is controlled by:

- `remoteControl.connectTimeoutSeconds`
- `remoteControl.heartbeatIntervalSeconds`
- `remoteControl.reconnectBaseDelaySeconds`
- `remoteControl.reconnectMaxDelaySeconds`

---

## Offline And Stale Semantics

The proxy computes instance state as:

- `connected`: websocket open and recent tunnel activity is within `staleAfterSeconds`
- `stale`: websocket still attached but no tunnel activity has been observed within `staleAfterSeconds`
- `offline`: no active websocket session is attached

---

## Status Surfaces

Armada exposes tunnel state through:

- `GET /api/v1/status`
- `GET /api/v1/status/health`
- `GET /api/v1/settings`
- `armada status`
- the React and legacy server dashboards

The proxy exposes tunnel-derived state through:

- `GET /api/v1/status/health`
- `GET /api/v1/instances`
- `GET /api/v1/instances/{instanceId}`

Current Armada tunnel states:

- `Disabled`
- `Disconnected`
- `Connecting`
- `Connected`
- `Error`
- `Stopping`
