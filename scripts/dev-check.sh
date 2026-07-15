#!/usr/bin/env bash
set -euo pipefail
dotnet restore VirtualSmartMotionCell.sln
dotnet build VirtualSmartMotionCell.sln -c Release --no-restore
dotnet run --project tests/VirtualSmartMotionCell.Specs -c Release --no-build
dotnet run --project tests/VirtualSmartMotionCell.IntegrationSpecs -c Release --no-build
npm ci --prefix web/viewer --no-audit --no-fund
npm run check --prefix web/viewer
npm run build --prefix web/viewer
python scripts/check_repo.py
if python -c 'import pytest' >/dev/null 2>&1; then
  pytest -q reference/python-simulator/tests
else
  echo 'pytest not installed; skipping Python reference tests'
fi
