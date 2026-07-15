# Platform support

## Tier 1 — CI-gated source builds

- Windows on the current GitHub-hosted Windows runner
- Ubuntu on the current GitHub-hosted Ubuntu runner
- macOS on the current GitHub-hosted macOS runner
- Chromium, Edge, Firefox, and Safari versions with WebGL2

The CI matrix restores, builds, executes the architecture specifications, and publishes a smoke package on each desktop runner. Exact CPU architecture follows GitHub's hosted-runner image used for that workflow run.

## Tier 2 — cross-published release targets

- Windows x64 and ARM64
- Linux x64 and ARM64
- macOS x64 and ARM64
- Debian/Ubuntu desktop distributions compatible with Avalonia native dependencies

Release automation cross-publishes self-contained artifacts for these runtime identifiers. Architectures not represented by a hosted runner require community or maintainer smoke validation before promotion to Tier 1.

## Tier 3 — experimental

- Embedded Linux framebuffer/DRM
- other Linux distributions
- containerized HMI
- mobile and WebAssembly HMI targets

The headless runtime has fewer native dependencies than the Avalonia HMI. A platform can therefore support the runtime even when desktop HMI support is best effort.
