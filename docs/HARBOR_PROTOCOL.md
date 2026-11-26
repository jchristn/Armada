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
| `file` | `HarborFileRequest` | stat, read, or write a file in one of the Harbor's docks, or add a git exclude entry; with a `root`, list, read, save, create, rename, delete, or search in a vessel's checkout |
| `deferredLaunch` | `HarborDeferredLaunchRequest` | arm a one-shot cutover: after the Admiral exits, launch a new slot, health-check it, and roll back on failure |
| `heartbeatAck` | `HarborHeartbeatAck` | acknowledge a heartbeat that carries a `sequence`, so the Harbor can time the link's round trip |

Harbor to server:

| type | class | purpose |
|------|-------|---------|
| `handshake` | `HarborHandshake` | identify the Harbor; advertise capabilities and capacity |
| `started` | `HarborStarted` | a launched process started (reports host PID) |
| `output` | `HarborOutput` | a chunk of stdout or stderr for a job |
| `activity` | `HarborActivity` | the latest activity of a job that asked for structured progress (a tool call, text, or reasoning) |
| `exited` | `HarborExited` | a captain process exited (exit code, plus optional `durationMs`, `timeToFirstTokenMs`, `discoveredContextLength` and `discoveredContextSource`) |
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
Admiral -> handshakeAck { accepted: true, mcpBaseUrl, missingCapabilities }
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
                    jobKind, missionId, captainId, structuredProgress, display,
                    cliPermissionPolicy, permissionPromptTimeoutSeconds }
