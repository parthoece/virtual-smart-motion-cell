from __future__ import annotations

from dataclasses import dataclass


@dataclass
class AxisPlant:
    """Simple second-order axis model: mass * a = command - damping * velocity."""

    mass: float = 1.0
    damping: float = 3.0
    position: float = 0.0
    velocity: float = 0.0
    disturbance: float = 0.0

    def step(self, command: float, dt: float) -> tuple[float, float]:
        if dt <= 0:
            raise ValueError("dt must be positive")
        if self.mass <= 0:
            raise ValueError("mass must be positive")
        acceleration = (command + self.disturbance - self.damping * self.velocity) / self.mass
        self.velocity += acceleration * dt
        self.position += self.velocity * dt
        return self.position, self.velocity
