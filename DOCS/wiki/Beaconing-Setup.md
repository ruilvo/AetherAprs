<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC-BY-SA-4.0
-->

# Beaconing

AetherAprs sends position beacons through enabled transmit ports when it has a location
fix and the beaconing conditions are met. It adjusts intervals using speed, distance
traveled, and course changes.

## Before beaconing

1. Enter your station callsign, SSID, and APRS symbol in **Settings**.
2. Grant Android location permission and enable location services.
3. Configure and enable at least one TX port.
4. Set the outgoing digipeater path and optional comment in **Settings → Beacon Settings**.

Android may request background location access and battery-optimization exemption if
you want tracking to continue when the app is not visible.

## Choose a profile

Open **Settings → Dynamic Beaconing → Configure Beaconing Modes**. Select the active
mode and adjust the Walk, Drive, or Custom configuration. Each profile has its own
slow/normal/fast intervals, speed thresholds, distance threshold, course-change
threshold, and optional beacon comment. Use **Reset to Default** in a profile to
restore its defaults.

Longer intervals and a higher minimum distance reduce transmissions when stationary.
Shorter intervals report movement more often but use more airtime.

## Troubleshooting

- **No GPS fix:** grant location permission, enable Android location services, and try
  outside with a clear view of the sky.
- **No transmission:** check the station callsign, enabled TX port, and port connection.
- **Unexpected frequency:** review the selected profile's speed, interval, distance,
  and course thresholds.

For path selection and beacon text, see [APRS Paths and Comments](APRS-Path-and-Comments.md).
