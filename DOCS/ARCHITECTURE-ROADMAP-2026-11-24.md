<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC-BY-SA-4.0
-->

# AetherAprs architecture roadmap

**Review date:** 2026-11-24 (open work only; refreshed 2026-11-26)

**Purpose:** the live backlog. It lists only work that is still outstanding, plus the
rules and acceptance criteria that the remaining work must satisfy. Completed items
are removed rather than ticked; Git history and the source are the record of what is
done. Historical audit identifiers are not a checklist and are not repeated here.

## Target

AetherAprs keeps strong protocol primitives and a substantial suite. The remaining
architectural risk is ownership at subsystem seams, and the end state is:

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

The target does not require a network backend. It requires one owner per mutable
resource, one direction per dependency, explicit durability and lifecycle contracts,
and tests that model Android and hardware failure modes.

The ownership rule the remaining work must keep true:

> No feature service creates `AppDbContext` directly. One runtime writer serializes
> all SQLite mutations. One logical reader owns independent short-lived query
> contexts. Features see typed command/query capabilities. Migration and seeding run
> under exclusive bootstrap ownership before either runtime pipeline starts.

The boundary scan in `AetherAprs.Tests/Architecture/` enforces this and the related
rules; a new violation fails `dotnet test`.

## Target architecture

### Assembly and dependency layers

Domain, infrastructure, UI and platform composition live in one assembly today. The
enforced rule is dependency direction; the physical split is later work:

```text
Presentation               → Application use cases/read projections → Domain
Infrastructure and Android → Application-owned ports and domain types
Composition root           → Presentation, application and concrete adapters
```

Application and domain code must not reference Avalonia controls,
`Dispatcher.UIThread`, `ObservableCollection`, Android classes, or Mapsui layers.
Presentation services may adapt application state into UI objects.

### Persistence

The writer/reader split is in place. What remains is the domain-facing message
boundary:

- `IMessageCommands` and `IMessageQueries`: one logical outbound message, attempt
  updates, ACK/REJ transitions, expiry and recovery. The outbox is separate from
  packet history; queries join explicitly when a feature needs both.

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

Invariants the remaining work must preserve: publication only after durable
insertion; explicit queue capacity and drop policy per transport class; bounded retry
of transient failures only, with ambiguous commits resolved by idempotency key or
reported as indeterminate; digipeating never blocks persistence; subscriber failures
cannot abort the consumer; drop counts and queue depth are diagnostic state; packet
timestamps are UTC instants at the domain boundary.

### Outbound transmission

```text
producer → optional durable work item → authorization and route selection
         → session generation + resource lease → modem
         → per-port outcome → history and/or durable retry state
```

The pipeline returns per-port results and does not hide partial success. Transport
readiness, RX enablement, per-route TX enablement and the operator's TX arm are
separate concerns, and authorization is rechecked at send time.

### Settings and configuration

Readers must see an immutable snapshot:

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
the immutable reference → raise change event. A newer on-disk schema is read-only and
is never re-stamped as an older schema. Validation reports all invalid fields.

### UI and application state

ViewModels own `ObservableCollection` instances and navigation state. Services expose
immutable projections and events or channels. UI dispatch is an injected interface.
Views wire controls and dialogs; they do not start background services, access the
database, or decide persistence order.

Startup remains an explicit state machine:

```text
resolve paths and read configuration → construct/validate application graph
→ bootstrap SQLite → run read/write pipelines → expose UI and eligible RX
→ request feature permissions on use → TX only after explicit operator arm
```

No startup task is silently discarded. Framework `async void` entry points catch and
record failures. Background workers have cancellation, bounded queues and a shutdown
deadline.

## Open work

Each item is sized so one agent can implement it without deciding architecture again.
Read the relevant source and tests first, add a regression test, and report build and
test evidence. A source-only change is partial where the observable behaviour is not
demonstrated.

### R0.3 Test harness

- Add a fake clock and use one captured instant per assertion; replace real delays in
  retry, lease, retention and expiry tests with virtual time.
