# ADR 0010: Use HTTP MES simulation with a transactional-style outbox boundary

## Context

Manufacturing endpoints can be slow, duplicated, or unavailable. Motion commands must never be blindly retried.

## Decision

Persist integration events locally before asynchronous delivery. Use event IDs as idempotency keys and treat duplicate acknowledgements as success.

## Consequences

Machine execution is isolated from MES availability, while contributors can replace file storage and HTTP contracts behind ports.
