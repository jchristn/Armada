# Proxy API

**Version:** 0.7.0

This document describes the first shipped Armada proxy API surface in `v0.7.0`.

`v0.7.0` includes:

- websocket tunnel termination at `/tunnel`
- challenge-based browser login using the shared proxy password
- in-memory instance registration keyed by `instanceId`
- live instance summary and detail endpoints
- a mobile-first remote operations shell served at `/`
- focused remote inspection endpoints for activity, missions, voyages, captains, logs, and diffs
- bounded remote management endpoints for fleets, vessels, playbooks, voyages, missions, and captain control
- live request/response forwarding for Armada health, status, detail snapshots, and management actions
- tunnel handshake validation using shared-password proofs plus optional enrollment-token validation

`v0.7.0` does not yet include:

- SaaS user accounts
- enrollment workflows beyond static token validation
- delegated identity or remote authorization mapping
- notification inboxes
- persistent proxy storage
- server-side remote action policy evaluation beyond the current shell confirmation prompts

---

## Default Bind

The proxy binds to:

- Host: `localhost`
- Port: `7893`

Configuration is read from the `ArmadaProxy` section:

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

---

## Authentication Model

The proxy now exposes a small auth surface for the browser app:

- `GET /api/v1/auth/challenge`
- `POST /api/v1/auth/login`
- `POST /api/v1/auth/logout`

`GET /api/v1/status/health` remains unauthenticated.

All other `/api/v1/*` routes require the `X-Armada-Proxy-Session` header, obtained from `POST /api/v1/auth/login`.

The browser does not send the raw shared password. It first requests a nonce from `/api/v1/auth/challenge`, computes a SHA-256 proof in the browser, and submits that proof to `/api/v1/auth/login`.

If `ArmadaProxy.password` is omitted or blank, the proxy defaults it to `armadaadmin`.

### GET /api/v1/auth/challenge

Returns a one-time challenge for browser login.

```json
{
  "nonce": "4f3a0c7a8f6c49d9b6711d2c1a7b5e90",
  "expiresUtc": "2026-04-04T05:10:00Z"
}
```

### POST /api/v1/auth/login

Validates the browser proof and returns a short-lived session token.

```json
{
  "nonce": "4f3a0c7a8f6c49d9b6711d2c1a7b5e90",
  "proofSha256": "8f5c4e1e1d7b5d8b2f6c6c987bfb76f5d55a75b8b940f882c817d39de42d83cc"
}
```

```json
{
  "token": "0f34455311b54e719f50927df5ecdfd798f8f27ed4ae45e2a73c0c3b2d194f73",
  "expiresUtc": "2026-04-04T17:05:00Z"
}
```

### POST /api/v1/auth/logout

Invalidates the current browser session identified by `X-Armada-Proxy-Session`.

---

## REST Endpoints

### GET /

Serves the proxy remote operations shell.

The shell is designed for quick remote triage rather than full local-dashboard parity. In `v0.7.0` it includes:

- instance list
- instance summary cards
- recent activity feed
- mission, voyage, captain, fleet, and vessel lists
- focused mission, voyage, captain, fleet, and vessel detail
- fleet creation and editing
- vessel creation and editing
- playbook creation, editing, deletion, and selection during voyage dispatch
- voyage dispatch and cancellation
- mission creation, editing, cancellation, and restart
- captain stop
- inline mission log, mission diff, and captain log viewers

### GET /api/v1/status/health

Returns proxy process health and instance counts.

```json
{
  "healthy": true,
  "product": "Armada.Proxy",
  "version": "0.7.0",
  "protocolVersion": "2026-04-04",
  "port": 7893,
  "startedUtc": "2026-04-03T21:00:00Z",
  "instances": {
    "total": 2,
    "connected": 1,
    "stale": 1,
    "offline": 0
  }
}
```

### GET /api/v1/instances

Returns summary rows for all known instances.

```json
{
  "count": 1,
  "instances": [
    {
      "instanceId": "armada-1f2e3d4c5b6a",
      "state": "connected",
      "armadaVersion": "0.7.0",
      "protocolVersion": "2026-04-04",
      "capabilities": [
        "remoteControl.handshake",
        "remoteControl.heartbeat",
        "remoteControl.events",
        "remoteControl.requests",
        "status.health",
        "status.snapshot",
        "settings.remoteControl"
      ],
      "remoteAddress": "127.0.0.1",
      "firstSeenUtc": "2026-04-03T21:00:00Z",
      "connectedUtc": "2026-04-03T21:00:00Z",
      "lastSeenUtc": "2026-04-03T21:02:00Z",
      "lastEventUtc": "2026-04-03T21:01:30Z",
      "lastDisconnectUtc": null,
      "lastError": null,
      "recentEventCount": 3,
      "pendingRequestCount": 0
    }
  ]
}
```

`state` can be:

- `connected`
- `stale`
- `offline`

