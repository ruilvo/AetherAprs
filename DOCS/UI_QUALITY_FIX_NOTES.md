<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC-BY-SA-4.0
-->
# UI quality platform verification

The November 2026 audit predates the existing persisted System/Light/Dark setting,
`BaseTheme="Inherit"` and Green/Teal palette. This pass kept those settings, made
Android system-bar icon contrast follow the actual theme, and enabled Avalonia
12.1.2's `IInsetsManager.DisplayEdgeToEdgePreference`. Avalonia automatically applies
safe-area padding to the root view. The AndroidX SplashScreen theme now uses the
same Green primary in either system mode, and hands off to the app's DayNight theme.
The reported Inter font registration is also absent in this checkout: Material.Avalonia
3.20.0 provides its own embedded Roboto via `MaterialDesignFonts`. No extra font
package or local font override is needed.

At the root Home tab, the app leaves Android's back dispatcher free to perform
predictive back-to-home. Other tabs and overlay pages use the existing navigation
callback; interactive in-app back gesture progress is not implemented. The
Avalonia navigation service does not expose an animation progress integration for
Android's predictive-back gesture.

Avalonia 12.1.2's installed APIs expose neither Android system font scaling nor
the platform's reduced-motion/animation scale setting. The existing layouts do not
force a font scale; map label fonts are still fixed-size Mapsui labels. Do not claim
system font or animation scaling support until the framework exposes and tests it.
On the Android 16 emulator, changing `font_scale` from 1.0 to 1.5 left the
Ports title at `[42,178][1038,240]` and its empty state at
`[315,389][765,439]`; the OS font preference does not currently scale them.

## On-device checks

- Cold start in light and dark system modes: verify AndroidX splash palette and
  icon, handoff, and system-bar icon contrast. Repeat after changing the Settings
  theme to System, Light, and Dark while the app remains open.
- Portrait and landscape, including a device with display cutouts: check the
  top/bottom tabs, map buttons, dialogs, keyboard, and safe-area padding.
- TalkBack: check Home/Ports/Messages FABs, port actions, back controls, and
  decorative Settings chevrons. Check large font size for clipping.
- Gesture back: from Home to the launcher, from another tab to Home, and from an
  overlay back to its parent. Verify the home animation on Android 14+.

## Emulator results (Android 16, API 36)

The `pixel_7_-_api_36_0` AVD ran the app through the SDK's Fast Deployment
install. Direct `adb install` of a debug APK without SDK deployment first
failed because it omitted the managed assemblies. A fully embedded debug APK
was rejected with `INSTALL_FAILED_INSUFFICIENT_STORAGE` on this 5.8 GB AVD;
incremental deployment plus updating the changed managed assembly allowed
testing without altering the AVD's other application data.

ADB's UI hierarchy showed the localized Ports and Messages empty states and
their named action buttons. Home's zoom/center FABs have accessible names.
After adding a port, the first implementation still displayed the empty-state
message; binding visibility to observable `HasPorts`/`HasConversations` properties
fixed collection updates. A repeated emulator check showed the new port card
instead of the empty state, with the localized "Port saved." snackbar visible.
In portrait, the page starts below the status bar at y=136 and the bottom
tabs end above the navigation bar at y=2337. In landscape, the content starts
after the cutout inset at x=136 and the tabs end above the navigation bar
at y=1017. Both system-theme transitions were observed without restarting
the app: the Settings surface changed from RGB (48,48,48) in dark mode to
(250,250,250) in light mode, and the Android window's
`LIGHT_STATUS_BARS LIGHT_NAVIGATION_BARS` flags appeared only in light mode.
Gesture back from Settings returned to Home; gesture back from Home returned
to the launcher. The emulator's `font_scale`, night mode, and orientation
were restored to their defaults after testing.

TalkBack speech, the transient splash frame, snackbar display, interactive
back animation progress, and animation-scale behavior still require visual
or assistive-technology verification. On initial permission setup, Android 16
rejected an attempted foreground-service start; the service now stops itself
when Android disallows foreground promotion, so the app can continue loading.
