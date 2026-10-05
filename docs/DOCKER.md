# Running Armada with Docker

This guide covers running the Armada server and dashboard using Docker containers.

---

## Prerequisites

- [Docker](https://docs.docker.com/get-docker/) installed and running
- [Docker Compose](https://docs.docker.com/compose/install/) (included with Docker Desktop)

---

## Quick Start

```bash
cd docker/armada
export ARMADA_INITIAL_ADMIN_PASSWORD='choose-a-strong-password'   # or put it in docker/armada/.env
docker compose up -d
```

The Admiral listens on all interfaces inside the container, so it refuses to start while the default admin password
is in use; `ARMADA_INITIAL_ADMIN_PASSWORD` (8+ characters) replaces it on first start and disables the `default`
bearer token. Compose stops with an error if the variable is not set.

This starts five containers:

| Service | Port | Description |
|---------|------|-------------|
| `armada-server` | 7890 | REST API, built-in dashboard, and WebSocket at /ws |
| `armada-server` | 7891 | MCP (agent communication) |
| `armada-server` | 9464 | Prometheus scrape endpoint (`/metrics`) |
| `armada-dashboard` | 3000 (container port 8080) | Standalone React dashboard |
| `prometheus` | 9090 | Metrics store, scrapes the Admiral |
| `loki` | 3100 | Log store |
| `grafana` | 3001 | Dashboards (login `admin` / `admin`) |

Open the dashboard at **http://localhost:3000** (the standalone container, which redirects to `/dashboard/`) or **http://localhost:7890/dashboard** (served by the Admiral). Both serve the same React build.

The stack also brings up a Prometheus / Loki / Grafana observability stack (telemetry is enabled in the
container config). Open Grafana at **http://localhost:3001** and see [TELEMETRY.md](TELEMETRY.md) for
the full metric list and configuration reference.

### Credentials

Sign in as `admin@armada` with the password you set in `ARMADA_INITIAL_ADMIN_PASSWORD`. The `default` bearer token
does not work in Docker installs (it is retired with the default password). For scripts, create a credential under
Server > Credentials (the token is shown once) and send it as a bearer token:

```bash
curl -H "Authorization: Bearer <token>" http://localhost:7890/api/v1/status
```

MCP clients on the host must also send a credential, because the MCP listener is not loopback-bound inside the
container: `claude mcp add --transport http armada http://localhost:7891/mcp --header "Authorization: Bearer <token>"`.

### Non-root containers and upgrades

The images run as non-root users on pinned base images: the Admiral and proxy as UID 1654 (`app`), the dashboard as
UID 101 on port 8080 (nginx-unprivileged). After upgrading an existing install, make the bind-mounted directories
writable by the Admiral's user, for example:

```bash
sudo chown -R 1654:1654 docker/armada/db docker/armada/logs
```

The settings file only needs to be readable by UID 1654 (mode 0644 is enough).

The Admiral reads its settings from `/app/data/settings.json` (`ARMADA_DATA_DIR=/app/data`); compose mounts
`docker/armada/armada.json` there.

---

## Architecture

```
+--------------------+         +--------------------+
|  Dashboard         |  :7890  |  Armada Server     |
|  (nginx, React)    |-------->|  REST + WS  :7890  |
|  port 3000         |         |  MCP        :7891  |
+--------------------+         +---------+----------+
                                         |
                                +--------+---------+
                                |  SQLite DB       |
                                |  /app/data/db    |
                                +------------------+
```

The dashboard container serves the React build under `/dashboard/` and proxies everything else (the REST API, the WebSocket at `/ws`, images, and translations) to the Admiral at `ARMADA_SERVER_URL` (default `http://armada-server:7890`), so the browser only talks to port 3000. The server container runs the .NET application with an embedded SQLite database; it also serves the same React build at `/dashboard` on port 7890.

The server image includes `git`, which the Admiral needs for every mission it runs itself (vessel clones, dock worktrees, landing), for example missions on an API-endpoint captain. It does not include agent CLIs such as Claude Code or Codex; run those through a Harbor (split mode) or on a host install.

That dashboard includes the planning workflow as well as direct dispatch: you can chat with a captain inside the UI, keep the transcript, and hand the selected reply directly into dispatch without leaving the browser.

---

## Docker Compose Configuration

The default Armada stack file is `docker/armada/compose.yaml`. The Admiral service, abridged (the file also
defines `armada-dashboard`, `prometheus`, `loki`, and `grafana`, each with a healthcheck):

```yaml
services:
  armada-server:
    build:
      context: ../..
      dockerfile: src/Armada.Server/Dockerfile
    ports:
      - "7890:7890"
      - "7891:7891"
      - "9464:9464"
    environment:
      - ARMADA_INITIAL_ADMIN_PASSWORD=${ARMADA_INITIAL_ADMIN_PASSWORD:?Set ARMADA_INITIAL_ADMIN_PASSWORD ...}
    volumes:
      - ./armada.json:/app/data/settings.json
      - ./db:/app/data/db
      - ./logs:/app/data/logs
    healthcheck:
      test: ["CMD", "curl", "-fsS", "-o", "/dev/null", "http://127.0.0.1:7890/api/v1/status/health"]
      interval: 5s
      timeout: 3s
      retries: 2
      start_period: 30s
    depends_on:
      loki:
        condition: service_healthy
```

The image sets `ARMADA_DATA_DIR=/app/data`, so settings, database, logs, docks, and bare repository clones all live
under `/app/data`. Only the settings file, `db/`, and `logs/` are bind-mounted; docks and repository clones live in
the container and are recreated from each vessel's repository URL when the container is replaced.

The proxy stack file is `docker/proxy/compose.yaml`:

```yaml
services:
  armada-proxy:
    build:
      context: ../..
      dockerfile: src/Armada.Proxy/Dockerfile
    ports:
      - "7893:7893"
    environment:
      - ARMADA_PROXY_SETTINGS_FILE=/config/proxysettings.json
      - ARMADA_PROXY_PASSWORD=${ARMADA_PROXY_PASSWORD:?Set ARMADA_PROXY_PASSWORD ...}
    volumes:
      - ./proxysettings.json:/config/proxysettings.json:ro
      - ./data:/app/data
      - ./logs:/app/data/logs
    healthcheck:
      test: ["CMD", "curl", "-fsS", "-o", "/dev/null", "http://127.0.0.1:7893/proxy-api/v1/status/health"]
      interval: 5s
      timeout: 3s
      retries: 2
      start_period: 20s
```

`ARMADA_PROXY_PASSWORD` is required: it is the shared proxy login and tunnel password, and the proxy refuses to start
with the built-in default. Set the same value as `remoteControl.password` on each Armada instance that tunnels in.

### Volumes

| Host Path | Container Path | Purpose |
|-----------|----------------|---------|
| `docker/armada/armada.json` | `/app/data/settings.json` | Server configuration |
| `docker/armada/db/` | `/app/data/db/` | SQLite database files |
| `docker/armada/logs/` | `/app/data/logs/` | Server log files |
| `docker/proxy/proxysettings.json` | `/config/proxysettings.json` | Proxy configuration |
| `docker/proxy/data/` | `/app/data/` | Proxy state files |
| `docker/proxy/logs/` | `/app/data/logs/` | Proxy log files |

### Server Configuration

Edit `docker/armada/armada.json` to customize (this is the file as shipped):

```json
{
  "dataDirectory": "/app/data",
  "logDirectory": "/app/data/logs",
  "docksDirectory": "/app/data/docks",
  "reposDirectory": "/app/data/repos",
  "admiralPort": 7890,
  "mcpPort": 7891,
  "gitHubToken": null,
  "webSocketEnabled": true,
  "syslogServers": [
    {
      "hostname": "127.0.0.1",
      "port": 514
    }
  ],
  "allowSelfRegistration": false,
  "rest": {
    "hostname": "0.0.0.0"
  },
  "database": {
    "type": "Sqlite",
    "filename": "/app/data/db/armada.db"
  },
  "telemetry": {
    "enabled": true,
    "serviceName": "armada",
    "otlpEndpoint": null,
    "prometheusEnabled": true,
    "prometheusPort": 9464,
    "lokiEndpoint": "http://loki:3100"
  }
}
```

To use MySQL, PostgreSQL, or SQL Server instead of SQLite, change the `database` section:

```json
{
  "database": {
    "type": "Mysql",
    "connectionString": "Server=db-host;Database=armada;User=root;Password=secret;"
  }
}
```

Valid `type` values: `Sqlite`, `Mysql`, `Postgresql`, `SqlServer`.

Stopping, restarting, rebuilding, and rolling back the server always require an admin (the old
`requireAuthForShutdown` setting is deprecated and ignored).

### Vessels from repositories mounted into the container

A vessel's repository URL can be a path inside the container, for example a host checkout mounted at
`/repos/myproject`. The mounted files keep their host owner (your UID on Linux, `root` as seen through Docker
Desktop), which is not the container's user (UID 1654), and git refuses to read a repository owned by another user:
dock provisioning fails with `fatal: detected dubious ownership in repository` (exit 128) and the mission stays
Pending. Trust each mounted path in a git config file and point `GIT_CONFIG_GLOBAL` at it:

```ini
# docker/armada/gitconfig
[safe]
	directory = /repos/myproject
```

```yaml
services:
  armada-server:
    environment:
      - GIT_CONFIG_GLOBAL=/app/data/gitconfig
    volumes:
      - ./gitconfig:/app/data/gitconfig:ro
      - /home/me/code/myproject:/repos/myproject
```

Use the global or system scope. Command-scope settings (`git -c`, or `GIT_CONFIG_COUNT` / `GIT_CONFIG_KEY_n` /
`GIT_CONFIG_VALUE_n` in the container environment) do not work here: a local clone runs `git-upload-pack` in the
source repository, and git removes command-scope configuration from that process's environment. List each path
rather than `*`, which trusts every repository the Admiral can read. Repositories cloned over HTTPS or SSH are not
affected. `scripts/common/install-verify/verify-docker.sh` uses exactly this setup for its test repository.

---

## Split Mode (Admiral in Docker, captains on the host)

`docker/armada/compose.split.yaml` runs the Admiral and the observability stack without the standalone dashboard,
using `docker/armada/armada.split.json`. That file sets `deploymentMode: "Split"`, `requireHarborForLaunch: true`, and
`harbor.advertisedMcpBaseUrl: "http://127.0.0.1:7891/mcp"`, so missions wait for a Harbor on the host instead of
running in the container. Split mode is experimental in 1.0 (see [COMPATIBILITY.md](COMPATIBILITY.md)).

```bash
cd docker/armada
export ARMADA_INITIAL_ADMIN_PASSWORD='choose-a-strong-password'
docker compose -f compose.split.yaml up -d
```

Then start Armada Harbor on the same host, set its `ServerLinkUrl` to `ws://127.0.0.1:7890/v1.0/harbor/connect`, and set its `AccessKey` to an Armada
credential (a bearer token from Server > Credentials): the container listens on `0.0.0.0`, so the Admiral accepts a
Harbor link only with a credential. The advertised MCP URL
uses `127.0.0.1`, which works only when the Harbor runs on the Docker host; change it in `armada.split.json` for a
Harbor on another machine. See [HARBOR.md](HARBOR.md).

---

## Stopping and Restarting

```bash
# Stop Armada containers (preserves data)
cd docker/armada
docker compose down

# Restart
docker compose up -d

# View logs
docker compose logs -f armada-server
docker compose logs -f armada-dashboard
```

To pick up a new version, `git pull` and run `docker/update.sh` (or `docker\update.bat`). It pulls the observability
images, rebuilds the Admiral and dashboard from the checkout, and recreates the stack; `db/`, `logs/`, and the named
volumes are kept. Pass a compose file relative to `docker/` to update another stack, for example
`docker/update.sh armada/compose.split.yaml` or `docker/update.sh proxy/compose.yaml`.

For the proxy stack:

```bash
cd docker/proxy
export ARMADA_PROXY_PASSWORD='replace-with-a-strong-shared-secret'
docker compose down
docker compose up -d
docker compose logs -f armada-proxy
```

---

## Factory Reset

To delete all data and start fresh while preserving configuration:

**Windows:**
```bash
cd docker/armada/factory
reset.bat
```

**Linux / macOS:**
```bash
cd docker/armada/factory
./reset.sh
```

Both scripts prompt for confirmation, stop containers, and delete local SQLite database and log files. The `armada.json` configuration file is preserved. If the Docker config points at MySQL, PostgreSQL, or SQL Server instead of the mounted SQLite file, the external database is not modified by the reset scripts.

---

## Building Images from Source

Build scripts are split by platform under `scripts/windows/`, `scripts/linux/`, and `scripts/macos/`. Shared shell implementations live under `scripts/common/`. They build with `docker buildx` (server and dashboard for `linux/amd64` and `linux/arm64/v8`; the proxy for `linux/amd64` only), push to Docker Hub, and then pull the pushed tags back into the local Docker registry so the same images are available locally. You need a `docker buildx` builder that can target those platforms and a `docker login` with push rights to `jchristn77`.

### Build latest only

```bash
Linux:
./scripts/linux/build-server.sh

macOS:
./scripts/macos/build-server.sh

Windows:
scripts\windows\build-server.bat

Linux:
./scripts/linux/build-dashboard.sh

macOS:
./scripts/macos/build-dashboard.sh

Windows:
scripts\windows\build-dashboard.bat
```

### Build latest + versioned tag

```bash
Linux:
./scripts/linux/build-server.sh v1.0.0

macOS:
./scripts/macos/build-server.sh v1.0.0

Windows:
scripts\windows\build-server.bat v1.0.0

Linux:
./scripts/linux/build-dashboard.sh v1.0.0

macOS:
./scripts/macos/build-dashboard.sh v1.0.0

Windows:
scripts\windows\build-dashboard.bat v1.0.0
```

This produces both `jchristn77/armada-server:latest` and `jchristn77/armada-server:v1.0.0` (and the same for the dashboard). After the push completes, each script pulls those tags back into the local registry so they are also available for local `docker run` / compose use.

### Build everything at once

To build, push, and locally pull the server, dashboard, and proxy images in one command, use the `build-all` script with an optional version tag:

```bash
Linux:
./scripts/linux/build-all.sh v1.0.0

macOS:
./scripts/macos/build-all.sh v1.0.0

Windows:
scripts\windows\build-all.bat v1.0.0
```

`build-all` covers the server, dashboard, and proxy images on every platform. Omit the tag argument to build and push `:latest` only.

### Repository-root release scripts

The repository root also carries `build-all`, `build-admiral`, and `build-proxy` scripts, as `.bat` for Windows and `.sh` for Linux/macOS. Each requires an image tag and builds both that tag and `latest` on the `cloud-jchristn77-jchristn77` cloud builder for `linux/amd64` and `linux/arm64/v8`, pushes to Docker Hub, then pulls both tags back to refresh the local copy:

```bat
build-all.bat v1.0.0
```

```bash
./build-all.sh v1.0.0
```

`build-all` calls `build-admiral` (`jchristn77/armada-server`) and then `build-proxy` (`jchristn77/armada-proxy`), stopping at the first failure.

### Building locally (no push)

If you want to build for local use without pushing to Docker Hub, run `docker build` directly:

```bash
# Server
docker build -f src/Armada.Server/Dockerfile -t armada-server:local .

# Dashboard
docker build -f src/Armada.Dashboard/Dockerfile -t armada-dashboard:local .

# Proxy
docker build -f src/Armada.Proxy/Dockerfile -t armada-proxy:local .
```

Run these from the repository root. The compose files already build from source (`build:`), so this is only needed
when you want named images: replace a service's `build:` block with `image: armada-server:local` (or a published tag
such as `jchristn77/armada-server:v1.0.0`) to use one.

---

## Ports Reference

| Port | Protocol | Service | Description |
|------|----------|---------|-------------|
| 7890 | HTTP | Admiral REST API | REST endpoints, OpenAPI, built-in dashboard, WebSocket at /ws |
| 7891 | HTTP | MCP | Model Context Protocol for agent communication (`/mcp`) |
| 9464 | HTTP | Admiral metrics | Prometheus scrape endpoint (`/metrics`) |
| 3000 | HTTP | React Dashboard | Standalone SPA (nginx) |
| 9090 | HTTP | Prometheus | Metrics UI |
| 3100 | HTTP | Loki | Log ingestion |
| 3001 | HTTP | Grafana | Dashboards |

---

## Troubleshooting

**Container won't start:**
```bash
cd docker/armada
docker compose logs armada-server
```
Check that `docker/armada/armada.json` exists, is valid JSON, and is readable by UID 1654 (it is mounted at
`/app/data/settings.json`), and that `ARMADA_INITIAL_ADMIN_PASSWORD` is set.

**Database permission errors:**
The container runs as UID 1654, so `docker/armada/db/` and `docker/armada/logs/` must be writable by that user. On
Linux, fix the ownership rather than making the directories world writable:
```bash
sudo chown -R 1654:1654 docker/armada/db docker/armada/logs
```

**Missions stay Pending with `detected dubious ownership` in `admiral.log`:**
The vessel's repository is a path mounted from the host. Trust it as described under
[Vessels from repositories mounted into the container](#vessels-from-repositories-mounted-into-the-container).

**Dashboard can't reach server:**
The standalone dashboard proxies API and WebSocket calls to `ARMADA_SERVER_URL` from inside the container. Check that the variable names the Admiral as the dashboard container sees it (the compose default is `http://armada-server:7890`) and that `armada-server` is healthy; `docker compose logs armada-dashboard` shows proxy errors.

**CORS errors:**
The Armada server enables CORS on all routes by default. If you see CORS errors, verify you're accessing the correct port (7890 for the API).

