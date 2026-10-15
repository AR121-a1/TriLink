# TriLink 0.16.0 Development Guide

Publication update (2026-10-15): client source is at the repository root and firmware source is in `firmware/`. The current distribution includes selected [0.16.0 packages and portable evidence](../releases/v0.16.0/README.md), superseding the former local-only firmware rule. The guide itself does not authorize additional hardware operations.


Document revision: 2026-10-14. Read together with the [Architecture Reference](ARCHITECTURE_0160.md).

This guide explains where to make a change, which existing interfaces to reuse, how to test it, and what evidence is needed before calling it complete. It does not authorize flashing, GPIO actions, destructive cleanup, or uploads beyond the explicitly selected publication scope. Commands below are instructions for future development; writing this guide did not run them or revalidate the delivered binaries.

## 1. Reading routes

| Task | Read first | Then use |
| --- | --- | --- |
| Understand the whole system | Architecture sections 1-5 | End-to-end flows below |
| Add a local desktop tool | Plugin recipe below | `TextToolsPlugin.cs`, plugin contracts and host tests |
| Add another real-device page | Shared-session recipe below | `NodeSessionContracts.cs`, `HardwareRoomView.cs` |
| Add an S3 action | Firmware module recipe below | `trilink_app.h`, bridge composition, bounded tests |
| Add P2P text or files | Cross-layer feature plan below | Capability/wire contract, flow control, staged acceptance |
| Debug a disabled button or unknown result | Failure matrix below | Shared session state and exact transport logs |
| Produce a new release | Build and delivery sections below | Frozen inputs, complete package, separate hardware record |

Priority for the next implementation cycle: first accept the 0.16.0 physical USB foundation; next close a small bounded text path; only then extend it to streams, package alignment and real-time state. Do not start with a generic plugin VM, transparent IP mesh or OTA as a prerequisite for text.

## 2. Workspace and development environment

```text
TriLink/
  src/, tests/, tools/      Windows console source, tests and build tools
  docs/                     Shared architecture and development documentation
  firmware/                 ESP32-S3 source, tests and build tools
  releases/v0.16.0/          Selected packages and portable validation records
  artifacts/                Locally generated PC outputs, ignored by Git
```

Keep source, runtime, test evidence and firmware output separate. `artifacts/NodeConsole-0.16.0/` is the original local packaging output; public users extract the complete PC ZIP from `releases/v0.16.0/` into their own runtime directory. An isolated TEMP build stage is not the daily entry point. The PC ZIP does not contain firmware or user `module-data`.

| Toolchain | Current requirement / behavior |
| --- | --- |
| PC compiler | Roslyn located using `vswhere`, or explicit `-CscPath` on `build.ps1` |
| PC target | .NET Framework 4.8 reference assemblies; WinForms/STA |
| Firmware SDK | Fixed ESP-IDF 5.5.5; do not silently update IDF during a feature change |
| Firmware libraries | Locked esp_tinyusb 2.0.0, tinyusb 0.18.0~2 |
| Firmware host tests | Windows-target GCC, Python and Node.js |
| Build paths | Short isolated paths; firmware staging path must be ASCII on this toolchain |

Development prerequisites are not an instruction to install or replace SDKs automatically. Inspect available tools and script parameters first. Ordinary builds can require network access if locked IDF component dependencies are not cached; a dependency lock pins inputs but is not an offline guarantee.

Before editing:

1. Inspect the current branch/status and applicable project instructions; preserve unrelated work.
2. Read the source contract and its tests, not only historical README paragraphs.
3. Name the changed layer and the claimed result: local UI, fake protocol, PC loopback, real USB, wireless, or physical output.
4. State any contract, capacity or privilege change before implementing it.
5. Use a fresh test/build output path. Do not overwrite a running version or erase old evidence.

## 3. Decide which layer must change

| Change | Expected implementation surface | Additional requirement |
| --- | --- | --- |
| Labels, layout, explanation of disabled actions | View/shell | No new I/O owner; lifecycle and render regression |
| Local text formatter/calculator | One desktop plugin | No fabricated S3 capability |
| Another Room/RGB presentation | New view using existing shared service | Subscription cleanup, stale selection and queue tests |
| Queue, polling or uncertainty fix | `NodeSessionService` | Cross-page, pause, late callback and identity-change regression |
| CDC/NCM binding change | Serial adapters plus firmware USB if needed | No unknown-write replay; strict physical identity tests |
| New board action | Firmware module/bridge + capability + typed PC service/view | Permission, schema, budget and end-to-end receipt design |
| P2P text | Both ends of a new bounded application protocol | Receiver endpoint, credit, UTF-8 and consumption acknowledgement |
| Continuous file | Stream protocol and PC source/sink | Session-window dedup; not just a larger text buffer |
| New shared C# contract | Abstractions + providers + consumers + manifests | Full matching runtime package, not one replacement DLL |

