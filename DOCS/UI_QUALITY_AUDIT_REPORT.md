<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC-BY-SA-4.0
-->
# AetherAprs UI Quality Audit Report

Run per `DOCS/UI_QUALITY_AUDIT_PROMPT.md`. This is an **audit only** — no code was
changed. Evidence is cited as `file:line`.

**Date:** 2026-11-20
**HEAD:** `66346dd` (clean working tree, `main`).
**Target design system:** Material Design 2 (MD2), Material.Avalonia 3.20.0, Avalonia UI 12.

---

## 1. Executive summary

The UI is structurally sound and disciplined — MVVM/DI boundaries, localization, icon
source, touch-target sizing, and house-style adoption are consistently applied across 15
pages and 7 components. The highest-severity problems are **theme and font drift** from the
MD2 spec: the app is hard-locked to `Light` base theme with a `LightBlue` secondary (spec
requires `Inherit` + Green/Teal plus a Settings System/Light/Dark override), and it ships
**Inter** instead of **Roboto**. OS integration — the headline goal — is largely unmet:
there is **no edge-to-edge/system-bar theming**, predictive back is only partially wired,
and the splash screen is hardcoded white. Accessibility has a systemic gap: **zero
`AutomationProperties`** anywhere, so icon-only FABs and buttons have no screen-reader name.

Top 5 to fix first: (1) theme palette + `BaseTheme=Inherit` + Settings override, (2) Roboto
font, (3) edge-to-edge + system-bar theming, (4) `AutomationProperties` on icon-only
controls, (5) empty states on Ports/Messages pages.

---

## 2. Inventory

- **Page Views** (`Views/Pages/`): 15 — AddEditPort, Bulletins, Conversation,
  DownloadOfflineMap, DynamicBeaconing, Home, MessageTemplates, Messages, ObjectEditor,
  Objects, PacketDetails, Packets, Ports, Search, Settings.
- **Component Views** (`Views/Components/`): 7 — AprsIsFilterBuilder, AprsSymbolPicker,
  MessageTemplateDialog, MessageTemplateEditDialog, PortItem, ScrollablePageContent,
  SymbolSelector.
- **App shell:** `Views/MainView.axaml` (TabbedPage + DialogHost + SnackbarHost),
  `Views/Windows/MainWindow.axaml` (desktop window).
- **Page ViewModels:** 15; **Component ViewModels:** 8 (`AprsIsFilterBuilderViewModel`,
  `AprsSymbolPickerViewModel`, `MapViewModel`, `MessageTemplateDialogViewModel`,
  `MessageTemplateEditViewModel`, `PortItemViewModel`, `SymbolSelectorViewModel`,
  `TemplateFieldViewModel`); plus `MainViewModel`, `ViewModelBase`.
- **Shared styles:** `Styles/PageStyles.axaml`, `Styles/CardStyles.axaml`.
- **UI services** (`Services/UI/`): Navigation, KeyboardInsets, OsUiCultureProvider,
  IUiCultureProvider, OfflineTilePackageDownloader, MbTilesTileSource, MapTileSourceService,
  MapClusterer, ReceivedBeaconsMapLayerService, UserLocationLayerService, AvaloniaUiDispatcher,
  IUiDispatcher, IConfirmationDialogService, DialogHostConfirmationService, IMapTileSourceService,
  IKeyboardInsetsService, IOfflineTilePackageDownloader.
- **UI converters** (`Converters/UI/`): 8 — CollectionEmptyToBoolConverter, DateTimeToLocalConverter,
  EnumDisplayConverter, EnumToBoolConverter, PacketDisplayTimeRangeConverter, RssiFormatConverter,
  TileSourceTypeConverter, TypeNameToIsVisibleConverter.
- **Theme declaration** (`App.axaml:19`): `MaterialTheme BaseTheme="Light" PrimaryColor="Green"
  SecondaryColor="LightBlue"`. No `BundledTheme` anywhere.
- **Font:** `Avalonia.Fonts.Inter` (`Directory.Packages.props:18`), wired via
  `.WithInterFont()` (`AetherAprs.Android/Application.cs:130`).
- **Icons:** `Material.Icons.Avalonia` glyphs only (Home, Message, PackageVariant, Lan, Cog,
  Plus, Minus, CrosshairsGps, Bluetooth, Stop, Refresh, ChevronRight). No ad-hoc geometry.
- **Dialog/Snackbar:** DialogHost `Identifier="MainDialogHost"` + SnackbarHost
  `HostName="MainSnackbarHost"` declared in `MainView.axaml:23-24`.

---

