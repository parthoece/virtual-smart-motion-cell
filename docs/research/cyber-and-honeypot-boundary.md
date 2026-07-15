# Cyber and honeypot research boundary

Cyber and honeypot work is optional and follows the machine-fault benchmark.

## Allowed research scope

- synthetic message delay, loss, duplication, reordering, and stale data;
- constrained command, telemetry, recipe, and service effects inside the simulator;
- isolated decoy services using synthetic identities and data;
- controlled red-team or low-interaction honeypot observation;
- translation of allowlisted decoy interactions into virtual process effects;
- PCAPNG capture, graded labels, analyst annotations, and replay.

## Prohibited default capabilities

- targeting external or real industrial systems;
- automatic device discovery outside the isolated test network;
- arbitrary exploit or shell execution;
- real production credentials or configurations;
- unrestricted outbound access;
- direct attacker access to the experiment control plane or dataset store;
- physical machine commands.

## Research design

The attacker-facing decoy, effect translator, simulator, and data plane must run in separate trust zones. Real honeypot traffic does not receive oracle intent labels; observations, derived assessments, model predictions, and analyst review remain distinct.
