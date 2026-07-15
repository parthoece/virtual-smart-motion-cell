# C4 level 3 — Runtime components

```mermaid
flowchart LR
    Endpoint[Command endpoint] --> Bus[Bounded command bus]
    Bus --> Processor[Single command processor]
    Processor --> Coordinator[Machine coordinator]
    Loop[10 ms simulation loop] --> Coordinator
    Coordinator --> Snapshot[Immutable snapshot store]
    Coordinator --> Events[Domain event queue]
    Events --> EventStore[Append-only event store]
    Events --> Outbox[Integration outbox]
    Snapshot --> Publisher[WebSocket publisher]
    Snapshot --> Metrics[Health and metrics]
```

The coordinator is the only component allowed to mutate machine state. The command processor and simulation loop may run on different hosted tasks, but coordinator operations are serialized by its state lock.
