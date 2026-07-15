# Simulated MES integration

`VirtualSmartMotionCell.MesSimulator` is a separate ASP.NET Core process that provides a small manufacturing-order and result interface for integration and resilience testing.

## Run

```bash
dotnet run --project tools/VirtualSmartMotionCell.MesSimulator
```

Default URL: `http://localhost:8090`.

## Endpoints

| Endpoint | Purpose |
| --- | --- |
| `POST /api/v1/orders` | Queue a production order |
| `GET /api/v1/orders/next?machineId=...` | Assign the next queued order |
| `POST /api/v1/results` | Accept an idempotent machine event |
| `GET /api/v1/orders` | Inspect queued and assigned orders |
| `GET /api/v1/results` | Inspect received results |
| `POST /admin/state` | Simulate offline state, latency, and duplicate responses |
| `GET /health/ready` | Readiness probe |

## Reliability behavior

The machine commits production evidence to a local outbox before attempting delivery. Each event ID is also the idempotency key. A temporary MES failure therefore degrades integration health but does not remove the local production record. The outbox publisher retries with bounded exponential backoff, and HTTP `409 Conflict` is treated as an idempotent acknowledgement.

## Demonstration

1. Start the MES simulator and runtime.
2. Queue an order through the MES API.
3. Start automatic operation from the HMI.
4. Set the MES simulator offline.
5. Complete a cycle and inspect the pending outbox count.
6. Restore the MES simulator and verify exactly-once materialization by idempotency key.

The simulator demonstrates integration contracts and failure behavior; it is not an implementation of a specific commercial MES product.
