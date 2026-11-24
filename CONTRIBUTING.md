<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC-BY-SA-4.0
-->

# Contributing to AetherAprs

Read [AGENTS.md](AGENTS.md) first; it is the authoritative development guide. This file
summarises the rules that reviewers enforce and the five architectural invariants that
prevent most regressions.

## Build and test

```powershell
dotnet build AetherAprs.slnx
dotnet test AetherAprs.Tests/AetherAprs.Tests.csproj
dotnet build AetherAprs.Android/AetherAprs.Android.csproj
pre-commit run reuse-lint-file --all-files
```

The Android project requires the .NET Android workload and an Android SDK. Use
`--no-build` for a test run only after a successful build.

## Five architectural rules

1. **Persistence ordering.** Every mutating operation validates, writes durably, then
   mutates in-memory state under its owning lock, then raises change events outside the
   lock, then updates derived runtime state. Never publish an in-memory state that has not
   been persisted when the operation promises durability.
2. **Ownership.** Whoever creates a disposable resource disposes it. Ports never write to
   the database; `PacketStorageService` is the only packet writer. A service must not
   dispose a dependency it did not create.
3. **No UI in services.** Services must not touch `Dispatcher.UIThread`, Avalonia controls,
   or `ObservableCollection` instances owned by a view. Inject `IUiDispatcher` instead.
4. **No state writes below the view model.** `IsEnabled`, selection, and navigation state
   belong to the view model. Views bind; they do not decide.
5. **Log-level budget.** No per-packet `Information` logging and no packet payloads in
   logs. Use structured `ILogger<T>` messages at `Debug`/`Trace` for hot paths, and keep
   error logs actionable without leaking credentials or message text.

## Non-negotiables

- Nullable reference types stay enabled; `ImplicitUsings` stays disabled.
- Use CommunityToolkit.Mvvm partial `[ObservableProperty]` properties and constructor
  injection. Register every new ViewModel in `ServiceProviderFactory`, `DesignData`, and
  `ViewLocator`.
- All user-facing text is localized in both `Strings.resx` and `Strings.pt.resx`.
- New files carry SPDX metadata; run the REUSE lint before committing.
- Add or update tests for behaviour changes. A bug fix needs a regression test that fails
  without the fix.

## Pull requests

Keep the change limited to the requested scope, state assumptions when repository
behaviour is ambiguous, and include build/test evidence in the description. User-facing
behaviour changes must update `DOCS/wiki`.

Security issues must not be reported in a public issue; see [SECURITY.md](SECURITY.md).
