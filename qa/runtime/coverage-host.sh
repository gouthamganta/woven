#!/usr/bin/env bash
set -eu
# Coverlet owns the instrumented copy; this target shuts the app down cleanly
# so hit counters flush before Coverlet restores the copy and writes its report.
dotnet /app/WovenBackend.dll &
backend_pid=$!
cleanup() {
  kill -TERM "$backend_pid" 2>/dev/null || true
  wait "$backend_pid" || true
}
trap cleanup EXIT INT TERM
while [ ! -f /qa-results/stop ]; do
  if ! kill -0 "$backend_pid" 2>/dev/null; then
    wait "$backend_pid"
    exit 1
  fi
  sleep 1
done