A transport adapter should not decide Room policy. A view should not decide wireless retries. A manifest capability is a description, not code that creates the advertised function.

## 4. Follow one complete operation before changing it

### 4.1 Device recognition

```text
SerialPlugin starts one watcher
  -> enumerate native CDC and optional eligible NCM candidates
  -> physical binding checks and HELLO identity handshake
  -> publish one preferred device for a MAC (CDC first)
  -> view binds through INodeSessionService
  -> read capabilities and Room state
  -> render copied snapshot with available actions and reasons
```

The USB list and nearby wireless peers are different lists. A peer advertisement does not prove that the remote PC is running, that its USB data port is connected, or that it joined the Room.

### 4.2 Room approval

```text
view captures selected request + current snapshot/generation
  -> Submit(ApproveJoin intent)
  -> immediate submission status
  -> shared FIFO chooses intent
  -> revalidate node/boot/Room/revision/term/role/request expiry
  -> watcher executes through existing physical owner
  -> USB worker -> bridge -> Room state machine
  -> OFFER / CONFIRM / committed snapshot / member ACK
  -> later snapshots show the actual membership result
```

Do not show a joined member merely because the local approval command returned. Membership has a separate wireless confirmation lifecycle. An expired ticket should be rejected visibly, not replayed using a newly discovered request without user intent.

### 4.3 RGB

Enabling RGB changes permission; pulse submission is a different operation. The UI captures and confirms the local permission/context, queues one pulse, records the returned operation ID, and follows that ID. Route acceptance, application acceptance, driver completion and optical observation are different outcomes.

Never convert a timeout into an automatic second pulse, including after CDC/NCM failover. Unknown work may have executed. A future non-idempotent action needs equally explicit outcome semantics.

## 5. Recipe: add a local desktop plugin

### 5.1 Smallest useful current implementation

The following example follows the current public interfaces. It is illustrative source, not an installed or separately compiled plugin delivered with this guide.

```csharp
using System.Windows.Forms;
using TriLink.Plugin;

namespace Example.Status
{
    public sealed class StatusPlugin : ITriLinkPlugin
    {
        public void Configure(IPluginContext context)
        {
            var features = context.GetRequired<IModuleFeatureRegistry>();
            var lease = features.Register(
                new ModuleFeature {
                    Id = "example.status.view",
                    ModuleId = context.PluginId,
                    Title = "Status",
                    Description = "Local diagnostic view.",
                    Scope = ModuleFeatureScope.DesktopTool
                },
                () => new Label {
                    Dock = DockStyle.Fill,
                    Text = "Ready"
                });
            context.Defer(lease.Dispose);
        }

        public void Start() { }
        public void Stop() { }
    }
}
```

Corresponding source manifest:

```json
{
  "schemaVersion": 1,
  "id": "example.status",
  "displayName": "Status",
  "version": "1.0.0",
  "hostApi": "1.0",
  "enabled": true,
  "entryAssembly": "Example.Status.dll",
  "entryType": "Example.Status.StatusPlugin",
  "dependencies": [],
  "requiresServices": ["trilink.module-features"],
  "providesServices": [],
  "capabilities": ["local-diagnostics"]
}
```

Reference `TriLink.Plugin.Abstractions` and the necessary framework assemblies; use the existing TextTools project as a project-file model. `ITriLinkPlugin`, `IPluginContext` and `IModuleFeatureRegistry` are in `TriLink.Plugin`; node contracts are in `TriLink.Core`.

The source manifest omits a hash because no DLL has yet been built. An importable manifest must contain the actual 64-hex-character DLL `sha256`; never ship a placeholder. The built-in build script computes it after compilation. The host infers the service-provider dependency from `requiresServices`, even when `dependencies` is empty.

### 5.2 Integration options

For a new first-party plugin:

1. Add a project under `src/Plugins/`, source manifest and focused tests.
2. Add the plugin to the intended profile, not automatically to every profile.
3. Update the explicit plugin selection/build/output maps in `tools/build.ps1`. Directory discovery alone does not compile new first-party code.
4. Update solution/project references where used and the release-layout/package checks for the deliberately changed runtime closure.
5. Test dependency failures, feature registration/disposal, disabled-next-start state and safe recovery.

