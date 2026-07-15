from __future__ import annotations

import random
import struct
from dataclasses import dataclass
from pathlib import Path
from typing import Any, BinaryIO

from .ethercat import (
    BROADCAST_MAC,
    ETHERCAT_CMD_LRW,
    ETHERCAT_ETHERTYPE,
    ETHERCAT_FRAME_TYPE_COMMANDS,
    ETHERCAT_LOGICAL_PROCESS_IMAGE,
    MASTER_MAC,
    axis_pdo_from_values,
    build_ethercat_lrw_frame,
    build_process_image,
    expected_lrw_working_counter,
)


@dataclass
class NetworkCondition:
    delay_ms: float = 1.0
    jitter_ms: float = 0.2
    loss_probability: float = 0.0
    duplicate_probability: float = 0.0
    working_counter_mismatch_probability: float = 0.0


class MinimalPcapNgWriter:
    """Deterministic PCAPNG writer using LINKTYPE_ETHERNET and nanosecond timestamps."""

    def __init__(self, path: Path):
        self.handle: BinaryIO = path.open("wb")
        self._write_section_header()
        self._write_interface_description()

    @staticmethod
    def _pad(data: bytes) -> bytes:
        return data + bytes((4 - len(data) % 4) % 4)

    def _block(self, block_type: int, body: bytes) -> None:
        padded = self._pad(body)
        total_length = 12 + len(padded)
        self.handle.write(struct.pack("<II", block_type, total_length))
        self.handle.write(padded)
        self.handle.write(struct.pack("<I", total_length))

    def _write_section_header(self) -> None:
        body = struct.pack("<IHHq", 0x1A2B3C4D, 1, 0, -1)
        self._block(0x0A0D0D0A, body)

    def _write_interface_description(self) -> None:
        base = struct.pack("<HHI", 1, 0, 65535)
        # if_tsresol option: decimal 10^-9 resolution, followed by end-of-options.
        timestamp_resolution = struct.pack("<HH", 9, 1) + b"\x09" + b"\x00" * 3
        end_options = struct.pack("<HH", 0, 0)
        self._block(0x00000001, base + timestamp_resolution + end_options)

    def write(self, frame: bytes, timestamp_ns: int) -> None:
        high = (timestamp_ns >> 32) & 0xFFFFFFFF
        low = timestamp_ns & 0xFFFFFFFF
        body = struct.pack("<IIIII", 0, high, low, len(frame), len(frame)) + frame
        self._block(0x00000006, body)

    def close(self) -> None:
        self.handle.close()


