# Armada benchmarks

The admission benchmark measures how captain selection affects mission admission, through the Admiral's public REST
API only. The harness (`src/Armada.Benchmark`, executable `Armada.Benchmark`) has no project references to Armada; it
provisions captains and missions the way a client would and reads timings from the missions' own server timestamps.

## What it measures

For each candidate count (10 and 100 captains by default; larger fleets with `--candidates`) the standard run starts a fresh isolated Admiral and
submits the same batch of 200 missions under each selection mode, in this order: Enforce, Observe, Disabled. An
unrecorded 20-mission Observe warmup runs first.

| Metric | Meaning |
|---|---|
| Admission latency p50/p95/p99 | `AssignedUtc - CreatedUtc` per mission: creation to the atomic slot assignment. With fewer captains than missions this includes queueing for a free captain, which is the same in every arm. |
| Throughput | Admissions per second from the first creation to the last assignment. |
| Waited over 5 s | Missions whose admission took longer than five seconds. |
| Account distribution | Admissions per synthetic account (captains, 4 concurrent missions each by default, are spread round-robin over accounts at 10, 20, 30, 50 and 89 percent used). |
| Accounting violations | Enforce only: while the arm runs the harness samples the reservation estimates Armada publishes on `GET /api/v1/usage/latest` once a second. Active plus unsettled reservation on an account must never exceed `100 - used - reserve`. |
| Errors and incomplete missions | Any request error or a mission that did not finish before the arm timeout makes the run incomparable (exit 1). |

The budget, from the usage-selection plan: Observe and Enforce p95 admission must stay within 20% of Disabled (plus
a 50 ms noise floor), with zero accounting violations. A broken budget exits 3.

## How it stays isolated

- `start-bench-server.sh` builds `Armada.Server` in Release and runs it from the working tree with data, logs and the
  database under `benchmarks/.run/<name>/`. `settings.json` is regenerated from `config/settings.json` on every start.
- The suite uses ports 25160-25169 on `127.0.0.1`. `~/.armada` and ports 7890/7891 are never touched.
- Captains run `stub/agent/claude`, which waits one second and prints a stream-json result. No model or network is used.
- Usage comes from the documented Custom capture contract: `stub/usage.py` prints a fixed five-hour window for each
  synthetic account. There is no product API that forges allowance.
- Vessels clone a one-commit local bare repository created under `.run/`.

## Running

```sh
benchmarks/run-baseline.sh                         # 10 and 100 candidates, 200 missions per arm
benchmarks/run-baseline.sh --candidates 10 --missions 50 --no-build
dotnet src/Armada.Benchmark/bin/Release/net10.0/Armada.Benchmark.dll compare \
  benchmarks/baselines/admission-100.json benchmarks/results/admission-100.json
```

Requirements: .NET 10 SDK, Python 3, git and curl. Reports are written to `benchmarks/results/` (ignored by git).
Curated reports are copied to `baselines/` and summarized in [RESULTS.md](RESULTS.md).

Each report records the commit, whether the tree was dirty, OS, architecture, processor count, runtime, database
provider, HTTP connection limit and timeout, and a SHA-256 of the scenario inputs. `compare` refuses reports whose
scenario hashes differ.

## Acceptance gate (1.5.0)

The gate in `archive/RELIABILITY_SIMPLICITY_PLAN.md` measures whether open models such as Qwen3.8 can live in a pipeline with
Opus and GPT-6.1. It is its own program, `src/Armada.Gate`, because it drives the Admiral through the public .NET SDK
while this harness stays black-box.

Two files configure it. `config/reference-pipelines.json` is committed and names roles only: which runtime and model
each role uses, the stages of each reference pipeline (Gate-A to Gate-D and the Gate-D variants for other open
families), which role staffs each persona, and how many runs each gets. `config/gate-endpoints.local.json` is
git-ignored and says where the open models are served (Conductor first, Ollama Cloud as a fallback) with their
credentials; copy `config/gate-endpoints.example.json` to start one. Nothing from the local file reaches a report or
a log.

```bash
benchmarks/start-bench-server.sh --name gate --port 25160 --real-agents --telemetry
dotnet run --project src/Armada.Gate -- --url http://127.0.0.1:25160 --setup-only
dotnet run --project src/Armada.Gate -- --url http://127.0.0.1:25160 --pipelines Gate-D --packs csharp --runs 1
```

The first run creates one model endpoint and captain per role and the gate pipelines in the isolated Admiral; later
runs reuse them by name. Each pipeline is a battery run in pipeline mode: every Worker task becomes a voyage through
all of the pipeline's stages and is graded on the final branch. The report lands in `results/gate-<time>.md` and
`.json`. It scores G1, G2, G4, G5, and G6 from the battery results and stage outcomes; the other measures name the
suite or benchmark that checks them and show as NotMeasured, never as passed. Mind the run budget in the plan:
Conductor models run freely, Ollama Cloud models sparingly, Opus and GPT-6.1 only a few times.

## Limits

- The standard run uses SQLite. Pass `--provider` only as a label; pointing the Admiral at a server database means
  editing `config/settings.json` for that run.
- No telemetry collector is started. Server-side stage latency is available from the `armada.selection.stage.duration`
  histogram when the bundled observability stack is running (see [docs/TELEMETRY.md](../docs/TELEMETRY.md)).
- Results come from one machine and are a regression baseline, not a cross-product comparison.

## Landing benchmark (1.5.0)

`run-landing-batch.sh` compares batched merge queue landing (`MergeQueueBatchSize` 4) against serial landing on
throwaway git repositories, with and without one failing entry, using a simulated verification of fixed length
(`--entries`, default 8; `--check-ms`, default 1500). It runs the opt-in `Services.LandingBatchBenchmark` suite, uses
no model, server or network, and writes `results/landing-batch.json` with landed and failed entries, verification
runs, fetches and wall time per arm.