For an independently built optional plugin, use the existing Modules import workflow. It accepts a manifest plus one managed DLL, not a ZIP, script bundle, dependency directory or S3 image. Current limits are 64 KiB manifest and 16 MiB DLL. Embed small resources or make a separately designed packaging-contract change; do not silently add arbitrary sidecar loaders.

Import uses preview/fingerprint/hash checks and rechecks the copied package. It does not execute the new plugin during import. A new plugin starts disabled; activation and updates take effect on the next process start. X only hides the application, so use tray Exit for a real restart.

Packages and per-profile configuration live under `module-data/` beside the EXE. Do not remove that directory as a build cleanup step. SHA-256 establishes byte integrity, not publisher trust. An activated plugin is not sandboxed.

## 6. Recipe: add a real-device page without duplicating I/O

Declare `trilink.node-session.v1` and any other service actually used in `requiresServices`. Obtain the existing service in `Configure`; inject it into the view factory. Do not instantiate a second service in the view constructor.

Lifecycle pattern, expressed as pseudocode rather than a new API:

```text
when a verified attached device is selected:
  subscription = sessions.Bind(device.NodeId, device.PortName)
  subscribe to subscription.Changed
  subscription.SetVisible(true, requestedAutoStart)
  render subscription.Snapshot

when the user clicks an operation:
  capture a single current snapshot and selected record
  explain/collect any required explicit confirmation
  create the supported NodeOperationIntent using that snapshot
  result = subscription.Submit(intent)
  show the exact result; never translate Queued into Completed

on hide:
  subscription.SetVisible(false, false)

on disposal or target replacement:
  detach Changed
  dispose subscription
```

Use these APIs on the owning desktop synchronization context. Calling `Task.Run` around `Submit` is not a performance optimization; it violates the service's thread-affinity contract. Actual I/O already uses its adapter path. Capture view values before `await`, and check disposal/binding generation after continuations before touching controls.

Current operations are only:

```text
CreateRoom, LeaveRoom, RetryJoin, JoinRoom, AcceptInvitation,
ApproveJoin, RejectJoin, KickMember, InviteMember,
EnableRgb, DisableRgb, LocalRgbPulse, MemberRgbPulse
```

`Generation`, `Basis`, `Record`, `Target` and `LocalPermissionConfirmed` are context, not arbitrary flags to set until a button works. Read `HardwareRoomView` and the service preflight for the particular action. A new operation needs provider implementation and tests, not merely a new enum value.

Required page tests:

- Two views share one physical binding, one foreground queue and coalesced refresh.
- Closing one view preserves another view's deduplicated intent and already submitted result tracking.
- Manual pause, fifth-failure pause and device-change latch survive hide/show/reopen and late callbacks.
- A physical node/boot/Room change between click and dequeue rejects stale work.
- Queue-full/duplicate/unsupported states have readable feedback rather than disabling the entire UI.
- Async disposal cannot produce a null-reference dialog; actual unexpected exceptions retain diagnostic evidence rather than being swallowed.

## 7. Recipe: add a compiled firmware module

### 7.1 Current registry contract

The real header is [trilink_app.h](../firmware/components/trilink_node/include/trilink_app.h):

```c
typedef struct {
    uint8_t capability_bit, marker, version;
    uint16_t wire_size;
    void *context;
    bool (*receive)(void *context, const trilink_route_view_t *message);
    void (*tick)(void *context, uint64_t now);
} trilink_app_module_t;
```

Composition uses `trilink_app_init`, `trilink_app_register`, `trilink_app_receive`, `trilink_app_tick` and `trilink_app_capabilities`. Registration is performed before the node task starts. Capacity is four slots; a module needs a unique nonzero one-bit capability, unique nonzero marker, nonzero version and exact wire size between 2 and 132 B.

Current RGB registration in the bridge is a useful reference: marker `0xA6`, version `1`, size `19`, `rgb_module_receive`, `rgb_module_tick`. These callbacks are bridge-private implementation examples, not public APIs for other modules to call.

### 7.2 Implementation sequence

