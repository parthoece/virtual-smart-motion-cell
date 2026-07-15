#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

if ! command -v docker >/dev/null 2>&1; then
  echo "Docker is required. Install Docker Desktop or Docker Engine with Compose v2." >&2
  exit 1
fi
if ! docker compose version >/dev/null 2>&1; then
  echo "Docker Compose v2 is required (the 'docker compose' command)." >&2
  exit 1
fi

mkdir -p runs
docker compose up --build -d mes-simulator machine-runtime research-studio
docker compose --profile bootstrap run --rm complete-bootstrap

latest=""
if [[ -f runs/LATEST_BUNDLE ]]; then
  latest="$(<runs/LATEST_BUNDLE)"
fi

cat <<EOF2

Virtual Smart Motion Cell is running.

Machine viewer/API:  http://localhost:${VSMC_RUNTIME_PORT:-8080}
MES simulator:       http://localhost:${VSMC_MES_PORT:-8090}
Experiment Studio:   http://localhost:${VSMC_STUDIO_PORT:-8091}
OPC UA endpoint:     opc.tcp://localhost:${VSMC_OPCUA_PORT:-4840}/vsmc
${latest:+Research report:     $ROOT/runs/$latest/report/index.html}

Stop the stack with:
  docker compose down
EOF2
