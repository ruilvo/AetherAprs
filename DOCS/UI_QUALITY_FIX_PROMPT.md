<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC-BY-SA-4.0
-->
# AetherAprs UI Quality Fix Prompt

A reusable, self-contained prompt that instructs an AI agent to **fix** the findings
produced by `DOCS/UI_QUALITY_AUDIT_PROMPT.md`, converging the AetherAprs UI on Material
Design 2 and the project's UI conventions while strictly obeying `AGENTS.md`.

> **How to use:** first run `DOCS/UI_QUALITY_AUDIT_PROMPT.md` to produce an audit report,
> then copy everything between `<!-- PROMPT START -->` and `<!-- PROMPT END -->` below
> into an agent **together with the audit report** (paste the report after this prompt).

---

<!-- PROMPT START -->

You are **fixing the UI** of **AetherAprs** (mobile-only Android, Avalonia UI 12,
Material.Avalonia 3.20.0) to resolve the findings in the attached UI audit report. Work
surgically, obey `AGENTS.md` at all times, and converge on the target design system below.

## Target design system (the end state you are fixing toward)

- **Material Design 2** (not MD3/Material You). 8dp grid / 4dp baseline, 2dp corners,
  drop-shadow elevation, MD2 type scale.
- **Theme:** `MaterialTheme` (`Material.Styles.Themes.MaterialTheme`), `BaseTheme="Inherit"`
  (follow system) **plus a manual System/Light/Dark override** in Settings.
- **Palette:** `PrimaryColor = Green` (`#4CAF50`), `SecondaryColor = Teal` (`#00BFA5`).
  Remove the current `LightBlue` secondary.
- **Typography:** Roboto (MD2 default), replacing Inter. Use the MD2 type scale as a
  hierarchy (Display / Headline 24 / Title 20 / Subheading 16 / Body 14 / Caption 12 /
  Button 14 medium) — no ad-hoc `FontSize`.
- **Color:** theme brushes only (`MaterialPrimaryBrush`, `MaterialBodyBrush`, etc.), no
  hardcoded hex/`White` in AXAML outside `Styles/*.axaml`.
- **Icons:** Material.Icons.Avalonia only; consistent 24dp size/weight.
- **States:** every data-driven page has empty/loading/error; every control has
  normal/disabled/pressed (and hover/focus).
- **OS integration:** edge-to-edge + system-bar theming, predictive back, system font
  scaling, system locale auto-detect, system motion/animation scale, splash-screen
  consistency. Implement what Avalonia 12 + Material.Avalonia 3.20.0 actually support;
  do not fake unsupported platform features.
- **A11y:** 48dp touch targets, WCAG 2.1 AA contrast, screen-reader semantics, no
  color-only meaning, font-scaling-safe layout.
- **House style:** `Styles/PageStyles.axaml` / `Styles/CardStyles.axaml` are mandatory for
  pages/cards; fix the house style itself where the audit says it deviates from MD2.

## Step 0 — Orient yourself

1. Read `AGENTS.md` completely (build commands, REUSE/ImplicitUsings/central package
   management, MVVM/DI rules, naming, localization, async, memory-leak rules, behavioral
   guidelines).
2. Read `CODEBASE_STRUCTURE.md`.
3. Read the attached audit report and its **prioritized action list**.
4. Read `App.axaml`, `App.axaml.cs`, `Views/MainView.axaml`, `Styles/*.axaml`, and any
   files named in the findings before touching them.
5. If a fix needs the exact Material.Avalonia API (theme brushes, `MaterialTheme`
   properties, control styles), consult `@material-avalonia` — do not guess.

## Step 1 — Triage (do not blindly apply every finding)

1. Order work by the audit's **prioritized action list**; fix **Blockers** before
   **Majors**, and skip **Info** items unless trivial and in the same file you already
   touched.
2. **Verify each finding is real** before fixing it — the auditor may have misjudged a
   library behavior. If a finding is actually a framework limitation or a false positive,
   note it and move on; do not "fix" a non-issue.
3. Group fixes by file/dimension so you touch each file once. Prefer small, reviewable
   diffs.

## Step 2 — Fix rules (non-negotiable, from AGENTS.md)

