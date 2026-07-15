# Portfolio and recruiter demo

## 30-second explanation

> Virtual Smart Motion Cell is a cross-platform equipment-software reference architecture. A headless .NET runtime owns machine sequencing, simulated motion, alarms, recipes, recovery checkpoints, traceability, and immutable state. An Avalonia operator HMI and a browser Three.js digital twin are independent clients. The project demonstrates how vendor-specific controllers and factory integrations can be added through adapters without changing the core machine domain.

## Evidence to show

1. Architecture diagram and project boundaries.
2. Unsafe command rejection with human-readable reasons.
3. Normal automatic cycle in both clients.
4. Guard-open or bus-loss fault and alarm lifecycle.
5. Browser disconnect while runtime continues.
6. Event history, checkpoint, metrics, and outbox files.
7. Multi-OS CI and RID-specific releases.
8. A small contributor adapter built from the SDK tutorial.

## Resume bullets

- Designed a vendor-neutral, cross-platform .NET equipment-software architecture with bounded command processing, immutable state snapshots, explicit machine state models, fault recovery, and independent HMI clients.
- Built an Avalonia operator HMI and bundled Three.js digital twin consuming live machine state over REST and WebSocket interfaces.
- Implemented deterministic two-axis motion simulation, alarm/interlock diagnostics, recipe validation, traceability events, recovery checkpoints, and an integration outbox.
- Established an open-source maintenance model with architecture decisions, adapter extension points, multi-OS CI, security scanning, release automation, and contributor workflows.
