#!/usr/bin/env bash
# Shared helpers for the install verification scripts. Source it; do not run it.
#
# Every helper keeps state under $IV_WORK (a temp directory the caller creates) and records the
# PIDs it starts in $IV_PIDS so iv_cleanup can stop exactly those processes. Nothing here reads or
# writes ~/.armada, and nothing binds a port outside the 34000-34100 range unless the caller asks.

IV_LIB_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
IV_REPO_ROOT="$(cd "${IV_LIB_DIR}/../../.." && pwd)"
IV_PIDS=()

iv_log() { echo "[install-verify] $*"; }

iv_die() { echo "[install-verify] ERROR: $*" >&2; exit 1; }

# Pick a Python 3 interpreter (python3 on Linux/macOS, python on Windows runners).
iv_python() {
  if command -v python3 >/dev/null 2>&1; then echo python3; return; fi
  if command -v python >/dev/null 2>&1; then echo python; return; fi
  iv_die "python3 is required"
}

# Fail fast when a port in our range is already taken, instead of testing someone else's server.
iv_require_free_port() {
  local port="$1"
  if (exec 3<>"/dev/tcp/127.0.0.1/${port}") 2>/dev/null; then
    iv_die "port ${port} is already in use; set a different IV_PORT_BASE"
  fi
}

# Create a bare origin repository with one commit on main: iv_make_origin <dir>
iv_make_origin() {
  local dir="$1"
  local seed="${dir}.seed"
  mkdir -p "$seed"
  git -C "$seed" init -q -b main
  git -C "$seed" config user.name "Armada Install Verify"
  git -C "$seed" config user.email "install-verify@armada.invalid"
  printf '# install smoke\n' > "$seed/README.md"
  git -C "$seed" add README.md
  git -C "$seed" commit -q -m "Initial commit"
  git clone -q --bare "$seed" "$dir"
  rm -rf "$seed"
}

# Write a minimal settings.json: iv_write_settings <data dir> <admiral port> <mcp port>
iv_write_settings() {
  local data="$1" rest="$2" mcp="$3"
  mkdir -p "$data"
  cat > "$data/settings.json" <<EOF
{
  "admiralPort": ${rest},
  "mcpPort": ${mcp},
  "telemetry": { "enabled": false, "prometheusEnabled": false },
  "syslogServers": []
}
EOF
}

# Start the stub inference server in the background: iv_start_stub <port> <log file>
iv_start_stub() {
  local port="$1" logfile="$2"
  local py
  py="$(iv_python)"
  "$py" "${IV_LIB_DIR}/stub_inference.py" --host 127.0.0.1 --port "$port" > "$logfile" 2>&1 &
  IV_PIDS+=("$!")
  local i
  for i in $(seq 1 50); do
    if "$py" -c "import urllib.request; urllib.request.urlopen('http://127.0.0.1:${port}/v1/models', timeout=2)" 2>/dev/null; then return 0; fi
    sleep 0.2
  done
  cat "$logfile" >&2
  iv_die "stub inference server did not start on port ${port}"
}

# Run the REST smoke flow: iv_smoke <args passed to smoke.py>
iv_smoke() {
  local py
  py="$(iv_python)"
  "$py" "${IV_LIB_DIR}/smoke.py" "$@"
}

# Stop every PID we started (and its children), newest first. Never matches by name.
iv_stop_pids() {
  local i pid
  for (( i=${#IV_PIDS[@]}-1; i>=0; i-- )); do
    pid="${IV_PIDS[$i]}"
    [ -z "$pid" ] && continue
    if kill -0 "$pid" 2>/dev/null; then
      kill "$pid" 2>/dev/null || true
      for _ in $(seq 1 50); do kill -0 "$pid" 2>/dev/null || break; sleep 0.2; done
      kill -9 "$pid" 2>/dev/null || true
    fi
    { wait "$pid"; } 2>/dev/null || true
  done
  IV_PIDS=()
}
