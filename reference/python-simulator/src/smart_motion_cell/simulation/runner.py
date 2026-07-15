from __future__ import annotations

import json
import math
import uuid
from dataclasses import asdict, dataclass
from datetime import UTC, datetime
from pathlib import Path

from smart_motion_cell.cell.interlocks import Interlocks
from smart_motion_cell.cell.state_machine import CellState, CellStateMachine
from smart_motion_cell.config import AxisConfig, SimulationConfig
from smart_motion_cell.control.pid import PID
from smart_motion_cell.manufacturing.oee import OEEMetrics, calculate_oee
from smart_motion_cell.manufacturing.telemetry import CycleRecord, SampleRecord, TelemetryStore
from smart_motion_cell.motion.axis import AxisController, AxisSample
from smart_motion_cell.motion.drive import SimulatedDrive
from smart_motion_cell.motion.plant import AxisPlant
from smart_motion_cell.motion.profile import TrapezoidalProfile
from smart_motion_cell.simulation.scenarios import FaultKind, FaultScenario, get_scenario


@dataclass(frozen=True)
class RunSummary:
    run_id: str
    scenario: str
    status: str
    requested_cycles: int
    completed_cycles: int
    good_cycles: int
    simulated_seconds: float
    rms_following_error: float
    max_abs_following_error: float
    final_state: str
    fault_reason: str | None
    oee: OEEMetrics
    database: str

    def as_dict(self) -> dict[str, object]:
        result = asdict(self)
        result["oee"] = asdict(self.oee)
        return result


@dataclass
class CellRuntime:
    x_axis: AxisController
    y_axis: AxisController
    interlocks: Interlocks
    machine: CellStateMachine


def build_axis(config: AxisConfig) -> AxisController:
    drive = SimulatedDrive()
    drive.enable_operation()
    return AxisController(
        name=config.name,
        plant=AxisPlant(mass=config.mass, damping=config.damping),
        controller=PID(
            kp=config.kp,
            ki=config.ki,
            kd=config.kd,
            output_min=-config.command_limit,
            output_max=config.command_limit,
        ),
        profile=TrapezoidalProfile(
            max_velocity=config.max_velocity,
            max_acceleration=config.max_acceleration,
        ),
        drive=drive,
        soft_min=config.soft_min,
        soft_max=config.soft_max,
        position_tolerance=config.position_tolerance,
        velocity_tolerance=config.velocity_tolerance,
        following_error_limit=config.following_error_limit,
    )


def build_runtime(config: SimulationConfig) -> CellRuntime:
    return CellRuntime(
        x_axis=build_axis(config.axes[0]),
        y_axis=build_axis(config.axes[1]),
        interlocks=Interlocks(),
        machine=CellStateMachine(recipe=config.recipe),
    )


