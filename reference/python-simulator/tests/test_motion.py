from __future__ import annotations

import pytest

from smart_motion_cell.motion.drive import DriveState, SimulatedDrive
from smart_motion_cell.motion.profile import TrapezoidalProfile
from smart_motion_cell.simulation.runner import build_axis


def test_profile_reaches_target_without_overshoot() -> None:
    profile = TrapezoidalProfile(max_velocity=1.0, max_acceleration=2.0)
    history = [profile.step(1.0, 0.01) for _ in range(300)]
    assert history[-1] == pytest.approx(1.0, abs=1e-9)
    assert max(history) <= 1.0 + 1e-9


def test_drive_requires_fault_reset() -> None:
    drive = SimulatedDrive()
    assert drive.enable_operation()
    drive.inject_fault("overcurrent")
    assert drive.state is DriveState.FAULT
    assert not drive.enable_operation()
    assert not drive.reset_fault(cause_cleared=False)
    assert drive.reset_fault(cause_cleared=True)
    assert drive.state is DriveState.SWITCH_ON_DISABLED


def test_axis_rejects_target_outside_soft_limit(demo_config) -> None:
    axis = build_axis(demo_config.axes[0])
    with pytest.raises(ValueError, match="outside soft limits"):
        axis.set_target(99)


def test_axis_reports_following_error(demo_config) -> None:
    axis = build_axis(demo_config.axes[0])
    axis.profile.position = 1.0
    axis.plant.position = 0.0
    assert axis.diagnostic_fault() == "x_following_error"
