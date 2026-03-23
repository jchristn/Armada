# Running Armada with Docker

This guide covers running the Armada server and dashboard using Docker containers.

---

## Prerequisites

- [Docker](https://docs.docker.com/get-docker/) installed and running
- [Docker Compose](https://docs.docker.com/compose/install/) (included with Docker Desktop)

---

## Quick Start

```bash
cd docker
docker compose up -d
```

This starts two containers:

| Service | Port | Description |
|---------|------|-------------|
| `armada-server` | 7890 | REST API and built-in dashboard |
| `armada-server` | 7891 | WebSocket (live updates) |
| `armada-server` | 7892 | MCP (agent communication) |
| `armada-dashboard` | 3000 | Standalone React dashboard |

Open the dashboard at **http://localhost:3000** (React SPA) or **http://localhost:7890/dashboard** (built-in).

### Default Credentials

| Field | Value |
|-------|-------|
| Email | `admin@armada` |
| Password | `password` |

For API access from scripts or curl:

```bash
curl -H "Authorization: Bearer default" http://localhost:7890/api/v1/status
```

---

## Architecture

```
┌──────────────┐       ┌──────────────────┐
│  Dashboard   │──────▶│  Armada Server   │
│  (nginx:80)  │ :7890 │  (REST + WS)     │
│  port 3000   │       │  ports 7890-7892  │
└──────────────┘       └──────────────────┘
                              │
                       ┌──────┴──────┐
                       │  SQLite DB  │
                       │  /app/data/ │
                       └─────────────┘
```

The dashboard container serves the React SPA and proxies nothing — the browser makes API calls directly to the server on port 7890. The server container runs the .NET application with an embedded SQLite database.

---

## Docker Compose Configuration

The default `docker/compose.yaml`:

```yaml
services:
  armada-server:
    image: jchristn77/armada-server:v0.3.0
    ports:
      - "7890:7890"
      - "7891:7891"
      - "7892:7892"
    volumes:
      - ./server/armada.json:/app/data/armada.json
      - ./armada/db:/app/data/db
      - ./armada/logs:/app/data/logs

  armada-dashboard:
    image: jchristn77/armada-dashboard:v0.3.0
    ports:
      - "3000:80"
    environment:
      - ARMADA_SERVER_URL=http://armada-server:7890
    depends_on:
      - armada-server
```

### Volumes

| Host Path | Container Path | Purpose |
|-----------|----------------|---------|
| `docker/server/armada.json` | `/app/data/armada.json` | Server configuration |
| `docker/armada/db/` | `/app/data/db/` | SQLite database files |
| `docker/armada/logs/` | `/app/data/logs/` | Server log files |

### Server Configuration

Edit `docker/server/armada.json` to customize:

```json
{
  "dataDirectory": "/app/data",
  "databasePath": "/app/data/db/armada.db",
  "logDirectory": "/app/data/logs",
  "docksDirectory": "/app/data/docks",
  "reposDirectory": "/app/data/repos",
  "admiralPort": 7890,
  "mcpPort": 7891,
  "webSocketPort": 7892,
  "allowSelfRegistration": true,
  "rest": {
    "hostname": "0.0.0.0"
  },
  "database": {
    "type": "Sqlite",
    "filename": "/app/data/db/armada.db"
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

---

## Stopping and Restarting

```bash
# Stop containers (preserves data)
cd docker
docker compose down

# Restart
docker compose up -d

# View logs
docker compose logs -f armada-server
docker compose logs -f armada-dashboard
```

---

## Factory Reset

To delete all data and start fresh while preserving configuration:

**Windows:**
```bash
cd docker/factory
reset.bat
```

**Linux / macOS:**
```bash
cd docker/factory
./reset.sh
```

Both scripts prompt for confirmation, stop containers, and delete database and log files. The `armada.json` configuration file is preserved.

---

## Building Images from Source

Build scripts are in the repository root. They build multi-platform images (amd64 + arm64) and push to Docker Hub.

### Build latest only

```bash
# Server
./build-server.sh
# or on Windows
build-server.bat

# Dashboard
./build-dashboard.sh
# or on Windows
build-dashboard.bat
```

### Build latest + versioned tag

```bash
# Server
./build-server.sh v0.3.0
# or on Windows
build-server.bat v0.3.0

# Dashboard
./build-dashboard.sh v0.3.0
# or on Windows
build-dashboard.bat v0.3.0
```

This produces both `jchristn77/armada-server:latest` and `jchristn77/armada-server:v0.3.0` (and the same for the dashboard).

### Building locally (no push)

If you want to build for local use without pushing to Docker Hub, run `docker build` directly:

```bash
# Server
docker build -f src/Armada.Server/Dockerfile -t armada-server:local .

# Dashboard
docker build -f src/Armada.Dashboard/Dockerfile -t armada-dashboard:local .
```

Then update `docker/compose.yaml` to reference your local tags instead of `jchristn77/...`.

---

## Ports Reference

| Port | Protocol | Service | Description |
|------|----------|---------|-------------|
| 7890 | HTTP | Admiral REST API | REST endpoints, OpenAPI, built-in dashboard |
| 7891 | TCP | MCP | Model Context Protocol for agent communication |
| 7892 | WebSocket | Live Updates | Real-time event streaming to dashboards |
| 3000 | HTTP | React Dashboard | Standalone SPA (nginx) |

---

## Troubleshooting

**Container won't start:**
```bash
docker compose logs armada-server
```
Check that `armada.json` exists and has valid JSON.

**Database permission errors:**
Ensure the `docker/armada/db/` directory is writable. On Linux:
```bash
chmod 777 docker/armada/db
```

**Dashboard can't reach server:**
The React dashboard makes API calls from the browser, not from the container. Ensure port 7890 is accessible from your machine. If running Docker on a remote host, update the dashboard's `VITE_ARMADA_SERVER_URL` environment variable to point to the server's external address.

**CORS errors:**
The Armada server enables CORS on all routes by default. If you see CORS errors, verify you're accessing the correct port (7890 for the API).
