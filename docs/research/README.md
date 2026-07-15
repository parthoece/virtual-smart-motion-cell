# Research benchmark documentation

The research extension keeps the original equipment-software architecture and adds a machine-fault-first benchmark/data plane. The first implemented environment is `VSMC-DynamicGantry-v1`.

## Start here

- [Final research plan](final-research-plan.md)
- [Research questions](research-questions.md)
- [Dynamic benchmark design](dynamic-benchmark.md)
- [Research governance](research-governance.md)
- [Practicality and community value](practicality-and-community-value.md)
- [Fault taxonomy](fault-taxonomy.md)
- [Data, network, and synchronization](data-and-synchronization.md)
- [EtherCAT protocol model](ethercat-protocol.md)
- [Ground truth and leakage controls](ground-truth-and-labeling.md)
- [Visual Experiment Studio](visual-experiment-studio.md)
- [Publication and reproducibility](publication-and-reproducibility.md)
- [Benchmark evaluation and acceptance criteria](benchmark-evaluation.md)
- [Cyber and honeypot research boundary](cyber-and-honeypot-boundary.md)

## Implemented through v0.5 EtherCAT research foundation

- seeded hybrid dynamic gantry simulation;
- orders, product variants, payloads, queues, changeovers, and maintenance;
- machine-fault and network-fault scenario taxonomy;
- gradual, abrupt, intermittent, and periodic scenario behavior;
- separate observable and oracle ground-truth planes;
- synchronized telemetry, production events, logs, EtherCAT frames, LRW exchanges, decoded PDOs, and flow tables;
- Parquet, CSV, PCAPNG, and JSONL outputs;
- multimodal one-second dataset windows and episode-level splits;
- guided browser Experiment Studio and CLI using the same manifests;
- reproducibility metadata, checksums, and deterministic transition hashes.

The research roadmap intentionally defers additional environments, cyber-influenced effect emulation, honeypot gateways, FMI import, and publication-scale reference datasets until the first environment is independently validated.

## Sample evidence

- [Generated dynamic machine-fault report](../assets/research-sample/index.html)
- [Sample metrics](../assets/research-sample/metrics.json)
- [Sample manifest](../assets/research-sample/manifest.yaml)
