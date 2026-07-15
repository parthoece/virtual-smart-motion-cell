from __future__ import annotations

import sqlite3
from collections.abc import Iterable
from dataclasses import dataclass
from pathlib import Path

SCHEMA = """
PRAGMA foreign_keys = ON;
CREATE TABLE IF NOT EXISTS runs (
    run_id TEXT PRIMARY KEY,
    scenario TEXT NOT NULL,
    started_at_utc TEXT NOT NULL,
    config_json TEXT NOT NULL,
    finished_at_utc TEXT,
    status TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS samples (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    run_id TEXT NOT NULL REFERENCES runs(run_id),
    sim_time REAL NOT NULL,
    cell_state TEXT NOT NULL,
    axis TEXT NOT NULL,
    target REAL NOT NULL,
    reference REAL NOT NULL,
    position REAL NOT NULL,
    velocity REAL NOT NULL,
    following_error REAL NOT NULL,
    command REAL NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_samples_run_time ON samples(run_id, sim_time);
CREATE TABLE IF NOT EXISTS events (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    run_id TEXT NOT NULL REFERENCES runs(run_id),
    sim_time REAL NOT NULL,
    event_type TEXT NOT NULL,
    severity TEXT NOT NULL,
    message TEXT NOT NULL,
    part_id TEXT
);
CREATE INDEX IF NOT EXISTS idx_events_run_id ON events(run_id, id);
CREATE TABLE IF NOT EXISTS cycles (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    run_id TEXT NOT NULL REFERENCES runs(run_id),
    cycle_number INTEGER NOT NULL,
    part_id TEXT NOT NULL,
    start_time REAL NOT NULL,
    end_time REAL NOT NULL,
    result TEXT NOT NULL,
    recipe_version TEXT NOT NULL,
    UNIQUE(run_id, cycle_number)
);
"""


@dataclass(frozen=True)
class SampleRecord:
    run_id: str
    sim_time: float
    cell_state: str
    axis: str
    target: float
    reference: float
    position: float
    velocity: float
    following_error: float
    command: float


@dataclass(frozen=True)
class CycleRecord:
    run_id: str
    cycle_number: int
    part_id: str
    start_time: float
    end_time: float
    result: str
    recipe_version: str


class TelemetryStore:
    def __init__(self, database: str | Path):
        self.path = Path(database)
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self.connection = sqlite3.connect(self.path)
        self.connection.row_factory = sqlite3.Row
        self.connection.executescript(SCHEMA)
        self.connection.commit()

    def close(self) -> None:
        self.connection.close()

    def __enter__(self) -> TelemetryStore:
        return self

    def __exit__(self, *_: object) -> None:
        self.close()

    def create_run(self, run_id: str, scenario: str, started_at_utc: str, config_json: str) -> None:
        self.connection.execute(
            "INSERT INTO runs (run_id, scenario, started_at_utc, config_json, status) VALUES (?, ?, ?, ?, ?)",
            (run_id, scenario, started_at_utc, config_json, "running"),
        )
        self.connection.commit()

    def finish_run(self, run_id: str, finished_at_utc: str, status: str) -> None:
        self.connection.execute(
            "UPDATE runs SET finished_at_utc = ?, status = ? WHERE run_id = ?",
            (finished_at_utc, status, run_id),
        )
        self.connection.commit()

    def add_samples(self, records: Iterable[SampleRecord]) -> None:
        self.connection.executemany(
            """
            INSERT INTO samples
            (run_id, sim_time, cell_state, axis, target, reference, position, velocity, following_error, command)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            [
                (
                    r.run_id,
                    r.sim_time,
                    r.cell_state,
                    r.axis,
                    r.target,
                    r.reference,
                    r.position,
                    r.velocity,
                    r.following_error,
                    r.command,
                )
                for r in records
            ],
        )

    def add_event(
        self,
        run_id: str,
        sim_time: float,
        event_type: str,
        severity: str,
        message: str,
        part_id: str | None = None,
    ) -> None:
        self.connection.execute(
            "INSERT INTO events (run_id, sim_time, event_type, severity, message, part_id) VALUES (?, ?, ?, ?, ?, ?)",
            (run_id, sim_time, event_type, severity, message, part_id),
        )
        self.connection.commit()

    def add_cycle(self, record: CycleRecord) -> None:
        self.connection.execute(
            """
            INSERT INTO cycles
            (run_id, cycle_number, part_id, start_time, end_time, result, recipe_version)
            VALUES (?, ?, ?, ?, ?, ?, ?)
            """,
            (
                record.run_id,
                record.cycle_number,
                record.part_id,
                record.start_time,
                record.end_time,
                record.result,
                record.recipe_version,
            ),
        )
        self.connection.commit()

    def commit(self) -> None:
        self.connection.commit()

    def latest_run_id(self) -> str | None:
        row = self.connection.execute(
            "SELECT run_id FROM runs ORDER BY rowid DESC LIMIT 1"
        ).fetchone()
        return None if row is None else str(row["run_id"])

    def query(self, sql: str, parameters: tuple[object, ...] = ()) -> list[dict[str, object]]:
        return [dict(row) for row in self.connection.execute(sql, parameters).fetchall()]
