from __future__ import annotations

from smart_motion_cell.cell.interlocks import Interlocks
from smart_motion_cell.cell.state_machine import CellState, CellStateMachine


def test_interlock_priority() -> None:
    interlocks = Interlocks(emergency_stop_released=False, guard_closed=False, bus_healthy=False)
    assert interlocks.block_reason() == "emergency_stop"
    assert not interlocks.motion_allowed()


def test_machine_homes_then_runs_sequence(demo_config) -> None:
    machine = CellStateMachine(recipe=demo_config.recipe)
    assert machine.request_home()
    command = machine.update(0.01, True, True, True)
    assert command.state is CellState.READY
    assert machine.request_start("PART-0001")

    expected = [
        CellState.DWELL_PICK,
        CellState.MOVE_INSPECT,
        CellState.DWELL_INSPECT,
        CellState.MOVE_PLACE,
        CellState.DWELL_PLACE,
        CellState.COMPLETE,
        CellState.READY,
    ]
    observed = []
    machine.update(0.01, True, True, True)
    observed.append(machine.state)
    machine.update(demo_config.recipe.dwell_seconds, True, True, True)
    observed.append(machine.state)
    machine.update(0.01, True, True, True)
    observed.append(machine.state)
    machine.update(demo_config.recipe.dwell_seconds, True, True, True)
    observed.append(machine.state)
    machine.update(0.01, True, True, True)
    observed.append(machine.state)
    machine.update(demo_config.recipe.dwell_seconds, True, True, True)
    observed.append(machine.state)
    machine.update(0.01, True, True, True)
    observed.append(machine.state)
    assert observed == expected
    assert machine.completed_cycles == 1


def test_motion_permission_loss_faults_active_sequence(demo_config) -> None:
    machine = CellStateMachine(recipe=demo_config.recipe)
    machine.request_home()
    machine.update(0.01, True, True, True)
    machine.request_start("PART-0001")
    machine.update(0.01, False, False, False)
    assert machine.state is CellState.FAULT
    assert machine.fault_reason == "motion_permission_lost"
    assert not machine.reset(cause_cleared=False)
    assert machine.reset(cause_cleared=True)
    assert machine.state is CellState.IDLE
