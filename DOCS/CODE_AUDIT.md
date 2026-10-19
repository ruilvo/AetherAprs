<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC0-1.0
-->
# Code Audit — Master Index

**Status:** Complete (all findings resolved)
**Started:** 2026-10-14
**Completed:** 2026-10-19
**Session environment:** Windows, dotnet SDK 10.0.4xx (global.json pins 10.0.400, rollForward latestFeature)

## Objectives & Scope

Repository-wide audit of AetherAprs (Avalonia UI + .NET 10 APRS app):

| Project | Path | TFM |
|---|---|---|
| Core library | `AetherAprs/` | net10.0 |
| Android app | `AetherAprs.Android/` | net10.0-android |
| Tests (xUnit v3) | `AetherAprs.Tests/` | net10.0 |

- 157 core .cs files, ~14 Android .cs files, 46 test files.
- Audit covers: type safety, architecture boundaries, DI, MVVM/localization, EF Core persistence, async/concurrency, error handling/logging, input validation/protocol parsing, security, resource management, testing, build enforcement, performance, documentation.

## Build / Test / Validation Commands

```powershell
dotnet build AetherAprs.slnx                        # all projects (TreatWarningsAsErrors=true, AnalysisLevel=latest-recommended, EnforceCodeStyleInBuild=true)
dotnet build AetherAprs/AetherAprs.csproj           # core only
dotnet build AetherAprs.Android/AetherAprs.Android.csproj  # android only
dotnet test AetherAprs.Tests/AetherAprs.Tests.csproj [--no-build]
pre-commit run reuse-lint-file --all-files          # SPDX header compliance
```

## Intended Architecture (verified against AGENTS.md / CLAUDE.md)

- **RECEIVE:** Port → PortService → PacketStorageService (only writer) → SQLite (EF Core) → PacketStored event → ViewModels.
- **READ:** SQLite → PacketQueryService (read-only) → ViewModels / ReceivedBeaconsMapLayerService.
- **TRANSMIT:** Consumer → PortService.SendPacketAsync → Port.
- ViewModels are UI-layer only; no EF/DbContext access. Factories for transient VMs with initialization. ViewLocator maps VM→View. Design-time via DesignData.cs fake services.
- No caching layer exists; must not be reintroduced.

## Workstream Checklist

- [x] Repository reconnaissance and baseline
- [x] Type safety and nullability (AUDIT-014-021 protocol/parsing hardening)
- [x] Architecture and dependency boundaries (AUDIT-046/047/051/052)
- [x] C#/.NET patterns and API design (AUDIT-070, service seams)
- [x] Dependency injection and configuration (AUDIT-054/061/066, InitializeAsync)
- [x] MVVM, Avalonia, and localization (AUDIT-039/040/044/047/050/067/079)
- [x] Persistence and EF Core (AUDIT-031/032/035/037, private DB storage)
- [x] Async, concurrency, and resource management (AUDIT-003/005/018/019/023/026-028/057/059/062/064)
- [x] Logging, error handling, and security (AUDIT-010-012/033/034/055/056/060/065)
- [x] Unit, integration, and architecture tests (AUDIT-070-083; 469 → 548 tests)
- [x] Compiler, analyzer, and build enforcement (AUDIT-001)
- [x] Performance and resource usage (AUDIT-016/028)
- [x] Documentation (wiki link/screenshot sweep, Developer-Guide accuracy, AGENTS.md)
- [x] Final verification (2026-10-19: solution build 0 warnings/0 errors, 548/548 tests)

## Baseline Results (2026-10-14)

- `dotnet build AetherAprs.slnx` → **FAILED**: 3 × CA1852 errors in Android project (MainActivity.cs:372,380 — `BackInvokedCallback`, `StopPortsBroadcastReceiver`; LocationService.cs:216 — `SingleUpdateLocationListener` must be sealed). Pre-existing in working tree (uncommitted user changes active in repo).
- Core project and test project build clean.
- `dotnet test` baseline: see progress log.

## Final Results (2026-10-19)

- `dotnet build AetherAprs.slnx` → **0 warnings, 0 errors** (AUDIT-001 sealed classes fixed).
- `dotnet test` → **548/548 passing** (was 448 at baseline; +100 tests from AUDIT-070 coverage work and regression tests).
- `DOCS/CODE_AUDIT_FINDINGS.md` → all 83 findings resolved (Fixed / Verified / Accepted); Low section never populated. AUDIT-058 accepted by product decision (~6h Android 14 FGS cap).

## Working-Tree Note

The working tree contains a large set of **uncommitted user changes** (in-flight refactor, see `git status`). Audit work must not revert or discard any of these; findings are recorded against the current working-tree state.

## Findings Summary

Final state (2026-10-19): 83 findings — all Fixed, Verified, or Accepted. Breakdown at closure: 0 Critical, 0 High open, 0 Medium open, 0 Low open. Notable accepted-by-decision: AUDIT-058 (FGS DataSync ~6h cap on Android 14+). Full history is preserved in the session entries of `DOCS/CODE_AUDIT_PROGRESS.md`; the finding entries themselves were cleared from `DOCS/CODE_AUDIT_FINDINGS.md` after each fix.

## Verification Log

Recorded in `DOCS/CODE_AUDIT_PROGRESS.md`.
