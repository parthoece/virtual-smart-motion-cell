# ADR: Use portable file storage in the reference runtime

- Status: Accepted
- Date: 2026-07-14

## Context

The architecture must run without native database packages on all platforms.

## Decision

Use JSONL events, atomic JSON checkpoints, and file-backed outbox records.

## Consequences

Easy local inspection; production adapters may use SQLite/PostgreSQL.
