#!/usr/bin/env bash
# =====================================================================
# verify-onboarding.sh -- timed check of the first-run path against the
# "first landed mission in under ten minutes" goal (V1 readiness W6.7).
#
# Walks the path a new user takes on Linux or macOS, with a fresh data
# directory and a fresh HOME, and times each stage:
#   1. install   "dotnet tool install Armada.Helm" from a locally packed
#                package (the same package NuGet serves) into a temp tool
#                path; packing is a build step and is not timed;
#   2. start     "armada server start" with a fresh ARMADA_DATA_DIR;
#   3. first run log in as admin@armada, load the dashboard, register a
#                captain, create a fleet and a vessel from a local repository
#                (a checkout with an origin, LandingMode LocalMerge), dispatch
#                one mission, and wait until it is Complete (landed);
#   4. landed    the change is merged into the local checkout.
# The captain is an ApiEndpoint captain driven by the stub inference server
# (stub_inference.py), so no model or API key is involved: the time measured
# is Armada's own overhead, not a model's.
#
# Fails when any step fails or the total exceeds the budget (default 600 s,
# the ten-minute goal; ONBOARDING_BUDGET_SECONDS overrides it). Prints a
# table of stage times either way.
#
# Usage: verify-onboarding.sh [--nupkg-dir <dir>] [--keep]
# Env:   IV_PORT_BASE (default 34000; uses +60/+61/+62),
#        ONBOARDING_BUDGET_SECONDS, IV_ADMIN_PASSWORD
# Never touches ~/.armada or the global dotnet tools directory.
# =====================================================================
set -uo pipefail
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/lib.sh"

NUPKG_DIR=""
KEEP=0
while [ $# -gt 0 ]; do
  case "$1" in
    --nupkg-dir) NUPKG_DIR="$2"; shift 2 ;;
    --keep) KEEP=1; shift ;;
    -h|--help) grep '^#' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done

PORT_BASE="${IV_PORT_BASE:-34000}"
REST_PORT=$((PORT_BASE + 60))
MCP_PORT=$((PORT_BASE + 61))
STUB_PORT=$((PORT_BASE + 62))
BUDGET="${ONBOARDING_BUDGET_SECONDS:-600}"
PASSWORD="${IV_ADMIN_PASSWORD:-Onboarding-Verify-$RANDOM-Pw1}"

IV_WORK="$(mktemp -d "${TMPDIR:-/tmp}/armada-onboarding.XXXXXX")"
SERVER_PID=""
TOOL=""
cleanup() {
  if [ -n "$TOOL" ] && [ -x "$TOOL" ] && [ -n "$SERVER_PID" ] && kill -0 "$SERVER_PID" 2>/dev/null; then
    env HOME="$IV_WORK/home" ARMADA_DATA_DIR="$IV_WORK/data" "$TOOL" server stop >/dev/null 2>&1 || true
  fi
  [ -n "$SERVER_PID" ] && IV_PIDS+=("$SERVER_PID")
  iv_stop_pids
  if [ "$KEEP" -eq 0 ]; then rm -rf "$IV_WORK"; else iv_log "work directory kept: $IV_WORK"; fi
}
trap cleanup EXIT

for p in "$REST_PORT" "$MCP_PORT" "$STUB_PORT"; do iv_require_free_port "$p"; done
command -v dotnet >/dev/null 2>&1 || iv_die "dotnet SDK is required"
command -v git >/dev/null 2>&1 || iv_die "git is required"
PY="$(iv_python)"
now() { "$PY" -c 'import time; print("%.1f" % time.time())'; }
elapsed() { "$PY" -c "print('%.1f' % ($2 - $1))"; }

if [ -z "$NUPKG_DIR" ]; then
  NUPKG_DIR="$IV_WORK/nupkg"
  iv_log "packing src/Armada.Helm into $NUPKG_DIR (build step, not timed)"
  dotnet pack "$IV_REPO_ROOT/src/Armada.Helm" -c Release -o "$NUPKG_DIR" --nologo -v q > "$IV_WORK/pack.log" 2>&1 \
    || { tail -40 "$IV_WORK/pack.log"; iv_die "dotnet pack failed"; }
fi
NUPKG="$(find "$NUPKG_DIR" -maxdepth 1 -name 'Armada.Helm.*.nupkg' | head -1)"
[ -n "$NUPKG" ] || iv_die "no Armada.Helm nupkg in $NUPKG_DIR"
VERSION="$(basename "$NUPKG" .nupkg)"; VERSION="${VERSION#Armada.Helm.}"

