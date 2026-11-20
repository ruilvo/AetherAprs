<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC-BY-SA-4.0
-->
# AetherAprs Roadmap

A phased, dependency-ordered plan to bring AetherAprs to parity with state-of-the-art
APRS apps (APRSdroid, APRSISCE/32, YAAC, Xastir, PinPointAPRS) while targeting
**mobile platforms only** (Android now, iOS later) and keeping the existing high
engineering bar (REUSE, localization, tests, MVVM/DI architecture).

> This is a plan only. No code changes are specified to be made in one pass — each
> phase is independently shippable and testable.

## Guiding Principles (non-negotiable)

1. **Mobile-first, iOS-ready.** Every platform capability is an interface in the core
   `AetherAprs/` project with a NoOp/desktop-safe default registered in
   `Factories/ServiceProviderFactory.cs`, and a concrete implementation in
   `AetherAprs.Android/Services/...` registered via `RegisterPlatformServices()`.
   Never reference `Android.*` APIs from core. Future iOS work adds an
   `AetherAprs.iOS/` project and swaps implementations — no core refactor required.
2. **Two-flow architecture is preserved.** Ports never touch the DB; storage goes
   through `PacketStorageService`; reads go through `PacketQueryService`; transmission
   goes through `PortService`. New features (objects, bulletins) follow this.
3. **Every new file gets a correct SPDX header** and lands in the folder/namespace
   dictated by `CODEBASE_STRUCTURE.md`.
4. **All user-facing strings are localized** (`Strings.resx` + `Strings.pt.resx`).
5. **All new logic has xUnit v3 tests** (NSubstitute), mirroring source namespaces.
6. **Offline-capable.** Core operation (RX/TX on RF, map of cached/offline tiles,
   messaging, objects) must work with no internet.

## Architecture Decisions (recorded so the plan is unambiguous)

- **AD-1 — New packet models.** Objects, Items, Bulletins, Third-Party, Queries and raw
  GPS need dedicated `AprsPacket` subtypes under `Models/Aprs/Packets/`. The current
  object parser (`;`) collapses objects into `PositionPacket`, losing object name and
  live/killed flag — that must be corrected with an `ObjectPacket`.
- **AD-2 — Raw frame persistence.** `PacketRecord` stores `RawInfo` (info field) but not
  the full AX.25 frame hex or the APRS-IS header line. "Raw packet view" needs both.
  Add persisted fields via an EF Core migration (do **not** discard existing data).
- **AD-3 — Position TX format is configurable.** Add a setting
  `PositionFormat { Uncompressed, Compressed, MicE }` (compressed is default for
  mobile). Compressed TX is info-field-only; MicE TX also rewrites the AX.25
  destination address, so it touches `Ax25Serializer`.
- **AD-4 — AFSK modem is fully managed C#.** Bell 202 (1200 baud, mark=1200 Hz,
  space=2200 Hz), NRZI, HDLC framing + CRC-16/X.25 (FCS). Reuse
  `Ax25Serializer`/`Ax25Parser`. Audio I/O goes through a new
  `IAudioStreamService` abstraction so Android and future iOS differ only in
  platform audio.
- **AD-5 — Notifications.** A new `INotificationService` in core; Android impl uses
  `NotificationManager` + a message channel with tap-to-open-conversation deep link.
- **AD-6 — iGate correctness.** RF→IS gating must emit the correct q-constructor
  (`qAo` for our own station, `qAR`/`qAC` as appropriate), not the client `TCPIP*`
  marker the APRS-IS modem currently hardcodes for our own originated packets.

---

## Initial Quality Audit (pre-flight)

**Objective:** Establish a measured quality baseline **before** any feature work, using the
full audit prompt in `DOCS/QUALITY_AUDIT_PROMPT.md`. Later phases are then prioritized
against a real inventory of defects rather than assumptions.

**Dependencies:** none (read-only).

**Work items:**

1. Run the prompt in `DOCS/QUALITY_AUDIT_PROMPT.md` against the current codebase and
   produce the structured report it specifies (executive summary, findings by dimension
   A–N, architecture violations, security findings, test-coverage gaps, prioritized
   action list).
2. Commit the report as a tracked artifact — extend or replace the existing
   `DOCS/CODE_AUDIT*.md` records with the new prompt-driven baseline.
3. Triage every **Blocker** and **Major** finding: assign each to a specific phase below
   (most will land in Phase 0) or record an explicit deferral with rationale. Fold any
   newly discovered architecture violations or security issues into Phase 0 work items.

