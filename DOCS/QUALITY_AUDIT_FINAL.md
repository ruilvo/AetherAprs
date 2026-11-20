<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC-BY-SA-4.0
-->
# AetherAprs Quality Audit — Final (release gate)

Run per `DOCS/QUALITY_AUDIT_PROMPT.md` as the "Final Quality Audit (release gate)" of
`DOCS/ROADMAP.md`, following feature phases 0–9.

**Date:** 2026-11-20
**Environment:** Windows, .NET SDK 10.0.400 (pinned by `global.json`).
**HEAD:** `9a1c476` (clean working tree, `main`).
**Baseline:** `DOCS/QUALITY_AUDIT_BASELINE.md` (`bf8bd70`).

## 1. Executive summary

The feature phases (0–9) are complete and the codebase still meets its engineering bar.
The suite has grown from **632/632** (baseline) to **868/868 passing tests** with a
**0-warning / 0-error** core build and a passing REUSE check. The two-flow architecture,
MVVM/DI boundaries, platform abstraction (zero `Android.*` in core), REUSE compliance, and
localization remain intact.

All five baseline findings (QA-01 … QA-05) are closed by the feature work. No new Blocker
or Major findings were introduced. The only outstanding item is the pre-existing,
orthogonal transport/platform SIL test-coverage gap carried in `DOCS/TESTING.md`
(phases T0–T2), which is not a release blocker for the mobile feature milestone.

## 2. Findings diff against baseline

| ID | Severity | Baseline | Final status |
|---|---|---|---|
| QA-01 | Major | Digipeater alias settings ignored; no `DigipeaterRespondToTrace` | **Closed.** Alias handling honors `DigipeaterRespondToWide1`/`DigipeaterRespondToWide2`; `DigipeaterRespondToTrace` added and wired (`Services/Ports/DigipeaterService.cs`, `Configuration/Settings/AprsSettings.cs`). |
| QA-02 | Major | `RawFrameHex`/`RawPath` not persisted | **Closed.** Both fields persisted on `PacketRecord` + `PacketRecordMapper` + migration `AddRawFrameAndPath`. |
| QA-03 | Major | AX.25 H-bit dropped | **Closed.** `Callsign.HasBeenRepeated` preserved through `Ax25Parser`; emitted by `Ax25Serializer.EncodeAddress`; digipeater skip/insert logic uses it. |
| QA-04 | Major | Telemetry/Capabilities payloads dropped | **Closed.** `TelemetryDataRecord`/`CapabilitiesDataRecord` owned types + mapping + migration `AddTelemetryAndCapabilities`. |
| QA-05 | Info | iGate `qAo`/`qAR` (AD-6) | **Closed.** `AprsIsModem` emits `qAo` (own station) / `qAR` (forwarded) instead of `TCPIP*`. |

## 3. Architecture violations

None. Re-verified the two-flow boundaries (RECEIVE / READ / TRANSMIT), the "no
ViewModel→EF Core" rule (no `Microsoft.EntityFrameworkCore`/`AppDbContext`/
`IDbContextFactory` references under `ViewModels/`), the "Port never writes the DB" rule
(`PacketStorageService` remains the only packet writer), and the platform boundary (grep of
core for `Android.*`/`Java.*`/`Android.OS` returns only documentation comments).

## 4. Security

No new findings. The APRS-IS passcode is not logged/hardcoded; EF Core queries are
parameterized; transport input (APRS-IS lines, KISS frames) is length-bounded. The
cross-port duplicate-suppression "heard" table and the AFSK transmit-holdoff table are both
bounded (256-entry, 30 s retention) to prevent unbounded growth.

## 5. Test coverage gaps

Carried forward unchanged from `DOCS/TESTING.md` (not a release blocker): the
transport/platform seam (TCP KISS E2E, APRS-IS E2E, Bluetooth abstraction) still has no
SIL coverage. Phases T0–T2 of the testing workstream remain open and are orthogonal to the
feature phases.

## 6. Cross-cutting dimensions (Phase 9 hardening goals)

- **A.6 — zero Android references in core:** verified by grep (only documentation comments
  remain). Core builds for `net10.0` cleanly.
- **E — REUSE/SPDX:** `pre-commit run reuse-lint-file --all-files` passes.
- **F — localization:** all user-facing strings use `{loc:Loc}`/`Strings.Get`; new feature
  strings present in both `Strings.resx` and `Strings.pt.resx`.
- **M — tests:** 868/868 passing (xUnit v3 + NSubstitute).
- **Platform-service completeness (Phase 9 item 1):** every platform capability now has a
  core interface + NoOp/unsupported default + Android impl. Added the missing core defaults
  for `IBluetoothLeScanner`, `IBluetoothClassicDeviceProvider`, and `IUiCultureProvider`.
- **iOS recipe (Phase 9 item 2):** documented in `DOCS/wiki/Developer-Guide.md`.

## 7. Conclusion

The release gate passes: zero open Blockers, zero open Majors, and all baseline findings
closed. The codebase is ready to tag the next mobile-feature release. The SIL test
workstream (`DOCS/TESTING.md` T0–T2) is a conscious, tracked follow-up rather than a
release blocker.
