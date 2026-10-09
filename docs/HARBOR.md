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

Split mode is **experimental** in 1.0 (see [Status](#status)). The Admiral runs detached -- in Docker or on another host -- and one or
more Harbors run on the machines where the code and tool logins live. The Admiral routes each unit of host
work to a Harbor over the link, the Harbor executes it locally, and results stream back. Split mode is what
lets a single containerized Admiral drive agents across several developer machines at once.

## How the link works

The Harbor is the client. It dials the Admiral at a configured WebSocket path (default
`/v1.0/harbor/connect`) over WSS and keeps that connection open. Every frame is a single JSON message; the
Admiral pushes host work down the link and the Harbor streams output, exit codes, and git results back up.
The Harbor's credential is checked when its handshake arrives; a refused Harbor gets a `handshakeAck` with
`accepted: false` and a reason, and the link closes. The credential is not re-checked for the life of an
accepted link. If the link drops, the Harbor app retries every few seconds and the captain processes keep
running on the host. See [Harbor disconnects](#harbor-disconnects) for what happens to their missions.

The full contract -- message types, framing, authentication, and the connect/run/delegate sequences -- is
specified in [HARBOR_PROTOCOL.md](HARBOR_PROTOCOL.md). Read that document if you are implementing either
side of the link or debugging a connection.

## Installing and running the Harbor app

For a Harbor on another machine than the Admiral (TLS, the `wss://` link URL, credentials, and the advertised MCP URL), see [REMOTE_SERVER.md](REMOTE_SERVER.md#harbors-on-other-machines).

The host runner is `src/Armada.Harbor`, an Avalonia tray (menu bar) app. Install it on the machine where your
repositories and agent logins live:

| Platform | Package | Start at login |
|---|---|---|
| macOS | `armada-harbor-<version>-osx-arm64.dmg` (or `-osx-x64`): drag **Armada Harbor** to Applications | `"/Applications/Armada Harbor.app/Contents/MacOS/Armada.Harbor" --install-startup` (once) |
| Windows | `armada-harbor-<version>-win-x64.exe` (Inno Setup) | Registered by the installer |
| Linux | `armada-harbor` `.deb` / `.rpm` | `armada-harbor --install-startup` (as yourself) |
| Any, from source | `scripts/<os>/run-harbor.sh` (or `scripts\windows\run-harbor.bat`), which builds and runs `src/Armada.Harbor`; or `dotnet run --project src/Armada.Harbor` | -- |

On macOS, Harbor is a menu bar app (`LSUIElement`): started from the login item it shows only its menu bar icon;
opening its window (click the icon, or launch it from Applications) adds a Dock icon and app menu, and closing the
window removes them again while the runner keeps working. Quit it from the menu bar icon. Until the release is signed
with a Developer ID and notarized, Gatekeeper blocks the first open; see "Signing" in
[BUILDING_INSTALLERS.md](../BUILDING_INSTALLERS.md). `--install-startup`, `--uninstall-startup`, and `--dry-run` are
described in [OPERATIONS.md](OPERATIONS.md#harbor---install-startup---uninstall-startup).

On first run the app writes a settings file to `~/.armada-harbor/settings.json` (on Windows,
`%USERPROFILE%\.armada-harbor\settings.json`), generating a Harbor id and defaulting the name to the machine
name. Change these settings in the app (**Settings > General**, see
[Managing Harbor and Armada from the app](#managing-harbor-and-armada-from-the-app)), which validates them, keeps a
backup of the previous file, and reconnects when a link setting changes. To edit the file by hand, use **Open File** on
that tab and then **Reload from File**, or edit it with Harbor stopped (it rewrites the file on startup). Keys are
PascalCase and case-sensitive: a camelCase key is ignored and replaced with the default when Harbor saves.

| Field | Description |
|---|---|
| `ServerLinkUrl` | WebSocket URL of the Admiral's Harbor link (`ws://` or `wss://`), on the Admiral's REST port. Default `ws://127.0.0.1:7890/v1.0/harbor/connect`. |
| `DashboardUrl` | Dashboard URL opened by the app's "Open Dashboard" action. Default `http://127.0.0.1:7890/dashboard`. |
| `HarborId` | Harbor identifier (`hbr_` prefix). Generated on first run when empty. |
| `Name` | Human-facing Harbor name. Defaults to the machine name. |
| `UserId` / `TenantId` | Sent at connect as `x-user-guid` and `x-tenant-guid`. The Admiral does not read `x-user-guid`: the owning user always comes from the `AccessKey` credential (none without one), so set `AccessKey` to one of your credentials when the Harbor must count as yours for `requireHarborForLaunch`. `TenantId` applies only to a credential-less loopback Harbor or when the credential is a global admin; otherwise the credential's tenant applies. |
| `Capabilities` | Runtimes and host tools advertised at handshake (e.g. `git`, `claude`). Default `["git"]`. Drives capability-based routing. |
| `Appearance` | Window color scheme: `System` (default), `Light`, or `Dark`. |
| `ShowActivityDetails` | Whether the main window's Activity log shows every request and result with request IDs and full paths (`true`) or the summary (`false`, the default). Set by the Activity log's **Show details** box. |
| `MaxConcurrentJobs` | Maximum concurrent jobs this Harbor will accept, advertised at handshake. Default 4. |
| `HeartbeatIntervalMs` | Heartbeat interval in milliseconds; `0` disables heartbeats. Default 15000. |
| `AccessKey` / `Secret` | `AccessKey` is an Armada credential (a bearer token from Server > Credentials, or the local API key); the Harbor registers under that credential's tenant and user. Leave it empty only for a Harbor on the same machine as a localhost-bound Admiral; a Harbor connecting from another host is refused without one. `Secret` is sent as `x-secret-key` but not used for authentication today, and is never logged. |

A Harbor does not have to be pre-registered: it self-registers on its first handshake. Pre-registering is
useful when you want to reserve a name and capacity, or set routing preferences, before the host connects. A Harbor id
that is already registered to a different tenant or user is refused.

### Managing Harbor and Armada from the app

The app has three windows, and every entry point opens the same ones:

- **Armada Harbor** (the main window): the connection state as the header (**Connected**, **Connecting**,
  **Disconnected**, or **Error**, with the Admiral's address and a plain-language detail), one **Connect** /
  **Disconnect** button, **Running now** (each job on this machine: a mission or an Ask turn, its runtime, and how long
  it has run, with how many of `MaxConcurrentJobs` slots are in use), and **Activity** (the link's recent work, with
  Show details, Copy, Clear, and All Logs; see **The Activity log** below). **Dashboard**, **Status**, and
  **Settings** are in the header. Closing the window keeps Harbor running in the tray.
- **Armada Harbor - Status**, with an **Overview** tab and a **Logs** tab.
- **Armada Harbor - Settings**, with **General**, **Repositories**, and **Admiral** tabs.

The menus are in the macOS menu bar while a Harbor window is in front (the app menu is **Armada Harbor**); on Windows
and Linux they are in a menu bar at the top of each Harbor window. The tray (menu bar) icon offers the windows and the
connection too, with the link status at the top, so nothing needs a window open.

| Menu | Items |
|---|---|
| **Armada Harbor** (macOS) | About Armada Harbor, Settings... (Cmd+,), and the standard Hide and Quit |
| **Harbor** | Connect, Disconnect, Reconnect (Cmd/Ctrl+R), Copy Harbor ID, Copy MCP URL, Open Harbor Folder (on Windows and Linux also Settings... (Ctrl+,) and Quit (Ctrl+Q)) |
| **View** | Status (Cmd/Ctrl+I), Logs (Cmd/Ctrl+L), Open Dashboard (Cmd/Ctrl+D) |
| **Window** (macOS) | Minimize, Close, Armada Harbor |
| **Help** | Armada Documentation, Copy Diagnostics (versions, paths, link state, recent activity; credentials are reported only as configured or not), and About on Windows and Linux |
| Tray | Link status, Open Armada Harbor, Status, Logs, Settings..., Connect, Disconnect, Open Dashboard, Quit Armada Harbor |

The Status window:

| Tab | What it does |
|---|---|
| **Overview** | This computer (Harbor name and id, connection, MCP URL, Harbor's log file, and the jobs running now), **Last 24 hours** (three small charts of this Harbor from the Admiral: jobs, slot usage against the capacity, and link health, each with a one-line caption; **Open Activity** opens the full charts; refreshed every 60 seconds while shown), the Admiral server's health, version, uptime, and ports (refreshed every 5 seconds while shown), captains, missions, and voyages, and, when the Admiral is on this machine, the disk used by each item in the Armada data directory. |
| **Activity** | This Harbor's charts from the Admiral (see [Harbor metrics](#harbor-metrics)) over the last hour, 24 hours, or 7 days: totals (finished, failed, running, peak slots, share of time connected, median round trip), **Jobs over time** (stacked bars: missions finished and failed, interactive finished and failed), **Slot usage** (peak and average concurrent jobs against a dashed line at the capacity; the value axis tops out at the capacity, `MaxConcurrentJobs`, with whole-number ticks), **Link health** (a strip of connected, reconnecting, and down stretches, disconnects, the reconnects Harbor reported, and the heartbeat round trip), **Token usage** (totals, stacked bars of tokens by type, and a table of totals per runtime and model; see below), and **Launch speed** (per runtime: jobs, median and p95 time to first output and total runtime, and a trend sparkline). Token bars stack uncached input, cached input, and output, and hovering a bar shows each part and the total. Recorded input already includes cache reads (Claude Code's input is input plus cache read plus cache creation; Codex's cached input is part of its input), so uncached input is input minus cached and each bar adds up to input plus output, the recorded total, without counting cached tokens twice. Refreshes every 30 seconds while the tab is shown and stops when it is hidden. |
| **Logs** | Always: **Harbor** (Harbor's own log) and **Jobs on this machine** (the output of every job this Harbor ran, see below). When the Admiral runs on this machine, also its groups: **Admiral: Server log**, Missions, Captains, Diffs, Instructions, Final messages, and Docks. **Open a mission's log** takes a mission (`msn_`) id, or a captain (`cpt_`) id for a local Admiral: the Admiral's log when it is local (it holds the whole transcript), else this machine's job log. The viewer shows the last 256 KB of a file, follows it as it grows, filters by level (continuation lines such as stack traces stay with their line), and finds text. |

The Settings window:

| Tab | What it does |
|---|---|
| **General** | This machine's Harbor settings in `~/.armada-harbor/settings.json`: name and id, the connection to the Admiral (address, dashboard address, access key, secret, tenant and user ids), work (jobs at once, tools offered, heartbeat), and the color scheme (follow the system, light, or dark; applied at once and kept on Save). Save validates the values, writes the file atomically keeping the previous version as `settings.json.bak-<timestamp>`, and reconnects the link when a connection setting changed. |
| **Repositories** | Where this machine keeps vessel checkouts and mission docks (see [Where docks live](#dock-affinity-and-routing)): **Vessel checkouts** (a vessel name or ID and its checkout folder, which must contain `.git`), **Root folders** searched for checkouts by remote URL, the **Docks folder** (default `~/.armada-harbor/docks`, not inside a checkout), and the **Clones folder** (default `~/.armada-harbor/repos`). Saved in `settings.json` as `Repositories`, `RepositoryRoots`, `DocksDirectory`, and `ReposDirectory`; validated on save and applied to new missions without reconnecting. |
| **Admiral** | The Admiral's own settings, not this machine's. **Change live**: the common Admiral settings (captain limits, heartbeat and stall timing, planning-session timeouts, landing mode, ports), read from and applied to the running Admiral through `GET`/`PUT /api/v1/settings`, so they take effect at once (ports after a restart). **Edit file (settings.json)**, when the Admiral is on this machine: its whole `settings.json` as JSON. Save refuses text the Admiral would not load (with the line and column of a syntax error, or the setting that is out of range), asks before overwriting a file that changed on disk, and keeps the previous version as a backup. The Admiral reads this file only at startup, so the tab offers **Restart Admiral** after a save. Until it restarts, a settings change made from the dashboard or Change live rewrites the file from the running settings and discards edits made here. **Restart Admiral** requires administrator privileges (see below). |

**IDs and URLs.** Every ID Harbor shows (the Harbor ID, mission, captain, and job IDs in Running now and Logs, tenant
and user IDs in General, and the Harbor ID in Status and About) is in a fixed-width font with a copy button beside it;
the button copies the ID and turns into a green check for about a second and a half. Every URL field (the Admiral
address and dashboard address in General, the Admiral address, MCP URL, and REST address in Status, and the link URL
in About) has a **Validate** button. It tests the URL from this computer, one stage at a time: DNS (skipped for an IP
address), the TCP connection, TLS for `https://` and `wss://` (the certificate must be trusted and match the host),
then an HTTP `GET` for `http(s)://` or a WebSocket upgrade for `ws(s)://`. It shows each stage with its time and a
success or failure reason (for example "Connection refused ... nothing is listening on that port"). It never sends
credentials: no access key, secret, API key, or cookie, and a user name or password in the URL is ignored. A server
that answers 401 or 403 is reported as reachable and asking for credentials. The test gives up after 10 seconds, and
Cancel stops it. The probe is `Armada.Core.Connectivity.UrlProbe`.

**Restarting the Admiral requires an administrator.** Restart Admiral sends `POST /api/v1/server/restart`, which the
Admiral allows only for a global administrator (the local API key, or a user with `IsAdmin`); a tenant administrator or
an ordinary user gets 403 and nothing restarts. Harbor asks the Admiral up front (`GET /api/v1/whoami`) who its
credential belongs to: when it is not an administrator, or the Admiral cannot be reached, the button is disabled and
the reason is shown under it. The confirmation says "This operation requires administrator privileges", and Harbor
checks again just before sending the request.

**Restore Previous Version.** General and the Admiral's settings file each have **Restore Previous Version...**, which
lists the kept versions of that file (newest first, with when each was replaced) and, after you confirm, puts the
chosen one back. The restore is an ordinary save: the version is validated first, and the current file is kept as a new
backup, so a restore can be undone the same way. Harbor keeps the newest 3 versions of its own `settings.json` and the
newest 5 of the Admiral's. Harbor does not show, restore, or delete the Admiral's database backups (`backups/` in the
data directory); see [UPGRADING.md](UPGRADING.md#backups-during-normal-operation).

**The terminal UI's settings** are not edited by Harbor. `armada tui` keeps them in `~/.armada/tui.json` (or the file
`ARMADA_TUI_PREFERENCES` names) and changes them from its own menus; see [TUI.md](TUI.md).

**Which Admiral's files.** The Armada data directory is `ARMADA_DATA_DIR`, or `~/.armada`. Harbor treats it as the
linked Admiral's only when the link URL points at this machine (`localhost` or a loopback address) and the directory
exists. Otherwise (a Harbor linked to an Admiral on another host) the Admiral's data folder, settings file, and logs are
unavailable and say so; Harbor's own log and its jobs' logs are always available, and Status and **Change live** still
work over the network.

**Authorization.** Status and Change live call the Admiral's REST API at the link URL's host and port (`ws://` becomes
`http://`, `wss://` becomes `https://`). For a same-machine Admiral, Harbor uses the local API key from its
`settings.json`; otherwise it uses the Harbor's `AccessKey`. Reading and changing settings and restarting need an admin
credential, the captain and mission counts need any credential, and the health line needs none.

**The Activity log.** The main window's Activity log tells the story at a glance by default (the summary view):

- A request and its result are one line without the request ID, for example
  `Check run Build in DocConverter: /bin/sh -lc dotnet build -> exit 0`, and a launch and its start are one line,
  `Mission msn_mv092791 started in DocConverter (ClaudeCode)`.
- Routine git and file work is not shown one command at a time. A run of it in the same dock or checkout is one line
  that counts the commands in place, as heartbeats do: `DocConverter msn_mv092791: prepared dock (14 git commands, 2 file
  writes)` before the job starts, `worked in dock` while it runs, and `landed and cleaned up (6 git commands)` after it
  exits. A line for the dock, its job, or a failure ends the run; later work starts a new line.
- Paths are short: a dock is `DocConverter/msn_mv092791...`, a vessel's checkout is the vessel's name, a job's scratch
  directory is `scratch`, and the home folder is `~`.
- A failure always gets its own line, even inside a run, with the command and its exit code (`git push -u origin HEAD
  -> failed (exit 128)`). A non-zero exit that the Admiral declared an expected answer (for example 1 from `git grep`
  or `git show-ref --verify --quiet` for no match, or from `git ls-files --error-unmatch` for an untracked file) is not
  a failure.
- Shown: connecting, the handshake, settings saved, job launches, starts, and exits (`Mission msn_mv092791 exited ok in
  36.7s (first output 36.4s)`, `Ask turn started (ClaudeCode)`), docks made and removed, check runs and other commands
  an operator asked for, failures, and heartbeats (consecutive heartbeats collapse into one line with a count).

**Show details**, in the Activity header, shows every entry as logged instead: each request and result, with request
IDs and full paths. Harbor remembers the choice (`ShowActivityDetails` in `settings.json`). Copy copies the view shown;
Help > Copy Diagnostics always includes the detailed lines. Harbor's log file always has full detail. Entries are
classified by typed fields set where they are logged (a level, a category, an outcome, the request ID that pairs a
request with its result, and the vessel, mission, job, and directory they are about), never by reading the message; the
Admiral declares what a command is for and which exit codes are answers in the `git` message (see
[HARBOR_PROTOCOL.md](HARBOR_PROTOCOL.md)).

**Harbor's logs.** The link, the job runner, and the activity log are written to `~/.armada-harbor/logs/harbor.log.<date>`
(one file per day, like the Admiral's `admiral.log.<date>`). Each job's output (stdout, stderr lines marked `[stderr]`,
and the exit code, but not the prompt) is written to `~/.armada-harbor/logs/jobs/`: a mission's runs append to
`<missionId>.log`, and every other job gets its own file named for what it is, its captain, and when it started (for
example `ask-cpt_abc-20261008T093000Z.log`). The newest 200 job logs are kept. The Admiral tells the Harbor what each
launch is through the optional `jobKind`, `missionId`, and `captainId` fields of the `launch` message (see
[HARBOR_PROTOCOL.md](HARBOR_PROTOCOL.md)).

### Admiral settings

The Admiral side is configured in `settings.json`. The Harbor link endpoint is always registered; there is no switch
that turns split mode on or off. Whenever at least one Harbor is connected, each captain launch is routed to an eligible Harbor when there is one (see
[Dock affinity and routing](#dock-affinity-and-routing)).

| Key | Default | Description |
|---|---|---|
| `harbor.linkPath` | `/v1.0/harbor/connect` | WebSocket path Harbors connect to, on the REST port. Restart required. |
| `harbor.requireAuth` | `false` | When true, every Harbor must present a credential. When false, a Harbor without a credential is still accepted only if the Admiral listens on a loopback hostname and the Harbor connects from loopback. |
| `harbor.advertisedMcpBaseUrl` | Derived: `http://<rest.hostname>:<mcpPort>/mcp` (`localhost` when listening on all interfaces) | MCP URL sent to Harbors in `handshakeAck` so captains can call home. When the file leaves it empty, the Admiral derives it from its own MCP port and hostname and writes it to `settings.json` at startup, which is right in Local mode; in split mode change it to a URL the Harbor host can reach. Ask turns and other interactive launches on a Harbor also bind their token to this URL, so it must be a plain `http://host:port/mcp` URL the Harbor host can reach (for an Admiral in Docker, use the published host port, not the container's 7891). Left at the derived value with a Harbor on another machine, captains get the Admiral's `localhost` URL and report that the Armada MCP server failed to connect. |
| `requireHarborForLaunch` | `false` | When true, a mission is assigned only while an eligible Harbor owned by the mission's user is connected; it stays Pending otherwise and never runs on the Admiral host or another user's Harbor. A Harbor's owner comes only from its `AccessKey` credential: a credential-less loopback Harbor has no owner and never counts for a mission that has a user. |
| `deploymentMode` | `Local` | `Local` or `Split`. Reserved and informational in 1.0: nothing reads it. Routing to a Harbor happens whenever one is connected; `requireHarborForLaunch` is what keeps captains off the Admiral host. |

`harbor.defaultMaxJobsPerHarbor` (4) is the capacity given to a Harbor that registers through its handshake without
advertising `maxConcurrentJobs` (omitted, or not positive); an existing registration keeps its capacity in that case.
The Harbor app always advertises its `MaxConcurrentJobs`, so the default matters only for other link clients.
`harbor.heartbeatIntervalSeconds` (15) is reserved: accepted and validated but not read in 1.0.
`harbor.heartbeatTimeoutSeconds` (45) is not a liveness check either: a Harbor is marked `Disconnected` when its link
closes, not on a missed heartbeat, and nothing marks a Harbor `Degraded`. It is how long a closed link counts as
reconnecting in the [Harbor metrics](#harbor-metrics) link timeline before it counts as down.

## Managing Harbors

Harbor registrations are managed through the same REST and MCP surfaces as the rest of Armada.

Over REST, the routes live under `/api/v1/harbors` -- list, register, read, update, delete,
enable/disable, and `POST /api/v1/harbors/{id}/probe`, which runs a one-off host command (default `git --version`) on a
connected Harbor over its link -- and are documented with request and response shapes in
[REST_API.md](REST_API.md#harbors). Only `name`, `maxConcurrentJobs`, and `enabled` are operator-editable;
everything else (`connectionStatus`, `lastSeenUtc`, `protocolVersion`, `osPlatform`, `architecture`) is
reported by the link and preserved server-side.

Over MCP, the tools are `get_harbor`, `get_harbor_metrics`, `create_harbor`, `update_harbor`, `delete_harbor`, and
`set_harbor_enabled`, and the `enumerate` tool accepts an entityType of `harbors` for paginated browsing.
See [MCP_API.md](MCP_API.md) for the tool schemas.

Disabling a Harbor is a soft off-switch: it keeps its docks and any running work, but the router stops
sending it new missions. Deleting a Harbor removes the registration entirely, with its metrics.

## Harbor metrics

The Admiral keeps the numbers behind each Harbor's charts, so the dashboard, the mobile app, and any `Armada.Client`
consumer show the same data. `GET /api/v1/harbors/{id}/metrics?range=1h|24h|7d` (MCP `get_harbor_metrics`,
`ArmadaClient.GetHarborMetricsAsync`) returns, over one set of buckets (1h in 1-minute buckets, 24h in 30-minute
buckets, the default, and 7d in 3-hour buckets, aligned to the UTC clock and ending with the bucket that holds now):

| Series | What it shows | Source |
|---|---|---|
| `jobs` | Jobs finished and failed per bucket, by when they ended, with missions apart from every other launch (Ask turns and captain chat, planning, refinement, context builds). Succeeded and Stopped count as finished; Failed and Lost as failed. Also the number running now. | `harbor_jobs` |
| `slots` | Peak and time-weighted average of concurrent jobs per bucket, against the Harbor's `maxConcurrentJobs`. A job holds a slot from its start until it ends. | `harbor_jobs` |
| `launchSpeed` | Per runtime, over the jobs that ended in the window: count, median and p95 time to first output, median and p95 total runtime, and the median time to first output per bucket (a sparkline). | `harbor_jobs` |
| `link` | A timeline of `Connected`, `Reconnecting`, `Down`, and `Unknown` stretches, the share of time connected, link drops in the window, heartbeat round trip per bucket (average and largest) and its median, and the reconnect count and last reconnect the Harbor reported. | `harbor_link_events`, `harbor_link_samples` |
| `tokens` | Tokens per bucket and over the window by runtime and model, for captains that ran on this Harbor. Armada records token counts, not prices, so there is no cost series. | `token_usage` rows with this `harbor_id` |

How the Admiral records them:

- **Jobs.** Every captain launch the Admiral sends to a Harbor gets a `harbor_jobs` row: kind (`Mission`, `AskTurn`,
  `Planning`, `Refinement`, `ContextBuild`, `Other`), runtime, model, mission, and captain, with the launch time. The
  row is updated when the Harbor reports `started` and `exited` (exit code, the Harbor's measured runtime and time to
  first output) or an `error` for the job (a refused launch fails). A job the Admiral asked to stop counts as Stopped.
  A job launched over an earlier link that the Harbor no longer lists in its first heartbeat after a reconnect is Lost.
- **Link.** Heartbeats report the round trip of the previous heartbeat and the Harbor's reconnect counters (see
  [Link health in heartbeats](HARBOR_PROTOCOL.md#link-health-in-heartbeats)); the Admiral writes one sample per Harbor
  per minute. A closed link counts as reconnecting for `harbor.heartbeatTimeoutSeconds` and as down after that. A Harbor
  that predates the link-health fields still gets the timeline, but no round trips or reconnect counts.
- **Tokens.** Token-usage records carry `harborId` for mission runs, captain chat and Ask turns, and planning turns that
  ran on a Harbor. The Token Usage APIs (`GET /api/v1/token-usage/summary`, `GET /api/v1/token-usage`, MCP
  `token_usage_summary`) filter by `harborId`, and the dashboard's Token Usage page takes `?harborId=`.

Anyone who can read a Harbor can read its metrics (the same tenant scoping as `GET /api/v1/harbors/{id}`). Token usage
follows the token-usage rules: an admin, a tenant admin, or the Harbor's owner sees every record on the Harbor in the
tenant; another user sees only their own. Harbor metrics follow `retention.jobRetentionDays` (default 30 days; 0 keeps
them): job records that ended, minute samples, and link events older than that are pruned, keeping each Harbor's latest
link event so its timeline still knows the state it opens with. The 7d range needs at least 7 days of retention.

The dashboard shows the charts on the Harbors page (pick a Harbor below the table) and in a Harbor's detail view, with
a 1h/24h/7d selector; the mobile app shows them on the Harbor detail screen; the TUI shows them under the Harbors
table for the selected Harbor (see [TUI.md](TUI.md)).

**In the Harbor app.** The Status window's Overview shows the last 24 hours in three small charts and its Activity tab
shows every series (see [the Status window](#managing-harbor-and-armada-from-the-app)). Harbor asks the Admiral for its
own ID with its own credential (the local Admiral's API key when the Admiral is on this machine, else the access key in
Settings > General). The charts are drawn by the app itself and follow the light and dark color scheme; hovering a bar,
point, or stretch shows its time and values, and each chart's accessible name summarizes what it shows (for example
"Jobs over time, last 24 hours: Missions finished 51, Missions failed 5, ..."). When the Admiral cannot answer, the tab
says why and keeps the last charts it had:

| Situation | What Harbor says |
|---|---|
| 401 | The Admiral did not accept Harbor's credential; set an access key (for a local Admiral: its `settings.json` has no API key Harbor can use). |
| 403 | The credential may not read this Harbor; use an access key of the Harbor's owner or a tenant administrator. |
| 404, and the Admiral knows the Harbor | The Admiral needs updating: it is older than this Harbor and has no metrics endpoint. |
| 404, and the Admiral does not know the Harbor | The Harbor has not connected to this Admiral yet. |
| No connection, timeout, 502, 503, 504 | Cannot reach the Admiral; the charts load again when it is back. |

## Dock affinity and routing

A dock is a git worktree, and a worktree exists on exactly one host's filesystem. That single fact drives
how the router assigns work. When a mission first needs a dock, the router picks a Harbor for it; from that
point on the mission is pinned to that Harbor, because its checkout, its branch, and its later git
operations all live there. A mission cannot hop hosts mid-flight: the router never re-routes it to another Harbor.

For a mission that does not yet own a dock, the router chooses among the registered Harbors in a fixed
order:

1. **Affinity.** If the mission's dock is already pinned to a Harbor, it goes to that Harbor -- no other
   Harbor is considered. If that Harbor is offline or no longer registered, no Harbor is chosen.
2. **Preference.** An eligible Harbor named by `vessel.preferredHarborId` wins.
3. **Capability.** A Harbor is eligible only if it is enabled, connected, under its `maxConcurrentJobs`
   capacity, and advertises the requested runtime plus every capability in `vessel.requiredCapabilities`.
4. **Least load.** Among the remaining eligible Harbors, the least loaded one is chosen, with larger
   remaining headroom and then name used as deterministic tie-breakers.

If nothing is eligible, the router returns a specific reason -- no connected Harbor, no Harbor with the
required capabilities, all capable Harbors at capacity, or the dock's Harbor is offline -- and what happens next
depends on `requireHarborForLaunch`:

- **Off (default):** the captain runs on the Admiral host, as in Local mode. The same happens when no Harbor is
  connected at all.
- **On:** the mission is not assigned until an eligible Harbor owned by the mission's user is connected; it stays
  Pending and is retried on each dispatch pass. Routing then considers only that user's Harbors, so a launch never
  lands on a shared Harbor or another user's Harbor, and if none can take it at launch time the launch is refused and
  the mission returns to Pending.

Interactive launches use the same routing: Ask Armada turns and narrations, direct captain chat, planning and
refinement turns, and vessel Model Context builds. With an eligible Harbor connected, the CLI runs on that Harbor and
its output streams back over the link. With `requireHarborForLaunch` on and none of the user's Harbors connected, the
turn fails with "No Harbor is connected to run this captain" and nothing runs on the Admiral host. Chat turns run in a
per-job scratch directory the Harbor creates and removes. Planning and refinement turns use the dock's or vessel's path
when it exists on the Harbor host, and otherwise use scratch. Model Context builds require the path to exist.
API-endpoint captains still run in-process on the Admiral, and fleet categorization always runs on the Admiral, so it
is refused while `requireHarborForLaunch` is on. See [CAPTAINS.md](CAPTAINS.md#where-interactive-turns-run).

**Where docks live.** A mission routed to a Harbor gets its dock (a git worktree) on that Harbor's machine, and the whole
mission runs there. The Admiral picks the Harbor *before* it creates the dock: routing (above) chooses a Harbor, and
the Admiral asks it whether it can serve the vessel. The Harbor answers from its own settings, because each machine
keeps code in different places:

1. **A checkout named for the vessel** in Harbor > Settings > Repositories (vessel name or ID, and the checkout's
   folder).
2. **A checkout discovered under a root folder** listed there (searched three folders deep): one whose remote URL names
   the vessel's repository URL. `https://`, `ssh://`, and `git@host:owner/repo` forms match each other; a `.git`
   suffix, a user or port, and case do not matter.
3. **Otherwise its own bare clone** of the vessel's repository URL, under `~/.armada-harbor/repos/<vessel>.git`
   (fetched before each new dock).

The dock is a worktree of that repository under the Harbor's docks folder, `~/.armada-harbor/docks/<vessel>/<mission>`
by default, never inside your code folder. Creating it from your checkout only fetches `origin` and adds the mission
branch (with no upstream) and the worktree: your checkout's files, index, current branch, and configuration are not
touched. The mission branch lives in your repository. Armada also adds its instruction file name (for example
`CLAUDE.md`) and `.armada/playbooks/` to the repository's `.git/info/exclude`, which git shares between a checkout and
its worktrees.

Everything after that runs on the Harbor too: the mission's instruction file and playbooks, the captain, the
Definition-of-Done gate (through `/bin/sh -lc`), diff and commit capture, landing, and removing the dock (`git worktree
remove`, then `prune`). Landing uses your checkout as the vessel's working directory on that machine: **LocalMerge**
merges the mission branch into it, **MergeAndPush** then pushes it, and **PullRequest** pushes the branch and runs `gh`
from the dock. As with a working directory on the Admiral, a merge into a checkout with uncommitted changes is refused
(the mission becomes `LandingFailed` and the branch is kept). A vessel served from the Harbor's own clone has no
checkout to merge into, so LocalMerge and MergeAndPush leave the work on the branch (`WorkProduced`); use PullRequest
landing or map the vessel to a checkout. MergeQueue landing still runs on the Admiral and does not see Harbor-side
branches.

If no connected eligible Harbor can serve the vessel, the earlier behavior applies: with `requireHarborForLaunch` on the
mission waits (Pending) and the Admiral logs why, for example "Harbor Mac (hbr_...) cannot serve vessel app: it has no
checkout of vessel app, and the vessel has no repository URL to clone; set the checkout's path in Harbor > Settings >
Repositories or add a root folder that contains it"; with it off the dock is created on the Admiral host, under its own
`docksDirectory`. A Harbor that cannot create a dock (for example a clone it has no credentials for) fails provisioning
with its reason, and the mission returns to Pending.

A dock on a Harbor stays there: later pipeline stages, retries, and rework on the same branch wait for that Harbor,
and a dock on a Harbor never runs on the Admiral host or another Harbor. Harbors from before Harbor-side docks (they do
not advertise the `harbor-docks` capability) are never asked; with only such Harbors the dock is created on the Admiral,
and before a launch goes to a Harbor the Admiral asks it (`git -C <dock> rev-parse --git-dir`) whether the dock exists
there. A Harbor that does not have it never receives the launch; with `requireHarborForLaunch` off the captain runs on
the Admiral host, and otherwise (or with the captain's CLI missing there) the mission fails once (`Infra`) with a reason
naming the Harbor and the dock path. Ask Armada turns and chat are unaffected: they run in Harbor scratch directories.

A dock that is already pinned to a Harbor never moves to another Harbor. If that Harbor is offline or no longer
registered, a relaunch of a dock created on the Admiral runs on the Admiral host when `requireHarborForLaunch` is off (a
warning names the pinned Harbor) and is refused when it is on; a dock created on the Harbor waits for it.

## Checkouts outside missions

Missions are not the only work that needs a vessel's code. Check runs (including the ones Ask Armada starts with
`run_check`), Workspace (browse, read, save, search, exec, changes, and diff), readiness, vessel health, the vessel's
git status and branch, history, push, and merge views, the manual-landing check, and fleet categorization all work in
the vessel's checkout. Armada finds it the same way for all of them:

1. **The Admiral host**, when the vessel's working directory exists there (as before; also when a Harbor on the same
   machine could serve it).
2. Otherwise **a connected Harbor that has a checkout of the vessel**: a folder named for it in Harbor > Settings >
   Repositories, or one discovered under a root folder by its remote URL. The Admiral asks Harbors as it does for a
   mission dock (`Resolve`), and uses the first that reports a checkout: the Harbor that owns the vessel's most recent
   docks, then the vessel's preferred Harbor, then the others by name. A Harbor's own bare clone does not count: it has
   no working tree. Harbors are scoped as launch routing scopes them: the vessel's tenant or a shared Harbor, and with
   `requireHarborForLaunch` only the requesting user's Harbors. A Harbor's answer is remembered for 30 seconds.

On a Harbor, commands run there in the checkout (a check runs through `/bin/sh -lc`, so the user's login PATH applies,
and Workspace exec through `/bin/sh -c`; `cmd.exe /c` on a Windows Harbor) and their output and exit code come back over
the link. A check run's expected artifacts are read from the Harbor and parsed for test and coverage results as on the
Admiral. Every file operation goes through the Harbor's file protocol, and the Harbor confines it: it accepts a root
only when it is the checkout it maps that vessel to (or inside its docks folder), and refuses absolute paths, paths that
leave the root, symbolic links, and `.git` (see [HARBOR_PROTOCOL.md](HARBOR_PROTOCOL.md)). Workspace exec keeps its
tenant-administrator rule, and every command still writes an `audit.command` event, whose `host` names the Harbor.
Readiness reports the vessel as having a checkout, names the Harbor and its path (`harborId`, `harborName` (the
Harbor's name alone), `checkoutPath`, and an Info issue), and probes git state, toolchains, and command dependencies on
the Harbor. The dashboard, the mobile app, and the TUI show it in every readiness panel as "Checkout: on Harbor
Joels-MacBook-Pro at /Users/joel/Code/DocConverter" with the Harbor ID (a copy button in the dashboard, selectable text
in the app, and `Y` Copy Harbor ID on the TUI vessel page), as "on the Admiral at <path>" when the working directory is
on the Admiral host, or with the reason from `checkoutErrorCode` when there is none. Vessel health on a Harbor checkout
runs the dependency tools there and decides `RestoreRequired` from `obj/project.assets.json` in the Harbor's checkout
(read with a `Stat` file request), never from the Admiral's disk.

When neither has a checkout, the operation fails with a reason that says what to set, for example:

> No connected Harbor has a checkout of DocConverter; in Harbor > Settings > Repositories set its folder or add a root
> folder that contains it (it has no working directory on the Admiral host; Harbor Mac (hbr_...): it has no checkout of
> vessel DocConverter, only its own clone for mission docks).

or, with no Harbor connected, "No connected Harbor has a checkout of DocConverter, and it has no working directory on
the Admiral host; start Harbor on the machine that has the checkout and, in Harbor > Settings > Repositories, set its
folder or add a root folder that contains it." REST returns 409 with `data.code` `VesselCheckoutUnavailable` and
`data.reason` `NoHarborConnected` or `NoHarborCheckout`; MCP tools (and so Ask Armada) return an `Unavailable` tool error
with `code` `VesselCheckoutUnavailable.NoHarborConnected` or `VesselCheckoutUnavailable.NoHarborCheckout` and the same
message. Readiness reports it as the `working_directory_missing` issue with `checkoutErrorCode`; the read-only views
(Workspace status, git status, branches, and history) return it in their `error` field.

Planning sessions and Model Context builds create their docks the way missions do: on a connected Harbor that can serve
the vessel (the captain then runs in that dock there), or on the Admiral host. With `requireHarborForLaunch` on and no
Harbor able to serve the vessel, the session or build fails at once with the reason instead of starting without the
code.

What stays on the Admiral: the merge queue (it tests and merges in the Admiral's own clone), fleet actions (they run on
the vessel's preferred Harbor in the working directory as configured), objective refinement sessions (they run in a
scratch directory when the working directory is not on the captain's host), and workflow inputs (environment variables
and files a workflow profile requires are checked on the Admiral, where secrets are resolved). A Harbor from before
checkout operations (no `harbor-checkouts` capability) is never asked; update it.

## Harbor disconnects

When a Harbor's link closes, the Admiral marks the Harbor `Disconnected`, fails any git or deferred-launch request
still waiting for its reply, and stops routing new work to it. Captain processes already running on that host keep
running, and the Harbor app reconnects on its own (it retries every 3 seconds).

Output and exit events a captain produces while the link is down are not buffered or replayed. If the Harbor
reconnects while the captain is still running, later output streams to the same job again and the mission carries on.
Otherwise the mission is recovered by stall detection, which is the accepted 1.0 behavior:

1. The Admiral keeps treating the delegated captain process as alive (it cannot probe a process on another host), so
   the mission is not failed the moment the link drops.
2. Once no output has arrived for `stallThresholdMinutes` (default 10), the health check marks the captain stalled,
   asks the Harbor to stop it (a no-op while the Harbor is disconnected), and runs auto-recovery: it relaunches the
   captain in the mission's existing dock, up to `maxRecoveryAttempts` (default 3) times. The dock is pinned to its
   Harbor, so the relaunch lands there if that Harbor has reconnected, and never on another Harbor. If it is still
   offline, the relaunch falls back to the Admiral host when `requireHarborForLaunch` is off. When it is on, the
   relaunch is refused and spends one recovery attempt; the captain keeps the mission with no process, and the next
   stall check (after another `stallThresholdMinutes`) tries again, so a Harbor that reconnects within the remaining
   attempts gets the mission back.
3. When recovery is exhausted, the mission fails with `StallRecoveryExhausted`. A recovery that cannot use the dock
   fails the mission as `Infra`. `maxMissionRuntimeMinutes` still applies throughout.

So a short blip costs nothing, while a Harbor that stays away holds its missions for about `stallThresholdMinutes`
before recovery starts (and, with `requireHarborForLaunch` on, for up to `maxRecoveryAttempts` stall intervals
before they fail). Lower `stallThresholdMinutes` to recover sooner, at the cost of flagging captains that are
merely quiet.

## Status

The management surface and the host-runner app exist today: the `Harbor` entity and its persistence, the
REST and MCP management APIs, the multi-Harbor router with dock affinity, the wire-protocol contract, and
the `Armada.Harbor` app. The live split-mode link transport -- the server-side WebSocket endpoint that
accepts Harbor links, credential authentication on the handshake, and remote captain-process delegation -- is
**experimental**. Local mode is the supported way to run Armada. Split mode works, but its link transport, the
Harbor management REST routes and MCP tools, the `harbor.*`, `deploymentMode`, and `requireHarborForLaunch` settings,
and the link protocol below are excluded from the 1.0 compatibility promise and may change in a minor release (see
[COMPATIBILITY.md](COMPATIBILITY.md)). Experimental routes and tools are marked `[Experimental]` in OpenAPI and in their
MCP descriptions.
