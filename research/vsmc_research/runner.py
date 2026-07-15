from __future__ import annotations

import hashlib
import math
import tempfile
import time
from pathlib import Path
from typing import Any

from .dataset import aggregate_flows, build_multimodal_windows
from .environment import DynamicGantryEnvironment
from .faults import load_scenarios
from .manifest import Manifest
from .network import EtherCATNetwork, NetworkCondition
from .recorder import ExperimentRecorder, copy_pcap


def _network_condition(manifest: dict[str, Any], scenario_states: list[Any]) -> NetworkCondition:
    config = manifest.get("network", {})
    condition = NetworkCondition(
        delay_ms=float(config.get("base_delay_ms", 1.0)),
        jitter_ms=float(config.get("base_jitter_ms", 0.2)),
        loss_probability=float(config.get("base_loss_probability", 0.0)),
        duplicate_probability=float(config.get("base_duplicate_probability", 0.0)),
    )
    for state in scenario_states:
        if not state.active or state.scenario.domain.value != "network":
            continue
        fault_type = state.scenario.fault_type
        if fault_type == "message_delay":
            condition.delay_ms += state.intensity
            condition.jitter_ms += state.intensity * 0.1
        elif fault_type == "packet_loss":
            condition.loss_probability = min(0.95, condition.loss_probability + state.intensity)
        elif fault_type == "message_duplication":
            condition.duplicate_probability = min(
                0.95, condition.duplicate_probability + state.intensity
            )
        elif fault_type == "working_counter_mismatch":
            condition.working_counter_mismatch_probability = min(
                0.95, condition.working_counter_mismatch_probability + state.intensity
            )
    return condition


def _build_intervals(
    manifest: Manifest, scenarios: list[Any], episode_id: str
) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    for scenario in scenarios:
        start = scenario.activation_time_s
        end = scenario.end_time_s(manifest.duration_s)
        if scenario.progression == "gradual":
            midpoint = start + 0.4 * (end - start)
            phases = [("incipient", start, midpoint), ("active", midpoint, end)]
        else:
            phases = [("active", start, end)]
        for phase, phase_start, phase_end in phases:
            rows.append(
                {
                    "experiment_id": manifest.experiment_id,
                    "episode_id": episode_id,
                    "label_id": f"{episode_id}-{scenario.scenario_id}-{phase}",
                    "scenario_id": scenario.scenario_id,
                    "start_time_ns": int(phase_start * 1e9),
                    "end_time_ns": int(phase_end * 1e9),
                    "domain": scenario.domain.value,
                    "category": scenario.category,
                    "fault_type": scenario.fault_type,
                    "component": scenario.component,
                    "phase": phase,
                    "severity": scenario.severity,
                    "progression": scenario.progression,
                    "root_cause_id": scenario.scenario_id,
                    "seed": scenario.seed,
                    "label_source": "oracle",
                    "confidence": 1.0,
                    "label_schema_version": "1.0.0",
                }
            )
    return rows


