# Manufacturing data model

The project separates transient machine state from durable production evidence.

## Core records

| Record | Purpose |
| --- | --- |
| `ProductionOrder` | target quantity, active recipe, lifecycle and progress |
| `PartRecord` | part identity, order, recipe and quality result |
| `CycleRecord` | cycle number, duration, result and timestamps |
| `TraceabilityRecord` | append-only order/part event history |
| `RecipeDescriptor` | recipe identity, schema, revision, lifecycle and checksum |
| `AlarmHistoryRecord` | raised, acknowledged and cleared alarm evidence |
| `RecoveryCheckpoint` | interrupted machine context requiring an operator decision |
| `MachineEvent` | integration and audit event with correlation and revision |

## Order lifecycle

```text
Queued → Active → Paused → Active → Completed
                  └───────────────→ Cancelled
```

A production cycle cannot start without an active recipe and loaded order. A completed cycle updates the order, part, cycle, traceability and OEE evidence before its integration event is retried from the outbox.

## Recipe lifecycle

```text
Draft → Approved → Active → Retired
```

Recipes are versioned JSON documents with schema version, revision, checksum, motion limits, positions and inspection parameters. Activation is blocked while unsafe machine states are active.

## Storage boundary

The included file repositories are transparent reference implementations suitable for local demonstrations and tests. `IProductionRepository`, `IRecipeStore`, `IAlarmHistoryStore`, `ICheckpointStore`, and `IOutboxStore` are replaceable ports for PostgreSQL, document storage, or managed services.
