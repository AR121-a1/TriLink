# TriLink 0.16.0 Architecture Reference

Publication layout note (2026-10-15): client paths are repository-relative; firmware is in `firmware/`. The [public release record](../releases/v0.16.0/README.md) supersedes earlier local-only publication policy. Linked validation summaries are portable derivatives of the original local receipts; existing build and hardware boundaries remain unchanged.


Document revision: 2026-10-14. Product: `0.16.0-local`. Firmware: `0.16.0`.

This is a source-backed description of the current system, not a list of promised features. Use the [Development Guide](DEVELOPMENT_GUIDE_0160.md) for implementation recipes, tests, and release steps. Use [RELEASE_0160](RELEASE_0160.md) for the frozen delivery record. Earlier dated sections in other documents remain historical evidence.

## 1. Product definition and status

TriLink is an ESP32-S3 autonomous wireless node with an optional Windows control console. The S3 owns discovery, wireless routing, volatile Room membership, and supported device actions. The PC supplies interaction, diagnostics, and desktop plugins. A future data-transfer feature will use PCs as sources and sinks; it will not turn the S3 into persistent NAS storage.

There are three different connections:

| Connection | Current mechanism | What it does not mean |
| --- | --- | --- |
| PC to its attached S3 | Native USB CDC; optional USB NCM/HTTP | CH343/CH340 UART is not the Room control interface |
| S3 to other S3 nodes | ESP-NOW discovery, bounded routing, Room protocol | Not an IP subnet, Internet P2P, or a transparent Ethernet bridge |
| Existing desktop game to another PC | GameLink UDP | Not automatically carried by S3 or enabled by joining a hardware Room |

The supported local topology is one attached S3 per PC. Six direct neighbors exclude the local node; six Room members include it. Three boards are a useful acceptance setup, not a minimum for local operation.

| Capability | 0.16.0 implementation status | Evidence boundary |
| --- | --- | --- |
| Autonomous discovery, Room, routed RGB | Implemented; inherited state machines retained | Software regression; old hardware runs do not qualify this new image |
| Shared PC queue, snapshots, pause, receipts | Implemented | Release/Debug and fake/lifecycle tests |
| TinyUSB composite CDC+NCM | Built into the new firmware | Enumeration and real USB behavior still pending |
| Board-local management page | Embedded assets and HTTP service implemented | Actual JS tested with fake DOM/HTTP, not real browser/USB acceptance |
| Desktop plugin import/enable/update | Implemented for PC DLL packages | Not an ESP32 plugin loader |
| Text Tools | Local UTF-8 size, hash, and Base64 inspection | No P2P text send/receive |
| Continuous files, package alignment, operating modes | Design only | No production capability bit or usable transfer API |
| HID wake, MSC, firmware OTA, dynamic C loading | Not implemented | TinyUSB availability does not establish these features |

## 2. System map

```text
PC A                                                    PC B
  WinForms shell / feature plugins                        Optional console
              | typed intent + copied snapshot                  |
       NodeSessionService                                       |
              |                                                 |
       TriLinkSerialWatcher                                     |
              | one _ioOwner                                    |
       CompositeSerialIo                                        |
          +---+------------------+                              |
          | CDC                  | NCM / HTTP                   | USB
          |                      |                              |
          |       Browser -------+                              |
          |                      v                              |
S3 A      |             bounded HTTP mailbox                 S3 B
          +----------------------+                              |
                         USB worker / mux                       |
                                |                               |
                         node bridge queue                      |
                                v                               |
                    node task: Room + route + app               |
                         |                 |                    |
                     RGB worker       radio TX worker           |
                                           |                    |
                                           +---- ESP-NOW -------+
```

The browser bypasses the PC session service, but not the firmware command arbiter. Consequently, a PC queue is not a global lock across browser and desktop. Firmware validation remains authoritative.

### 2.1 Responsibility boundaries

