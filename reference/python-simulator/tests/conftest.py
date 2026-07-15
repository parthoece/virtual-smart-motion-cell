from __future__ import annotations

from pathlib import Path

import pytest

from smart_motion_cell.config import SimulationConfig, load_config


@pytest.fixture(scope="session")
def project_root() -> Path:
    return Path(__file__).resolve().parents[1]


@pytest.fixture(scope="session")
def demo_config(project_root: Path) -> SimulationConfig:
    return load_config(project_root / "configs" / "demo-cell.json")
