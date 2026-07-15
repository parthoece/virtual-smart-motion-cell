from __future__ import annotations

import pytest

from smart_motion_cell.control.pid import PID


def test_pid_reduces_first_order_error() -> None:
    controller = PID(kp=4.0, ki=1.0, kd=0.1, output_min=-10, output_max=10)
    value = 0.0
    for _ in range(800):
        command = controller.update(1.0, value, 0.01)
        value += (command - value) * 0.01
    assert value == pytest.approx(1.0, abs=0.04)


def test_pid_output_is_limited() -> None:
    controller = PID(kp=100.0, ki=0.0, kd=0.0, output_min=-2.0, output_max=2.0)
    assert controller.update(100, 0, 0.01) == 2.0


def test_pid_rejects_invalid_dt() -> None:
    controller = PID(kp=1, ki=0, kd=0, output_min=-1, output_max=1)
    with pytest.raises(ValueError, match="dt"):
        controller.update(1, 0, 0)