def run_simulation(
    config: SimulationConfig,
    cycles: int,
    database: str | Path,
    scenario_name: str = "normal",
) -> RunSummary:
    if cycles <= 0:
        raise ValueError("cycles must be positive")
    scenario = get_scenario(scenario_name)
    database_path = Path(database)
    run_id = uuid.uuid4().hex[:12]
    started = datetime.now(UTC).isoformat()
    runtime = build_runtime(config)
    runtime.machine.request_home()

    sim_time = 0.0
    tick = 0
    fault_injected = False
    active_part: str | None = None
    cycle_start = 0.0
    production_start: float | None = None
    production_end: float | None = None
    previous_state = runtime.machine.state
    squared_errors: list[float] = []
    max_error = 0.0
    sample_buffer: list[SampleRecord] = []

    with TelemetryStore(database_path) as store:
        store.create_run(
            run_id=run_id,
            scenario=scenario.name,
            started_at_utc=started,
            config_json=json.dumps(asdict(config), sort_keys=True),
        )
        store.add_event(run_id, sim_time, "run_started", "info", f"scenario={scenario.name}")

        while sim_time < config.max_simulation_seconds:
            drive_fault = (
                runtime.x_axis.drive.fault_reason is not None
                or runtime.y_axis.drive.fault_reason is not None
            )
            motion_allowed = runtime.interlocks.motion_allowed(drive_fault=drive_fault)

            command = runtime.machine.update(
                config.cycle_time_seconds,
                x_at_target=_axis_at_rest(runtime.x_axis),
                y_at_target=_axis_at_rest(runtime.y_axis),
                motion_allowed=motion_allowed,
            )
            try:
                runtime.x_axis.set_target(command.x_target)
                runtime.y_axis.set_target(command.y_target)
            except ValueError as exc:
                runtime.machine.trip_fault(f"recipe_limit_error:{exc}")

            _inject_scenario_if_due(runtime, scenario, fault_injected)
            if _scenario_due(runtime, scenario) and not fault_injected:
                fault_injected = True
                _inject_scenario(runtime, scenario)
                store.add_event(
                    run_id,
                    sim_time,
                    "fault_injected",
                    "warning",
                    scenario.name,
                    runtime.machine.part_id,
                )

            drive_fault = (
                runtime.x_axis.drive.fault_reason is not None
                or runtime.y_axis.drive.fault_reason is not None
            )
            motion_allowed = runtime.interlocks.motion_allowed(drive_fault=drive_fault)
            x_sample = runtime.x_axis.step(config.cycle_time_seconds, motion_allowed)
            y_sample = runtime.y_axis.step(config.cycle_time_seconds, motion_allowed)

            diagnostic = runtime.x_axis.diagnostic_fault() or runtime.y_axis.diagnostic_fault()
            block_reason = runtime.interlocks.block_reason(drive_fault=drive_fault)
            if runtime.machine.state not in {
                CellState.IDLE,
                CellState.READY,
                CellState.STOPPED,
                CellState.FAULT,
            }:
                if diagnostic:
                    runtime.machine.trip_fault(diagnostic)
                elif block_reason:
                    runtime.machine.trip_fault(block_reason)

            for sample in (x_sample, y_sample):
                squared_errors.append(sample.following_error * sample.following_error)
                max_error = max(max_error, abs(sample.following_error))

            if tick % config.sample_every_ticks == 0:
                sample_buffer.extend(
                    _sample_records(run_id, sim_time, runtime.machine.state, x_sample, y_sample)
                )
            if len(sample_buffer) >= 200:
                store.add_samples(sample_buffer)
                store.commit()
                sample_buffer.clear()

            if runtime.machine.state is not previous_state:
                store.add_event(
                    run_id,
                    sim_time,
                    "state_change",
                    "info",
                    f"{previous_state.value} -> {runtime.machine.state.value}",
                    runtime.machine.part_id,
                )
                if runtime.machine.state is CellState.READY and previous_state is CellState.HOMING:
                    store.add_event(run_id, sim_time, "homing_complete", "info", "axes ready")
                if runtime.machine.state is CellState.MOVE_PICK:
                    active_part = runtime.machine.part_id
                    cycle_start = sim_time
                    if production_start is None:
                        production_start = sim_time
                if runtime.machine.state is CellState.COMPLETE and active_part:
                    production_end = sim_time
                    cycle_number = runtime.machine.completed_cycles + 1
                    store.add_cycle(
                        CycleRecord(
                            run_id=run_id,
                            cycle_number=cycle_number,
                            part_id=active_part,
                            start_time=cycle_start,
                            end_time=sim_time,
                            result="good",
                            recipe_version=config.recipe.version,
                        )
                    )
                    store.add_event(
                        run_id,
                        sim_time,
                        "cycle_complete",
                        "info",
                        f"cycle={cycle_number}",
                        active_part,
                    )
                    active_part = None
                previous_state = runtime.machine.state

            if (
                runtime.machine.state is CellState.READY
                and runtime.machine.completed_cycles < cycles
            ):
                part_id = f"PART-{runtime.machine.completed_cycles + 1:04d}"
                if runtime.machine.request_start(part_id):
                    runtime.x_axis.set_target(config.recipe.pick[0])
                    runtime.y_axis.set_target(config.recipe.pick[1])
            if runtime.machine.completed_cycles >= cycles:
                break
            if runtime.machine.state is CellState.FAULT:
                store.add_event(
                    run_id,
                    sim_time,
                    "machine_fault",
                    "error",
                    runtime.machine.fault_reason or "unknown",
                    runtime.machine.part_id,
                )
                break

            tick += 1
            sim_time += config.cycle_time_seconds

        if sample_buffer:
            store.add_samples(sample_buffer)
            store.commit()

        status = "completed" if runtime.machine.completed_cycles >= cycles else "faulted"
        if sim_time >= config.max_simulation_seconds and status != "completed":
            status = "timeout"
            if runtime.machine.fault_reason is None:
                runtime.machine.trip_fault("simulation_timeout")
        store.finish_run(run_id, datetime.now(UTC).isoformat(), status)

    rms = math.sqrt(sum(squared_errors) / max(1, len(squared_errors)))
    run_time = max(
        (production_end or sim_time) - (production_start or 0.0),
        config.cycle_time_seconds,
    )
    oee = calculate_oee(
        planned_production_time=run_time,
        run_time=run_time,
        ideal_cycle_time=config.recipe.ideal_cycle_seconds,
        total_count=runtime.machine.completed_cycles,
        good_count=runtime.machine.good_cycles,
    )
    return RunSummary(
        run_id=run_id,
        scenario=scenario.name,
        status=status,
        requested_cycles=cycles,
        completed_cycles=runtime.machine.completed_cycles,
        good_cycles=runtime.machine.good_cycles,
        simulated_seconds=sim_time,
        rms_following_error=rms,
        max_abs_following_error=max_error,
        final_state=runtime.machine.state.value,
        fault_reason=runtime.machine.fault_reason,
        oee=oee,
        database=str(database_path),
    )


