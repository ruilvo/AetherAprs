<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC-BY-SA-4.0
-->

# AetherAprs architecture roadmap

**Review date:** 2026-11-24

**Baseline reviewed:** `153f27d55753fda368e1a4f28719ffc44848a6ce..HEAD` plus the
working tree during this review

**Purpose:** define the target architecture, correct stale repository guidance, and
turn the audit into implementation-sized work items.

This document supersedes the audit and design sheet as the live backlog. The
[original audit][audit] and [design sheet][design] are available at the pre-consolidation
commit. They describe a different tree; neither a checklist tick nor an audit
severity is proof of current behavior. Recheck each ticket's starting point before editing.

[audit]: https://github.com/ruilvo/AetherAprs/blob/fb1130886a1efe8bc3f7920b667856e2753a0a83/DOCS/AUDIT-2026-11-23-Full-Audit.md
[design]: https://github.com/ruilvo/AetherAprs/blob/fb1130886a1efe8bc3f7920b667856e2753a0a83/DOCS/wiki/Production-Grade-Design-Sheet.md

## Executive assessment

AetherAprs has strong protocol primitives, useful packet models, central package
management, strict compiler settings, REUSE coverage, and a substantial test suite.
The main risk is ownership at subsystem seams. Runtime services still mix transport
lifecycle, persistence, retry policy, UI state, and platform orchestration.

The target is a durable local application with explicit boundaries:

```text
platform hardware/network
        ↓
transport adapters
        ↓
modem lifecycle/session manager
        ↓
packet ingest and transmit pipelines
        ↓
serialized database writer / read query services
        ↓
application state projections
        ↓
ViewModels and Avalonia views
```

The target does not require microservices or a network backend. It requires one
owner for each mutable resource, one direction for each dependency, explicit
durability and lifecycle contracts, and tests that model Android and hardware
failure modes.

The most urgent architectural rule is:

> No feature service creates `AppDbContext` directly. One runtime writer serializes
> all application SQLite mutations. One logical reader owns independent short-lived
> query contexts; concurrent reads do not share one context or wait on a read queue.
> Features see typed command/query capabilities. Schema migration and seeding run
> under exclusive bootstrap ownership before either runtime pipeline starts.

This is stronger and more accurate than the former statement that
`PacketStorageService` is the only writer. The current source has separate direct
writes in `PortService`, `BeaconService`, and startup seeding. The working-tree
message changes move direct EF access out of `MessageService`, but still use
packet records and their own transactions.
The code and documentation must converge on one enforceable ownership model.

## Source-verified state at this review

### Substantially fixed

- Per-port start/stop serialization exists in `PortService`; the original double-start
  race is materially reduced.
- Ingest storage happens before the packet event on the normal path, and ingest and
  digipeat have separate bounded consumers.
- Packet and session logging was reduced on hot paths; raw packet payloads are no
  longer required for normal send/receive logging.
- SQLite timestamps use an explicit UTC value converter for packet timestamp,
  packet protocol timestamp, and message retry timestamp. Round-trip tests were added.
- Android Classic KISS shutdown closes the stream before waiting for a blocking read.
- Android BLE requests an MTU, chunks writes, paces no-response writes, and retries
  selected GATT failures.
- Android foreground service declarations use connected-device/location semantics and
  have a timeout callback. The notification stop action directly targets the service.
- JNI callback disposal was added for location callbacks and the back callback.
- Temporary test database cleanup now clears pools, retries deletion, and uses logging.
- Core DI now uses `NullSecretProtector` by default, so unsupported hosts fail closed.
- Map tile cache initialization uses a private directory and creates `.nomedia`;
  user-visible configuration and offline package paths remain separate.
- The stale project compile item was removed and the repository folder map was updated.

“Substantially fixed” does not mean hardware evidence or a complete architectural
boundary exists. Each item still has residual verification tasks below.

### Partial or still open

| Area | Current truth | Required end state |
|---|---|---|
| Database ownership | Several services still create contexts and write directly | One serialized writer boundary and query-only readers |
| Message durability | Outbound row lookup and mutation use a serialized SQLite transaction in the working tree; identity is still peer/wire number against packet history | Stable logical identity, separate durable outbox, terminal expiry |
| Port lifecycle | Start/stop gates exist | Send leases, session manager ownership, no stale session use |
| Port configuration | Mutable live objects and in-memory-first mutations remain | Persist-first registry with immutable snapshots |
| Settings | Draft-based serialized updates exist in the working tree; legacy mutable access remains | Immutable settings and one serialized update path |
| Session state | Per-packet fan-out was reduced but not fully bounded | One coalescing state writer with transition events |
| Android permissions | Startup permission result plumbing exists | On-demand requests, denial recovery, settings deep link |
| Background location | Foreground permission is requested | Product guarantee narrowed or Android settings flow implemented |
| Foreground readiness | Start rejection is still easy to report as success | Observable `Active/Degraded/Stopped` state |
| Android hardware paths | Core tests cover helper policy; native paths still have behavioral gaps | Transport doubles plus a targeted device check when API behavior is central |
| UI ownership | Home page duplicate startup was removed | No service owns UI collections or Avalonia dispatch |
| Documentation | Historical audit and design sheet made conflicting claims | README for use, AGENTS for rules, this document for decisions and work |
| Release supply chain | Build provenance exists | SBOM, deterministic tool versions, secret-safe signing |

### Audit claims that are stale or overstated

The following claims from the original audit must not be copied into new issues
without rechecking the current tree:

- C-01 is not still the original unguarded double-start implementation, although
  send/stop leasing and gate cleanup remain open.
- C-08 is not still a desktop cleartext default after the fail-closed registration;
  Android Keystore behavior and explicit desktop capability reporting remain work.
- C-11 through C-14 are no longer accurately described as wholly absent. BLE
  chunking/pacing, Classic shutdown ordering, foreground type changes, and the
  explicit stop action exist; hardware and rejection-state tests remain open.
- C-15 is no longer an app-wide missing UTC convention. The converter covers the
  mapped entity properties; non-UTC display and all timestamp paths still need
  regression coverage.
- The app does request foreground location permission. The unresolved question is
  Android 11+ “Allow all the time” background permission and the product guarantee.
- The release workflow still has no verified SBOM step. The design sheet checkbox is
  therefore false until the workflow or the claim changes.
- The four-project count and dead project-file entry were corrected before
  consolidation. The source tree is the folder inventory; no separate inventory
  or folder-list CI check is needed.

## Target architecture

### Assembly and dependency layers

The current project hosts domain, infrastructure, UI, and platform composition in a
single core assembly. The immediate rule is dependency direction; the eventual
physical split is a separate migration:

```text
Presentation               → Application use cases/read projections → Domain
Infrastructure and Android → Application-owned ports and domain types
Composition root           → Presentation, application and concrete adapters
```

Application and domain code must not reference Avalonia controls, `Dispatcher.UIThread`,
`ObservableCollection`, Android classes, or Mapsui layers. Presentation services may
adapt application state into UI objects. A future `AetherAprs.Core` assembly should
make this rule compiler-visible, but the interfaces and ownership rules come first.

### Composition root

`ServiceProviderFactory` must become a composition root, not a service locator.

Required design:

1. Resolve app-data paths from a platform bootstrap value before building the final
   container; do not build a throwaway provider.
2. Build configuration once and inject the resulting settings store.
3. Use explicit replacement for platform services; do not rely on registration order
   as an undocumented override protocol.
4. Enable `ValidateOnBuild` and `ValidateScopes` in development and test hosts.
5. Keep the provider owned by the application lifetime and avoid static lookups from
   ViewModels or services.
6. Construct timers, event subscriptions, and background workers in `StartAsync` or
   `InitializeAsync`, not in constructors.

> **Status: items 1, 3, 4 and 6 implemented.** The final container in
> `ServiceProviderFactory` and the designer container in `DesignData` are both
> built with `ValidateOnBuild` and `ValidateScopes`. A registration that cannot
> be resolved, or a singleton that captures a scoped service, now fails at
> startup naming the offending type instead of surfacing as a navigation or
> packet-handling failure later.
>
> App-data paths no longer come from a throwaway container. `App` exposes
> `CreateAppDataDirProvider()`, the platform supplies that bootstrap value, and
> `CreateServiceProvider` takes it as its first argument and registers that
> exact instance. The container is therefore built once, and the settings file
> and database paths are known before it exists.
>
> Platform replacement is explicit. `OverrideCoreServices` receives
> `CoreServiceOverrides`, whose `Replace<TService, TImplementation>()` swaps an
> existing core descriptor and throws when the core does not register the
> service. `RegisterPlatformServices` is now additive only — the Android
> connectors accumulate there — so "last registration wins" is no longer how a
> platform overrides anything, and the duplicate registrations it used to leave
> behind are gone.
>
> Background work starts in a phase, not a constructor. `PacketIngestPipeline`
> and `DigipeatRouter` construct their queue but no consumer;
> `IPacketIngestPipeline.StartAsync` and `IDigipeatRouter.StartAsync` start the
> drain, and `SessionStateWriter` starts from `SessionCoordinator.InitializeAsync`.
> The startup sequence runs a `consumers` step before it arms any port, so a
> packet can never arrive while the queue has no reader, and disposing an
> application that never got that far no longer leaves a task behind.
>
> Item 5 is implemented as well. The static `App.GetService<T>()` accessor and the
> unused `ServiceProviderFactory.ServiceProvider` lazy locator are gone. The only
> remaining resolution is at a framework entry point that Android creates itself
> (an activity or a service): that object already holds the application, so it
> reads `App.ServiceProvider` and passes the resolved dependency into ordinary
> classes. Composition-root rule `A012` rejects both static accessors, so a
> ViewModel or service cannot reintroduce one.

### Persistence

Use one infrastructure-owned runtime writer and one infrastructure-owned logical
reader. EF delegates, if useful internally, stay internal. The writer accepts
commands through a bounded queue, owns the transaction and commit result, and
reports rejection or failure to the caller. The reader materializes detached results
using independent short-lived contexts; no `IQueryable` or tracked entity escapes.
Don't serialize reads behind writes. A query started after a successful write must
see that committed change; queries already in flight may finish on their prior snapshot.

The final domain-facing boundaries are:

- `IPacketWriter`: insert packet history and delete eligible history.
- `IPacketQueryService`: packet history, station summaries, object
  records, and map/trail projections.
