from __future__ import annotations

from typing import Any

import hashlib
import numpy as np
import pandas as pd


def aggregate_flows(messages: list[dict[str, Any]], window_s: float = 1.0) -> list[dict[str, Any]]:
    if not messages:
        return []
    frame = pd.DataFrame(messages)
    frame["window_index"] = (frame["simulation_time_ns"] / (window_s * 1e9)).astype(int)
    rows: list[dict[str, Any]] = []
    for (episode_id, window_index), group in frame.groupby(["episode_id", "window_index"]):
        rows.append(
            {
                "episode_id": episode_id,
                "window_start_ns": int(window_index * window_s * 1e9),
                "window_end_ns": int((window_index + 1) * window_s * 1e9),
                "source_asset_id": group["source_asset_id"].iloc[0],
                "destination_asset_id": group["destination_asset_id"].iloc[0],
                "protocol": group["protocol"].iloc[0],
                "message_count": int(len(group)),
                "bytes_total": int(group["payload_length"].sum()),
                "mean_latency_ms": float(group["latency_ms"].mean()),
                "p95_latency_ms": float(group["latency_ms"].quantile(0.95)),
                "jitter_ms": float(group["latency_ms"].std(ddof=0)),
                "dropped_count": int(group["dropped"].sum()),
                "duplicate_count": int(group["duplicated"].sum()),
                "command_count": int((group["message_direction"] == "command").sum()),
                "telemetry_count": int((group["message_direction"] == "telemetry").sum()),
                "machine_state_mode": group["machine_state"].mode().iloc[0],
                "production_step_mode": group["production_step"].mode().iloc[0],
            }
        )
    return rows


def build_multimodal_windows(
    telemetry: list[dict[str, Any]],
    messages: list[dict[str, Any]],
    logs: list[dict[str, Any]],
    intervals: list[dict[str, Any]],
    window_s: float = 1.0,
) -> pd.DataFrame:
    if not telemetry:
        return pd.DataFrame()
    telem = pd.DataFrame(telemetry)
    telem["window_index"] = (telem["simulation_time_ns"] / (window_s * 1e9)).astype(int)
    grouped = telem.groupby(["episode_id", "window_index"])
    features = grouped.agg(
        window_start_ns=("simulation_time_ns", "min"),
        window_end_ns=("simulation_time_ns", "max"),
        x_following_error_mean=("x_following_error", "mean"),
        x_following_error_max=("x_following_error", lambda x: float(np.abs(x).max())),
        y_following_error_mean=("y_following_error", "mean"),
        y_following_error_max=("y_following_error", lambda x: float(np.abs(x).max())),
        x_control_effort_mean=("x_control_effort", "mean"),
        y_control_effort_mean=("y_control_effort", "mean"),
        cycle_count=("cycle_count", "max"),
        input_queue_mean=("input_queue", "mean"),
        output_count=("output_count", "max"),
        rework_count=("rework_count", "max"),
        machine_state_mode=("machine_state", lambda x: x.mode().iloc[0]),
    ).reset_index()

    if messages:
        msg = pd.DataFrame(messages)
        msg["window_index"] = (msg["simulation_time_ns"] / (window_s * 1e9)).astype(int)
        net = (
            msg.groupby(["episode_id", "window_index"])
            .agg(
                network_message_count=("message_id", "count"),
                network_latency_mean_ms=("latency_ms", "mean"),
                network_latency_p95_ms=("latency_ms", lambda x: float(x.quantile(0.95))),
                network_drop_count=("dropped", "sum"),
                network_duplicate_count=("duplicated", "sum"),
            )
            .reset_index()
        )
        features = features.merge(net, on=["episode_id", "window_index"], how="left")

    if logs:
        log_frame = pd.DataFrame(logs)
        log_frame["window_index"] = (log_frame["simulation_time_ns"] / (window_s * 1e9)).astype(int)
        log_frame["is_warning"] = log_frame["severity"].isin(["warning", "error"])
        log_features = (
            log_frame.groupby(["episode_id", "window_index"])
            .agg(log_count=("event_code", "count"), warning_or_error_count=("is_warning", "sum"))
            .reset_index()
        )
        features = features.merge(log_features, on=["episode_id", "window_index"], how="left")

    for column in [
        "network_message_count",
        "network_latency_mean_ms",
        "network_latency_p95_ms",
        "network_drop_count",
        "network_duplicate_count",
        "log_count",
        "warning_or_error_count",
    ]:
        if column not in features:
            features[column] = 0
        features[column] = features[column].fillna(0)

    features["target_operational_condition"] = "normal"
    features["target_fault_domain"] = "none"
    features["target_fault_category"] = "none"
    features["target_fault_type"] = "none"
    features["target_component"] = "none"
    features["target_severity"] = "none"
    features["target_multi_label"] = ""

    for index, row in features.iterrows():
        active = [
            interval
            for interval in intervals
            if interval["episode_id"] == row["episode_id"]
            and interval["start_time_ns"] <= row["window_end_ns"]
            and interval["end_time_ns"] > row["window_start_ns"]
            and interval["phase"] != "inactive"
        ]
        if not active:
            continue
        domains = sorted({str(item["domain"]) for item in active})
        if domains == ["machine"]:
            condition = "machine_fault"
        elif domains == ["network"]:
            condition = "network_fault"
        else:
            condition = "combined"
        primary = next((item for item in active if item["domain"] == "machine"), active[0])
        features.at[index, "target_operational_condition"] = condition
        features.at[index, "target_fault_domain"] = primary["domain"]
        features.at[index, "target_fault_category"] = primary["category"]
        features.at[index, "target_fault_type"] = primary["fault_type"]
        features.at[index, "target_component"] = primary["component"]
        features.at[index, "target_severity"] = primary["severity"]
        features.at[index, "target_multi_label"] = "|".join(
            sorted(f"{x['domain']}:{x['fault_type']}:{x['component']}" for x in active)
        )

    def split(episode_id: str) -> str:
        number = int(hashlib.sha256(episode_id.encode()).hexdigest()[:8], 16) % 10
        return "train" if number < 7 else ("validation" if number < 9 else "test")

    features["dataset_split"] = features["episode_id"].map(split)
    return features
