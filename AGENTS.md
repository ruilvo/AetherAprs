<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC0-1.0
-->
# AGENTS

This repository hosts AetherAprs, an Avalonia-based APRS client with shared UI logic plus platform-specific desktop and Android heads.

## Quick context
- UI framework: Avalonia 12 with compiled bindings enabled by default.
- MVVM: CommunityToolkit.Mvvm.
- Styling: Material.Avalonia + Material.Icons.Avalonia.
- Config: appsettings.json (+ appsettings.Development.json in Debug).
- Licenses: REUSE-compliant; SPDX headers are required on all files; resources need a matching .license file.
- License types: source code GPL-3.0-or-later; resources CC-BY-CA-4.0; documentation and repo config CC0-1.0.

## Repo layout
- AetherAprs/ : shared UI, view models, services, assets.
- AetherAprs/Services/ : configuration, logging, map, and navigation services.
- AetherAprs.Desktop/ : desktop entry point.
- AetherAprs.Android/ : Android entry point.
- Directory.Packages.props : central NuGet versions.

## Build and run
- Build all: dotnet build AetherAprs.slnx
- Desktop run: dotnet run --project AetherAprs.Desktop
- Android build: dotnet build AetherAprs.Android

If a command fails due to SDK or workload, check local .NET 10 SDK and Android workloads.

## Guidance for changes
- Keep SPDX headers intact and add them to new files (all file types).
- Prefer shared code in AetherAprs/; only place platform-specific code in platform projects.
- Keep NuGet versions in Directory.Packages.props; do not add inline versions.
- When adding assets, include a matching .license file as done in Assets/.
- Maintain compiled bindings in Avalonia XAML; ensure view models expose typed properties.

## Where to look
- App styles and themes: AetherAprs/App.axaml
- View locator: AetherAprs/ViewLocator.cs
- App settings models/services: AetherAprs/Services/Configuration
- Navigation services: AetherAprs/Services/Navigation

## Testing
No tests are currently defined.
