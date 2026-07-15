# Observability

The runtime exposes four complementary evidence channels.

## Structured logs

ASP.NET requests and machine commands carry a correlation ID. The runtime uses logging scopes so a command can be followed through validation, state transition, motion execution, persistence, and manufacturing delivery.

Clients may provide:

```text
X-Correlation-ID: commissioning-session-42
```

The same value is returned in the response and included in command results, machine events, alarms, and traces.

## Health

- `/health/live` confirms that the process is running.
- `/health/ready` confirms that a machine snapshot is available.

## Metrics

`/metrics` exposes Prometheus text for revision, deadline misses, following error, production cycles, OEE, and outbox depth. The application also registers OpenTelemetry runtime, ASP.NET Core, HTTP client, machine meter, and machine activity instrumentation.

## Traces

Set `Observability:OtlpEndpoint` to export OTLP traces and metrics. Important spans include:

```text
HTTP request
└── machine.command
    ├── state transition
    ├── motion activity
    ├── persistence
    └── mes.deliver
```

## Operational questions

The observability model should let a reviewer answer:

- Why was a command rejected?
- Which interlock stopped motion?
- Why did one cycle take longer?
- Was a production event persisted before MES delivery?
- How old is the latest HMI snapshot?
- Did a slow client affect the machine loop?
