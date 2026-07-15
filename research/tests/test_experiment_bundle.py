from pathlib import Path

import pandas as pd

from vsmc_research.manifest import load_manifest
from vsmc_research.runner import run_experiment


ROOT = Path(__file__).resolve().parents[2]


def test_experiment_bundle_contains_synchronized_evidence(tmp_path: Path) -> None:
    manifest = load_manifest(ROOT / "benchmarks/manifests/machine-fault.yaml")
    manifest.data["episodes"] = 1
    manifest.data["environment"]["duration_s"] = 12
    for scenario in manifest.data["scenarios"]:
        scenario["activation_time_s"] = 3
        scenario["duration_s"] = 7
    bundle = run_experiment(manifest, tmp_path)

    expected = [
        "raw/capture.pcapng",
        "raw/runtime.jsonl",
        "normalized/operational/telemetry.parquet",
        "normalized/network/packets.parquet",
        "normalized/network/messages.parquet",
        "normalized/network/ethercat-pdos.parquet",
        "normalized/network/flows.parquet",
        "normalized/logs/logs.parquet",
        "normalized/synchronization/sync-events.parquet",
        "ground-truth/scenario-intervals.parquet",
        "datasets/multimodal-windows.parquet",
        "datasets/csv/multimodal-windows.csv",
        "metrics/metrics.json",
        "report/index.html",
        "checksums.sha256",
    ]
    for relative in expected:
        assert (bundle / relative).is_file(), relative

    telemetry = pd.read_parquet(bundle / "normalized/operational/telemetry.parquet")
    labels = pd.read_parquet(bundle / "ground-truth/scenario-intervals.parquet")
    windows = pd.read_parquet(bundle / "datasets/multimodal-windows.parquet")
    packets = pd.read_parquet(bundle / "normalized/network/packets.parquet")
    messages = pd.read_parquet(bundle / "normalized/network/messages.parquet")
    pdos = pd.read_parquet(bundle / "normalized/network/ethercat-pdos.parquet")
    assert "target_fault_type" not in telemetry.columns
    assert "fault_type" in labels.columns
    assert "target_fault_type" in windows.columns
    assert set(windows["target_operational_condition"]) >= {"normal", "machine_fault"}
    assert set(messages["protocol"]) == {"EtherCAT"}
    assert set(packets["ether_type"]) == {0x88A4}
    assert set(packets["command"]) == {"LRW"}
    assert set(pdos["axis_id"]) == {"x", "y"}
    assert messages["working_counter_valid"].all()
    assert (bundle / "raw/capture.pcapng").stat().st_size > 64
