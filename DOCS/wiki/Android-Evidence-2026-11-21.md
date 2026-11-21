<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC-BY-SA-4.0
-->

# Android evidence record: 2026-11-21

The Android Emulator `sdk_gphone64_x86_64` (API 36 / Android 16) and a named
physical target were connected during this evidence window. The emulator
results remain separate from the physical Bluetooth result below.

The on-device instrumentation command:

```text
adb shell am instrument -w -r com.AetherRadio.AetherAprs.Tests/com.aetheraprs.tests.TestInstrumentation
INSTRUMENTATION_RESULT: nunit-exit-code=0
INSTRUMENTATION_CODE: -1
```

The instrumentation log reported `Android instrumentation tests passed: 4`.
The emulator matrix executed clean app-data launch, process stop/relaunch, and
a reversible Wi-Fi disable/enable transition. The configuration-change
broadcast was rejected by Android with `Permission Denial`, so activity
recreation is recorded as blocked for this run.

The short collector used `-DurationHours 0.02` and captured two samples. These
are measurements only and are not a 24-hour soak result:

| UTC sample | PSS total (KiB) | RSS total (KiB) | CPU |
|---|---:|---:|---|
| 2026-11-21 15:42:26 | 182183 | 246584 | 0% user, 0% kernel |
| 2026-11-21 15:43:28 | 350505 | 398124 | 7.6% user, 88% kernel |

The full local command outputs are retained under
`artifacts/android-evidence/emulator-5554/`. No credentials, message text, or
raw APRS payloads are included in this record.

## Physical BLE result

The named physical target was a Google Pixel 9a, serial `53261JEBF06233`,
Android 17 / API 37, paired with a LilyGO T-Echo Plus running the
`richonguzman/LoRa_APRS_Tracker` firmware. The release APK was installed with
`adb install --no-incremental -r` while preserving the existing app data.

At 2026-11-21 17:12 device log time, the operator enabled the saved KISS port. The
redacted device log recorded:

```text
BluetoothGatt onConnectionState status=0 connected=true
BluetoothGatt onSearchComplete status=0
setCharacteristicNotification uuid=00000003-ba2a-46c9-ae49-01b0961f68bb enable=true
KISS modem started
Started KISS modem for port Porta KISS 1
Transmitted APRS position on port Porta KISS 1
```

The app process remained alive (`pidof` returned `27452`) and
`dumpsys activity` showed `MainActivity` resumed after the test. No
`FATAL EXCEPTION`, transport fault, or port-disable event appeared in the
captured log. This is a successful single-device BLE profile and transmit
smoke result; it does not satisfy MTU/pacing, Bluetooth-loss/reconnect,
Bluetooth Classic, audio-route, field-trial, or 24-hour soak gates.
