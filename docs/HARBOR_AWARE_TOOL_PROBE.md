# Harbor-Aware Runtime Tool Probe

The dashboard's "Ask Armada" page shows a warning when the selected captain is "not connected to Armada
over MCP" -- computed from `CaptainToolAccessResult.ArmadaToolCount <= 0`. The probe that produces that
number (`CaptainRuntimeToolCatalogService`) runs entirely on the **Admiral** host: it reads the runtime's
config files from the Admiral filesystem and runs the runtime CLI (e.g. `mux probe`) as a local process.

That is correct for standalone mode, where the captain runs on the Admiral. It is **wrong in split mode**,
where the captain runs on a **Harbor** (a remote host runner). In split mode the runtime CLI, its config
directory, and its provider auth all live on the Harbor -- so a local probe inspects the wrong machine,
fails outright (the Admiral may not even have the CLI installed), and the captain is reported as "not
connected" regardless of its real configuration.

This document describes the fix: route the probe through the existing host-command seam so it runs where
the captain actually runs.

## The existing plumbing (already built)

Armada already has a general "run a host command where the captain lives" seam:

- **`IHostCommandExecutor`** -- `RunAsync(HostCommandRequest) -> HostCommandResult` (exit code, stdout,
  stderr, timed-out). `HostCommandRequest` carries `Executable`, `Arguments`, `WorkingDirectory`,
  `StandardInput`, and `TimeoutMs`.
- **`LocalHostCommandExecutor`** -- runs the command in-process on the Admiral. It sets
  `ProcessStartInfo.FileName = request.Executable` with `UseShellExecute = false` and **does not restrict
  the executable** to git/gh; the "git/gh" naming is conventional only.
- **`RemoteHostCommandExecutor`** -- targets one Harbor by id and ships the identical request over the link
  as a `HarborGitRequest`, awaiting a correlated `HarborGitResult`.
- On the Harbor side, **`HarborLinkClient`** receives the `HarborGitRequest` and executes it through its own
  `IHostCommandExecutor` (a `LocalHostCommandExecutor` on the Harbor), then replies with the captured
  output.

So "run an arbitrary CLI on the Harbor and read its stdout/stderr/exit code" is a solved, tested capability
(`RemoteHostCommandExecutorSuite`, `HarborProtocolSuite`). The REST endpoint `POST /api/v1/harbors/{id}/probe`
already exposes it for one-off commands. **No protocol change is needed** to run a runtime CLI on a Harbor.

### Where a captain's Harbor is recorded

A captain runs in a dock (`captain.CurrentDockId`). `Dock.HarborId` is the id of the Harbor that owns the
dock (null when the dock is Admiral-local), and `Dock.WorktreePath` is the working directory on that host.
`HarborConnectionManager.IsConnected(harborId)` reports whether the link is currently up. Together these
resolve which executor to use and what working directory to run in.

## Design

Add an optional `HarborConnectionManager` to `CaptainRuntimeToolCatalogService`. For a captain being probed,
resolve a host context:

```
if HarborConnectionManager present
   and captain.CurrentDockId -> dock
   and dock.HarborId set
   and HarborConnectionManager.IsConnected(dock.HarborId):
       executor        = RemoteHostCommandExecutor(manager, dock.HarborId)
       workingDirectory = dock.WorktreePath      // path on the Harbor host
       isRemote         = true
else:
       executor        = LocalHostCommandExecutor()
       workingDirectory = (local context dir)
       isRemote         = false
```

The runtime CLI is then executed through `executor`. The local path is byte-for-byte the previous behavior,
so standalone mode is unaffected.

## Status

### Implemented: Mux

- `MuxCliService` now takes an optional `IHostCommandExecutor` (defaults to `LocalHostCommandExecutor`) and
  runs every `mux` invocation through it, with an optional working directory. Existing callers are
  unchanged (they get the local executor).
