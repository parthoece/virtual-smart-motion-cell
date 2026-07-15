# Dataset card — VSMC Dynamic Gantry v1

## Purpose

Generate synchronized machine-operation, production, CPS-network, log, and oracle-label data under dynamic product and fault conditions.

## Primary tasks

- normal versus abnormal detection;
- machine-fault diagnosis and localization;
- machine-fault versus network-fault discrimination;
- gradual degradation and early detection;
- multimodal data fusion;
- OOD product, payload, severity, and condition evaluation.

## Data sources

- 20 ms numerical axis telemetry by default;
- machine and production events;
- structured runtime logs;
- offline EtherCAT Ethernet PCAPNG evidence with LRW datagrams and decoded PDOs;
- semantic application messages and one-second flow windows;
- separate oracle scenario intervals.

## Limitations

The environment is synthetic. It does not reproduce the complete physics, noise, timing, protocol implementation, safety behavior, or failure distribution of a specific commercial machine. Models trained here require separate external validation.

## Ethical and safety boundary

The benchmark does not transmit traffic to external systems and does not control physical machinery. Later cyber or honeypot extensions must follow the documented isolation policy.
