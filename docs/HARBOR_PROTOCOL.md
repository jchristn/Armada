# Harbor Link Protocol

The Harbor link is the authenticated, client-to-server WebSocket a Harbor (host runner) opens to the
Admiral. The Admiral pushes host work down the link; the Harbor executes it and streams results back.
This document is the contract both sides implement. It is versioned by `HarborProtocol.Version`
(currently `1.0`); bump that constant on any breaking change to the message set.

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

The upgrade request is authenticated like any other inbound request: the Harbor presents a Credential
(signed-request preferred for remote, or access-key/secret over TLS), the server resolves the tenant and
normalizes to the Credential principal, and rejects with a close frame on failure. The session is
re-validated for the life of the link and torn down if the credential, user, tenant, or session is
disabled. Secrets never appear in any message, label, or log.

## Message set

Server to Harbor:

| type | class | purpose |
|------|-------|---------|
| `handshakeAck` | `HarborHandshakeAck` | accept/reject the link; advertise the MCP base URL |
| `launch` | `HarborLaunchRequest` | start a captain process for a job |
| `stdin` | `HarborStdinRequest` | write to a running captain's stdin |
| `kill` | `HarborKillRequest` | terminate a captain (graceful window, then kill tree) |
| `git` | `HarborGitRequest` | run a git/gh command in a working directory |

Harbor to server:

| type | class | purpose |
|------|-------|---------|
| `handshake` | `HarborHandshake` | identify the Harbor; advertise capabilities and capacity |
| `started` | `HarborStarted` | a launched process started (reports host PID) |
| `output` | `HarborOutput` | a chunk of stdout or stderr for a job |
| `exited` | `HarborExited` | a captain process exited (reports exit code) |
| `gitResult` | `HarborGitResult` | the result of a git/gh request |
| `heartbeat` | `HarborHeartbeat` | liveness plus the set of jobs still running |
| `error` | `HarborError` | a command could not be carried out, or a job failed abnormally |

## Identifiers

Jobs are keyed by a Harbor-scoped `jobId` assigned by the Admiral on `launch`. The host process id in
`started` is informational (it lives on the Harbor, not the Admiral); liveness and termination flow
through `jobId`, not PID. Git calls are keyed by `requestId`.

## Sequences

Connect and register:

```
Harbor -> handshake { harborId, name, protocolVersion, capabilities, maxConcurrentJobs }
Admiral -> handshakeAck { accepted: true, mcpBaseUrl }
Harbor -> heartbeat { liveJobIds: [] }        (repeated on the heartbeat interval)
```

Run a captain:

```
Admiral -> launch { jobId, runtime, workingDirectory, prompt, arguments, environment }
Harbor  -> started { jobId, processId }
Harbor  -> output  { jobId, stream: "stdout", data }   (repeated)
Admiral -> stdin   { jobId, data }                      (optional)
Admiral -> kill    { jobId, gracefulTimeoutMs }         (optional)
Harbor  -> exited  { jobId, exitCode }
```

Delegate a git operation:

```
Admiral -> git       { requestId, executable: "git", workingDirectory, arguments: ["worktree","add", ...] }
Harbor  -> gitResult { requestId, exitCode, standardOutput, standardError }
```

## Reconnection

If the link drops while jobs are running, the captain processes keep running on the Harbor. On reconnect
the Harbor re-sends `handshake` and its next `heartbeat` lists the still-live `jobId`s, so the Admiral
rebinds them and resumes streaming rather than orphaning the work or marking it stalled. Because a dock
lives on exactly one Harbor's filesystem, a mission cannot move hosts; while its owning Harbor is
disconnected, that mission's host operations queue and resume when the Harbor returns.