## 3. Findings by dimension

### A. Material Design 2 conformance

- **[Blocker] `App.axaml:19`** — `BaseTheme="Light"` hardcodes light mode. Spec requires
  `BaseTheme="Inherit"` (follow OS) plus a manual override. As written, the app never enters
  dark mode even when the OS is dark.
- **[Blocker] `App.axaml:19`** — `SecondaryColor="LightBlue"`; spec requires `Teal`
  (`#00BFA5`). This is the documented "current drift."
- **Correct (no finding):** FABs use `controls:FloatingButton` (Ports `:48`, Messages `:60`,
  Home `:32/41/50`); confirmations use `DialogHost`; lists use `ItemsControl`+`Card`; tabs use
  `TabbedPage TabPlacement="Bottom"`. No MD3-isms (28dp corners, tonal elevation, filled pill
  buttons, MD3 color roles) were found.

### B. Theme & color system

- **[Blocker] No System/Light/Dark override.** `AppSettings` (`Configuration/Settings/AppSettings.cs`)
  has only `Logging`, `Aprs`, `MessageTemplates`, `Map` — no theme field. A repo-wide grep for
  `RequestedThemeVariant|ThemeVariant|ThemeMode|BaseTheme|AppTheme|Dark|Light` in `*.cs`
  returns nothing. There is no runtime OS-theme re-evaluation either (ties to A).
- **[Major] `Styles/CardStyles.axaml:57`** — `.PrimaryBadge > TextBlock` sets
  `Foreground="White"`. This is a *use* of a hardcoded color, not a token definition, and
  breaks on light-primary surfaces; should be `MaterialPrimaryForegroundBrush`.
- **Correct (no finding):** No hardcoded hex/`White`/`Black` in any `Views/**/*.axaml`
  (grep for `Foreground/Background/BorderBrush/Color = "White|Black|#…"` returns zero
  matches); pages consistently use `MaterialSecondaryBodyBrush`, `MaterialPrimaryBrush`,
  `MaterialBackgroundBrush`, `MaterialDividerBrush`, `MaterialValidationErrorBrush`,
  `MaterialErrorBrush`.

### C. OS integration (see §4 for the full dedicated matrix)

Summary: locale auto-detect and keyboard insets are implemented; edge-to-edge/system-bar
theming is **absent**; predictive back is **partial**; splash consistency is **partial**
(hardcoded white); font-scaling and motion-scale behavior are **unverified/absent**.

### D. Layout & responsiveness

- **[Major] `Views/Pages/PortsPage.axaml`, `Views/Pages/MessagesPage.axaml`** — no fixed-width
  breakage, but the empty state is absent (see I). Layout otherwise uses `MaxWidth=600`
  (`PageStyles.axaml:44`, `AddEditPortPage.axaml:54`) and scrollable content.
- **Correct (no finding):** FABs sit in `Grid.Row=1` bottom-right with `PageContent` bottom
  padding `0,0,0,80` (`PageStyles.axaml:37`), so they do not obscure the last list item.
  `KeyboardInsetsBehavior` is attached at the shell (`MainView.axaml:26`), and every input
  page is a `ScrollViewer`/`ScrollablePageContent`.

### E. Typography

- **[Blocker] Inter, not Roboto.** `Directory.Packages.props:18` + `.WithInterFont()`
  (`AetherAprs.Android/Application.cs:130`). No Roboto font collection is configured.
- **[Major] House title size off MD2.** `PageStyles.axaml:23` sets `.PageTitle FontSize=24`
  (MD2 Headline) where a page title should be MD2 **Title 20**; `FontWeight="Bold"` should be
  Medium. `PageStyles.axaml:49` sets `.SectionTitle FontSize=20` (MD2 Title) where a section
  header should be **Subheading 16**.
- **[Major] Bold-as-emphasis crutch.** `FontWeight="Bold"`/`"SemiBold"` layered on top of
  Material typography classes (`Subtitle1`, `Headline5`) in `MessagesPage.axaml:46`,
  `SettingsPage.axaml:286/319/351/373`, `AddEditPortPage.axaml:45/196/231`. MD2 uses Medium
  (500) for emphasis, not Bold (700)/SemiBold (600).
- **Correct (no finding):** No off-scale ad-hoc `FontSize` in Views (only on-scale `12`
  Caption usages in `AddEditPortPage.axaml:197-198,232`).

### F. Iconography

