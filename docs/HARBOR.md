# Harbors

A Harbor is a detached host-side runner. It lets the Admiral -- Armada's coordinator process -- live in
one place while the agent CLIs, git, and worktrees that actually touch your code live somewhere else. The
two halves stay joined by a single authenticated link that the Harbor opens outward to the Admiral.

## Why Harbors exist

Armada wants to run as a durable server. The natural home for a durable server is a container or a box that
stays up: Docker, a VM, a spare host on your network. Agent work has the opposite gravity. Claude Code and
its peers expect to run where the developer already is -- where the repositories are checked out, where the
git credentials and `gh` login already work, where the tool config and the model API keys already live.
Containerizing the Admiral and then trying to reach back into the developer's machine to run those tools is
awkward and, from behind NAT or a published container port, often impossible.

Harbors resolve that tension by splitting the process. The Admiral keeps the database, the REST and MCP
surfaces, the dashboard, and the orchestration logic. A Harbor runs on the developer's machine and does the
host-bound work: it launches captain processes, feeds them stdin, kills them, and runs git and `gh`
commands in the right working directory. Because the Harbor dials out to the Admiral rather than the other
way around, the link works from behind NAT and from a container-published port without the Admiral ever
reaching into the host.

## Local mode vs Split mode

Today Armada runs in Local mode by default, and Local mode is fully functional. The Admiral and the agent
processes share one machine; there is no Harbor and no link, and nothing about your setup changes. This is
the right mode for a single developer running Armada on the same box they code on.

Split mode is the emerging shape. The Admiral runs detached -- in Docker or on another host -- and one or
more Harbors run on the machines where the code and tool logins live. The Admiral routes each unit of host
work to a Harbor over the link, the Harbor executes it locally, and results stream back. Split mode is what
lets a single containerized Admiral drive agents across several developer machines at once.

## How the link works

The Harbor is the client. It dials the Admiral at a configured WebSocket path (default
`/v1.0/harbor/connect`) over WSS and keeps that connection open. Every frame is a single JSON message; the
Admiral pushes host work down the link and the Harbor streams output, exit codes, and git results back up.
The connection is authenticated on the upgrade with the Harbor's credential, re-validated for the life of
the link, and torn down if that credential, user, tenant, or session is disabled. If the link drops while
jobs are running the captain processes keep running on the host, and the Harbor's next handshake and
heartbeat let the Admiral rebind them rather than orphaning the work.

The full contract -- message types, framing, authentication, and the connect/run/delegate sequences -- is
specified in [HARBOR_PROTOCOL.md](HARBOR_PROTOCOL.md). Read that document if you are implementing either
side of the link or debugging a connection.

## Installing and running the Harbor app

The host runner ships as `src/Armada.Harbor`, an Avalonia desktop app. Build and run it on the machine where
your repositories and agent logins live:

```bash
dotnet run --project src/Armada.Harbor
```

On first run the app writes a settings file to `~/.armada-harbor/settings.json` (on Windows,
`%USERPROFILE%\.armada-harbor\settings.json`), generating a Harbor id and defaulting the name to the machine
name. Edit that file to point the Harbor at your Admiral and to describe what the host can do:

| Field | Description |
|---|---|
| `serverLinkUrl` | WebSocket URL of the Admiral's Harbor link (`ws://` or `wss://`). Default `ws://127.0.0.1:7891/v1.0/harbor/connect`. |
| `dashboardUrl` | Dashboard URL opened by the app's "Open Dashboard" action. Default `http://127.0.0.1:7890/dashboard`. |
| `harborId` | Harbor identifier (`hbr_` prefix). Generated on first run when empty. |
| `name` | Human-facing Harbor name. Defaults to the machine name. |
| `capabilities` | Runtimes and host tools advertised at handshake (e.g. `git`, `claude`). Drives capability-based routing. |
| `maxConcurrentJobs` | Maximum concurrent jobs this Harbor will accept. Default 4. |
| `heartbeatIntervalMs` | Heartbeat interval in milliseconds; `0` disables heartbeats. Default 15000. |
| `accessKey` / `secret` | Credential material presented on the link. Leave empty for an unauthenticated local link; the secret is never logged. |

A Harbor does not have to be pre-registered: it self-registers on its first handshake. Pre-registering is
useful when you want to reserve a name and capacity, or set routing preferences, before the host connects.

## Managing Harbors

Harbor registrations are managed through the same REST and MCP surfaces as the rest of Armada.

Over REST, the routes live under `/api/v1/harbors` -- list, register, read, update, delete, and
enable/disable -- and are documented with request and response shapes in
[REST_API.md](REST_API.md#harbors). Only `name`, `maxConcurrentJobs`, and `enabled` are operator-editable;
everything else (`connectionStatus`, `lastSeenUtc`, `protocolVersion`, `osPlatform`, `architecture`) is
reported by the link and preserved server-side.

Over MCP, the tools are `get_harbor`, `create_harbor`, `update_harbor`, `delete_harbor`, and
`set_harbor_enabled`, and the `enumerate` tool accepts an entityType of `harbors` for paginated browsing.
See [MCP_API.md](MCP_API.md) for the tool schemas.

Disabling a Harbor is a soft off-switch: it keeps its docks and any running work, but the router stops
sending it new missions. Deleting a Harbor removes the registration entirely.

## Dock affinity and routing

A dock is a git worktree, and a worktree exists on exactly one host's filesystem. That single fact drives
how the router assigns work. When a mission first needs a dock, the router picks a Harbor for it; from that
point on the mission is pinned to that Harbor, because its checkout, its branch, and its later git
operations all live there. A mission cannot hop hosts mid-flight. If its owning Harbor goes offline, the
mission's host operations queue and resume when the Harbor reconnects rather than being re-routed elsewhere.

For a mission that does not yet own a dock, the router chooses among the registered Harbors in a fixed
order:

1. **Affinity.** If the mission already owns a dock, it goes to the Harbor that provisioned it -- no other
   Harbor is considered.
2. **Preference.** An eligible Harbor named by `vessel.preferredHarborId` wins.
3. **Capability.** A Harbor is eligible only if it is enabled, connected, under its `maxConcurrentJobs`
   capacity, and advertises the requested runtime plus every capability in `vessel.requiredCapabilities`.
4. **Least load.** Among the remaining eligible Harbors, the least loaded one is chosen, with larger
   remaining headroom and then name used as deterministic tie-breakers.

If nothing is eligible, the router holds the work with a specific reason -- no connected Harbor, no Harbor
with the required capabilities, or all capable Harbors at capacity -- so the mission starts as soon as a
slot or a host becomes available.

## Status

The management surface and the host-runner app exist today: the `Harbor` entity and its persistence, the
REST and MCP management APIs, the multi-Harbor router with dock affinity, the wire-protocol contract, and
the `Armada.Harbor` app. The live split-mode link transport -- the server-side WebSocket endpoint that
accepts Harbor links, credential authentication on the upgrade, and remote captain-process delegation -- is
still being rolled out. Until it lands, Local mode remains the supported way to run Armada, and the Harbor
management APIs let you register and shape the topology ahead of the transport going live.
