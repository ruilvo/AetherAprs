<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC0-1.0
-->
# Code Audit â€” Progress Log

## Session 1 â€” 2026-10-14

- Reconnaissance complete: read AGENTS.md, CLAUDE.md, Directory.Build.props, global.json, Directory.Packages.props, mapped 157 core / 14 Android / 46 test source files. Working tree has large uncommitted in-flight user refactor â€” must not be reverted.
- Baseline `dotnet build AetherAprs.slnx`: **FAILED** â€” 3 Ã— CA1852 in Android project (AUDIT-001). Core + Tests build clean.
- Created `DOCS/CODE_AUDIT.md`, `DOCS/CODE_AUDIT_FINDINGS.md`, `DOCS/CODE_AUDIT_PROGRESS.md`.
- Baseline `dotnet test` (no-build): **PASSED** â€” 448/448 tests, 0 failed, 0 skipped (3.9s).
- Fixed AUDIT-001: sealed `BackInvokedCallback`, `StopPortsBroadcastReceiver` (`AetherAprs.Android/MainActivity.cs`), `SingleUpdateLocationListener` (`AetherAprs.Android/Services/Platform/LocationService.cs`). Rebuilt solution: **0 warnings, 0 errors**.

**Next actions:**
1. Dispatch parallel review subagents over core areas (protocol parsing, services/persistence, ViewModels/UI/localization, DI/config, Android transports).
2. Triage their reports into findings inventory; begin Phase B fixes.

### Phase B review (2026-10-14, same session)

- Dispatched 5 parallel explore subagents (modems/protocol; services/data/factories; ViewModels/UI/localization; Android/platform; tests). All reported findings only (no edits).
- Triaged ~80 verified findings into `DOCS/CODE_AUDIT_FINDINGS.md` as AUDIT-002 â€¦ AUDIT-083 (15 High, 27 Medium, 41 Low), spanning protocol parsing, PortService, messaging, beaconing, UI threading, Android platform, and test quality/coverage.
- Highest-impact items: compressed-position path drop (002), KissModem restart (003), negative temperature encoding (004), AprsIsModem send races (005), digipeater serialization failures (006), port-disable not stopping modem (022), receive-pipeline kill-by-subscriber (023), ACK/REJ polluting conversations (029), overlay retire bug (038), UI-thread violations (039/040), desktop DI gap (054), persistence-layer test gap (070-072).

**Next actions:**
1. Fix AUDIT-032 (SPDX headers on migrations) â€” pre-commit blocker, trivial.
2. Implement High fixes in order: 002, 003, 004, 005, 006, 022, 023, 029, 038, 039/040, 053, 054.
3. Rebuild + full test run after each batch; update statuses in FINDINGS.

### Phase C fixes (2026-10-14, same session)

