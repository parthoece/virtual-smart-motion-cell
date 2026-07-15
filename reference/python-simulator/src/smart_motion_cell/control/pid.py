from __future__ import annotations

from dataclasses import dataclass


@dataclass
class PID:
    """Discrete PID controller with derivative-on-measurement anti-kick logic.

    Anti-windup is implemented with conditional integration. The controller is
    intentionally compact so the control behavior is easy to inspect in tests.
    """

    kp: float
    ki: float
    kd: float
    output_min: float
    output_max: float
    integral_min: float = -100.0
    integral_max: float = 100.0

    _integral: float = 0.0
    _previous_measurement: float | None = None

    def reset(self) -> None:
        self._integral = 0.0
        self._previous_measurement = None

    def update(self, setpoint: float, measurement: float, dt: float) -> float:
        if dt <= 0:
            raise ValueError("dt must be positive")
        if self.output_min >= self.output_max:
            raise ValueError("output_min must be less than output_max")

        error = setpoint - measurement
        derivative = 0.0
        if self._previous_measurement is not None:
            derivative = -(measurement - self._previous_measurement) / dt

        candidate_integral = _clamp(
            self._integral + error * dt,
            self.integral_min,
            self.integral_max,
        )
        unsaturated = self.kp * error + self.ki * candidate_integral + self.kd * derivative
        output = _clamp(unsaturated, self.output_min, self.output_max)

        drives_inward = (output >= self.output_max and error < 0) or (
            output <= self.output_min and error > 0
        )
        if output == unsaturated or drives_inward:
            self._integral = candidate_integral

        self._previous_measurement = measurement
        return output


def _clamp(value: float, minimum: float, maximum: float) -> float:
    return max(minimum, min(maximum, value))
