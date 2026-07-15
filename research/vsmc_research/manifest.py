from __future__ import annotations

import hashlib
import json
from dataclasses import dataclass
from pathlib import Path
from typing import Any

import yaml


@dataclass(frozen=True)
class Manifest:
    path: Path
    data: dict[str, Any]

    @property
    def experiment_id(self) -> str:
        return str(self.data["experiment_id"])

    @property
    def episodes(self) -> int:
        return int(self.data.get("episodes", 1))

    @property
    def base_seed(self) -> int:
        return int(self.data.get("base_seed", 1001))

    @property
    def duration_s(self) -> float:
        return float(self.data["environment"].get("duration_s", 60.0))

    @property
    def dt_s(self) -> float:
        return float(self.data["environment"].get("dt_ms", 20.0)) / 1000.0

    def canonical_json(self) -> str:
        return json.dumps(self.data, sort_keys=True, separators=(",", ":"))

    def sha256(self) -> str:
        return hashlib.sha256(self.canonical_json().encode()).hexdigest()


def load_manifest(path: str | Path) -> Manifest:
    manifest_path = Path(path).resolve()
    data = yaml.safe_load(manifest_path.read_text(encoding="utf-8"))
    validate_manifest_data(data)
    return Manifest(manifest_path, data)


def validate_manifest_data(data: dict[str, Any]) -> None:
    required = {"schema_version", "experiment_id", "environment", "capture"}
    missing = required - set(data)
    if missing:
        raise ValueError(f"Manifest is missing required fields: {sorted(missing)}")
    environment = data["environment"]
    if environment.get("id") != "VSMC-DynamicGantry-v1":
        raise ValueError("This release supports environment id VSMC-DynamicGantry-v1")
    if float(environment.get("dt_ms", 0)) <= 0:
        raise ValueError("environment.dt_ms must be positive")
    if float(environment.get("duration_s", 0)) <= 0:
        raise ValueError("environment.duration_s must be positive")
    network = data.get("network", {})
    if str(network.get("protocol", "ethercat")).lower() != "ethercat":
        raise ValueError("This release requires network.protocol=ethercat")
    cycle_period_ms = float(network.get("cycle_period_ms", network.get("publish_period_ms", 0)))
    if cycle_period_ms <= 0:
        raise ValueError("network.cycle_period_ms must be positive")
    for scenario in data.get("scenarios", []):
        if scenario.get("domain") not in {"machine", "network", "cyber_influenced"}:
            raise ValueError(f"Invalid scenario domain: {scenario.get('domain')}")
        if float(scenario.get("activation_time_s", -1)) < 0:
            raise ValueError("scenario activation_time_s cannot be negative")
