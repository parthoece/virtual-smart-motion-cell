from __future__ import annotations

import struct
from dataclasses import dataclass
from typing import Any

ETHERCAT_ETHERTYPE = 0x88A4
ETHERCAT_FRAME_TYPE_COMMANDS = 0x01
ETHERCAT_CMD_LRW = 0x0C
ETHERCAT_LOGICAL_PROCESS_IMAGE = 0x00001000
CIA402_MODE_CSP = 8
POSITION_COUNTS_PER_UNIT = 1_000_000
VELOCITY_COUNTS_PER_UNIT = 1_000_000

MASTER_MAC = "02:00:00:00:ec:01"
BROADCAST_MAC = "ff:ff:ff:ff:ff:ff"


@dataclass(frozen=True)
class AxisPdo:
    axis_id: str
    slave_position: int
    controlword: int
    mode_of_operation: int
    target_position_counts: int
    target_velocity_counts: int
    statusword: int
    mode_display: int
    actual_position_counts: int
    actual_velocity_counts: int
    following_error_counts: int
    error_code: int = 0

    def request_bytes(self) -> bytes:
        return struct.pack(
            "<Hbii",
            self.controlword,
            self.mode_of_operation,
            self.target_position_counts,
            self.target_velocity_counts,
        ) + bytes(17)

    def response_bytes(self) -> bytes:
        return struct.pack(
            "<HbiiHbiiiH",
            self.controlword,
            self.mode_of_operation,
            self.target_position_counts,
            self.target_velocity_counts,
            self.statusword,
            self.mode_display,
            self.actual_position_counts,
            self.actual_velocity_counts,
            self.following_error_counts,
            self.error_code,
        )

    def semantic_row(self) -> dict[str, Any]:
        return {
            "axis_id": self.axis_id,
            "slave_position": self.slave_position,
            "cia402_mode": "cyclic_synchronous_position",
            "mode_of_operation": self.mode_of_operation,
            "controlword": self.controlword,
            "statusword": self.statusword,
            "target_position_counts": self.target_position_counts,
            "target_velocity_counts": self.target_velocity_counts,
            "actual_position_counts": self.actual_position_counts,
            "actual_velocity_counts": self.actual_velocity_counts,
            "following_error_counts": self.following_error_counts,
            "error_code": self.error_code,
            "target_position": self.target_position_counts / POSITION_COUNTS_PER_UNIT,
            "target_velocity": self.target_velocity_counts / VELOCITY_COUNTS_PER_UNIT,
            "actual_position": self.actual_position_counts / POSITION_COUNTS_PER_UNIT,
            "actual_velocity": self.actual_velocity_counts / VELOCITY_COUNTS_PER_UNIT,
            "following_error": self.following_error_counts / POSITION_COUNTS_PER_UNIT,
        }


@dataclass(frozen=True)
class EtherCATDatagram:
    command: int
    index: int
    logical_address: int
    data: bytes
    working_counter: int
    irq: int = 0
    circulating: bool = False
    has_next: bool = False

    def to_bytes(self) -> bytes:
        if len(self.data) > 0x7FF:
            raise ValueError("EtherCAT datagram data exceeds 11-bit length field")
        flags = len(self.data)
        if self.circulating:
            flags |= 1 << 14
        if self.has_next:
            flags |= 1 << 15
        header = struct.pack(
            "<BBIHH",
            self.command,
            self.index,
            self.logical_address,
            flags,
            self.irq,
        )
        return header + self.data + struct.pack("<H", self.working_counter)


def _mac_bytes(value: str) -> bytes:
    parts = value.split(":")
    if len(parts) != 6:
        raise ValueError(f"Invalid MAC address: {value}")
    return bytes(int(part, 16) for part in parts)


def build_process_image(axes: list[AxisPdo], *, response: bool) -> bytes:
    if not axes:
        raise ValueError("At least one axis PDO is required")
    if response:
        return b"".join(axis.response_bytes() for axis in axes)
    return b"".join(axis.request_bytes() for axis in axes)


def expected_lrw_working_counter(slave_count: int) -> int:
    if slave_count < 0:
        raise ValueError("slave_count cannot be negative")
    # An LRW contributes one successful logical read and one successful logical write.
    # EtherCAT encodes these as +1 and +2 respectively per addressed SubDevice.
    return slave_count * 3


