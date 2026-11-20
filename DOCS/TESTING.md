<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC-BY-SA-4.0
-->
# AetherAprs Testing Strategy & SIL Plan

Companion to `ROADMAP.md`. This document defines how we raise test confidence at the
**platform/transport seam** — the layer where the app talks to the outside world over
TCP and Bluetooth — and how we run it in CI.

> Plan only. No code changes are made in this document; it is a spec another agent can
> implement.

---

## 1. Problem statement

The pure C# protocol logic (KISS framing, AX.25, APRS info-field parsing/serialization)
is already well covered. The risk is concentrated at the **I/O boundary**:

| Layer | Current coverage | Gap |
|---|---|---|
| `KissSerializer` / `KissFrame` / `KissCommandType` | Exhaustive (fuzz, escape round-trips) | — |
| `KissModem` (framing over `Stream`) | Strong (lifecycle, concurrency, split-reads, oversized, remote-close) | — |
| `AprsRfModem` (AX.25 over KISS) | RX/TX over in-memory streams | not driven over a real socket |
| `AprsIsModem` (APRS-IS TCP) | `ParseIsLine` thorough; one login/TX loopback test | no full receive loop / reconnect / oversized-line / concurrent-send E2E; not wired through `PortService` |
| `TcpKissStreamConnector` | one isolated loopback round-trip | never combined with `KissModem` → `PortService` → storage → query |
| `PacketStorageService` / `PacketQueryService` | real SQLite | — |
| `PortService` receive pipeline | E2E but with a **fake** `IKissStreamFactory` + **mock** storage | real connector + real DB never exercised in one test |
| **Bluetooth Classic (SPP) & BLE (NUS)** | **zero** tests | Android-only, no abstraction seam |
| CI | none | no automated gate |

The fix is a **Software-in-the-Loop (SIL)** harness: run the *real* production
transport/modems/services against a *scripted in-process peer* over loopback TCP, and add
an abstraction seam so the Bluetooth state machine becomes desktop-testable with fakes.

---

## 2. Architecture decisions (recorded so the plan is unambiguous)

- **AD-T1 — SIL is in-process loopback, not subprocess.** No external binaries
  (direwolf/aprs-serve/emulators). Two scriptable server fixtures built on `TcpListener`:
  `VirtualKissTncServer` and `VirtualAprsIsServer`. Deterministic, fast, CI-friendly, and
  run on any host that runs the test project. (A reference-implementation HIL layer is
  explicitly **out of scope** and noted in the backlog — see §6.)

- **AD-T2 — SIL drives the real connectors, not fakes.** The entire point is to close
  the seam that `PortServiceReceivePipelineTests` currently short-circuits. In SIL tests,
  `TcpKissStreamConnector`, `KissModem`, `AprsRfModem`, `AprsIsModem`, `PortService`,
  `PacketStorageService`, `PacketQueryService` are all real; only the *far end* is a
  scripted TCP peer. Unit tests continue to use fakes for speed and precision.

