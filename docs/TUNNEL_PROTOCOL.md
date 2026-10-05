# Tunnel Protocol

**Version:** `1.0.0`

This document describes the currently shipped tunnel contract between `Armada.Server` and `Armada.Proxy`.

The direction is now:

- keep the outbound Armada tunnel
- keep proxy tunnel termination at `/tunnel`
- add generic dashboard transport relay for `/api/v1/*` and `/ws`
- stop growing the proxy around feature-specific UI route families

Legacy feature-specific tunnel methods still exist for compatibility, but they are no longer the preferred growth path for remote dashboard support.

## Transport Overview

The shipped tunnel provides:

- outbound websocket connection from Armada to the proxy
- handshake with instance identity, shared-password proof, optional enrollment token, and capability manifest
- request/response correlation IDs
- `ping` and `pong` heartbeats
- proxy-maintained instance liveness and stale-state tracking
- generic HTTP relay for dashboard REST
- generic websocket relay for the dashboard `/ws` endpoint
- event publishing from Armada back to the proxy

Still not shipped:

- chunked or streamed relay bodies for very large payloads
- resumable subscriptions
- delegated remote identity
- a generic policy engine inside the tunnel itself

## Envelope Shape

All tunnel messages use the same envelope:

```json
{
  "type": "request",
  "correlationId": "5a9b9ed0cc4343e5882e5f4abaf9d0e0",
  "method": "armada.tunnel.handshake",
  "timestampUtc": "2026-05-16T18:30:00Z",
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

Field rules:

- `correlationId` is required for request/response pairing
- `method` is used for `request` and `event`
- `statusCode`, `success`, `errorCode`, and `message` are carried on terminal responses
- `payload` is optional and JSON-typed

## Handshake

The first Armada message on a new tunnel connection must be `armada.tunnel.handshake`.

Representative request payload:

```json
{
  "protocolVersion": "2026-04-04",
  "armadaVersion": "1.0.0",
  "instanceId": "armada-1f2e3d4c5b6a",
  "enrollmentToken": "optional-token",
  "passwordTimestampUtc": "2026-05-16T18:30:00Z",
  "passwordNonce": "9f31c41b5f934d7ea2865a0b56d3c8ce",
  "passwordProofSha256": "8c77f5..."
}
```

The proxy validates:

- required fields
- protocol version presence
- password timestamp freshness
- password proof correctness
- replay protection for the password proof
- optional enrollment-token policy

Representative successful response:

```json
{
  "type": "response",
  "correlationId": "5a9b9ed0cc4343e5882e5f4abaf9d0e0",
  "method": "armada.tunnel.handshake",
  "statusCode": 200,
  "success": true,
  "payload": {
    "protocolVersion": "2026-04-04",
    "proxyVersion": "1.0.0",
    "features": [
      "remoteControl.handshake",
      "remoteControl.heartbeat",
      "remoteControl.events",
      "remoteControl.requests",
      "dashboard.http.relay",
      "dashboard.websocket.relay"
    ]
  }
}
```

The feature list above is representative. Armada still advertises legacy feature-specific capabilities today, but the important new contract point is the presence of `dashboard.http.relay` and `dashboard.websocket.relay`.

## Generic HTTP Relay

The proxy now forwards dashboard REST traffic through `armada.http.request`.

Payload shape:

```json
{
  "method": "GET",
  "path": "/api/v1/status/health",
  "queryString": null,
  "headers": {
    "Accept": "application/json",
    "X-Request-Id": "abc123"
  },
  "contentType": null,
  "bodyBase64": null
}
```

Current behavior:

- only `/api/v1/*` is accepted
- selected request headers are forwarded
- request and response bodies are base64-encoded when present
- JSON, text, uploads, and downloads are supported
- cancellation and timeout propagate back as relay errors
- proxy-side policy still runs before the request is sent

Representative response payload:

```json
{
  "statusCode": 200,
  "reasonPhrase": "OK",
  "headers": {
    "Content-Type": "application/json; charset=utf-8"
  },
  "contentType": "application/json; charset=utf-8",
  "bodyBase64": "eyJzdGF0dXMiOiJIZWFsdGh5In0="
}
```

Current limitation:

- the first shipped relay assumes request and response bodies fit in one tunnel payload
- chunking/streaming remains open work

## Generic WebSocket Relay

The proxy now forwards the dashboard websocket through these method families:

- `armada.ws.open`
- `armada.ws.message`
- `armada.ws.close`
- `armada.ws.closed`
- `armada.ws.error`

Representative open request payload:

```json
{
  "proxySocketId": "7f3d1f7f1d90491c9b0f4fca1e50f8d9",
  "path": "/ws"
}
```

Representative message event payload:

```json
{
  "proxySocketId": "7f3d1f7f1d90491c9b0f4fca1e50f8d9",
  "data": "{\"type\":\"subscribe\",\"channel\":\"events\"}"
}
```

Representative close payload:

```json
{
  "proxySocketId": "7f3d1f7f1d90491c9b0f4fca1e50f8d9",
  "code": 1000,
  "reason": "Normal closure"
}
```

Current websocket relay guarantees:

- one proxy browser websocket maps to one proxied Armada websocket
- multiple browser sockets can relay through one connected instance tunnel
- message ordering is preserved per proxied socket
- close codes and reasons are forwarded where practical

Current limitation:

- reconnect and recovery semantics after tunnel interruption still need deeper verification

## Legacy Feature-Specific Methods

The server still handles older `armada.*` request families for compatibility, including objective/backlog, planning, workflow, delivery, diagnostics, workspace, and reference methods.

Those methods are now considered compatibility surface:

- they are not the preferred path for new dashboard support
- the shared dashboard should reach new REST behavior through generic `/api/v1/*` relay
- the shared dashboard should reach live behavior through generic `/ws` relay

Removing the legacy method families remains follow-up work after compatibility confidence is high enough.

## Connection Lifecycle And Health

Armada tunnel configuration lives under `remoteControl`:

- `enabled`
- `tunnelUrl`
- `instanceId`
- `enrollmentToken`
- `password`
- `connectTimeoutSeconds`
- `heartbeatIntervalSeconds`
- `reconnectBaseDelaySeconds`
- `reconnectMaxDelaySeconds`
- `allowInvalidCertificates`

Proxy-side instance state is derived as:

- `connected`: websocket is attached and recent tunnel activity is fresh
- `stale`: websocket is still attached but activity is older than `staleAfterSeconds`
- `disconnected`: no active tunnel session

Useful health surfaces:

- Armada: `GET /api/v1/status`, `GET /api/v1/status/health`, `GET /api/v1/settings`
- proxy: `GET /proxy-api/v1/status/health`, `GET /proxy-api/v1/instances`

## Directional Summary

The current tunnel is intentionally in a mixed state:

- generic dashboard relay is now shipped
- legacy feature-specific methods still exist for compatibility
- new remote dashboard work should bias to generic transport, not new feature-specific tunnel methods

That is the architectural direction this repo should continue following.
