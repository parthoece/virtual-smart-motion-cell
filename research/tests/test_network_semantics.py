from pathlib import Path

from vsmc_research.manifest import load_manifest
from vsmc_research.runner import run_experiment


ROOT = Path(__file__).resolve().parents[2]


def test_network_fault_affects_semantic_messages(tmp_path: Path) -> None:
    manifest = load_manifest(ROOT / "benchmarks/manifests/network-fault.yaml")
    manifest.data["episodes"] = 1
    manifest.data["environment"]["duration_s"] = 12
    manifest.data["scenarios"][0]["activation_time_s"] = 3
    manifest.data["scenarios"][0]["duration_s"] = 6
    bundle = run_experiment(manifest, tmp_path)
    import pandas as pd

    messages = pd.read_parquet(bundle / "normalized/network/messages.parquet")
    baseline = messages[messages["simulation_time_ns"] < 3e9]
    affected = messages[
        (messages["simulation_time_ns"] >= 3e9) & (messages["simulation_time_ns"] < 9e9)
    ]
    assert affected["latency_ms"].mean() > baseline["latency_ms"].mean() + 20
    assert {"machine_state", "production_step", "target_component"}.issubset(messages.columns)