**Acceptance criteria:**
- Baseline report committed; no untriaged Blocker/Major findings.
- Phase 0's work-item list reflects the audit's top findings (the two stay in sync).

**Tests:** n/a — the audit produces a report, not code.

---

## Phase 0 — Foundation & Correctness

**Objective:** Fix known defects and add the persistence/parsing primitives every
later phase depends on. No user-visible feature, but unblocks the rest.

**Dependencies:** none.

**Work items:**

1. **Wire up the unused digipeater alias settings.**
   - `DigipeaterService.GetDigipeatTargets` currently ignores
     `AprsSettings.DigipeaterRespondToWide1` / `DigipeaterRespondToWide2` (it treats
     `WIDE`/`TRACE` generically). Make alias handling respect these flags and add a
     `DigipeaterRespondToTrace` setting for `TRACEn-N`.
   - Files: `Services/Ports/DigipeaterService.cs`,
     `Configuration/Settings/AprsSettings.cs`,
     `ViewModels/Pages/SettingsViewModel.cs`, localization strings.

2. **Persist the full raw frame for RF and the raw line for APRS-IS.**
   - Add to `Data/Entities/PacketRecord.cs`: `RawFrameHex` (nullable string, hex of the
     KISS data payload) and `RawPath` (nullable string, the `source>dest,path` portion).
   - Populate in `Data/Mappers/PacketRecordMapper.cs` and the RF/IS modem receive paths.
   - Add an EF Core migration in `Data/Migrations/`.
   - Files: `Data/Entities/PacketRecord.cs`, `Data/Mappers/PacketRecordMapper.cs`,
     `Data/AppDbContext.cs`, `Modems/Aprs/AprsIsModem.cs`, `Modems/Aprs/AprsRfModem.cs`,
     `Modems/Aprs/Ax25Parser.cs`.

3. **Extract the AX.25 "has-been-repeated" (H) bit.**
   - `Ax25Parser.DecodeAddress` reads SSID but drops bit 7 of the SSID byte. Preserve it
     on `Callsign`/path so digipeater logic can skip already-repeated hops correctly.
   - Files: `Modems/Aprs/Ax25Parser.cs`, `Models/Aprs/Callsign.cs`.

4. **Persist Telemetry/Capabilities payloads** (currently only the discriminator is
   stored; reconstruction falls back to `UnknownPacket`).
   - Add owned types to `PacketRecord` + mapping in `PacketRecordMapper.cs` + DbContext
     config + migration.

**Acceptance criteria:**
- Existing full test suite passes; new tests cover alias handling, raw-frame
  persistence round-trip, H-bit decode, telemetry/capabilities persistence.
- `dotnet build AetherAprs.slnx` and `dotnet test` are green.
- Migration upgrades an existing DB without data loss.

**Tests:** `Services/Ports/DigipeaterServiceTests.cs` (alias toggles),
`Data/Mappers/PacketRecordMapperTests.cs` (raw fields), `Modems/Aprs/Ax25SerializerPathTests.cs`
(H-bit), telemetry/capabilities mapper tests.

---

## Phase 1 — Protocol TX (MicE + Compressed) & Packet Model Completeness

**Objective:** Close the transmit-side and receive-side protocol gaps that block
Objects, Bulletins, and compact mobile TX.

**Dependencies:** Phase 0 (H-bit, packet model base).

**Work items:**

1. **Compressed position TX** (base-91).
   - Add `AprsInfoFieldSerializer.FormatPositionCompressed(PositionPacket)` and route
     through the `PositionFormat` setting (`Configuration/Settings/AprsSettings.cs`).
   - Files: `Modems/Aprs/AprsInfoFieldSerializer.cs`, `Configuration/Settings/AprsSettings.cs`,
     `Factories/Packets/PacketFactory.cs`.

2. **MicE TX** (encode latitude in destination address + longitude/course/speed/status
   in info field).
   - Add `Modems/Aprs/MicESerializer.cs`; extend `Ax25Serializer.Serialize` to accept a
     MicE destination address; honor `PositionFormat.MicE`.
   - Files: `Modems/Aprs/MicESerializer.cs`, `Modems/Aprs/Ax25Serializer.cs`,
     `Modems/Aprs/AprsInfoFieldSerializer.cs`.

