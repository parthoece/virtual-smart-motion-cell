# Current limitations

The project is an equipment-software reference architecture and virtual-commissioning simulator. It intentionally separates software evidence from physical commissioning and certification.

## Not provided

- hard real-time scheduling guarantees
- certified functional safety or safe-motion behavior
- control of physical machinery
- vendor-specific servo, PLC, camera, barcode, or I/O conformance
- OPC Foundation certification or hardened production OPC UA security
- conformance with a specific commercial MES product
- production-ready identity, role, certificate, and secret management
- high-fidelity rigid-body, electrical, friction, backlash, thermal, or compliance physics
- guaranteed deterministic wall-clock timing under every operating-system scheduler

## Reference implementations

The file-backed production stores, event history, checkpoint, outbox, and recipe store make failure behavior transparent and testable. They are replaceable examples, not substitutes for a production database or broker.

The bundled OPC UA server is read-only and intentionally configured for anonymous local simulation. The HTTP MES is a fault-injection test service, not an ISA-95 or vendor-specific product implementation.

The Three.js viewer and Avalonia HMI are clients of immutable runtime state. Neither client owns motion logic or provides a safety function.

## Verification boundary

The repository contains Windows, Linux, and macOS CI and release definitions. A platform should only be described as tested after the corresponding GitHub Actions job is green. The first tagged release must not be published before the complete CI and end-to-end workflows succeed.

## Safe extension rule

New hardware or command adapters must remain disabled or read-only by default. Contributors must document trust boundaries, timeout behavior, stale-data handling, recovery, configuration, test doubles, and safe shutdown before operational use is considered.

## Research benchmark limitations

The dynamic research extension currently implements one synthetic environment. It does not yet include publication-frozen schemas, external fault-signature review, official baseline models, FMI import, multi-environment transfer studies, public datasets with persistent identifiers, cyber-influenced effect execution, or a honeypot gateway. The PCAPNG traffic contains offline wire-format EtherCAT frames and remains local to the recorder. The project does not implement a hardware-capable MainDevice, ENI/ESI commissioning, Distributed Clocks, mailbox services, raw-interface transmission, ETG conformance testing, or certified interoperability. Machine-fault signatures require external validation before they can be used to claim performance on physical equipment.
