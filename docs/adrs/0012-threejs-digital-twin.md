# ADR 0012: Bundle a Three.js digital twin as an independent client

## Context

A 3D view improves virtual-commissioning communication but must not own machine behavior.

## Decision

Use a locked npm dependency and bundle Three.js with Vite. The viewer consumes WebSocket snapshots or recorded replay data.

## Consequences

The viewer works in browsers across platforms, has no runtime CDN dependency, and can disconnect without affecting the machine loop.
