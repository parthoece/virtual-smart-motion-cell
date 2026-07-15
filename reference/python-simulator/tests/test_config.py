from __future__ import annotations

import json

import pytest

from smart_motion_cell.config import SimulationConfig, load_config


def test_demo_configuration_is_valid(demo_config: SimulationConfig) -> None:
    assert demo_config.cycle_time_seconds == 0.01
    assert demo_config.recipe.version == "demo-v1"
    assert [axis.name for axis in demo_config.axes] == ["X", "Y"]


def test_config_rejects_point_outside_soft_limit(project_root, tmp_path) -> None:
    raw = json.loads((project_root / "configs" / "demo-cell.json").read_text())
    raw["recipe"]["pick"] = [9.0, 0.3]
    path = tmp_path / "bad.json"
    path.write_text(json.dumps(raw))
    with pytest.raises(ValueError, match="outside X soft limits"):
        load_config(path)


def test_config_rejects_non_positive_cycle_time(project_root, tmp_path) -> None:
    raw = json.loads((project_root / "configs" / "demo-cell.json").read_text())
    raw["cycle_time_seconds"] = 0
    path = tmp_path / "bad.json"
    path.write_text(json.dumps(raw))
    with pytest.raises(ValueError, match="cycle_time_seconds"):
        load_config(path)


def test_bundled_default_configuration_is_valid() -> None:
    from smart_motion_cell.config import load_default_config

    config = load_default_config()
    assert config.recipe.version == "demo-v1"
    assert [axis.name for axis in config.axes] == ["X", "Y"]
