# Changelog

## 0.5.0 — EtherCAT research protocol foundation

- Replaced the custom JSON-over-UDP motion research traffic with offline wire-format EtherCAT Ethernet frames.
- Added EtherType `0x88A4`, command-frame headers, LRW datagrams, logical process-image addressing, and Working Counter validation.
- Added a documented two-axis CiA 402-style cyclic synchronous position PDO mapping.
- Added request/return packet capture, decoded EtherCAT exchange records, and `ethercat-pdos.parquet`.
- Changed packet-loss injection to preserve the outgoing cycle while removing the returned frame.
- Added EtherCAT protocol tests, bundle validation, documentation, and an architecture decision record.

## 0.4.0 — Dynamic research benchmark foundation

- Added a seeded hybrid dynamic gantry environment with orders, queues, product variation, changeovers, and maintenance.
- Added machine-fault-first and network-fault experiment manifests.
- Added separate oracle ground truth and observable data planes.
- Added synchronized Parquet, CSV, PCAPNG, JSONL, metrics, provenance, and checksum bundles.
- Added CPS-semantic packet, message, and flow extraction.
- Added multimodal dataset windows and episode-level split assignment.
- Added the browser Visual Experiment Studio and research CLI.
- Added research roadmap, questions, publication plan, labeling, synchronization, and cyber/honeypot boundary documents.


All notable changes follow Keep a Changelog and semantic versioning.

## [Unreleased]

### Planned

- role-based command authorization
- PostgreSQL production adapter
- secure OPC UA profiles and conformance-oriented testing
- published eight-hour soak evidence

## [0.3.0] - 2026-07-14

### Added

- simulation, replay, and fault-injection motion adapters behind `IMotionSystem`
- Manual, Automatic, Maintenance, Recovery, and Offline operation
- production orders, parts, cycles, recipes, traceability, OEE, alarm history, checkpoints, and outbox
- three explicit restart-recovery choices
- read-only OPC UA simulation server
- HTTP MES simulator with outages, latency, duplicates, polling, and idempotent result delivery
- OpenTelemetry traces and metrics with correlation propagation
- complete Avalonia operator workflow and bundled Three.js digital twin
- expanded integration, architecture, recovery, viewer, end-to-end, and reliability evidence
- six-target self-contained release automation with SBOMs and attestations

### Changed

- machine coordinator now depends on motion ports rather than constructing simulation internals
- browser viewer now uses a locked and bundled Three.js dependency instead of raw WebGL
- reliability campaign supports both deterministic cycle count and real-time soak duration

## [0.2.0] - 2026-07-14

### Added

- cross-platform .NET architecture baseline
- Avalonia HMI baseline
- REST, WebSocket, health and metrics interfaces
- bounded command processing and persistent reference stores

## [0.1.0] - 2026-07-14

### Added

- tested Python virtual motion-cell baseline with reports and deterministic fault scenarios
