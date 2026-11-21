# This file is part of AetherAprs
# SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
# SPDX-License-Identifier: GPL-3.0-or-later

[CmdletBinding()]
param(
    [string]$PackageName = "com.AetherRadio.AetherAprs",
    [double]$DurationHours = 24,
    [string]$OutputDirectory = ".\artifacts\soak"
)

$ErrorActionPreference = "Stop"
$duration = [TimeSpan]::FromHours($DurationHours)
$end = (Get-Date).ToUniversalTime().Add($duration)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$device = (& adb get-state).Trim()
if ($device -ne "device") {
    throw "No single ready adb device is connected."
}

& adb shell monkey -p $PackageName 1 | Out-File (Join-Path $OutputDirectory "launch.txt")
while ((Get-Date).ToUniversalTime() -lt $end) {
    $stamp = (Get-Date).ToUniversalTime().ToString("yyyyMMddTHHmmssZ")
    & adb shell dumpsys meminfo $PackageName |
        Out-File (Join-Path $OutputDirectory "$stamp-meminfo.txt")
    & adb shell dumpsys batterystats --charged $PackageName |
        Out-File (Join-Path $OutputDirectory "$stamp-battery.txt")
    & adb shell dumpsys cpuinfo |
        Select-String $PackageName |
        Out-File (Join-Path $OutputDirectory "$stamp-cpu.txt")
    & adb logcat -d -t 400 |
        Select-String "AetherAprs|AndroidRuntime|FATAL EXCEPTION" |
        Out-File (Join-Path $OutputDirectory "$stamp-logcat.txt")
    Start-Sleep -Seconds 60
}

& adb bugreport (Join-Path $OutputDirectory "bugreport.zip")
