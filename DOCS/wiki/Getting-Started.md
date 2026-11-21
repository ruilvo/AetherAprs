<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC-BY-SA-4.0
-->

# Getting started

AetherAprs currently supports Android. Download a signed APK from
[GitHub Releases](https://github.com/RuiOliveira/AetherAprs/releases), install it, and
grant the permissions needed for location, Bluetooth, notifications, and background
operation.

## Configure the station

1. Open **Settings**.
2. Enter the station callsign and SSID.
3. Select the APRS symbol and optional overlay.
4. Choose the packet display range and theme.
5. Configure beacon, digipeater, message, map, and search settings as needed.

Settings save as they change.

## Add an APRS-IS port

1. Open **Ports** and select **Add Port**.
2. Give the port a name and leave the type as **APRS-IS**.
3. Set the server, port, passcode, and optional APRS-IS filter.
4. Choose RX, TX, map, and digipeater participation.
5. Save the port and enable it from the Ports list.

`rotate.aprs2.net` and port `14580` are common APRS-IS defaults. Use a valid passcode
before enabling transmission.

## Add a KISS port

Select **KISS** as the port type, then choose TCP, Bluetooth Classic, or Bluetooth LE.
TCP needs the TNC host and port. Bluetooth devices must be paired or discoverable as
appropriate before scanning from the port editor.

## Use the app

- **Home** shows the map and provides zoom and location controls.
- **Packets** shows received packet summaries and details.
- **Messages** contains APRS conversations.
- **Ports** controls connections and their RX/TX state.
- **Settings** controls station, beacon, map, digipeater, messaging, object, bulletin,
  and search features.

## Troubleshooting

- A stopped port is disabled or failed to connect; check its server, credentials, and
  device connection.
- If no packets arrive, enable RX and review the APRS-IS filter.
- If location is missing, grant location access and wait for a GPS fix.
- If Android stops background activity, allow notifications and set the app battery mode
  to unrestricted.

Continue with [Port Configuration](Port-Configuration.md), [Beaconing](Beaconing-Setup.md),
or [Messaging](Messaging.md).
