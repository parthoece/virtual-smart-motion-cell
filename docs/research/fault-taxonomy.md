# Fault taxonomy

Machine faults are the primary benchmark domain.

## Machine categories

- mechanical;
- actuator and drive;
- sensor and feedback;
- motion control;
- process and tooling;
- safety and interlock;
- electrical and power;
- software and sequence.

## Network categories

- timing and jitter;
- loss and availability;
- duplication and reordering;
- protocol integrity;
- remote-service unavailability.

## Later cyber-influenced categories

- command manipulation effect;
- telemetry manipulation effect;
- parameter or recipe modification effect;
- loss of view;
- loss of control;
- alarm-notification suppression effect;
- service disruption.

## Label dimensions

A label is not only a class name. It records:

- domain;
- category;
- fine-grained type;
- affected component;
- progression;
- lifecycle phase;
- severity;
- root cause;
- observable symptoms;
- operational consequence;
- source, confidence, and schema version.

Fault source, symptom, and consequence remain distinct so the benchmark can represent identical symptoms with different root causes.
