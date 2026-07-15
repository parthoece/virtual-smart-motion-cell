# Test architecture

The repository uses complementary evidence layers:

1. dependency-light C# behavioral specifications for commands, modes, transitions, motion, faults, pause/stop/abort, alarms, and recovery
2. C# integration specifications for checkpoints, outbox idempotency, MES HTTP contracts, recipes, production materialization, alarm history, replay, state backpressure, project boundaries, and configuration
3. end-to-end CI that starts the machine runtime and MES simulator, exercises REST and WebSocket state, checks OPC UA TCP availability, and verifies persisted delivery
4. the retained 27-test Python numerical reference suite
5. a locked Three.js build, syntax check, and recorded replay data
6. repository contracts for metadata, JSON, YAML, XML/XAML, links, project references, HMI bindings, and all portfolio evidence areas
7. Windows, Linux, and macOS build and publish jobs
8. deterministic 10,000-cycle campaigns and optional real-time soak duration
9. CodeQL, dependency review, Dependabot, OpenSSF Scorecard, SBOM generation, and artifact attestations

Tests should assert externally meaningful behavior: accepted or rejected commands, transitions, alarms, cycle outcomes, persistence, idempotency, recovery, and client isolation. Avoid tests that only mirror private implementation details.

## Local commands

```bash
dotnet build VirtualSmartMotionCell.sln -c Release
dotnet run --project tests/VirtualSmartMotionCell.Specs -c Release --no-build
dotnet run --project tests/VirtualSmartMotionCell.IntegrationSpecs -c Release --no-build
npm ci --prefix web/viewer
npm run check --prefix web/viewer
npm run build --prefix web/viewer
python -m pip install -e "reference/python-simulator[dev]"
pytest -q reference/python-simulator/tests
python scripts/check_repo.py
```

## Reliability campaigns

Fast deterministic campaign:

```bash
dotnet run --project tools/VirtualSmartMotionCell.Reliability -c Release -- --cycles 10000
```

Real-time soak:

```bash
dotnet run --project tools/VirtualSmartMotionCell.Reliability -c Release -- --cycles 100 --duration-minutes 480
```

The report includes platform, runtime, cycles, memory, working set, following error, loop timing, deadline misses, deterministic transition hash, and final state.
