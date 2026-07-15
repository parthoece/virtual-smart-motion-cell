# ADR 0009: Isolate the OPC UA simulation server

## Context

The project needs industrial information-model evidence without coupling machine logic to an OPC UA stack.

## Decision

Host a read-only OPC UA simulation server in its own project. It maps immutable state snapshots to nodes and exposes no motion command methods.

## Consequences

The domain remains vendor-neutral. Production security and conformance remain explicit future hardening concerns.
