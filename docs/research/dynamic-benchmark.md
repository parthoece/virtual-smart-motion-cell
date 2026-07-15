# Dynamic benchmark design

## Hybrid execution

The benchmark combines:

- numerical integration for axis dynamics and controller behavior;
- discrete machine states for pick, inspect, place, changeover, maintenance, and recovery;
- discrete production events for part arrivals, queueing, orders, and completion;
- scheduled scenario effects for machine and network conditions;
- synchronized record generation.

A dynamic run remains reproducible because every stochastic process uses a derived seed recorded in provenance.

## Current environment

`VSMC-DynamicGantry-v1` contains:

- two PID-controlled numerical axes;
- product-specific payload, inspection, and changeover parameters;
- priority orders;
- seeded stochastic part arrivals;
- bounded input, output, and rework state;
- machine-state transitions driven by physical convergence and dwell times;
- planned maintenance;
- state- and load-dependent fault effects;
- synthetic state-publication traffic.

## Execution modes

- `accelerated`: run as quickly as possible for datasets and parameter sweeps;
- `real_time`: pace numerical steps against wall time for visual and deployment studies;
- replay-compatible: preserve manifest, seed, transition hash, and raw evidence for deterministic reruns.

## Scenario implementation principle

A scenario modifies the plant, sensor, actuator, process, software, or network model. It must not directly set a training label or force a desired alarm. Observable symptoms and alarms emerge independently.

## Current implemented scenario effects

- gradual increased axis friction;
- gradual encoder drift;
- abrupt drive derating;
- intermittent failed pick;
- message delay;
- packet loss;
- message duplication.

Additional fault types belong in the R1 roadmap and should include unit tests proving their physical/operational effect.
