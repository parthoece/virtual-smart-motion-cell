# Contributor quickstart

1. Install the .NET 10 SDK, Git, and Python 3.11+ if you plan to touch the reference simulator.
2. Fork and clone the repository.
3. Run `./scripts/dev-check.sh` or `pwsh ./scripts/dev-check.ps1`.
4. Start the runtime with `dotnet run --project src/VirtualSmartMotionCell.Api`.
5. Open the viewer and run the HMI in another terminal.
6. Create a focused branch and add tests or an executable specification.
7. Update documentation when a public contract or behavior changes.

Good first contributions include new simulated sensors, viewer improvements, documentation examples, localization, metrics, and fault scenarios. Architectural changes require a design proposal before implementation.
