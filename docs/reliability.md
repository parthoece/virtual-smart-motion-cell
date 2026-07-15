# Reliability and soak testing

The project defines reproducible campaigns rather than relying on a short visual demo.

| Campaign | Target |
| --- | --- |
| Deterministic scenario | Equivalent state result and transition hash across operating systems |
| Production endurance | At least 10,000 completed virtual cycles |
| Runtime soak | Optional real-time duration, typically eight hours |
| Command flood | Bounded queue and explicit rejection |
| Slow viewer | Latest-state backpressure; control loop remains independent |
| Viewer disconnect | Machine operation continues |
| Restart during production | `RecoveryRequired` with explicit operator choice |
| Integration outage | Outbox preserves events and retries idempotently |
| Corrupt recipe | Draft or activation is rejected safely |
| Following error | Deterministic fault and alarm evidence |

Fast deterministic campaign:

```bash
dotnet run --project tools/VirtualSmartMotionCell.Reliability -c Release -- --cycles 10000
```

Real-time soak:

```bash
dotnet run --project tools/VirtualSmartMotionCell.Reliability -c Release -- --cycles 100 --duration-minutes 480
```

The runner reports operating system, architecture, framework, cycle count, orders, simulated and wall-clock time, memory, working set, maximum following error, maximum step duration, deadline misses, transition hash, OEE, and final state. Scheduled CI archives the report.
