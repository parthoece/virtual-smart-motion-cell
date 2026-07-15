from __future__ import annotations

import sqlite3

import pytest

from smart_motion_cell.simulation.runner import run_simulation


def test_normal_simulation_completes_and_records_evidence(demo_config, tmp_path) -> None:
    database = tmp_path / "normal.sqlite"
    summary = run_simulation(demo_config, cycles=2, database=database)
    assert summary.status == "completed"
    assert summary.completed_cycles == 2
    assert summary.good_cycles == 2
    assert summary.max_abs_following_error < 0.35
    with sqlite3.connect(database) as connection:
        assert connection.execute("SELECT COUNT(*) FROM cycles").fetchone()[0] == 2
        assert connection.execute("SELECT COUNT(*) FROM samples").fetchone()[0] > 100


@pytest.mark.parametrize(
    ("scenario", "reason"),
    [
        ("emergency-stop", "emergency_stop"),
        ("guard-open", "guard_open"),
        ("bus-drop", "bus_unhealthy"),
        ("y-drive-fault", "y_drive_fault"),
        ("x-disturbance", "x_following_error"),
    ],
)
def test_fault_scenarios_are_detected(demo_config, tmp_path, scenario, reason) -> None:
    summary = run_simulation(
        demo_config,
        cycles=1,
        database=tmp_path / f"{scenario}.sqlite",
        scenario_name=scenario,
    )
    assert summary.status == "faulted"
    assert reason in (summary.fault_reason or "")