Harbor  -> started { jobId, processId }
Harbor  -> output  { jobId, stream: "Stdout", data }   (repeated; stream is "Stdout" or "Stderr")
Harbor  -> activity { jobId, activity }                (repeated, only with structuredProgress; at most ~1/s per job)
Admiral -> stdin   { jobId, data }                      (optional)
Admiral -> kill    { jobId, gracefulTimeoutMs }         (optional)
Harbor  -> output  { jobId, stream: "FinalMessage", data }   (once, only when returnFinalMessage was set)
Harbor  -> exited  { jobId, exitCode, durationMs, timeToFirstTokenMs, discoveredContextLength, discoveredContextSource }
```

Context discovery (`api-context-discovery-v1`, RS-7.8): for an API captain whose `inferenceEndpoint.inferenceOptions` sets
no `contextLength`, a Harbor that advertises this capability reads the window from the model server on its own host before
the first turn (Ollama `/api/ps`, then `/api/show` `num_ctx`; `/v1/models` `max_model_len` for an OpenAI-compatible server),
uses it for compaction and the context-limit stop, and returns it in `exited.discoveredContextLength` with
`discoveredContextSource`. `inferenceEndpoint.discoveredContextLength` and `discoveredContextSource` carry the window the
Admiral already knows; the Harbor uses it when its own read finds nothing, and does not report it back. The Admiral stores a
reported window on the endpoint only from a Harbor that advertised the capability, and never over a configured
`contextLength`. All four fields are optional and additive; a Harbor that predates them ignores them and runs without a
window.

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
| `structuredProgress` | Run a mission with the runtime's structured output (Claude Code `--output-format stream-json`, Codex `exec --json`) and decode it on the Harbor: the readable output the CLI prints in text mode (Claude Code's final reply, Codex's last agent message) goes out on the `Stdout` stream as before, and the captain's latest activity as `activity` messages. Ignored for other runtimes, which stay plain text. A Harbor that predates it ignores it and runs plain text, which the Admiral reads the same way; an Admiral that predates it never sets it, so it never receives `activity`. See [Mission progress streaming](CAPTAINS.md#mission-progress-streaming). |
| `display` | What the launch is about, for the Harbor's Running now list: an optional object whose fields are all optional strings or numbers. Informational only; a Harbor that predates it ignores it. See the table below. |
| `cliPermissionPolicy` | `Bypass`, `Refuse`, or `ApproveInArmada`, matching the Admiral's resolved policy. API Endpoint workers enforce this field for `run_process`; absent policy fails closed as Refuse. CLI workers also receive the corresponding `autoApprove` value. The Admiral selects `ApproveInArmada` only for a Harbor that advertised the `cli-permission-prompt` capability with the captain's runtime in its detail (comma-separated runtime names; the Harbor lists `ClaudeCode`, and `Mux` when its `mux` is 1.5.0 or later). The Harbor then points the CLI at Armada's `cli_permission_prompt` tool on the scoped server it binds `mcpSessionToken` to (Claude Code `--permission-prompt-tool mcp__armada__cli_permission_prompt`; Mux `--approval-policy ask --permission-prompt-tool armada.cli_permission_prompt` with the workspace-write sandbox). Without a bound token it runs the launch as `autoApprove` says (Refuse), and it writes which way it went in the job log. A Harbor that predates it ignores it and runs Refuse. |
| `permissionPromptTimeoutSeconds` | With `cliPermissionPolicy: ApproveInArmada`: the seconds Armada waits for a decision. The Harbor sets the CLI's own wait two minutes longer, so Armada expires (and denies) a request first. Absent means 600. |

`display` fields (the Admiral fills the ones that apply; null ones are omitted):

| Field | Meaning |
|---|---|
| `missionTitle` | The mission's title. |
| `vesselName` | The mission's vessel. |
| `voyageId`, `voyageTitle` | The mission's voyage. |
| `voyagePosition`, `voyageMissionCount` | The mission's position in its voyage (from 1, by creation order) and how many missions the voyage has ("2 of 3"). |
| `captainName`, `captainModel` | The captain and the model it runs (when it names one). |
| `stage` | The pipeline stage or persona the mission runs as (for example `Implement`, `Judge`). |
| `branchName` | The branch the mission works on. |
| `askThreadId` | The Ask Armada conversation of an Ask turn (also set for its narration and summary turns). |
| `askQuestion` | The user's question of an Ask turn: its first line, at most 200 characters. |

`activity` carries `jobId` and `activity`, a `RuntimeActivity`: `kind` (`ToolCall`, `Text`, or `Thinking`),
`toolName`, `description` (the description a tool call gave, such as Claude Code's Bash description), `detail` (the short
input of a tool call, such as the command or file path, or the first line of text or reasoning, at most 200
characters), `summary` (one line, for example `Running tests: dotnet test src/App.sln`), and `timestampUtc`. The Harbor
sends at most one per job per second, newest wins, and none after `exited`. The Admiral records it as the mission's
current activity, counts it as output for stall detection and for the job's time to first output, and mirrors it into
the mission log as a `> ` line. `exited.timeToFirstTokenMs` counts the first activity as output, so a mission that
streams its work reports when it started answering, not when it printed its final reply.

A Harbor reports stdout lines on the `Stdout` stream and stderr lines on the `Stderr` stream (a Harbor that predates the
split reports both as `Stdout`). The Admiral's mission lifecycle reads both; chat and planning read only `Stdout`.

`capabilities` is a list of `{ name, available, detail }` objects. A `handshake.maxConcurrentJobs` that is omitted or
not positive means the Harbor advertises no capacity: a new registration gets `harbor.defaultMaxJobsPerHarbor` and an
existing one keeps its capacity. The Admiral does not watch heartbeat timing for liveness: a Harbor is marked
disconnected when its link closes. `kill.gracefulTimeoutMs` defaults to 10000. A
Harbor that cannot launch (for example a build without a job runner, a CLI that is not installed on the Harbor host, or
a working directory that does not exist there) answers with `error { jobId, message }`; the Admiral fails that launch at
once with the Harbor's message instead of waiting for `started`.

### API captain capability flags

A Harbor that runs jobs also advertises one flag per captain runtime behavior that runs on the Harbor, each a capability
with `available: true` (names in `HarborCapabilityFlags`). A Harbor that lacks a flag runs the earlier behavior, and
the Admiral falls back to it: the launch still goes to that Harbor and nothing new is required of it.

| flag | behavior | advertised |
|------|----------|------------|
| `api-inference-streaming-v1` | streams API captain responses with idle and first-response deadlines | since 1.4.0 |
| `api-submit-tool-v1` | honors the native submit tool for a stage's structured output (`ARMADA_SUBMIT_TOOL`) | 1.5.0 |
| `api-tool-scope-v1` | limits scoped MCP tools to the persona's scope (`ARMADA_MCP_TOOL_SCOPE`) | 1.5.0 |
| `api-completion-line-v1` | treats a missing completion line as incomplete (`ARMADA_REQUIRES_COMPLETION_LINE`) | 1.5.0 |
| `cli-final-message-hygiene-v1` | CLI runtimes clean their final message: OpenCode JSON final message and stdout error events, no `--dir` for OpenCode 2, reasoning stripped | 1.5.0 |
| `api-text-tool-call-recovery-v1` | recovers tool calls a model wrote as text | reserved |
| `api-malformed-call-retry-v1` | corrects instead of resending a request that failed on a malformed tool call or a repeat loop | 1.5.0 |
| `api-repetition-guard-v1` | stops repeated identical tool calls | reserved |
| `api-reasoning-hygiene-v1` | strips reasoning from the final message and history | 1.5.0 |
| `api-tool-limits-scaled-v1` | scales tool output limits to the context window | 1.5.0 |
| `api-edit-encoding-preserve-v1` | keeps a file's byte order mark and line endings on edits | 1.5.0 |
| `api-edit-line-prefix-guard-v1` | rejects edits and writes whose text carries `read_file` line-number prefixes | 1.5.0 |
| `api-context-discovery-v1` | discovers a model's served context window on the Harbor | 1.5.0 |
| `api-model-warmup-v1` | loads an Ollama model with `keep_alive` before the first turn | 1.5.0 |

Reserved flags are declared but not advertised until their behavior ships. The Admiral expects every flag its own
Harbor build advertises. For a Harbor that lacks one, `handshakeAck.missingCapabilities` lists the missing names (null
when none are missing; an older Harbor ignores the field), the Harbor record's `missingCapabilities` and
`updateRecommended` (computed, not stored) report it on REST, and a captain whose exclusive session or active mission
runs on such a Harbor reads `harborUpdateRecommended: true`. The dashboard, TUI, and mobile app show "Harbor update
recommended" on the Harbor and the captain; the Harbor app shows it in its connection detail.

Streaming default: an API captain endpoint whose `inferenceOptions.streamingMode` is `Automatic` streams on
OpenAI-compatible, Ollama, and Ollama Cloud endpoints. For a Harbor launch the Admiral resolves `Automatic` before it
sends the endpoint: on when the Harbor advertises `api-inference-streaming-v1`, off otherwise. It sends the result as
`streamingMode` and as `streaming`, so a 1.4.0 Harbor applies it. `On` or `Off` set by the operator always wins; `On`
still requires a Harbor with the flag.

Delegate a git operation:

```
Admiral -> git       { requestId, executable: "git", workingDirectory, arguments: ["worktree","add", ...], timeoutMs,
                       expectedExitCodes, kind, label }