1. Write a compact contract: purpose, sender/receiver, fixed envelope, wire version, authority, timeout and terminal outcomes.
2. Reserve marker/capability explicitly in both ends of the protocol. Do not reuse RGB's bit or invent an untracked production ID in a sample.
3. Implement a pure codec and state reducer with bounded buffers. Encode integers explicitly and validate lengths before accessing fields.
4. Add long-lived static module context. Keep receive/tick bounded, nonblocking and owned by the node task.
5. Copy only bytes that must survive callback return; never retain the RX view pointer.
6. Design validated bridge adapters for current Room/boot/permission checks and sending, if needed. `s_room` and `s_route` are private; the current registry does not expose a complete module service API.
7. Add sources to the appropriate component CMake file; register in `trilink_bridge_init`, treating registration failure as initialization failure.
8. If PC control is needed, implement command encoding/parser, capabilities, typed PC session behavior and UI together.
9. Add malformed-input, stale identity, duplicate, timeout, capacity and autonomous-tick tests.
10. Run host tests, fresh firmware build and map/stack/resource gates. Expose the capability only when the implementation is actually usable.

Variable-length text needs special care: dispatch currently enforces exact `wire_size`. Choose a fixed bounded envelope with an explicit valid length, or deliberately version/change the registry contract with compatibility tests. Do not merely loosen the length check globally.

Adding a C module requires a new firmware image and separately authorized flashing. Copying a PC DLL or data manifest to another S3 cannot install missing native code.

### 7.3 Forbidden shortcuts

- No direct raw-radio send from a new UI/plugin to bypass Room authority.
- No `malloc(total_file_length)` or unbounded received-message collection.
- No per-module FreeRTOS task just to create an architectural layer.
- No automatic GPIO permission from remote Room membership.
- No shortened global dedup lifetime solely to increase file throughput.
- No automatic retransmission of an uncertain non-idempotent action with a new ID.
- No disabled capacity tests to make a larger build pass.

## 8. Recipe: close the next P2P text feature

This section is a development proposal, not an implemented API. The current Text Tools plugin and dormant content/file codecs do not complete it.

### 8.1 Write the contract before the UI

Specify at least:

| Contract field | Reason |
| --- | --- |
| Source/destination node and boot | Reject stale state after replacement or reset |
| Room ID/incarnation and appropriate membership basis | Keep an old transfer out of a different Room |
| Application ID/version and session ID | Separate services and repeated conversations |
| Total byte limit, sequence/offset and exact encoded length | Bound allocation and detect gaps/overlap |
| Receiver credit and endpoint lifetime | Stop when the PC is absent or its bounded sink is full |
| Result/consumption acknowledgement | Distinguish node receipt from PC consumption |
| Cancel/timeout/unknown behavior | Avoid claiming a submitted operation was undone |

Do not insert raw UTF-8 into the existing space-tokenized ASCII command line. Define bounded binary/enveloped data and cross-language encoding tests. A 512-byte UTF-8 limit is not 512 characters. Truncated multi-byte sequences, invalid UTF-8 and embedded control bytes need explicit policy.

### 8.2 First useful closed loop

```text
PC A bounded source -> S3 A bounded session
    -> routed fragments -> S3 B bounded session
    -> PC B receive endpoint -> explicit PC_CONSUMED receipt
    -> displayed result at PC A
```

Recommended first slice: one TX and one RX session, no compression, no persistent S3 content store, explicit receiver availability, one small message size limit. Define local loopback separately so one board remains useful without pretending that it proves wireless transfer.

The receiving PC may retain conversation text as an application policy. That is distinct from a board-side storage service. Hiding the PC UI to the tray can keep the endpoint alive; terminating the PC makes that endpoint unavailable. S3 discovery and supported autonomous actions can continue without it.

### 8.3 Suggested work packages and gates

| Work package | Deliverable | Gate before the next package |
| --- | --- | --- |
| T1: protocol | C/C# golden fixtures and limits | Both directions, malformed/oversized input and version rejection |
| T2: board session | Fixed TX/RX state, credit, expiry and receipts | Duplicate/loss/reorder/full-buffer/reset with no unbounded growth |
| T3: PC shared service | Source/sink and typed submit/query contract | No view-owned COM or transfer lifetime; queue/pause isolation |
| T4: UI | Send/receive/status/cancel with explicit outcome labels | One-board loopback fake and device scopes visibly different |
| T5: hardware | Same compatible firmware on two boards | Real message bytes, CRC/hash, disconnect and long-run resource record |

Only after this closure should file transfer reuse the source/sink and flow-control concepts. A long transfer should occupy one transfer session, not enqueue every chunk in the eight-item UI action FIFO.

Continuous streams need a separate bounded session/sequence window because the present destination seen table fills after 32 distinct reliable messages within its retention window. Intermediate forwarding dedup must also be addressed. Existing Room/RGB controls retain their stronger replay rules.

