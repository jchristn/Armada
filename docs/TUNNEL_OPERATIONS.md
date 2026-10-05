# Tunnel Operations

**Version:** 1.0.0

This guide covers the shipped remote-control tunnel and proxy MVP surfaces in Armada `v0.9.0`.

For a step-by-step operator setup path, see [REMOTE_MGMT.md](REMOTE_MGMT.md).

---

## Scope

`v0.9.0` now includes:

- the Armada-side outbound websocket tunnel client
- remote tunnel configuration in Armada settings and dashboards
- a minimal `Armada.Proxy` service with websocket termination and instance registry APIs
- a proxy-hosted remote operations shell served at `/`
- live forwarded status/health requests from the proxy into a connected Armada instance
- focused remote inspection requests for recent activity, missions, voyages, captains, logs, and diffs
- bounded remote management requests for fleets, vessels, playbooks, backlog/objectives, refinement sessions, planning sessions, workflow profiles, checks, environments, releases, deployments, incidents, runbooks, runbook executions, and captain control
- shell workflows for fleet and vessel editing, voyage dispatch and cancellation, mission create/update/cancel/restart, backlog/planning handoff, delivery operations, diagnostics, and read-only reference inspection

Still not included:

- user-facing SaaS auth
- delegated identity or local-session brokerage
- notification delivery
- persistent proxy storage
- server-side remote action policy evaluation beyond current shell confirmation prompts
- secret-bearing admin editing such as credentials or token overrides

Treat the current proxy as an implementation-stage operator service, not a hardened public SaaS surface.

---

## Armada Instance Configuration

Armada stores remote tunnel configuration in `settings.json`:

```json
{
  "remoteControl": {
    "enabled": false,
    "tunnelUrl": null,
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

- `GET /api/v1/status/health`
- `GET /api/v1/instances`
- `GET /api/v1/instances/{instanceId}`
- `GET /api/v1/instances/{instanceId}/status/snapshot`
- `GET /api/v1/instances/{instanceId}/health`
- `GET /api/v1/instances/{instanceId}/fleets`
- `GET /api/v1/instances/{instanceId}/vessels`
- `GET /api/v1/instances/{instanceId}/voyages`
- `GET /api/v1/instances/{instanceId}/missions`
- `POST /api/v1/instances/{instanceId}/fleets`
- `PUT /api/v1/instances/{instanceId}/fleets/{fleetId}`
- `POST /api/v1/instances/{instanceId}/vessels`
- `PUT /api/v1/instances/{instanceId}/vessels/{vesselId}`
- `POST /api/v1/instances/{instanceId}/voyages/dispatch`
- `DELETE /api/v1/instances/{instanceId}/voyages/{voyageId}`
- `POST /api/v1/instances/{instanceId}/missions`
- `PUT /api/v1/instances/{instanceId}/missions/{missionId}`
- `DELETE /api/v1/instances/{instanceId}/missions/{missionId}`
- `POST /api/v1/instances/{instanceId}/missions/{missionId}/restart`
- `POST /api/v1/instances/{instanceId}/captains/{captainId}/stop`

Proxy instance states:

- `connected`
- `stale`
- `offline`

---

## Common Failure Modes

### Armada enabled but no tunnel URL

Symptoms:

- Armada tunnel state becomes `Error`
- `lastError` says no tunnel URL is configured

Fix:

- set `remoteControl.tunnelUrl`
- or disable `remoteControl.enabled`

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

- check `instanceId` presence
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

The proxy currently supports live requests for:

- `armada.instance.summary`
- `armada.instance.status`
- `armada.instance.health`
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
- `armada.voyage.detail`
- `armada.voyage.cancel`
- `armada.captain.detail`
- `armada.captain.stop`
- `armada.playbooks.list`
- `armada.playbook.detail`
- `armada.playbook.create`
- `armada.playbook.update`
- `armada.playbook.delete`
- `armada.backlog.list`
- `armada.backlog.detail`
- `armada.backlog.create`
- `armada.backlog.update`
- `armada.backlog.delete`
- `armada.objective-refinement-sessions.list`
- `armada.objective-refinement-sessions.create`
- `armada.objective-refinement-session.detail`
- `armada.objective-refinement-session.message`
- `armada.objective-refinement-session.summarize`
- `armada.objective-refinement-session.apply`
- `armada.objective-refinement-session.stop`
- `armada.objective-refinement-session.delete`
- `armada.planning-sessions.list`
- `armada.planning-session.detail`
- `armada.planning-session.create`
- `armada.planning-session.message`
- `armada.planning-session.summarize`
- `armada.planning-session.dispatch`
- `armada.planning-session.stop`
- `armada.planning-session.delete`
- `armada.workflow-profiles.list`
- `armada.workflow-profile.detail`
- `armada.workflow-profile.create`
- `armada.workflow-profile.update`
- `armada.workflow-profile.delete`
- `armada.check-runs.list`
- `armada.check-run.detail`
- `armada.check-run.create`
- `armada.check-run.retry`
- `armada.check-run.delete`
- `armada.environments.list`
- `armada.environment.detail`
- `armada.environment.create`
- `armada.environment.update`
- `armada.environment.delete`
- `armada.releases.list`
- `armada.release.detail`
- `armada.release.create`
- `armada.release.update`
- `armada.release.refresh`
- `armada.release.delete`
- `armada.deployments.list`
- `armada.deployment.detail`
- `armada.deployment.create`
- `armada.deployment.update`
- `armada.deployment.approve`
- `armada.deployment.deny`
- `armada.deployment.verify`
- `armada.deployment.rollback`
- `armada.deployment.delete`
- `armada.incidents.list`
- `armada.incident.detail`
- `armada.incident.create`
- `armada.incident.update`
- `armada.incident.delete`
- `armada.runbooks.list`
- `armada.runbook.detail`
- `armada.runbook.create`
- `armada.runbook.update`
- `armada.runbook.delete`
- `armada.runbook-executions.list`
- `armada.runbook-execution.detail`
- `armada.runbook-execution.create`
- `armada.runbook-execution.update`
- `armada.runbook-execution.delete`
- `armada.captain.tools`
- `armada.request-history.list`
- `armada.request-history.detail`
- `armada.request-history.summary`
- `armada.workspace.status`
- `armada.workspace.tree`
- `armada.workspace.file`
- `armada.workspace.search`
- `armada.workspace.changes`
- `armada.pipelines.list`
- `armada.pipeline.detail`
- `armada.personas.list`
- `armada.persona.detail`
- `armada.prompt-templates.list`
- `armada.prompt-template.detail`
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

The remote shell uses simple browser confirmation prompts before destructive actions such as mission cancellation, voyage cancellation, mission restart, and captain stop. These are operator safety rails, not a substitute for the future delegated identity and policy model.

If the instance is offline, those endpoints return an error instead of cached data.

Recent forwarded events are retained in memory only and are bounded by `maxRecentEvents`.

---

## Release Notes

The `v0.7.0 -> v0.9.0` release adds Armada backlog/objective normalization schema changes on the server side.

`Armada.Server` applies those schema migrations automatically on first startup after upgrade. If you need a controlled DBA-managed rollout, the versioned handoff scripts in `migrations/` emit the backend-specific SQL and precheck guidance:

- `migrations/migrate_v0.7.0_to_v0.8.0.sh`
- `migrations/migrate_v0.7.0_to_v0.8.0.bat`