| Owner | Owns | Must not own |
| --- | --- | --- |
| Desktop shell | Window/tray lifecycle, navigation, user feedback | Radio retries or authoritative Room membership |
| Feature view | Selection, explicit confirmation, subscription lifetime | A new COM connection, polling timer, or independent node coordinator |
| Shared NodeSessionService | One binding, bounded intent FIFO, snapshots, polling, pending RGB result tracking | Native I/O, GPIO, or hidden firmware state changes |
| Serial plugin / transport adapters | Enumeration, physical identity, exclusive I/O, strict protocol parsing | Simulated success or retrying an uncertain mutation on another transport |
| USB worker / mux | CDC and HTTP arbitration, bridge reply ownership, timeout isolation | Room policy or two simultaneous bridge transactions |
| Node task / bridge | Room, route, app state, command validation and dispatch | UI state or unbounded payload storage |
| Radio / RGB workers | Bounded physical work and completion reporting | Mutating Room state from callbacks |

The foundation deliberately stays compiled and fixed. Discovery and protocol dispatch cannot depend on downloading the plugin needed to discover or transfer that same plugin.

## 3. Source and ownership map

Client paths below are relative to the repository root. Firmware paths are relative to `firmware/`. The original local workspace used sibling `trilink_game_branch/` and `trilink_firmware/` directories; those historical paths do not define the public checkout.

| Area | Primary source | Reading purpose |
| --- | --- | --- |
| PC startup | `src/TriLink.MinClient/Program.cs` | STA entry, profile, single instance, lifetime |
| Plugin composition | `src/TriLink.PluginHost/PluginRuntime.cs`, `PluginServices.cs` | Dependency ordering, services, cleanup |
| Package management | `src/TriLink.PluginHost/ModuleStore.cs` | Preview/import, local overlays, restart semantics |
| Public contracts | `src/TriLink.Plugin.Abstractions/` | Real interfaces; do not invent similar-looking APIs |
| Shared node service | `src/Plugins/TriLink.Plugin.HardwareRoom/NodeSessionService.cs` | Queue, identity, freshness, polling, result ownership |
| Hardware view / wire | `HardwareRoomView.cs`, `RoomWire.cs` in the same directory | Thin UI and C# wire validation |
| USB discovery and I/O | `src/Plugins/TriLink.Plugin.Serial/` | Watcher, SetupAPI catalog, CDC/NCM adapters and parsers |
| Firmware startup | `main/app_main.c` | Actual startup order and failure path |
| Firmware scheduling | `components/trilink_node/trilink_node.c` | Node loop, RX ownership, fixed TX queues |
| Firmware business seam | `components/trilink_node/trilink_bridge.c`, `trilink_app.c` | Room/RGB commands, registry, autonomous tick |
| Wire/state machines | `components/trilink_protocol/` | Frame, service, route, Room, strict codecs |
| USB transport | `components/trilink_usb/` | Descriptors, NCM netif, worker and pure-C mux |
| Embedded management | `components/trilink_web/` | HTTP policy, mailbox, static assets |

`TriLink.Core` is a historical C# namespace, not proof of an independent core assembly. Similarly, `wakenic_*` firmware names remain valid; renaming them is not a prerequisite for extending TriLink.

## 4. Firmware lifecycle and concurrency

### 4.1 Startup

The current `app_main` sequence is:

1. Initialize the core controller and post boot state.
2. Log and validate the board configuration. A failure enters the controller failure path.
3. Start the native USB transport, NCM interface, and local web service.
4. Start the radio/node subsystem.
5. Run `wakenic_core_run()` in the main task.

USB can exist before the node is ready; enumeration is not a successful application handshake. USB and Wi-Fi initialization share an IDF default event loop, so an already-created loop is handled deliberately rather than treated as a fatal duplicate.

### 4.2 Task and buffer ownership

| Execution context | Project stack / priority where explicitly configured | Responsibility |
| --- | --- | --- |
| Node task | 4096 B / 5 | Own Room, route and module state; process bounded RX; run periodic work |
| Radio TX worker | 3072 B / 4 | Perform ESP-NOW sends and bounded callback waits |
| RGB worker | 3072 B / 2 | Execute approved LED work and report driver completion |
| USB worker | 3072 B | Parse/serialize commands and be the sole bridge reply consumer |
| HTTP server task | 4096 B | Validate requests and use the single mailbox |
| TinyUSB/lwIP/Wi-Fi tasks | SDK-managed, additional to the above | USB/network stack work; included in runtime resource assessment |

Stacks above are configured task allocations, not measured high-water marks. The static per-function frame limit is a different quantity.