class EtherCATNetwork:
    """Creates offline, wire-format EtherCAT LRW cycles without external network I/O."""

    def __init__(self, seed: int, pcap_path: Path):
        self._rng = random.Random(seed)
        self._pcap = MinimalPcapNgWriter(pcap_path)
        self._sequence = 0
        self.packets: list[dict[str, Any]] = []
        self.messages: list[dict[str, Any]] = []
        self.pdos: list[dict[str, Any]] = []

    def close(self) -> None:
        self._pcap.close()

    def exchange(
        self,
        *,
        experiment_id: str,
        episode_id: str,
        simulation_time_ns: int,
        wall_time_s: float,
        machine_state: str,
        production_step: str,
        cycle_id: str | None,
        part_id: str | None,
        values: dict[str, Any],
        condition: NetworkCondition,
    ) -> None:
        self._sequence += 1
        exchange_id = f"{episode_id}-ECAT-{self._sequence:08d}"
        datagram_index = self._sequence & 0xFF
        response_dropped = self._rng.random() < condition.loss_probability
        response_duplicated = self._rng.random() < condition.duplicate_probability
        latency_ms = max(0.0, condition.delay_ms + self._rng.gauss(0, condition.jitter_ms))

        axes = [
            axis_pdo_from_values(
                axis_id="x",
                slave_position=1,
                command_position=float(values["x_command_position"]),
                actual_position=float(values["x_actual_position"]),
                velocity=float(values["x_velocity"]),
                following_error=float(values["x_following_error"]),
                machine_state=machine_state,
            ),
            axis_pdo_from_values(
                axis_id="y",
                slave_position=2,
                command_position=float(values["y_command_position"]),
                actual_position=float(values["y_actual_position"]),
                velocity=float(values["y_velocity"]),
                following_error=float(values["y_following_error"]),
                machine_state=machine_state,
            ),
        ]
        request_data = build_process_image(axes, response=False)
        response_data = build_process_image(axes, response=True)
        expected_wkc = expected_lrw_working_counter(len(axes))
        wkc_mismatch = (
            not response_dropped
            and self._rng.random() < condition.working_counter_mismatch_probability
        )
        actual_wkc = max(0, expected_wkc - 3) if wkc_mismatch else expected_wkc

        request_frame = build_ethercat_lrw_frame(
            process_data=request_data,
            datagram_index=datagram_index,
            working_counter=0,
        )
        response_frame = build_ethercat_lrw_frame(
            process_data=response_data,
            datagram_index=datagram_index,
            working_counter=actual_wkc,
        )
        request_timestamp_ns = int(wall_time_s * 1e9)
        response_timestamp_ns = int((wall_time_s + latency_ms / 1000.0) * 1e9)

        self._write_packet(
            frame=request_frame,
            timestamp_ns=request_timestamp_ns,
            experiment_id=experiment_id,
            episode_id=episode_id,
            exchange_id=exchange_id,
            packet_id=f"{exchange_id}-TX",
            simulation_time_ns=simulation_time_ns,
            direction="master_to_segment",
            datagram_index=datagram_index,
            working_counter=0,
            expected_wkc=expected_wkc,
            duplicate=False,
            latency_ms=0.0,
            machine_state=machine_state,
            production_step=production_step,
        )

        if not response_dropped:
            copies = 2 if response_duplicated else 1
            for copy_index in range(copies):
                duplicate_delay_ns = copy_index * 100_000
                self._write_packet(
                    frame=response_frame,
                    timestamp_ns=response_timestamp_ns + duplicate_delay_ns,
                    experiment_id=experiment_id,
                    episode_id=episode_id,
                    exchange_id=exchange_id,
                    packet_id=f"{exchange_id}-RX-{copy_index + 1}",
                    simulation_time_ns=simulation_time_ns,
                    direction="segment_to_master",
                    datagram_index=datagram_index,
                    working_counter=actual_wkc,
                    expected_wkc=expected_wkc,
                    duplicate=copy_index > 0,
                    latency_ms=latency_ms,
                    machine_state=machine_state,
                    production_step=production_step,
                )

        message = {
            "experiment_id": experiment_id,
            "episode_id": episode_id,
            "message_id": exchange_id,
            "simulation_time_ns": simulation_time_ns,
            "protocol": "EtherCAT",
            "ether_type": ETHERCAT_ETHERTYPE,
            "frame_type": ETHERCAT_FRAME_TYPE_COMMANDS,
            "operation": "logical_read_write",
            "message_type": "cyclic_process_data",
            "message_direction": "bidirectional_process_data",
            "source_asset_id": "ethercat-main-device",
            "destination_asset_id": "motion-segment",
            "target_component": "dynamic_gantry",
            "sequence_number": self._sequence,
            "datagram_index": datagram_index,
            "command": "LRW",
            "command_code": ETHERCAT_CMD_LRW,
            "logical_address": ETHERCAT_LOGICAL_PROCESS_IMAGE,
            "latency_ms": latency_ms,
            "dropped": response_dropped,
            "duplicated": response_duplicated,
            "response_received": not response_dropped,
            "expected_working_counter": expected_wkc,
            "actual_working_counter": None if response_dropped else actual_wkc,
            "working_counter_valid": not response_dropped and actual_wkc == expected_wkc,
            "slave_count": len(axes),
            "machine_state": machine_state,
            "production_step": production_step,
            "cycle_id": cycle_id,
            "part_id": part_id,
            "payload_length": len(response_data),
            "cia402_mode": "cyclic_synchronous_position",
        }
        self.messages.append(message)
        for axis in axes:
            self.pdos.append(
                {
                    "experiment_id": experiment_id,
                    "episode_id": episode_id,
                    "message_id": exchange_id,
                    "simulation_time_ns": simulation_time_ns,
                    "datagram_index": datagram_index,
                    "logical_address": ETHERCAT_LOGICAL_PROCESS_IMAGE,
                    "response_received": not response_dropped,
                    "working_counter_valid": message["working_counter_valid"],
                    "machine_state": machine_state,
                    "production_step": production_step,
                    "cycle_id": cycle_id,
                    "part_id": part_id,
                    **axis.semantic_row(),
                }
            )

    def _write_packet(
        self,
        *,
        frame: bytes,
        timestamp_ns: int,
        experiment_id: str,
        episode_id: str,
        exchange_id: str,
        packet_id: str,
        simulation_time_ns: int,
        direction: str,
        datagram_index: int,
        working_counter: int,
        expected_wkc: int,
        duplicate: bool,
        latency_ms: float,
        machine_state: str,
        production_step: str,
    ) -> None:
        self._pcap.write(frame, timestamp_ns)
        self.packets.append(
            {
                "experiment_id": experiment_id,
                "episode_id": episode_id,
                "packet_id": packet_id,
                "message_id": exchange_id,
                "simulation_time_ns": simulation_time_ns,
                "capture_time_ns": timestamp_ns,
                "source_mac": MASTER_MAC,
                "destination_mac": BROADCAST_MAC,
                "ether_type": ETHERCAT_ETHERTYPE,
                "transport_protocol": "ethernet",
                "application_protocol": "ethercat",
                "frame_direction": direction,
                "frame_type": ETHERCAT_FRAME_TYPE_COMMANDS,
                "command": "LRW",
                "command_code": ETHERCAT_CMD_LRW,
                "datagram_index": datagram_index,
                "logical_address": ETHERCAT_LOGICAL_PROCESS_IMAGE,
                "working_counter": working_counter,
                "expected_working_counter": expected_wkc,
                "working_counter_valid": direction == "master_to_segment"
                or working_counter == expected_wkc,
                "frame_length": len(frame),
                "duplicate": duplicate,
                "latency_ms": latency_ms,
                "machine_state": machine_state,
                "production_step": production_step,
            }
        )


# Compatibility alias for third-party code written against the 0.4 research package.
SyntheticNetwork = EtherCATNetwork
