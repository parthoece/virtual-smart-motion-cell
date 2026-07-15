# ADR: Publish immutable state snapshots

- Status: Accepted
- Date: 2026-07-14

## Context

Clients must not observe partial mutable state or derive authoritative machine state.

## Decision

Publish complete immutable records at a lower rate than the internal simulation loop.

## Consequences

More allocation, simpler client consistency.
