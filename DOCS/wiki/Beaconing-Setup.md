<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC-BY-SA-4.0
-->

# Beaconing Setup

This guide covers configuring automatic position beaconing in AetherAprs.

## Table of Contents
- [Understanding Beaconing](#understanding-beaconing)
- [Location Tracking](#location-tracking)
- [Beacon Modes](#beacon-modes)
- [Configuring Beacon Settings](#configuring-beacon-settings)
- [Manual Beaconing](#manual-beaconing)
- [Troubleshooting](#troubleshooting)

## Understanding Beaconing

**Beaconing** is the automatic transmission of your position to the APRS network. AetherAprs uses **smart beaconing** that adapts transmission intervals based on your speed and heading changes.

### How Smart Beaconing Works

AetherAprs monitors your:
- **Speed**: Transmits more frequently when moving fast
- **Course changes**: Transmits immediately when you change direction significantly
- **Distance**: Prevents beacons from GPS jitter when stationary

**Benefits:**
- Efficient bandwidth usage
- Detailed tracking when moving
- Reduced transmissions when stationary
- Battery-friendly operation

## Location Tracking

Before beaconing, you need GPS location data.

### Enabling Location

![Location Tracking](screenshots/location-tracking.png)
*Screenshot: Location tracking section on Home page*

**On first launch**, AetherAprs requests location permission:
1. Android prompts: "Allow AetherAprs to access this device's location?"
2. Tap **Allow** or **While using the app**

**Location indicator** (Location Tracking card on Home page):
- **Status: "Tracking"** + coordinates: GPS fix acquired
- **Status: "Stopped"**: Location tracking not active
- **"Permission denied"**: Location permission not granted

### Granting Location Permission

If you denied permission initially:

1. Android Settings → Apps → AetherAprs
2. Tap **Permissions**
3. Tap **Location**
4. Select **Allow all the time** or **Allow only while using the app**

**Recommended**: "Allow all the time" for continuous beaconing even when app is in background.

### GPS Fix Time

- **Outdoors**: 30-60 seconds typical
- **Indoors**: May take several minutes or fail
- **Cold start**: Longer initial fix
- **Subsequent fixes**: Faster (seconds)

**Status updates** appear on Home page:
- "Waiting for first location fix..."
- "Location: 38.1234, -77.5678"

## Beacon Modes

AetherAprs provides three preset beacon modes plus manual transmission.

![Beacon Mode Selection](screenshots/beacon-modes.png)
*Screenshot: Beacon mode selector on Home page*

### Walk Mode

**Optimized for pedestrians and hiking.**

**Default intervals:**
- **Slow**: 30 minutes (when moving < 5 km/h)
- **Normal**: 10 minutes (when moving 5-32 km/h)
- **Fast**: 5 minutes (when moving > 32 km/h)

**Minimum distance**: 50 meters
**Course change threshold**: 30 degrees

**Typical use:**
- Walking around town
- Hiking trails
- Parks and events

### Drive Mode

**Optimized for vehicles and bicycles.**

**Default intervals:**
- **Slow**: 10 minutes (when moving < 5 km/h)
- **Normal**: 2 minutes (when moving 5-80 km/h)
- **Fast**: 30 seconds (when moving > 80 km/h)

**Minimum distance**: 100 meters
**Course change threshold**: 20 degrees

**Typical use:**
- Driving
- Cycling
- Motorcycles
- Boats

### Custom Mode

**Fully configurable parameters.**

Create your own beacon profile with custom:
- Slow/normal/fast intervals
- Speed thresholds
- Minimum distance
- Course change threshold

See [Configuring Beacon Settings](#configuring-beacon-settings) below.

### Manual Beacon

**Transmit on-demand without automatic beaconing.**

- No automatic transmissions
- Use **Send Beacon** button to transmit manually
- Useful for fixed stations or manual operation

## Configuring Beacon Settings

Customize beacon behavior for each mode.

### Accessing Beacon Configuration

1. Tap **Settings** tab
2. Scroll to **Beacon Configuration** section

![Beacon Configuration](screenshots/beacon-config.png)
*Screenshot: Beacon configuration settings*

You'll see three expandable sections:
- Walk Mode Configuration
- Drive Mode Configuration
- Custom Mode Configuration

### Beacon Parameters

Each mode has the same configurable parameters:

#### Slow Interval
**How often to beacon when moving slowly.**

- Walk mode default: 1800 seconds (30 minutes)
- Drive mode default: 600 seconds (10 minutes)
- Range: 60-7200 seconds (1 minute to 2 hours)

**Example**: Set to 300 (5 minutes) for more frequent updates when slow.

#### Normal Interval
**How often to beacon at normal speeds.**

- Walk mode default: 600 seconds (10 minutes)
- Drive mode default: 120 seconds (2 minutes)
- Range: 30-3600 seconds (30 seconds to 1 hour)

**Example**: Set to 180 (3 minutes) for medium tracking detail.

#### Fast Interval
**How often to beacon when moving fast.**

- Walk mode default: 300 seconds (5 minutes)
- Drive mode default: 30 seconds
- Range: 10-600 seconds (10 seconds to 10 minutes)

**Example**: Set to 60 (1 minute) for detailed highway tracking.

#### Slow Speed Threshold
**Speed boundary between slow and normal intervals (km/h).**

- Walk mode default: 5 km/h
- Drive mode default: 5 km/h
- Range: 0-50 km/h

**Below this speed**: Uses slow interval  
**Above this speed**: Uses normal or fast interval

#### Fast Speed Threshold
**Speed boundary between normal and fast intervals (km/h).**

- Walk mode default: 32 km/h (~20 mph)
- Drive mode default: 80 km/h (~50 mph)
- Range: 0-200 km/h

**Below this speed**: Uses normal interval  
**Above this speed**: Uses fast interval

#### Minimum Distance
**Minimum meters moved before beaconing (prevents GPS jitter).**

- Walk mode default: 50 meters
- Drive mode default: 100 meters
- Range: 0-1000 meters

**Purpose**: Prevents beacons when you're stationary but GPS position drifts slightly.

**Example**: Set to 200 meters to beacon only when significantly moved.

#### Course Change Threshold
**Degrees of heading change that triggers immediate beacon.**

- Walk mode default: 30 degrees
- Drive mode default: 20 degrees
- Range: 0-180 degrees

**Purpose**: Beacon immediately when you turn, even if interval hasn't elapsed.

**Example**: Set to 45 degrees for less sensitive turn detection.

### Custom Mode Example

**Scenario**: Bicycle touring with moderate updates

**Configuration:**
- Slow interval: 600 sec (10 min) - stopped/lunch breaks
- Normal interval: 180 sec (3 min) - casual riding
- Fast interval: 60 sec (1 min) - descents/fast sections
- Slow speed threshold: 8 km/h
- Fast speed threshold: 40 km/h
- Minimum distance: 75 meters
- Course change threshold: 25 degrees

**Result**: 
- Beacons every 10 minutes when stopped
- Every 3 minutes when riding casually
- Every minute when going fast
- Immediate beacon on turns > 25°

## Manual Beaconing

Send position beacons on-demand without automatic transmission.

### Sending a Manual Beacon

1. Ensure you have a **GPS fix** (location showing on Home page)
2. Ensure at least one port is **enabled with TX**
3. Tap **Send Beacon** button on Home page

![Send Beacon Button](screenshots/send-beacon.png)
*Screenshot: Send Beacon button*

**Result**: Position packet transmitted immediately to all TX-enabled ports.

### When to Use Manual Beacons

- **Fixed stations**: Home station that doesn't move
- **Occasional updates**: Update position every few hours manually
- **Event operation**: Beacon when you change locations at an event
- **Testing**: Verify beaconing works before enabling automatic mode

### Manual Beacon Content

Manual beacons include:
- Your callsign and SSID
- Current GPS position (latitude/longitude)
- Altitude (from GPS)
- APRS symbol (from Settings)
- Comment text (from Settings, if configured)

## Beacon Transmission

### Which Ports Transmit Beacons?

Beacons are sent to **all enabled ports** with:
- ☑️ **Enable TX** checked
- Port status: **"Running"**

**Example:**
- APRS-IS port (TX enabled, connected): ✅ Beacon transmitted
- KISS TNC (TX enabled, connected): ✅ Beacon transmitted
- Monitor port (TX disabled): ❌ No beacon
- Disconnected port: ❌ No beacon

### Beacon Packet Format

AetherAprs sends standard APRS position packets:

```
CT7ALW-9>APRS,WIDE1-1,WIDE2-1:!3812.34N/00912.56W>AetherAprs Mobile
```

**Components:**
- `CT7ALW-9`: Your callsign and SSID
- `>APRS`: Destination (identifies APRS packet)
- `WIDE1-1,WIDE2-1`: Digipeater path (if configured)
- `!`: Position without timestamp
- `3812.34N/00912.56W`: Position (degrees/minutes)
- `>`: Symbol (car in this example)
- `AetherAprs Mobile`: Comment (configurable)

### Digipeater Paths

**Default path**: `WIDE1-1,WIDE2-1`

This is the standard APRS path for mobile stations:
- `WIDE1-1`: Digipeated by fill-in digis (low-level digipeaters)
- `WIDE2-1`: Digipeated by wide-coverage digipeaters

**Path is automatic**. You don't need to configure it.

## Beacon Status

### Viewing Last Beacon

On the Home page, the **Beacon Transmission** section shows:

![Beacon Status](screenshots/beacon-status.png)
*Screenshot: Beacon transmission status*

**Information displayed:**
- Current beacon mode (Walk/Drive/Custom/Manual)
- Time since last beacon
- Next beacon countdown (if automatic mode)

**Example:**
```
Mode: Drive
Last beacon: 2 minutes ago
Next beacon: in 1 minute
```

### Beacon History

Your transmitted beacons appear in:
- **Packets page**: Listed with your callsign
- **Map**: Your position updates on the map
- **APRS-IS**: Visible to other APRS users

To verify beacons are working:
1. Send a manual beacon
2. Check [aprs.fi](https://aprs.fi) for your callsign
3. Your position should appear within 30 seconds

## Best Practices

### Battery Optimization

Smart beaconing saves battery by:
- **Reducing beacons when stationary** (slow interval)
- **GPS power management** (built into Android)
- **Efficient interval calculation**

**Additional tips:**
- Use longer intervals for casual use
- Disable beaconing when not needed
- Close app when not in use (beaconing stops)

### Bandwidth Considerations

**RF channels** have limited bandwidth. Be courteous:
- Use appropriate intervals (don't beacon every 10 seconds)
- Drive mode fast interval (30 sec) is aggressive—use only when needed
- Longer intervals on busy channels

**APRS-IS** can handle high rates, but:
- Be considerate of filter users
- Excessive beacons may be rate-limited by servers

### Privacy

Your position is **publicly visible** on APRS networks:
- Anyone can see your location on aprs.fi and similar sites
- Historical data may be archived
- Disable beaconing when privacy is needed

### RF Safety

When beaconing on RF:
- Ensure proper antenna installation
- Follow license power limits
- Avoid transmitting near sensitive electronics
- Comply with local regulations

## Troubleshooting

### Beacons Not Transmitting

**Check location:**
- GPS fix acquired? (Location Tracking card shows "Status: Tracking")
- Valid coordinates displayed?

**Check ports:**
- At least one port enabled?
- Port has **Enable TX** checked?
- Port status is **"Running"**?

**Check beacon mode:**
- Automatic mode selected (not Manual)?
- Sufficient time elapsed since last beacon?
- Moved sufficient distance (check minimum distance setting)?

**Check permissions:**
- Location permission granted?
- Foreground service notification visible (Android)?

### Beacons Too Frequent

**For automatic modes:**
- Increase interval values (slow/normal/fast)
- Increase minimum distance
- Increase course change threshold

**Example**: Change drive mode fast interval from 30 to 60 seconds.

### Beacons Too Infrequent

**Possible causes:**
- Intervals too long (decrease values)
- Minimum distance too large (decrease)
- Course change threshold too large (decrease)
- Moving too slowly (below slow speed threshold)

**Solution**: Adjust intervals or switch to a mode with shorter intervals.

### GPS Not Acquiring Fix

**Troubleshooting:**
- Go outdoors (GPS needs clear sky view)
- Wait 60 seconds for initial fix
- Restart app
- Check location permission granted
- Enable "High accuracy" in Android location settings
- Disable battery optimization for AetherAprs

**Android location settings:**
Settings → Location → Location services → Google Location Accuracy → On

### Beacons Sent But Not Visible on aprs.fi

**APRS-IS delay:**
- Allow 30-60 seconds for propagation
- Check [aprs.fi](https://aprs.fi) or [findu.com](https://findu.com)

**Callsign issues:**
- Verify callsign is correct in Settings
- Ensure SSID is valid (0-15)
- Check for typos

**Port issues:**
- APRS-IS port with valid passcode?
- Port status shows **"Running"**?

**RF only:**
- Beacons only sent to RF won't appear on aprs.fi unless gated
- Enable an APRS-IS port with TX to reach Internet

---

**Next**: [Messaging →](Messaging.md)
