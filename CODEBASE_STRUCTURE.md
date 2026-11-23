<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC-BY-SA-4.0
-->

# Codebase structure

The repository has four projects:

```text
AetherAprs/                 Core application, services, models, and views
AetherAprs.Android/         Android entry point and platform adapters
AetherAprs.Tests/           xUnit v3 tests
AetherAprs.Android.Tests/   Android instrumentation tests (build-only in CI)
```

## Core folders

| Folder | Responsibility |
|---|---|
| `Behaviors/` | Avalonia attached behaviors |
| `Configuration/` | Settings, port configuration, UI enums |
| `Converters/Aprs/` | APRS-specific converters |
| `Converters/UI/` | General UI converters |
| `Data/` | EF Core context, entities, mappers, converters, migrations |
| `Extensions/` | General-purpose extension methods |
| `Factories/` | Service and ViewModel factories |
| `Helpers/` | Small shared helpers |
| `Imaging/` | Bitmap and image processing |
| `Localization/` | English and Portuguese resources |
| `Models/` | Domain and APRS packet models |
| `Modems/Afsk/` | AFSK modem implementation |
| `Modems/Aprs/` | APRS modem implementations |
| `Modems/Kiss/` | KISS modem and framing |
| `Services/` | Domain services grouped by responsibility |
| `Styles/` | Avalonia style resources |
| `Transports/Kiss/` | Network and KISS stream connectors |
| `ViewModels/Pages/` | Page-level ViewModels |
| `ViewModels/Components/` | Reusable component ViewModels |
| `Views/Pages/` | Page views |
| `Views/Components/` | Reusable component views |

Service folders are `Beaconing`, `Bluetooth`, `Configuration`, `Contracts`, `Diagnostics`,
`Location`, `Messaging`, `Objects`, `Packets`, `Platform`, `Ports`, `Transmission`, and `UI`.

## Placement rules

- APRS packet types go in `Models/Aprs/Packets/`.
- EF entities, mappers, converters, and migrations go in their matching `Data/` folders.
- Platform-neutral interfaces and NoOp implementations go in `Services/Platform/`.
- Android implementations go in `AetherAprs.Android/Services/`.
- Page and component ViewModels mirror their view folders.
- Tests generally mirror source areas under `AetherAprs.Tests/`.
- Namespaces must match folder structure.

The complete architecture and contributor rules are in [AGENTS.md](AGENTS.md). User and
developer guides are in [`DOCS/wiki`](DOCS/wiki).
