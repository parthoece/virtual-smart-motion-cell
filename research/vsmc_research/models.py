from __future__ import annotations

from dataclasses import asdict, dataclass, field
from enum import StrEnum
from typing import Any


class OperationalMode(StrEnum):
    NORMAL = "normal"
    FAULT_INFLUENCED = "fault_influenced"
    NETWORK_FAULT_INFLUENCED = "network_fault_influenced"
    COMBINED = "combined"


class FaultDomain(StrEnum):
    MACHINE = "machine"
    NETWORK = "network"
    CYBER_INFLUENCED = "cyber_influenced"


class FaultPhase(StrEnum):
    INACTIVE = "inactive"
    INCIPIENT = "incipient"
    ONSET = "onset"
    ACTIVE = "active"
    PROPAGATING = "propagating"
    DETECTED = "detected"
    MITIGATING = "mitigating"
    RECOVERING = "recovering"
    CLEARED = "cleared"


class MachineState(StrEnum):
    IDLE = "idle"
    CHANGEOVER = "changeover"
    MOVE_TO_PICK = "move_to_pick"
    PICK = "pick"
    MOVE_TO_INSPECT = "move_to_inspect"
    INSPECT = "inspect"
    MOVE_TO_PLACE = "move_to_place"
    PLACE = "place"
    BLOCKED = "blocked"
    MAINTENANCE = "maintenance"
    FAULTED = "faulted"


@dataclass(frozen=True)
class ProductRecipe:
    product_id: str
    payload_kg: float
    pick: tuple[float, float]
    inspect: tuple[float, float]
    place: tuple[float, float]
    inspect_s: float
    changeover_s: float
    quality_threshold: float = 0.94


@dataclass
class Order:
    order_id: str
    product_id: str
    quantity: int
    priority: int
    due_time_s: float
    produced: int = 0


@dataclass
class AxisState:
    name: str
    actual_position: float = 0.0
    command_position: float = 0.0
    velocity: float = 0.0
    control_effort: float = 0.0
    integral: float = 0.0
    previous_error: float = 0.0
    sensor_bias: float = 0.0
    enabled: bool = True


@dataclass
class Scenario:
    scenario_id: str
    domain: FaultDomain
    category: str
    fault_type: str
    component: str
    activation_time_s: float
    duration_s: float | None
    progression: str
    magnitude: float
    severity: str
    seed: int
    metadata: dict[str, Any] = field(default_factory=dict)

    def end_time_s(self, experiment_end_s: float) -> float:
        if self.duration_s is None:
            return experiment_end_s
        return min(experiment_end_s, self.activation_time_s + self.duration_s)


@dataclass
class ResearchRecord:
    experiment_id: str
    episode_id: str
    source_id: str
    event_type: str
    simulation_time_ns: int
    monotonic_time_ns: int
    wall_time_utc_ns: int
    observed_time_utc_ns: int
    sequence_number: int
    correlation_id: str | None = None
    trace_id: str | None = None
    span_id: str | None = None
    production_cycle_id: str | None = None
    part_id: str | None = None
    machine_state: str | None = None
    schema_version: str = "1.0.0"
    attributes: dict[str, Any] = field(default_factory=dict)

    def flatten(self) -> dict[str, Any]:
        row = asdict(self)
        attrs = row.pop("attributes")
        for key, value in attrs.items():
            row[key] = value
        return row
