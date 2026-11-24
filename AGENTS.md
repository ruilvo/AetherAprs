<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC-BY-SA-4.0
-->

# AetherAprs development guide

This is the working guide for contributors and coding agents. Keep it aligned with the
code. The supported application target is Android; the core project targets `net10.0`
and the test project targets `net10.0`.

## Before changing code

1. Read the relevant source and tests.
2. Check the package source references in `opencode.json` before guessing an Avalonia,
   Material.Avalonia, Mapsui, BruTile, Geo, or xUnit API.
3. Keep the change limited to the requested behavior.
4. State assumptions when the repository does not make the intended behavior clear.

## Build and test

```powershell
dotnet build AetherAprs.slnx
dotnet build AetherAprs/AetherAprs.csproj
dotnet build AetherAprs.Android/AetherAprs.Android.csproj
dotnet build AetherAprs.Tests/AetherAprs.Tests.csproj
dotnet test AetherAprs.Tests/AetherAprs.Tests.csproj
pre-commit run reuse-lint-file --all-files
```

Use `--no-build` for `dotnet test` only after a successful build. The Android project
requires the .NET Android workload and Android SDK.

## Repository layout

```text
AetherAprs/                 Core application and platform-neutral services
AetherAprs.Android/         Android entry point and Android service adapters
AetherAprs.Tests/            xUnit v3 tests
AetherAprs.Android.Tests/    Android instrumentation tests (build-only in CI)
DOCS/ARCHITECTURE-ROADMAP-2026-11-24.md   Target design and implementation backlog
```

Within the core project:

- `Configuration/` contains settings and port configuration.
- `Data/` contains EF Core entities, mappers, migrations, and database infrastructure.
- `Models/Aprs/Packets/` contains APRS packet types.
- `Services/{Beaconing,Bluetooth,Configuration,Contracts,Diagnostics,Location,Messaging,Objects,Packets,Platform,Ports,Transmission,UI}/`
  contains domain services, contracts, diagnostics, objects, and platform abstractions.
- `Factories/` contains service and ViewModel factories.
- `ViewModels/Pages/` and `ViewModels/Components/` mirror `Views/Pages/` and
  `Views/Components/`; `Views/Windows/` contains top-level windows.
- `Converters/Aprs/` and `Converters/UI/` contain Avalonia converters.
- New namespaces must match their folder path.

Android adapters live under `AetherAprs.Android/Services/Platform/` and
`AetherAprs.Android/Transports/`. Match tests to the subsystem in `AetherAprs.Tests/`.

## Architecture boundaries

Packet ingestion follows:

```text
Port → PortService → packet ingest queue → PacketStorageService → SQLite
```

Packet reads follow:

```text
SQLite → PacketQueryService → consumers
```

Outbound producers use `PortService.SendPacketAsync()` for transmission. Persist
durable work (such as user messages) before sending; don't queue live-only
forwarding work for replay.

The [roadmap](DOCS/ARCHITECTURE-ROADMAP-2026-11-24.md) defines the target: one
serialized runtime SQLite writer and one logical reader with typed operations.
Existing feature persistence is being migrated. Do not add direct EF access to
feature services or ViewModels. Use `PacketStorageService` and `PacketQueryService`
for packet history. Publish configuration changes only after successful persistence;
reconnect must never change the operator's saved enablement.
ViewModels must use query services for database reads and feature services for
state changes. Consumers must use `PortService` for transmission. Views contain
bindings, presentation logic, event wiring, and UI dialog orchestration; business,
persistence, and transport logic belong in ViewModels or services.

## MVVM and dependency injection

- Use CommunityToolkit.Mvvm.
- Use partial `[ObservableProperty]` properties, not backing-field generator syntax.
- Inject services and child ViewModels through constructors.
- Register ViewModels created by DI or resolved through navigation. Add a
  `ViewLocator` mapping only for ViewModels rendered through automatic
  data-template resolution; child ViewModels may be constructed by their owner.
- Use factories for transient ViewModels that require runtime context or
  post-construction initialization.
- Use `ContentControl` and `ViewLocator` for child ViewModels rendered by data
  templates. Explicitly wired views don't need a locator entry.
- Keep design time isolated from production data, network, hardware, and
  platform-only APIs. An isolated test database or a fake is fine.

## Events, async code, and disposal

- Unsubscribe from events when the owning object is disposed.
- Do not subscribe to an object's own `PropertyChanged` with a lambda; use toolkit
  partial change methods.
- Avoid `async void` except for event handlers, and catch/log failures in those handlers.
- Give background work cancellation and stop it during disposal.
- Use `ILogger<T>` with structured messages; do not use `Debug.WriteLine`.
- Store SQLite timestamps in UTC. Domain instants can use `DateTimeOffset`;
  convert for local display at the UI boundary. Keep receipt time distinct from
  protocol timestamps.
- Keep credentials, message text, raw frames, and exact positions out of routine
  diagnostics. Use synthetic data in fixtures and reports.

## Naming and source conventions

- Private fields: `_camelCase`
- Public types, properties, methods, and enum members: `PascalCase`
- Parameters and locals: `camelCase`
- Interfaces start with `I`.
- `ImplicitUsings` is disabled. Add every required `using`.
- Package versions belong in `Directory.Packages.props`; project references omit versions.

## Localization

All user-facing text must be localized:

- AXAML uses `{loc:Loc Key}`.
- C# uses `Strings.Get("Key")` or `Strings.Format("Key", ...)`.
- Add new keys to both `AetherAprs/Localization/Strings.resx` and
  `Strings.pt.resx`.

Logs, developer comments, configuration keys, and technical identifiers remain in English.

## Licensing

Every new file needs SPDX licensing metadata accepted by REUSE. Text files normally
carry a header; binary assets use a `.license` sidecar:

- C# and source code: `GPL-3.0-or-later`
- Application AXAML/XML: `GPL-3.0-or-later`
- Documentation: `CC-BY-SA-4.0`
- Configuration: use the license already established for that file type

Run `pre-commit run reuse-lint-file --all-files` before committing.

## Pull request checklist

- The change is limited to the requested scope.
- Relevant tests were added or updated.
- Build and tests pass.
- REUSE lint passes.
- No stale docs, hardcoded UI strings, direct database access, or unregistered ViewModels
  were introduced.
- Update README setup or usage instructions when affected; update the roadmap when
  an architectural decision or work-item status changes.