def _axis_at_rest(axis: AxisController) -> bool:
    return (
        abs(axis.target - axis.plant.position) <= axis.position_tolerance
        and abs(axis.plant.velocity) <= axis.velocity_tolerance
        and abs(axis.profile.velocity) <= axis.velocity_tolerance
    )


def _scenario_due(runtime: CellRuntime, scenario: FaultScenario) -> bool:
    return (
        scenario.kind is not None
        and runtime.machine.state is scenario.trigger_state
        and runtime.machine.state_elapsed >= scenario.trigger_after_seconds
    )


def _inject_scenario_if_due(
    runtime: CellRuntime,
    scenario: FaultScenario,
    already_injected: bool,
) -> None:
    # Hook retained for external adapters that need pre-step injection semantics.
    _ = runtime, scenario, already_injected


def _inject_scenario(runtime: CellRuntime, scenario: FaultScenario) -> None:
    if scenario.kind is FaultKind.EMERGENCY_STOP:
        runtime.interlocks.emergency_stop_released = False
    elif scenario.kind is FaultKind.GUARD_OPEN:
        runtime.interlocks.guard_closed = False
    elif scenario.kind is FaultKind.BUS_DROP:
        runtime.interlocks.bus_healthy = False
    elif scenario.kind is FaultKind.Y_DRIVE_FAULT:
        runtime.y_axis.drive.inject_fault("simulated_overcurrent")
    elif scenario.kind is FaultKind.X_DISTURBANCE:
        runtime.x_axis.plant.disturbance = 60.0


def _sample_records(
    run_id: str,
    sim_time: float,
    state: CellState,
    x_sample: AxisSample,
    y_sample: AxisSample,
) -> list[SampleRecord]:
    return [
        SampleRecord(
            run_id=run_id,
            sim_time=sim_time,
            cell_state=state.value,
            axis="X",
            target=x_sample.target,
            reference=x_sample.reference,
            position=x_sample.position,
            velocity=x_sample.velocity,
            following_error=x_sample.following_error,
            command=x_sample.command,
        ),
        SampleRecord(
            run_id=run_id,
            sim_time=sim_time,
            cell_state=state.value,
            axis="Y",
            target=y_sample.target,
            reference=y_sample.reference,
            position=y_sample.position,
            velocity=y_sample.velocity,
            following_error=y_sample.following_error,
            command=y_sample.command,
        ),
    ]