### 8.4 Package alignment and operating modes

Future package alignment is: describe capability -> compare compatible versions/data hashes -> request available source -> bounded transfer -> verify -> explicitly apply -> report result. It is not silent code installation. A node can copy only source bytes it still holds and only data its peer can interpret.

`normal`, `bulk`, `realtime` should be per-node scheduling policies, not a second Room system. Preserve control/ACK/cancel opportunities and bounded fairness. Reliable events such as joining or a confirmed game action differ from replaceable state such as a position sample. Test latest-wins and expiry before calling any mode real-time; the existing radio retry path provides no hard real-time guarantee.

## 9. Build and test without touching hardware

Commands in each subsection run from that project's root. Paths in angle brackets are placeholders and must be replaced with verified actual paths. A software-only build is not a flashing command.

### 9.1 Desktop full isolated build

```powershell
.\tools\build-isolated.ps1 -Configuration Both
```

This freezes `src`, `tests`, `tools` and `VERSION` into a short `tl0160-...` stage and prints `ISOLATED_STAGE=...`. Keep that exact value. `Both` runs Release and Debug; the packager requires both. Do not reuse a stage from a different edit or older test run.

`build.ps1` locates Roslyn and .NET Framework 4.8 references. For an existing plugin-only edit after a matching full build, the current direct command is:

```powershell
.\tools\build.ps1 -Configuration Debug -PluginId trilink.hardware-room
```

That writes the ordinary `artifacts/Debug` tree and is not full release evidence. It does not rebuild changed shared contracts. If output is in use, let the user exit that instance normally or choose a fresh isolated build; do not kill a process to make compilation succeed.

If tool discovery is wrong, direct `build.ps1` accepts `-CscPath` and `-FrameworkPath`. `build-isolated.ps1` itself accepts only `ProjectRoot` and `Configuration`; do not pass unsupported switches or silently substitute an older compiler.

### 9.2 Firmware focused tests and full build

```powershell
.\tools\test_host.ps1 -Compiler '<Windows-target GCC executable>'
node .\tests\host\test_web_ui.js
```

Host GCC must target Windows (`mingw`/`windows`), not the ESP32 Xtensa target. These tests use fakes for peripheral code. Probe unit tests are not the same as running a live probe against COM ports.

Full fresh validation with current machine defaults:

```powershell
.\tools\build_local.ps1
```

Portable parameter template (replace the placeholders with verified local paths):

```powershell
.\tools\build_local.ps1 `
  -IdfProfile '<ESP_IDF_5_5_5_PROFILE>' `
  -HostCompiler '<WINDOWS_HOST_GCC_EXE>' `
  -HostPython '<ESP_IDF_PYTHON_EXE>' `
  -HostNode 'node.exe'
```

The placeholders are not executable paths; replace them for the current machine. The build script's historical defaults may not exist on a clean checkout's host. Full validation runs C host tests, the legacy USB contract, Python probe unit tests, embedded JS tests, fresh IDF build, configuration/capacity checks and before/after source hash checks. It does not flash or open physical ports.

`-ReuseStage` is a diagnostic option with strict TEMP-path constraints and produces `FreshBuild=false`; do not relabel it as fresh release evidence.

### 9.3 Cross-language contract test

Use the `node-caps-fixture.txt` produced by the matching firmware host-test/build run. Test it with the freshly built serial test executable:

```powershell
& '<test-bin>\TriLink.SerialLifecycle.Tests.exe' `
  --caps-fixture '<firmware-artifact>\node-caps-fixture.txt' `
  --caps-version '0.16.0'
```

The separate `tests/TriLink.SerialLifecycle.Tests/run.ps1` still has a historical default CAPS version of `0.15.0` and compiler path. If using that runner, explicitly pass the matching version and compiler. A fixture copied from a different firmware build is not cross-version validation of the intended pair.

### 9.4 Test map