- **Surgical changes.** Touch only what the finding requires. Do not refactor adjacent
  code, rename unrelated identifiers, or "improve" formatting.
- **Simplicity first.** Minimum code to fix the finding; no speculative abstractions or
  new "flexibility".
- **Localization.** Every new/changed user-facing string must use `{loc:Loc StringKey}` in
  AXAML or `Strings.Get(...)`/`Strings.Format(...)` in code, with the key added to **both**
  `AetherAprs/Localization/Strings.resx` (en) and `Strings.pt.resx` (pt) using the
  naming conventions (title/section/field/`Description`/`Placeholder` suffixes).
- **REUSE.** Any new file must carry the correct SPDX header (GPL-3.0-or-later for code,
  CC-BY-SA-4.0 for docs). Run `pre-commit run reuse-lint-file --all-files` before finishing.
- **ImplicitUsings disabled.** Every file needs explicit `using` for everything
  (including `System`, `System.Collections.Generic`, `System.Linq`).
- **Central package management.** No package `Version` in `.csproj`; add versions to
  `Directory.Packages.props` only.
- **MVVM/DI.** No service locator in regular code; no logic in code-behind; new/changed
  ViewModels registered in `Factories/ServiceProviderFactory.cs`, `DesignData.cs`, and
  `ViewLocator.cs`; child ViewModels injected, not `new`ed.
- **Memory.** Unsubscribe events; use CommunityToolkit partial methods instead of
  self-`PropertyChanged` lambda subscriptions; wrap `async void` handlers in try/catch.
- **Naming.** `_camelCase` fields, `PascalCase` methods/properties, `camelCase`
  params/locals; `[ObservableProperty]` on partial properties, not backing fields.

## Step 3 — Known convergence fixes (do these if the audit flags them)

These are the *expected* high-impact fixes; apply them where the audit confirms them:

1. **Theme**: replace the current `MaterialTheme` declaration with
   `BaseTheme="Inherit"`, `PrimaryColor="Green"`, `SecondaryColor="Teal"`; add the
   System/Light/Dark override in Settings (persisted setting + re-apply on change and on
   startup), and ensure the theme re-evaluates on OS theme change at runtime.
2. **Font**: switch the app font from Inter to Roboto (find where Inter is wired —
   `.WithInterFont()` in `Program.cs` / `MainActivity.cs` — and replace with a Roboto
   font collection), and normalize ad-hoc `FontSize`s to the MD2 type scale.
3. **Hardcoded colors**: replace raw hex / `White` foregrounds in AXAML with theme brushes
   so dark mode works.
4. **States**: add missing empty/loading/error states to data-driven pages and
   disabled/pressed states to controls.
5. **OS integration**: implement edge-to-edge + system-bar theming and wire
   system-locale and font-scaling behavior where Avalonia 12 supports it; document (do not
   fake) what the framework does not expose.
6. **House style**: make pages/cards use the shared `PageStyles`/`CardStyles` classes, and
   correct the house style itself where it violates MD2 (title size, badge radius, etc.).

## Step 4 — Verify before finishing

For each batch of changes, and again at the end:

1. `dotnet build AetherAprs.slnx` succeeds with no new warnings.
2. `pre-commit run reuse-lint-file --all-files` passes (no missing SPDX headers).
3. No hardcoded user-facing strings remain in the files you touched (search for
   `Text="`/`Content="` literals and `Strings.`).
4. Views still resolve ViewModels (ViewLocator/DesignData/ServiceProviderFactory all
   updated).
5. Design-time preview still works (no runtime/network dependency introduced).
6. Both orientations / large-screen layout still render (no fixed pixel breakage).

## Step 5 — Report format

Finish with a concise report:

1. **Changes made** — grouped by dimension/file, each with the finding(s) it resolves.
2. **Findings not fixed** — with the reason (framework limitation, false positive,
   deferred, or out of scope).
3. **New risks** — anything introduced that needs attention.
4. **Remaining work** — a short prioritized list for a future pass.

## Rules

- Obey `AGENTS.md` and the target design system above over any "improvement" impulse.
- Do not fix findings the auditor got wrong; note them instead.
- Do not expand scope: fix the report, nothing else.
- When uncertain about Material.Avalonia's API, consult `@material-avalonia` first.

<!-- PROMPT END -->
