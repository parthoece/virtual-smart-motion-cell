from __future__ import annotations

import json
from pathlib import Path

from .manifest import load_manifest
from .runner import run_experiment


def replay_bundle(bundle: Path, output_root: Path) -> dict[str, object]:
    manifest_path = bundle / "manifest.yaml"
    original_metrics_path = bundle / "metrics/metrics.json"
    if not manifest_path.is_file() or not original_metrics_path.is_file():
        raise ValueError("Bundle must contain manifest.yaml and metrics/metrics.json")
    original = json.loads(original_metrics_path.read_text())
    replay = run_experiment(load_manifest(manifest_path), output_root)
    replay_metrics = json.loads((replay / "metrics/metrics.json").read_text())
    result = {
        "original_bundle": str(bundle),
        "replay_bundle": str(replay),
        "manifest_match": original.get("manifest_sha256") == replay_metrics.get("manifest_sha256"),
        "transition_match": original.get("transition_hash")
        == replay_metrics.get("transition_hash"),
        "completed_cycles_match": original.get("completed_cycles")
        == replay_metrics.get("completed_cycles"),
    }
    result["reproducible"] = all(
        bool(result[key])
        for key in ["manifest_match", "transition_match", "completed_cycles_match"]
    )
    return result
