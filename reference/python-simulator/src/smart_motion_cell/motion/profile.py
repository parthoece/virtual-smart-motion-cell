from __future__ import annotations

import math
from dataclasses import dataclass


@dataclass
class TrapezoidalProfile:
    """Incremental acceleration- and velocity-limited position reference."""

    max_velocity: float
    max_acceleration: float
    position: float = 0.0
    velocity: float = 0.0

    def reset(self, position: float = 0.0) -> None:
        self.position = position
        self.velocity = 0.0

    def step(self, target: float, dt: float) -> float:
        if dt <= 0:
            raise ValueError("dt must be positive")
        if self.max_velocity <= 0 or self.max_acceleration <= 0:
            raise ValueError("profile limits must be positive")

        distance = target - self.position
        if abs(distance) < 1e-9 and abs(self.velocity) < 1e-9:
            self.position = target
            self.velocity = 0.0
            return self.position

        direction = 1.0 if distance >= 0 else -1.0
        stopping_distance = (self.velocity * self.velocity) / (2.0 * self.max_acceleration)
        moving_toward = self.velocity == 0.0 or math.copysign(1.0, self.velocity) == direction
        acceleration = (
            -math.copysign(self.max_acceleration, self.velocity)
            if moving_toward and abs(distance) <= stopping_distance
            else direction * self.max_acceleration
        )

        next_velocity = _clamp(
            self.velocity + acceleration * dt,
            -self.max_velocity,
            self.max_velocity,
        )
        if self.velocity and math.copysign(1.0, self.velocity) != math.copysign(1.0, next_velocity):
            next_velocity = 0.0

        next_position = self.position + next_velocity * dt
        crossed = (target - self.position) * (target - next_position) <= 0
        if crossed and abs(distance) <= max(abs(next_velocity * dt), 1e-9):
            self.position = target
            self.velocity = 0.0
        else:
            self.position = next_position
            self.velocity = next_velocity
        return self.position


def _clamp(value: float, minimum: float, maximum: float) -> float:
    return max(minimum, min(maximum, value))
