<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC-BY-SA-4.0
-->

# AetherAprs

<p align="center">
  <img src="AetherAprs.Android/Assets/icon_400px.png" alt="AetherAprs Logo" width="200"/>
</p>

**AetherAprs** is a modern, cross-platform ham radio APRS (Automatic Packet Reporting System) application built with Avalonia UI and .NET 10. It brings together RF and Internet connectivity, intelligent beaconing, messaging, and digipeater functionality in a clean, Material Design interface.

[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](https://www.gnu.org/licenses/gpl-3.0)
[![REUSE Compliant](https://img.shields.io/badge/REUSE-compliant-green)](https://reuse.software/)
[![.NET 10](https://img.shields.io/badge/.NET-10-purple)](https://dotnet.microsoft.com/)

## Features

### 🌐 Multi-Protocol Connectivity
- **APRS-IS**: Connect to APRS Internet servers with filtering and authentication
- **KISS TNC Support** via:
  - TCP/IP (local or remote TNCs)
  - Bluetooth Classic (SPP) - Android only
  - Bluetooth LE (Nordic UART Service) - Android only
- Manage multiple ports simultaneously with per-port RX/TX control

### 📡 Smart Beaconing System
- **Dynamic position reporting** with three preset modes:
  - **Walk Mode**: Optimized for pedestrians (30min/10min/5min intervals)
  - **Drive Mode**: Optimized for vehicles (10min/2min/30sec intervals)
  - **Custom Mode**: Fully configurable parameters
- **Intelligent transmission logic**:
  - Speed-based intervals (slow/normal/fast)
  - Course change detection triggers immediate beacons
  - Minimum distance filtering prevents GPS jitter
  - Configurable beacon comments and status text

### 💬 Messaging
- Send and receive APRS messages with conversation threads
- **Automatic ACK/REJ handling** with exponential backoff retry
- Message delivery status tracking (Pending/Acknowledged/Rejected/Timeout)
- Persistent conversation history

### 🔄 Digipeater & Gating
- **RF-to-RF digipeating**: Relay packets between radio ports
- **RF-to-APRS-IS gating**: Forward RF packets to Internet
- **APRS-IS-to-RF gating**: Forward Internet packets to RF (optional)
- Per-port digipeater participation control
- Optional callsign insertion in digipeated paths

### 🗺️ Interactive Mapping
- Real-time position display with APRS symbol icons
- Position trails showing historical movement
- Time-range filtering (last hour/day/week/month/custom/all)
- Per-port map visibility control
- User location tracking with auto-center on first fix

### 📦 Packet Management
- View most recent packets per callsign
- Detailed packet inspection (position, message, weather, status)
- SQLite database with configurable retention (default: 30 days)
- Automatic cleanup of old packets
- Support for all APRS packet types

### 🌍 Localization
- Multi-language support (English, Portuguese)
- Easy to add new translations

## Documentation

📚 **[Visit the Wiki](https://github.com/RuiOliveira/AetherAprs/wiki)** for comprehensive documentation:

- [Getting Started Guide](https://github.com/RuiOliveira/AetherAprs/wiki/Getting-Started)
- [Port Configuration](https://github.com/RuiOliveira/AetherAprs/wiki/Port-Configuration)
- [Beaconing Setup](https://github.com/RuiOliveira/AetherAprs/wiki/Beaconing-Setup)
- [Messaging Guide](https://github.com/RuiOliveira/AetherAprs/wiki/Messaging)
- [Digipeater Configuration](https://github.com/RuiOliveira/AetherAprs/wiki/Digipeater-Configuration)
- [Android Release Setup](https://github.com/RuiOliveira/AetherAprs/wiki/Android-Release-Setup)
- [Developer Guide](https://github.com/RuiOliveira/AetherAprs/wiki/Developer-Guide)

## Platform Support

| Platform | Status | Notes |
|----------|--------|-------|
| **Android** | ✅ Fully Supported | Bluetooth, GPS, foreground service |
| **Desktop** | 🚧 Not Yet Supported | Planned for future release |

**Note**: AetherAprs is built with Avalonia UI (cross-platform framework) but currently supports Android only.

## Installation

### Android
Download the latest APK from the [Releases](https://github.com/RuiOliveira/AetherAprs/releases) page.

**Required Permissions:**
- Location (for GPS beaconing)
- Bluetooth (for Bluetooth TNCs)
- Foreground service (keeps app active while ports are enabled)

### Desktop
Pre-built binaries coming soon. For now, build from source (see below).

## Quick Start

1. **Configure Your Station**
   - Go to Settings
   - Enter your callsign and default SSID
   - Select your APRS symbol

2. **Add a Port**
   - Go to Ports page
   - Tap "Add Port"
   - Choose APRS-IS or KISS TNC
   - Configure connection details

3. **Enable Position Beaconing** (Optional)
   - On Home page, ensure location tracking shows your position
   - Beacons will transmit automatically based on your configured mode

4. **Start Receiving**
   - Enable your port (toggle switch)
   - Watch packets appear in real-time on the map and packets page

## Building from Source

### Prerequisites
- .NET 10 SDK
- Android workload (for Android builds): `dotnet workload install android`
- Git

### Clone and Build
```bash
git clone https://github.com/RuiOliveira/AetherAprs.git
cd AetherAprs

# Build all projects
dotnet build AetherAprs.slnx

# Run desktop app (Windows/Linux/macOS)
dotnet run --project AetherAprs/AetherAprs.csproj

# Build Android APK (requires Android SDK)
dotnet build AetherAprs.Android/AetherAprs.Android.csproj -c Release
```

### Running Tests
```bash
dotnet test AetherAprs.Tests/AetherAprs.Tests.csproj
```

## Architecture

- **UI Framework**: Avalonia UI with Material Design
- **Pattern**: MVVM with CommunityToolkit.Mvvm
- **Database**: SQLite with Entity Framework Core
- **DI Container**: Microsoft.Extensions.DependencyInjection
- **Configuration**: Microsoft.Extensions.Configuration (JSON)
- **Mapping**: Mapsui library
- **Logging**: Microsoft.Extensions.Logging

## Contributing

Contributions are welcome! Please:

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Ensure all files have proper SPDX headers (pre-commit hook enforces this)
4. Follow the coding conventions in `AGENTS.md`
5. **Update documentation**:
   - Add user-facing features to the [Wiki](https://github.com/RuiOliveira/AetherAprs/wiki)
   - Update `README.md` features section if adding major functionality
   - Update `AGENTS.md` if changing architecture or developer workflows
6. Commit your changes (`git commit -m 'Add amazing feature'`)
7. Push to the branch (`git push origin feature/amazing-feature`)
8. Open a Pull Request

### Development Guidelines

- **All user-facing text must be localized** (see `AGENTS.md` for details)
- Use `ILogger<T>` for logging, not `Debug.WriteLine`
- Follow MVVM pattern with proper DI registration
- Add tests for new features
- Run `pre-commit run --all-files` before committing

See [`AGENTS.md`](AGENTS.md) for detailed developer guidelines.

## License

This project is compliant with the [REUSE specification](https://reuse.software/), enforced by a git pre-commit hook.

- **Code**: GPL-3.0-or-later
- **Documentation**: CC-BY-SA-4.0
- **Configuration files**: CC0-1.0

For detailed license information, see [LICENSES](/LICENSES).

## Acknowledgments

- **Avalonia UI** - Cross-platform UI framework
- **Mapsui** - Mapping library
- **APRS Community** - Protocol specifications and support
- All contributors and testers

## Support

- **Issues**: [GitHub Issues](https://github.com/RuiOliveira/AetherAprs/issues)
- **Discussions**: [GitHub Discussions](https://github.com/RuiOliveira/AetherAprs/discussions)
- **Wiki**: [Documentation](https://github.com/RuiOliveira/AetherAprs/wiki)

## Amateur Radio Compliance

AetherAprs is designed for use by licensed amateur radio operators. Users are responsible for:
- Ensuring transmissions comply with their license privileges
- Following local regulations regarding APRS frequencies and power limits
- Properly identifying their station with their assigned callsign
- Understanding and following APRS network best practices

**73 de CT7ALW!** 📡