- **Correct (no finding):** Only `Material.Icons.Avalonia` glyphs; consistent 24dp (via
  `<Viewbox Width="24" Height="24">`), 20dp in BLE buttons (`AddEditPortPage.axaml:169/179`)
  which is a minor size inconsistency. Tab icons resolve via `MaterialIconGeometryExt`
  (`MainView.axaml:36/44/52/60/68`).

### G. Consistency & house style

- **[Major] Page-title pattern is inconsistent.** Ports/Messages/Settings use the house
  `.PageTitle` class; `AddEditPortPage.axaml:43-45` instead uses `Classes="Headline5"`
  + `FontWeight="Bold"`. `HomePage` has no page header at all (a bare map).
- **[Major] Secondary-text treatment is inconsistent.** Most pages use
  `MaterialSecondaryBodyBrush`; `AddEditPortPage.axaml:197-198,232` and
  `SearchPage.axaml:88` use `Opacity="0.7"` instead — a contrast hazard and a divergence
  from the house convention.
- **[Minor] Error-brush inconsistency.** `AddEditPortPage.axaml:187/222` uses
  `MaterialErrorBrush`; `ConversationPage.axaml:70`, `BulletinsPage.axaml:85`,
  `ObjectEditorPage.axaml:83` use `MaterialValidationErrorBrush` for the same semantic role.
- **[Minor] Badge radius off MD2.** `CardStyles.axaml:38/51` set `CornerRadius="4"` (spec:
  2dp). `Padding="8,2"`/`"8,4"` also off the 4dp baseline (should be `8,4`).

### H. Component usage & dialogs

- **[Major] SnackbarHost declared but never used.** `MainSnackbarHost` (`MainView.axaml:24`)
  is the only Snackbar reference in the codebase; no code ever shows a snackbar. Transient
  feedback (e.g. "saved", "port started", undo) has no path to the UI — feedback is limited
  to inline status text and confirmation `DialogHost` dialogs.
- **[Minor] Confirmation dialog built in code.** `DialogHostConfirmationService.cs:30-85`
  constructs `TextBlock`/`Button` controls in C# with ad-hoc margins and `MaxWidth=400`,
  and its buttons set `MinWidth=88` with **no** `MinHeight=48` (`:49-64`) — below the 48dp
  touch target. Functionally fine (uses localized `Strings.Get("Cancel"/"Confirm")`), but it
  bypasses the XAML house style and the a11y target.

### I. Interaction states (hard requirement)

- **[Blocker] Missing empty states on two data-driven pages.** `PortsPage.axaml:31` and
  `MessagesPage.axaml:29` render an `ItemsControl` with no empty-state element — with zero
  ports/conversations the page is a permanent blank list. Contrast with `ObjectsPage`,
  `BulletinsPage`, `PacketsPage`, `SearchPage`, `MessageTemplatesPage`, `PacketDetailsPage`,
  which all define a `Classes="EmptyState"` block.
- **Correct (no finding):** Loading states (`IsLoading` → indeterminate `ProgressBar`) and
  inline error text (`ErrorMessage`/`BleStatusText`/`SppStatusText` with `IsNotNullOrEmpty`
  visibility) are present on the list/detail pages. Controls use `IsEnabled`/`CanExecute`
  gating (e.g. BLE scan buttons `AddEditPortPage.axaml:166/176`).

### J. Accessibility (see §5 for the full dedicated list)

Summary: touch targets are mostly 48dp; contrast is undermined by `Opacity="0.7"` text;
screen-reader semantics are **entirely absent** (zero `AutomationProperties`).

### K. Localization

- **Correct (no finding):** All user-facing text uses `{loc:Loc …}` (AXAML) or
  `Strings.Get/Format` (code). The only hardcoded string is the app name
  `Title="AetherAprs"` (`Views/Windows/MainWindow.axaml:18`), which is not a translatable
  string. `locales_config.xml` declares `en-US` + `pt`, matching `Strings.resx` +
  `Strings.pt.resx`.
- **[Minor] `SettingsPage.axaml:102/271/276`** — numeric `PlaceholderText="12"/"5"/"30"`
  are literal but are value hints, not prose; low priority.

### L. MVVM & binding hygiene

- **Correct (no finding):** Views resolve ViewModels via `ViewLocator` +
  `<ContentControl Content="{Binding …}"/>`; no manual view instantiation or inline
  VM→View `DataTemplate`. `x:DataType` is set on every page. Service-locator use is confined
  to the documented exceptions: `KeyboardInsetsBehavior.cs:32` (Avalonia Behavior has no DI
  injection point — already noted in `CODE_AUDIT_PROGRESS.md`) and `MainActivity.cs`
  platform entry points. `DialogHost`-based views are instantiated in code-behind
  (`AprsSymbolPickerComponent.axaml.cs`, `ConversationPage.axaml.cs`, etc.), which is the
  accepted DialogHost pattern.

