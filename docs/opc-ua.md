# OPC UA simulation server

The API process hosts a read-only OPC UA simulation server through `VirtualSmartMotionCell.OpcUa`. It publishes the same immutable machine snapshot consumed by the HMI and browser viewer.

## Default endpoint

```text
opc.tcp://localhost:4840/vsmc
```

Configure it through `config/runtime.json` or environment variables.

## Information model

```text
Objects
└── VirtualSmartMotionCell
    ├── State
    │   ├── Mode
    │   ├── ExecutionState
    │   ├── ProductionStep
    │   ├── MotionPermitted
    │   ├── ActiveAlarm
    │   └── MotionAdapter
    ├── Axes
    │   ├── X/ActualPosition
    │   ├── X/FollowingError
    │   ├── Y/ActualPosition
    │   └── Y/FollowingError
    ├── Production
    │   ├── CycleCount
    │   ├── Oee
    │   ├── ActiveOrder
    │   ├── CompletedQuantity
    │   ├── TargetQuantity
    │   └── ActiveRecipe
    └── Integration
        └── MesHealth
```

## Security boundary

The bundled server uses anonymous access and `SecurityPolicy None` for local simulation. It is not production-hardened and does not claim OPC Foundation certification. Before operational use, add certificate provisioning, trust-list administration, authenticated identities, role permissions, encrypted endpoints, audit events, and conformance testing.

## Contributor extension

Keep OPC UA node construction inside the OPC UA adapter. Domain and sequence projects must not reference OPC UA packages. Add nodes by mapping immutable snapshots rather than reaching into mutable controller internals.
