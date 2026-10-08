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
  by `x-tenant-guid` (if any). The `x-user-guid` header is never read: a user comes only from a validated credential,
  so a credential-less Harbor does not count as any user's Harbor for `requireHarborForLaunch`.
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
| `dock` | `HarborDockRequest` | resolve a vessel's repository on the Harbor host, or create or remove a mission dock there |
| `file` | `HarborFileRequest` | stat, read, or write a file in one of the Harbor's docks, or add a git exclude entry |
| `deferredLaunch` | `HarborDeferredLaunchRequest` | arm a one-shot cutover: after the Admiral exits, launch a new slot, health-check it, and roll back on failure |
| `heartbeatAck` | `HarborHeartbeatAck` | acknowledge a heartbeat that carries a `sequence`, so the Harbor can time the link's round trip |

Harbor to server:

| type | class | purpose |
|------|-------|---------|
| `handshake` | `HarborHandshake` | identify the Harbor; advertise capabilities and capacity |
| `started` | `HarborStarted` | a launched process started (reports host PID) |
| `output` | `HarborOutput` | a chunk of stdout or stderr for a job |
| `exited` | `HarborExited` | a captain process exited (exit code, plus optional `durationMs` and `timeToFirstTokenMs`) |
| `gitResult` | `HarborGitResult` | the result of a git/gh request |
| `dockResult` | `HarborDockResult` | the result of a dock request |
| `fileResult` | `HarborFileResult` | the result of a file request |
| `deferredLaunchAck` | `HarborDeferredLaunchAck` | confirm a deferred-launch instruction is armed |
| `heartbeat` | `HarborHeartbeat` | liveness, the set of jobs still running, and the Harbor's view of link health (round trip and reconnects) |
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
Harbor -> heartbeat { liveJobIds: [], sequence: 1, lastRoundTripMs: null, reconnectCount: 0, lastReconnectUtc: null }
Admiral -> heartbeatAck { sequence: 1 }
Harbor -> heartbeat { liveJobIds: [], sequence: 2, lastRoundTripMs: 14, reconnectCount: 0, lastReconnectUtc: null }
...                                            (repeated on the heartbeat interval)
```

### Link health in heartbeats

The heartbeat's link-health fields and the `heartbeatAck` message were added to protocol 1.0 additively, so the version
stays `1.0`. All four heartbeat fields are optional: a Harbor that predates them omits them, and an Admiral that
predates them ignores them.

| Field | Meaning |
|---|---|
| `sequence` | Heartbeat number within this link session, starting at 1. When it is present the Admiral answers with `heartbeatAck { sequence }` as soon as it reads the heartbeat, before any other work. Without it the Admiral sends no acknowledgement, so a Harbor that predates `heartbeatAck` never receives one. |
| `lastRoundTripMs` | Round-trip time of the most recent acknowledged heartbeat in this session, in milliseconds: from the Harbor writing that heartbeat to the socket (not from queueing it, so output queued ahead of it does not count) to the Harbor reading the matching `heartbeatAck`, timed on the Harbor's monotonic clock. Null on the first heartbeat of a session and while no acknowledgement has arrived. An acknowledgement that matches no recent heartbeat is ignored. |
| `reconnectCount` | How many times this Harbor process re-established its link after its first accepted session. The Harbor app keeps the count across sessions (one `HarborLinkStatistics` shared by every session's `HarborLinkClient`). |
| `lastReconnectUtc` | When this Harbor process last re-established its link (UTC, the Harbor's clock), or null when it has not reconnected. |

The Admiral accumulates heartbeats per Harbor per minute (heartbeat count, round-trip count, total, and largest, and the
latest reconnect counters) and writes one `harbor_link_samples` row per Harbor per minute. It also records the link's
transitions in `harbor_link_events`: `Connected` when it accepts a handshake, `Reconnecting` when an open link closes,
and `Disconnected` when the Harbor has not dialed back in within `harbor.heartbeatTimeoutSeconds` (default 45) of the
close, dated at the end of that grace. At startup, before it accepts links, the Admiral closes any link a previous run
left open (at the Harbor's last heartbeat, or at the end of the grace after a close). These feed the link-health series
of `GET /api/v1/harbors/{id}/metrics`; see [Harbor metrics](HARBOR.md#harbor-metrics).

Run a captain:

```
Admiral -> launch { jobId, runtime, workingDirectory, model, prompt, promptViaStdin, arguments, environment,
                    inferenceEndpoint, autoApprove, mcpSessionToken,
                    scratchWorkingDirectory, streamJsonOutput, showThinking, returnFinalMessage,
                    jobKind, missionId, captainId }
