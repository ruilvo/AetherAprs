<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC-BY-SA-4.0
-->

# Port configuration

A port connects AetherAprs to APRS-IS or a KISS TNC. You can configure multiple ports and
enable or disable each one independently.

## Add a port

1. Open **Ports** and tap **Add Port**.
2. Enter a name and choose **APRS-IS** or **KISS**.
3. Choose whether to receive, transmit, display received positions on the map, and allow
   digipeating.
4. Enter the transport-specific settings, tap **Save**, then enable the port from the
   Ports list.

Use the port card to change visibility on the map, edit settings, or delete the port.
The port's status shows whether it is enabled.

## APRS-IS

- **Server**: defaults to `rotate.aprs2.net`.
- **Port**: defaults to `14580`.
- **Passcode**: generated from your station callsign; use a valid passcode to transmit.
- **Filter**: use the visual filter builder to limit incoming packets. The generated
  APRS-IS filter is saved with the port.

An overly restrictive filter can hide stations and messages. See the
[APRS-IS filter specification](http://www.aprs-is.net/javAPRSFilter.aspx) for the
filter syntax.

## KISS over TCP

Enter the TNC host and TCP port. For a TNC on your local network, use its LAN address;
`localhost` refers to the Android device running AetherAprs.

## KISS over Bluetooth Classic

Pair the TNC in Android's Bluetooth settings first. In the port editor, choose
**Bluetooth Classic**, refresh the paired-device list, and select the TNC.
Android must grant Bluetooth permissions before discovery or connection.

## KISS over Bluetooth LE

Choose **Bluetooth LE**, start a scan, and select a compatible KISS device.
The implementation uses Nordic UART Service. The scan can be stopped from the editor.

## Digipeating and gating

The port's **Allow Digipeating** control decides whether it participates when
digipeating is enabled globally. Configure alias and RF/APRS-IS gating options in
**Settings**. Check your radio's TX path and local APRS practices before enabling
RF transmission.

See [APRS Paths and Comments](APRS-Path-and-Comments.md) for the path used on outgoing
packets.
