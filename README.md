<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC-BY-SA-4.0
-->

# AetherAprs

AetherAprs is an Android APRS application for amateur radio operators. It connects to
APRS-IS and KISS TNCs, displays received stations on a map, sends position beacons and
messages, and supports digipeating and RF/Internet gating.

The application is built with Avalonia UI 12, Material.Avalonia, and .NET 10. Android is
the supported application target. The core project also builds on `net10.0` for tests and
platform-independent development.

## Features

- APRS-IS connections with server-side filters
- KISS TNC connections over TCP, Bluetooth Classic, and Bluetooth LE
- Position beaconing with Walk, Drive, and Custom profiles
- APRS messaging with acknowledgments, retries, and conversation history
- Packet history, packet details, station trails, and map display
- Digipeating and configurable RF/APRS-IS gating
- Offline map packages and cached map tiles
- English and Portuguese localization

## Install

Download the latest signed Android APK from
[GitHub Releases](https://github.com/RuiOliveira/AetherAprs/releases). Android asks for
location, Bluetooth, notification, and foreground-service permissions as needed.

The user guides are in [`DOCS/wiki`](DOCS/wiki):

- [Getting Started](DOCS/wiki/Getting-Started.md)
- [Port Configuration](DOCS/wiki/Port-Configuration.md)
- [Beaconing](DOCS/wiki/Beaconing-Setup.md)
- [Messaging](DOCS/wiki/Messaging.md)
- [APRS Paths and Comments](DOCS/wiki/APRS-Path-and-Comments.md)
- [Production-Grade Design Sheet](DOCS/wiki/Production-Grade-Design-Sheet.md)

## Build

Prerequisites:

- .NET 10 SDK
- Android workload for Android builds
- Android SDK for Android builds
- Git

```bash
git clone https://github.com/RuiOliveira/AetherAprs.git
cd AetherAprs
dotnet build AetherAprs.slnx
dotnet test AetherAprs.Tests/AetherAprs.Tests.csproj
dotnet build AetherAprs.Android/AetherAprs.Android.csproj -c Release
```

For Android deployment, emulator setup, signing, and troubleshooting, see
[Building from Source](DOCS/wiki/Building-from-Source.md).

## Architecture

The receive path is:

```text
RF/APRS-IS port → PortService → PacketStorageService → SQLite
```

The read path is:

```text
SQLite → PacketQueryService → ViewModels → Avalonia views
```

Transmission always goes through `PortService`. Ports do not access the database,
`PacketStorageService` is the packet writer, and ViewModels do not query EF Core directly.
The [Developer Guide](DOCS/wiki/Developer-Guide.md) describes the project structure and
development workflow. [AGENTS.md](AGENTS.md) contains the repository rules.

## Contributing

Keep changes focused, preserve the MVVM and service boundaries, localize user-facing
strings, and add tests for new behavior. Before submitting changes, run:

```bash
dotnet build AetherAprs.slnx
dotnet test AetherAprs.Tests/AetherAprs.Tests.csproj
pre-commit run reuse-lint-file --all-files
```

## License

Code is licensed under GPL-3.0-or-later. Documentation is licensed under CC-BY-SA-4.0.
Configuration files use the license stated in their SPDX header. License texts are in
[`LICENSES`](LICENSES).

APRS transmissions must comply with the operator's license privileges, local regulations,
and applicable APRS network practices.
