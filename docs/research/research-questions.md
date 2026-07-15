# Research questions

These questions define the intended publication program. Each study should select a small subset and preregister its hypotheses, variables, metrics, and exclusion criteria.

## Dynamic operation and machine faults

**RQ1.** How does fault-detection performance change when product type, payload, queue state, recipe, and maintenance condition vary dynamically rather than remaining fixed?

**RQ2.** Which fault-progression models—abrupt, intermittent, gradual, periodic, load-dependent, or cascading—are most difficult to detect without increasing false alarms?

**RQ3.** How early can gradual machine degradation be detected before the existing rule-based alarm threshold is crossed?

**RQ4.** How well can a model localize a fault to the X axis, Y axis, gripper, inspection station, runtime, or communication channel?

**RQ5.** Can a model distinguish root cause from propagated symptoms, such as mechanical friction causing drive derating and following-error alarms?

**RQ6.** How robust are diagnostic models to unseen product payloads, process times, recipes, and order mixes?

## Multimodal data

**RQ7.** Does combining operational telemetry, CPS-semantic network features, logs, and traces improve diagnosis compared with any single modality?

**RQ8.** Which synchronization accuracy is required before multimodal fusion ceases to improve detection or localization?

**RQ9.** Are event-centered windows more effective than fixed time windows for early machine-fault detection?

**RQ10.** Which CPS-semantic network features add information beyond conventional five-tuple flow statistics?

**RQ11.** Can graph representations linking packets, messages, commands, state transitions, telemetry, and alarms improve root-cause analysis?

## Generalization and reproducibility

**RQ12.** How large is the performance gap between IID tests and OOD tests involving unseen severity, dynamics, products, fault combinations, or environments?

**RQ13.** Which simulator parameters most strongly cause synthetic-to-real transfer risk or model shortcut learning?

**RQ14.** Can independently implemented .NET and Python readers produce equivalent analytical results from the same Parquet bundle?

**RQ15.** How much benchmark variance is introduced by operating system, architecture, runtime, and accelerated versus real-time execution?

## Model deployment

**RQ16.** How do inference latency, jitter, deadline misses, and resource consumption affect an otherwise accurate fault-detection model?

**RQ17.** Does shadow-mode disagreement with a deterministic baseline provide an effective distribution-shift signal?

**RQ18.** Which fallback strategies preserve production and traceability when an AI model becomes unavailable or exceeds its latency budget?

## Network and cyber-physical resilience

**RQ19.** Can a model distinguish machine faults from accidental network delay, loss, or service unavailability when observable symptoms overlap?

**RQ20.** How often does network degradation create false machine-fault alarms, and which multimodal features reduce them?

**RQ21.** How effectively do independent command validation and safety-envelope checks prevent hazardous virtual consequences from synthetic command manipulation?

**RQ22.** How do cyber-influenced effects change detection latency, safe-stop time, virtual damage score, and recovery time?

## Honeypot and deception research

**RQ23.** Does coupling an industrial decoy to a dynamic virtual process produce deeper or more meaningful interaction than a static protocol decoy?

**RQ24.** Which decoy process states and apparent machine conditions increase engagement without exposing real infrastructure?

**RQ25.** Can observed decoy actions be translated into constrained virtual effects while maintaining containment and reproducibility?

**RQ26.** How accurately can automated labels derived from protocol interaction be calibrated against analyst-reviewed session labels?

## Human and visual workflow

**RQ27.** Does a visual workflow reduce experiment-configuration errors compared with manifest-only operation?

**RQ28.** Can users correctly identify data leakage and invalid train/test splitting with visual warnings and dataset previews?

**RQ29.** Which live visualizations best support debugging of synchronized machine, network, log, and model events?

## Priority studies

The first three recommended studies are:

1. dynamic operation versus static operation for gradual machine-fault detection;
2. telemetry-only versus multimodal diagnosis under machine and network faults;
3. IID versus OOD product/payload evaluation with deployable shadow-mode baselines.
## EtherCAT and CPS communication questions

30. Does adding EtherCAT LRW, Working Counter, and PDO semantics improve machine-fault versus network-fault discrimination compared with generic flow statistics?
31. Which features are most useful: cycle timing, missing returns, Working Counter validity, Controlword/Statusword transitions, or process-value consistency?
32. Can multimodal models detect a mechanical or sensor fault earlier when EtherCAT feedback PDOs are synchronized with internal operational telemetry?
33. How robust are detectors to cycle-period changes and realistic communication jitter?
34. Can a model distinguish a missing EtherCAT cycle from a machine state in which motion correctly remains stationary?
35. How much performance is lost when only packet headers are available compared with decoded PDO semantics?

