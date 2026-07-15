# ADR 0013 — Separate observable and oracle research planes

## Status

Accepted for the v0.4 research foundation.

## Decision

Operational telemetry, packets, messages, flows, logs, and traces never include scenario ground truth. Oracle intervals are stored separately and joined only by the dataset builder.

## Consequences

The design reduces label leakage and supports supervised, unsupervised, and hidden-label evaluation from the same raw experiment bundle.
