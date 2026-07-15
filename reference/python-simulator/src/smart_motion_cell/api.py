from __future__ import annotations

import json
import sqlite3
from importlib.resources import files
from pathlib import Path

try:
    from fastapi import FastAPI, HTTPException
    from fastapi.responses import HTMLResponse
except ImportError as exc:  # pragma: no cover
    raise RuntimeError('Install API dependencies with: python -m pip install -e ".[api]"') from exc

from smart_motion_cell.manufacturing.oee import calculate_oee


def create_app(database: str | Path = "artifacts/demo.sqlite") -> FastAPI:
    database_path = Path(database)
    app = FastAPI(
        title="Virtual Smart Motion Cell API",
        version="0.1.0",
        description="Read-only API over simulated cell telemetry and traceability records.",
    )

    def connect() -> sqlite3.Connection:
        if not database_path.exists():
            raise HTTPException(status_code=503, detail="run a simulation first")
        connection = sqlite3.connect(database_path)
        connection.row_factory = sqlite3.Row
        return connection

    @app.get("/", response_class=HTMLResponse)
    def dashboard() -> str:
        return files("smart_motion_cell.web").joinpath("index.html").read_text(encoding="utf-8")

    @app.get("/health")
    def health() -> dict[str, object]:
        return {"status": "ok", "database_exists": database_path.exists()}

    @app.get("/api/runs")
    def runs(limit: int = 20) -> list[dict[str, object]]:
        with connect() as connection:
            rows = connection.execute(
                "SELECT run_id, scenario, started_at_utc, finished_at_utc, status FROM runs ORDER BY rowid DESC LIMIT ?",
                (_bounded_limit(limit),),
            ).fetchall()
        return [dict(row) for row in rows]

    @app.get("/api/events")
    def events(limit: int = 50, run_id: str | None = None) -> list[dict[str, object]]:
        with connect() as connection:
            selected = run_id or _latest_run_id(connection)
            rows = connection.execute(
                "SELECT sim_time, event_type, severity, message, part_id FROM events WHERE run_id = ? ORDER BY id DESC LIMIT ?",
                (selected, _bounded_limit(limit)),
            ).fetchall()
        return [dict(row) for row in rows]

    @app.get("/api/cycles")
    def cycles(limit: int = 50, run_id: str | None = None) -> list[dict[str, object]]:
        with connect() as connection:
            selected = run_id or _latest_run_id(connection)
            rows = connection.execute(
                "SELECT cycle_number, part_id, start_time, end_time, result, recipe_version FROM cycles WHERE run_id = ? ORDER BY cycle_number DESC LIMIT ?",
                (selected, _bounded_limit(limit)),
            ).fetchall()
        return [dict(row) for row in rows]

    @app.get("/api/status")
    def status(run_id: str | None = None) -> dict[str, object]:
        with connect() as connection:
            selected = run_id or _latest_run_id(connection)
            run = connection.execute("SELECT * FROM runs WHERE run_id = ?", (selected,)).fetchone()
            latest = connection.execute(
                "SELECT sim_time, cell_state FROM samples WHERE run_id = ? ORDER BY id DESC LIMIT 1",
                (selected,),
            ).fetchone()
        if run is None:
            raise HTTPException(status_code=404, detail="run not found")
        return {
            "run_id": selected,
            "scenario": run["scenario"],
            "run_status": run["status"],
            "cell_state": None if latest is None else latest["cell_state"],
            "sim_time": None if latest is None else latest["sim_time"],
        }

    @app.get("/api/metrics/oee")
    def oee(run_id: str | None = None) -> dict[str, float]:
        with connect() as connection:
            selected = run_id or _latest_run_id(connection)
            row = connection.execute(
                """
                SELECT COUNT(*) AS total,
                       SUM(CASE WHEN result = 'good' THEN 1 ELSE 0 END) AS good,
                       MIN(start_time) AS start_time,
                       MAX(end_time) AS end_time
                FROM cycles WHERE run_id = ?
                """,
                (selected,),
            ).fetchone()
            run = connection.execute(
                "SELECT config_json FROM runs WHERE run_id = ?", (selected,)
            ).fetchone()
        total = int(row["total"] or 0)
        if total == 0:
            return {"availability": 0.0, "performance": 0.0, "quality": 0.0, "oee": 0.0}
        if run is None:
            raise HTTPException(status_code=404, detail="run not found")
        run_time = max(float(row["end_time"] - row["start_time"]), 1e-9)
        config = json.loads(str(run["config_json"]))
        ideal_cycle_time = float(config["recipe"]["ideal_cycle_seconds"])
        metrics = calculate_oee(
            planned_production_time=run_time,
            run_time=run_time,
            ideal_cycle_time=ideal_cycle_time,
            total_count=total,
            good_count=int(row["good"] or 0),
        )
        return {
            "availability": metrics.availability,
            "performance": metrics.performance,
            "quality": metrics.quality,
            "oee": metrics.oee,
        }

    return app


def _latest_run_id(connection: sqlite3.Connection) -> str:
    row = connection.execute("SELECT run_id FROM runs ORDER BY rowid DESC LIMIT 1").fetchone()
    if row is None:
        raise HTTPException(status_code=404, detail="no runs found")
    return str(row[0])


def _bounded_limit(limit: int) -> int:
    return max(1, min(limit, 500))


app = create_app()
