<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC-BY-SA-4.0
-->

# Getting Started

This guide walks you through setting up AetherAprs for the first time.

## Table of Contents
- [Installation](#installation)
- [First Launch](#first-launch)
- [Station Configuration](#station-configuration)
- [Adding Your First Port](#adding-your-first-port)
- [Viewing Packets](#viewing-packets)
- [Next Steps](#next-steps)

## Installation

### Android

1. Download the latest APK from [Releases](https://github.com/RuiOliveira/AetherAprs/releases)
2. Enable "Install from Unknown Sources" in your Android settings
3. Open the APK file and install
4. Grant required permissions when prompted:
   - **Location**: Required for GPS beaconing
   - **Bluetooth**: Required for Bluetooth TNC connections
   - **Foreground Service**: Keeps app active while ports are enabled

### Desktop (Windows, Linux, macOS)

AetherAprs is built with Avalonia UI (cross-platform framework) but **desktop platforms are not officially supported**.

You can try building from source (see [Building from Source](Building-from-Source.md)), but desktop functionality is not tested or maintained. Android is the primary supported platform.

## First Launch

When you first open AetherAprs, you'll see the Home page:

![Home Page](screenshots/home-page.png)
*Screenshot: Home page on first launch*

The app starts with:
- No ports configured
- Location tracking ready (grant permissions if prompted)
- Default beacon mode set to "Walk"
- Map centered on default location

## Station Configuration

Before connecting to APRS networks, configure your station information.

### Step 1: Open Settings

Tap the **Settings** tab at the bottom of the screen (gear icon).

![Settings Tab](screenshots/settings-tab.png)
*Screenshot: Bottom navigation with Settings tab*

### Step 2: Configure Station Settings

![Station Settings](screenshots/station-settings.png)
*Screenshot: Station Settings section*

**Required Fields:**

1. **Callsign**: Your amateur radio callsign (e.g., `PU4THZ`)
   - Must be a valid amateur radio callsign
   - Uppercase letters and numbers only
   - No spaces or special characters

2. **Default SSID**: Secondary Station Identifier (0-15)
   - `0` = Primary station (often omitted)
   - `1` = Typically a digi or message-capable station
   - `5` = Other networks (DX cluster, etc.)
   - `7` = Handheld radios
   - `8` = Boats, sailboats
   - `9` = Mobile stations (cars, trucks)
   - `10` = Internet, laptops
   - `12` = APRS over satellite

3. **Symbol**: APRS symbol representing your station
   - Tap **Base Symbol** button to select your primary symbol (house, car, truck, etc.)
   - Tap **Overlay** button to add an alphanumeric overlay (if using alternate table symbols)
   - Preview shows the final combined symbol
   
   ![Symbol Picker](screenshots/symbol-picker.png)
   *Screenshot: APRS Symbol Picker*

**Optional Settings:**

4. **Comment**: Short text included in position beacons
   - Example: `AetherAprs Mobile`
   - Keep it brief (max ~43 characters)

5. **Status Text**: Your current status message
   - Example: `Monitoring APRS`
   - Can be updated anytime

### Step 3: Save Settings

Settings are **automatically saved** as you change them. No manual save button needed.

## Adding Your First Port

Ports are connections to APRS networks. Start with APRS-IS (Internet) for the easiest setup.

### Step 1: Navigate to Ports

Tap the **Ports** tab at the bottom of the screen (network icon).

![Ports Tab](screenshots/ports-tab.png)
*Screenshot: Bottom navigation with Ports tab*

![Ports Page Empty](screenshots/ports-empty.png)
*Screenshot: Empty ports page*

### Step 2: Add APRS-IS Port

1. Tap the **➕ (Add)** button in the bottom-right (floating action button)
2. Enter a **Port Name**: `APRS-IS Main`
3. Select **Port Type**: `APRS-IS`

![Add Port - Type Selection](screenshots/add-port-type.png)
*Screenshot: Port type selection*

### Step 3: Configure APRS-IS Connection

![APRS-IS Configuration](screenshots/aprs-is-config.png)
*Screenshot: APRS-IS port configuration*

**Connection Settings:**

1. **Server**: `rotate.aprs2.net` (recommended)
   - This automatically rotates between APRS-IS servers
   - Alternative: Specific regional servers (e.g., `noam.aprs2.net`)

2. **Port**: `14580` (standard APRS-IS port)
   - Use `14580` for full bidirectional (requires passcode)
   - Use `10152` for read-only (no passcode needed)

3. **Passcode**: Your APRS-IS passcode
   - **Read-only mode**: Leave as `-1` (no authentication)
   - **Full access**: Enter your passcode (generated from your callsign)
   - [Generate passcode](https://apps.magicbug.co.uk/passcode/)
   - ⚠️ **Never share your passcode!**

4. **Filter**: Server-side packet filtering
   - Default: `r/35/-95/600` (600km radius around lat 35, lon -95)
   - Your location: `r/[lat]/[lon]/[range_km]`
   - All packets: Leave empty (not recommended, high bandwidth)
   - [APRS-IS Filter Guide](http://www.aprs-is.net/javAPRSFilter.aspx)

**Port Direction:**

- **Enable RX**: ☑️ Checked (receive packets)
- **Enable TX**: ☑️ Checked (send packets, requires valid passcode)
- **Show on Map**: ☑️ Checked (display received positions on map)
- **Allow Digipeat**: ☐ Unchecked (APRS-IS handles routing, no digipeating needed)

### Step 4: Save and Enable

1. Tap **Save** button in the bottom action bar
2. Back on Ports page, **toggle the port ON**

![Port Enabled](screenshots/port-enabled.png)
*Screenshot: Port enabled showing "Running" status*

**Status Indicators:**
- **"Running"**: Port is enabled and active
- **"Stopped"**: Port is disabled

### Android: Foreground Service

When you enable a port on Android, a **persistent notification** appears:

![Foreground Service Notification](screenshots/foreground-notification.png)
*Screenshot: Foreground service notification*

This keeps the app active in the background. To stop:
- Tap **Stop** in the notification, or
- Disable all ports in the app

## Viewing Packets

Once your port is enabled, packets start arriving immediately.

### Map View (Home Page)

![Map with Packets](screenshots/map-packets.png)
*Screenshot: Map showing received stations*

The map displays:
- **Station markers** with APRS symbols
- **Callsign labels** next to each station
- **Your location** (blue dot, if GPS is enabled)
- **Trails** showing historical movement

**Interacting with the Map:**
- **Tap a marker**: View station details
- **Pinch to zoom**: Zoom in/out
- **Drag**: Pan the map
- **Two-finger rotate**: Rotate the map (if supported)

### Packets Page

View all received packets in a sortable list.

Tap the **Packets** tab at the bottom of the screen.

![Packets Tab](screenshots/packets-tab.png)
*Screenshot: Bottom navigation with Packets tab*

![Packets List](screenshots/packets-list.png)
*Screenshot: Packets page showing recent activity*

**Columns:**
- **Callsign**: Station identifier
- **Type**: Packet type (Position, Message, Status, etc.)
- **Time**: When the packet was received
- **Info**: Brief packet summary

**Tap a row** to view full packet details:

![Packet Details](screenshots/packet-details.png)
*Screenshot: Detailed packet information*

## Next Steps

Now that you're receiving packets, explore more features:

- **[Beaconing Setup](Beaconing-Setup.md)**: Transmit your position automatically
- **[Messaging](Messaging.md)**: Send messages to other stations
- **[Port Configuration](Port-Configuration.md)**: Add KISS TNCs or Bluetooth devices
- **[Map Usage](Map-Usage.md)**: Advanced map features and filtering
- **[Digipeater Configuration](Digipeater-Configuration.md)**: Route packets between networks

## Troubleshooting

### Port Won't Connect

**APRS-IS connection fails:**
- Check your internet connection
- Verify server address and port number
- If using full access (TX enabled), ensure passcode is correct
- Try `rotate.aprs2.net:14580` as fallback

### No Packets Appearing

- Ensure port is **enabled** (toggle is ON)
- Check **Port Direction**: RX should be enabled
- Verify **Filter** isn't too restrictive
- Check **Map Time Filter** (top of Map page) - set to "All" temporarily

### Location Not Showing

- Grant **Location permission** in Android settings
- Ensure GPS is enabled on your device
- Wait for GPS fix (can take 30-60 seconds outdoors)
- Check location indicator on Home page

### App Stops in Background (Android)

- Ensure foreground service notification is active
- Check battery optimization: Settings → Apps → AetherAprs → Battery → Unrestricted
- Some manufacturers (Xiaomi, Huawei) aggressively kill background apps - add to whitelist

For more help, see [Troubleshooting](Troubleshooting.md).

---

**Next**: [Port Configuration →](Port-Configuration.md)
