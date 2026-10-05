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

Standard .NET runtime, ASP.NET Core hosting, and HTTP client metrics are exported alongside these.

## Docker stack

`docker/armada/compose.yaml` includes a ready-to-run observability stack:

- **prometheus** (port `9090`) scrapes the Admiral's `/metrics` endpoint (`armada-server:9464`).
- **loki** (port `3100`) receives logs pushed by the Admiral.
- **grafana** (port `3001`, login `admin` / `admin`) is pre-provisioned with Prometheus and Loki
  datasources and two dashboards: "Armada Reliability" and "Armada Fleet Operations" (fleet action
  runs, vessel health evaluations, and vessel imports).

The container config (`docker/armada/armada.json`) enables telemetry with Prometheus scraping and Loki
push already pointed at the stack. Bring everything up with:

```bash
cd docker/armada
docker compose up --build
```

Then open Grafana at http://localhost:3001 -> Dashboards -> Armada -> "Armada Reliability".

Config files live under `docker/armada/observability/`:

- `prometheus.yml` -- scrape config
- `loki-config.yaml` -- single-binary Loki config
- `grafana/provisioning/` -- datasource and dashboard providers
- `grafana/dashboards/armada-reliability.json` -- the reliability dashboard
- `grafana/dashboards/armada-fleet-operations.json` -- fleet actions, vessel health, and vessel import

## Local (non-Docker) use

Set `telemetry.enabled` to `true` in your `settings.json` and leave `prometheusEnabled` on. The Admiral
serves metrics at `http://localhost:9464/metrics`; point any Prometheus at that target.
