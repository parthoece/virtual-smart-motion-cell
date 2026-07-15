from __future__ import annotations

from dataclasses import dataclass
from enum import StrEnum

from smart_motion_cell.config import CellRecipe


class CellState(StrEnum):
    IDLE = "idle"
    HOMING = "homing"
    READY = "ready"
    MOVE_PICK = "move_pick"
    DWELL_PICK = "dwell_pick"
    MOVE_INSPECT = "move_inspect"
    DWELL_INSPECT = "dwell_inspect"
    MOVE_PLACE = "move_place"
    DWELL_PLACE = "dwell_place"
    COMPLETE = "complete"
    STOPPED = "stopped"
    FAULT = "fault"


ACTIVE_STATES = {
    CellState.HOMING,
    CellState.MOVE_PICK,
    CellState.DWELL_PICK,
    CellState.MOVE_INSPECT,
    CellState.DWELL_INSPECT,
    CellState.MOVE_PLACE,
    CellState.DWELL_PLACE,
}


@dataclass(frozen=True)
class CellCommand:
    x_target: float
    y_target: float
    state: CellState
    part_id: str | None


@dataclass
class CellStateMachine:
    recipe: CellRecipe
    state: CellState = CellState.IDLE
    part_id: str | None = None
    fault_reason: str | None = None
    state_elapsed: float = 0.0
    completed_cycles: int = 0
    good_cycles: int = 0

    def request_home(self) -> bool:
        if self.state not in {CellState.IDLE, CellState.STOPPED}:
            return False
        self._transition(CellState.HOMING)
        return True

    def request_start(self, part_id: str) -> bool:
        if self.state is not CellState.READY:
            return False
        self.part_id = part_id
        self._transition(CellState.MOVE_PICK)
        return True

    def request_stop(self) -> bool:
        if self.state in ACTIVE_STATES or self.state in {CellState.READY, CellState.COMPLETE}:
            self.part_id = None
            self._transition(CellState.STOPPED)
            return True
        return False

    def trip_fault(self, reason: str) -> None:
        self.fault_reason = reason
        self._transition(CellState.FAULT)

    def reset(self, cause_cleared: bool) -> bool:
        if self.state is not CellState.FAULT:
            return True
        if not cause_cleared:
            return False
        self.fault_reason = None
        self.part_id = None
        self._transition(CellState.IDLE)
        return True

    def update(
        self,
        dt: float,
        x_at_target: bool,
        y_at_target: bool,
        motion_allowed: bool,
    ) -> CellCommand:
        if dt <= 0:
            raise ValueError("dt must be positive")
        self.state_elapsed += dt

        if not motion_allowed and self.state in ACTIVE_STATES:
            self.trip_fault("motion_permission_lost")

        both_at_target = x_at_target and y_at_target
        if self.state is CellState.HOMING and both_at_target:
            self._transition(CellState.READY)
        elif self.state is CellState.MOVE_PICK and both_at_target:
            self._transition(CellState.DWELL_PICK)
        elif self.state is CellState.DWELL_PICK and self.state_elapsed >= self.recipe.dwell_seconds:
            self._transition(CellState.MOVE_INSPECT)
        elif self.state is CellState.MOVE_INSPECT and both_at_target:
            self._transition(CellState.DWELL_INSPECT)
        elif (
            self.state is CellState.DWELL_INSPECT
            and self.state_elapsed >= self.recipe.dwell_seconds
        ):
            self._transition(CellState.MOVE_PLACE)
        elif self.state is CellState.MOVE_PLACE and both_at_target:
            self._transition(CellState.DWELL_PLACE)
        elif (
            self.state is CellState.DWELL_PLACE and self.state_elapsed >= self.recipe.dwell_seconds
        ):
            self._transition(CellState.COMPLETE)
        elif self.state is CellState.COMPLETE:
            self.completed_cycles += 1
            self.good_cycles += 1
            self.part_id = None
            self._transition(CellState.READY)

        x_target, y_target = self._targets_for_state()
        return CellCommand(
            x_target=x_target, y_target=y_target, state=self.state, part_id=self.part_id
        )

    def _targets_for_state(self) -> tuple[float, float]:
        if self.state in {
            CellState.IDLE,
            CellState.HOMING,
            CellState.READY,
            CellState.STOPPED,
            CellState.FAULT,
        }:
            return self.recipe.home
        if self.state in {CellState.MOVE_PICK, CellState.DWELL_PICK}:
            return self.recipe.pick
        if self.state in {CellState.MOVE_INSPECT, CellState.DWELL_INSPECT}:
            return self.recipe.inspect
        return self.recipe.place

    def _transition(self, state: CellState) -> None:
        self.state = state
        self.state_elapsed = 0.0
