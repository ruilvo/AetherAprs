<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC-BY-SA-4.0
-->

# AetherAprs Wiki

Welcome to the AetherAprs documentation! This wiki provides comprehensive guides for users and developers.

## About AetherAprs

AetherAprs is a modern, cross-platform ham radio APRS (Automatic Packet Reporting System) application built with Avalonia UI and .NET 10. It combines RF and Internet connectivity, intelligent beaconing, messaging, and digipeater functionality in a clean, Material Design interface.

## Quick Links

### For Users
- **[Getting Started](Getting-Started.md)** - First-time setup and basic configuration
- **[Port Configuration](Port-Configuration.md)** - Configure APRS-IS, KISS TNC connections
- **[Beaconing Setup](Beaconing-Setup.md)** - Configure position reporting
- **[Messaging Guide](Messaging.md)** - Send and receive APRS messages
- **[Map Usage](Map-Usage.md)** - Using the interactive map
- **[Digipeater Configuration](Digipeater-Configuration.md)** - Set up digipeating and gating
- **[Settings Reference](Settings-Reference.md)** - Complete settings documentation
- **[Troubleshooting](Troubleshooting.md)** - Common issues and solutions

### For Developers
- **[Building from Source](Building-from-Source.md)** - Compile and run AetherAprs
- **[Android Release Setup](Android-Release-Setup.md)** - Configure automatic APK builds
- **[Developer Guide](Developer-Guide.md)** - Architecture and contribution guidelines

## Features Overview

### 🌐 Multi-Protocol Connectivity
Connect to APRS networks via APRS-IS (Internet) or KISS TNCs over TCP/IP, Bluetooth Classic, or Bluetooth LE.

### 📡 Smart Beaconing
Intelligent position reporting that adapts to your speed and direction with preset modes for walking, driving, or custom configurations.

### 💬 Messaging
Full APRS messaging with automatic acknowledgments, retry logic, and delivery tracking.

### 🔄 Digipeater & Gating
Route packets between RF ports and Internet with flexible control over digipeater participation.

### 🗺️ Interactive Mapping
Real-time position display with trails, filtering, and APRS symbol rendering.

### 📦 Packet Management
View, filter, and inspect all received APRS packets with persistent storage.

## Platform Support

| Platform | Status | Features |
|----------|--------|----------|
| Android | ✅ Full | Bluetooth, GPS, foreground service |

**Note**: AetherAprs currently supports Android only. While built with Avalonia UI (cross-platform framework), desktop platforms are not yet supported.

## Getting Help

- **Issues**: Report bugs on [GitHub Issues](https://github.com/RuiOliveira/AetherAprs/issues)
- **Discussions**: Ask questions on [GitHub Discussions](https://github.com/RuiOliveira/AetherAprs/discussions)
- **Documentation**: Browse this wiki for detailed guides

## License

AetherAprs is free and open source software:
- Code: GPL-3.0-or-later
- Documentation: CC-BY-SA-4.0
- Configuration: CC0-1.0

See the [LICENSE files](https://github.com/RuiOliveira/AetherAprs/tree/main/LICENSES) for details.

## Amateur Radio Notice

AetherAprs is designed for licensed amateur radio operators. Users must:
- Comply with their license privileges
- Follow local regulations for APRS frequencies and power
- Properly identify with their assigned callsign
- Follow APRS network best practices

**73!** 📡
