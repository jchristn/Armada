#!/usr/bin/env bash
# =====================================================================
# verify-docker.sh -- install verification for the Docker install path
# (docker/armada/compose.yaml) (V1 readiness W5.5).
#
# Builds the Admiral and dashboard images from this checkout and runs
# docker/armada/compose.yaml under a throwaway project name with an
# override file that:
#   - publishes the Admiral on 127.0.0.1:IV_PORT_BASE+20 (REST) and +21
#     (MCP), the dashboard on +22, and nothing else on the host;
#   - replaces the db/logs/settings bind mounts with a temp directory, so
#     docker/armada/db and docker/armada/logs are never touched;
#   - mounts a temp bare git repository at /iv/origin.git, copied into a
#     docker volume owned by UID 4242 so it is owned by someone other than
#     the container user (UID 1654) on every host, as a host checkout is,
#     and a gitconfig that trusts it (safe.directory via GIT_CONFIG_GLOBAL);
#   - adds a "stub" service (python:3-alpine) serving the stub inference
#     endpoint the test captain uses.
# Then: compose up --wait (healthchecks), log in through the REST API,
# load the dashboard (Admiral /dashboard and the standalone nginx one),
# create a fleet and vessel, dispatch one mission, and require it to
# produce work. Finally compose down -v removes the containers, volumes,
# and network, and the images it built are removed by tag.
#
# The shipped compose.yaml runs the published v1.0.0 images. By default
# this harness builds the current checkout into throwaway per-run tags
# (armada-iv/<image>:<project>) through its generated override, so it
# verifies the source being tested; --no-build runs the published images.
#
# Usage: verify-docker.sh [--keep] [--no-build]
# Env:   IV_PORT_BASE (default 34000), IV_ADMIN_PASSWORD
# =====================================================================
set -uo pipefail
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/lib.sh"

KEEP=0
BUILD=1
while [ $# -gt 0 ]; do
  case "$1" in
    --keep) KEEP=1; shift ;;
    --no-build) BUILD=0; shift ;;
    -h|--help) grep '^#' "$0" | grep -v '^#!' | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done

PORT_BASE="${IV_PORT_BASE:-34000}"
REST_PORT=$((PORT_BASE + 20))
MCP_PORT=$((PORT_BASE + 21))
DASH_PORT=$((PORT_BASE + 22))
PASSWORD="${IV_ADMIN_PASSWORD:-Install-Verify-$RANDOM-Pw1}"
PROJECT="armada-iv-$(date +%s)-$$"
COMPOSE_DIR="$IV_REPO_ROOT/docker/armada"
ORIGIN_VOLUME="${PROJECT}-origin"
ORIGIN_UID=4242

command -v docker >/dev/null 2>&1 || iv_die "docker is required"
docker compose version >/dev/null 2>&1 || iv_die "docker compose v2 is required"
for p in "$REST_PORT" "$MCP_PORT" "$DASH_PORT"; do iv_require_free_port "$p"; done

IV_WORK="$(mktemp -d "${TMPDIR:-/tmp}/armada-iv-docker.XXXXXX")"
compose() {
  docker compose -p "$PROJECT" -f "$COMPOSE_DIR/compose.yaml" -f "$IV_WORK/compose.override.yaml" -f "$IV_WORK/compose.build.yaml" "$@"
}
cleanup() {
  if [ "$KEEP" -eq 1 ]; then
    iv_log "kept: project $PROJECT, work directory $IV_WORK"
    iv_log "remove with: docker compose -p $PROJECT -f $COMPOSE_DIR/compose.yaml -f $IV_WORK/compose.override.yaml -f $IV_WORK/compose.build.yaml down -v; docker image rm armada-iv/armada-server:$PROJECT armada-iv/armada-dashboard:$PROJECT; docker volume rm $ORIGIN_VOLUME"
    return
  fi
  iv_log "compose down (project $PROJECT)"
  compose down -v --remove-orphans >/dev/null 2>&1 || true
  if [ "$BUILD" -eq 1 ]; then
    docker image rm "armada-iv/armada-server:${PROJECT}" "armada-iv/armada-dashboard:${PROJECT}" >/dev/null 2>&1 || true
  fi
  docker volume rm "$ORIGIN_VOLUME" >/dev/null 2>&1 || true
  # The container wrote these as UID 1654; remove them from inside a container when the host user cannot.
  rm -rf "$IV_WORK" 2>/dev/null || docker run --rm -v "$IV_WORK:/w" busybox:1.37 rm -rf /w/data /w/logs >/dev/null 2>&1 || true
  rm -rf "$IV_WORK" 2>/dev/null || true
}
trap cleanup EXIT

# Writable by the container's non-root user (UID 1654) on Linux hosts, where bind mounts keep host modes.
mkdir -p "$IV_WORK/data/db" "$IV_WORK/logs"
chmod -R 0777 "$IV_WORK/data" "$IV_WORK/logs"
cp "$COMPOSE_DIR/armada.json" "$IV_WORK/armada.json"
chmod 0644 "$IV_WORK/armada.json"
iv_make_origin "$IV_WORK/origin.git"
# Copy the origin into a volume owned by a UID that is neither the host user nor the container user. A plain bind
# mount keeps the host owner on Linux (UID 1001 on a CI runner) but Docker Desktop reports the mounting user as the
# owner of most files, which hid the "dubious ownership" failure on macOS; the volume makes every host behave alike.
docker volume create "$ORIGIN_VOLUME" >/dev/null || iv_die "could not create volume $ORIGIN_VOLUME"
docker run --rm -v "$IV_WORK/origin.git:/src:ro" -v "$ORIGIN_VOLUME:/dst" busybox:1.37 \
  sh -c "cp -a /src/. /dst/ && chown -R ${ORIGIN_UID}:${ORIGIN_UID} /dst && chmod -R a+rwX /dst" \
  || iv_die "could not populate volume $ORIGIN_VOLUME"