Harbor  -> started { jobId, processId }
Harbor  -> output  { jobId, stream: "Stdout", data }   (repeated; stream is "Stdout" or "Stderr")
Admiral -> stdin   { jobId, data }                      (optional)
Admiral -> kill    { jobId, gracefulTimeoutMs }         (optional)
Harbor  -> output  { jobId, stream: "FinalMessage", data }   (once, only when returnFinalMessage was set)
Harbor  -> exited  { jobId, exitCode, durationMs, timeToFirstTokenMs }
```

The `launch` fields after `mcpSessionToken` were added for interactive launches (captain chat, Ask Armada turns,
planning, refinement). They are additive and optional: each defaults to `false`, a Harbor that predates them ignores
them, and the Admiral never sends anything a Harbor did not ask for, so the protocol version stays `1.0`. `jobKind`,
`missionId`, and `captainId` came later on the same terms: optional strings (absent from an older Admiral) that only
label the job on the Harbor.

| Field | Meaning |
|---|---|
| `scratchWorkingDirectory` | When `workingDirectory` is empty or does not exist on the Harbor host, run the job in a per-job scratch directory the Harbor creates (under its temporary directory, `armada-harbor/scratch/<jobId>`) and removes when the job ends. Without it the working directory must exist; a missing one fails the launch. Missions never set it. |
| `streamJsonOutput` | Run a Claude Code captain with `--output-format stream-json --include-partial-messages`, or a Codex captain with `codex exec --json` (chat turns: streaming and per-turn telemetry). Ignored for other runtimes; a Harbor that predates the Codex case runs Codex in plain text, which the Admiral also reads. |
| `showThinking` | Ask the runtime to surface the model's reasoning (Mux `--show-thinking`). |
| `returnFinalMessage` | Have the runtime write its final-message artifact (Codex `--output-last-message`, Mux) to a Harbor-side file outside the working directory, and send its content back as one `output` message on the `FinalMessage` stream just before `exited`. |
| `jobKind` | What the launch is for, as a string: `Mission`, `AskTurn`, `Planning`, `Refinement`, `ContextBuild`, or `Other`. Informational: the Harbor shows it in its job list and names its job logs with it. A value the Harbor does not know is treated as unknown, never refused. |
| `missionId` | The mission a `Mission` launch runs. Informational. |
| `captainId` | The captain the launch runs as. Informational. |

A Harbor reports stdout lines on the `Stdout` stream and stderr lines on the `Stderr` stream (a Harbor that predates the
split reports both as `Stdout`). The Admiral's mission lifecycle reads both; chat and planning read only `Stdout`.

`capabilities` is a list of `{ name, available, detail }` objects. A `handshake.maxConcurrentJobs` that is omitted or
not positive means the Harbor advertises no capacity: a new registration gets `harbor.defaultMaxJobsPerHarbor` and an
existing one keeps its capacity. The Admiral does not watch heartbeat timing for liveness: a Harbor is marked
disconnected when its link closes. `kill.gracefulTimeoutMs` defaults to 10000. A
Harbor that cannot launch (for example a build without a job runner, a CLI that is not installed on the Harbor host, or
a working directory that does not exist there) answers with `error { jobId, message }`; the Admiral fails that launch at
once with the Harbor's message instead of waiting for `started`.

Delegate a git operation:

```
Admiral -> git       { requestId, executable: "git", workingDirectory, arguments: ["worktree","add", ...], timeoutMs }
Harbor  -> gitResult { requestId, exitCode, standardOutput, standardError, timedOut }
```

`timeoutMs` (the Harbor stops the command after it; 0 or absent means the Harbor's default of 120 seconds) and
`timedOut` are additive. A command that cannot start on the Harbor (a working directory that does not exist there, a
missing executable) is answered with a failed `gitResult` (`exitCode` -1, the reason in `standardError`).

Harbor-side mission docks (only with a Harbor that advertises the `harbor-docks` capability):

```
Admiral -> dock       { requestId, operation: "Resolve", vesselId, vesselName, repoUrl, defaultBranch }
Harbor  -> dockResult { requestId, success, message, source: "Mapped"|"Discovered"|"Clone"|"None",
                        repositoryPath, checkoutPath }
Admiral -> dock       { requestId, operation: "Provision", vesselId, vesselName, repoUrl, defaultBranch, branchName, dockName }
Harbor  -> dockResult { requestId, success, message, source, repositoryPath, checkoutPath, worktreePath, headCommit }
Admiral -> dock       { requestId, operation: "Reclaim", vesselId, vesselName, worktreePath, repositoryPath }
Harbor  -> dockResult { requestId, success, message, worktreePath }
Admiral -> file       { requestId, operation: "Stat"|"Read"|"Write"|"AddGitExclude", path, content }
Harbor  -> fileResult { requestId, success, exists, isDirectory, content, message }
```

The Harbor decides where repositories and docks live on its machine (see
[HARBOR.md](HARBOR.md#dock-affinity-and-routing)): `Resolve` reports the checkout named for the vessel in its settings,
a checkout under its root folders whose remote matches `repoUrl`, or the bare clone it would make; `success: false`
(source `None`) carries a `message` saying which Harbor setting to change. `Provision` creates the dock under the
Harbor's docks folder as `<vessel>/<dockName>` and returns its path and HEAD commit. `Reclaim` and every `file`
operation refuse paths outside the docks folder; `AddGitExclude` takes a dock path and adds `content` to the git exclude
file of the repository the dock belongs to. After `Provision` the Admiral sends ordinary `git` requests (with the
Harbor's paths) for everything else it does with the dock, and `launch` with the dock as the working directory. These
messages are additive: the Admiral sends them only to a Harbor that advertised `harbor-docks`, so the protocol version
stays `1.0`.

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
Harbor disconnected and fails any `git`, `dock`, `file`, or `deferredLaunch` request still waiting for its reply. The Harbor app
reconnects every 3 seconds and re-sends `handshake`; a close from an older, superseded socket does not disconnect the
newer link.

Messages are not buffered across a drop: `output` and `exited` events produced while the link is down are lost. The
Admiral keeps its per-job listeners, so output a still-running job produces after the reconnect streams to the same
`jobId` again. `heartbeat.liveJobIds` is recorded as the Harbor's in-flight count; it is not used to reconcile the
missions of jobs that ended during the drop. For the Harbor metrics only, the first heartbeat after a reconnect settles
the Admiral's job records: a job launched over an earlier link that is not in `liveJobIds` is recorded as `Lost` (an
`exited` that arrives later still wins). A mission whose captain exited, or kept quiet, while the link was down is recovered by
stall detection; see [Harbor disconnects](HARBOR.md#harbor-disconnects).