- `IPortRegistry`: durable port configuration and immutable configuration snapshots.
- `IBeaconStateStore`: beacon configuration and state.
- `IMessageCommands` and `IMessageQueries`: one logical outbound message, attempt
  updates, ACK/REJ transitions, expiry and recovery. The outbox is separate from
  packet history; queries can join explicitly when a feature needs both.

Keep `AppDbContext`, provider APIs and `DbSet` inside data infrastructure and
exclusive bootstrap. Enforce dependency direction before considering project splits.

### Packet ingest

```text
modem event
  → port session manager
  → bounded ingest queue
  → packet writer transaction
  → StoredPacket event
  → query projections / message handler / map projection
  → independent digipeat policy queue
```

Invariants:

- A packet is published only after durable insertion succeeds.
- Queue capacity and drop policy are explicit per transport class.
- Retry only bounded transient failures for transactions known not to have committed.
  Resolve ambiguous commits by idempotency key or report an indeterminate outcome;
  never replay a side effect blindly.
- Digipeating never blocks packet persistence.
- Packet event subscribers cannot abort the ingest consumer.
- Drop counts, storage failures, and queue depth are diagnostic state.
- Packet timestamps are UTC instants at the domain boundary.

### Outbound transmission

All producers use one transmit pipeline:

```text
producer → optional durable work item → authorization and route selection
         → session generation + resource lease → modem
         → per-port outcome → history and/or durable retry state
```

The pipeline returns per-port results. It does not hide partial success. Messages,
beacons, objects, bulletins, digipeats, and gateways may choose different durability
policies, but the choice is explicit and tested. Transport readiness, RX enablement,
per-route TX enablement, and the operator's TX arm are separate. Recheck authorization
at send time, not just when admitting work to a queue.

### Port and modem lifecycle

`PortConfig.IsEnabled` is user intent. It is never changed by a reconnect supervisor.
Runtime state is separate:

```text
Stopped → Starting → Running → Stopping → Stopped
                    ↘ Faulted → Backoff → Starting
```

One `IPortSessionManager` owns the active session map, generation IDs, modem event
subscriptions and disposal. A separate foreground coordinator owns OS service
leases. Stop marks the session unavailable, rejects new sends, signals cancellation
and aborts transport I/O where required to unblock the operation. Never wait while
holding a lock needed for completion. Dispose after in-flight operations settle;
if a native path cannot settle by its deadline, report a fault, quarantine the
generation/resource and prevent reuse until cleanup. Serialize shared audio/PTT
and physical TNC access across ports as well as within each port.

`IAprsModem` should eventually be:

```csharp
public interface IAprsModem : IAsyncDisposable
{
    ModemState State { get; }
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
    Task SendAsync(AprsPacket packet, CancellationToken cancellationToken);
}
```

The creator owns the modem and all resources it constructs. Injected dependencies are
not disposed unless an explicit factory contract transfers ownership.

### Settings and configuration

`ConfigurationService.UpdateSettingsAsync` already serializes patches to private
drafts in the working tree. The settings page saves the changed field so a
concurrent offline map selection is preserved. `GetSettingsSnapshot` gives
readers an independent copy. Treat the legacy `Settings` graph as read-only until
all call sites migrate; returning an independent copy is not the final immutable
contract.

Settings must be immutable to readers:

```csharp
public interface IAppSettingsStore
{
    AppSettings Current { get; }
    bool IsReadOnly { get; }
    Task<SettingsUpdateResult> UpdateAsync(
        Func<AppSettings, AppSettings> update,
        CancellationToken cancellationToken);
}
```

The update sequence is validate → serialize temporary file → atomic replace → swap
the immutable reference → raise change event. A newer on-disk schema is read-only;
it is never re-stamped as an older schema. Validation reports all invalid fields.

### UI and application state

ViewModels own `ObservableCollection` instances and navigation state. Services expose
immutable projections and events or channels. UI dispatch is an injected interface.
Views wire controls and dialogs; they do not start background services, access EF,
or decide persistence order.

Startup is an explicit state machine:

```text
resolve paths and read configuration → construct/validate application graph
→ bootstrap SQLite → run read/write pipelines → expose UI and eligible RX
→ request feature permissions on use → TX only after explicit operator arm
```

No startup task is silently discarded. Framework `async void` entry points catch and
record failures. Background workers have cancellation, bounded queues, and a shutdown
deadline.

## Implementation roadmap

Each work item is intentionally sized so one agent can implement it without deciding
architecture again. Agents must read the relevant source and tests first, preserve
unrelated working-tree changes, add a regression test, and report build/test evidence.

Start with R0.2 and R1.1. Inventory current context users in `Data/`,
`PacketStorageService`, `PacketQueryService`, `PortService`, and `BeaconService`.
Land the writer/reader infrastructure with disk-backed integration tests before
changing feature callers. The first caller migration is history insertion and
post-commit publication. Keep the outbox schema and hardware lifecycle changes in
separate work items.

### R0 — Establish the contract

**R0.1 Architecture truth check**

- Use this document's work IDs in PRs. Record implementation, acceptance test and
  remaining limits when closing a work item. Treat source-only changes as partial
  where the observable behavior is not yet demonstrated.

**R0.2 Boundary scan**

Status: implemented. `AetherAprs.Tests/Architecture/` enforces the rules as
`dotnet test` cases, so CI runs the gate through the existing test step and the
explicit "Check architecture boundaries" step in `build-test.yml`.

- Fail if application services contain `IDbContextFactory`, `AppDbContext`, `DbSet`,
  `SaveChanges`, or `Dispatcher.UIThread` outside approved infrastructure adapters.
- Fail if `Debug.WriteLine` appears in production code.
- Keep sensitive payloads out of routine logs at every level. Assert redaction using
  a capturing logger in the paths that handle credentials or content.
- Baseline existing architecture violations by exact type and work ID. Fail on new
  violations and remove baseline entries when the corresponding migration lands.

Implemented rules: `A001` (persistence ownership), `A002` (UI thread dispatch),
`A003` (`Debug.WriteLine`), `A004` (modem construction, added with `R2.1`),
`A005` (modem handle ownership), `A006` (no bulk start-all path), `A007`
(bounded queues need a named owner) and `A008` (keep-alive transitions belong to
`IKeepAliveService`), the last four added alongside the port runtime and
keep-alive work items `R2.2`, `R2.3` and `R2.4`, plus `A009` (settings documents
stay inside the configuration adapter, added with `R3.1`), `A010` (startup steps
are declared in the composition root, added with `R3.3`), `A011` (only the
composition root builds a container, added with the composition-root items) and
`A012` (no static service accessor, added with the close-out of composition-root
item 5), `A013` (Android code logs through `ILogger`; only the logcat provider
names `Android.Util.Log`, added with the `R5.4` logging work), `A014` (an Android
component declares whether it is exported) and `A015` (pending intents are
immutable), both added with the exported-input review, and `A016` (a wake lock is
acquired with a timeout, added with the `R5.2` wake-lease bound). The
baseline in `ArchitectureBaseline.cs` is now empty: the `PortService` and
`BeaconService` `A001` entries were removed when `R1.3` landed. `MessageService` and
`ReceivedBeaconsMapLayerService` now receive `IUiDispatcher`, so their `A002` entries
were removed rather than re-baselined. Payload redaction is asserted where a log
path owns it — the permission reporting path has a capturing-logger case — and
remains open for the hot packet paths (`R6.7`).

**R0.3 Test harness**

- Reuse `TempAppDatabase` and its cleanup tests; add disk-backed fixtures for
  migrations, restart, and locking only where necessary.
- Add a bounded `WaitUntilAsync` helper using the test cancellation token.
- Add a fake clock and use one captured instant per assertion.
- Build narrow fakes at actual ports for clock, transport, foreground state and UI
  dispatch. Use gates/tasks instead of arbitrary sleeps to control race tests.
- Replace interaction-only tests with assertions on the subject's state.
- Stabilize `AprsIsModemTests.SendAsync_RfReceivedPacket_UsesQarConstructorWithPath`:
  writing and flushing the simulated server's login reply does not prove the modem
  consumed it. Await the modem's authenticated-ready signal with a test deadline
  before asserting the RF-to-IS wire format.

Exit: the boundary scan runs locally and in CI; no new violation can enter unnoticed.

### R1 — Database writer and reader

**R1.1 Create the serialized writer**

Implemented in `AetherAprs/Data/SqliteRuntimeWriter.cs` and
`SqliteRuntimeReader.cs`, with on-disk tests in `SqliteRuntimeTests.cs`.
The queue admits up to 128 waiting commands; a full queue rejects new writes
explicitly. A caller knows only after the writer returns whether its admitted
command committed. The reader opens a separate read-only context per query.

- Implement a single writer worker with a bounded command queue.
- Preserve command result, exception, cancellation, and shutdown semantics.
- Bound admission and distinguish cancellation before execution from cancellation
  after execution starts. Never return cancellation for a known successful commit.
- Bound retry on SQLite busy/locked; validate fresh context/transaction and outcome
  classification. Drain admitted work on normal stop, reject new work and resolve
  every waiting caller. Run cleanup in small commands so it cannot monopolize ingest.
- Add writer metrics: queue depth, operation duration, retries, failures.
- Test ordering, cancellation, writer failure, shutdown, and concurrent callers.

**R1.2 Move packet operations**

Files: `PacketStorageService.cs`, `PacketQueryService.cs`, `MessageService.cs`.

Packet history and the current message lifecycle use the shared runtime writer;
retention yields between committed batches. Outbound creation still looks up
the peer and wire message number in the existing schema. Packet insertion
notifies subscribers after commit, with each subscriber isolated. `MessageService`
uses the typed packet write and query interfaces. The separate outbox, durable
logical identity, and pending expiry remain follow-up work, as does migration of
port and beacon writes to this writer (R1.3).

- Move inserts and cleanup into history commands; move delivery and retry into
  outbox commands. Notify subscribers after commit and isolate each subscriber's
  failure from command result and other subscribers.
- Move all packet reads in `MessageService` behind query interfaces.
- Return a stable packet/message identity from first insertion.
- Use a caller-generated GUID for logical identity across enqueue retries/restart.
  Reserve the limited wire number atomically while active; correlate using local and
  peer identity plus number. Retain a bounded reuse guard for late ACKs. When no
  number is available, show a blocked/queued state; don't reuse an active number.
