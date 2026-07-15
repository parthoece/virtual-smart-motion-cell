# ADR: Keep the runtime independent from UI

- Status: Accepted
- Date: 2026-07-14

## Context

Operator interfaces can disconnect, restart, or run remotely.

## Decision

Host machine execution as background services; clients use contracts and snapshots.

## Consequences

UI failure does not directly stop the simulation.
