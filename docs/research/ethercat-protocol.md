# EtherCAT research protocol model

## Scope

The research benchmark generates **wire-format EtherCAT Ethernet frames** for its simulated motion segment. Frames use Ethernet EtherType `0x88A4`, an EtherCAT command-frame header, and an LRW logical read/write datagram. The raw frames are stored in PCAPNG and are designed to be decoded by EtherCAT-aware packet-analysis tools.

This is an offline protocol and process-image emulator. It does not open a raw network socket, discover equipment, control hardware, implement a complete EtherCAT MainDevice stack, or claim ETG conformance or certification.

## Cyclic exchange

Each benchmark communication cycle records:

1. a MainDevice-to-segment LRW request with command PDO values and Working Counter `0`;
2. a returned segment-to-MainDevice LRW frame containing simulated feedback PDO values;
3. a decoded exchange row in `messages.parquet`;
4. two decoded axis rows in `ethercat-pdos.parquet`.

When packet loss is injected, the request remains observable while the returned frame is absent. Delay changes the returned-frame timestamp. Duplication creates an additional returned frame. A Working Counter mismatch condition can reduce the returned counter while preserving the frame.

## Frame layout

```text
Ethernet II
├── Destination MAC: ff:ff:ff:ff:ff:ff
├── Source MAC:      02:00:00:00:ec:01
├── EtherType:       0x88A4
└── EtherCAT frame
    ├── Length: 11 bits
    ├── Type:   0x1 command frame
    └── LRW datagram
        ├── Command:         0x0C
        ├── Datagram index:  uint8
        ├── Logical address: 0x00001000
        ├── Data length:     56 bytes
        ├── IRQ:             0
        ├── Process image
        └── Working Counter
```

Ethernet padding is added when necessary. Captures omit an Ethernet FCS, which is common for host packet captures.

## Two-axis process image

The current process image uses a fixed, documented CiA 402-style PDO mapping. It is not presented as a vendor ESI mapping.

| Offset | Size | Axis | Direction | Semantic object |
|---:|---:|---|---|---|
| `0x00` | 2 | X | MainDevice → drive | Controlword (`0x6040` semantics) |
| `0x02` | 1 | X | MainDevice → drive | Modes of operation (`0x6060` semantics) |
| `0x03` | 4 | X | MainDevice → drive | Target position (`0x607A` semantics) |
| `0x07` | 4 | X | MainDevice → drive | Target velocity (`0x60FF` semantics) |
| `0x0B` | 2 | X | Drive → MainDevice | Statusword (`0x6041` semantics) |
| `0x0D` | 1 | X | Drive → MainDevice | Modes display (`0x6061` semantics) |
| `0x0E` | 4 | X | Drive → MainDevice | Position actual value (`0x6064` semantics) |
| `0x12` | 4 | X | Drive → MainDevice | Velocity actual value (`0x606C` semantics) |
| `0x16` | 4 | X | Drive → MainDevice | Following error (`0x60F4` semantics) |
| `0x1A` | 2 | X | Drive → MainDevice | Error code (`0x603F` semantics) |
| `0x1C` | 28 | Y | Both | Same mapping for the Y-axis drive |

All multibyte PDO values use little-endian encoding. Position and velocity values use `1,000,000` integer counts per simulation unit. The selected operation mode is cyclic synchronous position (`8`).

## CiA 402 state representation

The protocol layer maps the simulated drive state into Controlword and Statusword fields:

- `0x000F` requests operation enabled during normal simulated operation;
- `0x0027` represents an operation-enabled Statusword baseline;
- Statusword bit 10 is set when the target is reached;
- Statusword bit 7 is set for a large following-error warning;
- a simulated drive fault uses the fault-state bit pattern.

The machine-fault oracle remains separate. A mechanical or sensor fault does not automatically set a drive fault bit; the drive fields change only when the simulated drive state warrants it.

## Working Counter

The benchmark has two addressed simulated drive SubDevices. Its LRW exchange therefore expects a Working Counter of `6`. The returned Working Counter and validity flag are available in packet, message, and PDO tables.

Working Counter observations are features, not oracle labels. A researcher can study missing cycles, topology-like loss, or inconsistent process-data access without directly exposing the scenario identity.

## Analytical outputs

`normalized/network/packets.parquet` includes:

- EtherType, frame type, LRW command and datagram index;
- capture direction and timestamp;
- logical address;
- actual and expected Working Counter;
- machine state and production step at capture time.

`normalized/network/messages.parquet` includes one row per cyclic exchange:

- response latency and availability;
- duplicate indication;
- Working Counter validity;
- process-image length and slave count;
- production-cycle and part context.

`normalized/network/ethercat-pdos.parquet` includes one row per axis per exchange with decoded command, feedback, mode, Statusword, Controlword, and following-error values.

## Configuration

```yaml
network:
  protocol: ethercat
  cycle_period_ms: 20
  logical_address: 4096
  cia402_mode: cyclic_synchronous_position
  slave_count: 2
  base_delay_ms: 1.0
  base_jitter_ms: 0.2
```

The cycle period is a benchmark setting and does not constitute a hard real-time guarantee.

## Verification boundary

The repository tests verify:

- EtherType `0x88A4`;
- EtherCAT command-frame type;
- LRW command `0x0C`;
- datagram length and logical address;
- request/response process-image sizes;
- Working Counter values;
- packet-to-Parquet count agreement;
- X/Y PDO decoding;
- absence of oracle labels from observable protocol tables.

A future hardware or virtual-network adapter would require a genuine MainDevice stack, ENI/ESI configuration, raw-interface permissions, timing validation, interoperability testing, and explicit safeguards. That is outside the current offline benchmark.

## Public technical references

- EtherCAT Technology Group, “EtherCAT — the Ethernet Fieldbus”: <https://www.ethercat.org/en/technology.html>
- CAN in Automation, “CiA 402 series: CANopen device profile for drives and motion control”: <https://www.can-cia.org/can-knowledge/cia-402-series-canopen-device-profile-for-drives-and-motion-control>
- Wireshark display-filter reference for the EtherCAT frame header: <https://www.wireshark.org/docs/dfref/e/ethercat.html>

