# API and state-stream contract

The machine host exposes versioned REST endpoints and a read-only WebSocket state stream. Commands are limited to loopback clients by default.

## Runtime and diagnostics

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/health/live` | Process liveness |
| `GET` | `/health/ready` | Runtime snapshot readiness |
| `GET` | `/api/v1/state` | Latest immutable machine snapshot |
| `GET` | `/api/v1/diagnostics` | Runtime, interlock, integration and recovery diagnostics |
| `GET` | `/api/v1/integration` | MES/outbox and OPC UA status |
| `GET` | `/api/v1/opcua` | Simulation endpoint and security-boundary description |
| `GET` | `/api/v1/outbox` | Pending manufacturing-message count |
| `GET` | `/metrics` | Prometheus text metrics |

## Alarms and manufacturing records

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/api/v1/alarms` | Active alarms |
| `GET` | `/api/v1/alarms/history?limit=100` | Persistent alarm lifecycle history |
| `GET` | `/api/v1/orders?limit=100` | Materialized production orders |
| `POST` | `/api/v1/orders` | Load a production order through the command bus |
| `GET` | `/api/v1/parts?limit=100` | Part records |
| `GET` | `/api/v1/cycles?limit=100` | Cycle records |
| `GET` | `/api/v1/traceability?limit=200` | Traceability events |

Order example:

```json
{
  "orderId": "ORDER-DEMO-001",
  "quantity": 3,
  "recipeId": "standard-widget",
  "recipeRevision": 1
}
```

## Recipes

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/api/v1/recipes` | List recipe revisions |
| `GET` | `/api/v1/recipes/active` | Active recipe snapshot |
| `POST` | `/api/v1/recipes/drafts` | Validate and save a draft |
| `POST` | `/api/v1/recipes/{id}/{revision}/approve` | Approve a revision |
| `POST` | `/api/v1/recipes/{id}/{revision}/activate` | Activate through the command bus |

## Commands

Submit commands to `POST /api/v1/commands`.

```json
{"type":"set-mode","mode":"Automatic","requestedBy":"operator-a"}
```

```json
{"type":"jog","axis":"X","value":0.05,"requestedBy":"engineer-a"}
```

```json
{"type":"inject-fault","fault":"guard-open","requestedBy":"demo"}
```

Supported command families include initialization, homing, mode selection, order loading, start, pause, resume, controlled stop, abort, manual jog, alarm acknowledgment, fault injection/clearing, recipe activation and recovery actions.

Every command returns:

- command ID
- accepted or rejected status
- machine-readable reason code
- human-readable reasons
- completion timestamp
- machine revision
- correlation ID

Rejection is a normal domain result, not an unhandled exception.

## Correlation

Clients may provide `X-Correlation-ID`. The server returns the value in the response, puts it in the logging scope and propagates it into command results, domain events, activities and manufacturing delivery.

## WebSocket

Connect to `/ws/state`. The server sends complete JSON snapshots at the configured publication period. Each client has a bounded drop-oldest queue, so a stalled viewer cannot block the machine loop.

Clients must:

- tolerate additive fields
- reconnect after interruption
- replace local state with each complete snapshot
- treat the runtime as the only authoritative state owner