- Build narrow fakes at the actual ports (clock, transport, foreground state, UI
  dispatch) and use gates/tasks instead of sleeps to control races.
- Replace remaining interaction-only tests with assertions on the subject's state.
- Stabilize `AprsIsModemTests.SendAsync_RfReceivedPacket_UsesQarConstructorWithPath`:
  writing and flushing the simulated server's login reply does not prove the modem
  consumed it. Await the modem's authenticated-ready signal with a test deadline
  before asserting the RF-to-IS wire format.

### R1.2 Message outbox and durable identity

Files: `PacketStorageService.cs`, `PacketQueryService.cs`, `MessageService.cs`,
message entities and mappers.

- Move delivery and retry into outbox commands; notify subscribers after commit and
  isolate each subscriber's failure from the command result and from other
  subscribers.
- Return a stable logical message identity from first insertion. Use a
  caller-generated GUID for identity across enqueue retries and restart; reserve the
  limited wire number atomically while the message is active, and correlate using
  local identity, normalized peer identity and number.
- Retain a bounded reuse guard for late ACKs. When no wire number is available, show a
  blocked/queued state instead of reusing an active number.
- Migrate legacy rows with a deterministic merge and a safe fallback for ambiguous
  ownership; preserve history and never replay an uncertain legacy send.
- Make terminal rows eligible for cleanup; pending rows require expiry.

Acceptance:

- Five concurrent sends for one logical message create one durable message record.
- Attempt count increments on that record.
- ACK/REJ matches local and normalized peer identity plus number; duplicate and late
  outcomes never regress terminal state. A number becomes reusable only after the
  reservation and reuse guard expire.
- A restart restores only unexpired pending work.
- No feature service creates `AppDbContext`.

### R3.3 On-demand feature permissions

- Request Bluetooth and audio permission when the feature is used, not only during the
  startup burst, so an operator who adds a port later is prompted at that point.
- Surface a location permission answered through the location service's own dialog in
  the same operator-visible attention banner that the startup requests already use.

### R4 Application/UI separation

- Inject `IUiDispatcher`; remove remaining Avalonia calls from application services.
- Move conversation collections and active-conversation navigation state to ViewModels.
- Extract map interaction controllers from page code-behind.
- Replace stale collection converters with ViewModel `HasItems` properties.
- Virtualize unbounded lists and add paging to details/search.
- Move theme application behind `IThemeService`.
- Add an architecture test for assembly references before physically splitting projects.

Exit: application services can run in a plain test host with no Avalonia application.

### R5.1 Permission acceptance

- Run the clean-install, denial, revoke, settings-return, activity-recreation and
  slow-interaction checks on a device.

### R5.2 Foreground readiness acceptance

- Verify notification publication, modem readiness and first I/O as separate states on
  a device, including a rejected foreground start and the recovery after it.

### R5.3 Native adapter tests and device checks

- Add native adapter tests for Keystore key loss, audio short writes, audio route
  changes, compass re-entry, and notification icon/action behaviour.
- Validate BLE loss/reconnect and Classic stop on physical devices.
- Run a device check for the exported surface and the `neverForLocation` scan claim.

### R6 Operator features

Implement one vertical slice at a time. Start with the named source owners, change the
contract and its callers together, and test the observable result.