3. **New packet models + parser coverage.**
   - Add `ObjectPacket` (name, live/killed, timestamp), `ItemPacket`, `BulletinPacket`
     (or a bulletin discriminator on `MessagePacket`), `ThirdPartyPacket`, `QueryPacket`,
     `RawGpsPacket` under `Models/Aprs/Packets/`.
   - Update `AprsInfoFieldParser` to emit them for `;` (object), `)` (item), `}` (third
     party), `?` (query), `$` (raw GPS/NMEA). Keep `UnknownPacket` as fallback.
   - Add corresponding serializers in `AprsInfoFieldSerializer` where transmission is
     supported (object, item, bulletin, query).
   - Files: `Models/Aprs/Packets/*`, `Modems/Aprs/AprsInfoFieldParser.cs`,
     `Modems/Aprs/AprsInfoFieldSerializer.cs`.

**Acceptance criteria:**
- Round-trip tests: `FormatInfoField(ParseInfoField(x)) == x` for compressed position,
  MicE position, object, item, bulletin, query, third-party, raw GPS (within spec).
- `PositionFormat` setting defaults to Compressed; beacon packets emit compressed when
  selected.

**Tests:** extend `Modems/Aprs/AprsSerializerTests.cs`, add `MicESerializerTests.cs`,
`Modems/Aprs/AprsParserTests.cs` (object/item/query/third-party/raw-GPS cases).

---

## Phase 2 — Message Notifications (Android)