- Migrate legacy rows with a deterministic merge and safe fallback for ambiguous
  ownership; preserve history and don't replay uncertain legacy sends automatically.
- Make terminal rows eligible for cleanup; pending rows require expiry.

Acceptance:

- Five concurrent sends for one logical message create one durable message record.
- Attempt count increments on that record.
- ACK/REJ matches local and normalized peer identity plus number; duplicate and
  late outcomes never regress terminal state. A new message can reuse a number
  once the reservation and reuse guard expire.
- A restart restores only unexpired pending work.
- No feature service creates `AppDbContext`.

**R1.3 Move port and beacon persistence**

- Extract `IPortRegistry` and `IBeaconStateStore`.
- Make mutations durable-first, then update immutable in-memory snapshots.
- Ensure reconnect and health paths cannot mutate `IsEnabled`.
- Put seeding/migration in exclusive bootstrap; verify schema and load initial
  snapshots before starting runtime commands, readers, or modem work.
- Add failure tests proving memory remains unchanged after a failed write.

Exit: the source scan finds one approved database writer implementation and approved
query services only.

Status: implemented.

`IPortRegistry` (`Services/Ports/IPortRegistry.cs`) and `IBeaconStateStore`
(`Services/Beaconing/IBeaconStateStore.cs`) are the typed capabilities feature code
sees. Their adapters (`Data/Ports/PortRegistry.cs`,
`Data/Beaconing/BeaconStateStore.cs`) are the only port/beacon persistence code and
are allow-listed owners in the R0.2 source scan, so `PortService` and `BeaconService`
no longer reference `AppDbContext`, `IDbContextFactory`, or `SaveChanges` at all.
Both adapters run every mutation through `SqliteRuntimeWriter` and every read
through `SqliteRuntimeReader`.

Every port mutation is now durable-first: the store write must succeed before the
in-memory snapshot changes. `SetPortEnabledAsync` persists the intent, updates
memory, then converges the session; a failed start rolls the intent back durably
before reporting the failure, and re-enabling an already-enabled port still
guarantees a running session so supervision and startup stay correct.
`BeaconService.UpdateConfigurationAsync` and `SetActiveModeAsync` follow the same
order and are async on `IBeaconService`, which removed the fire-and-forget
`QueuePersistSnapshot` path that used to swallow write failures.

Published configuration is a copy, not a shared instance: `PortConfig` and its
type settings implement `Clone()`/`WithEnabled()`/`WithShowOnMap()`, and
`PortService.Ports`, `AddPortAsync`, and `PortItemViewModel.BuildConfig()` all work
on copies. A caller can no longer mutate service state by editing a published
snapshot.

`PortServiceSessionController` already isolates reconnect and health supervision on
`IPortSessionRuntime`, so those paths start and stop modems without touching the
persisted `IsEnabled` intent. Seeding and migration stay in the exclusive
`AppSavedDataInitializer` bootstrap.

Failure tests prove the durable-first ordering: `PortServiceTests` drops the
`Ports` table and asserts that `SetPortEnabledAsync`, `AddPortAsync`, and
`UpdatePortAsync` throw while leaving memory unchanged,
`BeaconService_FailedWrite_LeavesPreviousStateActive` does the same for the beacon
state store, and
`SetPortEnabledAsync_WhenAlreadyEnabled_StillEnsuresSessionRuns` pins the
idempotent-start rule.

### R2 — Port session subsystem

**R2.1 Extract modem factory**

- Move transport selection from `PortService` to `IModemFactory`.
- Add a factory test for every configured transport and unsupported transport.
- Propagate cancellation into every connector.

Status: implemented. `AetherAprs/Services/Ports/IModemFactory.cs` declares
`IModemFactory` and `IPortModem`; `AetherAprs/Services/Ports/ModemFactory.cs`
implements both. `ModemFactory` is now the only place that maps
`IPortTypeSettings` onto a concrete modem, and it owns the whole resource graph
for a session: an APRS-IS modem, an AFSK modem, or a KISS modem plus the `KissModem`
and stream beneath it. `OwnedPortModem` presents that graph as one
`IPortModem`, so `PortService` disposes exactly one object and no longer knows that
a KISS session keeps its own transport wrapper alive. `PortService` lost seven
constructor dependencies (`IConfigurationService`, `ILoggerFactory`,
`IKissStreamFactory`, `IAudioStreamService`, `IAudioPlaybackService`,
`IPttService`, `ILocationTrackingService`) and its `ActivePortSession` lost the
`KissModem` member. Modem ownership is now explicit: `AfskModem.DisposeAsync` stops
capture but no longer disposes the shared `IAudioStreamService`, and the factory
disposes the stream it opened when the modem cannot be built on top of it.
Cancellation reaches every connector because `CreateAsync` takes the caller's token
and forwards it to `IKissStreamFactory.OpenAsync`. A004 in the architecture scan
forbids `new AprsIsModem`, `new AprsRfModem`, `new KissModem`, and `new AfskModem`
outside the factory. `ModemFactoryTests` covers each transport, missing type
settings, an unregistered settings type, KISS without transport settings, an
unregistered KISS transport, token forwarding, and stream disposal;
`PortServiceTransportFaultTests` covers fault attribution and session disposal.

> **Status: implemented.** `IPortSessionManager` owns every running modem session;
> `PortSessionManager` is the only implementation and the only type besides the
> modem factory that names `IPortModem`. `PortService` keeps configuration, ingest,
> and digipeat relay, and asks the manager for starts, stops, and sends, so it never
> holds, starts, or disposes a modem. Each port has a runtime slot holding its start
> and stop gate, its session, and its send counter. Starts and stops take the gate,
> so two concurrent starts create one modem and a stop cannot interleave with a
> start. A send takes a lease before it leaves the lock and releases it when the
> modem call returns; a stop removes the session, refuses new sends, and waits for
> outstanding leases up to the drain deadline (five seconds by default) before it
> disposes the modem. A stop whose modem throws while stopping or disposing still
> reports `PortSessionStopStatus.Failed` with the original exception, has already
> released the session, and therefore permits a later start; `StopAllAsync` attempts
> every session and returns one result per session so failures are reported
> together. `RemoveAsync` retires the port's slot, and the slot is collected and its
> gate disposed only after the last operation leaves it, so pruning can never
> dispose a gate another operation is holding. Starts now return
> `PortSessionStartResult` (`Started`, `AlreadyRunning`, `UnknownPort`, `Cancelled`,
> `Failed`) with the original exception attached; `IPortSessionRuntime` returns that
> result, and `PortServiceSessionController` turns it into the exception the
> coordinator's reconnect loop needs while rethrowing cancellation unchanged.
> `IPortService.StartAllEnabledPortsAsync` was removed because it had no production
> caller and bypassed supervision; startup already goes through `SessionCoordinator`.
> A005 confines `IPortModem` to the factory, its contract, and the session manager,
> and A006 rejects any reappearance of a bulk start-all path.
> `PortSessionManagerTests` proves one modem per concurrent start, no send through a
> disposed modem, a bounded drain deadline, a failed stop that still permits a later
> start, aggregate stop-all reporting, slot pruning on removal, structured
> cancellation and failure results, and packet and fault attribution to the owning
> port. `PortServiceSessionControllerTests` proves a failed start surfaces as an
> exception carrying the original one and a cancelled start rethrows cancellation.

**R2.2 Extract session manager**

- Own active sessions, lifecycle state, start/stop gates, send leases, and disposal.
- Do not expose mutable modem instances to callers.
- Return structured start failures with the original exception attached.
- Remove `StartAllEnabledPortsAsync` or delegate it exclusively to the supervisor.
- Dispose and prune per-port gates with the session lifecycle.

Tests:

- Concurrent start creates one modem.
- Concurrent send and stop never sends through a disposed modem.
- Stop waits for an in-flight send and completes within a deadline.
- Failed stop permits a later start.
- Stop-all attempts every session and reports aggregate failures.

**R2.3 Extract ingest and digipeat**

- Move channels and consumers to `IPacketIngestPipeline`.
- Make backpressure/drop policy configurable and observable.
- Move forwarding decisions to `IDigipeatRouter`.
- Route digipeat through the same transmit authorization and leases as other work.
- With deterministic bursts and a blocked route, assert RX persistence continues,
  drops are counted and a stale forward is discarded rather than replayed.

> **Status: implemented.** The inbound queue and its consumer moved to
> `IPacketIngestPipeline` (`AetherAprs/Services/Packets/`), built from
> `PacketIngestOptions` so queue capacity, storage attempt limit and retry delay are
> explicit policy instead of constants. `Accept` never blocks the modem read loop;
> a full queue drops the newest packet, and `GetMetrics()` exposes enqueued, stored
> and dropped counts while the diagnostic sink records the drop reason. The pipeline
> raises `PacketStored` only after the write succeeded, which is what keeps live
> consumers and the database from diverging.
>
> Forwarding moved to `IDigipeatRouter` (`DigipeatRouter`), which owns its own
> bounded queue and consumer and decides routes when a candidate is dequeued, not
> when it arrives. Each decision reads current configuration through
> `IPortConfigurationSource` and current sessions through `IPortSessionManager`, so
> a route that disappeared is discarded instead of being retried. Transmission goes
> through `IPortSessionManager.SendAsync`, which means a forward uses the same send
> lease and stop-drain rules as any other outbound work, and the router never starts
> a port to satisfy a forward: only operator enablement may do that.
> `DigipeatRouterOptions.MaxCandidateAge` bounds freshness, so a relay that waited
> behind a blocked route is discarded rather than replayed, and
> `DigipeatRouterOptions.TransmitTimeout` keeps one wedged modem from stalling the
> consumer. Discards and failures are counted separately from receive drops:
> `IPortServiceTelemetry.GetDroppedForwardCount` reports the forwarding path, the
> redacted support report exposes both, and the diagnostic sink records the reason.
>
> To keep those dependencies acyclic, configured ports moved out of `PortService`
> into `IPortConfigurationService` (`PortConfigurationService`): it owns the port
> list, applies every mutation durable-first, publishes independent copies and
> raises `PortsChanged`; `IPortConfigurationSource` is the read-only view the router
> consumes. `PortService` is now a compatibility façade that converges operator
> intent, forwards observations, and keeps the keep-alive reason aligned; the
> composition root and design-time provider register every new owner. A007 confines
> bounded queue creation to the named owners (runtime writer, ingest pipeline,
> digipeat router, beacon transmit queue, and the two Android stream adapters that
> own their own consumers).
>
> Tests: `PacketIngestPipelineTests` proves a full queue counts the newest drop,
> that `PacketStored` is raised only after storage, that a storage failure drops
> without publishing, and that invalid options are rejected. `DigipeatRouterTests`
> proves queue-full discard, aged-out discard instead of replay, that a forward to a
> port that is not running is discarded without starting it, that a running target
> receives the forward and raises `PacketForwarded`, that a transmit failure is
> counted, and that a candidate from a port that is no longer configured is
> discarded. `PortConfigurationServiceTests` proves durable-first ordering, copy
> semantics, change-only notifications and that updating an unconfigured port
> reports no change. The end-to-end pipeline tests keep asserting that ingest
> continues while a relay is blocked and that forwarding drops are counted
> separately from receive drops.

