from __future__ import annotations

import hashlib
import json
import platform
import shutil
import sys
import time
from datetime import UTC, datetime
from pathlib import Path
from typing import Any

import pandas as pd
import yaml


class ExperimentRecorder:
    def __init__(self, output_root: Path, experiment_id: str, manifest: dict[str, Any]):
        timestamp = datetime.now(UTC).strftime("%Y%m%d-%H%M%S-%f")
        self.bundle = output_root / f"{experiment_id}-{timestamp}"
        self.bundle.mkdir(parents=True, exist_ok=False)
        for path in [
            "raw",
            "normalized/operational",
            "normalized/network",
            "normalized/logs",
            "normalized/synchronization",
            "ground-truth",
            "datasets/csv",
            "metrics",
            "report",
        ]:
            (self.bundle / path).mkdir(parents=True, exist_ok=True)
        (self.bundle / "manifest.yaml").write_text(yaml.safe_dump(manifest, sort_keys=False))
        self.telemetry: list[dict[str, Any]] = []
        self.production: list[dict[str, Any]] = []
        self.logs: list[dict[str, Any]] = []
        self.scenario_intervals: list[dict[str, Any]] = []
        self.sync_events: list[dict[str, Any]] = []

    def write_provenance(self, manifest_hash: str, seeds: list[int]) -> None:
        provenance = {
            "benchmark_version": "0.5.0",
            "environment": "VSMC-DynamicGantry-v1",
            "configuration_sha256": manifest_hash,
            "episode_seeds": seeds,
            "python_version": sys.version,
            "operating_system": platform.platform(),
            "architecture": platform.machine(),
            "created_at_utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
            "canonical_time": "simulation_time_ns",
        }
        (self.bundle / "provenance.json").write_text(json.dumps(provenance, indent=2))

    @staticmethod
    def _write_table(rows: list[dict[str, Any]], parquet_path: Path, csv_path: Path | None) -> None:
        frame = pd.DataFrame(rows)
        if frame.empty:
            frame = pd.DataFrame({"empty": pd.Series(dtype="bool")})
        frame.to_parquet(parquet_path, index=False)
        if csv_path:
            frame.to_csv(csv_path, index=False)

    def finalize(
        self,
        *,
        packets: list[dict[str, Any]],
        messages: list[dict[str, Any]],
        pdos: list[dict[str, Any]],
        flows: list[dict[str, Any]],
        datasets: dict[str, pd.DataFrame],
        metrics: dict[str, Any],
    ) -> Path:
        self._write_table(
            self.telemetry,
            self.bundle / "normalized/operational/telemetry.parquet",
            self.bundle / "datasets/csv/telemetry.csv",
        )
        self._write_table(
            self.production,
            self.bundle / "normalized/operational/production-events.parquet",
            self.bundle / "datasets/csv/production-events.csv",
        )
        self._write_table(
            packets,
            self.bundle / "normalized/network/packets.parquet",
            self.bundle / "datasets/csv/network-packets.csv",
        )
        self._write_table(
            messages,
            self.bundle / "normalized/network/messages.parquet",
            self.bundle / "datasets/csv/network-messages.csv",
        )
        self._write_table(
            pdos,
            self.bundle / "normalized/network/ethercat-pdos.parquet",
            self.bundle / "datasets/csv/ethercat-pdos.csv",
        )
        self._write_table(
            flows,
            self.bundle / "normalized/network/flows.parquet",
            self.bundle / "datasets/csv/network-flows.csv",
        )
        self._write_table(
            self.logs,
            self.bundle / "normalized/logs/logs.parquet",
            self.bundle / "datasets/csv/logs.csv",
        )
        with (self.bundle / "raw/runtime.jsonl").open("w", encoding="utf-8") as handle:
            for row in self.logs:
                handle.write(json.dumps(row, sort_keys=True) + "\n")
        self._write_table(
            self.scenario_intervals,
            self.bundle / "ground-truth/scenario-intervals.parquet",
            self.bundle / "datasets/csv/scenario-intervals.csv",
        )
        self._write_table(
            self.sync_events,
            self.bundle / "normalized/synchronization/sync-events.parquet",
            None,
        )
        for name, frame in datasets.items():
            frame.to_parquet(self.bundle / f"datasets/{name}.parquet", index=False)
            frame.to_csv(self.bundle / f"datasets/csv/{name}.csv", index=False)
        (self.bundle / "metrics/metrics.json").write_text(json.dumps(metrics, indent=2))
        self._write_report(metrics)
        self._write_checksums()
        return self.bundle

    def _write_report(self, metrics: dict[str, Any]) -> None:
        cards = "".join(
            f"<article><strong>{key.replace('_', ' ').title()}</strong><span>{value}</span></article>"
            for key, value in metrics.items()
            if isinstance(value, (int, float, str))
        )
        html = f"""<!doctype html><html><head><meta charset='utf-8'>
<title>VSMC research experiment</title><style>
body{{font:16px system-ui;max-width:1100px;margin:40px auto;padding:0 20px;background:#0b1020;color:#eef2ff}}
h1{{font-size:2rem}}.grid{{display:grid;grid-template-columns:repeat(auto-fit,minmax(210px,1fr));gap:14px}}
article{{background:#151d35;padding:18px;border-radius:12px;display:flex;flex-direction:column;gap:8px}}
article span{{font-size:1.5rem;color:#82d6ff}}code{{color:#9ff3c8}}
</style></head><body><h1>Virtual Smart Motion Cell research run</h1>
<p>This report summarizes a reproducible dynamic simulation bundle. Raw evidence and oracle labels are stored separately.</p>
<div class='grid'>{cards}</div><h2>Bundle layout</h2><pre><code>normalized/  observable data\nground-truth/ oracle intervals\ndatasets/    joined research views\nraw/         PCAPNG and JSONL evidence</code></pre></body></html>"""
        (self.bundle / "report/index.html").write_text(html)

    def _write_checksums(self) -> None:
        lines = []
        for path in sorted(self.bundle.rglob("*")):
            if not path.is_file() or path.name == "checksums.sha256":
                continue
            digest = hashlib.sha256(path.read_bytes()).hexdigest()
            lines.append(f"{digest}  {path.relative_to(self.bundle).as_posix()}")
        (self.bundle / "checksums.sha256").write_text("\n".join(lines) + "\n")


def copy_pcap(source: Path, destination_bundle: Path) -> None:
    destination = destination_bundle / "raw/capture.pcapng"
    if source.exists():
        shutil.move(str(source), destination)
