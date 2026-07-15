from __future__ import annotations

import json

from smart_motion_cell.reporting.report import generate_report
from smart_motion_cell.simulation.runner import run_simulation


def test_report_generates_portable_evidence(demo_config, tmp_path) -> None:
    database = tmp_path / "run.sqlite"
    summary = run_simulation(demo_config, cycles=1, database=database)
    report_dir = tmp_path / "report"
    report = generate_report(database, report_dir, summary.run_id)
    assert report.exists()
    assert (report_dir / "tracking.svg").exists()
    assert (report_dir / "cycles.csv").read_text().startswith("id,run_id")
    data = json.loads((report_dir / "summary.json").read_text())
    assert data["cycles"] == 1
    assert "Virtual Smart Motion Cell" in report.read_text()
