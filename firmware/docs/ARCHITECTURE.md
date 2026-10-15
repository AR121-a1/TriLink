# WakeNIC-P2P / TriLink v0.5 architecture

> Public copy, redacted for publication on 2026-10-15. Board1/Board2/Board3 are stable public labels, not transmitted MAC addresses. Physical MAC/USB serial/PnP instance identifiers, local COM assignments and backup hashes are withheld. Original private records remain unchanged. Historical local evidence paths are not public downloads.

> 2026-10-11：后续职责、公共业务分发、模式与容量约束以 [当前平台设计](../../docs/P2P_PLATFORM_RESPONSIBILITIES.md) 为准。新接口尚未实现；传输使用有界易失缓存，不引入板端资源库。本页仍为历史 v0.5 快照。

> 历史架构快照。当前 v0.9 Room / RGB 见 [V09_ROOM_RGB.md](V09_ROOM_RGB.md)，自动路由底座见 [V08_AUTO_ROUTING.md](V08_AUTO_ROUTING.md)。v0.6 的真实 USB 后端与 6 邻居结构见
> [V06_USB_DISCOVERY.md](V06_USB_DISCOVERY.md)，本页保留用于追溯。

## Compressed backbone

```text
app_main
  +-- wakenic_board: exact HW-678A N16R8 capacity check
  +-- wakenic_ports: native-USB extension seam (still inert)
  +-- trilink_node task: sole owner of membership and leader view
  |     +-- trilink_protocol: 200-byte wire frame + CRC32 + borrowed views
  |     |     `-- service envelope + dormant content/file wire codecs
  |     +-- trilink_cluster: fixed two-peer table + expiry + leader
  |     `-- trilink_radio: ESP-NOW + fixed RX pool + serialized TX
  `-- wakenic_core: boot/readiness queue -> reducer -> diagnostics
```

`wakenic_core` owns the boot/readiness reducer; `trilink_node` owns wireless
membership. Wi-Fi callbacks mutate neither model. There is no dedicated project
radio RX task in v0.5.

## Receive ownership and lifetime

```text
Wi-Fi driver buffer
   | one bounded memcpy, <= 200 B
   v
[16 x fixed RX slots in internal RAM]
   | ready/free queues move uint8_t indices only
   v
trilink_node borrows const slot
   | frame view -> service view; payload remains in the slot
   v
release the exact slot pointer
```

Each slot moves through `FREE -> FILLING -> READY -> ACQUIRED -> FREE`. The
callback uses zero-wait queue operations and returns immediately if no slot is
free. The node must release every successfully acquired pointer after all view
consumers finish. A view must never be stored beyond that release.

This preserves one state owner while removing the former radio-worker handoff,
node RX queue and decoder payload copy. Invalid/double release attempts are
counted as ownership errors.

## Send path

HELLO and heartbeat sends retain the locally proven model: one mutex, one
completion semaphore, one ESP-NOW send in flight and a 500 ms timeout. It is
synchronous but low-rate. v0.4 deliberately does not reserve another task or
bulk TX queue before file transfer exists.

When bulk transfer is added, it should contribute its own bounded window and a
control-first scheduler rather than enlarge the callback or membership task.
It must not buffer a complete file in internal RAM.

## Application multiplexing and content seam

`TRILINK_MSG_APP_DATA` carries a four-byte service header:

```text
service_id:u8 | opcode:u8 | data_len:u16-le | data[0..168]
```

IDs 1, 2, 3 and 4 name game, file, diagnostics and content. IDs 32..255 are
available to future application modules. Service and frame decoders return
non-owning views, so text, summaries and file chunks can be validated and
consumed without another payload copy.

v0.5 adds stateless codecs for:

```text
CONTENT: TEXT | SUMMARY | GET_RANGE | ERROR
FILE:    OPEN | CHUNK | ACK | CLOSE | CANCEL
```

Content exchange is peer-to-peer and does not route through the game Leader.
Broadcast remains discovery-only; a group text/summary is fanned out as two
unicasts. The current node still records APP_DATA as deferred, so service,
content and file objects compile with host coverage but are absent from the
current firmware map.

## State semantics

```text
BOOT --APP_BOOT--> INIT
INIT --board + real ESP-NOW port + USB seam--> CORE_READY

TriLink node:
DISCOVERY --two active peers--> READY
READY --peer age >= 2000 ms--> DISCOVERY
any membership view --recompute--> lowest active MAC leader
```

`CORE_READY` does not claim three boards are connected. `cluster=READY` means
the local volatile table currently has two peers. Lowest-MAC selection remains
a deterministic discovery bootstrap, not quorum consensus.

## Resource contract

- RX slot: 210 B actual, bounded by a 216 B static assertion;
- RX pool: 16 slots / 3,360 B, fixed in internal RAM;
- ready/free queue payload: 16 B each because only slot indices move;
- project radio RX task: none;
- node task: 4,096 B retained until hardware high-water evidence exists;
- RX callback static frame: 48 B, guarded at 64 B;
- node loop static frame: 64 B, guarded at 96 B.

The v0.5 codec-only addition changes none of those runtime objects. The clean
image is 753,040 B and static DIRAM remains 122,543 B. The three dormant codec
objects total 4,105 B of object-level text and zero data/bss; that is a future
all-functions reference, not current linked cost.

See `TRILINK_V05_CONTENT_EXCHANGE.md` for protocol decisions, exact layouts,
occupancy and activation gates. `TRILINK_V04_LEAN_DATA_PATH.md` remains the
historical RX optimization record; `TRILINK_V03_SKELETON.md` is the earlier
pre-optimization description.
