# C4 level 2 — Container view

```mermaid
flowchart TB
    HMI[Avalonia desktop HMI] -->|REST commands| API
    HMI -->|WebSocket snapshots| API
    Browser[Three.js browser viewer] -->|WebSocket snapshots| API
    API[ASP.NET Core machine host] --> Runtime[Hosted runtime services]
    Runtime --> App[Application + domain]
    App --> Control[Motion simulation]
    Runtime --> Files[(Events · checkpoint · outbox)]
    Adapter[External adapter process/library] --> SDK[Adapter contracts]
    SDK --> App
```

The API and runtime share one process for a single virtual machine. The HMI and browser remain independent processes. This is a deliberate modular-monolith decision, not an unfinished microservice migration.
