from pathlib import Path

from vsmc_research.manifest import load_manifest
from vsmc_research.replay import replay_bundle
from vsmc_research.runner import run_experiment


ROOT = Path(__file__).resolve().parents[2]


def test_bundle_replay_matches_semantic_execution(tmp_path: Path) -> None:
    manifest = load_manifest(ROOT / "benchmarks/manifests/normal-operation.yaml")
    manifest.data["episodes"] = 1
    manifest.data["environment"]["duration_s"] = 8
    original = run_experiment(manifest, tmp_path / "original")
    result = replay_bundle(original, tmp_path / "replay")
    assert result["reproducible"] is True