| Behavior | Current starting test source |
| --- | --- |
| Plugin dependencies/import/runtime lifecycle | `tests/TriLink.Core.Tests`, `tests/TriLink.Modules.Tests` |
| CDC/NCM identity, enumeration, parser, deadlines | `tests/TriLink.SerialLifecycle.Tests/Program.cs`, `NcmTests.cs` |
| Queue, pause, cross-page binding, Room/RGB UI | `tests/TriLink.HardwareRoom.Tests/Program.cs` |
| Main window/tray integration and render | `tests/TriLink.Desktop.Tests/Program.cs`, `tools/test-built-in-smoke.ps1` |
| Existing PC game | `tests/TriLink.Thunder.Tests/` |
| Firmware registry/bridge/Room/route | `tests/host/test_trilink_app.c`, `test_trilink_bridge.c`, `test_trilink_room.c`, `test_trilink_route.c` |
| RGB worker/driver boundaries | `tests/host/test_trilink_room_rgb.c`, `test_trilink_rgb_worker.c`, `test_trilink_rgb_rmt.c` |
| USB transaction lifetime | `tests/host/test_trilink_usb_mux.c` |
| Web policy/mailbox/actual embedded JS | `tests/host/test_web_policy.c`, `test_web_ui.js` |

The last four rows use firmware-relative paths; the first five use client-relative paths. Keep new tests next to the owning boundary. An internal pure function test and an end-to-end fake test answer different questions and may both be needed.

## 10. Time, queue and failure testing

Prefer explicit logical clocks/fake services for exhaustive long-delay behavior. Use a separate isolated process for any OpenSpeedy experiment, record its exact executable hash/PID, calibrate each relevant time source, and never change system time as a substitute.

The old 20x results concern an earlier isolated client harness. They do not establish 0.16.0 correctness, and accelerating the PC does not accelerate the S3 uptime clock, FreeRTOS, radio or a remote PC. Mixed real-device/accelerated-PC testing can create artificial deadline failures rather than a valid long-duration simulation.

| Boundary to test | Cases | Required observation |
| --- | --- | --- |
| Five-failure latch | Failure 4, 5, and delayed old success | Fifth failure stops scheduling; late success cannot resume |
| FIFO capacity | Eight pending, ninth submit, one executing | Explicit full response, no hidden overflow |
| Intent expiry | Just before/at/after 6 s or 15 s | Expired operations never submit |
| Page disposal | Close before/after dequeue; shared duplicate | Only owned unsubmitted work is removed |
| Device replacement | Same COM, different MAC/boot/PnP; CDC/NCM swap | Old queued work cannot reach new identity |
| HTTP cutoff | Just before/at/after 3500 ms | Final receive cannot escape the total per-request deadline |
| Firmware mux | Reply before/after 2000 ms; never replies | Unknown retains owner; late reply never reaches next command |
| Web intent | Queued expiry, disconnected client, ticket rollover | No stale submission; bounded/fail-closed state |
| Room transition | 1-person draft, 2-to-1, 3-to-2, stale ACK | Draft allowed, formed pair dissolves, orderly succession only |
| RGB outcome | Duplicate, delayed accepted, terminal, unknown | No duplicate physical action or terminal regression |

Log virtual elapsed time, an independent real elapsed measure, queue depth, generation, submission count and final pause state. Longer wall time alone is not coverage; assertions must cross the intended boundaries. Resource snapshots are trends, not proof of zero leaks.

## 11. Release and update workflow

### 11.1 Freeze and package

1. Finish source and contract changes, including test changes, before the final build.
2. Run a fresh firmware build if firmware changed; retain its `verification.json`, input manifest, logs, map/stack evidence and exact images.
3. Run a full isolated desktop `Both` build and the matching cross-language fixture.
4. Inspect the resulting UI renders; record their offscreen/fake boundary.
5. Package that exact stage only after all required checks pass.

Client-root packaging command:

```powershell
.\tools\package-local.ps1 `
  -Stage '<actual ISOLATED_STAGE>' `
  -FirmwareReceipt '<matching firmware verification.json>'
```

The current default package version is `0.16.0`. This release already exists, so rerunning the command against its occupied targets intentionally fails. Do not delete the existing delivery to force it through. For a new release, deliberately update product/file/plugin/protocol versions as appropriate and review version-specific build/package/evidence expectations before building. Passing a different `-Version` alone does not update compiled code.

The package script validates the current source against the frozen stage, including added/deleted inputs, and validates both configurations and runtime hashes. It references a firmware receipt; it does not rebuild firmware. Current receipt validation expects software-only firmware flags, so real-hardware evidence belongs in a separate record rather than overwriting the original build receipt.

### 11.2 Delivery contents and rollback

The 0.16.0 portable closure contains 22 files: EXE, shared Abstractions and PluginHost DLLs, nine plugin DLL/manifest pairs and a profile. Local launch shortcuts are additional and not portable. Firmware, test executables, screenshots, temporary stages and user `module-data` do not belong in the PC ZIP.

Upgrade procedure:

