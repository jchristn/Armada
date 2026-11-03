#!/usr/bin/env bash
# Builds the dashboard before .NET embeds its assets, then exercises isolated admission scenarios.
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
source "${SCRIPT_DIR}/resolve-framework.sh"
armada_resolve_framework "$@"
REPORT="${ARMADA_CONCURRENCY_TEST_RESULTS:-${TMPDIR:-/tmp}/armada-concurrency-results.json}"
npm --prefix "${REPO_ROOT}/src/Armada.Dashboard" run build
dotnet run --project "${REPO_ROOT}/src/Test.Automated" -c Release $ARMADA_DOTNET_FRAMEWORK_ARGS -- \
  --suites Services.AdmissionStuck,Services.OuterGateInvariant,Services.VerificationCapacity,Services.StageCheckResumeSafety,Services.ConcurrencyInvariant,E2E.CaptainConcurrency,Database.CaptainConcurrency,Services.AgentLifecycleHandler,E2E.Concurrency,Services.ConcurrencySettings,Services.SequentialDispatch,Services.MissionAssignmentBlocker,Services.PipelineDispatch,Services.CaptainRouting \
  --results "${REPORT}"
echo "Concurrency test report: ${REPORT}"
