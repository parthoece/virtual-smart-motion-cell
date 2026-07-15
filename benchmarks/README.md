# Research benchmarks

The first implemented benchmark is **VSMC-DynamicGantry-v1**. It combines a continuous axis model with discrete production events, changing orders, queues, product-specific payloads, changeovers, maintenance, seeded variation, and synchronized data capture.

## Included manifests

| Manifest | Purpose |
| --- | --- |
| `normal-operation.yaml` | Dynamic baseline without injected faults |
| `machine-fault.yaml` | Primary gradual mechanical-fault benchmark |
| `network-fault.yaml` | Secondary communication-timing benchmark |
| `combined-fault.yaml` | Multi-label machine and network condition |

Run a benchmark through the visual studio or:

```bash
vsmc-bench run benchmarks/manifests/machine-fault.yaml --output runs
```

The manifest is the source of truth for the browser, CLI, CI, and publication scripts.
