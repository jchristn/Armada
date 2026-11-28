#!/usr/bin/env bash
# =====================================================================
# run-landing-batch.sh -- batched against serial merge queue landing.
#
# Runs the opt-in Services.LandingBatchBenchmark suite: on throwaway git
# repositories it enqueues N entries for one vessel and target branch and
# lands them with MergeQueueBatchSize 1 (serial) and 4 (batched), with and
# without one failing entry, using a simulated verification of fixed length.
# No model, server or network is used. Writes
# benchmarks/results/landing-batch.json.
#
# Usage:
#   run-landing-batch.sh [--entries 8] [--check-ms 1500] [--no-build]
# =====================================================================
set -uo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
ENTRIES=8; CHECK_MS=1500; BUILD=1
while [ $# -gt 0 ]; do
  case "$1" in
    --entries) ENTRIES="$2"; shift 2 ;;
    --check-ms) CHECK_MS="$2"; shift 2 ;;
    --no-build) BUILD=0; shift ;;
    *) echo "unknown option: $1" >&2; exit 2 ;;
  esac
done

cd "${REPO_ROOT}/src" || exit 1
if [ "${BUILD}" = "1" ]; then
  dotnet build Test.Automated -c Release -f net10.0 || exit 1
fi
ARMADA_BENCH_LANDING=1 ARMADA_BENCH_LANDING_ENTRIES="${ENTRIES}" ARMADA_BENCH_LANDING_CHECK_MS="${CHECK_MS}" \
  dotnet run --project Test.Automated -c Release --framework net10.0 --no-build -- --suites Services.LandingBatchBenchmark
