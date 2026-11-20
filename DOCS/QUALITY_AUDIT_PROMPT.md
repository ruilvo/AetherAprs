<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC-BY-SA-4.0
-->
# AetherAprs Full Quality Audit Prompt

A reusable, self-contained prompt that instructs an AI agent to perform an exhaustive
quality audit of the AetherAprs codebase against its architecture and good
design/programming principles.

> **How to use:** copy everything between `<!-- PROMPT START -->` and
> `<!-- PROMPT END -->` and paste it into an agent. It is intentionally self-contained
> (it tells the agent which files to read first and what to check) so it can be run in a
> fresh session with no extra context.

---

<!-- PROMPT START -->

You are performing a full quality audit of the **AetherAprs** codebase. Your goal is to
produce a rigorous, evidence-based review of architecture conformance, code quality,
design, security, and test health — not to fix anything (unless explicitly asked
afterwards). Be exhaustive, be specific, and cite file paths and line numbers for every
finding.

## Step 0 — Orient yourself (read these first, in order)

Before auditing anything, read these files completely:

1. `AGENTS.md` — the authoritative instruction file: build commands, critical
   requirements (REUSE, ImplicitUsings, central package management), architecture
   overview, MVVM/DI rules, naming conventions, memory-leak rules, localization rules,
   async rules, and behavioral guidelines (Think Before Coding / Simplicity First /
   Surgical Changes / Goal-Driven Execution).
2. `CODEBASE_STRUCTURE.md` — folder organization and namespace conventions.
3. `Directory.Build.props`, `Directory.Packages.props`, `global.json` — build/packaging
   configuration.
4. `opencode.json` — note the `references` (upstream repos exposed as `@material-avalonia`,
   `@maps-avalonia-12`, `@xunit-v3`, `@bruTile`, `@geo`) and the `instructions` array.
5. `DOCS/ROADMAP.md` and `DOCS/TESTING.md` — intended direction and testing/SIL strategy.
6. Skim the solution layout (`AetherAprs/`, `AetherAprs.Android/`, `AetherAprs.Tests/`).

If you need the exact API or behavior of an external dependency (Material.Avalonia,
Mapsui, xUnit v3, BruTile, Geo), consult the corresponding `@mention` reference — do not
guess from memory.

## Step 1 — Inventory

Produce a factual inventory before judging:

- Project count, target frameworks, and any `TargetFramework` that looks mismatched.
- Number of source files per project; number of test files.
- List of registered services (from `Factories/ServiceProviderFactory.cs`) and
  design-time services (from `DesignData.cs`).
- List of platform-abstracted interfaces (core) and their Android implementations.
- List of EF Core entities, migrations, and DbContext/factory.
- List of ViewModels and their corresponding Views.

## Step 2 — Audit dimensions

For each dimension below, report **Findings**, each with a **Severity** and **Location**.
Use severity labels consistently:

- **Blocker** — violates a stated non-negotiable rule, breaks the build/architecture, or
  is a correctness/security bug.
- **Major** — material design or quality defect that should be fixed soon.
- **Minor** — style, naming, or convention drift with no functional impact.
- **Info** — observation, suggestion, or question.

### A. Architecture conformance (highest priority)

Check against the **two-flow architecture** and service responsibilities in `AGENTS.md`:

1. **RECEIVE flow** — every path must be `Port → PortService → PacketStorageService →
   SQLite`. Confirm no `Port` (modem/connector) writes to the database directly, and
   `PacketStorageService` is the ONLY writer of packet data.
2. **READ flow** — all packet reads must go `PacketQueryService → ViewModels`. Confirm no
   ViewModel, View, or map-layer code touches `AppDbContext`, `IDbContextFactory`, or EF
   Core directly. Flag any `using Microsoft.EntityFrameworkCore` inside `ViewModels/` or
   `Views/`.
3. **TRANSMIT flow** — all transmission must go through `PortService.SendPacketAsync(...)`.
   Flag any direct `IPort`/modem `Send*` calls from ViewModels/services.