# The user's repository: a checkout of a repository with an origin, as on a developer machine.
iv_make_origin "$IV_WORK/origin.git"
git clone -q "$IV_WORK/origin.git" "$IV_WORK/myproject"
git -C "$IV_WORK/myproject" config user.name "Armada Onboarding"
git -C "$IV_WORK/myproject" config user.email "onboarding@armada.invalid"
iv_start_stub "$STUB_PORT" "$IV_WORK/stub.log"

mkdir -p "$IV_WORK/home"
export HOME="$IV_WORK/home"
export ARMADA_DATA_DIR="$IV_WORK/data"
export ARMADA_INITIAL_ADMIN_PASSWORD="$PASSWORD"
cd "$IV_WORK" || exit 1

RESULT=0
T0="$(now)"

iv_log "1. dotnet tool install Armada.Helm $VERSION"
dotnet tool install Armada.Helm --version "$VERSION" --tool-path "$IV_WORK/tools" \
  --add-source "$NUPKG_DIR" --ignore-failed-sources > "$IV_WORK/install.log" 2>&1 \
  || { cat "$IV_WORK/install.log"; iv_die "dotnet tool install failed"; }
TOOL="$IV_WORK/tools/armada"
[ -x "$TOOL" ] || iv_die "tool shim not found at $TOOL"
T1="$(now)"

iv_log "2. armada server start (fresh data directory)"
# The ports are the only setting a first run needs here, and only because the defaults (7890/7891) may be in use.
iv_write_settings "$ARMADA_DATA_DIR" "$REST_PORT" "$MCP_PORT"
"$TOOL" server start > "$IV_WORK/server-start.log" 2>&1
START_CODE=$?
cat "$IV_WORK/server-start.log"
SERVER_PID="$(sed -n 's/.*(PID: \([0-9][0-9]*\)).*/\1/p' "$IV_WORK/server-start.log" | head -1)"
[ "$START_CODE" -eq 0 ] && [ -n "$SERVER_PID" ] || iv_die "armada server start failed (exit $START_CODE)"
T2="$(now)"

iv_log "3. log in, fleet, vessel from $IV_WORK/myproject, one mission, wait until landed"
iv_smoke --base-url "http://127.0.0.1:${REST_PORT}" --password "$PASSWORD" \
  --stub-url "http://127.0.0.1:${STUB_PORT}/v1" --repo-url "$IV_WORK/origin.git" \
  --working-directory "$IV_WORK/myproject" --landing-mode LocalMerge \
  --expect landed --timings --health-timeout 120 --mission-timeout 300 || RESULT=1
T3="$(now)"

iv_log "4. the landed change is in the local checkout"
if [ -f "$IV_WORK/myproject/INSTALL_SMOKE.md" ] && git -C "$IV_WORK/myproject" log --oneline -5 -- INSTALL_SMOKE.md | grep -q .; then
  echo "PASS  INSTALL_SMOKE.md is committed in the local checkout ($(git -C "$IV_WORK/myproject" rev-parse --abbrev-ref HEAD))"
else
  echo "FAIL  INSTALL_SMOKE.md is not in the local checkout"
  git -C "$IV_WORK/myproject" log --oneline -5 2>&1 | sed 's/^/      /'
  RESULT=1
fi
T4="$(now)"

if [ "$RESULT" -ne 0 ]; then
  iv_log "admiral log tail:"
  grep -hE "Warn|Error|Alert|fatal|Landing|landing" "$ARMADA_DATA_DIR/logs/"admiral.log* 2>/dev/null | tail -40 | cut -c1-400
fi

"$TOOL" server stop > /dev/null 2>&1 || true
for i in $(seq 1 30); do kill -0 "$SERVER_PID" 2>/dev/null || break; sleep 1; done

TOTAL="$(elapsed "$T0" "$T4")"
echo
echo "Onboarding timing (Armada $VERSION, $(uname -s) $(uname -m))"
printf '  %-46s %8ss\n' "install (dotnet tool install)" "$(elapsed "$T0" "$T1")"
printf '  %-46s %8ss\n' "start (armada server start)" "$(elapsed "$T1" "$T2")"
printf '  %-46s %8ss\n' "first run (healthy, login, ... landed mission)" "$(elapsed "$T2" "$T3")"
printf '  %-46s %8ss\n' "check the local checkout" "$(elapsed "$T3" "$T4")"
printf '  %-46s %8ss  (budget %ss)\n' "total" "$TOTAL" "$BUDGET"

if "$PY" -c "import sys; sys.exit(0 if $TOTAL <= $BUDGET else 1)"; then
  echo "PASS  first landed mission within the ${BUDGET}s budget"
else
  echo "FAIL  first landed mission took ${TOTAL}s, over the ${BUDGET}s budget"
  RESULT=1
fi

if [ "$RESULT" -eq 0 ]; then iv_log "onboarding verification: PASS"; else iv_log "onboarding verification: FAIL"; fi
exit "$RESULT"
