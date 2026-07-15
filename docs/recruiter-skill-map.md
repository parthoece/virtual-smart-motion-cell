# Recruiter skill map

This document maps visible repository evidence to common equipment and automation software competencies.

| Competency | Evidence |
|---|---|
| Cross-platform product design | .NET runtime, Avalonia HMI, browser viewer, multi-OS workflows |
| Clean architecture | domain and contracts isolated from UI, network, persistence, and vendor adapters |
| Concurrent runtime design | bounded serialized command channel, independent snapshot publication, slow-client backpressure |
| Machine-state modeling | separate mode, execution, and production-step state models |
| Motion fundamentals | PID controller, trajectory reference, simulated plant, limits, homing, following-error monitor |
| Reliability | controlled stop, abort, fault injection, checkpoints, explicit restart recovery |
| Manufacturing integration | orders, parts, cycles, recipes, traceability, OEE, OPC UA, MES simulation, idempotent outbox and receipts |
| HMI engineering | operator command workflow, manual jog, interlocks, alarms, diagnostics |
| Digital twin | live and replayable Three.js machine visualization |
| Observability | health endpoints, Prometheus-format metrics, OpenTelemetry traces/metrics, correlation IDs, and durable event log |
| Extensibility | adapter SDK, sample adapter, compatibility policy, design proposal process |
| Open-source maintenance | governance, triage, security, CI, CodeQL, Scorecard, Dependabot, releases, SBOM |

## Suggested interview path

1. Explain the system context and why the runtime is headless.
2. Trace one operator command through validation, serialization, state transition, persistence, and state publication.
3. Show that a slow or disconnected viewer cannot block machine execution.
4. Demonstrate an unsafe command rejection and a controlled fault.
5. Explain the recovery checkpoint and why restart never resumes motion automatically.
6. Show the adapter boundary and describe how simulation can be replaced without changing the domain.
7. Discuss one ADR and one tradeoff that was deliberately rejected.
