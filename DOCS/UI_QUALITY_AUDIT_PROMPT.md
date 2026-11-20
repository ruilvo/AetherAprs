<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC-BY-SA-4.0
-->
# AetherAprs UI Quality Audit Prompt

A reusable, self-contained prompt that instructs an AI agent to perform an exhaustive
audit of the AetherAprs user interface against **Material Design 2** and the project's
UI conventions: visual consistency, correctness of Material control usage, OS
integration, accessibility, localization, and MVVM/design-time hygiene.

> **How to use:** copy everything between `<!-- PROMPT START -->` and
> `<!-- PROMPT END -->` and paste it into an agent. It is intentionally self-contained
> (it tells the agent which files to read first and what to check) so it can be run in a
> fresh session with no extra context.

---

<!-- PROMPT START -->

You are performing a **UI quality audit** of the **AetherAprs** application. AetherAprs
is a **mobile-only** (Android) ham-radio APRS app built with **Avalonia UI 12** and
**Material.Avalonia 3.20.0**. Your goal is to produce a rigorous, evidence-based review
of the UI's conformance to **Material Design 2**, internal consistency, OS integration,
accessibility, localization, and MVVM hygiene — **not to fix anything** (unless explicitly
asked afterwards). Be exhaustive, be specific, and cite file paths and line numbers for
every finding.

## The target design system (the spec you audit against)

These are the *decisions* the UI is supposed to conform to. Treat them as the source of
truth. Where the current code disagrees with this spec, that is a finding.

- **Design language:** **Material Design 2 (MD2)**, *not* Material Design 3 / Material
  You. MD2 means: 4dp baseline grid (8dp spacing grid), 2dp default corner radius (not
  28dp), drop-shadow elevation (not tonal elevation), rectangular/outlined/flat button
  styles (not filled pill buttons), and the MD2 type scale. Flag anything that is an
  MD3-ism.
- **Theme engine:** the `MaterialTheme` control (`Material.Styles.Themes.MaterialTheme`).
  The `BundledTheme` class is `[Obsolete]` — flag any usage of it.
- **Light/dark:** `BaseTheme` must be `Inherit` (follow the OS theme) by default, with a
  **manual override** in Settings offering **System / Light / Dark**. The app must
  re-evaluate when the OS theme changes at runtime.