- **AD-T3 — Test-only seams, minimal production churn.** Where production code blocks
  testing (e.g. `AprsIsModem`'s hard-coded 5-second reconnect delay in `ReadLoopAsync`),
  add an `internal` constructor overload / property (visible via `InternalsVisibleTo`) to
  inject the delay or a clock — do **not** redesign the class. Keep the diff surgical.

- **AD-T4 — Bluetooth via abstraction + fakes, not instrumentation.** Real RFCOMM/GATT
  cannot run on the desktop test host or in CI. Extract the Android Bluetooth API behind
  platform-neutral interfaces in core; the Android impl becomes a thin adapter; the
  connection/stream **state machine** is unit-tested on the desktop with fakes. Physical
  RF interop stays a manual test (documented). No Android instrumentation project now.

- **AD-T5 — One test project, folder + `[Trait]` for SIL.** SIL tests live in
  `AetherAprs.Tests/Integration/` (namespace `AetherAprs.Tests.Integration`), tagged
  `[Trait("Category", "Integration")]`. They remain in the same `dotnet test` run (they
  are fast loopback, not slow I/O). Splitting into a separate project is deferred until
  they demonstrably slow the suite.

- **AD-T6 — CI builds core + tests, not the Android head.** `AetherAprs.slnx` includes
  `AetherAprs.Android` (`net10.0-android`), which requires the .NET Android workload that
  GitHub-hosted runners do not pre-install. CI therefore builds and tests the
  `net10.0` core + test project (`dotnet build AetherAprs/AetherAprs.csproj` +
  `dotnet test AetherAprs.Tests/AetherAprs.Tests.csproj`), which transitively exercises
  everything except the Android-only transport heads. Installing the Android workload to
  build the full solution is a follow-up, not a prerequisite.

---

## 3. Test pyramid target

```
        ┌───────────────────────────────┐
        │  Manual / field (real radio)  │  ← documented, out of CI
        ├───────────────────────────────┤
        │  SIL (loopback TCP E2E)       │  ← NEW (this plan)
        ├───────────────────────────────┤
        │  Service / ViewModel (NSub)   │  ← mostly present
        ├───────────────────────────────┤
        │  Unit + fuzz (pure logic)     │  ← strong
        └───────────────────────────────┘
```

Goals:

1. Every protocol transform has a **round-trip invariant** test
   (`serialize(parse(x)) == x` within spec) — extended in `ROADMAP.md` Phase 1.
2. Every transport seam has a **SIL test** (real socket, scripted peer).
3. Bluetooth connection/stream logic has **fake-driven unit tests**.
4. CI runs the whole suite on **every push and PR** so regressions gate the merge.

---

## Phase T0 — CI baseline & test taxonomy

**Objective:** an automated gate and a place for SIL tests to live.

**Dependencies:** none.

**Work items:**

1. Add `.github/workflows/ci.yml`:
   - Trigger: `push` + `pull_request`.
   - Matrix: `ubuntu-latest` and `windows-latest` (the repo is Windows-first; Linux
     catches path/encoding/line-ending bugs).
   - Steps: checkout → setup .NET 10 SDK → `dotnet build AetherAprs/AetherAprs.csproj`
     → `dotnet test AetherAprs.Tests/AetherAprs.Tests.csproj` (no `--no-build` on CI).
   - Do **not** build `AetherAprs.slnx` initially (Android workload) — per AD-T6.

2. Create `AetherAprs.Tests/Integration/` folder + `AetherAprs.Tests.Integration`
   namespace convention; document the `[Trait("Category", "Integration")]` convention in
   this file (§5 cross-cutting).

**Acceptance criteria:**
- A push opens a CI run; core builds and the full test suite is green on both OSes.
- A deliberately failing test fails the check.

**Tests:** n/a (infrastructure).

---

## Phase T1 — Loopback SIL harness (the centerpiece)

**Objective:** exercise the real TCP transport + modem + port + storage + query stack in
both directions, over a real socket.

**Dependencies:** Phase T0 (CI + folder).

**New files:**

```
AetherAprs.Tests/Integration/Harness/VirtualKissTncServer.cs
AetherAprs.Tests/Integration/Harness/VirtualAprsIsServer.cs
AetherAprs.Tests/Integration/TcpKissEndToEndTests.cs
AetherAprs.Tests/Integration/AprsIsEndToEndTests.cs
```

### T1.1 — `VirtualKissTncServer` (scripted KISS peer)

An `IAsyncDisposable` that:
- Binds a `TcpListener` on loopback port 0 and exposes `Port`.
- `AcceptAsync()` accepts one client.
- `SendFrameAsync(KissFrame)` encodes via `KissSerializer.EncodeFrame` and writes it,
  optionally in **arbitrary chunks** / with injected `Task.Delay` to simulate
  fragmentation and slow delivery.
- `ReadFrameAsync(timeout)` reads/parses one complete KISS frame from the client and
  records it; `ReceivedFrames` accumulates them.
- `CloseAsync()` closes the socket (simulating a dropped TNC).
- Deterministic, no sleeps in the happy path (use `WaitAsync(timeout)` on the read side).

### T1.2 — `VirtualAprsIsServer` (scripted APRS-IS peer)

An `IAsyncDisposable` that:
- Binds a `TcpListener`, accepts one client, reads the login line, and asserts it.
- Optionally replies `# logresp <callsign> verified, server TEST` (or a rejection line).
- `SendLineAsync(string)` writes a raw APRS-IS packet line.
- `ReadLineAsync(timeout)` reads the next client line; `ReceivedLines` accumulates them.
- `CloseAsync()` drops the connection.

### T1.3 — `TcpKissEndToEndTests` (RX and TX)

All tests construct the **real** `PortService` with a real `KissStreamFactory([new
TcpKissStreamConnector()])`, a real `PacketStorageService` over `TempAppDatabase`, and a
real `PacketQueryService`; the KISS port's `TcpKissTransportSettings` points at the
`VirtualKissTncServer`'s ephemeral port.

1. **RX → DB → query (full receive path).** Server sends a position KISS frame; assert
   `PortService.PacketReceived` fires with a `PositionPacket`, a `PacketRecord` is
   persisted with the right `SourceBase`/`PacketType`/position, and
   `PacketQueryService.GetMostRecentPacketsAsync()` returns that station. This is the
   definitive "KISS works end-to-end" regression test.
2. **TX (full transmit path).** `PortService.SendPacketAsync(portId, positionPacket)` →
   server `ReadFrameAsync` → `KissSerializer.DecodeFrame` → `Ax25Parser.ParseFrame` →
   assert the decoded `PositionPacket` matches (lat/lon, source, symbol).
3. **TX→RX loopback through two ports.** Two ports, each on its own `VirtualKissTncServer`
   on the same `PortService`; send on port A, assert port B's peer received the frame
   (and vice-versa). Exercises the digipeat/gate path over real sockets.
4. **Fragment / slow-delivery RX.** Server sends one frame split across multiple `Write`s
   and/or with delays; the modem still decodes exactly one frame (mirrors the existing
   unit-level split-read tests, but over a real socket).
5. **Escape round-trip over the wire.** Send a frame whose AX.25 payload contains `0xC0`
   and `0xDB` bytes; assert the peer decodes the identical bytes (proves escaping survives
   the real transport).
6. **Dropped connection → clean stop.** Server `CloseAsync()` mid-stream; assert the port
   stops without a spurious `ReceiveError` and `StopAllPortsAsync` completes.

### T1.4 — `AprsIsEndToEndTests`

Same real-stack construction with a `VirtualAprsIsServer`:

1. **Login + RX.** Server verifies the login line, then sends a position line; assert
   `PortService.PacketReceived` + DB + `PacketQueryService` see it.
2. **TX (q-constructor).** `PortService.SendPacketAsync` → server `ReadLineAsync`; assert
   the line is well-formed. **Note:** the current code emits `TCPIP*` (see `AD-6` in
   `ROADMAP.md`); the test asserts *current* behavior and is updated in `ROADMAP.md`
   Phase 8 when the iGate q-constructor is corrected.
3. **Login rejection.** Server replies `# logresp <callsign> invalid`; assert the port
   surfaces a `ReceiveError` and does not treat the connection as ready.
4. **Reconnect.** Server drops the connection after a packet; the modem reconnects and
   resumes RX (inject the reconnect delay per AD-T3 so the test doesn't sleep 5 s).
5. **Oversized line.** Server sends a line longer than `MaxLineLength`; assert the modem
   discards it, keeps the connection, and processes the next valid line.
6. **Concurrent send serialization.** Two concurrent `SendPacketAsync` calls; assert the
   server receives two complete, non-interleaved lines.

**Acceptance criteria:**
- The six KISS + six APRS-IS scenarios above pass and run in the same `dotnet test` pass
  without flakiness (no unbounded sleeps; all waits use timeouts).
- `dotnet test` is green on both CI OSes.

---

## Phase T2 — Bluetooth abstraction + fakes

**Objective:** make the Android-only Bluetooth connection/stream logic testable on the
desktop, closing the largest coverage hole.

**Dependencies:** none (parallel to T1).

**Current state (verified):**
- `AetherAprs.Android/Services/Transports/BluetoothSocketDuplexStream.cs` wraps
  `Android.Bluetooth.BluetoothSocket`.
- `AetherAprs.Android/Services/Transports/BluetoothLeGattDuplexStream.cs` contains the
  non-trivial state machine: GATT callback ordering (`OnConnectionStateChange` →
  `DiscoverServices` → `OnServicesDiscovered` → CCCD write → `OnDescriptorWrite`),
  the RX `Channel<byte>` bridge, and the write gate with the "abandoned on cancel"
  recovery path.
- `AetherAprs.Android/Services/Bluetooth/BluetoothPermissionHelper.cs` is a static facade
  touching `Application.Context` and `MainActivity.Instance` directly.

**Work items:**

1. **Extract platform-neutral interfaces in core** under `Services/Platform/` (new files,
   namespaces per `CODEBASE_STRUCTURE.md`):
   - `IBluetoothSocket` (open/connect/close/is-connected + `Stream InputStream` /
     `OutputStream`), `IBluetoothDevice` (`CreateRfcommSocket`), `IBluetoothAdapter`
     (`GetRemoteDevice`, `IsEnabled`, `CancelDiscovery`).
   - `IBluetoothGatt` (`Connect`, `DiscoverServices`, `GetService`, `GetCharacteristic`,
     `GetDescriptor`, `SetCharacteristicNotification`, `WriteCharacteristic`,
     `WriteDescriptor`, `Disconnect`, `Close`), plus a callback surface
     (`IBluetoothGattCallback`) exposing the same events the Android
     `BluetoothGattCallback` fires.
   - Core gains a `NoOp`/`Unsupported` default so `net10.0` still compiles (same pattern as
     the existing `UnsupportedBluetooth*KissStreamConnector`).

2. **Move the state machine into core.** Extract `BluetoothLeGattDuplexStream`'s logic
   into a core class (e.g. `Services/Platform/GattByteChannel.cs`) that depends only on
   `IBluetoothGatt`/`IBluetoothGattCallback`. The Android project keeps a thin adapter
   that binds Android types → these interfaces. `BluetoothSocketDuplexStream` similarly
   becomes a core `DuplexStream` adapter over `IBluetoothSocket`.

3. **Refactor the connectors** (`BluetoothClassicKissStreamConnector`,
   `BluetoothLeKissStreamConnector`) to depend on the interfaces rather than
   `BluetoothPermissionHelper`/Android statics directly; inject an
   `IBluetoothAdapterProvider` (core interface, Android impl uses the real adapter) so
   adapter lookup and permission gating are testable.

4. **Fakes** in `AetherAprs.Tests/` (namespace `AetherAprs.Tests.Services.Bluetooth`):
   `FakeBluetoothAdapter`, `FakeBluetoothDevice`, `FakeBluetoothSocket`,
   `FakeBluetoothGatt` (+ characteristics/descriptors) that script the callback sequence
   and record writes.

5. **Tests** (new files mirroring source, e.g.
   `AetherAprs.Tests/Services/Bluetooth/BluetoothLeGattDuplexStreamTests.cs`,
   `BluetoothClassicKissStreamConnectorTests.cs`):
   - GATT success path: connect → service discovery → characteristics found → CCCD
     written → ready; a subsequent notification delivers bytes via `ReadAsync`.
   - Failure paths: service/characteristic missing; `DiscoverServices` returns false;
     CCCD write fails; disconnect before ready.
   - **Write-abandoned-on-cancel:** cancel a `WriteAsync` mid-flight, then a late
     callback arrives; assert the write gate is eventually released and the next write
     proceeds (regression for the existing `_writeAbandoned` logic).
   - SPP: connect success/failure, adapter-disabled, permission-denied, remote-close.
   - Stream semantics: partial reads, `CanRead`/`CanWrite` after dispose.

**Acceptance criteria:**
- `BluetoothLeGattDuplexStream`'s state machine and `BluetoothSocketDuplexStream` are
  covered by fake-driven unit tests that run on the desktop.
- Core builds for `net10.0` with zero `Android.*` references (grep-verify).
- Physical RFCOMM/GATT interop remains a documented manual test (see §6).

**Tests:** as listed in item 5.

---

## Phase T3 — Protocol robustness & regression guards (folded into T1 where natural)

Smaller items that don't warrant their own phase but harden the suite:

1. A shared **KISS round-trip fuzzer** already exists in `KissModemEdgeCaseTests`; extend
   it to run through `TcpKissEndToEndTests` (feed random frames through the real socket).
2. **`AprsIsModem` reconnect-delay injection** (AD-T3) so reconnect tests are fast and
   deterministic.
3. A **"no flaky sleeps"** review of the new SIL tests: every wait uses
   `WaitAsync(timeout)`; no `Task.Delay` in the happy path.

---

## 4. Suggested execution order

```
T0 (CI + folder)  →  T1 (loopback SIL)  →  T2 (Bluetooth abstraction + fakes)
                                              ↑ (parallelizable with T1 after T0)
```

- **Do T0 first** — it is small and makes everything else verifiable on every push.
- **T1 is the highest-value work**: it is the direct answer to "KISS isn't working
  end-to-end" and guards the TCP seam in CI.
- **T2 is the largest refactor** (touches Android code); do it after T1 stabilizes, or in
  parallel if two agents are available.

This testing workstream is **orthogonal to the feature phases** in `ROADMAP.md`. Run it
**before/alongside Phase 1** (protocol TX) so the round-trip invariants introduced there
land on top of a working SIL net.

---

## 5. Cross-cutting checklist (testing workstream)

- [ ] Every new test file has an SPDX header (`GPL-3.0-or-later`, matching the test
      project) and explicit `using` statements.
- [ ] New test files mirror the source namespace under `AetherAprs.Tests/` (SIL tests use
      `AetherAprs.Tests.Integration`; harness fixtures use
      `AetherAprs.Tests.Integration.Harness`).
- [ ] SIL tests are tagged `[Trait("Category", "Integration")]`.
- [ ] New core interfaces (T2) get a `NoOp`/`Unsupported` default so `net10.0` builds.
- [ ] No production change is made solely to enable a test without an `internal` seam
      (AD-T3) — and any `internal` member is gated by `InternalsVisibleTo` (already
      present for tests).
- [ ] `dotnet build AetherAprs/AetherAprs.csproj` and
      `dotnet test AetherAprs.Tests/AetherAprs.Tests.csproj` are green locally before push.
- [ ] CI (`ci.yml`) is committed and passing.

---

## 6. Out of scope (backlog)

1. **Reference-implementation HIL** (direwolf KISS-over-TCP / scripted igate). Deferred
   per decision; revisit only if interop bugs (e.g. q-constructor or framing) slip through
   the self-authored SIL peer.
2. **Android instrumentation / emulator tests.** Revisit if Bluetooth RF interop needs
   automated coverage beyond fake-driven unit tests.
3. **A separate slow-integration test project.** Split out only if SIL tests measurably
   slow the suite.
4. **Coverage tooling / thresholds** (coverlet, minimum line-coverage gates). Consider
   after the SIL net is in place.
