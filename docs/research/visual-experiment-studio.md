# Visual Experiment Studio

The browser studio is the primary guided interface. The CLI remains available for CI, batch execution, and paper reproduction.

## Current workflow

1. select normal, machine-fault, network-fault, or combined template;
2. edit episodes, duration, seed, window, and scenario type;
3. inspect and validate the portable manifest;
4. launch the experiment;
5. monitor run status and visual machine movement;
6. inspect the generated bundle and metrics.

The studio never stores an alternative private workflow representation. It edits and runs the same manifest accepted by `vsmc-bench`.

## Planned workspaces

- design: drag-and-drop plugin workflow and parameter sweeps;
- run: full Three.js dynamic cell, orders, queues, faults, packets, and model predictions;
- analyze: aligned timelines, packet/message inspection, traces, labels, and experiment comparison;
- dataset builder: feature selection, resampling, event windows, official splits, leakage warnings, and exports;
- model deployment: ONNX/Python adapters, shadow/advisory/simulation-only control, and latency metrics.

## Accessibility goal

A user should be able to run a valid benchmark without writing a command, but every visual action must remain reproducible through an exported manifest.