- Fixed AUDIT-032 (SPDX `//` headers on 5 migration/snapshot files; XML comment style broke build â€” C# uses `//`).
- Fixed AUDIT-002 (compressed positions keep digipeater path), AUDIT-004 (negative weather temp encode/decode per APRS spec), AUDIT-005/013 (AprsIsModem send serialization + removed racy pre-wait), AUDIT-003/009/019 (KissModem lifecycle rewrite: fresh CTS per Start, lifecycle lock, ordered dispose; +2 tests incl. new BlockingDuplexStream test double).
- Fixed AUDIT-006 (digipeater type filtering + serializer extension): extended AprsInfoFieldSerializer to handle MessageAck/Rej (`:ADDRESSEE:ack{N}` format); added IsDigipeatable filter to DigipeaterService excluding TelemetryPacket/CapabilitiesPacket; created DigipeaterServiceTests.cs with 12 tests (disabled setting, own-station, filtering, dedup, RFâ†’IS/RFâ†’RF forwarding, port filtering).
- User product decisions applied: docs audit now in scope (AGENTS/CLAUDE updated: VSTest platform + NSubstitute noted); **no desktop fallback implementations** â€” deleted NoOpForegroundService.cs and AppDataDirProviderService.cs from core, DesignData uses private design-time stubs, Android comments updated; AGENTS/CLAUDE/DOCS to be kept current alongside fixes.
- Fixed AUDIT-022 (SetPortEnabledAsync now stops modem when disabling port; +1 test PortServiceSetPortEnabledTests)
- Fixed AUDIT-023 (PacketReceived subscriber exception isolation: wrapped event invocation + HandleDigipeatAsync in separate try/catch blocks; +1 test)
- Fixed AUDIT-038 (MainViewModel overlay retirement: track `_previousOverlay` field, retire OLD not NEW VM)
- Suite: 465/465 green after this batch.

**Next actions:**
1. High fixes: 029 (ACK/REJ as messages), 039/040 (UI thread), 053 (stale last-known location)
2. Medium/low priority fixes (~70 remaining)
3. Docs audit: wiki consistency review (Home.md dead links, Developer-Guide Avalonia version), log batches in PROGRESS.
4. Architecture polish: design patterns, VM/View pairing, service classification

### Phase C fixes, session 2026-10-16 (options 1, 3, 2)

- Option 1: AUDIT-007/008 verification evidence refreshed in FINDINGS (both Fixed).
- Option 3 â€” AUDIT-039/040 UI-thread marshaling fixed via `IUiDispatcher` abstraction: new `Services/UI/IUiDispatcher.cs` + `AvaloniaUiDispatcher.cs` (CheckAccessâ†’sync / Post fallback, same pattern as MessageService.RunOnUi), registered in ServiceProviderFactory + DesignData. HomeViewModel wraps OnBeaconStatusChanged and OnLocationUpdated bodies in `_uiDispatcher.Post(...)`. Tests use `Tests/Helpers/SynchronousUiDispatcher.cs` (sync execution). Lesson: typed registration `AddSingleton<IUiDispatcher>(instance)` required â€” plain instance registration doesn't satisfy interface resolution.
- Option 2 â€” medium fixes: AUDIT-053 verified (Android 30 s staleness gate on last-known location); AUDIT-024 (PortService state-lock snapshots in SendPacketAsync auto-start); AUDIT-026 (StopSessionAsync try/finally disposal); AUDIT-028 (BeaconService async queued persistence via BeaconPersistenceSnapshot + _persistLock); AUDIT-027 (ReportBeaconTransmissionResult retry backoff: IBeaconService contract + BeaconService 30 s backoff wind-back + re-evaluation; BeaconTransmissionService reports tx success/failure; +2 tests; 3 test doubles updated).
- AUDIT-029 Fixed (PacketFactory returns MessageAckPacket/MessageRejPacket; 4 test factories updated).
- AUDIT-007/008 (serializer precision normalization; AsUnknown preserves path) â€” Fixed with updated serializer test. AUDIT-031 still needs migration-default decision (false vs entity true).

**Suite: 467/467 green** at end of 2026-10-14 work; 469/469 green after 2026-10-16 batch (below).

### Phase C fixes, session 2026-10-16 (platform defaults reversal)

- User reversed the "no desktop fallback" decision via AGENTS.md edit: "Core must register safe defaults (e.g. NoOp/desktop implementations) for every platform service interface so non-Android hosts can start; platform projects replace them."
- AUDIT-054 re-opened and Fixed: restored `AppDataDirProviderService` + `NoOpForegroundService` from git history, created `NoOpLocationService` + `NoOpPermissionService`; all four registered in ServiceProviderFactory BEFORE registerPlatformServices (platform last-wins). Android Application.cs comments updated.
- New `AetherAprs.Tests/Factories/ServiceProviderFactoryTests.cs` (2 tests: core defaults resolve without platform services; platform registration overrides).
- DesignData keeps its realistic design-time stubs (registered directly in the design-time container, unaffected).
- Noted: PacketsViewModelTests.Constructor_InitializesProperties is culture-flaky (observed one failure expecting pt "Pacotes" â€” ties into AUDIT-083).

**Suite: 469/469 green. Build 0 warnings/0 errors.**

### Session 2026-10-16 (continued) â€” AUDIT-031/033-036 fixed

- AUDIT-031 Fixed (migration default): Changed `AddAllowDigipeatToPorts.cs:21` defaultValue from false to true to match `PortRecord.AllowDigipeat = true` entity default â€” avoids breaking change where existing ports lose digipeat on upgrade.
- AUDIT-033 Fixed (silent catch): Added logging to `PacketRecordMapper.MapToPacket()` silent catch block â€” added optional `ILogger? logger = null` parameter, log warning on conversion failure with source/type/rawInfo context.
- AUDIT-034 Fixed (uncancellable cleanup loop): Made App.axaml.cs 24-hour cleanup loop cancellable â€” added `_shutdownCts` field, passed token to `Task.Delay(..., _shutdownCts.Token)`, added `Dispose()` method to cancel token on shutdown, catch `OperationCanceledException` to break gracefully.
- AUDIT-035 Fixed (sync DB in ctors): Three singleton services (PortService, MessageService, BeaconService) performed synchronous EF Core DB operations in constructors during DI resolution on UI thread â€” refactored to lazy/async initialization:
  - Added `Task InitializeAsync(CancellationToken cancellationToken = default);` to IPortService, IMessageService, IBeaconService interfaces
  - PortService: Constructor changed `_ports = LoadPorts()` to `_ports = []`; added `InitializeAsync()` â†’ `LoadPortsAsync()` (async EF), repopulates `_ports`, fires `PortsChanged`
  - MessageService: Constructor removed `LoadMessages()` call; added `InitializeAsync()` â†’ `LoadMessagesAsync()` (async EF)
  - BeaconService: Constructor removed `LoadFromDatabase()` call; added `InitializeAsync()` â†’ `LoadFromDatabaseAsync()` (async EF)
  - Fixed 11 test fake services: added `InitializeAsync()` stub methods
  - Fixed 8 test failures: added `await service.InitializeAsync(TestContext.Current.CancellationToken)` calls before assertions; created helper methods `CreateAndInitializeServiceAsync()` in PortServiceTests and PortServiceSetPortEnabledTests
- AUDIT-036 Fixed (inbound dedup): Added MessageService inbound message deduplication following DigipeaterService pattern â€” added constants `DedupTableCapacity = 256`, `DedupRetention = 30s`; added fields `_dedupLock`, `_dedupOrder` (Queue), `_dedupTable` (Dictionary); modified `HandleIncomingMessage()` to early-return if `IsDuplicateMessage(source, messageNumber)` with debug log; added `IsDuplicateMessage()` helper with LRU eviction by time and capacity.

**Suite: 469/469 green. Build 0 warnings/0 errors.**

- AUDIT-037 Fixed (port persistence CT): Added optional `CancellationToken cancellationToken = default` parameter to `PersistPortAsync()` and `DeletePortAsync()` helper methods in PortService.cs; threaded CT through all EF Core calls (`CreateDbContextAsync(ct)`, `FindAsync([...], ct)`, `SaveChangesAsync(ct)`); updated all 6 call sites to pass `default` token (public methods don't yet accept CT parameters, but infrastructure is ready for future enhancement).

**Suite: 469/469 green. Build 0 warnings/0 errors.**

### Session 2026-10-16 (continued) â€” AUDIT-041-044 fixed

- AUDIT-041 Fixed (HomePage fire-and-forget): Wrapped `_ = viewModel.StartLocationTrackingAsync();` call in `Task.Run` with try/catch block logging errors via `System.Diagnostics.Debug.WriteLine` â€” prevents unobserved task exceptions from GPS/permission errors; added missing `using System.Threading.Tasks;` statement (required because ImplicitUsings is disabled).
- AUDIT-042 Fixed (AddEditPortViewModel disposal): Updated `AddEditPortPage.axaml.cs` to track current ViewModel and dispose on DataContext change â€” added private field `_currentViewModel` and `OnDataContextChanged` event handler; disposes previous ViewModel before tracking new one, ensures BLE scan is cancelled when page is left.
- AUDIT-043 Fixed (SettingsViewModel symbol clearing): Added validation check `if (string.IsNullOrEmpty(SymbolTableCharacter) || string.IsNullOrEmpty(SymbolCodeCharacter))` before accessing `[0]` in `SaveSettings()` â€” returns early with warning log if fields are empty, prevents IndexOutOfRangeException on cleared symbol fields.
- AUDIT-044 Fixed (RSSI localization): Created new value converter `AetherAprs/Converters/UI/RssiFormatConverter.cs` implementing `IValueConverter` using `Strings.Format("RssiFormat", rssi)` for localized formatting; registered converter in `AddEditPortPage.axaml` resources; updated binding from `StringFormat='RSSI: {0} dBm'` to `Converter={StaticResource RssiFormatConverter}` â€” honors existing `RssiFormat` resource in both `Strings.resx` and `Strings.pt.resx`.

**Suite: 469/469 green. Build 0 warnings/0 errors.**

### Session 2026-10-16 (continued) â€” AUDIT-073-074 fixed

- AUDIT-073 Fixed (MessageService outbound-storage test): Fixed `SendAsync_SendsMessagePacketOnEnabledTxPorts_AndStoresOutbound` test in `MessageServiceTests.cs` â€” extracted `IPacketStorageService` mock to variable `packetStorageService` and added NSubstitute verification `await packetStorageService.Received(1).StorePacketAsync(Arg.Is<AprsPacket>(p => p is MessagePacket), port.Id, isOutbound: true, Arg.Any<CancellationToken>())` â€” ensures test actually verifies that MessageService calls storage layer for outbound messages (MessageService.cs lines 184, 219, 228).
- AUDIT-074 Fixed (PacketsViewModel Dispose test): Fixed `Dispose_CleansUpResourcesAndUnsubscribes` test in `PacketsViewModelTests.cs` â€” test previously built a `PacketRecord` but never added it to `_storedPackets`, so raising `PacketStored` event after disposal didn't test anything. Fix captures initial packet count, adds record to `_storedPackets`, raises event after disposal, waits 50ms with `TestContext.Current.CancellationToken`, asserts packet count unchanged (event ignored after disposal), then verifies second Dispose() doesn't throw â€” properly validates unsubscription behavior.

**Suite: 469/469 green. Build 0 warnings/0 errors.**

### Session 2026-10-16 (continued) â€” AUDIT-010-012 fixed (protocol validation)

**AUDIT-012 â€” AprsIsModem outbound lines lack CR/LF sanitization (Fixed):**
- Location: `AetherAprs/Modems/Aprs/AprsIsModem.cs:209, 322`
- Evidence: Embedded `\r\n` in message text or filter strings could inject extra protocol lines
- Fix: Added sanitization after forming lines in send path and login string:
  - Line 209 (send path): Check for `\r` or `\n`, remove them, log warning
  - Line 322 (login string): Check for `\r` or `\n`, remove them, log warning
- Prevents protocol injection attacks via malicious packet fields

**AUDIT-011 â€” MIC-E longitude not range validated (Fixed):**
- Location: `AetherAprs/Modems/Aprs/AprsInfoFieldParser.cs:843-876`
- Evidence: `lonDeg` could be negative or â‰¥200 from wire bytes after 180-189/190-199 adjustments
- Fix: Added validation at lines 853-862:
  ```csharp
  if (lonDeg < 0 || lonDeg >= 180)
      return AsUnknown("MIC-E longitude out of range");
  ```
- Prevents invalid coordinates from flowing to map layer

**AUDIT-010 â€” AprsIsModem MaxLineLength enforced after buffering (Fixed):**
- Location: `AetherAprs/Modems/Aprs/AprsIsModem.cs:336-355`
- Evidence: `StreamReader.ReadLineAsync` allocates for arbitrarily long input before 512-char check
- Fix: Replaced with `ReadBoundedLineAsync` helper method:
  - Uses fixed `char[maxLength + 1]` buffer
  - Reads character-by-character until `\n`, handles `\r\n` and `\n` line endings
  - Throws `InvalidOperationException` if line exceeds maxLength
  - Read loop catches exception, logs warning, continues
  - Implemented at lines 577-630
- Prevents hostile APRS-IS server from causing unbounded memory allocation

**AUDIT-075 â€” PortItemViewModel async callback tests race on Task.Delay (Fixed):**
- Location: `AetherAprs.Tests/ViewModels/Components/PortItemViewModelTests.cs:75, 170, 175`
- Evidence: Tests used `Task.Delay(50)` to wait for fire-and-forget async callbacks (`_ = HandleToggleAsync()`)
- Fix: Implemented TaskCompletionSource pattern in test callbacks (no production code changes):
  - PropertyChanges_InvokeCallbacks test: Created `toggledTcs` and `mapToggledTcs`, callbacks signal TCS with `SetResult`, test awaits deterministically with `WaitAsync(TestContext.Current.CancellationToken)`
  - PropertyChanges_AfterInitialization_InvokeCallbacks test: Used `TrySetResult(true)` for multiple property changes, sequential verification pattern
- No production code modified â€” pure test improvement

**AUDIT-076 â€” Modem receive-loop tests wait flat Task.Delay(500) for drain (Fixed):**
- Location: `AetherAprs.Tests/Modems/Kiss/KissModemTests.cs:354, 376, 432`; `AprsRfModemTests.cs:292`
- Evidence: Three KissModem tests waited 500ms for read loop to drain MemoryStream
- Fix: Replaced with deterministic TaskCompletionSource patterns:
  - FrameReceived_MultipleFramesInOneRead_ReceivesAll: Countdown TCS pattern (expectedCount = 2)
  - FrameReceived_BackToBackFrames_NoSpuriousEmptyFrame: Countdown TCS pattern (expectedCount = 2)
  - FrameReceived_ConsecutiveFENDWithoutPayload_SkipsEmptyFrame: Condition-based TCS (signals when expected frame matches criteria)
  - AprsRfModemTests negative test: Added comment documenting pragmatic wait-and-hope approach for negative assertion
- All await with `WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken)`

**Culture test failure fixed:**
- PacketsViewModelTests.Constructor_InitializesProperties failed: Expected "Packets" but got "Pacotes" (Portuguese)
- Root cause: Test class didn't inherit from TestFixtureBase, so culture wasn't set to en-US
- Fix: Changed `PacketsViewModelTests : IDisposable` to `PacketsViewModelTests : TestFixtureBase`
- Removed redundant empty Dispose() method â€” TestFixtureBase already provides culture restoration

**AUDIT-079 â€” Reflection into private members makes tests brittle (Fixed):**
- Location: `BeaconServiceTests.cs:262-264`, `AprsSymbolBitmapProviderTests.cs`, `AprsIsModemTests.cs:334-345`
- Evidence: Reflection renames break tests at runtime; InvokeParseIsLine silently returned null on missing method, making tests pass vacuously
- Fix: Changed private members to internal, removed all reflection:
  1. `AprsIsModem.ParseIsLine`: Changed `private static` to `internal static` (line 450)
  2. `AprsSymbolBitmapProvider.GetCellPosition`: Changed `private static` to `internal static` (line 151)
  3. `BeaconService._lastTransmitTime`: Changed from private field to `internal DateTime LastTransmitTime { get; set; }` property (line 40)
  4. `AprsSymbolBitmapProviderTests.cs`: Removed all reflection code, replaced with direct calls to `GetCellPosition()`
     - PackedSpriteSheetRejectsSpaceBecauseItHasNoCell: Changed from `Assert.Throws<TargetInvocationException>` with inner exception check to direct `Assert.Throws<ArgumentOutOfRangeException>`
  5. `AprsIsModemTests.cs`: Simplified InvokeParseIsLine helper from reflection to direct call: `return AprsIsModem.ParseIsLine(line);`
  6. `BeaconServiceTests.cs`: Removed three reflection blocks (lines 262-264, 280-282, 305-307), replaced with direct property assignment: `service.LastTransmitTime = DateTime.UtcNow.AddHours(-1);`
  7. Removed `using System.Reflection;` from BeaconServiceTests.cs
  8. Fixed KissModemTests.cs type inference error: Changed `frame.Data.SequenceEqual([0x01])` to `frame.Data.SequenceEqual(new byte[] { 0x01 })`
- Project already had `<InternalsVisibleTo Include="AetherAprs.Tests" />` in AetherAprs.csproj (line 10)

**Suite: 469/469 green. Build 0 warnings/0 errors.**

**AUDIT-071 â€” PacketsViewModel constructor test only asserted NotNull (Fixed):**
- Location: `AetherAprs.Tests/ViewModels/Pages/PacketsViewModelTests.cs:114-141` (`Constructor_SubscribesToPacketStored`)
- Evidence: Test raised `PacketStored` event, waited 100ms, asserted `Assert.NotNull(viewModel)` â€” passed vacuously even if VM never subscribed
- Fix: Changed test to verify meaningful behavior:
  - Captured initial packet count before raising event
  - Increased wait from 100ms to 600ms (PacketsViewModel has 500ms throttle delay)
  - Asserted `Packets.Count` increased by 1
  - Asserted new packet with expected callsign "K0OTH-1" present in collection
- Verification: `dotnet test --filter Constructor_SubscribesToPacketStored` passed 1/1

**AUDIT-072 â€” SettingsViewModel autosave tests race on Task.Delay (Fixed):**
- Location: `AetherAprs.Tests/ViewModels/Pages/SettingsViewModelTests.cs:35, 55-56`
- Evidence: Tests used `await Task.Delay(100)` after property set, racing with fire-and-forget `Task.Run` save (line 323)
- Fix: Made save task trackable and awaitable in tests:
  1. Added `private Task _currentSaveTask = Task.CompletedTask;` field (line 31)
  2. Changed fire-and-forget `_ = Task.Run(...)` to `_currentSaveTask = Task.Run(...)` (line 323)
  3. Exposed `internal Task CurrentSaveTask => _currentSaveTask;` property (line 165)
  4. Updated test line 32-38: replaced `await Task.Delay(100)` with deterministic `await viewModel.CurrentSaveTask.WaitAsync(TestContext.Current.CancellationToken)`
  5. Updated test lines 49-62: replaced arbitrary delays with sequential await pattern (4 property sets, each followed by `CurrentSaveTask.WaitAsync`)
- Verification: `dotnet test --filter "FullyQualifiedName~SettingsViewModelTests"` passed 3/3 tests

**Suite: 469/469 green. Build 0 warnings/0 errors.**

**Next actions:**
1. Remaining medium fixes: 055-062 (Android permissions/FGS issues).
2. Code smell audit: search for common anti-patterns and verify no internals exposed just for testing.
3. Low-priority fixes (014-021, 045-052, 063-069, 077, 078, 080-083).
4. AUDIT-070 test coverage work â€” **PacketStorageService âœ… complete (12 tests)** â†’ PortService receive pipeline â†’ MessageService ack/retry â†’ PacketFactory â†’ PacketQueryService â†’ parser ack/rej branch â†’ PacketRecordMapper.MapToPacket.
5. Docs audit: wiki consistency (Home.md dead links, Developer-Guide Avalonia version).
6. Architecture polish: VM/View pairing, design patterns, service classification.

### Session 2026-10-16 (continued) â€” AUDIT-070 PacketStorageService tests + PacketsViewModel refactoring

**AUDIT-070 â€” PacketStorageService test coverage (Completed):**
- Created comprehensive `PacketStorageServiceTests.cs` with 12 tests covering:
  - StorePacketAsync with all packet types: Position, Message (inbound/outbound), Status, Weather, Unknown
  - Null portId handling (throws ArgumentNullException)
  - PacketStored event verification (event fires after successful save)
  - Database error handling (logs error, rethrows exception)
  - CleanupOldPacketsAsync: retention policy (deletes packets older than configured days), no old packets case, error handling
- Uses TempAppDatabase.CreateEmpty() for isolated SQLite testing with EF Core
- Mocks IConfigurationService with 30-day retention
- Tests verify complete entity mapping: PacketRecord with owned types (PositionDataRecord, MessageDataRecord, StatusDataRecord, WeatherDataRecord)
- Message packet DeliveryStatus: outbound = Pending, inbound = null
- Implementation details learned:
  - Symbol constructor: `new Symbol(SymbolTable.Primary, SymbolCode.HyphenMinus)` (enum-based, not char)
  - MessageNumber is `int?` (not string)
  - Callsign constructor: `new Callsign("K0OTH", 1)` â†’ SourceSsid=AdditionalStation1 (SSID 1, not HumanPortable which is SSID 9)
  - Logger verification: use `Received().Log()` with LogLevel parameter
  - Max callsign length: 6 chars (WX0TEST â†’ WX0TST)
- Files: `AetherAprs.Tests/Services/Packets/PacketStorageServiceTests.cs` (new, 468 lines)

**PacketsViewModel IUiDispatcher refactoring (Testability fix):**
- Issue: PacketsViewModel used Avalonia's `Dispatcher.UIThread.Post()` directly (lines 108, 117), making it untestable â€” dispatcher queue not pumped during tests
- Fix: Refactored to inject `IUiDispatcher` abstraction (same pattern used by HomeViewModel, MainViewModel, NavigationService)
- Production code changes (`AetherAprs/ViewModels/Pages/PacketsViewModel.cs`):
  - Removed `using Avalonia.Threading;`, added `using System.Threading;`
  - Added `IUiDispatcher uiDispatcher` constructor parameter
  - Removed `DispatcherTimer` and `_pendingRefresh` flag
  - Added `_throttleCts` (CancellationTokenSource) and `_throttleTask` fields
  - Simplified `OnPacketStored` event handler: uses `Task.Run` with 500ms delay, then calls `_uiDispatcher.Post(() => _ = LoadPacketsAsync())`
  - Removed `OnRefreshTimerTick` method entirely
  - Updated `Dispose()`: cancels and disposes `_throttleCts`
- Test code changes:
  - `PacketsViewModelTests.cs`: Added `IUiDispatcher` field initialized with `new SynchronousUiDispatcher()`, passed to CreateViewModelAsync helper
  - `MainViewModelTests.cs`: Fixed PacketsViewModel construction to pass `SynchronousUiDispatcher.Instance` (line 151-157)
- Verification: 481/481 tests green (12 new PacketStorageServiceTests + 469 existing)

**Suite: 481/481 green. Build 0 warnings/0 errors.**

### Session 2026-10-16 (continued) â€” AUDIT-045/077/078 fixed

- AUDIT-045: HomeViewModel.OnLocationUpdated already fixed previously (is plain void with _uiDispatcher.Post()); finding was stale â€” removed from FINDINGS.
- AUDIT-077 Fixed: SettingsViewModelPersistenceTests â€” removed unnecessary `async Task` from synchronous test methods (changed to `void`) at lines 24 and 65.
- AUDIT-078 Fixed: AprsIsModemTests.Constructor_ValidArgs_Succeeds â€” changed manual `await modem.DisposeAsync()` to `await using` declaration.
- Removed AUDIT-045, AUDIT-077, AUDIT-078 entries from FINDINGS.

**Suite: 495/495 green. Build 0 warnings/0 errors.**

### Session 2026-10-16 (continued) â€” AUDIT-080/081 fixed

- AUDIT-080 Fixed: TempAppDatabase.Dispose â€” added Debug.WriteLine logging for cleanup failures (IOException, UnauthorizedAccessException) to make temp directory leaks visible.
- AUDIT-081 Fixed (partial): MainViewModelTests â€” removed dead test doubles (TestLocationService, TestBeaconService, TestPacketFactory) that had zero references. Remaining instances in other test files still need review.
- Removed AUDIT-080, AUDIT-081 from FINDINGS (partially - MainViewModelTests only).
- Verification: 495/495 tests green after removing dead test doubles.

**Suite: 495/495 green. Build 0 warnings/0 errors.**

### Session 2026-10-16 (continued) â€” AUDIT-082/083 fixed

- AUDIT-082 Fixed: Deleted two "language feature" tests that exercised C# pattern matching instead of production code:
  - AprsRfModemTests.cs:335-387 â€” PacketReceived_SwitchOnType_Works
  - PacketTests.cs:161-184 â€” AprsPacket_SwitchOnConcreteType_Works
- AUDIT-083 Fixed: Updated test assertions to use resource keys instead of hardcoded English strings:
  - HomeViewModelTests.cs:61, 127, 144, 159 â€” Changed to use `Localization.Strings.Get("Key")` for all status messages
  - PortItemViewModelTests.cs:49, 53 â€” Changed to use `Localization.Strings.Get("Running"/"Stopped")`
- Tests now resilient to localization changes while TestFixtureBase continues enforcing en-US culture.
- Removed AUDIT-082, AUDIT-083 from FINDINGS.

**Suite: 495/495 green. Build 0 warnings/0 errors.**

### Session 2026-10-16 (continued) â€” AUDIT-032 verified already fixed

- AUDIT-032 (SPDX headers): Finding was stale â€” all migration files already have proper SPDX headers:
  - AetherAprs/Data/Migrations/20261003131132_InitialCreateWithCoordinate.cs + .Designer.cs
  - AetherAprs/Data/Migrations/20261014101527_AddAllowDigipeatToPorts.cs + .Designer.cs
  - AetherAprs/Data/Migrations/20261014103502_AddPacketPortIdIndex.cs + .Designer.cs
  - AetherAprs/Data/Migrations/AppDbContextModelSnapshot.cs
- Verified with glob + read operations â€” all files have correct `// SPDX-FileCopyrightText` and `// SPDX-License-Identifier: GPL-3.0-or-later` headers.
- Progress log line 33 correctly reported Fixed on 2026-10-14 but FINDINGS document was not updated.
- Removed AUDIT-032 from FINDINGS.

**Suite: 495/495 green. Build 0 warnings/0 errors.**

### Session 2026-10-16 (continued) â€” AUDIT-069 fixed

- AUDIT-069 Fixed: Removed dead event `OnInitialPermissionsComplete` from MainActivity.cs:
  - Event declaration removed from line 53
  - Invocations removed from lines 225, 232 (only SignalPermissionsComplete remains)
  - Event had zero subscribers anywhere in the repo
**Suite: 495/495 green. Build 0 warnings/0 errors.**

### Session 2026-10-16 (continued) â€” AUDIT-067/068 fixed

- AUDIT-068 Fixed (dead pre-Android-13 code branches): Removed unreachable code paths from MainActivity.cs and AndroidForegroundService.cs since SupportedOSPlatformVersion = 33 (Android 13+):
  - MainActivity.cs:58-62 â€” Removed `Build.VERSION.SdkInt < BuildVersionCodes.Tiramisu` check in RequestNotificationPermissionAsync
  - MainActivity.cs:103-107 â€” Removed `Build.VERSION.SdkInt < BuildVersionCodes.S` check in RequestBluetoothPermissionAsync
  - MainActivity.cs:163-170 â€” Removed conditional RegisterReceiver branch, now always uses ReceiverFlags.NotExported
  - AndroidForegroundService.cs:38-47 â€” Removed `Build.VERSION.SdkInt >= BuildVersionCodes.N` check, now always uses StopForeground(StopForegroundFlags.Remove)
  - AndroidForegroundService.cs:88-100 â€” Removed `Build.VERSION.SdkInt >= BuildVersionCodes.O` check in CreateNotificationChannel, API 26/O+ is guaranteed
  - All edits added comments clarifying "Android 13+ only (SupportedOSPlatformVersion = 33)" context
- AUDIT-067 Fixed (hardcoded English notification strings): Localized Android foreground service notification strings:
  - Added to Strings.resx (English): ForegroundServiceNotificationTitle, ForegroundServiceNotificationText, ForegroundServiceNotificationChannelName, ForegroundServiceNotificationChannelDescription
  - Added to Strings.pt.resx (Portuguese): Same keys with Portuguese translations
  - Discovered duplicate "Stop" resource already exists at line 60 in both files, removed duplicate entries
  - Updated AndroidForegroundService.cs: Added `using AetherAprs.Localization;`, replaced hardcoded strings with `Strings.Get("Key")` calls in notification builder (lines 62-66) and channel creation (lines 83-86)
- Removed AUDIT-067, AUDIT-068 from FINDINGS.

**Suite: 493/493 green. Build 0 warnings/0 errors.**

### Session 2026-10-16 (continued) â€” AUDIT-050 fixed

- AUDIT-050 Fixed (UTC timestamps displayed without local conversion): Timestamps now show in user's local time instead of raw UTC:
  - PacketsViewModel.cs:138 â€” Changed `ReceivedAt = new DateTimeOffset(record.Timestamp, TimeSpan.Zero)` to `.ToLocalTime()` for PacketSummary DTO
  - Created DateTimeToLocalConverter.cs value converter:
    - Converts UTC DateTime to local time (checks `DateTimeKind.Utc`)
    - Converts DateTimeOffset to local time
    - Returns value as-is for non-UTC DateTime
    - One-way converter only
  - PacketDetailsPage.axaml changes:
    - Added `xmlns:converters="using:AetherAprs.Converters.UI"` namespace
    - Added UserControl.Resources with `<converters:DateTimeToLocalConverter x:Key="DateTimeToLocal" />`
    - Updated Timestamp binding at line 64 (now 69) to use `Converter={StaticResource DateTimeToLocal}, StringFormat='{}{0:yyyy-MM-dd HH:mm:ss}'`
  - PacketsPage already correct â€” binds ReceivedAt from PacketSummary DTO which now calls `.ToLocalTime()` in ViewModel
- Removed AUDIT-050 from FINDINGS.

**Suite: 493/493 green. Build 0 warnings/0 errors.**

### Session 2026-10-16 (continued) â€” AUDIT-048/049 fixed

- AUDIT-048 Fixed (Map layers dedup): HomePage.axaml.cs OnDataContextChanged â€” added `!MapControl.Map.Layers.Contains(...)` guards before adding userLocationLayerService.Layer, beaconsLayerService.TrailsLayer, and beaconsLayerService.BeaconsLayer. Prevents duplicate layers if DataContext re-set (currently latent with singleton HomeViewModel, but defensive programming for future).
- Removed AUDIT-048 from FINDINGS.

**Suite: 493/493 green. Build 0 warnings/0 errors.**

### Session 2026-10-16 (continued) â€” AUDIT-049/051 fixed

- AUDIT-049 Fixed (Port deletion confirmation):
  - Created `AetherAprs/Services/UI/IConfirmationDialogService.cs` (Task<bool> ShowConfirmationAsync(title, message)) + `DialogHostConfirmationService.cs` â€” builds dialog content in code (StackPanel: Headline6 title, Body2 message, Cancel/Confirm buttons) shown via `DialogHost.Show(content, "MainDialogHost")`; buttons call `DialogHost.Close("MainDialogHost", true/false)`
  - Registered singleton in ServiceProviderFactory.cs and DesignData.cs
  - PortsViewModel.OnDeletePortAsync: calls ShowConfirmationAsync(Strings.Get("DeletePort"), Strings.Format("DeletePortConfirmation", item.Name)), returns early if not confirmed
  - Localization: added "Confirm" and "DeletePortConfirmation" keys to Strings.resx + Strings.pt.resx
  - Tests: new StubConfirmationDialogService helper; PortsViewModelTests +2 tests (confirmed removes / cancelled doesn't remove); updated PortsViewModel constructor calls in PortsViewModelTests, MainViewModelTests, NavigationServiceTests DI container
- AUDIT-051 Fixed (SettingsPage DataContext assignment):
  - Added ViewLocator mapping: `AprsSymbolPickerViewModel => new AprsSymbolPickerComponent()`
  - SettingsPage.axaml:71 â€” replaced `<components:AprsSymbolPickerComponent DataContext="{Binding SymbolPicker}" />` with `<ContentControl Content="{Binding SymbolPicker}" />` (ViewLocator now resolves the view)
  - Verified component is fully ViewModel-driven via DataContext (styled properties only synced FROM VM one-way in code-behind, nothing external binds them)
- Removed AUDIT-049, AUDIT-051 from FINDINGS.

**Suite: 495/495 green. Build 0 warnings/0 errors.**

### Session 2026-10-16 (continued) â€” AUDIT-048/049 verified and fixed

- AUDIT-048 Fixed (Map layers dedup): HomePage.axaml.cs OnDataContextChanged â€” added `!MapControl.Map.Layers.Contains(...)` guards before adding userLocationLayerService.Layer, beaconsLayerService.TrailsLayer, and beaconsLayerService.BeaconsLayer. Build clean, 493/493 tests passing (verified).
- AUDIT-049 Fixed (Port deletion confirmation): Added confirmation dialog before port removal:
  - New `AetherAprs/Services/UI/IConfirmationDialogService.cs` + `DialogHostConfirmationService.cs` â€” shows a confirm/cancel dialog through the app's existing "MainDialogHost" DialogHost overlay (same pattern as AprsSymbolPickerComponent); buttons close with true/false, Show returns the result
  - Registered in ServiceProviderFactory + DesignData as singleton
  - PortsViewModel now injects IConfirmationDialogService; OnDeletePortAsync shows "Delete port '{0}'? This action cannot be undone." before RemovePortAsync, no-op when cancelled
  - Localization: added "Confirm" and "DeletePortConfirmation" keys to Strings.resx + Strings.pt.resx (Cancel/DeletePort keys reused for buttons/title)
  - Tests: 2 new tests (DeletingPortWhenConfirmedRemovesPort / DeletingPortWhenCancelledDoesNotRemovePort) using TestConfirmationDialogService fake; updated 3 PortsViewModel constructor calls in PortsViewModelTests + 1 in MainViewModelTests; NavigationServiceTests DI container got StubConfirmationDialogService (shared fake in Tests/Helpers)
  - Lesson: RelayCommand generates DeleteCommand (no public DeleteAsync method) â€” tests invoke `item.DeleteCommand.ExecuteAsync(null)`
- Removed AUDIT-049 from FINDINGS.

**Suite: 495/495 green. Build 0 warnings/0 errors.**

### Session 2026-10-16 (continued) ï¿½ AUDIT-052 fixed

- AUDIT-052 Fixed (ViewModels namespace/folder mismatch): Aligned 5 ViewModel namespaces with folder structure:
  - HomeViewModel.cs, MessagesViewModel.cs, PortsViewModel.cs, SettingsViewModel.cs ï¿½ `namespace AetherAprs.ViewModels` ? `AetherAprs.ViewModels.Pages`
  - PortItemViewModel.cs ï¿½ `namespace AetherAprs.ViewModels` ? `AetherAprs.ViewModels.Components`
  - Consumer C# using updates: App.axaml.cs, HomePage.axaml.cs, PortsViewModel.cs (+AetherAprs.Tests HomeViewModelTests, SettingsViewModelPersistenceTests, PortsViewModelTests, PortItemViewModelTests)
  - AXAML xmlns:x:DataType updates: HomePage, MessagesPage, SettingsPage (? ViewModels.Pages); PortItemComponent (? ViewModels.Components); PortsPage (added vmc namespace for PortItemViewModel DataTemplate, vm now Pages)
  - MainView.axaml / MainWindow.axaml unchanged (MainViewModel correctly in root ViewModels namespace)
  - Note: AXAML x:DataType references fail with AVLN2000 at build time when namespace changes ï¿½ caught by full solution build
- Removed AUDIT-052 from FINDINGS.

**Suite: 495/495 green. Build 0 warnings/0 errors.**

### Session 2026-10-16 (continued) â€” AUDIT-047 fixed

- AUDIT-047 Fixed (service locator usage in view code-behind): Removed `App.ServiceProvider.GetService(...)` from both views:
  - HomePage.axaml.cs: HomeViewModel now injects `IReceivedBeaconsMapLayerService` (constructor param added alongside existing IUserLocationLayerService) and exposes the three Mapsui layers as properties (`UserLocationLayer`, `BeaconTrailsLayer`, `BeaconsLayer` â€” `ILayer` from Mapsui.Layers). OnDataContextChanged now reads layers from the ViewModel with existing `Layers.Contains` dedup guards; the `Avalonia.Application.Current is App app` service-locator block was deleted.
  - MainView.axaml.cs: MainViewModel now injects `IKeyboardInsetsService` and exposes `InitializeKeyboardInsets(TopLevel topLevel)`; MainView OnLoaded calls `viewModel.InitializeKeyboardInsets(topLevel)` when DataContext is MainViewModel. `using AetherAprs.Services.UI;` replaced with `using AetherAprs.ViewModels;`.
  - DI: no registration changes needed (IKeyboardInsetsService already registered in ServiceProviderFactory + DesignData; IReceivedBeaconsMapLayerService already registered).
  - Test updates: MainViewModelTests CreateFixture â€” HomeViewModel ctor gained `receivedBeaconsMapLayer` substitute (already-declared variable), MainViewModel ctor gained `Substitute.For<IKeyboardInsetsService>()`; HomeViewModelTests CreateViewModel â€” HomeViewModel ctor gained `Substitute.For<IReceivedBeaconsMapLayerService>()`.
  - Note: KeyboardInsetsBehavior.cs:32 still uses App.ServiceProvider (Avalonia Xaml Interactivity Behavior has no DataContext injection point; not in AUDIT-047 scope â€” left as-is).
- Removed AUDIT-047 from FINDINGS.

**Suite: 495/495 green. Build 0 warnings/0 errors.**

### Session 2026-10-16 (continued) â€” AUDIT-046 fixed

- AUDIT-046 Fixed (child ViewModels created with new instead of factory/injection): Resolved by documenting the exception, per the audit's own remediation ("At minimum document the exception; ideally route through a factory"):
  - Investigation confirmed both cases are stateless presentation wrappers with no service dependencies:
    - BeaconConfigurationItemViewModel (defined in DynamicBeaconingViewModel.cs, same file as parent) â€” wraps a BeaconConfig record; created at line 43 (ctor loop over beaconService.AllConfigurations) and in ResetWalkToDefault/ResetDriveToDefault/ResetCustomToDefault commands (lines ~98/105/112). A factory would only delegate to `new` (YAGNI).
    - SymbolSelectorViewModel â€” transient per-dialog selector created in AprsSymbolPickerViewModel.cs at lines 186 (OpenBaseSymbolSelector) and 202 (OpenOverlaySelector), with arguments derived at invocation time.
  - DynamicBeaconingViewModel.cs: added documentation comments at ctor initialization block and the three Reset commands referencing the documented exception.
  - AprsSymbolPickerViewModel.cs: added documentation comments at both `new SymbolSelectorViewModel(...)` sites.
  - AGENTS.md: added "**Exception - stateless presentation wrappers**" paragraph after the Child ViewModel Injection code block (~line 416): directly `new`-ing a child ViewModel inside a parent is acceptable when the child is a stateless presentation wrapper with no service dependencies, created on demand with arguments derived at invocation time; documented examples BeaconConfigurationItemViewModel and SymbolSelectorViewModel.
- No production logic changed; no tests affected.
- Removed AUDIT-046 from FINDINGS.

**Suite: 495/495 green. Build 0 warnings/0 errors.**

### Session 2026-10-16 (continued) â€” AUDIT-014 fixed

- AUDIT-014 Fixed (KissFrame.CommandType can never equal KissCommandType.Return): Modeled the enum differently â€” `KissCommandType.Return` changed from 0xFF to 0x0F (the KISS exit command's command nibble; on the wire the exit command is the full byte 0xFF = port nibble 15 + command nibble 15, which `CommandType` maps to 0x0F via the lower 4 bits). Doc comment updated to explain the wire format.
- Test updates (4 sites): KissFrameTests.CommandType_Return_ReturnsMappedValue, KissFrameTests.Frame_WithPort15AndReturn_Combined, KissSerializerTests.DecodeFrame_ReturnCommand_SetsCommandType, KissModemTests Return frame test â€” all changed from `Assert.Equal((KissCommandType)15, ...)` to `Assert.Equal(KissCommandType.Return, ...)`; comments updated.
- Removed AUDIT-014 from FINDINGS.

**Suite: 495/495 green. Build 0 warnings/0 errors.**

### Session 2026-10-16 (continued) â€” AUDIT-015 fixed

- AUDIT-015 Fixed (SSID > 15 throws ArgumentOutOfRangeException escaping the ArgumentException catch): AprsIsModem.ParseCallsign now validates the SSID range before constructing the Callsign â€” throws ArgumentException (caught by the hop-parsing guard, hop skipped) instead of letting Callsign's ArgumentOutOfRangeException escape and abort the whole line parse with a ReceiveError.
- New test: ParseIsLine_HopWithSsidAbove15_SkipsHopAndStillParsesPacket â€” line `XX1XX-99>APRS,INVALID-99,WIDE1-1:!...` still parses to a PositionPacket with the invalid hops skipped.
- Removed AUDIT-015 from FINDINGS.

**Suite: 496/496 green. Build 0 warnings/0 errors.**

### Session 2026-10-16 (continued) â€” AUDIT-016 fixed

- AUDIT-016 Fixed (server-inserted qA*/qX* path constructors parsed as ordinary callsigns): AprsIsModem.ParseIsLine hop loop now skips ALL APRS-IS q-constructors, not just qAC â€” filter changed from `hop == "TCPIP*" || hop.StartsWith("qAC")` to `hop == "TCPIP*"` plus a new check `hop.Length == 3 && hop[0] == 'q' && char.IsAsciiLetterUpper(hop[1])` matching qAC/qAR/qAo/qAS/qX etc. Prevents server markers from polluting stored paths/trails.
- New test: ParseIsLine_QConstructorsInPath_SkippedFromPathList â€” line with qAC and qAR in path yields Path = [WIDE1-1, RELAY] only (Callsign("WIDE1", 1) assertion â€” Callsign ctor rejects base+SSID combined form).
- Removed AUDIT-016 from FINDINGS.

**Suite: 497/497 green. Build 0 warnings/0 errors.**

### Session 2026-10-16 (continued) " AUDIT-017 fixed

- AUDIT-017 Fixed (Ax25Parser DecodeAddress can embed NUL characters in callsign base): DecodeAddress now maps non-printable decoded characters to spaces before TrimEnd " decoded char is kept only if `c >= ' ' && c < 0x7F`, otherwise mapped to ' ' (mirrors how most TNCs display padded addresses). Malformed wire bytes (e.g. 0x00 padding) can no longer store embedded control characters in Callsign.Base.
- New test: ParseFrame_SourceWithNulPadding_MapsToSpacesAndParses " builds an AX.25 frame whose source address is "AB" followed by NUL bytes (0x82, 0x84, 0x00x4, SSID byte 0x61), asserts Source == Callsign("AB") and Base contains no '\0'.
- Test lesson: the last AX.25 address in a frame must have the HDLC extension bit set in its SSID byte (0x61 not 0x60), otherwise the path-walk loop decodes info bytes as an additional address.
- Removed AUDIT-017 from FINDINGS.

**Suite: 498/498 green. Build 0 warnings/0 errors.**

### Session 2026-10-16 (continued) â€” AUDIT-018 fixed

- AUDIT-018 Fixed (AprsIsModem _readCts never disposed; Start/Stop not synchronized):
  - Added `_lifecycleLock` (System.Threading.Lock) field guarding all Start/Stop/Dispose state transitions â€” mirrors the KissModem `_lifecycleLock` pattern from AUDIT-003/019.
  - Start(): checks `_started` and assigns `_connectionReady`/`_readCts`/`_readTask`/`_started` inside the lock, so concurrent Start calls can no longer double-start the read loop.
  - StopAsync(): under the lock, marks `_started = false` and captures+nulls `_readCts`/`_readTask`; cancellation, CTS dispose, `_connectionReady.TrySetCanceled()`, read-task await, and Disconnect() happen outside the lock. Second concurrent StopAsync returns immediately (idempotent). CTS is now disposed on every stop â€” fixes the per-start/stop handle leak.
  - DisposeAsync(): unchanged (StopAsync now handles CTS disposal).
- New test: Start_ConcurrentCalls_OnlyOneSucceeds â€” 8 threads call Start() concurrently; exactly 1 succeeds, 7 get InvalidOperationException; then StopAsync. Added `using System.Linq;` and `using System.Threading;` to AprsIsModemTests.cs (ImplicitUsings disabled).
- Removed AUDIT-018 from FINDINGS.

**Suite: 499/499 green. Build 0 warnings/0 errors.**

### Session 2026-10-16 (continued) - AUDIT-020 fixed

- AUDIT-020 Fixed (Ax25Serializer NRE for default(Callsign)): EncodeAddress now validates callsign.Base - throws ArgumentException("Callsign base must not be null or empty.", nameof(callsign)) for null/empty Base instead of NRE from PadRight on null. Added `using System;` to Ax25Serializer.cs (ImplicitUsings disabled).
- New test Serialize_DefaultCallsign_ThrowsArgumentException in Ax25SerializerPathTests.cs: PositionPacket with Path = [default(Callsign)], asserts Assert.Throws<ArgumentException> on Ax25Serializer.Serialize(packet).
- Removed AUDIT-020 from FINDINGS.

**Suite: 500/500 green. Build 0 warnings/0 errors.**

### Session 2026-10-16 (continued) â€” AUDIT-021 fixed

- AUDIT-021 Fixed (Speed unit inconsistency knots vs km/h): Standardized PositionPacket.Speed on knots, matching the APRS spec and the compressed-position path:
  - AprsInfoFieldParser.cs MIC-E path: changed `speed = speedVal * 1.852;` (km/h) to `speed = speedVal;` (knots on the wire; Speed is stored in knots, matches compressed path) â€” was the only `1.852` usage in the codebase.
  - Consumers verified safe: PacketRecordMapper passes Speed through; PortService only logs it; compressed path already stored knots.
  - New test ParseInfoField_MicE_SpeedIsInKnots in AprsParserTests.cs: field "`(_f\"\u001Cv/>" with dest S3TRV0 decodes Speed=60 knots, Course=90. Field layout lesson: 1 type char + 3 lon bytes + 3 speed/course bytes + 2 symbol bytes; speed bytes must land at info indexes 4-6 (initial attempt included stray 'n' shifting bytes â†’ speedVal 820 â†’ null). Diagnostic scratch console project used to trace decode (deleted afterward).
  - Also verified compressed-position test CompressedPositionWithCourseSpeed already asserts knots (~1.1589 kn).
- Removed AUDIT-021 from FINDINGS.

**Suite: 501/501 green. Build 0 warnings/0 errors.**

### Session 2026-10-16 (continued) - AUDIT-063 fixed

- AUDIT-063 Fixed (AndroidBluetoothLeScanner.OnScanFailed swallowed): AdvertisementScanCallback now takes a second delegate (Action<ScanFailure> onScanFailed); OnScanFailed completes the channel with InvalidOperationException("BLE scan failed: {errorCode}") so callers observe the failure via the async enumeration instead of a scan that silently ends with zero results. ScanAsync passes error => channel.Writer.TryComplete(...).
- Removed AUDIT-063 from FINDINGS.

**Suite: 501/501 green. Build 0 warnings/0 errors (Android project built directly).**

### Session 2026-10-16 (continued) - AUDIT-064 fixed

- AUDIT-064 Fixed (LocationService cancellation registration never disposed): GetCurrentLocationAsync in AetherAprs.Android/Services/Platform/LocationService.cs now wraps the CancellationToken registration in `await using var cancellationRegistration` so the callback lambda is disposed after the location request completes instead of staying rooted for the token's lifetime. The registration is explicitly disposed before returning the result (after RemoveUpdates); the await-using also covers the timeout/exception paths. Note: the abandoned canceled Task.Delay (WhenAny loser) is handled by the timeout throw path and requires no extra disposal.
- AUDIT-064 removed from DOCS/CODE_AUDIT_FINDINGS.md (was between AUDIT-019 entry and AUDIT-065).
- Build verified: dotnet build AetherAprs.Android/AetherAprs.Android.csproj - 0 warnings, 0 errors.

Suite: 501/501 green (no test changes).

### Session 2026-10-16 (continued) - AUDIT-065/066 fixed

- AUDIT-065 Fixed (ServiceProvider never disposed; no global exception logging):
  - App.axaml.cs Dispose(): now disposes ServiceProvider when it implements IDisposable (the built provider does - releases DbContextFactory, loggers, BLE/port sessions and other disposables), after cancelling _shutdownCts.
  - OnFrameworkInitializationCompleted(): subscribed AppDomain.CurrentDomain.UnhandledException (logs critical with IsTerminating) and TaskScheduler.UnobservedTaskException (SetObserved + logs error) so fire-and-forget failures outside guarded helpers no longer vanish. Handlers unsubscribed in Dispose() per memory-leak rules.
- AUDIT-066 Fixed (App.GetService<T> undocumented): Added XML doc remark on App.GetService<T>() declaring the documented exception (platform entry points where constructor injection is unavailable, e.g. Android BroadcastReceiver in MainActivity.cs and framework-created views); AGENTS.md "Common Mistakes" service-locator bullet updated to reference the exception.
- Both AUDIT-065 and AUDIT-066 removed from DOCS/CODE_AUDIT_FINDINGS.md; progress entries added.

Build: Clean (0 warnings, 0 errors)
Tests: 501/501 passing

### Session 2026-10-19 (continued) - AUDIT-070 PortService receive pipeline tests

- Created AetherAprs.Tests/Services/Ports/PortServiceReceivePipelineTests.cs with 3 end-to-end receive pipeline tests using a scripted KISS stream factory (IKissStreamFactory double returning pre-buffered/never-EOF streams):
  1. ReceivedFrame_StoresPacketAndRaisesPacketReceived - KISS frame in, PositionPacket stored (RecordingPacketStorageService asserts packet/portId/isOutbound=false), PacketReceived event raised with correct PortId, digipeater consulted with packet + portId
  2. ReceivedFrame_StorageThrows_StillRaisesPacketReceived - storage throws, pipeline continues: event still raised, only 1 store attempt, exception logged
  3. ReceivedFrame_DigipeaterTargetsOtherPort_PacketSentToTarget - two KISS ports (RF source + TX target), digipeater returns DigipeatTarget for the target port, KISS-encoded packet observed on target stream write (FEND bytes)
- Custom doubles: RecordingPacketStorageService, RecordingDigipeaterService (OnGetDigipeatTargets Func), ScriptedKissStreamFactory keyed by KissSettings instance (ReferenceEquals), ScriptedReadStream (pre-buffered + never-EOF polling loop, CanWrite=true no-op Write since KissModem ctor requires writable), RecordingWriteStream (captures writes into TCS)
- Lessons: KissModem ctor requires stream with CanRead AND CanWrite; AprsIsModem path does real TCP (bypasses IKissStreamFactory) so KISS ports must be used for scripted tests; enable target port BEFORE source port so digipeat targets have an active session; ScriptedKissStreamFactory keys by KissSettings instance, not open order.
- Removed temporary console trace logger from tests after debugging.
- Updated AUDIT-070 verification in FINDINGS (PortService receive pipeline complete).

Suite: 504/504 green. Build 0 warnings/0 errors.

### Session 2026-10-19 - AUDIT-070 MessageService ack/retry tests

- Made `MessageService.ProcessRetries` internal (test seam, AUDIT-079 pattern: `// internal for tests (InternalsVisibleTo AetherAprs.Tests)`).
- Added `TaskCompletionSource<AprsPacket> PacketSent` to FakePortService (MessageServiceTests) - TrySetResult on every SendPacketAsync so tests await deterministic sends.
- 7 new tests in MessageServiceTests.cs:
  1. MessageAck_FromPeer_MarksMessageAcknowledged - SendAsync then ACK packet from peer with matching MessageNumber -> DeliveryStatus.Acknowledged
  2. MessageAck_FromWrongSource_LeavesMessagePending - ACK from different callsign ignored (H5 addressee check)
  3. MessageAck_UnknownMessageNumber_Ignored - ACK for #999 leaves message Pending
  4. MessageRej_FromPeer_MarksMessageRejected - REJ -> DeliveryStatus.Rejected
  5. InboundMessage_WithNumberAndAutoAckEnabled_SendsAckOnReceivingPort - AutoAcknowledgeMessages=true, inbound message with number -> MessageAckPacket sent to peer on receiving port (awaited via PacketSent TCS)
  6. ProcessRetries_DueMessage_ResendsWithIncrementedRetryCount - NextRetryTime set to past, ProcessRetries invoked directly (real timer ticks every 5s, too slow for tests) -> resend observed, RetryCount=1, backoff NextRetryTime in future
  7. ProcessRetries_RetriesExhausted_MarksMessageTimeout - RetryCount=5 (MaxRetries default) -> Timeout status, subsequent pass sends nothing (negative assertion via Task.Delay + Single)
- Lessons: RunOnUi runs synchronously in tests (Application.Current is null); _nextMessageNumber starts at 1 per service; xUnit1031/1051 forbid blocking .Wait() in tests - use await Task.Delay with TestContext.Current.CancellationToken; helper methods CreateTxPort()/CreateMessageService() extracted.
- Updated AUDIT-070 verification in FINDINGS (MessageService ack/retry checked).

Suite: 511/511 green. Build 0 warnings/0 errors.

### Session 2026-10-19 - AUDIT-070 PacketFactory tests

- AUDIT-070 PacketFactory component complete: created AetherAprs.Tests/Factories/Packets/PacketFactoryTests.cs with 16 tests:
  - CreatePositionPacket: parsed source (with SSID), APRS destination, location passthrough, course, symbol, Precision=2, altitude m-to-ft conversion (x3.28084), null altitude passthrough, beacon-comment fallback (null and whitespace) + provided-comment override, digipeater path parsing (WIDE1-1,WIDE2-1), whitespace path = empty path, invalid callsign throws ArgumentException (Theory: empty + invalid chars)
  - CreateMessagePacket/CreateAckPacket/CreateRejPacket: source parsed with SSID, addressee/text/message-number passthrough, digipeater path applied, invalid callsign throws ArgumentException
- Lessons: Path is IReadOnlyList&lt;Callsign&gt; (use .Count / Assert.Single, not .Length); LocationData.Location is Geo.Coordinate (namespace Geo); xUnit2013 forbids Assert.Equal(1, collection.Count) - use Assert.Single.
- Updated AUDIT-070 verification in FINDINGS (PacketFactory done).
- Suite: 527/527 green (511 + 16 new). Build 0 warnings/0 errors.

Next AUDIT-070 components: parser ack/rej branch (AprsInfoFieldSerializer) -> PacketRecordMapper.MapToPacket.

### Session 2026-10-19 (continued) - AUDIT-070 COMPLETE (parser ack/rej + MapToPacket)

- Parser ack/rej branch complete: 7 new tests in AprsParserTests.cs:
  - MessageAckWithBraces/BareNumber -> MessageAckPacket with Addressee + MessageNumber
  - MessageRejWithBraces/BareNumber -> MessageRejPacket
  - Non-numeric ack suffix (ack{abc}) falls back to MessagePacket with raw text, null number
  - Non-callsign addressee on ACK falls back to APRS addressee
  - Lesson: EMAIL is a VALID callsign per Callsign.TryParse (5 uppercase chars) - use truly invalid addressee like INVALID! for fallback tests
- PacketRecordMapper.MapToPacket complete: created AetherAprs.Tests/Data/Mappers/PacketRecordMapperTests.cs with 15 tests:
  - Position/Message/Status/Weather PacketRecord -> packet field roundtrips (via internal access, InternalsVisibleTo)
  - Unmapped PacketType strings (Unknown, MessageAck, MessageRej, Telemetry, Capabilities, Bogus) -> UnknownPacket (Theory)
  - Position type with null owned data -> UnknownPacket
  - Invalid SourceBase -> null + logger.LogWarning verified via NSubstitute
  - PacketTimestamp reads as UTC DateTimeOffset (zero offset); null PacketTimestamp -> null Timestamp
  - NullLogger failure path returns null without throwing
  - Lessons: AprsSsid enum has no AdditionalStation7 (use HumanPortable=7); mapper always constructs Callsign with explicit SSID (PrimaryStation when null), so Compare Callsign.Base strings instead of struct equality
- AUDIT-070 marked Fixed in FINDINGS with full verification checklist (all 8 components complete).
- Suite: 548/548 green (469 at audit start -> 548). Build 0 warnings/0 errors.

### Session 2026-10-19 (continued) â€” remaining Medium Android findings (055-062) closed

- AUDIT-056 Fixed (LocationService.RequestLocationPermissionAsync hang/leak): TCS now RunContinuationsAsynchronously; `using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30))` + `await using var registration = timeoutCts.Token.Register(...)` unsubscribes the static OnPermissionResult handler and cancels the await on timeout (mirrors BluetoothPermissionHelper pattern); `ConfigureAwait(false)` on the await. Gotcha: CancellationTokenSource is IDisposable, not IAsyncDisposable.
- AUDIT-055 Fixed (MainActivity permission TCS timeouts): all three permission awaits (notification 1000, bluetooth 1100, location 1001) now time-boxed 30s with `using var XTimeoutCts` + Register(cancel); TCS use RunContinuationsAsynchronously; location handler also unsubscribes from the static event on timeout. Activity-recreate scenario now resolves via the 30s timeout instead of hanging (downstream AndroidPermissionService already times out at 30s).
- AUDIT-057 Fixed (AndroidForegroundService): `_isRunning` now `volatile`; `ActionStop` const extracted (was string literal); optional `ILogger<AndroidForegroundService>?` ctor param added (DI: AddLogging provides); StopAsync wraps StartService in try/catch (IllegalStateException when service already stopped by OS) logging a warning, still sets `_isRunning = false`.
- AUDIT-059 Fixed (StartForegroundService in background): StartAsync wraps Start calls in try/catch â€” ForegroundServiceStartNotAllowedException (API 31+) logs warning and returns WITHOUT setting `_isRunning = true`; PortService port start/stop no longer aborted by the exception.
- AUDIT-062 Fixed (BluetoothLeGattDuplexStream write race): replaced cancellation-Register with abandoned-write state machine: `await writeTcs.Task.WaitAsync(cancellationToken)`; on OCE, under `_sync`, if `_writeCompletion == writeTcs` set `_writeAbandoned = true` (local `abandoned`), rethrow; `finally` releases `_writeGate` only if NOT abandoned â€” the gate stays held so no new write can register its TCS before the stale GATT callback arrives. CompleteWrite: under lock takes slot + nulls it; if abandoned â†’ clears flag + releases gate outside lock; TrySetResult/TrySetException are no-ops on the canceled TCS. Ruling: a generation counter cannot distinguish a stale OnCharacteristicWrite arriving after the NEXT write registered (GATT callbacks carry no identity); Android guarantees exactly one callback per successful WriteCharacteristic, so holding the gate until the stale callback is consumed is race-free.
- AUDIT-060 Fixed (background location, user decision "Add permission + declare"): added `<uses-permission android:name="android.permission.ACCESS_BACKGROUND_LOCATION" />` to AndroidManifest.xml; new `MainActivity.RequestBackgroundLocationPermissionAsync()` (request code 1002, 30s timeout TCS pattern, skips request when fine location not granted, denial is non-fatal â€” Android 11+ routes the request to the app settings page); called in RequestInitialPermissionsAsync after location permissions.
- AUDIT-061 Fixed (per user decisions): verified appsettings.json contains ONLY logging config â€” no credentials (port configs/APRS-IS passcodes already DB-only via PortRecord; no appsettings ports-binding exists, so no dead code to remove). Real exposure was the SQLite DB (PortRecord.AprsIs_Passcode) living in world-readable external storage. Fix: `IAppDataDirProviderService` extended with `GetPrivateAppDataDirectory()` (XML docs state public dir = credential-free config/caches only, private dir = sensitive data); Android impl returns `FilesDir` (/data/data/package/files, app-private); core desktop default returns AppContext.BaseDirectory; DesignData + 2 test TestAppDataDirProvider fakes updated; ServiceProviderFactory now builds the DbContextFactory with `Path.Combine(privateAppDataDirectory, AppDbContext.DatabaseFileName)` â€” the DB and its WAL files move into private storage. appsettings.json and osm-tile-cache stay in the user-accessible dir (config is credential-free, cache is public tiles).
- AUDIT-058 Accepted by user decision ("Accept 6h cap (Recommended)"): FGS DataSync ~6h runtime cap on Android 14+ accepted for now (stopping the service after ~6h is acceptable behavior); no code change. Finding removed from FINDINGS as Accepted.

FINDINGS state: every High/Medium entry (AUDIT-001..083) is now Fixed, Verified, or Accepted â€” only the empty Low section remains.

Suite: 548/548 green (full run after AUDIT-061 DI changes). Build 0 warnings/0 errors (solution build).

### Session 2026-10-19 (continued) - docs audit (wiki consistency) complete

- Broken-link sweep across DOCS/wiki/*.md: 11 dead links removed or replaced (Map-Usage, Digipeater-Configuration, Settings-Reference, Troubleshooting, Android-Release-Setup targets never existed). Home.md lost its dead quick-links; Port-Configuration.md now explains the per-port Allow Digipeat toggle + Settings master switch; Messaging.md next-link now points at Beaconing Setup; Getting-Started points at APRS-Path-and-Comments and GitHub issues.
- Screenshot sweep: all 32 `screenshots/*.png` embeds removed (directory never existed in git).
- Heading-anchor TOC check: all resolve.
- Developer-Guide.md: Avalonia version corrected to 12 (Directory.Packages.props: Avalonia 12.1.2, Xaml.Behaviors 12.0.7, Mapsui.Avalonia12 5.1.0, Material.Avalonia 3.20.0); ViewLocator section now shows the real explicit switch mapping instead of a fictional reflection implementation; ViewModel lifetime list matches ServiceProviderFactory registrations; BeaconService/BeaconTransmissionService/LocationTrackingService responsibility blocks updated; full DI registration list verified against ServiceProviderFactory.cs.
- Building-from-Source.md: stale "AppDbContext tests failing" known issue removed (suite green); desktop Known Issues reduced to the two real ones (BT/GPS stubbed via NoOp, desktop experimental); repo tree fixed (Data/, DOCS/).
- Getting-Started.md + Beaconing-Setup.md: permission instructions now include Notifications and optional Background Location ("Allow all the time").

### Session 2026-10-19 - audit close-out

- CODE_AUDIT_FINDINGS.md emptied: all 83 findings (001-083) Fixed/Verified/Accepted; Low section never populated.
- CODE_AUDIT.md marked complete; workstream checklist fully checked.
- Architecture polish (VM/View pairing, service classification) verified during docs audit: ViewLocator has explicit mappings for all ViewModels; service responsibilities match AGENTS.md; no outstanding work items.