- `CaptainRuntimeToolCatalogService.DescribeMuxAsync` resolves the host context and, when the captain runs
  on a connected Harbor, runs `mux probe` on that Harbor. `MuxProbeResult` already reports `BuiltInToolCount`,
  `McpConfigured`, `McpServersFilePresent`, and `McpServerCount` computed from the host where mux runs, so
  the built-in tool count and MCP-configuration signal are now accurate in split mode.
- Coverage: `Services.MuxCliService` (executor-seam behavior) plus the unchanged
  `Services.RemoteHostCommandExecutor` / `Services.HarborConnectionManager` suites.

Known limitation (see below): in split mode the probe reports *whether* MCP is configured on the Harbor and
*how many* servers there are, but it does **not** enumerate individual MCP tools, so it cannot confirm the
Armada MCP server specifically. `ArmadaToolCount` is therefore left at `0` (unknown) for remote Mux rather
than inferred from the raw server count, and the "Ask Armada" banner is not yet suppressed on that basis.

### Not yet done: OpenCode

OpenCode has no probe branch at all today (it falls through to `unsupported-runtime`). Adding one needs one
piece of verification this document cannot settle from the Admiral: whether the `opencode` CLI has a
subcommand that dumps its MCP servers/tools (e.g. `opencode mcp ...`). If it does, run it over the seam; if
not, the fallback is to read OpenCode's config over the seam. `OpenCodeRuntime.cs` currently contains no
MCP-config handling, so how an OpenCode captain is wired to the Armada MCP server should be confirmed first.

## The remaining gap: per-server tool enumeration over the link

Fully flipping the "connected to Armada" signal in split mode requires enumerating the *tools* each MCP
server exposes (to identify the Armada server by its `armada` registration source). Today that enumeration
runs from the Admiral: it reads `mcp-servers.json` locally and opens an MCP session to each server (spawning
stdio servers as local child processes, or connecting to HTTP URLs). In split mode:

- the config file is on the Harbor,
- stdio MCP servers must be spawned on the Harbor,
- HTTP MCP servers may only be reachable from the Harbor's network.

The one-shot `HarborGitRequest`/`Result` exec model is enough to *read a config file* on the Harbor, but not
to hold an interactive MCP stdio session. Closing the gap therefore needs one of:

1. **MCP-over-link proxying** -- a new Harbor message pair that opens/streams an MCP session on the Harbor
   and relays JSON-RPC frames, so the Admiral's existing MCP client can probe Harbor-side servers. This is
   the complete fix and the largest.
2. **A Harbor-side enumerator** -- have the Harbor itself probe the configured MCP servers and return a tool
   inventory in one shot. Smaller, but duplicates MCP-client logic on the Harbor.
3. **Config-only inference** -- read `mcp-servers.json` (or equivalent) over the link and mark the captain
   "connected to Armada" when the Armada MCP server URL is present, without live tool enumeration. Cheapest;
   trades a live check for a config check.

Until one of these lands, split-mode captains get an accurate built-in-tool and MCP-configured signal, and
an honest summary noting that per-server tool visibility is not yet available over the Harbor link.

## Related dashboard change

Independently, the "Ask Armada" banner was made correct for `ApiEndpoint` captains. The probe now
classifies `ApiEndpoint` intentionally instead of falling through to `unsupported-runtime`, and the
dashboard shows an accurate note for it rather than the inapplicable "add the Armada MCP server to this
captain's runtime config" warning.

Update: `ApiEndpoint` captains run Armada's built-in coding tools in-process, and, in an Ask Armada chat,
they now also act as an MCP client against Armada's own `/mcp` endpoint -- the runtime is handed the local
MCP URL plus a short-lived per-caller session token and merges the discovered Armada tools into its
tool-calling loop, scoped to the asking user. The dashboard note reflects this (it no longer states that
orchestration tools are unavailable to `ApiEndpoint` captains in chat).