Harbor  -> gitResult { requestId, exitCode, standardOutput, standardError, timedOut }
```

`timeoutMs` (the Harbor stops the command after it; 0 or absent means the Harbor's default of 120 seconds) and
`timedOut` are additive. A command that cannot start on the Harbor (a working directory that does not exist there, a
missing executable) is answered with a failed `gitResult` (`exitCode` -1, the reason in `standardError`). The Harbor runs
each `git` request off its receive loop, so a long command (a check run or a build) does not hold up launches, dock and
file requests, or heartbeat acknowledgements; requests with the same working directory still run one at a time, in the
order they arrived. Commands still running when the link closes are stopped.

`expectedExitCodes`, `kind`, and `label` are optional and additive; they only change how the Harbor presents the command
in its activity log, not how it runs or what `gitResult` reports. `expectedExitCodes` lists non-zero exit codes the
Admiral reads as an answer rather than a failure (for example `[1]` for `git grep`, `git show-ref --verify --quiet`,
`git merge-base --is-ancestor`, or `git ls-files --error-unmatch`, and `[128]` for `git rev-parse --git-dir` outside a
repository); the Harbor does not show such an exit as a failure. `kind` is `Routine` (the default when absent: git work
behind a mission, dock, or checkout, which the Harbor collapses into one line per dock), `CheckRun` (a check run's
command), or `Command` (a command an operator asked for, such as a fleet action or a Workspace command); check runs and
commands get their own line. `label` is a short name for the line, such as the check's name.

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

Operations in a vessel's checkout (only with a Harbor that advertises the `harbor-checkouts` capability, which a Harbor
with `harbor-docks` from this release advertises too):

```
Admiral -> file       { requestId, operation, root, path, vesselId, vesselName, repoUrl,
                        content, newPath, expectedHash, query, maxResults, maxBytes }
Harbor  -> fileResult { requestId, success, errorCode, exists, isDirectory, sizeBytes, lastWriteUtc, truncated,
                        content, tree, file, saved, entry, search, message }
