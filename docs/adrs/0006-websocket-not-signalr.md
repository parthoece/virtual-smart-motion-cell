# ADR: Use raw WebSocket for the reference state stream

- Status: Accepted
- Date: 2026-07-14

## Context

A dependency-light browser and HMI client improve portability and learning value.

## Decision

Use a simple server-pushed JSON snapshot protocol over WebSocket.

## Consequences

Less transport abstraction; protocol remains intentionally small.
