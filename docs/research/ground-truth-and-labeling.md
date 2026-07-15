# Ground truth, labels, and leakage controls

## Invariant

> Observable data describes what the system could know. Ground truth describes what the experiment orchestrator knows.

The scenario engine creates oracle intervals at activation. Dataset generation joins those intervals to observable windows only when a supervised view is requested.

## Label hierarchy

- operational condition: normal, machine fault, network fault, combined;
- domain: machine, network, cyber influenced;
- category: mechanical, sensor feedback, drive, process, software, and others;
- type: fine-grained scenario type;
- component: affected asset;
- progression and lifecycle phase;
- severity;
- source and confidence.

Multiple scenarios can be active simultaneously. The dataset therefore includes both a primary target and a multi-label representation.

## Current lifecycle

Gradual scenarios emit incipient and active intervals. Other scenarios emit active intervals. Future versions will add onset, propagation, detection, mitigation, recovery, and cleared phases from independently measured events.

## Leakage controls

- source telemetry contains no scenario ID or oracle target;
- source packets contain machine context but no ground-truth condition;
- normal runtime logs avoid fault-injection names;
- labels are stored under `ground-truth/`;
- train/test splits are assigned by episode rather than individual rows;
- generated filenames do not serve as model features;
- scenario timing, seeds, and manifest metadata are excluded from default feature views.

Every published dataset should undergo a feature-name, value-distribution, temporal, and split-leakage audit.
