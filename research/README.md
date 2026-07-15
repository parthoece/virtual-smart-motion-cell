# VSMC Research Bench

This package adds a runnable research slice to Virtual Smart Motion Cell:

- dynamic orders, product mix, queues, changeovers, and variable payloads;
- machine-fault-first scenarios with explicit lifecycle labels;
- secondary network-fault scenarios against an EtherCAT cyclic process-data model;
- accelerated, wall-clock, and deterministic replay-compatible manifests;
- synchronized telemetry, production events, network records, logs, and oracle labels;
- Parquet and CSV analytical exports plus wire-format EtherCAT PCAPNG evidence;
- multimodal time-window dataset generation;
- a browser Experiment Studio for guided operation.

## Install and run

```bash
python -m pip install -e "research[dev]"
vsmc-bench validate benchmarks/manifests/machine-fault.yaml
vsmc-bench run benchmarks/manifests/machine-fault.yaml --output runs
python scripts/validate_research_bundle.py runs/<experiment-bundle>
vsmc-bench replay runs/<experiment-bundle> --output runs/replays
vsmc-studio --host 127.0.0.1 --port 8090
```

Open `http://127.0.0.1:8090` for the visual workflow.

The benchmark never sends traffic to external systems. PCAPNG files contain offline Ethernet frames using EtherCAT EtherType `0x88A4`, LRW datagrams, and a documented CiA 402-style two-axis PDO image. This is not a hardware MainDevice or conformance claim.