4. **Service responsibilities** — verify each service matches its documented contract
   (`PortService`, `PacketStorageService`, `PacketQueryService`, `MessageService`,
   `BeaconService`, `BeaconTransmissionService`, `LocationTrackingService`,
   `ReceivedBeaconsMapLayerService`, `UserLocationLayerService`, `KeyboardInsetsService`,
   `PermissionService`, `DigipeaterService`, `PacketFactory`). Flag scope creep or
   misplaced logic.
5. **MVVM boundaries** — ViewModels must contain UI state, commands, presentation data,
   and service orchestration ONLY. Flag any business logic, packet-processing algorithms,
   port management, network operations, or data access inside ViewModels.
6. **Platform boundary** — the core `AetherAprs/` project must contain zero `Android.*`
   references. Every platform capability must be an interface in core with a NoOp/default
   in `Factories/ServiceProviderFactory.cs` and a concrete Android impl registered via
   `RegisterPlatformServices()`. Flag core files that reference platform-specific APIs.

### B. Dependency injection & composition

1. Flag any **service locator** usage (`App.GetService<>()` or
   `IServiceProvider.GetRequiredService<>()` in regular code). The only allowed exceptions
   are platform entry points (e.g. Android `BroadcastReceiver`, framework-created views) —
   verify each occurrence is genuinely one of these and not a workaround.
2. Verify **child ViewModels are injected** via constructor, not `new`ed inside a parent
   (allowed documented exception: stateless presentation wrappers such as
   `BeaconConfigurationItemViewModel` and `SymbolSelectorViewModel`).
3. Verify transient ViewModels that need post-construction initialization use the
   **factory pattern** (`I*ViewModelFactory`) and not service locator.
4. Check lifetime choices (Singleton vs Transient) are appropriate: page ViewModels
   singleton; dialogs transient; services match their statefulness.
5. Verify every ViewModel is registered in **all three places**: `ServiceProviderFactory.cs`
   (runtime), `DesignData.cs` (design-time, with an exposed public property), and
   `ViewLocator.cs` (mapping). Flag any missing registration.

### C. Structure, namespaces & placement

1. Confirm **namespace matches folder structure** exactly (e.g. `Data/Entities/` →
   `AetherAprs.Data.Entities`; `Services/Ports/` → `AetherAprs.Services.Ports`). Flag any
   file whose namespace disagrees with its path.
2. Confirm files live in the correct domain folder per `CODEBASE_STRUCTURE.md`
   (services by domain, packets under `Models/Aprs/Packets/`, mappers under
   `Data/Mappers/`, converters under `Converters/{Aprs|UI}/`, factories under
   `Factories/{Packets|ViewModels}/`, etc.).
3. Flag misplaced files, orphans, and duplicate/parallel implementations of the same
   concern.

### D. Naming & conventions

Check against the `C# Naming Conventions` section of `AGENTS.md`:

1. Private fields use `_camelCase`; private static fields use `_camelCase`.
2. Public/protected properties and methods use `PascalCase`; private methods use
   `PascalCase`.
3. Parameters and local variables use `camelCase`.
4. Constants use `PascalCase`; enums/records/interfaces/classes use `PascalCase`;
   interfaces start with `I`; enum members use `PascalCase`.
5. No `this.` prefix when `_camelCase` fields distinguish members.
6. `[ObservableProperty]` uses **partial properties**, not backing fields. Flag any
   `[ObservableProperty] private string _foo;` pattern.
