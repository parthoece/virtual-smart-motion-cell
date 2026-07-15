# ADR 0015 — Preserve PCAPNG and derive typed Parquet/CSV views

## Status

Accepted.

## Decision

Synthetic raw packets are retained in PCAPNG. Decoded message and CPS-semantic flow records are stored in Parquet, with optional CSV exports.

## Consequences

Researchers can reprocess raw evidence while most ML workflows consume efficient typed analytical tables.
