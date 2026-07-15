# ADR: Use a modular monolith for one machine

- Status: Accepted
- Date: 2026-07-14

## Context

A single machine benefits from one authoritative runtime and shared transaction boundary. Separate clients provide process isolation without distributing every domain object.

## Decision

Use one ASP.NET Core machine-host process with modular projects and independent HMI/viewer clients.

## Consequences

Simpler deployment and debugging; horizontal scaling is not a goal.
