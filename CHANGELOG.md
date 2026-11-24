<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC-BY-SA-4.0
-->

# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project intends to use
semantic versioning once a GA release exists.

## [Unreleased]

### Added

- Redacted, local support diagnostics with packet, message, beacon, and gateway events.
- Android Keystore-backed credential storage with explicit backup exclusions.
- Database retention, storage-pressure handling, migration backups, and recovery.
- Digipeating and RF/APRS-IS gating behind a tested policy engine with duplicate
  suppression and rate limits.
- Editable default APRS-IS server configuration and a single-active-profile rule.
- CI dependency and license scanning, APK inspection, SBOM generation, and signed artifact
  verification.
- A repo-wide documentation drift check and a NuGet vulnerability gate that fails CI.
- A contributor guide, a security policy, and this changelog.

### Changed

- Transmitted packets identify the application with the APRS software identifier
  `APZAEP` in the AX.25 destination field instead of the generic `APRS`.
- BLE KISS transmits now negotiate the ATT MTU, chunk writes to the negotiated payload
  size, and pace write-without-response operations.
- Foreground service declares `connectedDevice` and `location` types, drops `dataSync`,
  and degrades gracefully when a time-limited type expires.
- Notification Stop is delivered explicitly to the service, so it works without a live
  activity.
- Configuration files written by a newer app version are opened read-only instead of
  being rewritten or downgraded.

### Fixed

- Port start/stop serialisation, session publication ordering, and digipeat relay no
  longer share the ingest path (`C-01`, `C-05`, `C-06`, `C-07`).
- Message status now has one durable row per message with terminal states (`C-04`).
- Settings snapshots clone every property, and an unversioned settings file no longer
  skips migration (`C-09`, `C-10`).
- BLE stream shutdown, Bluetooth Classic stop, and foreground-service stop paths no
  longer hang or hide a rejected start (`C-11`..`C-14`).
- Local timestamps treat unspecified-kind values as UTC (`C-15`).
- Packet factories share source/path resolution, and SSID formatting is one documented
  display helper (`M-06`, `M-15`).
- Empty-state and result-list visibility now update after in-place collection changes
  instead of going stale (`V-13`).
- The per-object edit button and the APRS-IS/KISS TCP port placeholder use the correct
  resource keys (`V-17`).