def run_experiment(manifest: Manifest, output_root: Path) -> Path:
    output_root.mkdir(parents=True, exist_ok=True)
    recorder = ExperimentRecorder(output_root, manifest.experiment_id, manifest.data)
    seeds = [manifest.base_seed + episode * 1009 for episode in range(manifest.episodes)]
    recorder.write_provenance(manifest.sha256(), seeds)
    all_packets: list[dict[str, Any]] = []
    all_messages: list[dict[str, Any]] = []
    temporary_pcap = Path(tempfile.mkstemp(prefix="vsmc-", suffix=".pcapng")[1])
    network = EtherCATNetwork(manifest.base_seed + 9001, temporary_pcap)
    start_wall = time.time()
    start_perf = time.perf_counter()
    total_steps = 0

    try:
        for episode_index, seed in enumerate(seeds):
            episode_id = f"{manifest.experiment_id}-EP-{episode_index + 1:04d}"
            episode_wall_offset_s = episode_index * (manifest.duration_s + 1.0)
            environment = DynamicGantryEnvironment(manifest.data, seed, episode_id)
            scenarios = load_scenarios(manifest.data.get("scenarios", []), seed)
            environment.set_scenarios(scenarios)
            recorder.scenario_intervals.extend(_build_intervals(manifest, scenarios, episode_id))
            steps = math.ceil(manifest.duration_s / manifest.dt_s)

            for _ in range(steps):
                output = environment.step()
                total_steps += 1
                telemetry = {
                    "experiment_id": manifest.experiment_id,
                    "episode_id": episode_id,
                    **output.telemetry,
                }
                recorder.telemetry.append(telemetry)
                recorder.production.extend(
                    {
                        "experiment_id": manifest.experiment_id,
                        "episode_id": episode_id,
                        **event,
                    }
                    for event in output.production_events
                )
                recorder.logs.extend(
                    {
                        "experiment_id": manifest.experiment_id,
                        "episode_id": episode_id,
                        "source_id": "machine-runtime",
                        **log,
                    }
                    for log in output.logs
                )

                if output.telemetry["step_index"] % max(1, int(1 / manifest.dt_s)) == 0:
                    sync_id = f"{episode_id}-SYNC-{output.telemetry['step_index']:08d}"
                    recorder.sync_events.append(
                        {
                            "sync_id": sync_id,
                            "experiment_id": manifest.experiment_id,
                            "episode_id": episode_id,
                            "source_id": "machine-runtime",
                            "simulation_time_ns": output.telemetry["simulation_time_ns"],
                            "wall_time_utc_ns": int(
                                (start_wall + episode_wall_offset_s + environment.time_s) * 1e9
                            ),
                            "quality": "oracle",
                        }
                    )

                if output.network_publish_due:
                    condition = _network_condition(manifest.data, output.scenario_states)
                    network.exchange(
                        experiment_id=manifest.experiment_id,
                        episode_id=episode_id,
                        simulation_time_ns=output.telemetry["simulation_time_ns"],
                        wall_time_s=start_wall + episode_wall_offset_s + environment.time_s,
                        values={
                            "x_command_position": output.telemetry["x_command_position"],
                            "x_actual_position": output.telemetry["x_measured_position"],
                            "x_velocity": output.telemetry["x_velocity"],
                            "x_following_error": output.telemetry["x_following_error"],
                            "y_command_position": output.telemetry["y_command_position"],
                            "y_actual_position": output.telemetry["y_measured_position"],
                            "y_velocity": output.telemetry["y_velocity"],
                            "y_following_error": output.telemetry["y_following_error"],
                        },
                        machine_state=output.telemetry["machine_state"],
                        production_step=output.telemetry["production_step"],
                        cycle_id=output.telemetry["cycle_id"],
                        part_id=output.telemetry["part_id"],
                        condition=condition,
                    )

                if manifest.data.get("execution", {}).get("mode") == "real_time":
                    target_elapsed = total_steps * manifest.dt_s
                    remaining = target_elapsed - (time.perf_counter() - start_perf)
                    if remaining > 0:
                        time.sleep(remaining)
    finally:
        network.close()

    all_packets.extend(network.packets)
    all_messages.extend(network.messages)
    all_pdos = list(network.pdos)
    flows = aggregate_flows(all_messages)
    windows = build_multimodal_windows(
        recorder.telemetry,
        all_messages,
        recorder.logs,
        recorder.scenario_intervals,
        window_s=float(manifest.data.get("datasets", {}).get("window_s", 1.0)),
    )
    transition_sequence = "|".join(
        str(row.get("current"))
        for row in recorder.production
        if row.get("event_type") == "machine.state_changed"
    )
    completed_by_episode: dict[str, int] = {}
    good_by_episode: dict[str, int] = {}
    rejected_by_episode: dict[str, int] = {}
    for row in recorder.telemetry:
        episode = str(row["episode_id"])
        completed_by_episode[episode] = max(
            completed_by_episode.get(episode, 0), int(row.get("cycle_count", 0))
        )
        good_by_episode[episode] = max(
            good_by_episode.get(episode, 0), int(row.get("good_count", 0))
        )
        rejected_by_episode[episode] = max(
            rejected_by_episode.get(episode, 0), int(row.get("rejected_count", 0))
        )
    condition_counts = (
        windows["target_operational_condition"].value_counts().to_dict()
        if not windows.empty
        else {}
    )
    split_counts = windows["dataset_split"].value_counts().to_dict() if not windows.empty else {}
    metrics = {
        "experiment_id": manifest.experiment_id,
        "episodes": manifest.episodes,
        "steps": total_steps,
        "telemetry_rows": len(recorder.telemetry),
        "network_messages": len(all_messages),
        "captured_packets": len(all_packets),
        "ethercat_pdo_rows": len(all_pdos),
        "ethercat_protocol": "EtherCAT LRW / CiA 402-style PDO mapping",
        "production_events": len(recorder.production),
        "ground_truth_intervals": len(recorder.scenario_intervals),
        "dataset_windows": len(windows),
        "completed_cycles": sum(completed_by_episode.values()),
        "good_parts": sum(good_by_episode.values()),
        "rejected_parts": sum(rejected_by_episode.values()),
        "max_following_error": max(
            (float(x.get("max_following_error", 0)) for x in recorder.telemetry), default=0.0
        ),
        "condition_counts": condition_counts,
        "dataset_split_counts": split_counts,
        "transition_hash": hashlib.sha256(transition_sequence.encode()).hexdigest(),
        "elapsed_wall_s": round(time.perf_counter() - start_perf, 4),
        "manifest_sha256": manifest.sha256(),
    }
    bundle = recorder.finalize(
        packets=all_packets,
        messages=all_messages,
        pdos=all_pdos,
        flows=flows,
        datasets={"multimodal-windows": windows},
        metrics=metrics,
    )
    copy_pcap(temporary_pcap, bundle)
    # Checksums need to include the PCAPNG moved after finalization.
    recorder._write_checksums()  # noqa: SLF001 - internal finalization within package
    return bundle
