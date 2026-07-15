from pathlib import Path

from vsmc_research.environment import DynamicGantryEnvironment
from vsmc_research.faults import load_scenarios
from vsmc_research.manifest import load_manifest


ROOT = Path(__file__).resolve().parents[2]


def run_trace(path: Path) -> tuple[list[str], int]:
    manifest = load_manifest(path)
    environment = DynamicGantryEnvironment(manifest.data, manifest.base_seed, "EP-1")
    environment.set_scenarios(
        load_scenarios(manifest.data.get("scenarios", []), manifest.base_seed)
    )
    states = []
    for _ in range(1200):
        output = environment.step()
        states.append(output.telemetry["machine_state"])
    return states, environment.cycle_count


def test_dynamic_environment_is_deterministic() -> None:
    path = ROOT / "benchmarks/manifests/machine-fault.yaml"
    first = run_trace(path)
    second = run_trace(path)
    assert first == second
    assert first[1] > 0


def test_machine_fault_changes_observable_dynamics() -> None:
    normal = load_manifest(ROOT / "benchmarks/manifests/normal-operation.yaml")
    fault = load_manifest(ROOT / "benchmarks/manifests/machine-fault.yaml")
    normal_env = DynamicGantryEnvironment(normal.data, 123, "N")
    fault_env = DynamicGantryEnvironment(fault.data, 123, "F")
    fault_env.set_scenarios(load_scenarios(fault.data["scenarios"], 123))
    normal_errors = []
    fault_errors = []
    for _ in range(2200):
        normal_errors.append(normal_env.step().telemetry["max_following_error"])
        fault_errors.append(fault_env.step().telemetry["max_following_error"])
    assert max(fault_errors[1200:]) > max(normal_errors[1200:])
