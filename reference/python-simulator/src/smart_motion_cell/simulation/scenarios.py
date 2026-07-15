from __future__ import annotations

from dataclasses import dataclass
from enum import StrEnum

from smart_motion_cell.cell.state_machine import CellState


class FaultKind(StrEnum):
    EMERGENCY_STOP = "emergency-stop"
    GUARD_OPEN = "guard-open"
    BUS_DROP = "bus-drop"
    Y_DRIVE_FAULT = "y-drive-fault"
    X_DISTURBANCE = "x-disturbance"


@dataclass(frozen=True)
class FaultScenario:
    name: str
    kind: FaultKind | None = None
    trigger_state: CellState | None = None
    trigger_after_seconds: float = 0.0


SCENARIOS: dict[str, FaultScenario] = {
    "normal": FaultScenario(name="normal"),
    "emergency-stop": FaultScenario(
        name="emergency-stop",
        kind=FaultKind.EMERGENCY_STOP,
        trigger_state=CellState.DWELL_INSPECT,
        trigger_after_seconds=0.04,
    ),
    "guard-open": FaultScenario(
        name="guard-open",
        kind=FaultKind.GUARD_OPEN,
        trigger_state=CellState.MOVE_PLACE,
        trigger_after_seconds=0.02,
    ),
    "bus-drop": FaultScenario(
        name="bus-drop",
        kind=FaultKind.BUS_DROP,
        trigger_state=CellState.MOVE_INSPECT,
        trigger_after_seconds=0.04,
    ),
    "y-drive-fault": FaultScenario(
        name="y-drive-fault",
        kind=FaultKind.Y_DRIVE_FAULT,
        trigger_state=CellState.MOVE_PICK,
        trigger_after_seconds=0.04,
    ),
    "x-disturbance": FaultScenario(
        name="x-disturbance",
        kind=FaultKind.X_DISTURBANCE,
        trigger_state=CellState.MOVE_INSPECT,
        trigger_after_seconds=0.02,
    ),
}


def get_scenario(name: str) -> FaultScenario:
    try:
        return SCENARIOS[name]
    except KeyError as exc:
        choices = ", ".join(sorted(SCENARIOS))
        raise ValueError(f"unknown scenario {name!r}; choose one of: {choices}") from exc
