<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC-BY-SA-4.0
-->

# APRS Path and Beacon Comments

This guide covers configuring digipeater paths and beacon comments in AetherAprs.

## Table of Contents
- [Understanding APRS Paths](#understanding-aprs-paths)
- [Configuring Digipeater Path](#configuring-digipeater-path)
- [Beacon Comments](#beacon-comments)
- [Digipeater Alias Configuration](#digipeater-alias-configuration)
- [Technical Details](#technical-details)
- [Best Practices](#best-practices)
- [Troubleshooting](#troubleshooting)

## Understanding APRS Paths

### What is a Digipeater Path?

An APRS **digipeater path** (or just "path") is a list of callsigns or aliases that specify which digipeaters should repeat your packet. When you transmit a beacon, the path determines how far and through which network nodes your packet will travel.

### Standard Path Format

A typical path looks like: `WIDE1-1,WIDE2-1`

This consists of:
- **WIDE1-1**: A request for fill-in digipeaters (local, low-level digis)
- **WIDE2-1**: A request for wide-coverage digipeaters

The number after the dash is the **hop count** (SSID):
- First number would be the original request (e.g., WIDE2-2)
- Gets decremented by each digipeater that repeats it
- When it reaches 0, the packet is no longer repeated
- `-1` means "repeat me once"

### How Paths Work

When you transmit `CT7ALW-9>APRS,WIDE1-1,WIDE2-1:!position...`:

1. **Local fill-in digi** receives it
   - Sees WIDE1-1, decrements to WIDE1-0 (exhausted)
   - Repeats the packet

2. **Wide-coverage digi** receives it
   - Sees WIDE2-1, decrements to WIDE2-0 (exhausted)
   - Repeats the packet one more time

3. **No more hops available** - packet stops being repeated

### Path Types

**Mobile Station (default)**: `WIDE1-1,WIDE2-1`
- Reaches local digipeaters and wide-area network
- Standard for moving stations
- Good balance of coverage and bandwidth

**Local Only**: `WIDE1-1`
- Only fill-in digipeaters repeat
- Limited coverage area
- Good for dense urban areas or testing

**Wide Coverage**: `WIDE2-2`
- Repeated by wide-coverage digis twice
- Larger coverage area
- More bandwidth usage - use sparingly

**Direct Only**: Empty path (no digipeaters)
- No digipeaters repeat your packet
- Only heard by direct receivers (APRS-IS, nearby stations)
- Good for fixed stations in well-covered areas

**Fixed Station**: Usually empty or `WIDE2-1`
- Fixed home stations often use no path (direct only)
- Or single wide digi hop for regional coverage

## Configuring Digipeater Path

### Accessing Path Settings

1. Open AetherAprs
2. Tap **Settings** tab
3. Scroll to **Beacon Settings** section
4. Find **Digipeater Path** field

![Digipeater Path Setting](screenshots/digipeater-path.png)

### Setting Your Path

**Default path**: `WIDE1-1,WIDE2-1`

**To change:**
1. Tap the **Digipeater Path** field
2. Enter your desired path (comma-separated)
3. Settings save automatically

**Examples:**

```
WIDE1-1,WIDE2-1       Standard mobile path
WIDE1-1               Local coverage only
WIDE2-2               Extended coverage (use carefully)
                      Empty = no digipeating (direct only)
W7XYZ,WIDE1-1         Specific digi, then fill-in
```

### Path Validation

AetherAprs validates paths:
- ✅ Valid callsign format
- ✅ Hop count in range 0-15
- ✅ Comma-separated entries
- ❌ Invalid: `WIDE11` (no dash)
- ❌ Invalid: `WIDE1-20` (hop count too high, max 15)

### When Path Changes Apply

Path changes apply to **new transmissions**:
- Next automatic beacon
- Next manual beacon
- Existing queued beacons use old path

## Beacon Comments

### What is a Beacon Comment?

The **beacon comment** is text appended to your position beacon. It appears after your position and symbol in the packet.

**Example beacon packet:**
```
CT7ALW-9>APRS,WIDE1-1,WIDE2-1:!3812.34N/00912.56W>AetherAprs Mobile
                                                     ^^^^^^^^^^^^^^^^
                                                     Comment text
```

### Comment Length Limit

**Maximum**: 43 characters

This is an APRS protocol limit to keep packets small and efficient.

### Configuring Default Comment

**Default comment** applies to all beacons unless overridden by specific beacon configuration.

**To set:**
1. Open **Settings** tab
2. Scroll to **Beacon Settings** section
3. Enter text in **Default Beacon Comment** field
4. Settings save automatically

**Examples:**
```
AetherAprs Mobile               17 characters
/A=001234 https://example.com   29 characters (altitude + URL)
Monitoring 146.520              19 characters
```

### Comment Priority

Comments are selected in this order:

1. **Beacon-specific comment** (from beacon mode configuration)
2. **Default beacon comment** (from Settings)
3. **No comment** (empty)

This allows you to:
- Set a global default comment
- Override it for specific beacon modes if needed
- Leave it empty for minimal packets

### Per-Mode Comments

Each beacon mode (Walk, Drive, Custom) has its own optional comment field:

1. **Settings** → **Beacon Configuration**
2. Expand the mode (e.g., Walk Mode Configuration)
3. Find **Comment** field
4. Enter mode-specific comment

**Example use case:**
- Default comment: `AetherAprs`
- Walk mode comment: `Walking`
- Drive mode comment: `Mobile`

### Useful Comment Content

**Altitude**: `/A=001234` (altitude in feet)
- Automatically formatted by some APRS software
- Format: `/A=XXXXXX` (6 digits, padded with zeros)

**Website**: `https://example.com`
- Share your website or tracking page
- Keep it short (long URLs consume comment space)

**Status**: `Monitoring 146.520`, `En route`, `At home`
- Brief activity description

**Contact**: `email@example.com` or phone (for events)

**Avoid:**
- Personal information you don't want public
- Long sentences (max 43 chars!)
- Special characters that might cause parsing issues

## Digipeater Alias Configuration

AetherAprs can act as a digipeater and respond to WIDE1/WIDE2 alias requests.

### Understanding Digipeater Aliases

**WIDE1-N**: Fill-in digipeater
- Local, low-level coverage
- Responds to any WIDE1 request (WIDE1-1, WIDE1-2, etc.)
- Helps with "weak signal" situations
- Recommended for high-altitude or strategic locations

**WIDE2-N**: Wide coverage digipeater  
- Regional or area coverage
- Responds to any WIDE2 request (WIDE2-1, WIDE2-2, etc.)
- Full network participation
- Recommended for established digipeater stations

### Configuring Alias Response

1. **Settings** → **Digipeater & Gating**
2. Find alias checkboxes:
   - ☑️ **Respond to WIDE1-N**
   - ☑️ **Respond to WIDE2-N**

**Default settings:**
- ☐ Respond to WIDE1-N: **Disabled**
- ☑️ Respond to WIDE2-N: **Enabled**

### When to Enable WIDE1-N Response

Enable if:
- ✅ High altitude location (hilltop, tall building)
- ✅ Strategic location with weak coverage
- ✅ Intentional fill-in digipeater operation
- ✅ Good antenna system

Do NOT enable if:
- ❌ Mobile station (creates network loops)
- ❌ Dense urban area (too much traffic)
- ❌ Poor antenna (won't help coverage)
- ❌ Temporary operation (inconsistent coverage)

### When to Enable WIDE2-N Response

Enable if:
- ✅ Operating an established digipeater station
- ✅ Coordinated with local APRS network
- ✅ Good coverage area
- ✅ Reliable power and uptime

Do NOT enable if:
- ❌ Mobile station
- ❌ Limited coverage
- ❌ Not coordinated with network
- ❌ Temporary/portable operation

### Digipeater Best Practices

**Before enabling digipeater:**
- Coordinate with local APRS group
- Ensure proper antenna installation
- Test coverage area
- Monitor for network problems
- Document your digipeater callsign and location

**Responsible operation:**
- Monitor your digipeater traffic
- Disable if causing issues
- Use proper callsign identification
- Follow local regulations

## Technical Details

### AX.25 Frame Structure with Path

APRS packets use AX.25 frames with this structure:

```
[Destination] [Source] [Digi1] [Digi2] ... [DigiN] [Control] [PID] [Info]
   7 bytes     7 bytes  7 bytes 7 bytes    7 bytes   1 byte  1 byte  ...
```

**Address fields** (7 bytes each):
- 6 bytes: Callsign (space-padded)
- 1 byte: SSID + flags
  - Bit 0: Extension bit (0 = more addresses, 1 = last address)
  - Bits 1-4: SSID (0-15) — used for hop count in digipeater paths
  - Bit 5-6: Reserved
  - Bit 7: Has-been-repeated flag

**Control**: `0x03` (UI frame)
**PID**: `0xF0` (no layer 3 protocol)
**Info**: APRS data (position, comment, etc.)

### Path Parsing

AetherAprs parses paths from comma-separated strings:

```
"WIDE1-1,WIDE2-1" → [Callsign("WIDE1", HopCount=1), Callsign("WIDE2", HopCount=1)]
```

**Rules:**
- Comma-separated entries
- Each entry: `CALLSIGN` or `CALLSIGN-N` where N is the hop count
- CALLSIGN: 1-6 alphanumeric characters
- Hop count (N): 0-15, stored in the SSID field of the AX.25 address
- The number after the dash indicates remaining hops before the alias is exhausted
- Whitespace around commas is ignored

**Important**: The `-1` in `WIDE1-1` means "one hop remaining", not "SSID 1". The SSID field in AX.25 addresses is repurposed for hop counting in APRS paths.

### Path Hop Decrementing

When digipeating, AetherAprs decrements the hop count:

```
WIDE2-2  →  WIDE2-1  (repeated, decrement)
WIDE2-1  →  WIDE2-0  (repeated, exhausted - don't repeat again)
WIDE2-0  →  (ignored, already exhausted)
```

### Alias Matching

**WIDE1-N matching:**
- Callsign: `WIDE1`
- Any hop count: 1-7 (practical range)
- Matches: `WIDE1-1`, `WIDE1-2`, `WIDE1-3`
- Does NOT match: `WIDE1-0` (exhausted)

**WIDE2-N matching:**
- Callsign: `WIDE2`
- Any hop count: 1-7 (practical range)
- Matches: `WIDE2-1`, `WIDE2-2`, `WIDE2-3`
- Does NOT match: `WIDE2-0` (exhausted)

## Best Practices

### Path Selection Guidelines

**Mobile stations:**
- Use `WIDE1-1,WIDE2-1` (default)
- Well-balanced coverage and bandwidth
- Works in most areas

**Fixed stations with good coverage:**
- Use empty path (direct only)
- Or `WIDE2-1` for regional reach
- No need for fill-in digis

**Emergency/event operation:**
- Use `WIDE1-1,WIDE2-2` for extended coverage
- More hops = more bandwidth
- Only when necessary

**Urban areas with dense network:**
- Consider `WIDE1-1` only
- Reduce congestion
- Usually sufficient coverage

**Rural/remote areas:**
- Use `WIDE1-1,WIDE2-2` for extended reach
- Fewer digipeaters means more hops needed
- Balance coverage vs. bandwidth

### Comment Best Practices

**Be concise:**
- 43 character limit enforced
- Shorter is better for bandwidth
- Omit unnecessary words

**Be relevant:**
- Current status or activity
- Contact information (if needed)
- Avoid static boilerplate

**Be appropriate:**
- Keep it family-friendly
- No personal attacks or profanity
- Follow amateur radio regulations

**Update regularly:**
- Change comments to reflect activity
- Remove outdated information
- Keep it current

### Bandwidth Courtesy

**APRS is a shared resource:**
- Every packet consumes bandwidth
- Be considerate of others
- Use appropriate paths and intervals

**Recommendations:**
- Don't beacon excessively (follow beacon interval guidelines)
- Use shortest path that provides needed coverage
- Omit comments if not needed
- Coordinate digipeater operation with local group

## Troubleshooting

### Path Not Working

**Symptoms:**
- Beacons not digipeated
- Limited coverage
- Not appearing on aprs.fi

**Checks:**
1. **Verify path syntax**: Comma-separated, valid callsigns
2. **Check local digipeater coverage**: Are WIDE1/WIDE2 digis in range?
3. **Test with direct path**: Try empty path to confirm basic transmission
4. **Monitor RF**: Use another radio to verify packets on-air
5. **Check APRS-IS**: Direct APRS-IS connection bypasses digipeaters

**Solutions:**
- Verify path is correctly formatted
- Increase path hops if in remote area
- Check local APRS network documentation
- Ensure RF port is transmitting (not just APRS-IS)

### Comments Not Appearing

**Symptoms:**
- Position beacons work but comment missing
- Wrong comment showing

**Checks:**
1. **Verify comment set**: Settings → Beacon Settings → Default Beacon Comment
2. **Check length**: Max 43 characters
3. **Mode-specific override**: Check if beacon mode has its own comment
4. **View on aprs.fi**: Comments visible on tracking sites

**Solutions:**
- Re-enter comment and save
- Shorten if over 43 characters
- Clear mode-specific comments to use default
- Send a test beacon and verify on aprs.fi

### Digipeater Not Responding

**Symptoms:**
- Packets with WIDE1-1/WIDE2-N not being repeated
- Digipeater function not working

**Checks:**
1. **Digipeater enabled**: Settings → Digipeater & Gating → Enable Digipeater
2. **Alias enabled**: Verify WIDE1/WIDE2 checkboxes
3. **Port capable**: RF port must be enabled with TX
4. **Incoming packets**: Must receive packets to digipeat them

**Solutions:**
- Enable digipeater and appropriate aliases
- Ensure RF port is running
- Monitor incoming packets (must receive to digipeat)
- Check antenna and coverage

### Invalid Path Error

**Symptoms:**
- Path not accepted
- Validation error

**Common mistakes:**
```
WIDE11          → Use: WIDE1-1 (missing dash)
WIDE1-20        → Use: WIDE1-1 (hop count too high, max 15)
WIDE1 WIDE2     → Use: WIDE1-1,WIDE2-1 (space instead of comma)
```

**Solutions:**
- Use dash notation: `CALLSIGN-N` where N is hop count
- Hop count range: 0-15
- Comma-separated: `WIDE1-1,WIDE2-1`

---

**Related:**
- [Beaconing Setup →](Beaconing-Setup.md)
- [Port Configuration →](Port-Configuration.md)
- [Getting Started →](Getting-Started.md)