**R2.4 Extract keep-alive**

- Make keep-alive acquisition a serialized state machine.
- Foreground start failure produces `Degraded`, never `Active`.
- Report `Active`, `Degraded(reason)`, `Stopping`, and `Stopped`.
- Pair every acquire/release under concurrent transitions.

> **Status: implemented.** `IKeepAliveService` is now a serialized state machine
> rather than a reference-counting wrapper. `KeepAliveService` holds the set of
> active reasons and applies every transition under one gate that also spans the
> platform call, so a release that arrives while a start is in flight waits for
> that start and is applied after it; the platform never sees a stop racing a
> start, and the reported state always describes the last completed transition.
> `KeepAliveStatus` reports `Active`, `Degraded` with a reason, `Stopping` and
> `Stopped`, together with the held reasons and the declared service types, and
> `StatusChanged` publishes only real changes. `Stopping` is published before the
> platform stop, so no observer sees `Stopped` for a service that is still
> running.
>
> The platform contract became honest first, which is what makes `Degraded`
> reachable: `IForegroundService.StartAsync` returns a
> `ForegroundServiceStartResult` instead of `Task`. `AndroidForegroundService`
> records the request, waits for the service to publish `started` or `failed`
> over its existing state broadcast (five-second deadline) and reports a
> refusal, a rejection or a timeout as `Rejected` with a reason, so a start is
> never assumed from the fact that the intent was delivered. The no-op and the
> design-time adapter report success because no platform service exists to hold.
> A refusal never loses a reason: the hold becomes `Degraded`, the reason stays
> held, and the next acquire retries, so supervision or the operator can recover
> without restarting the application. A stop that leaves the service running, or
> that throws, is reported as `Degraded` with no reasons held rather than as
> `Stopped`.
>
> `PortService` no longer keeps its own `_portsKeepAliveActive` copy of the
> platform state: it states intent (`AcquireAsync` while a session runs,
> `ReleaseAsync` otherwise) and the state machine deduplicates. Transitions are
> recorded in the diagnostic sink under the `keepalive` category (degraded
> entries as warnings), and the redacted support report exposes the hold state,
> reasons, declared types and degraded reason. A008 confines
> `IForegroundService` to the platform adapters, the keep-alive service, the
> composition root and the design-time provider.
>
> Tests: `KeepAliveServiceTests` proves first acquire starts and reports
> `Active`, a repeated reason starts once, a distinct reason refreshes the
> declared types, a rejected start reports `Degraded` and never `Active`, a
> degraded hold retries on the next acquire, a throwing platform call degrades
> with the failure, a release keeps the service while another reason is held,
> the last release reports `Stopping` then `Stopped`, releasing an unheld reason
> never touches the platform, a service that keeps running after a stop degrades,
> a release queued behind an in-flight start applies after it without a racing
> stop, concurrent acquires converge on the held reasons, and a degraded
> transition is recorded in diagnostics. `DiagnosticsTests` proves the support
> report includes the hold state.

Exit: each mutable resource has one owner; `PortService` delegates if a
compatibility façade is still needed.

### R3 — Settings and application lifecycle

**R3.1 Immutable settings**

- Convert settings graph to immutable records or immutable snapshot objects.
- Replace `Settings` mutation and hand-written clone with `Current` plus `UpdateAsync`.
- Remove synchronous save and blocking semaphore waits.
- Preserve newer schemas as read-only.
- Add migration, invalid-field, backup-retention, and concurrent-update tests.

> **Status: implemented.** Settings are now an immutable snapshot graph:
> `AppSettings`, `AprsSettings`, `MapSettings`, `MessageTemplatesSettings` and
> `MessageTemplate` are sealed records with `init` accessors and read-only
> collections, so a caller cannot mutate live configuration and a `with`
> expression is the only way to describe a change. `IConfigurationService`
> exposes `Current` plus `UpdateAsync(Func<AppSettings, AppSettings>)`; the
> mutable `Settings` property, the synchronous `SaveSettingsAsync`, the
> `GetSettingsSnapshot` clone and the `UpdateSettingsAsync` action overload are
> gone, and every former caller now states a transform (the settings page
> rewrites its auto-save handlers, the message template service replaces a list
> entry instead of editing one).
>
> `UpdateAsync` takes one asynchronous gate, runs the transform against the
> committed snapshot, writes the result and only then publishes it. The write is
> therefore durable-first: a failed save leaves `Current` untouched and raises no
> `SettingsChanged`, and two concurrent partial updates observe each other's
> result in the order their transforms run rather than overwriting one another.
> A transform that returns `null` or throws aborts the update with the committed
> snapshot intact. No blocking wait remains on the save path.
>
> Binding is the one place that needs settable state, so the configuration
> adapter still binds key by key into internal documents under
> `Configuration/Settings/Documents`. Keeping them separate is what let the
> public graph become immutable without losing the loader's behaviour: the
> per-key `ConfigurationBinder` call and its try/catch still skip one bad value
> and keep the rest, because the document keeps the validating setters. A
> document converts once through `ToSettings()`, and the snapshot is the only
> shape the rest of the application sees. Schema versioning is unchanged:
> migrations still run before binding, the file is backed up when the version
> changes, and a document newer than this build is read-only, so `UpdateAsync`
> refuses before invoking the transform and logs the refusal instead of
> downgrading the file.
>
> Validation lives in one `SettingsValidation` helper that both the document
> setters and the record accessors call, so a value assigned through `with` is
> rejected exactly like a value read from the file. The callsign normalises to
> upper case and rejects blank, over-long and non-alphanumeric input; SSID,
> symbol table, symbol code and overlay reject undefined enum values.
>
> Tests: `ConfigurationSnapshotTests` guards the two graphs against drift by
> reflection, failing if any public property on a snapshot type has a setter or
> if a document stops carrying a snapshot property. `AprsSettingsTests` covers
> blank and malformed callsigns, each undefined enum, upper-casing, and that
> `with` leaves the original snapshot unchanged. `ConfigurationServiceTests`
> covers an immutable snapshot that a caller cannot tamper with through
> `Current`, serialised concurrent partial updates, and a throwing transform
> that leaves the committed snapshot unchanged. `SettingsVersioningTests` covers
> migration round-tripping, backup retention and the read-only refusal, which now
> asserts no snapshot change and a single warning naming `UpdateAsync`. A009
> confines the document types to the configuration adapter and their own folder.

**R3.2 Session coordinator**

- Separate user intent from runtime state.
- Replace per-event file writes with one coalescing writer.
- Persist state transitions and periodic diagnostic timestamps only.
- Bound telemetry input with a channel.
- Stop with a non-cancelled teardown token and deadline.

> **Status: implemented.** `SessionCoordinator` now exposes `SessionIntent`, the
> operator's desired running set, separately from the per-port `PortSessionStatus`
> runtime state it supervises. Intent lives only in memory, so a restarted process
> always comes up disarmed and waits for an explicit start; `StartAsync` arms it and
> `StopAsync` disarms it.
>
> Persistence goes through `SessionStateWriter`, the single owner of the
> `session-state.json` writer. Its queue holds one request, so a burst of reconnect
> transitions collapses into one write of the newest snapshot instead of one write
> per event, and `FlushAsync` lets a caller that needs durability wait against its
> own deadline. Transitions are published immediately; `LastReceiveUtc` and
> `LastTransmitUtc` are diagnostic timestamps that travel with the next transition
> or the periodic flush, which runs every `diagnosticPersistInterval` (one minute by
> default) instead of once per packet (audit C-03).
>
> `SessionCoordinatorTelemetryBridge` no longer starts one task per transport event.
> It posts observations into `SessionTelemetryIntake`, a bounded queue with a single
> consumer that applies them in order; a full queue drops the observation instead of
> blocking a modem read loop and exposes `DroppedObservations` so the loss is
> observable.
>
> `StopAsync` takes no cancellation token. Teardown runs against an internal
> `teardownTimeout` (ten seconds by default) so a shutdown that has already cancelled
> its own token still cancels reconnect loops, disconnects every port and flushes the
> final snapshot; an expired deadline is recorded as a diagnostic instead of leaving
> sessions half torn down.
>
> A007 covers the two new queue owners. Tests: `SessionStateWriterTests` proves
> coalescing, flush ordering, recovery after a failed write, and drain-on-dispose;
> `SessionTelemetryIntakeTests` proves in-order delivery, the counted drop when the
> queue is full, and that a failing handler does not stop later observations;
> `SessionCoordinatorTests` adds intent/runtime separation, the internal teardown
> deadline when a transport hangs, and the periodic diagnostic flush.

**R3.3 Startup**

- Sequence database initialization, settings load, feature permissions, session arm,
  and port start.