7. Flag inconsistent or misleading identifiers (names that don't match behavior).

### E. REUSE / SPDX license compliance

1. Confirm every source file (`*.cs`, `*.axaml`, `*.csproj`, `*.slnx`, YAML/config) has a
   correct SPDX header matching its type (GPL-3.0-or-later for code, CC0-1.0 for
   JSON/config, CC-BY-SA-4.0 for docs), or a companion `.license` file where inline
   comments are not allowed (e.g. `opencode.json.license`).
2. Note any file lacking a header (pre-commit `reuse-lint-file` would reject it).
3. Note any asset/third-party content lacking a declared license (e.g. the APRS symbol
   bitmaps — verify `Assets/Aprs/COPYRIGHT.md` and the `LicenseRef-AprsSymbols` entries).

### F. Localization compliance

1. Confirm **all user-facing strings** are localized — AXAML uses `{loc:Loc StringKey}`
   and C# uses `Strings.Get(...)` / `Strings.Format(...)`.
2. Flag any hardcoded English (or other) UI text in AXAML or code.
3. Confirm every new key exists in BOTH `AetherAprs/Localization/Strings.resx` (English)
   and `Strings.pt.resx` (Portuguese), with consistent naming (title/section/field/
   `Description`/`Placeholder` suffixes).
4. Confirm the correct non-localization of log messages, exception messages, comments,
   config keys, and technical identifiers.

### G. Async/await correctness

1. Flag `async void` except where it is genuinely an event handler, and confirm those
   handlers wrap their body in try/catch (exceptions in `async void` crash the app).
2. Flag fire-and-forget calls (`_ = SomeAsync(...)`) that lack try/catch + logging + state
   recovery, or that run without a cancellation token and survive disposal.
3. Check `.Result`, `.Wait()`, and `.GetAwaiter().GetResult()` for deadlock risk on a UI
   thread.
4. Check that long-running background work takes and respects a `CancellationToken`, and
   that disposal cancels and disposes its `CancellationTokenSource`.
5. Check `ConfigureAwait` discipline where a synchronization context matters (Avalonia
   UI continuations).
6. Flag `async` methods that are effectively synchronous (no `await`) or that
   `await`-then-block unnecessarily.

### H. Resource & memory management

1. **Event subscriptions** — every `+=` must have a matching `-=` in disposal or on source
   change. Flag lambdas subscribing to the object's own `PropertyChanged` (must use
   CommunityToolkit partial methods instead).
2. **IDisposable/IAsyncDisposable** — verify disposable resources (streams, sockets,
   DbContext, layers, `CancellationTokenSource`, semaphores) are disposed; verify
   thread-safe disposal (capture-then-null).
3. Flag potential leaks: static caches that grow unbounded, retained event handlers,
   undisposed `TcpClient`/`NetworkStream`/`Stream` in modem/connector paths.
4. Flag `GC.SuppressFinalize` misuse or missing `Dispose(bool)` patterns where a finalizer
   exists.

### I. Database & EF Core

1. Confirm timestamps are stored as **`DateTime` (UTC)**, not `DateTimeOffset` (SQLite
   LINQ translation issues). Flag `DateTimeOffset` columns/orderings.
2. Confirm queries translate to SQL (no client-side `.ToList()` then in-memory filtering
   where the DB should do it; no untranslatable LINQ patterns).
3. Check index coverage for hot queries (`PacketQueryService` — most-recent-per-callsign,
   by-port, by-callsign history) and flag potential N+1 or full-table scans.
4. Confirm migrations are complete and the model snapshot matches the current entities
   (no pending schema drift).
5. Confirm mappers (`Data/Mappers/`) are the only place domain ↔ entity conversion
   happens; flag inline mapping elsewhere.

### J. Error handling & logging

1. Confirm errors are logged with **`ILogger<T>`** structured logging, not
   `Debug.WriteLine` / `Console.WriteLine`.
2. Flag swallowed exceptions (`catch { }` or `catch (Exception) { }` with no log) unless
   the exception is genuinely ignorable and the reason is documented.
3. Flag over-broad `catch (Exception)` that masks programming bugs.
4. Confirm user-visible errors are surfaced to the UI layer (dialog/snackbar) rather than
   silently dropped or shown as raw exception text.
5. Confirm argument validation (`ArgumentNullException.ThrowIfNull`, `ArgumentException`)
   on public API boundaries, but not "error handling for impossible scenarios" (per the
   Simplicity First guideline).

### K. Security

1. **Input validation** — APRS-IS lines, KISS frames, and user-entered callsigns/config are
   untrusted input; confirm bounds checks (e.g. `MaxLineLength`), length limits, and
   sanitization before use.
2. **APRS-IS passcode** — flag any hardcoded passcode/secret or logging of the passcode.
3. **SQL injection** — verify EF Core parameterized queries throughout (no raw SQL string
   interpolation).
4. **Path traversal / file access** — verify app-data-dir paths are sanitized.
5. Flag any secret/credential in config files or committed test data.

### L. Design & programming principles (general)

1. **Single Responsibility** — flag classes doing more than one thing (e.g. a modem that
   also parses, formats, and manages UI).
2. **DRY** — flag meaningful duplication of non-trivial logic (identical parsing,
   serialization, or validation repeated across files).
