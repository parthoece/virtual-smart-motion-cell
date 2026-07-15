from __future__ import annotations

import pytest

from smart_motion_cell.manufacturing.oee import calculate_oee
from smart_motion_cell.manufacturing.telemetry import CycleRecord, SampleRecord, TelemetryStore


def test_oee_components() -> None:
    metrics = calculate_oee(
        planned_production_time=100,
        run_time=90,
        ideal_cycle_time=1,
        total_count=80,
        good_count=76,
    )
    assert metrics.availability == pytest.approx(0.9)
    assert metrics.performance == pytest.approx(80 / 90)
    assert metrics.quality == pytest.approx(0.95)
    assert metrics.oee == pytest.approx(0.76)


def test_telemetry_round_trip(tmp_path) -> None:
    database = tmp_path / "telemetry.sqlite"
    with TelemetryStore(database) as store:
        store.create_run("run1", "normal", "2026-01-01T00:00:00Z", "{}")
        store.add_samples([SampleRecord("run1", 0.0, "ready", "X", 0, 0, 0, 0, 0, 0)])
        store.add_event("run1", 0.0, "state_change", "info", "idle -> ready")
        store.add_cycle(CycleRecord("run1", 1, "PART-1", 0.0, 2.0, "good", "v1"))
        store.commit()
        assert store.latest_run_id() == "run1"
        assert store.query("SELECT COUNT(*) AS count FROM samples")[0]["count"] == 1
        assert store.query("SELECT result FROM cycles")[0]["result"] == "good"
