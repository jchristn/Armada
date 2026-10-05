# Harbor Link Protocol

The Harbor link is the authenticated, client-to-server WebSocket a Harbor (host runner) opens to the
Admiral. The Admiral pushes host work down the link; the Harbor executes it and streams results back.
This document is the contract both sides implement. It is versioned by `HarborProtocol.Version`
(currently `1.0`); bump that constant on any breaking change to the message set.

> **Experimental.** The Harbor link (split mode) is experimental for Armada 1.0 and excluded from the compatibility
> promise in [COMPATIBILITY.md](COMPATIBILITY.md). The protocol version above still guards Harbor/Admiral pairing.

## Transport and framing

The Harbor dials the Admiral at the configured link path (default `/v1.0/harbor/connect`) over WSS.
Because the Harbor is the client, the connection works from behind NAT and from a container-published
port without the Admiral ever reaching into the host. Every frame is a UTF-8 JSON text message carrying
exactly one `HarborMessage`. Both ends serialize and deserialize through `Armada.Core.Harbor.HarborProtocol`
so the format is identical: camelCase properties, enums as strings, null properties omitted, and a `type`
discriminator that selects the concrete class.

Messages are polymorphic. The `type` field is written first and read to pick the subtype:

```json
{ "type": "launch", "correlationId": "c-1", "jobId": "job-1", "runtime": "claude", ... }
```

Every message carries an optional `correlationId` (tying a response to its request) and an optional
`traceParent` (W3C trace context, so delegated work joins the Admiral's trace).

## Authentication

The Harbor presents its credential as headers on the WebSocket upgrade, and the Admiral checks them when the first
message arrives. That first message must be a `handshake`; anything else gets an `error` and the link closes.

- A credential is either `x-access-key: <token>` or an `Authorization` header. It must be a valid Armada credential
  (a bearer token, a session token, or the local API key), not a mission- or Ask-scoped token. The Harbor registers
  under that credential's tenant and user; a global admin may name a tenant with `x-tenant-guid`.
- Without a credential the link is accepted only when `harbor.requireAuth` is false, the Admiral listens on a loopback
  hostname, and the Harbor connects from loopback. The Harbor then registers with no owning user, in the tenant named
  by `x-tenant-guid` (if any).
- A refused Harbor receives `handshakeAck { accepted: false, reason }` and the link closes. A Harbor id already
  registered to a different tenant or user is refused the same way.

The credential is checked once per link; it is not re-validated while the link stays open. Use TLS (`wss://`) for
any Harbor that is not on the Admiral's host. Secrets never appear in any message, label, or log.

## Message set

Server to Harbor:

| type | class | purpose |
|------|-------|---------|
| `handshakeAck` | `HarborHandshakeAck` | accept/reject the link (`accepted`, `reason`); advertise the MCP base URL |
| `launch` | `HarborLaunchRequest` | start a captain process for a job |
| `stdin` | `HarborStdinRequest` | write to a running captain's stdin |
| `kill` | `HarborKillRequest` | terminate a captain (graceful window, then kill tree) |
| `git` | `HarborGitRequest` | run a git/gh command in a working directory |
| `deferredLaunch` | `HarborDeferredLaunchRequest` | arm a one-shot cutover: after the Admiral exits, launch a new slot, health-check it, and roll back on failure |

Harbor to server:

| type | class | purpose |
|------|-------|---------|
| `handshake` | `HarborHandshake` | identify the Harbor; advertise capabilities and capacity |
| `started` | `HarborStarted` | a launched process started (reports host PID) |
| `output` | `HarborOutput` | a chunk of stdout or stderr for a job |
| `exited` | `HarborExited` | a captain process exited (exit code, plus optional `durationMs` and `timeToFirstTokenMs`) |
| `gitResult` | `HarborGitResult` | the result of a git/gh request |
| `deferredLaunchAck` | `HarborDeferredLaunchAck` | confirm a deferred-launch instruction is armed |
| `heartbeat` | `HarborHeartbeat` | liveness plus the set of jobs still running |
| `error` | `HarborError` | a command could not be carried out, or a job failed abnormally |

## Identifiers

Jobs are keyed by a Harbor-scoped `jobId` assigned by the Admiral on `launch`. The host process id in
`started` is informational (it lives on the Harbor, not the Admiral); liveness and termination flow
through `jobId`, not PID. Git calls are keyed by `requestId`.

## Sequences

Connect and register:

```
Harbor -> handshake { harborId, name, protocolVersion, osPlatform, architecture, capabilities, maxConcurrentJobs }
Admiral -> handshakeAck { accepted: true, mcpBaseUrl }
Harbor -> heartbeat { liveJobIds: [] }        (repeated on the heartbeat interval)
```

Run a captain:

```
Admiral -> launch { jobId, runtime, workingDirectory, model, prompt, promptViaStdin, arguments, environment,
                    inferenceEndpoint, autoApprove, mcpSessionToken }
Harbor  -> started { jobId, processId }
Harbor  -> output  { jobId, stream: "Stdout", data }   (repeated; stream is "Stdout" or "Stderr")
Admiral -> stdin   { jobId, data }                      (optional)
Admiral -> kill    { jobId, gracefulTimeoutMs }         (optional)
Harbor  -> exited  { jobId, exitCode, durationMs, timeToFirstTokenMs }
```

`capabilities` is a list of `{ name, available, detail }` objects. `kill.gracefulTimeoutMs` defaults to 10000. A
Harbor that cannot launch (for example a build without a job runner) answers with `error { jobId, message }`.

Delegate a git operation:

```
Admiral -> git       { requestId, executable: "git", workingDirectory, arguments: ["worktree","add", ...] }
Harbor  -> gitResult { requestId, exitCode, standardOutput, standardError }
```

Delegate a rebuild cutover (health-gated rollback):

```
Admiral -> deferredLaunch    { requestId, launchExePath, waitForPid, workingDirectory,
                               healthUrl, healthTimeoutSeconds, fallbackExePath, fallbackSlot, currentPointerPath }
Harbor  -> deferredLaunchAck  { requestId, armed: true }   (armed: false plus message when launchExePath is missing)
(Admiral then exits.)
Harbor  launches launchExePath with ARMADA_RESTART_WAIT_PID=waitForPid, polls healthUrl up to
        healthTimeoutSeconds; on failure it rewrites currentPointerPath to fallbackSlot and launches
        fallbackExePath.
```

The instruction is delivered and acknowledged while the Admiral is still alive, so the link being dead
during the actual cutover is irrelevant. See [SERVER_REBUILD.md](SERVER_REBUILD.md).

## Reconnection

If the link drops while jobs are running, the captain processes keep running on the Harbor. The Admiral marks the
Harbor disconnected and fails any `git` or `deferredLaunch` request still waiting for its reply. The Harbor app
reconnects every 3 seconds and re-sends `handshake`; a close from an older, superseded socket does not disconnect the
newer link.

Messages are not buffered across a drop: `output` and `exited` events produced while the link is down are lost. The
Admiral keeps its per-job listeners, so output a still-running job produces after the reconnect streams to the same
`jobId` again. `heartbeat.liveJobIds` is recorded as the Harbor's in-flight count; it is not used to reconcile jobs
that ended during the drop. A mission whose captain exited, or kept quiet, while the link was down is recovered by
stall detection; see [Harbor disconnects](HARBOR.md#harbor-disconnects).
