# ADR 0011: Use OpenTelemetry for vendor-neutral observability

## Context

The project should demonstrate traces and metrics without requiring one commercial backend.

## Decision

Instrument ASP.NET Core, HTTP clients, runtime metrics, commands and manufacturing delivery with OpenTelemetry. OTLP export is optional.

## Consequences

Local use remains dependency-light while external collectors can receive standard telemetry.
