# ADR 0014 — Use one experiment manifest for visual, CLI, and CI operation

## Status

Accepted.

## Decision

The browser studio edits a portable manifest. The CLI, CI, publication scripts, and visual interface execute the same schema.

## Consequences

Visual accessibility does not create a second, non-reproducible execution path.