### GET /api/v1/instances/{instanceId}

Returns the current summary plus recent inbound event history for an instance.

```json
{
  "summary": {
    "instanceId": "armada-1f2e3d4c5b6a",
    "state": "connected",
    "armadaVersion": "0.7.0",
    "protocolVersion": "2026-04-04",
    "capabilities": [
      "remoteControl.handshake",
      "remoteControl.heartbeat",
      "remoteControl.events",
      "remoteControl.requests",
      "status.health",
      "status.snapshot",
      "settings.remoteControl"
    ],
    "remoteAddress": "127.0.0.1",
    "firstSeenUtc": "2026-04-03T21:00:00Z",
    "connectedUtc": "2026-04-03T21:00:00Z",
    "lastSeenUtc": "2026-04-03T21:02:00Z",
    "lastEventUtc": "2026-04-03T21:01:30Z",
    "lastDisconnectUtc": null,
    "lastError": null,
    "recentEventCount": 3,
    "pendingRequestCount": 0
  },
  "recentEvents": [
    {
      "method": "mission.completed",
      "correlationId": "b0f3d61d59d74e5a855f6b2e3953c64f",
      "message": null,
      "timestampUtc": "2026-04-03T21:01:30Z",
      "payload": {
        "message": "Mission completed: Update prompt templates",
        "missionId": "msn_abc123"
      }
    }
  ]
}
```

### GET /api/v1/instances/{instanceId}/summary

Returns the aggregated remote-shell summary for a connected instance by issuing `armada.instance.summary` over the tunnel and unwrapping the successful payload.

```json
{
  "generatedUtc": "2026-04-03T21:05:00Z",
  "health": {
    "status": "healthy",
    "version": "0.7.0"
  },
  "status": {
    "activeVoyages": 1,
    "workingCaptains": 1
  },
  "recentActivity": [],
  "recentMissions": [],
  "recentVoyages": [],
  "recentCaptains": []
}
```

### Focused Remote Inspection Endpoints

The following proxy routes unwrap the successful payload returned by the instance:

- `GET /api/v1/instances/{instanceId}/activity?limit=20`
- `GET /api/v1/instances/{instanceId}/missions/recent?limit=10`
- `GET /api/v1/instances/{instanceId}/voyages/recent?limit=10`
- `GET /api/v1/instances/{instanceId}/captains/recent?limit=10`
- `GET /api/v1/instances/{instanceId}/missions/{missionId}`
- `GET /api/v1/instances/{instanceId}/missions/{missionId}/log?lines=200&offset=0`
- `GET /api/v1/instances/{instanceId}/missions/{missionId}/diff`
- `GET /api/v1/instances/{instanceId}/voyages/{voyageId}`
- `GET /api/v1/instances/{instanceId}/captains/{captainId}`
- `GET /api/v1/instances/{instanceId}/captains/{captainId}/log?lines=50&offset=0`

Example mission detail response:

```json
{
  "mission": {
    "id": "msn_abc123",
    "title": "Update prompt templates",
    "status": "Review",
    "persona": "Judge"
  },
  "captain": {
    "id": "cpt_abc123",
    "name": "judge-1",
    "runtime": "ClaudeCode"
  },
  "voyage": {
    "id": "vyg_abc123",
    "title": "Remote control tranche"
  },
  "vessel": {
    "id": "vsl_abc123",
    "name": "armada-repo"
  },
  "dock": {
    "id": "dck_abc123",
    "branchName": "armada/remote-shell"
  }
}
```

### Remote Management Endpoints

The proxy now forwards a bounded management surface into the connected Armada instance.

Fleet management:

- `GET /api/v1/instances/{instanceId}/fleets?limit=25`
- `GET /api/v1/instances/{instanceId}/fleets/{fleetId}`
- `POST /api/v1/instances/{instanceId}/fleets`
- `PUT /api/v1/instances/{instanceId}/fleets/{fleetId}`

Vessel management:

- `GET /api/v1/instances/{instanceId}/vessels?limit=25&fleetId={fleetId}`
- `GET /api/v1/instances/{instanceId}/vessels/{vesselId}`
- `POST /api/v1/instances/{instanceId}/vessels`
- `PUT /api/v1/instances/{instanceId}/vessels/{vesselId}`

Playbook management:

- `GET /api/v1/instances/{instanceId}/playbooks?limit=25`
- `GET /api/v1/instances/{instanceId}/playbooks/{playbookId}`
- `POST /api/v1/instances/{instanceId}/playbooks`
- `PUT /api/v1/instances/{instanceId}/playbooks/{playbookId}`
- `DELETE /api/v1/instances/{instanceId}/playbooks/{playbookId}`

Voyage management:

- `GET /api/v1/instances/{instanceId}/voyages?limit=25&status=InProgress`
- `POST /api/v1/instances/{instanceId}/voyages/dispatch`
- `DELETE /api/v1/instances/{instanceId}/voyages/{voyageId}`