```

The Admiral uses these when the vessel has no working directory on its own host and a Harbor's `Resolve` reported a
checkout (`Mapped` or `Discovered`) for it: check runs, Workspace, readiness, health, and the vessel branch views (see
[HARBOR.md](HARBOR.md#checkouts-outside-missions)). Commands in the checkout (the check command, Workspace exec, git)
are ordinary `git` requests with the checkout as the working directory.

| Field | Meaning |
|---|---|
| `root` | The checkout (or a dock) the request is confined to, as an absolute path on the Harbor host. The Harbor accepts it only when it is inside its docks folder or is the checkout it maps the request's vessel to (it locates the vessel itself from `vesselId`, `vesselName`, and `repoUrl`, as for `Resolve`); any other root is refused. |
| `path` | Relative to `root`; empty for the root itself. An absolute path, a path that leaves the root, a path through a symbolic link or junction, and `.git` are refused. |
| `operation` | `Stat` and `Read` (root-relative; build output folders allowed; `Read` returns no content and `truncated: true` past `maxBytes`, default 8 MB), and the Workspace operations `List`, `ReadFile`, `SaveFile` (`content`, `expectedHash`), `CreateDirectory`, `Rename` (`newPath`), `Delete`, and `Search` (`query`, `maxResults`), which also hide `node_modules`, `bin`, `obj`, `dist`, and `coverage` as Workspace does on the Admiral. `OpenTerminal` opens a terminal window on the Harbor's desktop in the root (or the folder `path` inside it); unlike the other operations it is refused for a dock, so the root must be the checkout the Harbor maps the vessel to, and the Admiral sends it only to a Harbor that advertised `open-terminal`. |
| `tree`, `file`, `saved`, `entry`, `search` | The Workspace result of `List`, `ReadFile`, `SaveFile`, `CreateDirectory`/`Rename`/`Delete`, and `Search`: the same shapes as the REST Workspace routes. |
| `errorCode` | Why a request failed: `Refused` (a path or root that is not allowed), `NotFound`, `Conflict` (the file changed after it was opened), `Invalid` (for example a directory where a file was expected), or `Failed`; `None` on success. The Admiral raises the same error the failure has on its own disk, so REST and MCP report it the same way. Dock file requests (without `root`) carry it too. |

These fields and operations are additive: the Admiral sends them only to a Harbor that advertised `harbor-checkouts`,
so the protocol version stays `1.0`. A desktop Harbor (the tray app) also advertises `open-terminal`, which enables
`OpenTerminal` for the Workspace's Open in Terminal; an Admiral linked to a Harbor without it tells the user to update
that Harbor instead of sending the operation.

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

Job messages survive a drop. The Harbor keeps one job outbox for the life of the process: every `started`, `output`,
`activity`, `exited`, and job-scoped `error` carries a rising `outboxSequence`, and is kept until the Admiral
acknowledges it (`lastJobSequence` on `handshakeAck` and on heartbeat acks). After the next handshake the Harbor replays,
in order, everything not yet acknowledged; the Admiral drops duplicates per Harbor by sequence. The handshake carries a
`jobOutboxEpoch` that changes when the Harbor process restarts, which resets the sequence. The outbox holds at most
20000 messages or 32 MiB; when full it drops the oldest `output` first and keeps `started` and `exited`. All of these
fields are optional, so an older Harbor or Admiral keeps the earlier behavior.

Reconciliation: the handshake lists the Harbor's running jobs (`liveJobIds`) and its `heartbeatIntervalMs`. A job the
Admiral tracks that has started but is missing from a handshake or heartbeat, and whose `exited` does not arrive within
20 seconds, is reported to its listener as failed; for a mission that is an interrupted exit, which redispatches it. The
`handshakeAck` carries `ownedJobIds`, and the Harbor stops any job it is running that the Admiral no longer owns (for
example a mission that was recovered while the laptop slept), so two agents never work in one dock.

Dead links: the Admiral closes a link silent for longer than max(45 seconds, 3x the Harbor's heartbeat interval), and
the Harbor ends a session on its side after the same gap once a heartbeat has been acknowledged, then reconnects. A
Harbor with heartbeats disabled is never swept.

### Optional storage inventory in heartbeats

Protocol 1.0 heartbeats may carry `storageReport`, a typed `StorageHostReport` using the protocol's camelCase JSON naming. The updated Harbor app collects its managed logs, job logs, docks, bare clones and explicitly mapped repositories on a background worker every five minutes. Heartbeat creation reads the last report without touching the filesystem. Heartbeats disabled means these reports are not delivered. Discovery search roots and external model/CLI caches are not recursively scanned. Older Harbors omit the field; older Admirals ignore it.

A report contains `measuredUtc`, `status`, `locations`, `volumes` and bounded warnings. Location detail uses logical file lengths and bounded largest-file lists, never file content. The Admiral accepts a report only on an authenticated linked Harbor, overrides its host identity with the link's Harbor ID, and bounds collection detail. The report is visible exclusively through the global administrator storage API, never through ordinary Harbor detail or metrics responses. Cached measurements survive a disconnect within the running Admiral process and become stale. Restarting the Admiral clears this cache.