Radio RX callbacks copy bounded frame data into the fixed pool and return. The node consumes/releases pool slots; a frame view is borrowed only for that processing interval. Never save `message->data` for a later tick without a bounded owned copy. There is no dedicated project radio RX task and no task per application module.

The app tick runs independently of a visible PC page and before USB reply backpressure can skip bridge work. This enables existing autonomous behavior; it does not invent a general background task scheduler.

### 4.3 Discovery is not Room admission

The default radio channel is 6. HELLO runs at 500 ms and peer heartbeat at 1000 ms; direct-neighbor expiry is 2000 ms. This is a fixed-channel peer protocol, not all-channel Wi-Fi scanning.

The older `trilink_cluster` discovery leader is selected by MAC ordering. A real Room leader is the first member in the Room's ordered roster. These are separate concepts. The historical three-node discovery-ready threshold does not prohibit a one-person Room.

## 5. Desktop composition and shared node session

### 5.1 Plugin system

The console is .NET Framework 4.8 / WinForms. Host API is `1.0`. A profile selects nine built-in plugins; manifests declare entry assembly/type, service dependencies and provided services. The host resolves dependency/service order, calls `Configure`, then `Start`, and handles `Stop` and deferred cleanup.

| Plugin | Role |
| --- | --- |
| `trilink.desktop` | Window, tray and navigation |
| `trilink.serial` | Device discovery, CAPS and exclusive CDC/NCM I/O |
| `trilink.hardware-room` | Shared node session and real Room/RGB view |
| `trilink.modules` | Module management and on-demand feature registry |
| `trilink.text-tools` | Local text inspection only |
| `trilink.rooms`, `trilink.simulation` | Explicit desktop simulation domain |
| `trilink.game-link`, `trilink.thunder` | PC UDP transport and PC game |

An imported managed DLL runs with the process's user privileges. Service declarations and hashes are not a sandbox or a publisher signature. Import only trusted code. Updates activate on restart; the host does not unload and hot-replace a running assembly.

### 5.2 Public contracts

- `IDeviceDiscoveryService`: attached devices, arrival/removal/status events, discovery polling and nearby search.
- `INodeCapabilityService`: validated node firmware/boot/capacity/module description.
- `IHardwareCommandService`: existing low-level Room/RGB compatibility commands. This is not a generic arbitrary-command API for new plugins.
- `INodeSessionService`: `Bind(nodeId, portName)`, `PausePolling()`, `ResumePolling()`.
- `INodeSessionSubscription`: `Snapshot`, `Changed`, `RefreshAsync()`, `Submit(intent)`, `SetVisible(...)`, `CancelPending()`, `Dispose()`.

The shared service currently lives in the HardwareRoom plugin, despite serving multiple views. New views reuse it; they do not construct another `NodeSessionService`. A future physical relocation must preserve service identity, single composition, and tests.

### 5.3 Queue and freshness invariants

The current budgets are 16 subscriptions, one active node binding, eight queued intentions plus an executing operation. Approval/rejection intentions expire after 6 seconds; other intentions after 15 seconds. These limits are not application throughput guarantees.

```text
click -> validate enough to accept/reject immediately
      -> bounded FIFO / duplicate ownership
      -> fresh identity + boot + Room/role/ticket checks
      -> one physical I/O owner
      -> submitted / rejected / failed / uncertain result
      -> refresh authoritative state and track supported receipts
```

`NodeSubmissionStatus` is one of `Queued`, `Duplicate`, `QueueFull`, `InvalidTarget`, `InvalidState`, `Unsupported`, `Disposed`. `Queued` is not device acceptance. `NodeOperationIntent` is a Room/RGB-specific contract, not the proposed future generic transfer operation.

Binding and preflight use physical node identity, endpoint and capability information. A changed MAC, PnP identity, transport binding, node boot or relevant Room generation invalidates stale work. Snapshot DTOs are copies; editing one cannot update the board.

Background reads coalesce and yield at each completed USB transaction so foreground work can progress. They do not release a COM port in the middle of a transaction. Approvals may still expire, and a queue can still fill: responsive means accepting an intent or explaining why it cannot be accepted, not promising every button will execute in every state.

Closing a view releases its subscription and ownership of unsubmitted work. A deduplicated operation requested by another view remains. Submitted work/results belong to the shared service, not the destroyed control.

### 5.4 Three different periodic mechanisms

