# Publication and reproducibility

## Publication sequence

1. benchmark architecture and dynamic machine-fault environment;
2. synchronized multimodal dataset and labeling methodology;
3. dynamic-domain-shift and OOD benchmark;
4. multimodal diagnosis study;
5. model-in-the-loop deployment and resilience;
6. controlled cyber-resilience extension;
7. optional honeypot/deception study.

These should be separate publications rather than one feature inventory.

## Artifact requirements

Each publication release should include:

- frozen source commit and benchmark version;
- exact manifests and seeds;
- immutable environment, schema, and taxonomy versions;
- container or documented clean setup;
- raw and processed data;
- expected tables and figures;
- checksums;
- one-command reproduction;
- known limitations;
- permanent archive and persistent identifier;
- independent reproduction report.

## Reproducibility levels

- deterministic repeatability: same manifest/seed gives the same semantic transition hash;
- computational reproducibility: another machine regenerates equivalent metrics within declared tolerance;
- artifact reproducibility: an external user produces the paper outputs from the archived bundle;
- empirical validity: later comparison with imported models or physical/recorded traces.

## Current reproduction command

```bash
./scripts/reproduce-research-foundation.sh
```

It runs the research tests, generates the reference machine-fault bundle, validates PCAPNG/Parquet/checksums and leakage boundaries, then replays the manifest and compares semantic execution hashes.
