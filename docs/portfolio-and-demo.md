# Portfolio and demo guide

This file is retained as the detailed interview companion to the shorter [portfolio demo](portfolio-demo.md).

## 90-second demo script

1. **Problem, 10 seconds:** “Automation companies need equipment software that remains testable when the controller, HMI, operating system, or factory integration changes.”
2. **Architecture, 15 seconds:** show the modular-monolith diagram and explain that the headless runtime owns machine behavior while UI and integration clients consume contracts.
3. **Normal operation, 20 seconds:** initialize, home, load an order, and start the two-axis pick–inspect–place cycle from the Avalonia HMI while the Three.js viewer follows live state.
4. **Fault and recovery, 20 seconds:** open the simulated guard or interrupt communication, show command rejection and the alarm lifecycle, then demonstrate an explicit recovery choice.
5. **Manufacturing integration, 15 seconds:** stop the MES simulator, complete or interrupt work according to policy, show queued outbox data, restore the MES, and show idempotent delivery.
6. **Software quality, 10 seconds:** show tests, multi-OS CI, ADRs, observability, release packages, and the adapter-development guide.

## Resume bullets

Use only after the corresponding evidence is visible in the public repository:

- Designed a vendor-neutral, cross-platform .NET equipment-software architecture using bounded command processing, immutable machine-state snapshots, explicit operating modes, recovery checkpoints, and ports-and-adapters boundaries.
- Built an Avalonia operator HMI and a Three.js digital twin as independent clients of a headless machine runtime, with live REST/WebSocket state and prerecorded replay.
- Implemented deterministic two-axis motion simulation, fault injection, alarm/interlock diagnostics, production orders, recipe revisions, part/cycle traceability, OEE, and an idempotent manufacturing outbox.
- Added a read-only OPC UA simulation server, an outage-capable MES simulator, OpenTelemetry instrumentation, multi-operating-system CI, release automation, SBOMs, security scans, and contributor extension points.

## Interview stories

### Architecture portability

**Situation:** equipment applications often couple machine rules directly to one HMI, controller, or database.  
**Task:** create a portfolio project whose core behavior survives changes in vendor and deployment environment.  
**Action:** separated domain, application, adapters, infrastructure, runtime, HMI, integration, and visualization projects behind explicit contracts.  
**Result:** simulated, replay, and fault-injecting motion implementations can be changed without rewriting the machine sequence or clients.

### Failure and recovery

**Situation:** normal-cycle demos do not show how production software behaves during interruption.  
**Task:** make faults, restart, and external-system outages first-class scenarios.  
**Action:** added alarms, structured rejection reasons, checkpoints, `RecoveryRequired`, three recovery choices, an outbox, retries, idempotency keys, and deterministic tests.  
**Result:** the repository can demonstrate controlled degradation and explain recovery decisions rather than silently restarting motion.

### Open-source engineering

**Situation:** a public portfolio needs maintainable contribution boundaries, not only source code.  
**Task:** make useful changes possible without requiring contributors to understand the complete runtime.  
**Action:** added an adapter SDK, sample adapter, good-first-issue list, ADR process, compatibility policy, governance, CI, security scanning, and release documentation.  
**Result:** contributors can add adapters, scenarios, translations, tests, viewer components, and manufacturing integrations through documented extension points.

## Evidence checklist

- [ ] Replace maintainer and repository placeholders.
- [ ] Push the repository and obtain green Windows, Ubuntu, and macOS CI jobs.
- [ ] Record a normal cycle, a rejected command, a fault/recovery case, and an MES outage/replay case.
- [ ] Upload `docs/assets/social-preview.png` if one is added later.
- [ ] Enable Discussions and private vulnerability reporting.
- [ ] Apply the recommended repository ruleset.
- [ ] Link the companion `industrial-controls-learning-lab` repository.
- [ ] Publish `v0.5.0` only after the release workflow is green.