Mission management:

- `GET /api/v1/instances/{instanceId}/missions?limit=25&status=Review&voyageId={voyageId}&vesselId={vesselId}`
- `POST /api/v1/instances/{instanceId}/missions`
- `PUT /api/v1/instances/{instanceId}/missions/{missionId}`
- `DELETE /api/v1/instances/{instanceId}/missions/{missionId}`
- `POST /api/v1/instances/{instanceId}/missions/{missionId}/restart`

Captain control:

- `POST /api/v1/instances/{instanceId}/captains/{captainId}/stop`

Example voyage dispatch request:

```json
{
  "title": "Remote release hardening",
  "description": "Ship the v0.7.0 proxy management surface.",
  "vesselId": "vsl_abc123",
  "pipeline": "FullPipeline",
  "selectedPlaybooks": [
    {
      "playbookId": "pbk_abc123",
      "deliveryMode": "InlineFullContent"
    }
  ],
  "missions": [
    {
      "title": "Tighten remote shell UX",
      "description": "Add mission and voyage browser filters to the proxy shell."
    },
    {
      "title": "Update proxy docs",
      "description": "Document the shipped management endpoints and shell workflows."
    }
  ]
}
```

Example mission update request:

```json
{
  "title": "Update proxy docs",
  "description": "Document the shipped management endpoints and shell workflows.",
  "persona": "Worker",
  "priority": 50,
  "vesselId": "vsl_abc123",
  "voyageId": "vyg_abc123"
}
```

### GET /api/v1/instances/{instanceId}/status/snapshot

Sends a live tunnel request to the connected Armada instance using method `armada.status.snapshot`.

```json
{
  "correlationId": "62cf28fa232f49a5aab48debe031eb89",
  "success": true,
  "statusCode": 200,
  "errorCode": null,
  "message": "Armada status snapshot captured.",
  "payload": {
    "totalCaptains": 2,
    "idleCaptains": 1,
    "workingCaptains": 1,
    "stalledCaptains": 0,
    "activeVoyages": 1,
    "missionsByStatus": {
      "Pending": 2,
      "InProgress": 1
    },
    "voyages": [],
    "recentSignals": [],
    "remoteTunnel": {
      "enabled": true,
      "state": "Connected",
      "tunnelUrl": "wss://proxy.example.com/tunnel",
      "instanceId": "armada-1f2e3d4c5b6a",
      "lastError": null,
      "reconnectAttempts": 0,
      "latencyMs": 42
    },
    "timestampUtc": "2026-04-03T21:02:00Z"
  }
}
```

### GET /api/v1/instances/{instanceId}/health

Sends a live tunnel request to the connected Armada instance using method `armada.status.health`.

```json
{
  "correlationId": "a81ec0a5ee024679b719046d1bf8de85",
  "success": true,
  "statusCode": 200,
  "errorCode": null,
  "message": "Armada health snapshot captured.",
  "payload": {
    "status": "healthy",
    "timestamp": "2026-04-03T21:02:00Z",
    "startUtc": "2026-04-03T20:00:00Z",
    "uptime": "0.01:02:00",
    "version": "0.7.0",
    "ports": {
      "admiral": 7890,
      "mcp": 7891
    },
    "remoteTunnel": {
      "enabled": true,
      "state": "Connected",
      "tunnelUrl": "wss://proxy.example.com/tunnel",
      "instanceId": "armada-1f2e3d4c5b6a",
      "lastError": null,
      "reconnectAttempts": 0,
      "latencyMs": 42
    }
  }
}
```

If the instance is offline, the live endpoints return a `400` response with:

```json
{
  "error": "Instance armada-1f2e3d4c5b6a is not connected."
}
```

---

## Tunnel Endpoint

### GET /tunnel

`/tunnel` is a websocket endpoint, not a normal REST resource.

Expected first message:

- `type = request`
- `method = armada.tunnel.handshake`

See [docs/TUNNEL_PROTOCOL.md](TUNNEL_PROTOCOL.md) for envelope details.

---

## Current Guardrails

- the registry is process-local and in-memory
- browser API access is protected by a shared-password session gate, not a multi-user identity model
- tunnel registration requires a valid shared-password proof and optional enrollment-token validation
- routed requests currently support:
  - `armada.instance.summary`
  - `armada.fleets.list`
  - `armada.fleet.detail`
  - `armada.fleet.create`
  - `armada.fleet.update`
  - `armada.vessels.list`
  - `armada.vessel.detail`
  - `armada.vessel.create`
  - `armada.vessel.update`
  - `armada.playbooks.list`
  - `armada.playbook.detail`
  - `armada.playbook.create`
  - `armada.playbook.update`
  - `armada.playbook.delete`
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
- recent event history is bounded by `maxRecentEvents`
- destructive actions are client-confirmed in the remote shell, but there is still no per-user authz or policy engine; this remains an implementation-stage operator service
