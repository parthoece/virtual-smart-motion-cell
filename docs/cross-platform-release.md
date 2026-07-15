# Cross-platform build and release

## CI support

Every pull request builds the complete .NET solution on:

- Windows
- Ubuntu Linux
- macOS

The browser viewer and Python numerical oracle run in separate jobs. End-to-end CI starts the runtime and simulated MES, exercises REST and WebSocket APIs, checks the OPC UA TCP endpoint, and verifies persisted integration evidence.

## Release artifacts

The release workflow publishes self-contained artifacts for:

```text
win-x64
win-arm64
linux-x64
linux-arm64
osx-x64
osx-arm64
```

Each runtime identifier receives machine-runtime, Avalonia-HMI and MES-simulator packages. The release also includes the bundled Three.js viewer, checksums, an SPDX SBOM and GitHub artifact attestations.

## Platform claim policy

A platform is **supported in source** when a target is present in CI and release definitions. It is **tested** only after a green job has run on that operating system. Maintainers must not publish the first tagged release until the complete CI and end-to-end workflows pass.

## Local commands

```bash
dotnet restore VirtualSmartMotionCell.sln
dotnet build VirtualSmartMotionCell.sln -c Release
dotnet run --project tests/VirtualSmartMotionCell.Specs -c Release --no-build
dotnet run --project tests/VirtualSmartMotionCell.IntegrationSpecs -c Release --no-build
npm ci --prefix web/viewer
npm run build --prefix web/viewer
```
