from __future__ import annotations

from dataclasses import dataclass

from smart_motion_cell.control.pid import PID
from smart_motion_cell.motion.drive import SimulatedDrive
from smart_motion_cell.motion.plant import AxisPlant
from smart_motion_cell.motion.profile import TrapezoidalProfile


@dataclass(frozen=True)
class AxisSample:
    target: float
    reference: float
    position: float
    velocity: float
    following_error: float
    position_error: float
    command: float
    at_target: bool


@dataclass
class AxisController:
    name: str
    plant: AxisPlant
    controller: PID
    profile: TrapezoidalProfile
    drive: SimulatedDrive
    soft_min: float
    soft_max: float
    position_tolerance: float = 0.01
    velocity_tolerance: float = 0.02
    following_error_limit: float = 0.35
    target: float = 0.0

    def set_target(self, target: float) -> None:
        if not self.soft_min <= target <= self.soft_max:
            raise ValueError(
                f"{self.name} target {target} outside soft limits "
                f"[{self.soft_min}, {self.soft_max}]"
            )
        self.target = target

    def reset_reference(self) -> None:
        self.profile.reset(self.plant.position)
        self.controller.reset()
        self.target = self.plant.position

    def step(self, dt: float, motion_allowed: bool) -> AxisSample:
        reference = self.profile.step(self.target, dt)
        command = 0.0
        if motion_allowed and self.drive.operation_enabled:
            command = self.controller.update(reference, self.plant.position, dt)
        else:
            self.controller.reset()

        position, velocity = self.plant.step(command, dt)
        position_error = self.target - position
        following_error = reference - position
        at_target = (
            abs(position_error) <= self.position_tolerance
            and abs(velocity) <= self.velocity_tolerance
            and abs(self.profile.velocity) <= self.velocity_tolerance
        )
        return AxisSample(
            target=self.target,
            reference=reference,
            position=position,
            velocity=velocity,
            following_error=following_error,
            position_error=position_error,
            command=command,
            at_target=at_target,
        )

    def diagnostic_fault(self) -> str | None:
        if not self.soft_min <= self.plant.position <= self.soft_max:
            return f"{self.name.lower()}_soft_limit_exceeded"
        if abs(self.profile.position - self.plant.position) > self.following_error_limit:
            return f"{self.name.lower()}_following_error"
        if self.drive.fault_reason:
            return f"{self.name.lower()}_drive_fault:{self.drive.fault_reason}"
        return None