1. Ask the user to exit the old program using tray Exit, not X.
2. Keep the previous complete runtime and package.
3. Extract/start a new complete runtime directory; do not mix old and new shared DLLs.
4. Treat module configuration/package migration as a separate explicit operation; do not automatically copy untrusted overlays into the new runtime.
5. Verify version/profile and expected device control transport before any mutation.

PC rollback means returning to a compatible complete runtime. It does not roll back S3 firmware or recover an already executed action. Firmware rollback requires compatible image/layout and separately authorized flashing. Firmware source and the selected image package are included in the current public distribution, but remain separate from the PC portable ZIP. This publication policy does not authorize automatic flashing or uploading arbitrary backups/logs.

### 11.3 Documentation-only edits

New guides and README changes do not alter the current `src/tests/tools/VERSION` desktop build-input set or the firmware's compiled inputs. A documentation correction does not require inventing a new binary version or retagging old test results. If executable source, tests or tools also change, use the affected verification and release gates again.

## 12. Physical acceptance plan

These are future authorized bench steps, not results of writing this guide. Identify a board by actual MAC/interface/flash information, not only by an old label such as board 1 or a remembered COM number.

| Phase | Scope | Pass evidence |
| --- | --- | --- |
| H0 | Confirm board, recovery port, image hash and power | Correct target; recovery and backup plan; no unintended erase/eFuse/partition change |
| H1 | Authorized new-image flashing and boot | Matching written image, reset/boot log, expected version, no crash loop |
| H2 | One-board Windows 10 native CDC | Enumerates, HELLO/CAPS/Room read works, DTR-false and unplug/replug behavior |
| H3 | Suitable Windows 11 CDC+NCM | Composite interfaces, DHCP, fixed local endpoint; no default-route/DNS pollution |
| H4 | Browser and desktop contention | Bounded BUSY/timeout, no crossed replies or replay, identity change rejection |
| H5 | Two-board discovery and admission | Both sides confirm matching Room/incarnation/revision/term/roster |
| H6 | Three-board management | Non-leader invitation, leader decision, orderly 3-to-2 succession and pair dissolution |
| H7 | Separately permitted RGB | Target-local opt-in, exactly one pulse request, driver receipt; optical observation separately recorded |
| H8 | Long-run resources | Heap minima/largest block, task stack high-water, queue drops/timeouts, resets and repeated USB reconnects |

Changing USB firmware may require physically moving the cable to the CH interface for recovery/flashing, then back to native USB for control. Do not reinterpret the CH port as a supported control port. A failure in H2/H3 should not trigger arbitrary driver installation or route rewriting.

## 13. Troubleshooting matrix

| Symptom | First read-only check | Likely boundary / safe next step |
| --- | --- | --- |
| Process exists, no visible UI | Exact process path, profile, whether an old tray instance owns the mutex | X hides; second launch may activate an old instance. Verify interactive desktop separately |
| No controllable device | Physical port and cable; native USB enumeration; exact version | CH UART is not the data port. Do not synthesize a simulated device as recovery |
| USB polling stopped | Discovery limiter state and last error | Explicit resume after diagnosing cause; do not restart from a page timer |
| Buttons disabled | Binding, snapshot validity, pause state, permission, selection, leader role | Show the reason; a global busy flag should not mask queued-operation support |
| Nearby peer but no Room | Peer capability, actual V3 Room records, channel/version | Discovery is not membership; old V1 Room placeholders are not V3 state |
| Waiting for approval indefinitely | Ticket expiry, OFFER/CONFIRM state, leader synchronization | Do not spam JOIN; use supported retry only for the actual unresolved state |
| NCM missing on Windows 10 | CDC identification | CDC is the intended compatibility path; NCM is not required |
| NCM IP exists but refused | Exact USB interface, source IP/mask, best route and ambiguity | Fail-closed binding or address collision; use CDC, do not force traffic to arbitrary same-subnet host |
| Web session/403 error | Token freshness and accepted host/origin | Reconnect/read a fresh session; never paste tokens into public logs |
| BUSY or 504/unknown | Mux owner and original operation context | Refresh/query supported state; do not resubmit a possibly executed mutation |
| CDC HELLO works but HTTP commands stall | An unresolved bridge owner | HTTP exchange is behind mux; static page availability is not healthy bridge proof |
| RGB enabled but no pulse | Whether pulse was submitted, correct target and permission, result ID | Permission is not action. Driver-complete is not optical proof |
| Imported plugin does not appear | Hash, profile, disabled-next-start, dependency result and actual restart | Import is not hot activation; inspect host diagnostic without loading untrusted replacements |
| Build says source changed | Compare frozen input manifest with current files | Retain failed evidence, start a fresh build after edits settle |
| Package target exists | Exact runtime/ZIP/evidence version | Preserve the old release; prepare a deliberate new version, do not delete broadly |