### M. Design-time support

- **Correct (no finding):** All ViewModels are registered in `ServiceProviderFactory.cs`,
  `DesignData.cs` (with public properties), and `ViewLocator.cs`; every View carries
  `d:DataContext="{x:Static local:DesignData.…}"` with realistic data.

### N. Structure & placement

- **[Minor] Orphaned doc reference.** `CODEBASE_STRUCTURE.md:46` lists
  `Converters/UI/BoolToTrackingStatusConverter.cs`, which no longer exists (the current
  `Converters/UI/` has 8 files, none named that). Documentation-only; no dead code.
- **Correct (no finding):** Namespace/folder placement matches `CODEBASE_STRUCTURE.md`.

### O. Performance

- **[Info] Non-virtualized lists.** `PortsPage.axaml:31`, `MessagesPage.axaml:29` use
  `ItemsControl` with a `StackPanel` panel (not virtualized). For a ham-radio app the
  conversation/port counts are small, so this is acceptable today, but it will not scale if
  the list grows unbounded.
- **Correct (no finding):** APRS symbol bitmaps are cached (`AprsSymbolBitmapProvider`) and
  OSM tiles use a file cache (`App.axaml.cs:250-264`).

---

## 4. OS integration findings (headline)

| # | Behavior | Status | Classification | Evidence |
|---|---|---|---|---|
| 1 | Edge-to-edge + system bars | **Absent** | Missed implementation (verify against Avalonia 12) | No `EnableEdgeToEdge`, `WindowCompat.setDecorFitsSystemWindows`, status/nav-bar color or light/dark icon config anywhere in `MainActivity.cs`. Theme is `Theme.AppCompat.DayNight.NoActionBar` (`styles.xml:13`). |
| 2 | Predictive back | **Partial** | Missed implementation | `OnBackInvokedCallback` registered on `OnResume` (`MainActivity.cs:227-230`) and unregistered on `OnPause` (`:446-455`). No `android:enableOnBackInvokedCallback` manifest opt-in and no Android 14 predictive-back-to-home animation handling; legacy `OnBackPressed()` (`:475`) and `Finish()` fallback remain. |
| 3 | System font scaling | **Unverified** | Framework-dependent | No explicit font-size override found (a good sign), but no tests/guarantees that scaling doesn't clip; `HomePage` map labels use a fixed-size `Font` (`ReceivedBeaconsMapLayerService.cs:450/491`). |
| 4 | System locale auto-detect | **Implemented** | — | `AndroidUiCultureProvider` reads per-app locale (`AetherAprs.Android/Services/Platform/AndroidUiCultureProvider.cs`); `Localization.UiCulture.Apply` called at startup (`App.axaml.cs:125`, `MainActivity.cs:190`); `android:localeConfig` + `locales_config.xml` (en-US, pt). |
| 5 | Motion/animation scale | **Unverified** | Framework-dependent | No reduced-motion handling found; likely a framework limitation — verify before flagging. |
| 6 | Splash screen consistency | **Partial** | Missed implementation | `splash_screen.xml` is a legacy `layer-list` (windowBackground), not AndroidX SplashScreen; `colors.xml:9` hardcodes `#FFFFFF` — not the Green primary and not dark-mode aware. |

**Note on #1:** Avalonia 12 on Android exposes edge-to-edge via the activity/top-level inset
APIs; this should be confirmed against the Avalonia 12 docs before implementing, but the
current state (no system-bar theming at all) is a genuine gap regardless of the exact API.

---

## 5. Accessibility findings (dedicated)

- **[Blocker] Zero `AutomationProperties` anywhere.** A repo-wide grep for
  `AutomationProperties` returns matches only in the audit/fix prompt docs — none in
  `Views/`. Icon-only controls therefore have no accessible name:
  - Ports/Messages FAB (`PortsPage.axaml:48`, `MessagesPage.axaml:60`) — `Plus` icon only.
  - Home zoom/center FABs (`HomePage.axaml:32/41/50`) have a localized `ToolTip.Tip` but
    **no** `AutomationProperties.Name` (tooltips are not reliably read by screen readers).
  - `SettingsPage`/`SearchPage`/`ObjectsPage` `ChevronRight` icons are decorative but carry
    no name/`IsIgnored` hint.
- **[Major] Contrast via opacity.** `AddEditPortPage.axaml:197-198,232` and
  `SearchPage.axaml:88` render secondary text with `Opacity="0.7"` over the surface instead
  of a theme secondary brush — likely below WCAG 4.5:1. Replace with
  `MaterialSecondaryBodyBrush`.
