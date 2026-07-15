from __future__ import annotations

from dataclasses import dataclass

from .models import FaultDomain, FaultPhase, Scenario


@dataclass(frozen=True)
class ScenarioState:
    scenario: Scenario
    phase: FaultPhase
    intensity: float
    active: bool


def scenario_state(scenario: Scenario, time_s: float, experiment_end_s: float) -> ScenarioState:
    start = scenario.activation_time_s
    end = scenario.end_time_s(experiment_end_s)
    if time_s < start or time_s >= end:
        return ScenarioState(scenario, FaultPhase.INACTIVE, 0.0, False)

    elapsed = time_s - start
    span = max(end - start, 1e-9)
    progress = min(max(elapsed / span, 0.0), 1.0)

    if scenario.progression == "gradual":
        intensity = scenario.magnitude * progress
        phase = FaultPhase.INCIPIENT if progress < 0.4 else FaultPhase.ACTIVE
    elif scenario.progression == "intermittent":
        active = int(elapsed * 5 + scenario.seed) % 4 != 0
        intensity = scenario.magnitude if active else 0.0
        phase = FaultPhase.ACTIVE if active else FaultPhase.INACTIVE
        return ScenarioState(scenario, phase, intensity, active)
    elif scenario.progression == "periodic":
        active = int(elapsed * 2 + scenario.seed) % 2 == 0
        intensity = scenario.magnitude if active else 0.0
        phase = FaultPhase.ACTIVE if active else FaultPhase.INACTIVE
        return ScenarioState(scenario, phase, intensity, active)
    else:
        intensity = scenario.magnitude
        phase = FaultPhase.ONSET if elapsed < 0.2 else FaultPhase.ACTIVE

    return ScenarioState(scenario, phase, intensity, True)


def load_scenarios(raw: list[dict], base_seed: int) -> list[Scenario]:
    scenarios: list[Scenario] = []
    for index, item in enumerate(raw):
        scenarios.append(
            Scenario(
                scenario_id=str(item["id"]),
                domain=FaultDomain(item["domain"]),
                category=str(item["category"]),
                fault_type=str(item["type"]),
                component=str(item["component"]),
                activation_time_s=float(item["activation_time_s"]),
                duration_s=(None if item.get("duration_s") is None else float(item["duration_s"])),
                progression=str(item.get("progression", "abrupt")),
                magnitude=float(item.get("magnitude", 1.0)),
                severity=str(item.get("severity", "major")),
                seed=int(item.get("seed", base_seed + 101 * (index + 1))),
                metadata=dict(item.get("metadata", {})),
            )
        )
    return scenarios