# Trust only the mounted test origin (see the GIT_CONFIG_GLOBAL comment in the override below).
printf '[safe]\n\tdirectory = /iv/origin.git\n' > "$IV_WORK/gitconfig"
chmod 0644 "$IV_WORK/gitconfig"

cat > "$IV_WORK/compose.override.yaml" <<EOF
# Generated by scripts/common/install-verify/verify-docker.sh; removed with its temp directory.
services:
  armada-server:
    ports: !override
      - "127.0.0.1:${REST_PORT}:7890"
      - "127.0.0.1:${MCP_PORT}:7891"
    volumes: !override
      - ${IV_WORK}/armada.json:/app/data/settings.json
      - ${IV_WORK}/data/db:/app/data/db
      - ${IV_WORK}/logs:/app/data/logs
      - ${ORIGIN_VOLUME}:/iv/origin.git
      - ${IV_WORK}/gitconfig:/app/data/gitconfig:ro
    environment:
      # The test origin is owned by UID ${ORIGIN_UID}, as a mounted host checkout is owned by the host user, not the
      # container's user (UID 1654), and git refuses to clone a repository owned by another user
      # ("dubious ownership", exit 128) unless the path is trusted. This is the
      # documented recipe for any host repository mounted into the Admiral container (docs/DOCKER.md): a mounted
      # gitconfig named by GIT_CONFIG_GLOBAL. Command-scope config (GIT_CONFIG_COUNT/KEY/VALUE or git -c) does
      # NOT work: git strips it from the environment of the upload-pack process a local clone spawns.
      GIT_CONFIG_GLOBAL: "/app/data/gitconfig"
    depends_on:
      stub:
        condition: service_healthy
  armada-dashboard:
    ports: !override
      - "127.0.0.1:${DASH_PORT}:8080"
  loki:
    ports: !reset []
  prometheus:
    ports: !reset []
  grafana:
    ports: !reset []
  stub:
    image: python:3.13-alpine
    command: ["python", "/stub/stub_inference.py", "--host", "0.0.0.0", "--port", "8080"]
    volumes:
      - ${IV_LIB_DIR}/stub_inference.py:/stub/stub_inference.py:ro
    healthcheck:
      test: ["CMD", "python", "-c", "import urllib.request; urllib.request.urlopen('http://127.0.0.1:8080/v1/models')"]
      interval: 5s
      timeout: 3s
      retries: 2
      start_period: 10s
volumes:
  ${ORIGIN_VOLUME}:
    external: true
EOF

if [ "$BUILD" -eq 1 ]; then
  # Build the checkout into per-run tags so the published release tags are never overwritten locally.
  cat > "$IV_WORK/compose.build.yaml" <<EOF
# Generated by scripts/common/install-verify/verify-docker.sh: run the checkout instead of the published images.
services:
  armada-server:
    image: armada-iv/armada-server:${PROJECT}
    build:
      context: ${IV_REPO_ROOT}
      dockerfile: src/Armada.Server/Dockerfile
  armada-dashboard:
    image: armada-iv/armada-dashboard:${PROJECT}
    build:
      context: ${IV_REPO_ROOT}
      dockerfile: src/Armada.Dashboard/Dockerfile
EOF
else
  printf 'services: {}\n' > "$IV_WORK/compose.build.yaml"
fi

export ARMADA_INITIAL_ADMIN_PASSWORD="$PASSWORD"
compose config --quiet || iv_die "compose configuration is invalid"

if [ "$BUILD" -eq 1 ]; then
  iv_log "building images (project $PROJECT)"
  compose build armada-server armada-dashboard 2>&1 | tail -15
  [ "${PIPESTATUS[0]}" -eq 0 ] || iv_die "docker compose build failed"
fi

iv_log "compose up --wait armada-server armada-dashboard stub"
RESULT=0
if ! compose up -d --wait --wait-timeout 300 armada-server armada-dashboard stub; then
  iv_log "compose up --wait failed; container states and logs follow"
  compose ps -a
  compose logs --no-color --tail 60 armada-server armada-dashboard
  exit 1
fi
compose ps

iv_smoke --base-url "http://127.0.0.1:${REST_PORT}" --password "$PASSWORD" \
  --stub-url "http://stub:8080/v1" --repo-url "/iv/origin.git" \
  --dashboard-url "http://127.0.0.1:${DASH_PORT}/dashboard/" \
  --api-via "http://127.0.0.1:${DASH_PORT}" || RESULT=1

if [ "$RESULT" -ne 0 ]; then
  compose logs --no-color --tail 40 armada-server
  iv_log "admiral log (warnings, errors, mission and dock lines):"
  grep -hE "Warn|Error|Alert|Mission|Dock|Git|fatal|error:" "$IV_WORK"/logs/admiral.log* 2>/dev/null | grep -v "^   at " | tail -60 | cut -c1-400
fi

if [ "$RESULT" -eq 0 ]; then iv_log "Docker install verification: PASS"; else iv_log "Docker install verification: FAIL"; fi
exit "$RESULT"