- **Correct (no finding):** Touch targets are 48dp in the vast majority of interactive
  controls (`MinHeight="48"` on buttons/checkboxes/textboxes; FABs `MinHeight/Width="56"`).
  Exceptions: `DialogHostConfirmationService.cs:49-64` buttons (no `MinHeight=48`).

---

## 6. Consistency drift (house style vs MD2)

1. `.PageTitle` `FontSize=24` + `Bold` (should be Title **20** / Medium) — `PageStyles.axaml:22-25`.
2. `.SectionTitle` `FontSize=20` (should be Subheading **16**) — `PageStyles.axaml:48-51`.
3. `TypeBadge`/`PrimaryBadge` `CornerRadius="4"` (should be **2dp**) — `CardStyles.axaml:38/51`.
4. `.PrimaryBadge > TextBlock` `Foreground="White"` (should be `MaterialPrimaryForegroundBrush`) — `CardStyles.axaml:57`.
5. `AddEditPortPage` uses `Headline5`+`Bold` instead of `.PageTitle` — `AddEditPortPage.axaml:43-45`.
6. Secondary text via `Opacity="0.7"` vs `MaterialSecondaryBodyBrush` — `AddEditPortPage.axaml:197-198/232`, `SearchPage.axaml:88`.
7. Two error brushes in play — `MaterialErrorBrush` vs `MaterialValidationErrorBrush`.

---

## 7. Prioritized action list (dependency order)

1. **Theme palette + base theme.** Change `App.axaml:19` to `BaseTheme="Inherit"`
   `PrimaryColor="Green"` `SecondaryColor="Teal"`; add a persisted System/Light/Dark override
   to `AppSettings` + Settings UI; re-apply on change/startup and on OS theme change.
   *Verify:* app follows OS dark mode and honors the manual override.
2. **Roboto font.** Replace `.WithInterFont()` (`Application.cs:130`) and the
   `Avalonia.Fonts.Inter` package with a Roboto font collection.
   *Verify:* `FontFamily` resolves to Roboto at runtime.
3. **Edge-to-edge + system-bar theming.** Implement (confirm Avalonia 12 API first) so content
   draws behind bars and bar icons follow theme.
   *Verify:* no clipping under status/nav bars; bar icons correct in light/dark.
4. **`AutomationProperties` on icon-only controls.** Add `.Name`/`.HelpText` to FABs, icon
   buttons, and mark decorative chevrons.
   *Verify:* TalkBack announces a meaningful label for every FAB.
5. **Empty states on Ports + Messages.** Add `Classes="EmptyState"` blocks mirroring
   Objects/Bulletins/Packets.
   *Verify:* a fresh install shows a localized empty message instead of a blank list.
6. **House-style typography.** `.PageTitle`→20/Medium, `.SectionTitle`→16, remove
   `FontWeight="Bold"` crutches, unify `AddEditPortPage` header to `.PageTitle`.
   *Verify:* visual hierarchy matches the MD2 scale.
7. **Color hygiene.** Replace `PrimaryBadge` `White` with `MaterialPrimaryForegroundBrush`;
   replace `Opacity="0.7"` text with `MaterialSecondaryBodyBrush`; unify error brush.
   *Verify:* dark mode renders correctly; text meets AA contrast.
8. **Snackbar feedback path.** Wire `MainSnackbarHost` for transient/undo feedback (save
   success, port start/stop, download complete).
   *Verify:* a user action produces a snackbar.
9. **Splash screen.** Align splash background with the Green primary (or a dark-mode-aware
   value) and consider AndroidX SplashScreen.
   *Verify:* splash matches theme in both modes.
10. **Badge radius + confirmation-dialog touch targets.** `CornerRadius` 4→2, add
    `MinHeight=48` to the code-built dialog buttons.
    *Verify:* badges are 2dp; dialog buttons are ≥48dp.

---

## 8. Findings the auditor should re-verify before fixing

- **Predictive back (#2):** the `enableOnBackInvokedCallback` flag defaults to `true` at
  targetSdk 33+, so the code registration may already work; the manifest flag and the
  Android 14 home-animation are the only certain gaps. Confirm against Avalonia 12 before
  declaring it broken.
- **Edge-to-edge (#1) and font-scaling/motion-scale (#3/#5):** these may be partially or
  fully framework-limited; confirm the Avalonia 12 / Material.Avalonia 3.20.0 surface before
  implementing or dismissing each.
