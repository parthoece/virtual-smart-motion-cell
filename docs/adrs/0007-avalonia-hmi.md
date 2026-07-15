# ADR: Use Avalonia for desktop HMI

- Status: Accepted
- Date: 2026-07-14

## Context

WPF is Windows-only, while the project targets desktop Windows, Linux, and macOS.

## Decision

Implement the operator client in Avalonia with MVVM-style view models.

## Consequences

One UI codebase; native platform differences still require testing.