| Mechanism | Owner | Pause meaning |
| --- | --- | --- |
| Attached-device enumeration | Serial watcher | Stop automatic host enumeration; explicit resume clears its limiter |
| Real Room/status refresh | Shared node session | Preserve `ManuallyPaused`, `FailurePaused`, or `DeviceChanged` until explicit recovery |
| Wireless HELLO/heartbeat/Room work | S3 node task | Continues independently of PC view visibility |

Node polling states are `NotStarted`, `Running`, `ManuallyPaused`, `FailurePaused`, `DeviceChanged`. Five consecutive automatic failures latch a pause; late results and reopening a view must not silently resume it. A successful background sample is not authority to override an already-latched pause.

Light refresh is approximately 2 s for a leader and 5 s otherwise. Active-Room, non-full background SEARCH is throttled to at least 15 s; full/manual and inactive-Room refreshes can search immediately. These are scheduling policies, not exact wall-clock service deadlines.

X hides the main window to the tray. Tray Exit performs shutdown. A surviving process or offscreen-rendered control alone does not establish that a user-visible window was displayed.

## 6. USB, NCM and HTTP boundary

### 6.1 USB configuration

The application uses TinyUSB full-speed OTG, replacing the old application USB Serial/JTAG driver. Both share the internal PHY and must not own it concurrently. UART0 via CH343/CH340 remains the recovery/flashing/log path. Windows can assign a new COM number after this migration.

| Descriptor property | Current value |
| --- | --- |
| VID:PID / bcdDevice | `303A:4011` / `0160`; development identity, not a production allocation |
| Product / serial | `TriLink USB Node` / physical Wi-Fi MAC as 12 uppercase hex characters |
| CDC interfaces / endpoints | `00/01`; notification `81`, bulk `02/82` |
| NCM interfaces / endpoints | `02/03`; notification `83`, bulk `04/84` |
| Endpoint budget | EP0 plus four IN and two OUT |
| Power/wake | Declared bus-powered 500 mA; no remote-wakeup claim |

CDC readiness does not require DTR/RTS. TinyUSB FIFO persistence is disabled; bus reset clears CDC data. Partial input expires after 500 ms or USB epoch change. An old-epoch reply is not delivered to a new enumeration.

### 6.2 Host transport selection

Windows 10 uses CDC without depending on NCM. On a suitable host, CDC is still preferred for the same physical MAC. NCM is an optional validated alternative for subsequent operations, not a retry route for a failed write.

NCM admission checks the exact USB `VID_303A&PID_4011&MI_02` binding, operational interface, local IPv4/mask, and the best route to the fixed board address. The binding fingerprint includes PnP identity, interface GUID/index and source IPv4. Ambiguous or conflicting bindings fail closed.

`TriLinkDevice.TransportKind` is `UsbSerial=0` or `UsbNcm=1`. For NCM, `PortName` is the machine key `NCM:<interface GUID>`, not a URL. `ManagementUrl` is display metadata after validation, not permission to connect to arbitrary addresses.

`NcmHttpClient` binds the socket to the validated source/interface and talks only to `192.168.240.1:80`. It does not use a system proxy, follow redirects, resolve DNS, or automatically retry POST. Identity/binding is rechecked before writing.

### 6.3 Board-local network

- S3: `192.168.240.1/24`; DHCP leases: `.2` through `.3`.
- No advertised default gateway or DNS, no forwarding/NAT/IPv6, no Wi-Fi bridge.
- Host and board use different locally administered MACs derived from the physical MAC.
- Two aligned 1514 B RX slots; exhausting them drops a frame instead of expanding memory.
- Synchronous network TX has a 20 ms wait budget; NCM uses two 2048 B NTBs per direction.

The two-address pool is the smallest explicit range accepted by the pinned IDF configuration, not support for two physical USB hosts. A collision with an existing `192.168.240.0/24` network requires diagnosis or CDC use; the program does not rewrite host routes to force access.

### 6.4 HTTP surface

| Route | Purpose |
| --- | --- |
| `GET /` | Embedded HTML |
| `GET /app.js`, `GET /app.css` | Embedded assets; no CDN dependency |
| `GET /api/v1/session` | RAM session token, protocol, firmware, endpoint and board uptime |
| `POST /api/v1/exchange` | One strict ASCII TRILINK line, including final LF |

