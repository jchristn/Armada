<p align="center">
  <img src="https://raw.githubusercontent.com/jchristn/armada/main/assets/logo.png" alt="Armada Logo" width="200" />
</p>

<h1 align="center">Armada</h1>

<p align="center">
  <strong>Reduce context switching across projects. Keep agent work in queryable memory.</strong>
</p>

<p align="center">
  <a href="https://github.com/jchristn/armada">GitHub</a> |
  <a href="https://github.com/jchristn/armada/blob/main/docs/DOCKER.md">Docker guide</a> |
  <a href="https://github.com/jchristn/armada/blob/main/docs/REST_API.md">REST API</a> |
  <a href="https://github.com/jchristn/armada/blob/main/docs/MCP_API.md">MCP API</a>
</p>

---

## What Armada is

Armada coordinates AI coding agents (Claude Code, Codex, Gemini, Cursor, Mux, OpenCode, or a hosted model endpoint)
across your git repositories. You describe the work; Armada gives each agent its own git worktree and branch, runs
it through a pipeline (plan, implement, test, review), lands the result, and keeps the whole history: missions,
logs, diffs, status changes, and the context agents learned about each repository.

It exists for two problems. Coming back to a project means rebuilding context: what was in flight, what landed,
what failed. And agent sessions vanish into terminal scrollback, so a week later nobody can answer "what happened
here?" Armada keeps the state of the work outside your head and behind a dashboard, a REST API, and MCP tools that
both you and the agents can query.

Armada uses its own nautical vocabulary. The **Admiral** is the server. A **captain** is an agent. A **vessel** is a
repository, a **fleet** is a group of them, a **mission** is one unit of work, a **voyage** is a batch of missions,
and a **dock** is the worktree a captain works in.

## Images

| Image | What it runs | Port |
|-------|--------------|------|
| [`jchristn77/armada-server`](https://hub.docker.com/r/jchristn77/armada-server) | The Admiral: REST API, built-in dashboard, WebSocket (`/ws`), MCP server, Prometheus metrics | 7890, 7891, 9464 |
| [`jchristn77/armada-dashboard`](https://hub.docker.com/r/jchristn77/armada-dashboard) | Standalone React dashboard served by nginx | 80 |
| [`jchristn77/armada-proxy`](https://hub.docker.com/r/jchristn77/armada-proxy) | Armada.Proxy, a relay for reaching an Admiral that is not directly exposed | 7893 |

Every image that serves HTTP includes `curl`, and the compose files define healthchecks that probe
`http://127.0.0.1:<port>/...` every 5 seconds.

## Use cases

- **Solo developer multiplier.** Dispatch three tasks across two repositories, go do something else, and come
  back to branches that were implemented, tested, and reviewed in parallel.
- **Multi-repo coordination.** One voyage can touch a backend, a frontend, and a shared library, each in its own
  worktree.
- **Ship with confidence.** Pipelines put a test and review step between "the agent says it is done" and your main
  branch.
- **Let AI manage AI.** An orchestrating agent (Claude Code, Codex, and others) can drive Armada itself through its
  MCP tools: create voyages, watch missions, and retry failures.
- **A record of what agents changed.** Tech leads and teams get searchable history instead of scattered terminal
  sessions.

## Architecture

<p align="center">
  <img src="https://raw.githubusercontent.com/jchristn/armada/main/assets/screenshot-1.png" alt="Armada dashboard" width="800" />
</p>

The Admiral is a single .NET process. It stores everything in SQLite by default (PostgreSQL, MySQL, and SQL Server
are also supported), serves the REST API and dashboard on port 7890, and serves MCP on port 7891 so agents can call
home. Captains run as CLI processes in their own worktrees.

In the default compose stack the Admiral also runs those captains inside its container, which means the agent CLIs
and their logins would have to live there too. Most people instead use **split mode** (`compose.split.yaml`): the
Admiral runs in Docker while the **Harbor** host-runner app on your machine executes agents, git, and worktrees
where your repositories and tool logins already are. Split mode is experimental in this release.

The compose stack ships with an observability set: Prometheus scrapes the Admiral's `/metrics`, the Admiral pushes
logs to Loki, and Grafana comes with provisioned dashboards.

## Getting started

Clone the repository for the compose files and default configuration:

```bash
git clone https://github.com/jchristn/armada.git
cd armada/docker/armada
docker compose up -d
```

| Service | Host port | Notes |
|---------|-----------|-------|
| `armada-server` | 7890 | REST API, built-in dashboard at `/dashboard`, WebSocket at `/ws` |
| `armada-server` | 7891 | MCP endpoint for agents |
| `armada-server` | 9464 | Prometheus metrics |
| `armada-dashboard` | 3000 | Standalone dashboard |
| `prometheus` / `loki` / `grafana` | 9090 / 3100 / 3001 | Observability (Grafana login `admin` / `admin`) |

Open `http://localhost:7890/dashboard` and sign in with the default account (`admin@armada` / `password`). Change
that password before the Admiral is reachable from any other machine; the defaults are meant for a local first run.

The SQLite database and logs are bind-mounted from `docker/armada/db` and `docker/armada/logs`, and the server
configuration is `docker/armada/armada.json`.

To pull newer images and recreate the stack without touching your data:

```bash
cd armada/docker
./update.sh                       # or update.bat on Windows
./update.sh armada/compose.split.yaml
```

To wipe the local database and logs and start over, use `docker/armada/factory/reset.sh` (or `reset.bat`).

For split mode, the proxy, TLS, backups, and troubleshooting, see the
[Docker guide](https://github.com/jchristn/armada/blob/main/docs/DOCKER.md) and the
[operations guide](https://github.com/jchristn/armada/blob/main/docs/OPERATIONS.md).

## License

MIT. See [LICENSE.md](https://github.com/jchristn/armada/blob/main/LICENSE.md).