| ID / prerequisite | Starting points and change | Acceptance |
|---|---|---|
| R6.1 Station setup / R3.1, R1.3 | `SettingsViewModel`, `AddEditPortViewModel`, `PortRecordMapper`, `AprsIsModem`. Validate callsign/SSID, symbol, comment, path, server/port and filter in field context. Keep one active identity; allow anonymous APRS-IS receive and require verified login for TX. Add editable default server and explicit TLS mode with normal certificate/hostname validation; never silently downgrade. Preserve an explicit legacy plaintext choice. | Invalid edits leave the old config live; unauthenticated/rejected login stays RX-only; simulation covers successful login, TLS rejection and reconnect. |
| R6.2 Message workflow / R1.2, R2.2 | `MessageService`, message ViewModels, notification adapter. Persist before send; show pending, sending, acknowledged, rejected, expired and failed with route/retry detail. Use 3 retries after the initial attempt and 15-minute default expiry as the target, not today's behaviour; preserve explicit operator values. Cap attempts across restarts, select an eligible route or leave queued, deduplicate inbound across ports without losing route counts. Live ACKs remain best effort and require authorization. Hide notification text by default. | Inject clock and routes: no route, multiple RX copies, partial TX, duplicate/late ACK/REJ, expiry, restart disarmed, and restart then re-arm. Assert one logical conversation item and no replay of terminal work. |
| R6.3 Position and beaconing / R1.3, R2.2, R3.3 | `BeaconService`, beacon decision/transmit code, `ILocationService`, `HomeViewModel`. Give the application a single location coordinator with purposes for map and beacon; snapshots carry sample time and optional accuracy. Separate decision from confirmed route outcome. Ship configurable Walk/Drive/Custom (targets Walk 5 min/100 m, Drive 2 min/500 m); inspect actual defaults and preserve explicit user config. Preview encoded position/path/symbol. Coalesce queued position work per identity/profile, require freshness at send, and expire stale work visibly. | Fake clock and location prove manual/periodic behaviour, bad-fix suppression, partial send, no update of last successful TX on failure, and one location worker across navigation. |
| R6.4 Offline map and packet explorer / R1.2, R4 | `ReceivedBeaconsMapLayerService`, map views, history/search ViewModels, offline downloader/MBTiles source. Bound query and layer work; use stable paging `(timestamp, id)` and cancel superseded search/render. Preserve selected and own station through updates; support trails, age/port filters, clustering and follow mode. Show receipt/protocol times separately with raw/path/port provenance plus decoded weather, telemetry, objects/items and unknown-frame detail. Validate package metadata/bounds, distinguish a missing tile from a corrupt package, honour provider licenses, attribution, download policy and free-space limits. Use temporary downloads; resume only when remote identity/range support is verifiable. | In-process dataset with paging ties and rapid filter switches; a small offline fixture proves zero network calls, attribution, missing tile, cancellation, corrupt file and storage-quota handling. |
| R6.5 Forwarding / R2.2, R2.3 | Digipeat/gateway policy and settings; independent direction switches, default off, explicit operator enablement and arm, per-port participation and validated aliases. Keep path mutation in pure policy, preserve provenance, and enforce loop checks, scoped duplicate cache, bounded age, rate limits and Internet-to-RF allowlists. Route all sends through the same TX policy. Never replay forwarding after reconnect or restart. | Given packet, route and clock, golden tests verify accept/reject reasons, alias/repeated bits, duplicate paths across ports, third-party encapsulation, loop prevention, and no stale RF send. Expose bounded counters and route/reason history without logging payloads. |
| R6.6 Transport setup and audio / R2.1, R5 | Port editor, `KissStreamFactory`, Android Bluetooth adapters, AFSK/audio/PTT. Distinguish generic Classic SPP, supported BLE profiles (including the compatible LilyGO T-Echo Plus firmware target) and KISS TCP. Report connection stage, GATT service/characteristics, CCCD, write mode, negotiated usable payload and reconnect outcome. For AFSK show route, sample rate, level, calibration, RX/TX state and audio focus; separate VOX from physical PTT capability. Revalidate route and authorization before TX after a route switch. | Fake connector covers each negotiation failure; GATT tests cover chunk/retry and stale callbacks, audio tests cover short writes and focus/route loss, one brief device check exercises each changed native behaviour. Keep unsupported routes visibly unavailable. |
| R6.7 Local data and support / R1, R3 | `AppSavedDataInitializer`, retention, configuration providers, diagnostics, Android backup resources. Private database/log/cache paths, configurable packet retention (30-day target), indexed bounded batch cleanup, disk-pressure signalling and preserve-on-migration-failure. Independently clear packet history, messages, pending outbound work, offline packages and diagnostics, with confirmation and results. Use an explicit versioned redacted report with preview, no upload, no history, no secrets or precise coordinates. Keep useful version/platform/settings-schema and bounded failure counters. | Disk-backed migration/reopen with synthetic data, low-space and failed-upgrade cases, clear actions preserving unrelated data, redaction tests for logs/report and backup rule review. A missing key blocks affected TX without hiding unrelated ports. |
| R6.8 Presentation finish / R4 | ViewLocator, dialogs, styles, strings and packet converters. Replace misleading errors and placeholders, use domain success/failure states, keep the language resources together, and review labels, dynamic text, contrast and portrait/landscape. Only English and Portuguese resources exist so far; add the other common languages with a check that every key is present in each file. | Typed-binding and localization checks, targeted navigation/dialog tests, brief accessibility check on the touched screen. |

