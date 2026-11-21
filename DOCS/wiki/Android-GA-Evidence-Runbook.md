<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC-BY-SA-4.0
-->

# Android GA evidence runbook

This runbook defines repeatable device, soak, and field-trial evidence for the
acceptance criteria in the [production design sheet](Production-Grade-Design-Sheet.md).
Each run records the commit, APK checksum, device model, Android API level, UTC
start/end times, test operator, and the attached redacted support report.

## Prepare a release candidate

Build and verify the same signed APK that will be tested:

```bash
dotnet build AetherAprs.slnx -c Release
dotnet build AetherAprs.Android.Tests/AetherAprs.Android.Tests.csproj -c Release
sha256sum AetherAprs.Android/bin/Release/net10.0-android/*Signed.apk
adb install -r AetherAprs.Android/bin/Release/net10.0-android/*Signed.apk
adb install -r AetherAprs.Android.Tests/bin/Release/net10.0-android/*Signed.apk
```

Run the on-device instrumentation suite:

```bash
adb shell am instrument -w -r \
  com.AetherRadio.AetherAprs.Tests/com.aetheraprs.tests.TestInstrumentation
```

The result must contain `nunit-exit-code=0` and the log must contain
`Android instrumentation tests passed`.

## Lifecycle and transport matrix

Run each row on every representative Android 13+ device. Capture the command
output and a redacted support report after each failure or recovery.

| Scenario | Reproduction | Evidence |
|---|---|---|
| Clean install and permissions | `adb shell pm clear com.AetherRadio.AetherAprs`, launch, complete setup | Permission results, usable home screen, no transmission |
| Activity recreation | `adb shell am broadcast -a android.intent.action.CONFIGURATION_CHANGED` | Session state and foreground notification recover |
| Process death | `adb shell am force-stop com.AetherRadio.AetherAprs`, relaunch | Persisted ports restore stopped, explicit start reconnects |
| Foreground rejection | Trigger a background start while the app is backgrounded | Session remains degraded/rejected and queues remain intact |
| Network transition | Toggle emulator/device network or move between Wi-Fi and mobile data | APRS-IS port degrades, retries within bounded backoff, then recovers |
| Bluetooth loss | Disconnect the paired Classic/BLE device during receive | Port becomes degraded, queued work is preserved, reconnect is visible |
| Permission revocation | Revoke a feature permission in system settings, resume the app | Affected feature is blocked with a recovery path |

## 24-hour soak

Use the collector from the repository root:

```powershell
.\tools\android\collect-soak.ps1 `
  -PackageName com.AetherRadio.AetherAprs `
  -DurationHours 24 `
  -OutputDirectory .\artifacts\soak\<device-id>
```

Run separate receive-only, beaconing, messaging, and mixed-port sessions. Record
the configured ports and queue limits without recording passcodes, message text,
or raw packet payloads. The evidence package must include the collector output,
the final support report, and before/after values for memory, CPU, battery,
queue depth, database size, packet counts, and reconnect count.

## Field trial

Use one known-good Bluetooth Classic radio, one BLE radio, and both handset and
Bluetooth audio routes where supported. The operator records:

- device model, Android API, app version, commit, and APK SHA-256;
- route and radio firmware identifiers;
- UTC start/end times, activity type (foot or vehicle), and approximate duration;
- receive, transmit, reconnect, notification, location, audio, and battery results;
- every failure with a timestamp, visible state, reproduction steps, and redacted report ID.

The field operator signs the trial record. Engineering links the report and
mitigation status from the release checklist before publishing the release.