The request body is less than 384 B; reply is less than 2048 B. Token header is `X-TriLink-Token`. Mutations also require `X-TriLink-Confirm: execute` and `X-TriLink-Deadline`, expressed in the board uptime clock; valid deadlines must be in the future and at most 5000 ms ahead. Current clients target about 2500 ms, not a user-PC UTC timestamp.

Allowed commands are V1 HELLO/SEARCH and V3 CAPS/ROOMGET/RGBRESULT/ROOM/RGB/RGBENABLE. HTTP does not expose arbitrary INFO/CONFIG/SEND/POLL/RELAY/TABLE diagnostics. Host, Origin, fetch-site, local destination and peer address are validated; no CORS or query-driven command routing. RAM tokens are not written to logs or disk.

This is local USB management with cross-site/misuse protections, not encrypted transport or authentication against a malicious local process. MAC, Room membership, CRC and SHA hashes are not wireless sender authentication. Current ESP-NOW peers also use `encrypt=false`; the wireless link is not confidential and must not carry secrets under an assumption of encryption.

### 6.5 Arbitration and uncertainty

CDC and HTTP share a 20-command/s admission limit and one bridge transaction. The mux binds owner, nonce, HTTP ticket and USB epoch. The HTTP handler uses a mailbox; only the USB worker submits bridge work and consumes bridge replies.

```text
mailbox: IDLE -> QUEUED -> RUNNING -> DONE -> IDLE
                              |
                       waiter times out
                              v
                         ABANDONED -> completion drains -> IDLE

mux: NONE -> CDC or WEB owner -> matching reply -> NONE
                    |
              2000 ms timeout
                    v
              RESULT_UNKNOWN, retain owner
                    |
              matching late reply drained -> NONE
```

An indefinitely missing bridge reply intentionally blocks further bridge work. CDC V1 HELLO/SEARCH can still read local snapshots, but HTTP commands, including HTTP HELLO/SEARCH, wait behind the occupied mux. Do not advertise universal management availability in this failure mode.

| Deadline/budget | Scope |
| --- | --- |
| 1200 ms | Board HTTP request-read deadline |
| 2000 ms | USB bridge result wait; timeout does not release uncertain ownership |
| 2200 ms | HTTP mailbox wait |
| 3500 ms | Desktop total deadline for each HTTP transaction, including final return check |

A session fetch, HELLO and business request are separate HTTP transactions. Their combined operation is not limited to 3500 ms. HTTP status such as 403, 409, 503 or 504 is a transport/policy result; the desktop may conservatively report an I/O failure. A timeout does not prove no mutation happened.

The page has a four-entry bounded queue, duplicate suppression, five-failure pause and manual resume. It revalidates identity/Room context before a confirmed write. Its UI implements inspection plus explicit create/leave only; approval, invitation, kick and RGB remain desktop operations.

## 7. Wire contracts and application execution

### 7.1 Version and capacity description

After a valid identity handshake, the current node accepts:

```text
TRILINK/3 CAPS <nonce>
TRILINK/3 CAPS <nonce> 1 0.16.0 <BOOT8HEX> 84 06 06 01
```

The response contains descriptor version 1, firmware version, boot instance, route body limit `0x84` (132 B), six neighbors, six members, and module mask `0x01` (RGB only). The notation above omits framing newlines for readability. The discovery CAPS bit signals descriptor support; older firmware must not be probed as though it supports the new command without checking.

Host API, plugin version, firmware version, descriptor version, outer frame version, route format and Room opcode are independent compatibility axes. A matching product version string does not establish wire compatibility.

### 7.2 Actual framing limits

```text
200 B outer frame
  - 24 B frame header
  -  4 B frame CRC
  =172 B payload
  -  4 B service header
  =168 B service data
  - 36 B route header
  =132 B routed application body
```

An application header is part of the 132 B, not additional space. Encode fields explicitly; do not transmit compiler struct layouts. Room packets are also 132 B, but use their own service 5/opcode 2 path, not the RGB route body encoding. The current RGB application payload is 19 B.

Route format 2 uses one local reliable send in flight, 1200 ms retry spacing and at most four attempts. Destination seen cache is 32 entries retained for 30 s; the separate transit cache is 32 entries with 10 s lifetime. Topology advertisements have their own bounded lifetime and forwarding rules.

