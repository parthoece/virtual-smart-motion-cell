#!/usr/bin/env python3
from __future__ import annotations

import argparse
import hashlib
import json
import struct
from pathlib import Path

import pandas as pd


REQUIRED = [
    "manifest.yaml",
    "provenance.json",
    "checksums.sha256",
    "raw/capture.pcapng",
    "raw/runtime.jsonl",
    "normalized/operational/telemetry.parquet",
    "normalized/network/messages.parquet",
    "normalized/network/ethercat-pdos.parquet",
    "normalized/network/flows.parquet",
    "normalized/logs/logs.parquet",
    "ground-truth/scenario-intervals.parquet",
    "datasets/multimodal-windows.parquet",
    "metrics/metrics.json",
]


def validate_checksums(bundle: Path) -> None:
    for line in (bundle / "checksums.sha256").read_text().splitlines():
        expected, relative = line.split("  ", 1)
        path = bundle / relative
        actual = hashlib.sha256(path.read_bytes()).hexdigest()
        if actual != expected:
            raise ValueError(f"checksum mismatch: {relative}")


def validate_pcapng(path: Path) -> tuple[int, int]:
    data = path.read_bytes()
    offset = 0
    enhanced_packets = 0
    ethercat_lrw_packets = 0
    while offset < len(data):
        if offset + 12 > len(data):
            raise ValueError("truncated PCAPNG block")
        block_type, total_length = struct.unpack_from("<II", data, offset)
        if total_length < 12 or offset + total_length > len(data):
            raise ValueError("invalid PCAPNG block length")
        trailing = struct.unpack_from("<I", data, offset + total_length - 4)[0]
        if trailing != total_length:
            raise ValueError("PCAPNG block lengths disagree")
        if block_type == 6:
            enhanced_packets += 1
            captured_length = struct.unpack_from("<I", data, offset + 20)[0]
            frame_start = offset + 28
            frame = data[frame_start : frame_start + captured_length]
            if len(frame) < 17:
                raise ValueError("captured Ethernet frame is truncated")
            ether_type = struct.unpack_from("!H", frame, 12)[0]
            if ether_type != 0x88A4:
                raise ValueError(f"non-EtherCAT frame found: 0x{ether_type:04x}")
            raw_header = struct.unpack_from("<H", frame, 14)[0]
            frame_type = (raw_header >> 12) & 0x0F
            command = frame[16]
            if frame_type != 1 or command != 0x0C:
                raise ValueError("captured EtherCAT frame is not a command-frame LRW datagram")
            ethercat_lrw_packets += 1
        offset += total_length
    if offset != len(data) or enhanced_packets == 0:
        raise ValueError("PCAPNG contains no captured packets")
    return enhanced_packets, ethercat_lrw_packets


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("bundle", type=Path)
    args = parser.parse_args()
    bundle = args.bundle.resolve()
    for relative in REQUIRED:
        if not (bundle / relative).is_file():
            raise SystemExit(f"missing {relative}")

    validate_checksums(bundle)
    packet_count, ethercat_lrw_count = validate_pcapng(bundle / "raw/capture.pcapng")
    if packet_count != ethercat_lrw_count:
        raise SystemExit("not every captured packet is an EtherCAT LRW frame")
    telemetry = pd.read_parquet(bundle / "normalized/operational/telemetry.parquet")
    messages = pd.read_parquet(bundle / "normalized/network/messages.parquet")
    labels = pd.read_parquet(bundle / "ground-truth/scenario-intervals.parquet")
    windows = pd.read_parquet(bundle / "datasets/multimodal-windows.parquet")
    packets = pd.read_parquet(bundle / "normalized/network/packets.parquet")
    pdos = pd.read_parquet(bundle / "normalized/network/ethercat-pdos.parquet")

    forbidden = {"scenario_id", "fault_type", "fault_category", "target_fault_type"}
    leaked = forbidden.intersection(telemetry.columns)
    if leaked:
        raise SystemExit(f"oracle label leakage in telemetry: {sorted(leaked)}")
    if "fault_type" not in labels or "target_fault_type" not in windows:
        raise SystemExit("ground-truth or supervised target columns are missing")
    if packet_count != len(pd.read_parquet(bundle / "normalized/network/packets.parquet")):
        raise SystemExit("PCAPNG packet count does not match packets.parquet")
    if set(messages["episode_id"]) != set(telemetry["episode_id"]):
        raise SystemExit("network and operational episode sets differ")
    if set(messages["protocol"]) != {"EtherCAT"}:
        raise SystemExit("network messages are not EtherCAT")
    if set(packets["ether_type"]) != {0x88A4}:
        raise SystemExit("captured frames do not use EtherCAT EtherType 0x88A4")
    if set(packets["command"]) != {"LRW"}:
        raise SystemExit("captured EtherCAT frames do not contain LRW datagrams")
    if set(pdos["axis_id"]) != {"x", "y"}:
        raise SystemExit("EtherCAT PDO table does not contain both simulated axes")

    metrics = json.loads((bundle / "metrics/metrics.json").read_text())
    print(
        json.dumps(
            {
                "valid": True,
                "bundle": str(bundle),
                "episodes": int(telemetry["episode_id"].nunique()),
                "telemetry_rows": len(telemetry),
                "packets": packet_count,
                "messages": len(messages),
                "labels": len(labels),
                "dataset_windows": len(windows),
                "transition_hash": metrics.get("transition_hash"),
            },
            indent=2,
        )
    )


if __name__ == "__main__":
    main()