3. **Simplicity / KISS** — flag speculative abstractions, unused configurability, dead
   code, or 200-line solutions to 50-line problems (per AGENTS.md "Simplicity First").
4. **Encapsulation** — flag over-exposed internals (public members that should be
   `internal`/`private`), mutable state exposed without invariants.
5. **Immutability** — note where value objects (`Callsign`, `Symbol`, records) are
   mutable and shouldn't be.
6. **Nullability** — check nullable-reference-type annotations are present and correct;
   flag missing `?`/`!` misuse, and unnecessary null-forgiving operators.
7. **Thread-safety** — flag shared mutable state accessed from multiple threads without
   synchronization (modem read loops, GATT callbacks, channel bridges, beacon timer).
8. **Performance** — flag obvious hotspots: string concatenation in loops, repeated
   allocations on hot paths, unbounded collections, `Task.Run` abuse, LINQ in tight loops.
9. **Testability** — flag concrete dependencies that block unit testing (no interface
   seam), static facades touching hardware/platform APIs (e.g. `BluetoothPermissionHelper`).

### M. Test suite health

1. Confirm tests use **xUnit v3** (`xunit.v3` package) conventions: `[Fact]` in `Xunit`,
   `Assert.ThrowsAsync<T>` on `ValueTask` methods without `.AsTask()`, `await using` for
   `IAsyncDisposable`, `TestContext.Current.CancellationToken`.
2. Confirm mocking uses **NSubstitute** consistently.
3. Map coverage gaps against `DOCS/TESTING.md` — especially the **transport/platform seam**
   (TCP KISS E2E, APRS-IS E2E, Bluetooth abstraction). Note where the SIL plan's gaps are
   still open.
4. Flag tests that don't assert anything meaningful (assert-only-true, no assertions),
   tests coupled to implementation details, and flaky-prone tests (unbounded sleeps, real
   network, wall-clock time).
5. Confirm test files mirror source namespaces under `AetherAprs.Tests/`.
6. Note orphaned/stale tests (e.g. tests referencing deleted ViewModels) and tests marked
   with skip/todo without justification.

### N. Build & packaging

1. Confirm no package `Version` is set in individual `.csproj` files (central package
   management via `Directory.Packages.props`); flag any inline version.
2. Confirm `<ImplicitUsings>disable</ImplicitUsings>` and that every file carries explicit
   `using` for everything (including `System`, `System.Collections.Generic`, `System.Linq`).
3. Confirm `Directory.Packages.props` versions are consistent and up-to-date (note, don't
   silently change).
4. Note any `PackageReference` without a matching `PackageVersion`, or unused packages.
5. Note warnings-as-errors / analyzer settings in `Directory.Build.props` and any
   suppressed warnings that hide real issues.

## Step 3 — Report format

Produce a single structured report:

1. **Executive summary** — 3–6 sentences: overall health, biggest risks, top 5 things to
   fix first.
2. **Findings by dimension** — grouped A–N, each finding as:
   - `[Severity] Location` — one-line title
   - 1–3 sentences of explanation
   - suggested fix (concise).
3. **Architecture violations** — a dedicated section listing every A.1–A.6 violation
   explicitly (these are the most important).
4. **Security findings** — pulled out separately for visibility.
5. **Test coverage gaps** — mapped to `DOCS/TESTING.md`.
6. **Prioritized action list** — top 10 concrete fixes in dependency order, each with a
   one-line "verify" criterion.

## Rules for the audit

- **Evidence over opinion.** Every finding cites a file path and (where useful) a line
  number or symbol. No hand-waving.
- **Respect the "Surgical Changes" guideline** — you are *auditing*, not refactoring. Do
  not edit any code. Report only.
- **Distinguish rule violations from style preferences.** A violation of a stated
  `AGENTS.md`/`CODEBASE_STRUCTURE.md` rule is a Blocker/Major; a mere preference is Minor.
- **Do not invent problems.** If a dimension is clean, say so in one line rather than
  padding.
- **Be complete but not noisy.** Aggregate repeated instances of the same mistake into one
  finding with a list of locations.
- **When uncertain about an external library's API**, consult the `@mention` reference
  before flagging something as a misuse.

<!-- PROMPT END -->
