<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC-BY-SA-4.0
-->
# AGENTS.md

Quick-reference instructions for AI agents working in the AetherAprs codebase.

## Project Overview

AetherAprs is a cross-platform ham radio APRS application built with Avalonia UI and .NET 10. Two projects:
- `AetherAprs/` - Core library (net10.0), contains ViewModels, Views, Services
- `AetherAprs.Android/` - Android app (net10.0-android, API 23+), references core project

## Build Commands

```powershell
# Build solution
dotnet build AetherAprs.slnx

# Build specific project
dotnet build AetherAprs/AetherAprs.csproj
dotnet build AetherAprs.Android/AetherAprs.Android.csproj

# Run desktop (if supported on platform)
dotnet run --project AetherAprs/AetherAprs.csproj
```

No test framework currently exists in this repo.

## Critical Requirements

### REUSE License Compliance

Every file MUST have an SPDX license header. Pre-commit hook enforces this via `reuse-lint-file`.

Code files (C#):
```csharp
// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
```

XML files (.csproj, .axaml, .slnx):
```xml
<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: GPL-3.0-or-later
-->
```

YAML/config files:
```yaml
# This file is part of AetherAprs
# SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
# SPDX-License-Identifier: CC0-1.0
```

Verify compliance: `pre-commit run reuse-lint-file --all-files`

### ImplicitUsings Disabled

All projects have `<ImplicitUsings>disable</ImplicitUsings>`. You MUST include explicit using statements for everything, including `System`, `System.Collections.Generic`, etc. Do not assume any default usings.

### Central Package Management

Package versions are centralized in `Directory.Packages.props`. When adding a new package:

1. Add version to `Directory.Packages.props`:
   ```xml
   <PackageVersion Include="PackageName" Version="x.y.z" />
   ```

2. Reference without version in `.csproj`:
   ```xml
   <PackageReference Include="PackageName" />
   ```

Do NOT specify versions in individual project files.

## Architecture Notes

**Dependency Injection**: `ServiceProviderFactory.CreateServiceProvider()` in `App.axaml.cs:OnFrameworkInitializationCompleted()` builds the DI container. Platform-specific services registered via `ConfigurePlatformServices()` override (Android app provides its own `IAppDataDirProviderService`).

**MVVM**: Uses CommunityToolkit.Mvvm. ViewModels resolved from DI container and assigned to DataContext.

**Design-time data**: `DesignData.cs` provides design-time ViewModels for XAML previews. When creating new ViewModels:
1. Register in `ServiceProviderFactory.cs` (runtime DI)
2. Register in `DesignData.cs` (design-time DI)
3. Add public property to expose the ViewModel instance

Without design-time registration, XAML previews will fail.

**Configuration**: Uses Microsoft.Extensions.Configuration with `appsettings.json` and `appsettings.Development.json`. Files copied to output directory. Android project links these from core project via `<AndroidAsset Include="..\AetherAprs\appsettings.json">`. Development config only included in Android Debug builds.

**Multi-platform lifecycle**: App.axaml.cs handles three Avalonia lifetime types:
- `IClassicDesktopStyleApplicationLifetime` - Desktop (Windows, macOS, Linux)
- `IActivityApplicationLifetime` - Android/iOS with factory pattern
- `ISingleViewApplicationLifetime` - Browser/single-view platforms

## Common Mistakes to Avoid

- Creating files without SPDX headers - pre-commit will reject
- Adding `using System;` style imports and assuming implicit usings - they're disabled
- Putting package versions in .csproj instead of Directory.Packages.props
- Assuming standard .NET namespaces are available without explicit using statements
- Adding Android-specific code to core project instead of Android project
- Creating new ViewModels without registering them in both `ServiceProviderFactory.cs` AND `DesignData.cs`
