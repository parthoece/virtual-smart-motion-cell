# ADR: Serialize commands through a bounded queue

- Status: Accepted
- Date: 2026-07-14

## Context

Concurrent commands can produce ambiguous equipment behavior and unbounded memory growth.

## Decision

Use a single-reader bounded channel and explicit queue-full rejection.

## Consequences

Commands are ordered and overload is visible.
