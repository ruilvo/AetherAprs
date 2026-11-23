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
DOCS/wiki/                  User and developer guides
```

Within the core project:

- `Configuration/` contains settings and port configuration.
- `Data/` contains EF Core entities, mappers, migrations, and database infrastructure.
- `Models/Aprs/Packets/` contains APRS packet types.
- `Services/{Beaconing,Location,Messaging,Packets,Platform,Ports,Transmission,UI}/`
  contains domain services and platform abstractions.
- `Factories/` contains service and ViewModel factories.
- `ViewModels/Pages/` and `ViewModels/Components/` mirror `Views/Pages/` and
  `Views/Components/`.
- `Converters/Aprs/` and `Converters/UI/` contain Avalonia converters.
- New namespaces must match their folder path.

See [CODEBASE_STRUCTURE.md](CODEBASE_STRUCTURE.md) for the folder map.

## Architecture boundaries

Incoming packets follow:

```text
Port → PortService → PacketStorageService → SQLite
```

Database reads follow:

```text
SQLite → PacketQueryService → consumers
```

Outgoing packets follow:

```text
consumer → PortService.SendPacketAsync() → Port
```

Ports must not write to the database. `PacketStorageService` is the only packet writer.
ViewModels must use `PacketQueryService` for packet database reads; message data goes
through `MessageService`. Consumers must use `PortService` for transmission. Views
contain presentation and bindings, not business or infrastructure logic.

## MVVM and dependency injection

- Use CommunityToolkit.Mvvm.
- Use partial `[ObservableProperty]` properties, not backing-field generator syntax.
- Inject services and child ViewModels through constructors.
- Register every new ViewModel in `ServiceProviderFactory`, `DesignData`, and `ViewLocator`.
- Use a factory for transient ViewModels that need post-construction context.
- Let `ViewLocator` map ViewModels to views. Use `ContentControl` for child ViewModels.
- Keep design-time registrations free of live databases, network connections, hardware,
  and platform-only APIs.

## Events, async code, and disposal

- Unsubscribe from events when the owning object is disposed.
- Do not subscribe to an object's own `PropertyChanged` with a lambda; use toolkit
  partial change methods.
- Avoid `async void` except for event handlers, and catch/log failures in those handlers.
- Give background work cancellation and stop it during disposal.
- Use `ILogger<T>` with structured messages; do not use `Debug.WriteLine`.
- Use `System.DateTime` in UTC for SQLite timestamps and convert at UI boundaries.

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
- User-facing behavior is documented in `DOCS/wiki` when it changes.
