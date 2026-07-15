from __future__ import annotations

from dataclasses import dataclass


@dataclass
class Interlocks:
    emergency_stop_released: bool = True
    guard_closed: bool = True
    bus_healthy: bool = True
    external_permission: bool = True

    def block_reason(self, drive_fault: bool = False) -> str | None:
        if not self.emergency_stop_released:
            return "emergency_stop"
        if not self.guard_closed:
            return "guard_open"
        if not self.bus_healthy:
            return "bus_unhealthy"
        if not self.external_permission:
            return "external_permission_removed"
        if drive_fault:
            return "drive_fault"
        return None

    def motion_allowed(self, drive_fault: bool = False) -> bool:
        return self.block_reason(drive_fault) is None
