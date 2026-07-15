# Data, CPS network semantics, and synchronization

## Data planes

The benchmark records observable data separately from oracle ground truth.

Observable sources:

- axis and machine telemetry;
- production events;
- runtime logs;
- wire-format EtherCAT frames;
- decoded protocol messages;
- aggregated CPS semantic flows;
- synchronization events.

Oracle sources:

- scenario identity;
- root-cause domain and category;
- affected component;
- start and end times;
- progression and phase;
- severity and seed.

## Canonical experiment time

`simulation_time_ns` is authoritative in accelerated and replay studies. Wall and observed time are retained for real-time performance studies.

Every episode also emits periodic synchronization events. Future external collectors can use these markers to estimate source offset, drift, and transport latency.

## Network representation

The motion-segment network layer creates offline Ethernet frames with EtherType `0x88A4`. Each cyclic exchange contains an EtherCAT LRW datagram and a documented two-axis CiA 402-style process image. The recorder writes the request and returned frame to PCAPNG without opening a network interface.

Analytical tables include:

- `packets.parquet`: one row per captured EtherCAT frame;
- `messages.parquet`: one row per LRW request/return exchange;
- `ethercat-pdos.parquet`: one row per decoded axis PDO per exchange;
- `flows.parquet`: one row per one-second CPS communication window.

CPS-specific context includes:

- EtherCAT frame type, LRW command, datagram index, logical address, and Working Counter;
- Controlword, Statusword, operation mode, target/actual position and velocity, and following error;
- response latency, missing return frames, duplicate returns, and Working Counter validity;
- machine state, production step, cycle, and part identifiers.

Oracle fault and cyber labels are never written into these source tables. The complete mapping and verification boundary are documented in [EtherCAT protocol model](ethercat-protocol.md).

## Experiment bundle

```text
manifest.yaml
provenance.json
checksums.sha256
raw/capture.pcapng
raw/runtime.jsonl
normalized/operational/*.parquet
normalized/network/*.parquet
normalized/logs/*.parquet
normalized/synchronization/*.parquet
ground-truth/*.parquet
datasets/*.parquet
datasets/csv/*.csv
metrics/metrics.json
report/index.html
```

## Compatibility policy

Parquet schemas should prefer stable primitive columns. Each schema will be versioned, and conformance fixtures will be read by both Python and .NET before a dataset release is frozen.