A routed ACK means destination callback/inbox acceptance. It does not mean the LED driver finished, a PC consumed text, a file was saved, or all Room replicas agreed.

### 7.3 Room semantics

Room state is a volatile replicated roster with room ID, incarnation, revision, term and member boot identities. The leader handles approval/rejection/kick; other members may invite. Invitation acceptance still enters admission rather than bypassing the leader.

```text
request JOIN -> leader approval -> OFFER
       -> requester CONFIRM -> leader commits membership
       -> SNAPSHOT / member ACK -> synchronized management state
```

Tickets, expiry and revision floors prevent stale requests or old snapshots from completing a new admission. Missing membership acknowledgements freeze unsafe management; silence is not converted into an authorized kick.

Creating a one-person draft Room is supported. Once a formed Room falls to one member, it dissolves. With at least two remaining members, an orderly leader exit hands leadership to the next roster member; the departing leader retains a bounded 10 s handoff advertisement. This is not durable consensus, arbitrary crash failover, or persistent Room recovery.

Even one member rebooting changes its boot ID and loses that node's RAM Room state. Rediscovery is not automatic readmission; the remaining replicas do not silently delete the old member or elect a replacement leader. Missing acknowledgements can leave management frozen until an explicitly supported recovery path is used.

### 7.4 RGB closure

RGB is disabled by default and requires local permission on the target. The existing board path uses GPIO48; board/pin confirmation precedes physical use. Enabling permission is not an execution test.

```text
explicit permission -> submit pulse once -> target accepts
     -> worker runs -> driver result -> original operation ID queried
```

Local pulse and member pulse share device authority checks. The current local-pulse workflow still needs Room context: if absent, the desktop explicitly confirms creating and keeping a one-person Room. It is not a hidden prerequisite mutation or a generic Room-free local-action API.

The driver has bounded waits and fault latching. Driver completion is stronger than route acceptance but remains weaker than observing physical light. Unknown results retain uncertainty; changing pages or transports does not authorize a second pulse.

## 8. Extension model and resource strategy

There are four different extension objects:

| Object | Executes where | Update boundary |
| --- | --- | --- |
| PC managed plugin DLL and manifest | PC process | Trusted local import, validation, next restart |
| Firmware C module | Compiled S3 application | Compile, verify, explicitly flash a compatible image |
| Future bounded task/parameter data | Existing compatible S3 handler | Future schema/hash/permission checks; not arbitrary code |
| Firmware image/OTA | Boot/update subsystem | Separate future design with partition and recovery requirements |

The firmware registry has four composition-time slots. `trilink_app_module_t` declares capability bit, marker, version, exact wire size, context, receive callback and optional tick. It rejects duplicate marker/capability, invalid size/version, or excess slots. Current dispatcher state is 84 B in the built image.

This is a receive/tick dispatcher, not a finished generic module SDK. It provides no generic send service, authorization context, dynamic installer or variable-length stream engine. Room/route state is private to the bridge; RGB explicitly validates its context in `rgb_input`. New modules require a deliberately designed validated bridge/service seam. Registration alone grants no permission.

Use static bounded state, borrowed views, short handlers and shared scheduling. Add a dedicated worker only when a measured blocking peripheral requires isolation, not for every plugin. Do not equate 8 MiB PSRAM with unlimited internal USB/radio buffers.

### 8.1 Future stages, not current APIs

| Stage | Work | Release gate |
| --- | --- | --- |
| A, implemented | Shared node session, fixed module dispatch, CAPS | Existing Room/RGB compatibility and queue/pause regressions |
| 0.16 transport step, implemented in software | CDC+NCM and local web management | Separate physical enumeration, contention and wireless acceptance still pending |
| B | Small P2P text, endpoint credit and consumption acknowledgement | Single-board loopback and real two-board delivery distinguished |
| C | Continuous stream, bounded window dedup, control fairness | Transfer beyond old seen-cache capacity, constant-memory progress, cancellation |
| D | Capability/package alignment, bounded autonomous tasks | Compatible executor and live source required; explicit apply and expiry |
| E | Real-time replaceable state plus reliable events | Stale samples never replayed; measured latency under load |

`normal`, `bulk`, `realtime` are proposed scheduling policies, not current functioning mode switches. Mode, USB connection, Room membership and application session are separate state dimensions. Switching a future mode must not implicitly recreate the Room or grant GPIO permission.