- Request Bluetooth/audio/location permissions when the feature is used.
- Keep arm in runtime memory. Initialize disarmed on reboot, force-stop, update
  and process death; activity recreation in the same live session is not a new
  arm or a stop. Show pending work and a resume action. (Decided: the resume is
  automatic — launch re-arms every enabled port, because configuration is the
  operator's durable intent; see the status note below.)
- Make maintenance a cancellable deferred worker.

> **Status: implemented.** Startup now runs one ordered sequence
> instead of independent fire-and-forget tasks. `ApplicationStartup` executes
> named `StartupStep` records in declaration order, stops on a failed required
> step and skips the rest, records a failed optional step and continues, and
> returns the first run's report to every later caller so activity recreation in
> a live process never re-arms or stops the session. Each outcome is logged,
> recorded in diagnostics under `startup`, and raised through `StepCompleted`.
> `StartupStepFactory` declares the single sequence — database, settings, ports,
> permissions, location, maintenance, consumers, sessions — and rule A010 keeps
> step declarations out of feature code.
>
> Permissions are feature-scoped. `PortStartupPermissions` asks for the
> notification, Bluetooth and audio permissions that the enabled ports actually
> need, so a launch with no enabled port prompts for nothing; the location
> permission and the battery-optimization exemption moved from startup into
> `LocationTrackingService.RequestPermissionAndStartTrackingAsync`, which the
> home page already invokes when the operator starts tracking. A denied
> permission degrades the feature that needs it instead of stopping startup.
>
> Retention is no longer an `App` loop. `DatabaseMaintenanceWorker` defers the
> first sweep, repeats on an interval, keeps running after a failed sweep, and
> cancels with the application.
>
> Arm/resume is settled by an explicit product decision: **launch auto-arms every
> enabled port**. Configuration is the operator's durable intent, so after a reboot,
> force-stop, update or process death the process restores no live session from the
> snapshot and arms exactly the ports that are enabled now. Intent itself never
> leaves memory, so activity recreation in a live process still does not re-arm or
> stop anything. The restart harness (`RestartResumeTests`) proves both halves:
> a snapshot that recorded a connected session restores as `Stopped` with no intent,
> and arming connects only the port that is still enabled, never the one disabled
> since the previous run.

Exit: a startup harness proves ordering and a process-death harness proves safe resume.

### R4 — Application/UI separation

- Inject `IUiDispatcher`; remove Avalonia calls from application services.
- Move conversation collections and active-conversation navigation state to ViewModels.
- Extract map interaction controllers from page code-behind.
- Replace stale collection converters with ViewModel `HasItems` properties.
- Virtualize unbounded lists and add paging to details/search.
- Test ViewLocator routes that use automatic view resolution; exercise explicitly
  wired and owner-created views through their actual composition.
- Use localized fallback text or reject unmapped ViewModels.
- Move theme application behind `IThemeService`.
- Add an architecture test for assembly references before physically splitting projects.

Exit: application services can run in a plain test host with no Avalonia application.

### R5 — Android platform correctness

**R5.1 Permission and location contract**

- Implement request-on-demand permission APIs with one cancellable request budget.
- Queue one OS permission request at a time in an activity-owned broker; complete
  requests on callback, cancellation or activity teardown. Match request IDs so
  a stale callback cannot satisfy a new request. Recheck grants on resume and at
  capability use; do not infer permanent denial from `ShouldShowRequestPermissionRationale`
  without knowing whether an earlier request was made.
- Distinguish denied, permanently denied, unavailable, and granted.
- Add rationale UI and an application-settings recovery action.
- Request foreground permission for the feature in use. If background-location
  access is necessary beyond legally started while-in-use location foreground
  work, explain the need, open app settings, and recheck on resume. Keep the app
  useful when the operator declines; never promise continuous tracking.
- Test clean install, denial, revoke, settings return, activity recreation, and slow
  user interaction.

> **Status: distinct permission states implemented (2026-11-26); broker queue, rationale UI and native acceptance still open.**
> `IPermissionService` no longer answers with a `bool`. `RequestAsync(PermissionKind)`
> returns a `PermissionRequestResult` carrying one of `Granted`, `Denied`,
> `PermanentlyDenied` or `Unavailable`, so callers can tell "ask again" from "the
> operator must use the application settings" from "this platform cannot do it".
> The decision lives in `PermissionRequestPolicy.Evaluate`, which takes the grant
> flag, the platform rationale hint, whether this process already requested the
> permission, and whether the platform level supports it. The "requested before"
> fact is what separates a first-time denial (no rationale is also reported before
> the first prompt) from a permanent denial, which is the rule the roadmap calls
> out. `OpenApplicationSettingsAsync` is the recovery action for the permanent case.
>
> `PortStartupPermissions` now logs each outcome with its own consequence and keeps
> prompting the remaining kinds after an unmet one, and
> `LocationTrackingService` names a permanently denied battery exemption instead of
> discarding the answer.
>
> Tests: `PermissionRequestPolicyTests` covers granted-with-rationale, unsupported,
> denial-with-rationale, first denial without rationale (must stay `Denied`) and
> denial without rationale after an earlier request (`PermanentlyDenied`), plus the
> kind/outcome mapping of all four results. `PortStartupPermissionsTests` runs the
> unmet `Bluetooth` case as a theory over `Denied`, `PermanentlyDenied` and
> `Unavailable` and proves the audio prompt still follows.
>
> Still open for this item: one queued OS request at a time with request-ID matching
> instead of the activity's per-kind completion sources, cancelling a pending
> request on activity teardown, rechecking grants on resume, rationale UI and the
> settings action wired to an operator-visible control, background-location
> explanation, and the clean-install/denial/revoke/settings-return/recreation/
> slow-interaction acceptance runs on a device.

**R5.2 Foreground service**

- Return a result or throw on rejected start.
- Make keep-alive state consume that result.
- Verify notification publication, modem readiness, and first I/O as separate states.
- Select eligible Android foreground service types for the work in progress;
  connected-device is not a blanket exemption for a generic Internet socket.
  Map start rejection to a recoverable outcome; roll back requested leases.
- Route app and notification Stop through the same app-session stop. Disarm first,
  cancel retries and location work, release foreground/wake leases, and ensure
  reconnect cannot start a new generation after stop.
- Bound wakelock use and recheck battery optimization.
- Decide reboot behavior: explicit re-arm is the current product requirement.

> **Status: single stop path implemented (2026-11-26).**
> `IApplicationSessionControl` (`Services/Lifecycle/ApplicationSessionControl.cs`)
> is the one stop an in-app exit and the notification action share. It serializes
> concurrent stops and runs five ordered steps: `SessionCoordinator.StopAsync`
> (disarms the intent and disconnects), `IPortService.StopAllPortsAsync` (sweeps
> sessions a send started outside the armed set), `StopTracking`, then an explicit
> release of the location and port keep-alive reasons. A step failure is reported
> with its step name and the later steps still run, so one wedged transport cannot
> leave location polling or the notification behind.
>
> `SessionCoordinator.ReportPortFaultAsync` now ignores a fault that arrives while
> the session is stopped and disarmed, so a late transport callback cannot move the
> lifecycle back to `Degraded` or reopen a reconnect loop.
>
> Tests: `ApplicationSessionControlTests` proves the stop disarms the intent and a
> late fault cannot reconnect, sweeps sessions the coordinator did not arm, cancels
> location work and releases both keep-alive reasons, reports a failed step while
> still running the rest, serializes concurrent stops, and completes every step when
> nothing is running.
>
> The notification's stop action is an operator-requested **application close**:
> the service runs the shared stop, gives up foreground status, stops itself and
> finishes every activity. The in-app exit (`MainActivity.OnRequestAppExit`) runs
> the same stop before it finishes the affinity. A platform foreground-service
> timeout uses the same stop but keeps the UI available, because a type limit ends
> the background session rather than the application.
>
> The service no longer acquires a partial wake lock without a timeout. `OnCreate`
> creates the lock but holds nothing; each start command that brings work in arms a
> bounded lease (`WakeLeasePolicy.LeaseMilliseconds`, ten minutes) and the stop
> paths and `OnDestroy` release it. A held lease is never extended, because the
> platform counts acquisitions and a second acquire would leave a reference the
> matching single release cannot clear. A foreground service already keeps the
> process alive, so the bound does not stop the session: network, Bluetooth and
> location callbacks wake the CPU, and a service nobody asks to work is asleep
> instead of pinning the CPU until the battery dies. Architecture rule A016 rejects
> `Acquire()` without a timeout in the Android project, so the pattern cannot come
> back unnoticed.
>
> The battery-optimization exemption is re-read on each service start rather than
> remembered, and one warning per service instance records when the application is
> still optimized, because a foreground service is not exempt from Doze and the
> system may defer or stop the work the session depends on.
>
> Tests: `WakeLeasePolicyTests` proves an idle session with no lease arms it, a
> stopped session releases it, a lease that already matches is left alone (no extra
> platform reference), and the declared lease is positive and bounded to fifteen
> minutes. Temporarily replacing the bounded call with `Acquire()` makes the
> real-tree A016 scan fail with
> `[A016 AetherAprs.Android/Services/Platform/AndroidForegroundService.cs -> .Acquire()]`,
> so the rule is load-bearing.
>
> Remaining for this item: the native acceptance check for notification
> publication, modem readiness and first I/O as separate states, and rolling back
> requested leases when a foreground start is rejected (the current start rejection
> reports `Degraded` but keeps the held reasons).

**R5.3 Hardware transports**

- Add a BLE test double for MTU, chunk boundaries, busy statuses, and late callbacks.
- Add an RFCOMM double for non-cancellable blocking reads.
- Validate BLE loss/reconnect and Classic stop on physical devices.
- Bound scanner channels and filter advertisements by supported service.
- Add native adapter tests for Keystore key loss, audio short writes, audio route
  changes, compass re-entry, and notification icon/action behavior.

**R5.4 Android logging and security**

- Route `MainActivity` and application logging through `ILogger`.
- Catch Keystore failure per port; mark credentials unavailable and continue loading
  unrelated ports.
- Verify exported activity/deep-link inputs and notification privacy.
- Document the `neverForLocation` rationale or remove the flag.

> **Status: lost credentials no longer hide the port list, the operator can repair
> them in the port editor, Android logging goes through `ILogger`, and the exported
> surface is declared and validated (2026-11-26); the `neverForLocation` decision
> still open.**
> `PortRegistry` restored the APRS-IS passcode while mapping every record, so one
> unreadable secret threw out of `LoadAsync` and the operator saw no ports at all.
> Mapping now catches `SecretUnavailableException` per port: the port keeps its name,
> enablement and type settings, its passcode drops to the read-only default, and
> `PortConfig.CredentialsUnavailable` marks it for the operator to fix. Other ports
> load normally. The flag is runtime state (never persisted) and `Clone` keeps it.
>
> The Android adapter now throws `SecretUnavailableException` instead of a generic
> `InvalidOperationException` for both directions, so a lost Keystore key cannot be
> confused with a programming error.
>
> The port editor turns that state into something the operator can act on.
> `AddEditPortViewModel.HasUnavailableCredentials` drives a localized notice above
> the passcode field, and the field opens empty instead of showing the read-only
> default, so re-entry is an explicit action rather than an invisible overwrite.
> Saving without re-entering keeps the port receive-only: `BuildConfig` re-applies
> the read-only passcode and keeps `CredentialsUnavailable` set, because persisting
> an empty passcode would look configured while every login is rejected. Entering a
> passcode clears the marker and restores transmit.
>
> Tests: `PortRegistryCredentialTests` seeds two APRS-IS ports and an RF port with a
> protector that fails for exactly one secret, and asserts the other two load intact
> while the affected port is marked, keeps its settings and reports the read-only
> passcode. Removing the per-port handling makes the same test fail with the raw
> exception escaping `LoadAsync`, so the regression is real. A second case proves a
> fully readable set marks no port, and a clone keeps the marker.
> `AddEditPortViewModelTests` covers the editor side: a marked port opens with the
> notice and an empty passcode, a readable port opens without it, saving without
> re-entry keeps the read-only passcode and the marker, and entering a passcode
> clears both. Reverting the `BuildConfig` guard makes
> `BuildConfig_WhenThePasscodeWasNotReentered_KeepsThePortReceiveOnly` fail, so that
> case is a regression test too.
>
> Logging no longer reaches a bare platform call. `MainActivity`, the Android
> application class, the foreground service and the permission adapter all write
> through `ILogger` categories; `ApplicationLog` resolves the container's factory
> for those framework-created objects and falls back to
> `AndroidLogcatLoggerProvider` before the container exists, so bootstrap failures
> still reach logcat from one adapter instead of dozens of call sites. Repeated
> step-by-step permission chatter moved to debug and the startup burst reports one
> bounded summary, so volume drops without losing the outcome.
>
> Tests: architecture rule A013 rejects any `Android.Util.Log` outside the logcat
> provider, with one case for a rejected call site and one for the permitted
> provider. `PermissionLoggingTests` drives the permission reporting path with a
> capturing logger over ports carrying a distinctive server, passcode, filter and
> name, and asserts the single warning names only the permission and its
> consequence while neither the rendered message nor the structured state contains
> any of those values; a second case asserts a fully granted launch logs nothing.
>
> Exported input no longer reaches a decision point unchecked. `MainActivity`
> declares `Exported = true`, reads the conversation extra only from a
> main/launcher intent and passes it through `ConversationDeepLink`, which bounds
> the value and rejects anything that is not a plain address instead of trimming it
> into shape; the extra is removed once consumed. The foreground service declares
> `Exported = false` and masks the requested keep-alive types to the two it
> advertises, so a stale or foreign intent cannot widen them. Notification and
> service pending intents stay `Immutable`.
>
> Tests: architecture rules A014 and A015 reject a component without `Exported` and
> a pending intent without `Immutable`; `ConversationDeepLinkTests` feeds valid
> tactical addresses and same-shape rejects (spaces, path separators, shell
> metacharacters, over-length, SSID 16) into the deep-link parser.
>
> Still open for this item: the `neverForLocation` decision. The exported surface
> has architecture coverage but no device run: nothing here proves how the platform
> delivers a malformed intent to the real activity.

Exit: permission, disconnect, notification stop, and route change behavior have
focused acceptance checks. When a behavior depends on native APIs, use a short
manual check for that behavior; device matrices and timed campaigns are not gates.

### R6 — Operator features and concrete acceptance

These packages preserve the product work from the design sheet. Implement one
vertical slice at a time. Start with the named source owners, change the contract
and its callers together, and test the observable result. The dependencies are
for implementation, not release gates.

| ID / prerequisite | Starting points and change | Acceptance |
|---|---|---|
| R6.1 Station setup / R3.1, R1.3 | `SettingsViewModel`, `AddEditPortViewModel`, `PortRecordMapper`, `AprsIsModem`. Validate callsign/SSID, symbol, comment, path, server/port and filter in field context. Keep one active identity; allow anonymous APRS-IS receive and require verified login for TX. Add editable default server and explicit TLS mode with normal certificate/hostname validation; never silently downgrade. Preserve an explicit legacy plaintext choice. | Invalid edits leave the old config live; unauthenticated/rejected login stays RX-only; simulation covers successful login, TLS rejection, and reconnect. |
| R6.2 Message workflow / R1.2, R2.2 | `MessageService`, message ViewModels, notification adapter. Persist before send; show pending, sending, acknowledged, rejected, expired, and failed with route/retry detail. Use 3 retries after the initial attempt and 15-minute default expiry as the former design's **target**, not today's behavior; preserve explicit operator values. Cap attempts across restarts, select an eligible route or leave queued, deduplicate inbound across ports without losing route counts. Live ACKs remain best effort and require authorization. Hide notification text by default. | Inject clock and routes: no route, multiple RX copies, partial TX, duplicate/late ACK/REJ, expiry, restart disarmed, and restart then re-arm. Assert one logical conversation item and no replay of terminal work. |
| R6.3 Position and beaconing / R1.3, R2.2, R3.3 | `BeaconService`, beacon decision/transmit code, `ILocationService`, `HomeViewModel`. Give the application a single location coordinator with purposes for map and beacon; snapshots carry sample time and optional accuracy. Separate decision from confirmed route outcome. Ship configurable Walk/Drive/Custom; former design targets Walk 5 min/100 m and Drive 2 min/500 m; inspect actual defaults and preserve explicit user config. Preview encoded position/path/symbol. Coalesce queued position work per identity/profile, require freshness at send, and expire stale work visibly. | Fake clock and location prove manual/periodic behavior, bad fix suppression, partial send, no update of last successful TX on failure, and one location worker across navigation. |
| R6.4 Offline map and packet explorer / R1.2, R4 | `ReceivedBeaconsMapLayerService`, map views, history/search ViewModels, offline downloader/MBTiles source. Bound query and layer work; use stable paging `(timestamp, id)` and cancel superseded search/render. Preserve selected and own station through updates; support trails, age/port filters, clustering and follow mode. Show receipt/protocol times separately with raw/path/port provenance plus decoded weather, telemetry, objects/items and unknown-frame detail. Validate package metadata/bounds, distinguish missing tile from corrupt package, honor provider licenses, attribution, download policy and free-space limits. Use temporary downloads; resume only when remote identity/range support is verifiable. | In-process dataset with paging ties and rapid filter switches; a small offline fixture proves zero network calls, attribution, missing tile, cancellation, corrupt file and storage-quota handling. |
| R6.5 Forwarding / R2.2, R2.3 | Digipeat/gateway policy and settings; use independent direction switches, default off, explicit operator enablement and arm, per-port participation and validated aliases. Keep path mutation in pure policy, preserve provenance, and enforce loop checks, scoped duplicate cache, bounded age, rate limits and Internet-to-RF allowlists. Route all actual sends through the same TX policy. Never replay forwarding after reconnect/restart. | Given packet, route and clock, golden tests verify accept/reject reasons, alias/repeated bits, duplicate paths across ports, third-party encapsulation, loop prevention, and no stale RF send. Expose bounded counters and route/reason history without logging payloads. |
| R6.6 Transport setup and audio / R2.1, R5 | Port editor, `KissStreamFactory`, Android Bluetooth adapters, AFSK/audio/PTT. Distinguish generic Classic SPP, supported BLE profiles (including the compatible LilyGO T-Echo Plus firmware target), and KISS TCP. Report connection stage, GATT service/characteristics, CCCD, write mode, negotiated usable payload and reconnect outcome. For AFSK show route, sample rate, level, calibration, RX/TX state and audio focus; separate VOX from physical PTT capability. Revalidate route and authorization before TX after a route switch. | Fake connector covers each negotiation failure; GATT tests cover chunk/retry and stale callbacks, audio tests cover short writes and focus/route loss, one brief device check exercises each changed native behavior. Keep unsupported routes visibly unavailable. |
| R6.7 Local data and support / R1, R3 | `AppSavedDataInitializer`, retention, configuration providers, diagnostics, Android backup resources. Private DB/log/cache paths, configurable packet retention (30-day target), indexed bounded batch cleanup, disk-pressure signaling and preserve-on-migration failure. Independently clear packet history, messages, pending outbound work, offline packages and diagnostics, with confirmation and results. Use an explicit versioned redacted report with preview, no upload, no history, no secrets or precise coordinates. Keep useful version/platform/settings-schema and bounded failure counters. | Disk-backed migration/reopen with synthetic data, low-space and failed-upgrade cases, clear actions preserving unrelated data, redaction tests for logs/report and backup rule review. A missing key blocks affected TX without hiding unrelated ports. |
| R6.8 Presentation finish / R4 | ViewLocator, dialogs, styles, strings and packet converters. Replace misleading errors and placeholders, use domain success/failure states, keep en/pt resources together, and review labels, dynamic text, contrast and portrait/landscape. | Typed-binding and localization checks, targeted navigation/dialog tests, brief accessibility check on the touched screen. |

Product scope: Android 13+, local identity, no mandatory cloud account, no analytics
upload. Offline means local map/history and available RF/GPS work without Internet;
it does not imply APRS-IS is available. Defer public-service workflows, bulletin
authoring expansion, historical telemetry graphs/alerts, export/import/backup, cloud
sync, extra distributions, USB serial, and additional radio accessories. Preserve
existing functionality unless a documented safety decision changes it.

### R6.9 — Small fixes that can ship independently

Pick these by behavior, not by raw audit severity. Start by reproducing the symptom
on the current branch; use the source identifiers and historical IDs to find renamed
code. If the symptom no longer reproduces, record the test or source reason and
close the ticket without refactoring unrelated code.

| Work | Change and regression |
|---|---|
| Port editor and numbering (`m-54`, `m-56`, `m-57`, `m-59`) | Use common port validation for create/update and choose the next number from all relevant configured port types. Keep a save error out of the BLE scan status. Simulate a mixed list and denied storage; assert a unique number, visible general error, no navigation away and no mutation to the active port. |
| Search and object routing (`m-61–m-63`) | Give each search a generation and apply results, errors and `IsLoading` only for the current generation; await/catch canceled and disposed work. Select object history by object identity. Complete the older search last in a fake and verify neither spinner nor selection regresses. |
| Time and packet data (`m-15`, `m-17`, `m-47`, `V-18/V-19`) | Add explicit units at conversions, preserve repeated path flags while decrementing an SSID, preserve sparse channel slots, and use culture-specific UI formats only after UTC read. Cover encoded units, repeated flags, later telemetry channel after an empty slot, and both sides of a DST boundary. |
| Map lifetime and cache (`m-65`, `V-01`, `V-07–V-09`) | Keep private runtime map tiles apart from explicit offline downloads, dispose page-owned layers once and stop subscriptions on detach; retain shared provider ownership at application lifetime. Attach/detach/reopen and show that old callbacks do not update a disposed layer. |
| Display and binding (`V-03–V-06`, `V-14–V-17`) | Virtualize long packet collections without nesting a scrolling container that defeats virtualization; fix lost binding types with typed projections and correct localized fallback actions. Verify the actual large-list and dialog paths before deleting shared styles. |
| Diagnostics and Android affordances (`m-67–m-72`, `P-24–P-27`, `P-29`) | Remove payload-bearing hot logs, use structured safe fields, make compass start/stop idempotent, use a monochrome notification small icon, and validate external navigation targets. Handle a malformed intent without switching context or performing an action. |

> **Status: search generations and object identity implemented (2026-11-26).**
> `SearchViewModel` now numbers every query change and applies results, a logged
> error and `IsLoading` only while that generation is still current. Clearing the
> box resets immediately and supersedes an in-flight search. The fire-and-forget
> debounce task catches cancellation, disposal and any other failure, so a newer
> query can no longer fault the older one, and it no longer disposes a token source
> whose token the previous search may still be using.
>
> Tests: `SearchViewModelTests` completes the older search last in a fake and
> proves the newer spinner stays visible and its results are not replaced; a second
> test clears the query while a search is in flight and proves the late result is
> discarded. Removing the generation guard makes the first test fail with the stale
> result, so it is a real regression test rather than a restatement of the code.
>
> Still open in this row: nothing; both items are covered by tests above.

> **Status: object history selected by object identity (`m-63`).**
> A search result now carries the matched object's own name in
> `SearchResultItem.ObjectName`, and `SearchViewModel.OpenResult` opens
> `ObjectDetailsViewModel` through `IObjectDetailsViewModelFactory` for that name
> instead of opening the reporting station's packet history. The reporting station
> stays visible as the result subtitle, so "Reported by N0CALL" no longer doubles as
> the navigation key. Object and item histories are read by name
> (`GetPacketsByObjectNameAsync`), which is the APRS identity for a named report and
> is stable when a different station reports the object next.
>
> Tests: `SearchViewModelTests.OpenResult_Object_OpensObjectHistoryByIdentity` asserts
> the object factory receives `SHELTER1`, the packet-details factory is never called
> and navigation lands on the object page; the object-result search test now also
> asserts the result carries `ObjectName`. No behavioral verification exists on
> device yet, because this is a ViewModel-level navigation change.

> **Status: port editor numbering and save error implemented (2026-11-26).**
> `PortsViewModel.GetNextPortNumber` takes the highest number already embedded in
> a localized default port name across every port type, so adding a second KISS
> port no longer suggests "KISS Port 1" merely because no APRS-IS port exists, and
> a number left behind by a deleted port is not reused. Custom names carry no
> number, so the configured port count remains the lower bound.
>
> A failed save no longer writes into `BleStatusText`. `AddEditPortViewModel` has a
> dedicated `SaveErrorText`, shown above the action buttons, so a storage failure
> cannot look like a Bluetooth scan result; the message is the localized
> `ErrorPrefix` plus the failure detail.
>
> Tests: `PortsViewModelTests` covers the mixed-type list and the deleted-port gap;
> `AddEditPortViewModelTests` covers a denied save (general error set, Bluetooth
> status untouched, no navigation) and a retry that clears the previous error.

> **Status: callsign, addressee and object name types audited and separated (2026-11-26).**
> The app now states which of the three it means at each boundary. `Callsign` (two to
> six characters plus SSID) stays on AX.25 station fields only: source, destination,
> digipeater path and the operator's own station. `AprsAddress` (one to nine characters
> plus SSID) is the type for anything a message is addressed to: message, ack and
> reject destinations, bulletin names, conversation peers and the notification deep
> link. Object and item names are neither, so they are checked against the shared
> nine-character name field instead of being parsed as addresses.
>
> Fixed while auditing: a message to `APRS2SOTA` could not be reopened from its own
> notification because the deep link parsed a callsign, and templates were scoped by
> callsign, so a tactical address never found its templates. Both now use
> `AprsAddress`, and the compose field accepts the full address width (twelve
> characters with an SSID) with the hint and error text saying "callsign or address".
> `PacketDetailsViewModel`'s send-from-packet action also stopped dropping the SSID.
>
> Storage was wrong in a way that split conversations. The entity keeps the addressee
> SSID in a required byte, so "no SSID" and SSID 0 collapse to the same column, and the
> reader rebuilt `N0CALL-0` while the operator had typed `N0CALL`. `AprsAddress`
> distinguishes the two, so the reloaded message formed a second thread. The new
> `AprsAddress.FromStored` reads a stored zero back as "no SSID" (they are the same
> station on the air), and the record mapper and message history loader both use it.
>
> `AprsNameField` guards what this application writes into the nine-character object,
> item and bulletin name fields. The serializer padded with `PadRight(9)`, so a tenth
> character silently shifted the status byte and the position for every receiver;
> `PacketFactory` now rejects such a name before a packet exists.
>
> Tests: `AprsAddressTests` covers nine-character base addresses, the text-width
> constants and the stored-zero round trip; `ConversationDeepLinkTests` covers a
> tactical address, a callsign with SSID, oversized values and path-like input;
> `PacketRecordMapperTests` proves a stored `APRS2SOTA` plus SSID 0 reloads as the
> base-only address and that `APRS2SOTA` survives a write/read round trip;
> `MessageTemplateServiceTests` and `ConversationViewModelTests` prove templates are
> scoped to a tactical address; `AprsNameFieldTests` and `PacketFactoryTests` prove a
> nine-character name is accepted and a wider one is rejected by all three factories.
>
> Still open: object, item and bulletin names remain `string` fields validated at
> creation rather than a dedicated value type, `MessageTemplate.Destination` is still a
> string parsed at use, and none of this has been exercised against a live APRS-IS
> server or on a device.

