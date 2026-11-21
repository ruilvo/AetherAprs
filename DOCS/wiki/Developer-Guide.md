<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC-BY-SA-4.0
-->

# Developer guide

AetherAprs is an Android-first Avalonia UI 12 application targeting .NET 10. It uses
CommunityToolkit.Mvvm, Material.Avalonia, EF Core with SQLite, Mapsui, and
Microsoft.Extensions.DependencyInjection.

## Data flows

Received packets follow:

```text
Port → PortService → PacketStorageService → SQLite
```

`PortService` coordinates ports and persistence. Ports do not access the database.
`PacketStorageService` is the only packet writer.

Database-backed reads follow:

```text
SQLite → PacketQueryService → ViewModels/services → Views
```

ViewModels never access EF Core or `DbContext` directly. Map and packet detail screens
obtain data through the query and map-layer services; conversations use `MessageService`.

Transmission follows:

```text
ViewModel/service → PortService.SendPacketAsync() → Port
```

Consumers do not send through a port directly.

## Dependency injection and MVVM

`Factories/ServiceProviderFactory.cs` builds the runtime container. Platform projects
override the core NoOp implementations through `RegisterPlatformServices`.

When adding a ViewModel:

1. Register it in `ServiceProviderFactory.cs`.
2. Register it in `DesignData.cs`.
3. Expose its design-time instance in `DesignData`.
4. Add its ViewModel-to-view mapping in `ViewLocator.cs`.
5. Use constructor injection and a factory when initialization needs context.

Views use compiled bindings, localized resources, shared page/card styles, and
`ContentControl` for child ViewModels. Keep business and infrastructure logic in services.

## Source layout

| Area | Location |
|---|---|
| Settings and port configuration | `AetherAprs/Configuration/` |
| Database entities and migrations | `AetherAprs/Data/` |
| APRS packet models | `AetherAprs/Models/Aprs/Packets/` |
| Protocol and modem code | `AetherAprs/Modems/` |
| Domain services | `AetherAprs/Services/` |
| ViewModels | `AetherAprs/ViewModels/` |
| Avalonia views | `AetherAprs/Views/` |
| Android adapters | `AetherAprs.Android/Services/` |
| Tests | `AetherAprs.Tests/` |

Use the folder that matches the responsibility and namespace.

## Testing and validation

```bash
dotnet build AetherAprs.slnx
dotnet test AetherAprs.Tests/AetherAprs.Tests.csproj
pre-commit run reuse-lint-file --all-files
```

Tests mirror source namespaces. Add focused tests for new behavior and update existing
tests when bindings or service contracts change.

## UI and localization

All user-facing strings belong in both `Strings.resx` and `Strings.pt.resx`. Use
`{loc:Loc Key}` in AXAML and `Strings.Get`/`Strings.Format` in C#.

Keep ViewModels disposable when they subscribe to events. Cancel background work during
disposal, use structured `ILogger<T>` messages, and keep platform APIs out of the core
project.