def build_ethercat_lrw_frame(
    *,
    process_data: bytes,
    datagram_index: int,
    working_counter: int,
    logical_address: int = ETHERCAT_LOGICAL_PROCESS_IMAGE,
    source_mac: str = MASTER_MAC,
    destination_mac: str = BROADCAST_MAC,
) -> bytes:
    datagram = EtherCATDatagram(
        command=ETHERCAT_CMD_LRW,
        index=datagram_index & 0xFF,
        logical_address=logical_address,
        data=process_data,
        working_counter=working_counter,
    ).to_bytes()
    if len(datagram) > 0x7FF:
        raise ValueError("EtherCAT command payload exceeds 11-bit frame length field")
    frame_header = struct.pack("<H", len(datagram) | (ETHERCAT_FRAME_TYPE_COMMANDS << 12))
    ethernet_header = (
        _mac_bytes(destination_mac) + _mac_bytes(source_mac) + struct.pack("!H", ETHERCAT_ETHERTYPE)
    )
    frame = ethernet_header + frame_header + datagram
    # Ethernet captures commonly omit the 4-byte FCS. Pad to 60 captured bytes.
    if len(frame) < 60:
        frame += bytes(60 - len(frame))
    return frame


def parse_ethercat_lrw_frame(frame: bytes) -> dict[str, Any]:
    if len(frame) < 28:
        raise ValueError("Frame is too short to contain EtherCAT LRW data")
    destination_mac = ":".join(f"{byte:02x}" for byte in frame[0:6])
    source_mac = ":".join(f"{byte:02x}" for byte in frame[6:12])
    ether_type = struct.unpack_from("!H", frame, 12)[0]
    if ether_type != ETHERCAT_ETHERTYPE:
        raise ValueError(f"Unexpected EtherType 0x{ether_type:04x}")
    raw_header = struct.unpack_from("<H", frame, 14)[0]
    frame_length = raw_header & 0x07FF
    frame_type = (raw_header >> 12) & 0x0F
    datagram_start = 16
    datagram_end = datagram_start + frame_length
    if datagram_end > len(frame):
        raise ValueError("EtherCAT frame length exceeds captured bytes")
    command, index, logical_address, flags, irq = struct.unpack_from(
        "<BBIHH", frame, datagram_start
    )
    data_length = flags & 0x07FF
    data_start = datagram_start + 10
    data_end = data_start + data_length
    if data_end + 2 > datagram_end:
        raise ValueError("EtherCAT datagram length is inconsistent with frame length")
    working_counter = struct.unpack_from("<H", frame, data_end)[0]
    return {
        "destination_mac": destination_mac,
        "source_mac": source_mac,
        "ether_type": ether_type,
        "frame_type": frame_type,
        "frame_payload_length": frame_length,
        "command": command,
        "command_name": "LRW" if command == ETHERCAT_CMD_LRW else f"0x{command:02x}",
        "datagram_index": index,
        "logical_address": logical_address,
        "data_length": data_length,
        "circulating": bool(flags & (1 << 14)),
        "has_next": bool(flags & (1 << 15)),
        "irq": irq,
        "working_counter": working_counter,
        "data": frame[data_start:data_end],
    }


def axis_pdo_from_values(
    *,
    axis_id: str,
    slave_position: int,
    command_position: float,
    actual_position: float,
    velocity: float,
    following_error: float,
    machine_state: str,
) -> AxisPdo:
    drive_fault = machine_state == "faulted"
    controlword = 0x0000 if drive_fault else 0x000F
    statusword = 0x0008 if drive_fault else 0x0027
    if not drive_fault and abs(following_error) <= 0.025 and abs(velocity) <= 0.08:
        statusword |= 1 << 10  # Target reached.
    if abs(following_error) > 0.15:
        statusword |= 1 << 7  # Warning.
    return AxisPdo(
        axis_id=axis_id,
        slave_position=slave_position,
        controlword=controlword,
        mode_of_operation=CIA402_MODE_CSP,
        target_position_counts=int(round(command_position * POSITION_COUNTS_PER_UNIT)),
        target_velocity_counts=0,
        statusword=statusword,
        mode_display=CIA402_MODE_CSP,
        actual_position_counts=int(round(actual_position * POSITION_COUNTS_PER_UNIT)),
        actual_velocity_counts=int(round(velocity * VELOCITY_COUNTS_PER_UNIT)),
        following_error_counts=int(round(following_error * POSITION_COUNTS_PER_UNIT)),
    )
