# Portfolio evidence matrix

This matrix maps the project’s recruiter-facing claims to source, tests, workflows, and demonstration evidence. A capability is considered **implemented** when source and executable specifications exist. Multi-operating-system execution becomes **verified** after the first green GitHub Actions run on Windows, Ubuntu, and macOS.

| Area | Implementation evidence | Verification evidence |
| --- | --- | --- |
| Cross-platform runtime | `.NET 10` headless runtime in `VirtualSmartMotionCell.Runtime` and API host in `VirtualSmartMotionCell.Api` | `.github/workflows/ci.yml` builds on Windows, Ubuntu, and macOS; `.github/workflows/release.yml` publishes six runtime identifiers |
| Equipment architecture | Contracts, Domain, Control, Application, Adapter SDK, Infrastructure, Runtime, OPC UA, API, HMI | Architecture specifications and C4/ADR documentation |
| Machine operation | Manual, Automatic, Maintenance, Recovery, Offline modes; initialize, home, jog, start, pause, resume, stop, abort, reset, recovery commands | `tests/VirtualSmartMotionCell.Specs` state and command scenarios |
| Command processing | Bounded channel, serialized processor, command IDs, correlation IDs, structured rejection reasons | command queue and unsafe-command specifications |
| Motion abstraction | `IMotionSystem`, simulated, replay, and fault-injecting adapters | replay, fault, homing, following-error, and adapter-boundary specifications |
| HMI | Avalonia overview, automatic, manual, maintenance, alarm/recovery, recipe/integration, and diagnostics screens | multi-OS build matrix and ViewModel/XAML contract validation |
| Digital twin | Three.js 3D cell consuming `/ws/state`, with recorded replay mode | locked npm build, syntax check, bundled preview artifact, API WebSocket smoke test |
| Reliability | pause, resume, controlled stop, abort, restart checkpoint, three recovery choices, slow-client isolation | fault/recovery specs, scheduled 10,000-cycle campaign, optional real-time soak duration |
| Manufacturing data | orders, parts, cycles, recipe revisions, traceability, alarm history, OEE | file-repository integration specifications and API endpoints |
| Integration | OPC UA simulation server and HTTP MES simulator with outage, delay, duplicate, outbox, and idempotency behavior | end-to-end CI starts both services, checks OPC TCP, WebSocket, MES delivery, and outbox |
| Observability | structured logs, health, Prometheus text metrics, OpenTelemetry traces/metrics, correlation propagation | API smoke test and OTLP-configurable host |
| Deployment | self-contained runtime, HMI, MES simulator, viewer, checksums, SBOM and attestations | release matrix for Windows/Linux/macOS, x64/ARM64 |
| Testing | executable unit-style specs, integration specs, architecture rules, end-to-end smoke, Python oracle, reliability campaign | CI, CodeQL, dependency review, Scorecard, weekly reliability workflow |

## Evidence rule

Do not claim a platform has been tested until its corresponding GitHub Actions job is green. Do not describe the OPC UA server, functional interlocks, or simulator as certified safety or vendor-conformance evidence.
