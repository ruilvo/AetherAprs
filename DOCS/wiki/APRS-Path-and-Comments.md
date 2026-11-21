<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC-BY-SA-4.0
-->

# APRS paths and beacon comments

The digipeater path in **Settings → Beacon Settings** controls the aliases included in
outgoing APRS packets. Settings save automatically.

Common paths:

| Path | Use |
|---|---|
| `WIDE1-1,WIDE2-1` | Common mobile path using a fill-in and a wide-area hop |
| `WIDE1-1` | Local coverage |
| `WIDE2-1` | One wide-area hop |
| Empty | Direct transmission without requested digipeater hops |

Choose a path appropriate for your region and available digipeaters. Extra hops consume
more shared airtime. The path applies to new transmissions.

The **Default Beacon Comment** field adds short text to position beacons (maximum 43
characters). Beacon profiles can specify their own comment. Comments are transmitted
over a public network, so avoid sensitive information.

## Digipeater aliases

**Settings → Digipeater and Gating** controls whether the station repeats packets,
inserts its callsign in the path, responds to WIDE1/WIDE2/TRACE aliases, and gates
packets between RF and APRS-IS. Each port also has an **Allow Digipeating** control.

Use these features only after reviewing your radio setup and local APRS network
practices. See [Port Configuration](Port-Configuration.md) for port participation.
