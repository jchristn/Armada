# Telemetry and Observability

Armada can export OpenTelemetry metrics and logs to a Prometheus / Loki / Grafana stack.
Telemetry is **disabled by default** -- a fresh install ships no telemetry surface until an operator
opts in.

## How it works

- Armada's code emits measurements through the .NET base class library (`System.Diagnostics.Metrics`).
  Emitting costs nothing until a host subscribes, so the core libraries take no dependency on any
  telemetry framework.
- When telemetry is enabled, the Admiral hosts an OpenTelemetry pipeline (via the
  [Radiant](https://www.nuget.org/packages/Radiant) host) that observes Armada's meter plus the web
  server and HTTP client instrumentation, and exports to:
  - an **OTLP collector** (optional push, e.g. an OpenTelemetry Collector),
  - an **in-process Prometheus scrape endpoint** (`/metrics`, default port `9464`), and/or
  - **Loki** (optional direct log push).
- When a Loki or OTLP endpoint is set, the Admiral's own log stream (Info and above) is also exported as logs, so
  exception traces land in Loki next to the metrics.

## Settings

Add a `telemetry` block to `settings.json`:

```json
{
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

| Field               | Default    | Description                                                                 |
|---------------------|------------|-----------------------------------------------------------------------------|
| `enabled`           | `false`    | Master switch. When false, no telemetry host is started.                    |
| `serviceName`       | `armada`   | Logical service name reported to the backend. Blank falls back to `armada`. |
| `otlpEndpoint`      | `null`     | OTLP collector endpoint (e.g. `http://collector:4317`). Null disables OTLP push. |
| `prometheusEnabled` | `true`     | Serve an in-process Prometheus scrape endpoint.                             |
| `prometheusPort`    | `9464`     | Port for the scrape endpoint. Clamped to `[1, 65535]`.                      |
| `lokiEndpoint`      | `null`     | Loki push endpoint (e.g. `http://loki:3100`). Null disables Loki export.    |

The terminal UI (`armada tui`) has its own, separately configured telemetry with the same fields, set in
`~/.armada/tui.json` and also off by default; see [TUI.md](TUI.md#telemetry).

## Metrics

The Admiral exposes reliability counters under the `Armada` meter. Exported to Prometheus they appear
as `armada_*_total` series:

| Prometheus series                       | Meaning                                             |
|-----------------------------------------|-----------------------------------------------------|
| `armada_captains_stalls_total`          | Captain stalls detected                             |
| `armada_captains_recoveries_total`      | Captain auto-recovery attempts                      |
| `armada_missions_failed_total`          | Missions that reached Failed                        |
| `armada_missions_runtime_exceeded_total`| Missions force-failed for exceeding the max runtime |
| `armada_reviews_overdue_total`          | Reviews released after going overdue                |
| `armada_handoffs_redriven_total`        | Dangling pipeline handoffs re-driven                |
| `armada_docks_provisioned_total`        | Docks provisioned                                   |
| `armada_docks_reclaimed_total`          | Docks reclaimed                                     |
| `armada_mergequeue_processed_total`     | Merge-queue entries processed to a terminal state   |
| `armada_fleet_action_targets_total`     | Fleet action targets finished; labels `kind` (Command, Mission) and `outcome` (Succeeded, Failed, TimedOut, Skipped, Cancelled) |
| `armada_health_evaluations_total`       | Vessel health evaluations, one per vessel; label `outcome` (Succeeded, Failed, Cancelled) |
| `armada_vessel_import_items_total`      | Vessel import items processed; label `outcome` (Created, SkippedExisting, SkippedNotSelected, Failed) |

Two histograms cover the slow paths: `armada_fleet_action_target_duration_seconds` (label `kind`) and
`armada_health_criterion_duration_seconds` (label `criterion`). Labels stay low-cardinality on purpose:
vessel IDs, paths, and commands never appear in metric labels. Health evaluations also emit spans from the
`Armada` activity source, one root span per evaluation job with a `stage:<Criterion>` child per
criterion.

The pipeline also subscribes to the `Watson` (web server), `Microsoft.AspNetCore.Hosting`, and `System.Net.Http`
meters, so whichever of those emit are exported alongside these.

## Docker stack

`docker/armada/compose.yaml` includes a ready-to-run observability stack:

- **otel-collector** receives OTLP from the Admiral and exports metrics, traces and logs.
- **prometheus** (loopback port `9090`) scrapes `otel-collector:9465`.
- **tempo** (loopback port `3200`) retains traces.
- **loki** (loopback port `3100`) receives background logs through the collector.
- **grafana** (loopback port `3001`, username `admin`, configured password) includes
  Prometheus, Loki and Tempo datasources and the Reliability, Fleet Operations,
  Storage and Usage dashboards.

Bring everything up with:

```bash
cd docker/armada
export ARMADA_INITIAL_ADMIN_PASSWORD='choose-a-password'
export ARMADA_GRAFANA_ADMIN_PASSWORD='choose-another-password'
docker compose up -d
```

Open Grafana at http://127.0.0.1:3001 -> Dashboards -> Armada -> "Armada Usage".

Config files live under `docker/armada/observability/`:

- `prometheus.yml` -- scrape config
- `loki-config.yaml` -- single-binary Loki config
- `grafana/provisioning/` -- datasource and dashboard providers
- `grafana/dashboards/armada-reliability.json` -- the reliability dashboard
- `grafana/dashboards/armada-fleet-operations.json` -- fleet actions, vessel health, and vessel import

## Local (non-Docker) use

Set `telemetry.enabled` to `true` in your `settings.json` and leave `prometheusEnabled` on. The Admiral
serves metrics at `http://localhost:9464/metrics`; point any Prometheus at that target.

## Usage operations

The Usage dashboard (`/d/armada-usage`) distinguishes scheduling, queued work,
host execution, provider reads, parsing, storage, queries and retention. Its
`Armada` meter uses only operation, runtime, outcome and retention kind labels;
account, captain, tenant and host IDs and arbitrary errors are excluded.

| Instrument | Meaning |
|---|---|
| `armada.usage.operations` | Operation count by bounded outcome |
| `armada.usage.duration` (seconds) | Stage duration, including queued work |
| `armada.usage.active` | Active operation depth; queued/host operations distinguish waiting and execution |
| `armada.usage.observation_age` (seconds) | Increasing age of the most recently committed observation in this process; absent until the first observation |
| `armada.usage.observation_age_at_capture` (seconds) | Distribution of observation age when persisted |
| `armada.usage.retention.deleted` | Raw, rollup and inactive metadata rows removed |

The aggregate age gauge is a process health signal, not a per-account freshness
claim. Per-account observation age remains in product history. Spans use the
existing Armada ActivitySource and propagate only W3C trace context to the host
request. A host needs its own configured exporter to deliver its provider spans.
Telemetry listener/export failures cannot fail capture or dispatch. The server
also subscribes to Watson's activity source; no duplicate HTTP counters are added.

The bundled compose files now send OTLP to `otel-collector:4317`. The collector
exports metrics on its private `9465` endpoint for Prometheus, traces to Tempo,
and background logs through Loki's OTLP endpoint. Direct Admiral Prometheus/Loki
exports are disabled in the bundled settings to avoid duplicate series and logs.
Existing standalone installations can keep their direct exporters.

Set both `ARMADA_INITIAL_ADMIN_PASSWORD` and `ARMADA_GRAFANA_ADMIN_PASSWORD`
before starting compose. Grafana uses username `admin` and the configured password;
there is no shipped shared Grafana password. Prometheus, Grafana, Loki and Tempo
published ports bind to 127.0.0.1. Collector ingestion and scraping stay inside the
compose network. Use a tunnel or authenticated reverse proxy for remote access.
Configuration mounts are read-only and image versions are pinned. The collector's
health extension is available on its private 13133 port; Tempo readiness is `/ready`.

The dashboard home page's External services card links administrators to the Usage
operations dashboard, Prometheus and Tempo. Browser URLs default to the browser's
hostname on ports 3001, 9090 and 3200. Set `VITE_GRAFANA_URL`,
`VITE_PROMETHEUS_URL` and `VITE_TEMPO_URL` at frontend build time for reverse-proxy
or remote installations; these are public URLs, never credential fields.