**Objective:** Real Android notification on incoming APRS messages; tap opens the
conversation. Fixes the doc/code mismatch (wiki claims a notification that doesn't exist).

**Dependencies:** none (independent of Phase 1).

**Work items:**

1. Define `INotificationService` in core with `ShowMessageNotificationAsync(callsign,
   text, conversationId)` and a `NoOpNotificationService`.
   - Files: `Services/Platform/INotificationService.cs`, `Services/Platform/NoOpNotificationService.cs`.

2. Android implementation: notification channel, content intent that deep-links to the
   conversation, POST_NOTIFICATIONS handling (already requested at startup in
   `App.axaml.cs`).
   - Files: `AetherAprs.Android/Services/Platform/AndroidNotificationService.cs`.

3. Hook `MessageService.HandleIncomingMessage` to post a notification (when the app is
   not foreground/thread not currently open).
   - Files: `Services/Messaging/MessageService.cs`.

4. Register in DI (`Factories/ServiceProviderFactory.cs`, `DesignData.cs`) and Android
   `RegisterPlatformServices()`.

**Acceptance criteria:**
- Incoming message posts a notification; tapping opens the conversation.
- No notification when the user is already viewing that conversation.

**Tests:** `Services/Messaging/MessageServiceTests.cs` (notification invoked on
incoming), platform NoOp stub test.

---

## Phase 3 — Objects & Items + Bulletins (End-to-End)

**Objective:** Full create/edit/kill/display for objects and items; send/list bulletins.
Critical for event/emergency operations.

**Dependencies:** Phase 1 (packet models), Phase 0 (raw persistence).

**Work items:**

1. **Persistence.** Add `ObjectRecord` entity (name, position, symbol, comment,
   live/killed, timestamp, source) + `BulletinRecord` (or reuse message storage with a
   bulletin flag). Migration.
   - Files: `Data/Entities/ObjectRecord.cs`, `Data/AppDbContext.cs`, `Data/Migrations/`.

2. **Service.** `IObjectService` (create/update/kill/list, persist + transmit via
   `PortService.SendPacketAsync`) and `IBulletinService` (send/list, bulletin addressee
   handling for `BLN#` / named bulletins).
   - Files: `Services/` new category (e.g. `Services/Objects/`, `Services/Messaging/`
     for bulletins).

3. **Map display.** Render object/items as distinct features (killed objects hidden or
   dimmed); update `ReceivedBeaconsMapLayerService` or add an `ObjectsLayer`.
   - Files: `Services/UI/ReceivedBeaconsMapLayerService.cs`.

4. **UI.** Object create/edit dialog (name, position, symbol, comment, kill switch),
   bulletins list + compose, per the factory pattern for transient ViewModels.
   - Files: `ViewModels/Pages/`, `Views/Pages/`, factories under `Factories/ViewModels/`,
     registration in `ServiceProviderFactory.cs`, `DesignData.cs`, `ViewLocator.cs`.

**Acceptance criteria:**
- Create an object → appears on map + on APRS-IS/RF; kill → hidden/removed; edit updates.
- Send bulletin → received by other stations; list shows bulletin text + origin.
- Round-trip persistence across app restart.

**Tests:** object/bulletin service tests, ViewModel tests, mapper tests.

---

## Phase 4 — Rich Station Details + Raw Packet View + Search

**Objective:** aprs.fi-like station inspection: history, distance/bearing, raw hex/line,
and a search facility.

**Dependencies:** Phase 0 (raw persistence), Phase 1 (models).

**Work items:**

1. **Query additions** in `IPacketQueryService` / `PacketQueryService`: search by
   callsign/object name (prefix + exact), distance/bearing from a reference point.
   - Files: `Services/Packets/IPacketQueryService.cs`, `Services/Packets/PacketQueryService.cs`.

2. **Station details expansion** in `PacketDetailsViewModel`: tabs for position history,
   raw packets (hex + APRS-IS line), weather/status/telemetry where present, distance &
   bearing from user location.
   - Files: `ViewModels/Pages/PacketDetailsViewModel.cs`, `Views/Pages/PacketDetailsPage.axaml`.
   - Includes a radar/compass header: a relative-bearing needle computed as the station
     bearing minus the device heading. Heading is provided by `ICompassService`
     (`Services/Platform/ICompassService.cs`) — `NoOpCompassService` on desktop, a
     sensor-fusion `CompassService` on Android. The service exposes `IsAvailable`,
     `HeadingDegrees`, `HeadingUpdated`, `StartListening()`/`StopListening()`.

3. **Search UI** (station/object lookup) with results list → tap opens details.
   - Files: `ViewModels/Pages/`, `Views/Pages/`, localization.

**Acceptance criteria:**
- Tap a station → full history, raw hex + raw line, distance/bearing shown.
- Search finds stations and objects; results are navigable.

**Tests:** query service search/bearing tests, PacketDetails ViewModel tests.

---

## Phase 5 — AFSK Audio Modem (Soundcard TNC)

**Objective:** RX/TX APRS over a radio's audio (mic/speaker or audio cable) without a
hardware TNC — APRSdroid's defining capability. Delivered in two vertical slices.

**Dependencies:** Phase 0 (AX.25 framing/parsing + H-bit), Phase 1 (compact TX).

**Work items — Slice A (RX first):**

1. `IAudioStreamService` abstraction (start/stop capture, expose PCM samples) in core +
   `NoOpAudioStreamService`; Android impl using `AudioRecord`.
   - Files: `Services/Platform/IAudioStreamService.cs`, `NoOpAudioStreamService.cs`,
     `AetherAprs.Android/Services/Platform/AudioStreamService.cs`.

2. AFSK modem core: `Modems/Afsk/AfskModulator.cs` (DSP), `AfskDemodulator.cs`
   (Bell 202 + NRZI + clock recovery), `HdlcFramer.cs` (flag/bit-stuff/CRC-16), and a
   top-level `AfskModem` implementing `IAprsModem` (decode PCM → AX.25 → APRS).
   - Files: `Modems/Afsk/*` (namespace `AetherAprs.Modems.Afsk`).

3. Port integration: new `AfskSettings : IPortTypeSettings` (audio source, PTT
   control, TX delay), a new case in `PortService.StartModemAsync`, and audio stream
   factory. Update `Configuration/PortConfig.cs` + JSON polymorphic registration.
   - Files: `Configuration/PortConfig.cs`, `Services/Ports/PortService.cs`,
     `Transports/` audio connector.

**Work items — Slice B (TX + PTT):**

4. AFSK modulation → `AudioTrack` playback; PTT abstraction (`IPttService` + Android
   VOICE_TRANSMIT / speaker-mic toggle). TX delay + tail to match TNC behavior.

5. Settings: audio level, PTT mode, squelch threshold (RX).

**Acceptance criteria:**
- Slice A: nearby AFSK APRS packets decode into the packet pipeline and appear on the
  map/packets page.
- Slice B: own beacons/messages modulate out and are decoded by another APRS app/TNC.
- Offline-capable (no internet dependency).

**Tests:** DSP unit tests (synthesized signal → demod → frames), HDLC framing round-trip,
port-service start with AfskSettings.

> **Note:** This is the largest phase. If time-constrained, ship Slice A alone first.

---

## Phase 6 — Offline Maps (MBTiles / Caching)

**Objective:** Usable map with no internet (RF-only operation).

**Dependencies:** none.

**Work items:**

1. Tile source abstraction + MBTiles provider (verify Mapsui 5.x MBTiles support; if
   absent add `BruTile.MbTiles` via `Directory.Packages.props` or a small custom
   provider). Add an offline tile-package downloader (pre-download a region).
   - Files: `Services/UI/` map/tile services, `Directory.Packages.props`.

2. Map settings: tile source selection (OSM online / offline MBTiles), cache size,
   download-region UI.
   - Files: `Configuration/Settings/`, `ViewModels/Pages/SettingsViewModel.cs`,
     `Views/Pages/SettingsPage.axaml`.

**Acceptance criteria:**
- Select an offline tile package and the map renders fully offline.
- Online tile caching still works; download-region workflow is localized.

**Tests:** tile-source factory tests, MBTiles provider tests (with a fixture package).

---

## Phase 7 — APRS-IS Filter Builder UI

**Objective:** Visual construction of APRS-IS filter strings (replacing free-text).

**Dependencies:** none.

**Work items:**

1. Filter model + string generator (radius, prefix, buddy, type, range/height, port,
   area) producing the space-separated filter string; parser to round-trip into the UI.
   - Files: `Models/Aprs/` or `Configuration/`, `Services/` filter builder.

2. Filter builder component ViewModel + View; integrate into `AddEditPortViewModel`
   for APRS-IS ports.
   - Files: `ViewModels/Components/`, `Views/Components/`, localization.

**Acceptance criteria:**
- Build a filter visually; generated string matches APRS-IS filter spec; saved filter
  round-trips into the UI on edit.

**Tests:** filter generator/parser tests, ViewModel tests.

---

## Phase 8 — Full-Featured Digipeater / iGate

**Objective:** Proper WIDE/TRACE alias handling, preemptive digi, RF channel access
timing, and correct iGate q-constructors.

**Dependencies:** Phase 0 (H-bit + alias wiring), Phase 5 (AFSK for TX delay/slottime),
Phase 1 (path model).

**Work items:**

1. Alias engine: WIDE1-N (fill-in), WIDE2-N, TRACEn-N; per-alias enable/disable;
   decrement logic using the H-bit. (Phase 0 already wires the settings; this phase
   completes TRACE + preemptive behavior.)
   - Files: `Services/Ports/DigipeaterService.cs`.

2. iGate q-constructors (AD-6): RF→IS emits `qAo`/`qAR` instead of client `TCPIP*`;
   honor `EnableAprsIsToRfGate` / `EnableRfToAprsIsGate` with correct path rewriting.
   - Files: `Modems/Aprs/AprsIsModem.cs`, `Services/Ports/DigipeaterService.cs`.

3. RF channel access: TX delay (randomized slottime + persist-aloha), TX tail, and a
   holdoff to avoid double-transmitting recently heard frames. Applies to the AFSK
   modem; KISS TNCs handle this natively (document).
   - Files: `Modems/Afsk/` + a shared `TxTiming` helper.

4. Duplicate suppression across all ports with a bounded "heard" table (source+dest+info
   hash), replacing/strengthening the current per-service dedup.

**Acceptance criteria:**
- A packet with `WIDE2-2` is decremented once and forwarded per alias config; exhausted
  paths are not re-forwarded; TRACEn-N inserts our callsign.
- RF→IS gating shows correct q-constructor on aprs.fi raw packets.

**Tests:** `Services/Ports/DigipeaterServiceTests.cs` (alias matrix, q-constructors,
TX-timing), AFSK TX-timing tests.

---

## Phase 9 — iOS-Readiness & Architecture Hardening

**Objective:** Guarantee the iOS path is cheap: document platform extension points and
eliminate any core/Android coupling introduced by the above work.

**Dependencies:** all preceding phases.

**Work items:**

1. Audit core for Android leaks; ensure every platform service has a core interface +
   NoOp default + Android impl (audio, notification, location, Bluetooth, foreground,
   PTT, app-data).
2. Document the iOS extension recipe in `DOCS/wiki/Developer-Guide.md` (add project,
   implement services, register in `RegisterPlatformServices()`).
3. Final REUSE + localization + test-coverage pass.

**Acceptance criteria:**
- Core builds for `net10.0` with zero Android API references (verify via dependency
  analysis / manual grep).

---

## Final Quality Audit (release gate)

**Objective:** Before tagging a release, re-run the same audit in
`DOCS/QUALITY_AUDIT_PROMPT.md` and prove the codebase meets the engineering bar with no
regressions against the baseline.

**Dependencies:** all feature phases (0–9) plus the Initial Quality Audit baseline.

**Work items:**

1. Re-run the prompt and produce a final report.
2. Diff against the Initial Quality Audit baseline: every **new** Blocker or Major must be
   fixed before release; every baseline finding must be either closed or consciously
   accepted (with rationale and, where appropriate, a tracking issue).
3. Confirm the cross-cutting dimensions are clean — architecture boundaries (A), REUSE
   (E), localization (F), test coverage (M), and zero Android references in core (A.6) —
   i.e. that Phase 9's hardening goals are verifiably met.
4. Record the final state in the audit artifact (close it or mark it superseded).

**Acceptance criteria:**
- Zero open Blockers; Majors fixed or risk-accepted with a tracked issue.
- Final report committed alongside the release.

**Tests:** n/a — verification gate, not a code phase.

---

## Release Process

Cut a GitHub release after each **major feature / milestone**. The release is fully
automated by CI: pushing a version tag builds, signs, and publishes the Android APK.

**Tag convention (from repo history + `.github/scripts/extract-version.sh`):**
- Format: `vMAJOR.MINOR.PATCH[-<type>.<number>]` (e.g. `v0.1.0-beta.2`).
- Pre-release type progression: `alfa` → `beta` → `rc` → final (no suffix). The repo's
  established spelling is **`alfa`** (not "alpha"); the script accepts both, but history
  uses `alfa`. Existing tags: `v0.1.0-alfa.1` … `v0.1.0-alfa.13`, `v0.1.0-beta.1`.
- The script maps the type/number to an Android version code (`alfa`=1–33, `beta`=34–66,
  `rc`=67–98, final=99) used as `ApplicationVersion`.

**How to release:**
1. Confirm the Final Quality Audit gate (above) passes for this milestone.
2. Tag and push the version:
   ```bash
   git tag -a v0.1.0-beta.2 -m "Release v0.1.0-beta.2"
   git push origin v0.1.0-beta.2
   ```
3. The `v*` tag push triggers `.github/workflows/release-apk.yml`, which builds and signs
   the APK, derives the version code/name from the tag, and creates the GitHub release
   (auto-generated release notes; any `-<type>.<number>` suffix is published as a
   pre-release).

**Files:** `.github/workflows/release-apk.yml`, `.github/scripts/extract-version.sh`,
`README.md` (features/versioning). Signing-secret setup is documented in the wiki's
"Android Release Setup" page (linked from `README.md`).

---

## Phase 10 — Nice-to-Haves (Backlog, Post-MVP)

Ordered, low-priority; pick opportunistically:

1. **Own-station weather reporting** (sensor input → `WeatherPacket` TX) + NWS weather
   alert parsing/display.
2. **Telemetry labels/units** (`PARM`/`UNIT`/`EQNS` decoding) + simple graph.
3. **Smart-beaconing refinements** (corner pegging, hysteresis, proportional pathing).
4. **Email/SMS gateway** (`EMAIL`/SMSGTE) for messages.
5. **Tracks export/import** (GPX/KML).
6. **Activity/statistics log** (heard stations, RX/TX counts).
7. **USB-serial & AGWPE transports** (deferred per your answer; AFSK is the priority).
8. **Station heard list** with distance/bearing (complements Phase 4).

---

## Suggested Execution Order & Parallelism

- **Bookends:** Initial Quality Audit (baseline) → feature phases → Final Quality Audit
  (release gate) → Release Process (push a `v###` tag).
- **Solo-dev critical path:** Initial Quality Audit → P0 → P1 → P3 → P4 → P5 → P8 → P9 →
  Final Quality Audit.
- **Parallelizable / independent (can be interleaved any time):** P2 (notifications),
  P6 (offline maps), P7 (filter builder).
- **Quick wins for momentum:** P2, P7, then P1 (compressed TX is small and high-value).

## Cross-Cutting Checklist (every phase)

- [ ] SPDX header on every new file; `pre-commit run reuse-lint-file --all-files`.
- [ ] Explicit `using` statements (ImplicitUsings disabled).
- [ ] New packages added to `Directory.Packages.props` (no versions in `.csproj`).
- [ ] Localization strings in `Strings.resx` + `Strings.pt.resx`.
- [ ] DI registration in `ServiceProviderFactory.cs` + `DesignData.cs` (+ `ViewLocator.cs`
      for new ViewModels/Views).
- [ ] `dotnet build AetherAprs.slnx` and `dotnet test` green.
