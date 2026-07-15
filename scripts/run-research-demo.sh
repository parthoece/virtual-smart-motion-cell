#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUTPUT="${1:-$ROOT/runs}"
cd "$ROOT"
python -m pip install -e "research[dev]"
BUNDLE="$(vsmc-bench run benchmarks/manifests/machine-fault.yaml --output "$OUTPUT" | tail -1)"
python scripts/validate_research_bundle.py "$BUNDLE"
echo "Research report: $BUNDLE/report/index.html"