### R7 — Build and distribution maintenance

`build-test.yml` already builds and runs the core suite; Android instrumentation is
build-only in CI. `release-apk.yml` produces a checksum and build provenance, not an
SBOM. Keep the existing dependency gate. In a separate release tooling change:

1. Use one pinned build-tools version for build and APK verification; emit one
   explicit signed APK path and fail if it is absent or ambiguous.
2. Pass signing secrets using a supported secret-safe channel and verify masking and
   workspace cleanup. Validate tag/version-code inputs for collisions and check
   release source before signing.
3. Pin external actions where practical. If publishing an SBOM, generate it from
   the actual resolved build, publish it and verify its association with the APK;
   keep the claim out of docs until the workflow really does it.
4. Preserve GitHub Releases as the distribution channel and keep artifact checksum,
   signature, and upgrade behavior verifiable. Use release notes for version history.

> **Status: platform baseline updated (2026-11-26).** The solution targets
> `net11.0` and `net11.0-android` on SDK 11.0.100, with Avalonia 12.2.0 (plus
> `Xaml.Behaviors.Avalonia` 12.2.0 and `Avalonia.Controls.DataGrid` 12.1.2),
> EF Core, `Microsoft.Data.Sqlite` and `Microsoft.Extensions` at 11.0.0, and
> Material.Avalonia 3.22.0. Two .NET 11 Android changes were required: AOT is now
> ReadyToRun, so `RunAOTCompilation` is rejected (XA1044) and
> `PublishReadyToRun` is the switch, and BLE connects use the
> settings/executor overload on API 37 while keeping the legacy overload on API
> 33–36.
>
> The explicit `SQLitePCLRaw.lib.e_sqlite3.android` 2.1.x reference is gone. It
> was never needed: `Microsoft.Data.Sqlite` 11 brings SQLitePCLRaw 3.x, whose
> native library already ships in the APK (verified for arm64-v8a and x86_64),
> and pinning the 2.x lib as well put two different builds of `libe_sqlite3.so`
> in the same APK.
>
> Trimming is on for Release. `AetherAprs` declares `IsTrimmable`, so the trim
> analyzer runs on every build, and the Android project publishes Release with
> `PublishTrimmed=true`. Every hazard the earlier publish reported was removed
> rather than silenced: the settings snapshot no longer models `Logging` (the
> host logging configuration owns that section, so a save now carries the node
> from disk over verbatim), the settings file, session snapshot and beacon outbox
> serialize through source-generated `JsonSerializerContext`s, the support report
> builds its JSON node graph explicitly instead of serializing anonymous types,
> `MaxLengthAttribute` was replaced by a reflection-free
> `MaxTextLengthAttribute`, and the per-key configuration binding is rewritten by
> the binding generator. Three narrow suppressions remain, each with its
> reflective targets statically rooted and the reasoning written in code:
> `AppDbContext` (EF Core 11 still annotates `DbContext` construction),
> `ViewModelBase`/`SettingsViewModel` (the MVVM validator inspects attributes on
> properties the views bind), and the lazy-query guard in `SqliteRuntimeReader`
> (interface identity only, no member access). ILLink additionally reports IL2035
> because Material.Avalonia's style toolkit annotates an optional TreeDataGrid
> package this application never references; that single code is suppressed for
> the Android project instead of adding an unused dependency.

