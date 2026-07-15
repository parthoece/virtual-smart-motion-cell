from __future__ import annotations

from fastapi.testclient import TestClient

from smart_motion_cell.api import create_app
from smart_motion_cell.simulation.runner import run_simulation


def test_api_exposes_status_cycles_events_and_oee(demo_config, tmp_path) -> None:
    database = tmp_path / "api.sqlite"
    run_simulation(demo_config, cycles=1, database=database)
    client = TestClient(create_app(database))
    assert client.get("/health").json()["status"] == "ok"
    assert client.get("/api/status").status_code == 200
    assert len(client.get("/api/cycles").json()) == 1
    assert client.get("/api/events").json()
    assert 0.9 < client.get("/api/metrics/oee").json()["oee"] < 1
    assert "Virtual Smart Motion Cell" in client.get("/").text
