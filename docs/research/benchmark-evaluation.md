# Benchmark evaluation and acceptance criteria

## Scientific quality

Evaluate:

- fault realism and causal emergence;
- scenario diversity and coverage;
- deterministic execution;
- label correctness and independence;
- synchronization accuracy;
- schema stability;
- leakage resistance;
- IID and OOD difficulty;
- baseline strength;
- usability and external reproducibility.

## Machine-fault metrics

- RMS and maximum following error;
- control effort and saturation duration;
- cycle time, throughput, rework, and good-part rate;
- detection delay and false alarms per operating hour;
- category, type, severity, and component metrics;
- safe-stop and recovery time;
- remaining-useful-life error for gradual degradation.

## Software and deployment metrics

- control-step duration and deadline misses;
- CPU, memory, and allocation growth;
- recorder backlog and dropped records;
- inference P50/P95/P99 latency;
- connected-client and data-export overhead;
- cross-platform equivalence.

## Initial release acceptance

The first publication-grade release is not complete until:

- machine-fault signatures are reviewed by at least one external controls/maintenance researcher;
- multiple seeds and dynamic product conditions are evaluated;
- official baselines and OOD splits are published;
- an external user reproduces a reference experiment;
- schemas and taxonomies are frozen under semantic versioning;
- a permanent archive contains source, data, and expected results.