## Implementation handoffs

| Handoff | First change | Must be true before the next slice |
|---|---|---|
| R0 → R1.1/R1.2 | Inventory every context factory, mutation, migration and connection option; include raw SQL and mapper side effects. Introduce infrastructure tests before moving callers. | Writer is injectable and running only after bootstrap; tests prove queue policy and reader visibility using a temporary on-disk DB. |
| R1.2 → R1.3/R6.2 | Move history insertion and publication first; move messaging queries next; add outbox schema/backfill last. Keep existing read UI functional through a typed projection. | A throwing notification subscriber cannot change successful commit into failure; old DB upgrades without automatic TX; protocol number rollover has tests. |
| R1.3 → R2.2 | Extract load/change/remove/enable commands from `PortService` and load/save from `BeaconService`; reconcile after commit. | A failed DB write leaves the last committed snapshot and running config intact. A committed edit with failed restart shows degraded and the persisted intent. |
| R2.1 → R2.2 → R2.3 | Extract construction first, then generation/lease ownership, then queues. Keep a façade until all producers call the same send entry point. | A stale callback cannot update a new generation; send/stop, stop-all, timeout and shared hardware tests complete deterministically. |
| R3.1/R3.2 → R3.3 → R5 | Remove remaining shared settings mutations and bound/coalesce telemetry; introduce a single start/stop coordinator used by app and notification. | Opening a page or recreating an Activity cannot start duplicate work, restart alone cannot arm TX, and rejected foreground startup is visible. |
| R1/R2/R3 → R4/R6 | Keep view changes separate from domain migrations; adapt through immutable projections. Map/render and dialog code stay in presentation. | Plain application host starts without UI; UI tests assert current values after cancellation/navigation, not just mock invocation. |

