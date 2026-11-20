<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC-BY-SA-4.0
-->
# AetherAprs Quality Audit — Baseline (pre-flight)

Run per `DOCS/QUALITY_AUDIT_PROMPT.md` as the "Initial Quality Audit (pre-flight)" gate
of `DOCS/ROADMAP.md`.

**Date:** 2026-11-20
**Environment:** Windows, .NET SDK 10.0.400 (pinned by `global.json`).
**HEAD:** `bf8bd70` (clean working tree, `main`).

## 1. Executive summary

The codebase is in strong shape: the prior audit (2026-10-14 → 2026-10-19) closed all 83
findings (AUDIT-001…083), and the suite has since grown to **632/632 passing tests** with a
**0-warning / 0-error** core build. The two-flow architecture, MVVM/DI boundaries, REUSE
compliance, and localization are intact.

The remaining open work is the **known feature/correctness debt** that Phase 0 of the
roadmap is scoped to fix — it is not a regression but the gap between "architecturally
clean" and "protocol-complete". Four items are confirmed below and mapped to Phase 0, plus
one transmit-correctness item (AD-6, the iGate q-constructor) deferred to Phase 8.

## 2. Findings (confirmed against source)

| ID | Severity | Location | Finding |
|---|---|---|---|
| QA-01 | Major | `Services/Ports/DigipeaterService.cs:292` | `BuildForwardedPath` treats `WIDE`/`TRACE` generically and ignores `AprsSettings.DigipeaterRespondToWide1` / `DigipeaterRespondToWide2`. There is no `DigipeaterRespondToTrace` setting. Alias enable/disable is dead. → Phase 0 item 1. |
| QA-02 | Major | `Data/Entities/PacketRecord.cs` + `Data/Mappers/PacketRecordMapper.cs` | Only the info field (`RawInfo`) is persisted; the full AX.25 frame hex and the path portion (`source>dest,path`) are not stored, so "raw packet view" is incomplete. → Phase 0 item 2. |
| QA-03 | Major | `Modems/Aprs/Ax25Parser.cs` | SSID decode drops the "has-been-repeated" (H) bit, so digipeater logic cannot skip already-repeated hops correctly. → Phase 0 item 3. |
| QA-04 | Major | `Data/Mappers/PacketRecordMapper.cs` | `TelemetryPacket`/`CapabilitiesPacket` resolve to their `PacketType` discriminator but have no owned-type in `ToRecord`/`MapToPacket`, so their payloads are dropped and reconstruct as `UnknownPacket`. → Phase 0 item 4. |
| QA-05 | Info | `Modems/Aprs/AprsIsModem.cs` | Client-originated packets are emitted with a `TCPIP*` q-constructor rather than `qAo`/`qAR` (AD-6). Deferred to Phase 8; noted so the Phase 8 acceptance criteria remain valid. |

## 3. Architecture violations

None found. Verified the two-flow boundaries (RECEIVE / READ / TRANSMIT), the
"no ViewModel→EF Core" rule, and "no Android.* in core" all hold in the current tree.

## 4. Security

No new findings. The APRS-IS passcode is not logged/hardcoded in source; EF Core queries
are parameterized; transport input (APRS-IS lines, KISS frames) is length-bounded.

## 5. Test coverage gaps

Carried forward from `DOCS/TESTING.md` (not a blocker for Phase 0): the transport/platform
seam (TCP KISS E2E, APRS-IS E2E, Bluetooth abstraction) has no SIL coverage yet. Phases T0–T2
of the testing workstream remain open and are orthogonal to the feature phases.

## 6. Prioritized action list (Phase 0)

1. **QA-01** — wire digipeater alias settings + add `DigipeaterRespondToTrace`.
2. **QA-02** — persist `RawFrameHex` / `RawPath` (+ migration).
3. **QA-03** — preserve the AX.25 H-bit.
4. **QA-04** — persist Telemetry/Capabilities payloads.
5. **QA-05** — (Phase 8) correct iGate q-constructors.

Each item's verify criterion is its Phase 0 acceptance criteria plus green build/tests.
