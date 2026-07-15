# Python reference simulator

This directory preserves the original tested Python implementation as a numerical reference model and migration oracle for the .NET runtime.

It contains:

- discrete PID and motion-profile logic
- simulated two-axis plant and drive states
- pick–inspect–place sequencing
- deterministic fault scenarios
- telemetry, reporting, and API tests

Run it from the repository root:

```bash
python -m pip install -e "reference/python-simulator[dev]"
pytest -q reference/python-simulator/tests
```

The Python package is not the primary public runtime after v0.2. New platform architecture and operator UI work belongs in the .NET projects. Changes to numerical behavior should add parity scenarios that can be executed by both implementations.