Product scope: Android 13+, local identity, no mandatory cloud account, no analytics
upload. Offline means local map/history and available RF/GPS work without Internet; it
does not imply APRS-IS is available. Defer public-service workflows, bulletin
authoring expansion, historical telemetry graphs/alerts, export/import/backup, cloud
sync, extra distributions, USB serial, and additional radio accessories. Preserve
existing functionality unless a documented safety decision changes it.

### R6.9 Small fixes that can ship independently

Pick these by behaviour, not by audit severity. Reproduce the symptom on the current
branch first; if it no longer reproduces, record the test or source reason and close
the item without refactoring unrelated code.

| Work | Change and regression |
|---|---|
| Map lifetime and cache | Keep private runtime map tiles apart from explicit offline downloads, dispose page-owned layers once and stop subscriptions on detach; retain shared provider ownership at application lifetime. Attach/detach/reopen and show that old callbacks do not update a disposed layer. |
| Display and binding | Virtualize long packet collections without nesting a scrolling container that defeats virtualization; fix lost binding types with typed projections and correct localized fallback actions. Verify the actual large-list and dialog paths before deleting shared styles. |
| Diagnostics and Android affordances | Remove payload-bearing hot logs, use structured safe fields, make compass start/stop idempotent, use a monochrome notification small icon, and validate external navigation targets. Handle a malformed intent without switching context or performing an action. |

### R7 Build and distribution maintenance

1. Use one pinned build-tools version for build and APK verification; emit one
   explicit signed APK path and fail if it is absent or ambiguous.
2. Pass signing secrets using a supported secret-safe channel and verify masking and
   workspace cleanup. Validate tag/version-code inputs for collisions and check
   release source before signing.
3. Pin external actions where practical. If publishing an SBOM, generate it from the
   actual resolved build, publish it and verify its association with the APK; keep the
   claim out of the documentation until the workflow really does it.
4. Preserve GitHub Releases as the distribution channel and keep artifact checksum,
   signature and upgrade behaviour verifiable. Use release notes for version history.

## Definition of architectural done

- Database writes have one serialized owner and database reads have explicit query
  services; source scans enforce the boundary.
- Packet publication follows durable insertion; outbound work has explicit durability,
  retry, expiry and terminal-state semantics.
- User intent, runtime session state, platform readiness and first I/O are separate.
- A modem cannot be sent to while it is being stopped or disposed.
- Every long-lived worker has cancellation, bounded input, observable failure and a
  shutdown deadline.
- Application services run without Avalonia or Android runtime objects.
- ViewModels own UI collections and navigation state.
- Unsupported platform features fail honestly and are visible to the operator.
- Android permissions, background location, foreground service rejection, Bluetooth,
  audio, Keystore recovery and notification actions have tested contracts.
- Product documents distinguish requirement, implementation and evidence.
- CI continues to verify builds, tests, dependencies and license metadata. Add focused
  contract checks with the owning slice. Verify signing and artifacts when changing
  distribution.

## Documentation decisions

`README.md` owns install, use, build, private reporting and links. `AGENTS.md` owns
current source conventions and migration rules. This roadmap owns the target, feature
acceptance and priorities. Historical audits and design sheets stay in Git; do not
carry their completion claims into the README.
