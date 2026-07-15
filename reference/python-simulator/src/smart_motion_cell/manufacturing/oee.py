from __future__ import annotations

from dataclasses import dataclass


@dataclass(frozen=True)
class OEEMetrics:
    availability: float
    performance: float
    quality: float
    oee: float


def calculate_oee(
    planned_production_time: float,
    run_time: float,
    ideal_cycle_time: float,
    total_count: int,
    good_count: int,
) -> OEEMetrics:
    if planned_production_time <= 0:
        raise ValueError("planned_production_time must be positive")
    if run_time < 0 or ideal_cycle_time <= 0:
        raise ValueError("run_time must be non-negative and ideal_cycle_time positive")
    if total_count < 0 or good_count < 0 or good_count > total_count:
        raise ValueError("invalid production counts")

    availability = min(1.0, run_time / planned_production_time)
    performance = 0.0 if run_time == 0 else min(1.0, ideal_cycle_time * total_count / run_time)
    quality = 0.0 if total_count == 0 else good_count / total_count
    return OEEMetrics(
        availability=availability,
        performance=performance,
        quality=quality,
        oee=availability * performance * quality,
    )