The proposed first text budget is 512 UTF-8 bytes, with one TX and one RX session and bounded credit. The 96 B candidate chunk and 8 KiB incremental RAM target in the [responsibility plan](P2P_PLATFORM_RESPONSIBILITIES.md) are design budgets, not published wire limits.

Sending fresh chunk messages faster than entries expire can fill the 32-entry seen table within its 30 s retention window; slow traffic need not fill it. The current cache is not a continuous-stream sliding window. Application fragmentation alone does not solve this. Stage C needs session/sequence-aware bounded windows at both endpoints and forwarding nodes without weakening existing control/RGB anti-replay behavior. Short messages should not acquire a compression library without measured benefit.

## 9. Measured baseline and acceptance boundary

Target baseline: ESP32-S3-WROOM-1-N16R8 / HW-678A lineage, 16 MiB Flash, 8 MiB PSRAM, fixed ESP-IDF 5.5.5. Dependency lock pins esp_tinyusb 2.0.0 and tinyusb 0.18.0~2. Board defaults, partition layout and the narrow existing XMC flash compatibility path were retained; no eFuse change was made.

Physical Flash capacity is not the usable application partition: the current factory App partition is 1 MiB and there are no dual OTA slots. Larger images or OTA require an explicit partition/recovery design, not just another module registration.

The original final firmware receipt, represented by the [public firmware validation summary (derived)](../releases/v0.16.0/validation-summary.json) records:

| Quantity | Measured | Gate / interpretation |
| --- | ---: | --- |
| Application image | 898736 B | 1048576 B maximum; 149840 B headroom |
| Static DIRAM | 164979 B | 196608 B maximum; 31629 B project-budget headroom |
| Largest project static frame | 784 B | 1024 B maximum; not whole task stack use |
| Web assets | 14335 B | 32768 B maximum |
| RX pool / route / Room / dispatcher | 3360 / 3560 / 4472 / 84 B | Symbol sizes, not a complete RAM total |

These are static build measurements. They do not establish runtime free heap, largest free block, stack high-water marks, power quality, optical output, or long-run leak freedom.

The original final desktop receipt, represented by the [public validation summary (derived)](../releases/v0.16.0/validation-summary.json) records 102 frozen inputs, 22 portable files, nine plugins and a 179765 B ZIP. Each Release/Debug run passed 1244 checks; the C-to-C# CAPS fixture run passed 260 separately. Firmware passed 17 C host groups, eight probe unit tests and 32 embedded-JS fake checks.

At the frozen build baseline, USB hardware and other physical acceptance were pending. On 2026-10-14, three boards separately passed flashing, 35-second idle startup and native CDC read-only checks: [Board 1](../firmware/docs/HARDWARE_0160_BOARD1_20261014.md), [Board 2](../firmware/docs/HARDWARE_0160_BOARD2_20261014.md) and [Board 3](../firmware/docs/HARDWARE_0160_BOARD3_20261014.md). The third board needed native download-mode recovery. NCM matched the Microsoft driver but failed with Code10 on all three on this host. The [round overview](../firmware/docs/HARDWARE_0160_ROUND_20261014.md) preserves their separate scope. Windows10 CDC, NCM/DHCP/web, interactive desktop, multi-board 0.16.0 Room/RGB and loaded runtime-resource acceptance remain pending or failed. Neither original build flags nor old 0.14.x hardware/OpenSpeedy evidence are rewritten as these results.

## 10. Related documents

- [Developer recipes, tests, release and diagnosis](DEVELOPMENT_GUIDE_0160.md)
- [0.16.0 release and evidence](RELEASE_0160.md)
- [Current directory layout](DIRECTORY_LAYOUT.md)
- [PC plugin platform](PLUGIN_PLATFORM.md)
- [Longer-term responsibility and staged migration plan](P2P_PLATFORM_RESPONSIBILITIES.md)
- [Firmware USB boundary](../firmware/components/trilink_usb/README.md)
- [Room admission wire protocol](../firmware/docs/V010_ADMISSION.md)
- [Routing protocol](../firmware/docs/V08_AUTO_ROUTING.md)

When a document and implementation disagree, check the exact source and frozen receipt before editing the contract. Historical evidence is preserved, not rewritten into a new result.
