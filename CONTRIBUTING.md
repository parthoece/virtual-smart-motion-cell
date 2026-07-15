# Contributing

Thank you for helping build an approachable, testable bridge between controls engineering, motion software, and smart manufacturing.

## Good first contributions

- reproduce a documented scenario on another operating system
- add a unit or fault-injection test
- improve a diagram, explanation, accessibility, or terminology
- add a recipe or simulated plant model
- improve the evidence report or dashboard
- add vendor-neutral Structured Text examples
- propose an adapter interface without coupling core simulation code to a vendor SDK

## Before starting

1. Search existing issues and pull requests.
2. Use an issue form for defects, features, adapters, or documentation work.
3. Discuss public interfaces, database migrations, hardware adapters, and large dependencies before implementation.
4. Do not submit proprietary code, confidential machine diagrams, employer/customer data, credentials, vendor license files, or copied safety logic.

## Development setup

```bash
python -m venv .venv
source .venv/bin/activate       # Windows: .venv\Scripts\activate
python -m pip install --upgrade pip
python -m pip install -e ".[dev]"
make check
```

Run a normal and fault scenario:

```bash
smc simulate --cycles 1
smc simulate --cycles 1 --scenario emergency-stop --database artifacts/fault.sqlite || test $? -eq 2
```

## Engineering expectations

- keep the deterministic simulation reproducible
- use type hints and explicit validation
- separate commands, state, status, diagnostics, and persistence
- add normal and failure tests when behavior changes
- state whether code is simulation-only, pseudocode, or hardware-tested
- keep hardware integrations behind explicit adapter boundaries
- never imply that standard application logic is functional safety
- document units, timing assumptions, limits, and failure behavior

## Pull requests

A pull request should explain the problem, scope, design choices, evidence, and risk. It should be focused enough to review, update documentation when behavior changes, and pass CI.

Suggested commit prefixes: `feat:`, `fix:`, `docs:`, `test:`, `refactor:`, `perf:`, and `chore:`.

## Licensing

By submitting a contribution, you agree that it will be licensed under Apache-2.0. You retain copyright to your contribution.

Participation is governed by [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md). Report sensitive problems according to [SECURITY.md](SECURITY.md).

## Research benchmark contributions

Research changes must begin with a clear question, a versioned manifest or schema change, and evidence that the scenario modifies a plant, sensor, actuator, process, software, or network model rather than directly setting an alarm or target label.

Before opening a pull request:

```bash
python -m pip install -e "research[dev]"
pytest -q research/tests
vsmc-bench validate benchmarks/manifests/machine-fault.yaml
```

Keep oracle ground truth out of observable source tables. New benchmark environments and fault taxonomies require an architecture or research-scenario proposal and must document reproducibility, expected symptoms, metrics, limitations, and leakage risks. See [`docs/research/`](docs/research/README.md).
