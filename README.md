<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC-BY-SA-4.0
-->

# AetherAprs

AetherAprs is an Android APRS application for amateur radio operators. It connects to
APRS-IS and KISS TNCs, displays received stations on a map, sends position beacons and
messages, and supports digipeating and RF/Internet gating.

The application is built with Avalonia UI 12, Material.Avalonia, and .NET 11. Android is
the supported application target. The core project also builds on `net11.0` for tests and
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

Install the signed APK from [GitHub Releases](https://github.com/ruilvo/AetherAprs/releases)
on Android 13 or later.

1. In **Settings**, set your station callsign and SSID, symbol, and beacon profile.
2. In **Ports**, add an APRS-IS or KISS port. APRS-IS uses a server, optional receive
   filter, and a valid passcode for transmit; `rotate.aprs2.net:14580` is a common
   starting point. KISS/TCP uses the TNC's address. Pair Classic devices in Android
   settings; scan for a compatible BLE KISS device from the port editor.
3. Enable RX on the port, save it, and start it. Check **Packets** for traffic and
   **Home** for positions. If nothing arrives, check the connection and receive filter.
4. Enable TX only on the intended routes. Review your beacon comment, APRS path, and
   forwarding settings before sending. **Messages** shows conversations and delivery
   status. APRS traffic is public; don't transmit private information.

The app requests location permission; a position also needs a usable fix. Bluetooth
needs nearby-device permissions and Android controls notification and background
behavior. Continuous operation after Android stops the app is not guaranteed.
Download or select offline map data before losing Internet access.

## Build

Prerequisites:

- .NET 11 SDK
- Android workload for Android builds
- Android SDK for Android builds
- Git

```bash
git clone https://github.com/ruilvo/AetherAprs.git
cd AetherAprs
dotnet workload restore AetherAprs.Android/AetherAprs.Android.csproj
dotnet build AetherAprs.slnx
dotnet test AetherAprs.Tests/AetherAprs.Tests.csproj
dotnet build AetherAprs.Android/AetherAprs.Android.csproj
```

For local deployment, use an API 33+ emulator or a device with USB debugging and
`dotnet build AetherAprs.Android/AetherAprs.Android.csproj -t:Install`.

Release APKs are built for the device ABI with trimming and composite ReadyToRun
enabled, because .NET 11 Android runs on CoreCLR:

```bash
dotnet publish AetherAprs.Android/AetherAprs.Android.csproj -c Release -f net11.0-android -r android-arm64
```

ReadyToRun precompiles the assemblies into the APK so startup does less JIT work. It
costs package size (roughly 24 MB → 43 MB for the current arm64 APK). Release builds
trim the shared framework; trimming the application and its dependencies is tracked in
the [architecture roadmap](DOCS/ARCHITECTURE-ROADMAP-2026-11-24.md).

## Architecture

The receive path is:

```text
RF/APRS-IS port → PortService → PacketStorageService → SQLite
```

The read path is:

```text
SQLite → PacketQueryService → ViewModels → Avalonia views
```

`PortService` coordinates packet transmission. Packet history has a shared storage
and query path; the application-wide SQLite writer and reader boundaries are still
being consolidated. [AGENTS.md](AGENTS.md) covers the source layout and contributor
rules. The [architecture roadmap](DOCS/ARCHITECTURE-ROADMAP-2026-11-24.md) describes
the target architecture, feature work, and implementation order.

## Contributing

Keep changes focused, preserve the MVVM and service boundaries, localize user-facing
strings, and add tests for new behavior. Before submitting changes, run:

```bash
dotnet build AetherAprs.slnx
dotnet test AetherAprs.Tests/AetherAprs.Tests.csproj
pre-commit run reuse-lint-file --all-files
```

Report unfixed vulnerabilities privately through
[GitHub Security Advisories](https://github.com/ruilvo/AetherAprs/security/advisories/new).
Include the affected build, observed impact, and a minimal description with synthetic
data. Keep credentials, real callsigns, location histories, and packet or message
content out of diagnostic attachments and public issues.

## License

Code is licensed under GPL-3.0-or-later. Documentation is licensed under CC-BY-SA-4.0.
Configuration files use the license stated in their SPDX header. License texts are in
[`LICENSES`](LICENSES); bundled symbols retain their
[upstream notices](AetherAprs/Assets/Aprs/COPYRIGHT.md).

APRS transmissions must comply with the operator's license privileges, local regulations,
and applicable APRS network practices.