- **Palette:** `PrimaryColor = Green` (`#4CAF50`, `Green500`), `SecondaryColor = Teal`
  (`#00BFA5`, `TealA700`). The secondary (accent) must *not* be `LightBlue` (current
  drift). Reference shades (use Material's dynamic brushes, not raw hex):

  | Role | Swatch | Hex | Material.Avalonia enum |
  |---|---|---|---|
  | Primary | Green 500 | `#4CAF50` | `PrimaryColor.Green` |
  | Primary (pressed/text) | Green 600 / 700 | `#43A047` / `#388E3C` | — |
  | Primary (light fill) | Green 50 / 100 | `#E8F5E9` / `#C8E6C9` | — |
  | Secondary (accent) | Teal A700 | `#00BFA5` | `SecondaryColor.Teal` |
  | Secondary (tint) | Teal 200 | `#80CBC4` | — |

- **Typography:** **Roboto** (the MD2 default), not Inter. Enforce the MD2 type scale as
  a *consistent hierarchy* rather than ad-hoc `FontSize` values: Display 4/3/2/1,
  Headline (24), Title (20), Subheading (16), Body 2 (14 medium), Body 1 (14), Caption
  (12), Button (14 medium). Flag arbitrary sizes (e.g. `FontSize="24"` for a page title
  that should be `20` Title, or `12`/`13` off-scale values).
- **Color usage:** always reference theme brushes
  (`MaterialPrimaryBrush`, `MaterialSecondaryBrush`, `MaterialBodyBrush`,
  `MaterialSecondaryBodyBrush`, `MaterialPrimaryForegroundBrush`, …), never hardcoded
  hex in AXAML (the only tolerated exception is inside `Styles/*.axaml` where a token is
  *defined*, not where it is *used*). Flag `White`/`#FFFFFF` text hardcoded instead of
  the theme's foreground brush (breaks dark mode).
- **Iconography:** **Material.Icons.Avalonia** (the community Material Design Icons set)
  is the *only* allowed icon source. Flag ad-hoc geometry, emoji, or mismatched
  icon sizes/weights. Tab icons via `MaterialIconGeometryExt` must resolve to MDI glyphs.
- **Interaction states (hard requirement):** every data-driven page must have **empty,
  loading, and error** states; every interactive control must have **normal, disabled,
  and pressed** states (and hover/focus where relevant). Flag pages/controls that render
  a permanent blank/zero state or silently do nothing on error.
- **OS integration (the headline goal):** the UI must feel native on Android. The audit
  must check (and distinguish *framework-supported* from *not-supported* — consult
  `@material-avalonia` and the Avalonia 12 docs before flagging):
  1. **Edge-to-edge + system bars** — content draws behind the status/navigation bars;
     bars tinted to match theme; no content clipped under bars or undercut by the nav bar.
  2. **Predictive back** (Android 14+).
  3. **System font scaling** — respects the user's font-size setting with no clipped or
     overflowing text.
  4. **System locale auto-detect** — `OsUiCultureProvider`/`UiCulture` wired so the app
     follows the OS language without restart.
  5. **System motion/animation scale** — respects reduced-motion / animation scale.
  6. **Splash screen** consistency with the theme (AndroidX SplashScreen is already
     referenced).
- **Form factors:** the UI must be correct in **all orientations and large screens**
  (tablets/foldables/resizable windows), not just the 400×800 phone-portrait
  design-time default.
- **Accessibility (hard requirements):** minimum **48dp** touch targets (Material
  accessibility guideline), **WCAG 2.1 AA** contrast on text, screen-reader semantics
  (`AutomationProperties.Name` / `HelpText` on icon-only or ambiguous controls), no
  color-as-the-only-means-of-conveying-information, correct focus/tab order.
- **House style:** `Styles/PageStyles.axaml` and `Styles/CardStyles.axaml` define the
  mandatory house style for pages and cards. **Every page must use the house style
  (`.PageContainer`, `.PageTitle`, `.PageSubtitle`, `.SectionTitle`, `.EmptyState`,
  `.StandardCard`, `.ClickableCard`, badges, etc.) rather than ad-hoc inline styling.**
  At the same time, **audit the house style itself** against MD2 (e.g. a page title of
  `FontSize="24"` vs MD2 Title 20; badge `CornerRadius="4"` vs MD2 2dp; `MaxWidth=600`
  that should be an MD2 measure).

## Step 0 — Orient yourself (read these first, in order)

1. `AGENTS.md` — MVVM/DI rules, naming conventions, memory-leak rules, localization
   rules, async rules, and behavioral guidelines.
2. `CODEBASE_STRUCTURE.md` — folder organization for `Views/`, `ViewModels/`,
   `Styles/`, `Converters/`, `Services/UI/`.
3. `App.axaml` and `App.axaml.cs` — current theme setup, DI, font configuration.
4. `Views/MainView.axaml` — the app shell (TabbedPage navigation, Snackbar/Dialog hosts,
   keyboard insets).
5. `Styles/PageStyles.axaml` and `Styles/CardStyles.axaml` — the house style.
6. `DOCS/UI_QUALITY_AUDIT_PROMPT.md` (this spec) — the target design system above.
7. Skim all `Views/**/*.axaml`, `Views/**/*.axaml.cs`, `ViewModels/**/*.cs`,
   `Converters/UI/*.cs`, `Services/UI/*.cs`, and `Localization/Strings*.resx`.

If you need the exact API or behavior of Material.Avalonia (control names, theme brushes,
`MaterialTheme` properties, elevation, `SnackbarHost`, `DialogHost`), consult the
`@material-avalonia` reference — **do not guess from memory.**

## Step 1 — Inventory

Produce a factual inventory before judging:

- Count of page Views (under `Views/Pages/`) and component Views (`Views/Components/`),
  and the app shell (`MainView`, `MainWindow`).
- Count of page ViewModels and component ViewModels.
- List of shared styles (`Styles/*.axaml`) and the style classes they define.
- List of UI services (`Services/UI/`) and UI converters (`Converters/UI/`).
- The current theme declaration (base theme, primary/secondary color, font) as it exists
  in `App.axaml` / `App.axaml.cs`.
- List of icons used and their source (Material.Icons vs ad-hoc).
- List of dialog/snackbar usages (DialogHost identifiers, SnackbarHost names).

## Step 2 — Audit dimensions

For each dimension below, report **Findings**, each with a **Severity** and **Location**.
Use severity labels consistently:

- **Blocker** — violates a hard requirement (MD2 conformance, OS integration, a11y,
  localization, theme correctness), breaks the UI, or is a crash risk.
- **Major** — material visual/consistency/UX defect that should be fixed soon.
- **Minor** — style drift, spacing, or naming inconsistency with no functional impact.
- **Info** — observation, suggestion, or question.

### A. Material Design 2 conformance (highest priority)

1. Correct control for the purpose (MD2): lists use list controls / `Card`, transient
   feedback uses `SnackbarHost`, confirmations/prompts use `DialogHost`, primary action
   uses a `FloatingActionButton`, tabs use the MD2 bottom-navigation pattern.
2. Flag **MD3-isms**: 28px corner radius, tonal/filled pill buttons, tonal elevation,
   large "chip" surfaces, MD3 color roles.
3. Flag hand-rolled substitutes where a Material control exists (custom-styled `Button`
   pretending to be a `Card`, custom modal instead of `DialogHost`, custom toast instead
   of `SnackbarHost`).
4. Elevation: correct use of raised vs flat surfaces; cards elevate, content areas don't;
   no invented elevation levels.
5. Spacing: adherence to the 8dp grid and 4dp baseline; no arbitrary `1`/`3`/`5`/`7` px
   paddings/margins where a 4dp/8dp increment belongs.

### B. Theme & color system

1. `MaterialTheme` used (not `[Obsolete] BundledTheme`), with `BaseTheme="Inherit"`.
2. Manual override (System/Light/Dark) exists and actually re-applies the theme.
3. `PrimaryColor = Green`, `SecondaryColor = Teal` (flag the current `LightBlue`
   secondary and any other drift).
4. Consistent brush usage — no hardcoded hex/`White` in AXAML outside style definitions.
5. Dark-mode correctness — every surface readable in dark; no hardcoded light/dark
   assumptions that break one mode.
6. WCAG AA contrast on text over its background (compute where possible).

### C. OS integration (headline goal — pulled out separately in the report)

For each of the six behaviors in the target spec (edge-to-edge/system bars, predictive
back, font scaling, locale auto-detect, motion/animation scale, splash screen):
1. State whether it is **implemented, partially implemented, or absent** — with file
   evidence.
2. State whether the absence is a **framework limitation** (Avalonia 12 / Material
   .Avalonia 3.20.0 does not expose it) or a **missed implementation** — consult
   `@material-avalonia` / Avalonia 12 docs; do not flag a framework limitation as if the
   app could trivially fix it.
3. Check `KeyboardInsetsService`/`KeyboardInsetsBehavior` correctness (content not hidden
   behind the soft keyboard).
4. Check system-bar theming (status/nav bar colors, light/dark icons on the bars).

### D. Layout & responsiveness

1. All orientations + large screens: no fixed pixel widths/heights that clip or overflow;
   content scrolls; `MaxWidth` used sensibly for large screens.
2. Bottom-nav (TabbedPage) vs content overlap; FAB not obscuring last list item.
3. Keyboard insets applied everywhere input is possible.
4. Nested scrolling correctness (no scroll-in-scroll conflicts; maps and lists coexist).

### E. Typography

1. Roboto (not Inter) is the effective font.
2. MD2 type scale used as a hierarchy; no ad-hoc `FontSize`.
3. Consistent weights (Medium for titles/buttons, Regular for body, Light for Display);
   correct line-height/letter-spacing defaults.
4. Flag off-scale sizes and inconsistent emphasis (bold used as a crutch).

### F. Iconography

1. Only Material.Icons.Avalonia glyphs are used.
2. Consistent size (24dp default) and weight.
3. Icons match their action/label (no misleading glyphs).
4. Tab icons resolve to MDI glyphs via `MaterialIconGeometryExt`.

### G. Consistency & house style

1. Every page uses the house style classes from `PageStyles.axaml` / `CardStyles.axaml`;
   flag inline ad-hoc styling that duplicates or contradicts them.
2. Cards are uniform: consistent padding, spacing, badge styles, click affordance.
3. Audit the house style itself against MD2 (title size, badge radius, max-widths).
4. Flag repeated-but-inconsistent patterns (two different "empty state" renderings, two
   different card paddings, two different section-header styles).

### H. Component usage & dialogs

1. `SnackbarHost` used for transient/undo/error feedback; not abused for confirmations.
2. `DialogHost` used for dialogs; dialogs are focusable, dismissible, and have a clear
   primary action; correct dialog style (simple vs custom).
3. Lists use the appropriate control (`ListBox`/`ItemsControl` + `Card`, or
   `Material.Avalonia.DataGrid` where tabular data fits).
4. Buttons use the correct Material style (raised for primary, flat/text for secondary,
   outlined for medium emphasis) — not all-raised or all-flat.

### I. Interaction states (hard requirement)

1. Every data-driven page has empty, loading, and error states (with retry where
   sensible).
2. Every interactive control has normal/disabled/pressed (and hover/focus) states; no
   control that is "always enabled" when it can't act, or visually inert when disabled.
3. No silent failure — errors surface via snackbar/dialog/empty-state, not silence.

### J. Accessibility (hard requirement — pulled out separately in the report)

1. **48dp minimum touch targets** for all tappable elements (buttons, list items, tab
   items, FABs, icon buttons).
2. **WCAG 2.1 AA contrast** (4.5:1 body, 3:1 large text/UI components).
3. **Screen-reader semantics** — `AutomationProperties.Name`/`HelpText` on icon-only
   buttons, images, and the map; meaningful content descriptions.
4. No **color-only** meaning (e.g. "green = connected" must also have a label/icon).
5. Font scaling does not break layout (ties to C.3).
6. Focus order and keyboard/d-pad reachability where relevant.

### K. Localization

1. All user-facing text via `{loc:Loc StringKey}` (AXAML) or `Strings.Get(...)` /
   `Strings.Format(...)` (code); flag any hardcoded string.
2. Every key exists in **both** `Strings.resx` (en) and `Strings.pt.resx` (pt); flag
   missing/extra keys and empty values.
3. **Overflow/truncation** for the longer Portuguese strings — flag labels sized for
   English that clip in pt.
4. Correct non-localization of log/exception/technical strings.

### L. MVVM & binding hygiene

1. No UI/business logic in code-behind (`*.axaml.cs`); code-behind only wires view events
   to the ViewModel.
2. Views resolve ViewModels via `ViewLocator` (`<ContentControl Content="{Binding ...}" />`);
   no manual view instantiation, no inline `DataTemplate` for VM→View mapping.
3. No service locator in ViewModels/views (documented exception: platform entry points).
4. Bindings are correct and error-free at design time (no obvious `#ERROR`/unresolved
   bindings); `x:DataType` set for compiled bindings.
5. No memory leaks: event subscriptions unsubscribed; no lambda subscription to own
   `PropertyChanged` (use CommunityToolkit partial methods).

### M. Design-time support

1. Every ViewModel is registered in `Factories/ServiceProviderFactory.cs` (runtime),
   `DesignData.cs` (design-time, with an exposed public property), and mapped in
   `ViewLocator.cs`.
2. Views preview in the Avalonia designer without runtime/network/platform deps.
3. `d:DataContext` / design-time data is realistic (not empty).

### N. Structure & placement

1. Views in `Views/{Pages|Components|Windows}/`, ViewModels in `ViewModels/{Pages|
   Components}/`, UI services in `Services/UI/`, UI converters in `Converters/UI/`,
   shared styles in `Styles/` — per `CODEBASE_STRUCTURE.md`.
2. Namespace matches folder structure exactly.
3. No orphaned styles/converters/ViewModels (dead code left over from refactors).

### O. Performance

1. Lists virtualize (`VirtualizingStackPanel`) — flag `StackPanel`-inside-`ScrollViewer`
   for large/unbounded collections.
2. No layout thrash or expensive work in bindings/converters on hot paths.
3. APRS symbol bitmaps and map tiles are cached (no re-decode/re-fetch per render).

## Step 3 — Report format

Produce a single structured report:

1. **Executive summary** — 3–6 sentences: overall UI health, biggest risks, top 5 things
   to fix first.
2. **Findings by dimension** — grouped A–O, each finding as:
   - `[Severity] Location` — one-line title
   - 1–3 sentences of explanation
   - suggested fix (concise).
3. **OS integration findings** — a dedicated section listing every C-dimension finding
   explicitly, each labeled *implemented / partial / absent* and *framework-limitation vs
   missed-implementation* (these are the headline concern).
4. **Accessibility findings** — pulled out separately for visibility.
5. **Consistency drift** — the specific places where the house style is violated or the
   house style itself deviates from MD2.
6. **Prioritized action list** — top 10 concrete fixes in dependency order, each with a
   one-line "verify" criterion.

## Rules for the audit

- **Evidence over opinion.** Every finding cites a file path and (where useful) a line
  number or symbol. No hand-waving.
- **Respect "Surgical Changes".** You are *auditing*, not fixing. Do not edit any code.
  Report only.
- **Distinguish spec violations from preferences.** A violation of the target design
  system above, `AGENTS.md`, or `CODEBASE_STRUCTURE.md` is a Blocker/Major; a mere
  aesthetic preference is Minor/Info.
- **Do not invent problems.** If a dimension is clean, say so in one line.
- **Be complete but not noisy.** Aggregate repeated instances of the same mistake into
  one finding with a list of locations.
- **Do not guess library behavior.** Consult `@material-avalonia` (and Avalonia 12 docs)
  before flagging a control usage as wrong or an OS feature as "missing."
- **"Beautiful" is operationalized as:** MD2 conformance + the 8dp/4dp grid + consistent
  typography hierarchy + consistent color from theme brushes + correct elevation +
  consistent iconography + complete states. Judge against those, not vibes.

<!-- PROMPT END -->