Build and run the relevant unit/integration tests for each implementation slice.
Use virtual time for retries, leases, retention and expiry. Use temporary SQLite
for atomicity, migration and restart behavior. Build Android when changing its
adapters. Targeted native checks are useful for features that depend on OS or
hardware semantics. There is no timed soak, field trial, beta milestone, or
coverage-percentage gate in this roadmap.

## Audit crosswalk

Status below is based on the reviewed source, including work in progress. “Routed”
means the package owns triage and reproduction; it does not assert the original
report is valid or open. Historical locations and details are in the [audit].

| Audit IDs | Review outcome and destination |
|---|---|
| C-01, C-02, C-03 | Duplicate starts now gated, reconnect no longer writes enablement, and per-packet file rewriting was reduced. Remaining send/lifecycle races and fan-out: R2.2 and R3.2. |
| C-04, C-05 | Atomic insertion for the current message key, store-before-publish, and post-commit observer isolation are implemented. Independent durable identity, wire-number reuse, and expiry remain: R1.2/R6.2. |
| C-06, C-07 | Separate bounded forwarding and receive-drop counts exist. Audit silent-RX-drop wording is stale; validate forward drops and worker shutdown: R2.3. |
| C-08, P-07/P-31 | Fail-closed default in working tree; check inline fallbacks, key-loss isolation, restored/old credentials and unsupported-host behavior: R1.3/R5.4/R6.7. |
| C-09, C-10 | Newer-schema guard and clone omission addressed; mutation escape, recovery and backup retention remain: R3.1. |
| C-11–C-14, P-01–P-04 | Source implements BLE chunk/pacing, close-first Classic stop, changed service types and direct Stop action. They do not prove negotiated readiness, OS eligibility or teardown/re-arm semantics: R2.2/R5/R6.6. |
| C-15, V-19 | UTC read/write conversion added for three mapped fields in working tree. Check legacy DB and local display at DST/offset boundaries; distinguish receipt and protocol time: R1.2/R6.4. |
| M-01–M-05 | R1.3/R2: split responsibilities, commit intent first, centralize send authorization and track foreground leases. Recheck current send fix before editing. |
| M-06, M-15 | Consolidation landed; verify configurable packet destination/path and callsign/SSID equivalence against wire vectors: R6.1/R6.5. |
| M-07–M-10 | R1.2/R4/R6.2–R6.3. Beacon disposal and asynchronous file queue changes are partial; durable queue, confirmed-send accounting and UI ownership remain. |
| M-11–M-14 | R2.1/R2.2/R6.1/R6.5. Extract parser decisions only with vectors; distinguish connection/login/readiness; no split merely to meet a line count. |
| M-16–M-20 | R3.3/R4/R6.7/R6.8. Composition, startup, report lifetime and translated UI need separate work; recount keys rather than trusting dated counts. |
| M-21/M-22 | Vulnerability gate and dead project entry addressed; SBOM absent; no numerical coverage gate or duplicated directory inventory. R7 and consolidated docs. |
| m-01–m-12, m-13–m-22 | R3.1 and R6.1/R6.3/R6.4/R6.5: recheck dead APIs and deduplicate validation, paths, callsign, persistence helpers and packet mapping. Preserve SSID equality and repeated-path semantics; use explicit units, nullable unknown accuracy, and explicit transport provenance. |
| m-23–m-36 | R1–R3/R5: route lifecycle races, swallowed/unobserved work, cancellation, semaphore and worker disposal. Optional test-only constructors/loggers are cleanup when touching the owner. |
| m-37–m-48 | R1/R6.4/R6.7: verify connection options per connection, migration thread, index plans, sparse telemetry slots and error propagation. A serialized writer does not require row versions everywhere; consider optimistic checks only for stale user commands. Don't remodel coordinates or owned tables without need and measured queries. |
| m-49–m-66 | R3.1/R4/R6.1/R6.4/R6.8: validation, debug overlay, backup, templates, port numbering, editor errors, search cancellation generations, object navigation and theme. Tile-cache path has a working-tree fix; `m-66` was already withdrawn. |
| m-67–m-72, P-26/P-27 | Hot port payload logging reduced in working tree; recheck every producer and Android bridge, log safe event metadata and bound volume: R0.2/R5.4/R6.7. |
| m-73–m-90 | Routed to R7. Evaluate restore determinism, signing and build settings before adding packages/analyzers; use frameworks' defaults where proven. The dead compile entry is gone. Separate contributor, security and changelog files are not required to preserve their useful contents. |
| T-01–T-15 | R0.3 and owning slice: isolate process-wide culture; assert observer failure isolation; replace vacuous checks and sleeps with behavioral tests/fake time; add map projection coverage; do not confuse instrumentation build with execution. Temp cleanup, orphan file and selected async tests already improved; verify before repeating. |

> **Status: two intermittent failures removed (2026-11-26).**
> `AprsIsModem.SendAsync` snapshotted the stream writer before awaiting the login
> response. `Start()` returns before authentication, and the writer is only assigned
> after the login line is written, so a send issued in that window waited, then
> rejected a connection that had just authenticated ("Modem is not connected"). The
> send now treats readiness as the gate and reads the writer under the connection lock
> after the await, so the writer and version always belong to the authenticated
> connection. A pending readiness source is also reused by a connect attempt instead
> of being replaced, so a waiter created before the connect is not stranded on a
> source nobody completes.
>
> `PortsViewModelTests` built "KISS Port 1"/"KISS Port 3" from an English literal while
> `PortsViewModel` derives the number from the localized default name. Because
> `TestFixtureBase` applies culture process-wide, a concurrently running fixture could
> change it and the numbers silently fell back to the port count. The tests now build
> the names with `Strings.Format("DefaultKissPortName", n)`, so they assert numbering
> in whatever culture is current.
>
> Tests: `AprsIsModemTests.SendAsync_BeforeLoginCompletes_WaitsForAuthenticationAndTransmits`
> proves a send issued before the login response stays pending and then transmits.
> Fourteen consecutive full-suite runs passed after both changes (2381 tests each);
> the port-number test failed roughly once in eight runs before it.
>
> Still open here: the remaining T items, and the process-wide culture that
> `TestFixtureBase`/`UiCulture.Apply` still share between parallel tests.
| P-05/P-06/P-08/P-10/P-16/P-17 | R5.1/R5.2: permission answers now carry granted/denied/permanently denied/unavailable, with a settings-recovery call; the queued request broker, rationale UI and repeated on-demand prompt remain. Foreground location is requested; all-time settings flow is distinct. Rejected FGS can look successful; fix bounded permission/wake ownership. |
| P-09/P-11–P-15/P-18/P-19 | R3.3/R5/R6.6: recheck context startup and telemetry boundaries; JNI disposal/activity recreation, GATT failures and Classic timeout have source changes. Verify late callbacks and start/abort race. |
| P-20–P-25/P-29/P-30/P-32–P-34 | R5/R6.6/R6.8: bounded BLE scan, async audio stop, short writes, actual PTT capability, idempotent compass, monochrome icon, exported input validation, design-time adapters, and dead SDK branches. P-33 cosmetic fix landed; `neverForLocation` is compatible with separate GPS if scan results do not derive location. |
| P-28 | No boot receiver is consistent with explicit run-scoped TX arm; the real gap is automatic startup of saved routes conflating enabled and armed. R3.3. |
| V-01–V-19 | R4/R6.4/R6.8: separate actual policy from legitimate presentation glue; remove duplicate location start (working-tree fix), fix map detach/ownership, virtualize history, validate bindings/fallback and culture. V-11 requires checking actual SDK defaults, V-12 compile item removed, V-13/V-17 fixes landed. Confirm the audit's overlapping converter and time issues individually. |

## Documentation decisions

`README.md` owns install, use, build, private reporting and links. `AGENTS.md`
owns current source conventions and migration rules. This roadmap owns the target,
feature acceptance and priorities. Keep upstream asset notices. The audit,
design sheet, wiki, duplicated folder map, security/contributor pages and changelog
are retired from the live tree; historical claims and the field smoke record stay
in Git. The historical design checkboxes overstated SBOM, outbox expiry, background
recovery, backup evidence, BLE compatibility, and instrumentation. Do not carry
those completion claims into the README or treat absence of a timed test as a defect.

## Definition of architectural done

The architecture work is complete only when all of these are true:

- Database writes have one serialized owner and database reads have explicit query
  services; source scans enforce the boundary.
- Packet publication follows durable insertion; outbound work has explicit durability,
  retry, expiry, and terminal-state semantics.
- User intent, runtime session state, platform readiness, and first I/O are separate.
- A modem cannot be sent to while it is being stopped or disposed.
- Every long-lived worker has cancellation, bounded input, observable failure, and a
  shutdown deadline.
- Application services run without Avalonia or Android runtime objects.
- ViewModels own UI collections and navigation state.
- Unsupported platform features fail honestly and are visible to the operator.
- Android permissions, background location, foreground service rejection, Bluetooth,
  audio, Keystore recovery, and notification actions have tested contracts.
- Product documents distinguish requirement, implementation, and evidence.
- Existing CI continues to verify builds, tests, dependencies and license metadata.
  Add focused contract checks with the owning slice. Verify signing and artifacts
  when changing distribution.
