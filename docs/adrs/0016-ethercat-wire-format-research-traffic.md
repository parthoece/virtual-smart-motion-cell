# ADR 0016 — Use wire-format EtherCAT for the research motion segment

## Status

Accepted.

## Context

The original research foundation used JSON over synthetic UDP. That representation was deterministic but did not reflect motion-control fieldbus framing, logical process images, or EtherCAT Working Counter behavior.

## Decision

The benchmark now emits offline Ethernet frames with EtherType `0x88A4`, EtherCAT command-frame headers, LRW datagrams, and a documented two-axis CiA 402-style process image. Both the outgoing request and returned frame are preserved in PCAPNG. Typed packet, exchange, and PDO tables are derived from the same in-memory frame model.

## Consequences

- packet captures contain recognizable EtherCAT wire structures;
- network faults can be expressed as missing, delayed, duplicated, or Working-Counter-invalid cycles;
- ML features can include protocol and process-image semantics;
- the project no longer depends on a custom UDP application protocol for motion-segment research;
- the implementation remains an offline emulator and must not be described as a conformant or hardware-capable EtherCAT MainDevice.
