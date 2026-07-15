# Runtime data

The default reference persistence uses inspectable files so contributors can understand durability and replay behavior without installing a database.

```text
runtime-data/
├── events.jsonl
├── checkpoint.json
├── alarms/
│   └── history.jsonl
├── production/
│   ├── orders.jsonl
│   ├── parts.jsonl
│   ├── cycles.jsonl
│   └── traceability.jsonl
├── outbox/
│   ├── pending/
│   └── delivered/
└── manufacturing-delivery.jsonl   # file-gateway mode
```

Configuration recipes live outside runtime data:

```text
config/recipes/
├── standard-widget.v1.json
├── <contributed recipes>.json
└── active-recipe.json             # generated active-revision pointer
```

## File responsibilities

- `events.jsonl` is the append-only domain-event and diagnostic stream.
- `checkpoint.json` is atomically replaced and forces explicit recovery after an interrupted cycle.
- `alarms/history.jsonl` records alarm raise, acknowledge and clear lifecycle changes.
- `production/*.jsonl` materializes orders, parts, cycles and traceability records for query APIs.
- `outbox/pending` contains manufacturing events that have not been acknowledged.
- `outbox/delivered` contains successfully delivered idempotent events.
- `manufacturing-delivery.jsonl` is used only by the local file MES adapter.

The HTTP MES adapter sends pending outbox messages with an `Idempotency-Key` header and marks them delivered only after an accepted or duplicate response.

## Checkpoint cadence

While production is active, the runtime refreshes the recovery checkpoint at a configurable cadence—one second by default—and on significant transitions. This preserves recent simulated position and part context without writing on every control tick.

## Replacement adapters

Production-oriented contributors may implement SQLite, PostgreSQL or another durable store behind the existing ports. Replacements must preserve:

- idempotent event identity
- atomic checkpoint replacement
- deterministic ordering
- cancellation support
- recovery semantics
- separation from the control loop
