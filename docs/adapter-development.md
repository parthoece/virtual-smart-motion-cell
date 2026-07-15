# Adapter development

Adapters connect vendor or protocol concerns to stable machine contracts.

## Rules

- Do not reference an adapter package from the domain project.
- Keep blocking I/O outside the simulation loop.
- Support cancellation and explicit timeouts.
- Return structured health information.
- Never blindly retry motion commands.
- Include a deterministic fake for tests.
- Document thread-safety and reconnect behavior.

Start from `examples/VirtualSmartMotionCell.SampleAdapter`. A complete adapter contribution should include its descriptor, health check, configuration schema, failure tests, usage documentation, and compatibility statement.
