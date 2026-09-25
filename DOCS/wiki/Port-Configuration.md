<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC-BY-SA-4.0
-->

# Port Configuration

This guide covers all port types supported by AetherAprs and how to configure them.

## Table of Contents
- [Understanding Ports](#understanding-ports)
- [APRS-IS Configuration](#aprs-is-configuration)
- [KISS TNC over TCP/IP](#kiss-tnc-over-tcpip)
- [KISS TNC over Bluetooth Classic](#kiss-tnc-over-bluetooth-classic)
- [KISS TNC over Bluetooth LE](#kiss-tnc-over-bluetooth-le)
- [Port Direction Settings](#port-direction-settings)
- [Managing Ports](#managing-ports)

## Understanding Ports

A **port** in AetherAprs is a connection to an APRS network. You can have multiple ports active simultaneously.

**Port Types:**
- **APRS-IS**: Internet connection to APRS servers
- **KISS TNC**: Hardware TNC (Terminal Node Controller) for RF connections
  - TCP/IP: Network connection to TNC
  - Bluetooth Classic (SPP): Serial profile connection
  - Bluetooth LE: Nordic UART Service (NUS) connection

**Common Configurations:**
- Single APRS-IS port (Internet only, simplest setup)
- APRS-IS + KISS TNC (gateway between Internet and RF)
- Multiple KISS TNCs (multiple radios/frequencies)

## APRS-IS Configuration

APRS-IS provides Internet connectivity to the global APRS network.

### Adding an APRS-IS Port

1. Tap **Ports** tab at bottom
2. Tap **➕ (Add)** button in bottom-right (floating action button)
3. Enter **Port Name**: `APRS-IS` (or any descriptive name)
4. Select **Port Type**: `APRS-IS`

![APRS-IS Port Configuration](screenshots/aprs-is-config.png)
*Screenshot: APRS-IS configuration screen*

### Connection Settings

#### Server
The APRS-IS server hostname or IP address.

**Recommended**: `rotate.aprs2.net`
- Automatically load-balances across APRS-IS tier 2 servers
- Provides best reliability and performance

**Regional servers** (optional):
- `noam.aprs2.net` - North America
- `soam.aprs2.net` - South America
- `euro.aprs2.net` - Europe
- `asia.aprs2.net` - Asia
- `aunz.aprs2.net` - Australia/New Zealand

**Tier 1 servers** (core network, generally not needed):
- `rotate.aprs.net`

#### Port
The TCP port number for APRS-IS.

- **14580**: Full bidirectional (requires valid passcode)
- **10152**: Read-only, no authentication needed

**Default**: `14580`

#### Passcode
Authentication code for transmitting to APRS-IS.

- **Read-only mode**: Enter `-1` (no authentication, receive only)
- **Full access**: Enter your APRS passcode
  - Generated from your callsign
  - [Generate passcode](https://apps.magicbug.co.uk/passcode/)
  - ⚠️ **Never share your passcode publicly!**

**How to get your passcode:**
1. Visit a passcode generator (e.g., https://apps.magicbug.co.uk/passcode/)
2. Enter your callsign (e.g., `CT7ALW`)
3. Copy the numeric passcode
4. Paste into AetherAprs

**Important**: Passcodes are **algorithmically generated** from your callsign. Anyone can compute your passcode, so it's not a security feature—it's an identity verification mechanism.

#### Filter
Server-side filter to reduce bandwidth and processing.

**Syntax**: Space-separated filter commands

**Common filters:**

| Filter | Description | Example |
|--------|-------------|---------|
| `r/lat/lon/range` | Radius filter | `r/38.9/-77.0/100` (100km around Washington DC) |
| `p/prefix` | Callsign prefix | `p/CT/CS` (Portuguese stations) |
| `b/call1/call2` | Buddy list | `b/CT7ALW/CT1ETE` |
| `t/poimqstunw` | Packet types | `t/p` (positions only) |
| `m/km` | My range (require login) | `m/50` (50km around your position) |

**Examples:**

```
r/38.9/-77.0/500       # 500km radius around lat 38.9, lon -77.0
p/CT                   # All callsigns starting with CT
b/CT7ALW/CT1ETE/CS7   # Only these specific callsigns
t/pm                   # Positions and messages only
r/38.9/-77.0/100 t/p   # Combine: radius + positions only
```

**Leave empty** to receive all packets (not recommended—high bandwidth).

**Filter reference**: [APRS-IS Filter Guide](http://www.aprs-is.net/javAPRSFilter.aspx)

### Port Direction

- **Enable RX**: ☑️ Receive packets from APRS-IS
- **Enable TX**: ☑️ Send packets to APRS-IS (requires valid passcode)
- **Show on Map**: ☑️ Display received positions on map
- **Allow Digipeat**: ☐ Typically unchecked (APRS-IS servers handle distribution)

### Save and Enable

1. Tap **Save** button in the bottom action bar
2. Toggle the port **ON** in the ports list
3. Status should show **"Running"** (connected)

## KISS TNC over TCP/IP

Connect to a KISS TNC via network (local or remote).

### Use Cases
- NetworkTNC devices
- TNC connected to Raspberry Pi running kissattach
- Direwolf software TNC
- Remote TNCs over VPN or Internet

### Adding a KISS TCP Port

1. Tap **Ports** tab
2. Tap **➕ (Add)** (floating action button in bottom-right)
3. Enter **Port Name**: `KISS TCP` (or descriptive name)
4. Select **Port Type**: `KISS`
5. Select **Transport**: `TCP/IP`

![KISS TCP Configuration](screenshots/kiss-tcp-config.png)
*Screenshot: KISS TCP configuration*

### Connection Settings

#### Host
IP address or hostname of the TNC.

**Examples:**
- `192.168.1.100` - Local network TNC
- `localhost` or `127.0.0.1` - TNC on same device
- `tnc.example.com` - Remote TNC via hostname

#### Port
TCP port number the TNC is listening on.

**Common ports:**
- `8001` - Direwolf default
- `6700` - Some TNCs
- `8100` - Alternative common port

**Default**: `8001`

### Port Direction

- **Enable RX**: ☑️ Receive packets from RF
- **Enable TX**: ☑️ Transmit packets to RF
- **Show on Map**: ☑️ Display received positions
- **Allow Digipeat**: ☑️ Participate in digipeating (if enabled globally)

⚠️ **Important**: Ensure your TNC and radio are properly configured before enabling TX.

### Direwolf Example

If using Direwolf software TNC:

**direwolf.conf:**
```
AGWPORT 8000
KISSPORT 8001
```

**Connect from AetherAprs:**
- Host: `127.0.0.1` (if on same device) or device IP
- Port: `8001`

## KISS TNC over Bluetooth Classic

Connect to a KISS TNC via Bluetooth SPP (Serial Port Profile).

### Requirements
- Android device with Bluetooth
- TNC with Bluetooth Classic (SPP) support
- TNC must be **paired** in Android Bluetooth settings first

### Compatible TNCs
- Mobilinkd TNC2
- Mobilinkd TNC3 (also supports BLE)
- Bluetooth-enabled hardware TNCs
- DIY TNCs with HC-05/HC-06 Bluetooth modules

### Adding a Bluetooth Classic Port

1. **Pair TNC first**:
   - Android Settings → Bluetooth
   - Put TNC in pairing mode
   - Find TNC in available devices
   - Pair (may require PIN, often `1234` or `0000`)

2. **Add port in AetherAprs**:
   - Tap **Ports** tab
   - Tap **➕ (Add)** (floating action button in bottom-right)
   - Enter **Port Name**: `Mobilinkd TNC3`
   - Select **Port Type**: `KISS`
   - Select **Transport**: `Bluetooth Classic`

![Bluetooth Classic Configuration](screenshots/bt-classic-config.png)
*Screenshot: Bluetooth Classic configuration*

### Device Selection

Tap **Select Device** to browse paired Bluetooth devices.

![Bluetooth Device Selection](screenshots/bt-device-list.png)
*Screenshot: Paired Bluetooth devices*

**Select your TNC** from the list. Only paired devices appear.

### Port Direction

- **Enable RX**: ☑️ Receive RF packets
- **Enable TX**: ☑️ Transmit to RF
- **Show on Map**: ☑️ Display positions
- **Allow Digipeat**: ☑️ Participate in digipeating

### Troubleshooting

**TNC not appearing in device list:**
- Ensure TNC is powered on
- Pair in Android Bluetooth settings first
- Restart AetherAprs

**Connection fails:**
- Check TNC battery
- Verify TNC is not connected to another device
- Try unpairing and re-pairing
- Ensure TNC is in KISS mode (not other modes like APRS tracker)

**Bluetooth permission denied:**
- Grant Bluetooth permissions in Android settings
- Settings → Apps → AetherAprs → Permissions → Nearby devices

## KISS TNC over Bluetooth LE

Connect to a KISS TNC via Bluetooth Low Energy using Nordic UART Service (NUS).

### Requirements
- Android device with Bluetooth LE support
- TNC with BLE and Nordic UART Service (NUS)
- TNC does **not** need to be paired first

### Compatible TNCs
- Mobilinkd TNC3 (firmware 3.0+)
- Mobilinkd TNC4
- DIY TNCs with nRF52/nRF51 modules running NUS firmware

### Adding a Bluetooth LE Port

1. Tap **Ports** tab
2. Tap **➕ (Add)**
3. Enter **Port Name**: `Mobilinkd TNC4`
4. Select **Port Type**: `KISS`
5. Select **Transport**: `Bluetooth LE`

![Bluetooth LE Configuration](screenshots/bt-le-config.png)
*Screenshot: Bluetooth LE configuration*

### Device Discovery

Tap **Scan for Devices** to discover nearby BLE devices.

![BLE Scan](screenshots/ble-scan.png)
*Screenshot: BLE device scanning*

**Wait for scan to complete** (a few seconds), then **select your TNC**.

**Device information shown:**
- Device name (e.g., `Mobilinkd TNC4`)
- MAC address
- Signal strength (RSSI)

### Port Direction

- **Enable RX**: ☑️ Receive RF packets
- **Enable TX**: ☑️ Transmit to RF
- **Show on Map**: ☑️ Display positions
- **Allow Digipeat**: ☑️ Participate in digipeating

### BLE vs Classic Bluetooth

| Feature | Bluetooth Classic (SPP) | Bluetooth LE (NUS) |
|---------|-------------------------|---------------------|
| Pairing required | Yes | No |
| Power consumption | Higher | Lower |
| Range | ~10m | ~10-30m |
| Latency | Lower | Slightly higher |
| Connection stability | Good | Good |

**Recommendation**: Use BLE if your TNC supports it for lower power consumption.

### Troubleshooting

**No devices found during scan:**
- Ensure TNC is powered on and nearby
- Check Bluetooth is enabled on phone
- Grant location permission (required for BLE scanning on Android)
- Move closer to TNC

**Connection drops frequently:**
- Check TNC battery level
- Reduce distance between phone and TNC
- Avoid obstacles/interference

**Bluetooth permission issues:**
- Grant all Bluetooth permissions
- Android 12+: "Nearby devices" permission required

## Port Direction Settings

Each port has four direction flags controlling its behavior.

### Enable RX (Receive)
☑️ **Checked**: Port receives packets

When enabled:
- Incoming packets are processed
- Packets stored in database
- Packets forwarded to digipeater service
- Messages processed by message service

☐ **Unchecked**: Port ignores incoming data

**Use case**: Disable RX if you only want to transmit on this port.

### Enable TX (Transmit)
☑️ **Checked**: Port can send packets

When enabled:
- Beacons transmitted on this port
- Messages transmitted on this port
- Digipeated packets transmitted on this port

☐ **Unchecked**: Port will not transmit

**Use case**: Receive-only monitoring, or APRS-IS read-only mode.

### Show on Map
☑️ **Checked**: Packets from this port appear on map

When enabled:
- Position packets displayed as markers
- Trails shown for stations
- Counts toward packet statistics

☐ **Unchecked**: Packets received but not shown on map

**Use case**: Hide clutter from high-traffic ports while still logging packets.

### Allow Digipeat
☑️ **Checked**: Port participates in digipeating

When enabled (and global digipeater is enabled):
- Digipeated packets transmitted to this port
- Packets received on this port can be digipeated to other ports

☐ **Unchecked**: Port excluded from digipeating

**Use case**: 
- Uncheck for APRS-IS ports (servers handle routing)
- Uncheck for monitor-only ports (reduce clutter)
- Check for RF ports participating in digipeater network

See [Digipeater Configuration](Digipeater-Configuration.md) for details.

## Managing Ports

### Editing Ports

1. Tap **Ports** tab
2. Tap the **port** you want to edit
3. Modify settings
4. Tap **Save** (checkmark)

**Note**: Editing a port while it's enabled will disconnect and reconnect it.

### Deleting Ports

1. Tap **Ports** tab
2. Tap the **Delete** button (trash icon) on the port card
3. Confirm deletion

⚠️ **Warning**: Deletion is permanent. Configuration cannot be recovered.

### Enabling/Disabling Ports

Toggle the **switch** next to each port in the ports list.

**When enabled:**
- Connection established
- Status shows **"Running"**
- Foreground service notification appears (Android)

**When disabled:**
- Connection closed
- Status shows **"Stopped"**
- No processing or transmission occurs

### Port Priority

Ports have **no priority order**. All enabled ports:
- Process packets simultaneously
- Transmit beacons to all TX-enabled ports
- Participate in digipeating based on settings

### Multiple APRS-IS Ports

You can have multiple APRS-IS ports with different filters:

**Example:**
- Port 1: `r/38/-77/100` - Local area (high detail)
- Port 2: `p/CT` - All Portuguese stations (specific interest)
- Port 3: `b/CT7ALW/CT1ETE` - Buddy tracking

Each port maintains its own connection and processes packets independently.

## Best Practices

### Filter Configuration
- Use **radius filters** to limit bandwidth: `r/lat/lon/range`
- Combine filters for efficiency: `r/38/-77/100 t/pm` (positions + messages only)
- Avoid empty filters (receives everything, high load)

### RF Safety
- **Never enable TX** until your TNC and radio are properly configured
- Test RX-only first
- Verify proper antenna connection
- Ensure compliance with license privileges

### Bluetooth Reliability
- Keep phone close to TNC (within 5 meters)
- Use BLE for better power efficiency
- Avoid using Bluetooth in crowded RF environments

### Port Naming
- Use descriptive names: `APRS-IS Europe`, `Mobilinkd 2m`, `Home TNC`
- Include frequency if RF: `KISS 144.390 MHz`
- Include purpose: `Monitor Only`, `TX Beacon`

## Troubleshooting

### Port Won't Connect

**APRS-IS:**
- Verify server and port (try `rotate.aprs2.net:14580`)
- Check internet connection
- Verify passcode if TX enabled

**KISS TCP:**
- Ping host to verify reachability: `ping 192.168.1.100`
- Verify port number matches TNC configuration
- Check firewall rules
- Ensure TNC is in KISS mode

**Bluetooth:**
- Ensure TNC is powered and nearby
- Check pairing (Classic) or scan again (LE)
- Grant all Bluetooth permissions
- Restart both phone and TNC

### Port Connects But No Packets

- Verify **Enable RX** is checked
- Check **Show on Map** if expecting map markers
- Check APRS-IS filter isn't too restrictive
- For RF: Verify radio is receiving (check TNC LEDs/indicators)

### Transmit Not Working

- Verify **Enable TX** is checked
- APRS-IS: Ensure passcode is valid (not `-1`)
- KISS: Verify TNC is in KISS mode and connected to radio
- Check radio PTT (push-to-talk) configuration in TNC

---

**Next**: [Beaconing Setup →](Beaconing-Setup.md)
