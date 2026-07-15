# Roadmap

## v0.3 — Complete cross-platform reference architecture

- [x] .NET 10 headless machine runtime and bounded command processor
- [x] explicit Manual, Automatic, Maintenance, Recovery, and Offline modes
- [x] simulation, replay, and fault-injection motion adapters
- [x] pause, resume, controlled stop, abort, restart checkpoint, and three recovery choices
- [x] Avalonia operator HMI with operation, alarms, recovery, recipes, integration, and diagnostics
- [x] bundled Three.js digital twin with live WebSocket and recorded replay
- [x] orders, parts, cycles, traceability, alarm history, recipe lifecycle, OEE, checkpoints, and outbox
- [x] read-only OPC UA simulation information model
- [x] HTTP MES simulator with outage, latency, duplicate, and idempotency scenarios
- [x] structured logs, health, Prometheus metrics, OpenTelemetry and correlation IDs
- [x] behavioral, integration, architecture, fault, recovery, end-to-end, and reliability tests
- [x] Windows/Linux/macOS CI and six self-contained release targets
- [x] contributor SDK, governance, community-health files, SBOM and attestations


## v0.4 — Dynamic research benchmark foundation

- [x] add `VSMC-DynamicGantry-v1` with dynamic orders, queues, product loads, changeovers, and maintenance
- [x] add seeded machine-fault and network-fault scenario manifests
- [x] separate observable data from oracle ground truth
- [x] export synchronized Parquet, CSV, PCAPNG, and JSONL research bundles
- [x] add CPS-semantic message and flow tables
- [x] add multimodal fixed-window dataset views and episode-level splits
- [x] add browser Visual Experiment Studio and shared manifest execution
- [x] document the final research plan and research questions
- [ ] complete the full machine-fault library and independent fault-signature review
- [ ] add event-centered/cycle-centered datasets, schema registry, and .NET/Python conformance fixtures
- [ ] publish official baselines, IID/OOD splits, and immutable reference results
- [ ] add ONNX shadow deployment and deployment-performance metrics

## v0.5 — EtherCAT research protocol foundation

- [x] replace custom UDP research traffic with EtherCAT Ethernet frames using EtherType `0x88A4`
- [x] add LRW logical process-data cycles and Working Counter validation
- [x] add two-axis CiA 402-style CSP PDO mapping and decoded PDO tables
- [x] preserve request/return frames in PCAPNG with nanosecond timestamp resolution
- [x] model missing return frames, delay, duplication, and Working Counter mismatch
- [x] document the non-conformance, offline-emulation boundary
- [ ] validate generated captures with Wireshark/TShark in CI
- [ ] add startup/configuration datagrams and AL-state transitions
- [ ] add Distributed Clocks and mailbox/CoE research fixtures
- [ ] add an optional genuine MainDevice adapter only after security and hardware review

## v0.6 — Multi-environment research suite

- [ ] coupled-axis environment
- [ ] conveyor-tracking environment
- [ ] multi-station production-line environment
- [ ] networked-cell environment
- [ ] quality-inspection environment
- [ ] common Gymnasium-compatible Python wrapper and benchmark environment contract
- [ ] FMI model import proof of concept

## v0.7 — Controlled resilience and deception research

- [ ] allowlisted synthetic cyber-influenced effects in an isolated test network
- [ ] virtual hazard consequence and protection-effectiveness metrics
- [ ] optional controlled/private low-interaction honeypot gateway
- [ ] graded label confidence and analyst annotation workflow
- [ ] explicit privacy, ethics, legal, containment, and retention controls

## v1.0-research — Publication-grade artifact

- [ ] freeze benchmark, environment, taxonomy, and data-schema versions
- [ ] publish reference results on supported platforms
- [ ] provide one-command reproduction of tables and figures
- [ ] publish a permanent archive with checksums and persistent identifier
- [ ] complete independent external artifact reproduction

## v0.4-platform — Hardening and contributor growth

- [ ] add role-based operator/engineer/maintenance authorization
- [ ] add PostgreSQL production and outbox adapter
- [ ] add certificate-managed OPC UA security profiles and conformance-oriented tests
- [ ] add a typed MES client contract package and schema compatibility tests
- [ ] add localization catalogs and community translations
- [ ] publish C#–Python numerical parity reports with tolerances
- [ ] publish an eight-hour real-time soak report and memory trend
- [ ] add screenshot/video automation for release demonstrations
- [ ] establish external contributor ownership of one adapter

## v1.0 — Stable reference platform

- [ ] stabilize public contracts under the compatibility policy
- [ ] publish signed, attested Windows/Linux/macOS releases after green multi-OS CI
- [ ] document upgrade and data migration guarantees
- [ ] complete threat-model mitigations appropriate for authenticated deployments
- [ ] publish a plugin compatibility kit and third-party adapter registry