Capture the full exception type and stack, operation and transport, product/plugin/firmware versions, node/boot/Room context, and whether the evidence used fakes or hardware. Exclude session tokens and unrelated machine/user data. Do not hide exceptions just to make a test appear stable.

## 14. Definition of done and review checklist

For each change, record:

- The user-visible behavior and exact supported minimum setup (one board, two boards, or PC-only).
- Layer ownership, public contract changes and old-version behavior.
- Bounds on bytes, queues, sessions, subscriptions, retries and task/stack costs.
- Authority at actual execution, not only at UI preflight.
- Failure semantics: rejected before submission, failed, cancelled with evidence, or unknown after submission.
- Relevant tests with source/build identity and reproducible commands.
- Capacity delta for firmware; full runtime closure for shared PC changes.
- Clearly separate build/fake/offscreen/loopback/desktop/USB/RF/driver/optical evidence.
- A compatible update/rollback path and any required hardware action still awaiting authorization.

Review questions:

1. Is there still one physical I/O owner and one node-state owner?
2. Can a delayed callback from an old device or Room alter the current UI or cause a write?
3. Can a full queue, expired request or failed bridge grow memory or revive polling?
4. Is a received byte sequence being mistaken for execution or end-user consumption?
5. Is any future API, dormant codec, advertised mode or historical result being presented as current capability?
6. Does a new plugin really need firmware work, and is that dependency visible to the user?

## 15. Course presentation and engineering explanation

The project demonstrates useful networking principles without pretending to implement a full Internet stack over ESP-NOW:

| Principle | Concrete TriLink mechanism | Honest limitation |
| --- | --- | --- |
| Link framing / addressing | MAC identity, bounded frames and CRC | CRC is not authentication |
| Neighbor discovery | Periodic HELLO, heartbeat and expiry | Fixed channel; discovery is not admission |
| Routing | Bounded link state, hop selection and loop prevention | Six-member design; no arbitrary-scale mesh claim |
| Reliability | ACK, retries, dedup and backpressure | ACK is not application completion |
| Distributed state | Room replicas, terms/revisions, orderly succession | No durable consensus or automatic partition recovery |
| Application protocol | RGB permission, request identity and result query | Physical behavior needs separate observation |
| Local network adaptation | USB NCM, DHCP, HTTP policy | Local management link only; not transparent wireless Ethernet |
| Concurrency/control | Single-owner queues, fairness, deadlines and pause latches | No unlimited throughput or hard real-time guarantee |

For a demonstration, show one successful path and one deliberately injected failure with the corresponding state transitions. State the exact firmware/client pair and hardware setup. This is more informative than claiming every future feature is already provided by the plugin architecture.

## 16. Source and evidence index

- [Architecture and current bounds](ARCHITECTURE_0160.md)
- [Release record and baseline counts](RELEASE_0160.md)
- [Directory layout](DIRECTORY_LAYOUT.md)
- [Shared session contracts](../src/TriLink.Plugin.Abstractions/NodeSessionContracts.cs)
- [Plugin contracts](../src/TriLink.Plugin.Abstractions/PluginContracts.cs)
- [Module contracts](../src/TriLink.Plugin.Abstractions/ModuleContracts.cs)
- [Reference local plugin](../src/Plugins/TriLink.Plugin.TextTools/TextToolsPlugin.cs)
- [Shared coordinator](../src/Plugins/TriLink.Plugin.HardwareRoom/NodeSessionService.cs)
- [Firmware dispatcher contract](../firmware/components/trilink_node/include/trilink_app.h)
- [Firmware USB boundary](../firmware/components/trilink_usb/README.md)
- [Desktop build/package tools](../tools/)
- [Firmware build/test tools](../firmware/tools/)
- [Public validation summary (derived)](../releases/v0.16.0/validation-summary.json)
- [Public firmware validation summary (derived)](../releases/v0.16.0/validation-summary.json)

Firmware source links resolve within `firmware/` in this public checkout. Current validation links point to the derived public summary under `releases/v0.16.0/`; original machine-bound receipts and excluded historical artifacts remain local. A missing historical evidence path is not a reason to publish additional logs, device backups or TEMP files.
