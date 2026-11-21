<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC-BY-SA-4.0
-->

# Codebase structure

The repository has three projects:

```text
AetherAprs/                 Core application, services, models, and views
AetherAprs.Android/         Android entry point and platform adapters
AetherAprs.Tests/            xUnit v3 tests
```

## Core folders

| Folder | Responsibility |
|---|---|
| `Configuration/` | Settings, port configuration, UI enums |
| `Data/` | EF Core context, entities, mappers, converters, migrations |
| `Factories/` | Service and ViewModel factories |
| `Models/` | Domain and APRS packet models |
| `Modems/` | APRS, AX.25, KISS, and AFSK protocol logic |
| `Services/` | Domain services grouped by responsibility |
| `Transports/` | Network and KISS stream connectors |
| `ViewModels/Pages/` | Page-level ViewModels |
| `ViewModels/Components/` | Reusable component ViewModels |
| `Views/Pages/` | Page views |
| `Views/Components/` | Reusable component views |
| `Converters/Aprs/` | APRS-specific converters |
| `Converters/UI/` | General UI converters |
| `Localization/` | English and Portuguese resources |

Service folders are `Beaconing`, `Bluetooth`, `Configuration`, `Contracts`, `Location`,
`Messaging`, `Packets`, `Platform`, `Ports`, `Transmission`, and `UI`.

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
