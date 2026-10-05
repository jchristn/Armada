# Tunnel Protocol

**Version:** `1.0.0`

This document describes the currently shipped tunnel contract between `Armada.Server` and `Armada.Proxy`.

In short:

- Armada opens an outbound websocket to the proxy, which terminates it at `/tunnel`
- the proxy relays dashboard traffic for `/api/v1/*` and `/ws` through generic relay methods
- the proxy has no feature-specific UI route families

The Admiral serves only the generic relay methods (`armada.http.request` and `armada.ws.open`, `armada.ws.message`, `armada.ws.close`). The older feature-specific methods were removed; any other request method gets a `404` response with error code `unsupported_method`.

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
  "passwordProofSha256": "8c77f5...",
  "capabilities": [
    "remoteControl.handshake",
    "remoteControl.heartbeat",
    "remoteControl.events",
    "remoteControl.requests",
    "dashboard.http.relay",
    "dashboard.websocket.relay"
  ]
}
```

`protocolVersion` is `Constants.RemoteTunnelProtocolVersion` (currently `2026-04-04`); `armadaVersion` is the product
version. `capabilities` is the Admiral's capability manifest: exactly the six features above, where
`dashboard.http.relay` and `dashboard.websocket.relay` signal the generic relay.

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
  "statusCode": 200,
  "success": true,
  "message": "Handshake accepted.",
  "payload": {
    "accepted": true,
    "proxyVersion": "1.0.0",
    "protocolVersion": "2026-04-04",
    "instanceId": "armada-1f2e3d4c5b6a",
    "message": "Handshake accepted.",
    "capabilities": [
      "proxy.portal",
      "dashboard.static",
      "dashboard.http.relay",
      "dashboard.websocket.relay",
      "instances.summary",
      "instances.selection",
      "tunnel.handshake",
      "tunnel.ping"
    ]
  }
}
```

Responses carry no `method`; they are matched by `correlationId`. A first message that is not
`armada.tunnel.handshake` gets an `error` envelope with code `invalid_handshake` and the socket closes. A handshake that
fails validation gets a response with error code `handshake_rejected` and the socket closes; repeated failures from
one client lock it out for a while.

## Generic HTTP Relay

The proxy forwards dashboard REST traffic through `armada.http.request`.

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

- only a canonical path under `/api/v1/*` is accepted
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

- request and response bodies must fit in one tunnel payload; bodies larger than 8 MiB
  (`Constants.DefaultRemoteRelayMaxBodyBytes`) are rejected
- chunking/streaming remains open work

## Generic WebSocket Relay

The proxy forwards the dashboard websocket through these requests, which the Admiral serves:

- `armada.ws.open`
- `armada.ws.message`
- `armada.ws.close`

The Admiral sends socket traffic and state back to the proxy with these events:

- `armada.ws.message` (a frame from the Armada `/ws` endpoint, same payload shape as the request)
- `armada.ws.closed`
- `armada.ws.error`

Representative open request payload:

```json
{
  "proxySocketId": "7f3d1f7f1d90491c9b0f4fca1e50f8d9",
  "path": "/ws",
  "queryString": null,
  "subprotocols": null
}
```

Only `path: "/ws"` is accepted.

Representative message payload:

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

## Unsupported Methods

The Admiral answers only `armada.http.request`, `armada.ws.open`, `armada.ws.message`, and `armada.ws.close`. The
older feature-specific request families (objective/backlog, planning, workflow, delivery, diagnostics, workspace, and
reference methods) are no longer served. A request with any other method gets a response with status `404` and error
code `unsupported_method`:

```json
{
  "type": "response",
  "correlationId": "5a9b9ed0cc4343e5882e5f4abaf9d0e0",
  "statusCode": 404,
  "success": false,
  "errorCode": "unsupported_method",
  "message": "Tunnel method armada.objectives.list is not supported. Use generic dashboard relay methods instead."
}
```

A request with no method gets `400` with error code `missing_method`.

The shared dashboard reaches REST behavior through the generic `/api/v1/*` relay and live behavior through the
generic `/ws` relay; new remote features need no new tunnel methods.

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
- `offline`: no active tunnel session

Useful health surfaces:

- Armada: `GET /api/v1/status`, `GET /api/v1/status/health`, `GET /api/v1/settings`
- proxy: `GET /proxy-api/v1/status/health`, `GET /proxy-api/v1/instances`

## Directional Summary

- the generic dashboard relay (`armada.http.request`, `armada.ws.*`) is the whole request surface the Admiral serves
- every other request method returns `404 unsupported_method`
- new remote dashboard work uses the generic transport, not new feature-specific tunnel methods
