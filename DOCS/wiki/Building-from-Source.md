<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC-BY-SA-4.0
-->

# Building from Source

This guide covers building AetherAprs from source code for development or creating custom builds.

## Table of Contents
- [Prerequisites](#prerequisites)
- [Clone the Repository](#clone-the-repository)
- [Building for Android](#building-for-android)
- [Building for Desktop](#building-for-desktop-experimental)
- [Running Tests](#running-tests)
- [Development Setup](#development-setup)
- [Troubleshooting](#troubleshooting)

## Prerequisites

### Required Software

1. **.NET 10 SDK**
   - Download: [https://dotnet.microsoft.com/download](https://dotnet.microsoft.com/download)
   - Verify installation: `dotnet --version` (should show 10.x.x)

2. **Git**
   - Download: [https://git-scm.com/](https://git-scm.com/)
   - Verify installation: `git --version`

3. **Android Workload** (for Android builds)
   ```bash
   dotnet workload install android
   ```

4. **Android SDK** (for Android builds)
   - Installed automatically with Android workload, or
   - Install Android Studio and configure SDK path

### Optional Software

- **Visual Studio 2022** (Windows) with .NET and Android workloads
- **Visual Studio Code** with C# Dev Kit extension
- **JetBrains Rider**
- **Android Studio** (for Android development tools)

### System Requirements

**For Android Development:**
- Windows 10/11, macOS 10.15+, or Linux (Ubuntu 20.04+)
- 8 GB RAM minimum (16 GB recommended)
- 20 GB free disk space
- Java Development Kit (JDK) 11 or later (installed with Android workload)

## Clone the Repository

```bash
git clone https://github.com/RuiOliveira/AetherAprs.git
cd AetherAprs
```

### Install Git Pre-commit Hooks

AetherAprs uses pre-commit hooks to enforce REUSE compliance (SPDX headers).

```bash
# Install pre-commit (Python required)
pip install pre-commit

# Install hooks
pre-commit install
```

**Note**: All files must have proper SPDX license headers. The pre-commit hook will reject commits without them.

## Building for Android

### Debug Build

Build a debug APK for testing:

```bash
dotnet build AetherAprs.Android/AetherAprs.Android.csproj -c Debug
```

**Output location:**
```
AetherAprs.Android/bin/Debug/net10.0-android/com.aetheraprs-Signed.apk
```

### Release Build (Unsigned)

Build an unsigned release APK:

```bash
dotnet build AetherAprs.Android/AetherAprs.Android.csproj -c Release
```

⚠️ **Unsigned APKs cannot be installed on most devices.** See [Signing APKs](#signing-apks) below.

### Signing APKs

To create an installable release APK, you need to sign it with a keystore.

#### Step 1: Create a Keystore

```bash
keytool -genkeypair -v -keystore release.keystore -alias aetheraprs -keyalg RSA -keysize 2048 -validity 10000 -storetype PKCS12
```

You'll be prompted for:
- **Keystore password**: Choose a strong password
- **Key password**: Choose a strong password (or press Enter to use keystore password)
- **Name, organization, etc.**: Fill in as appropriate

**Important**: Store `release.keystore` and passwords securely. Never commit the keystore to git!

#### Step 2: Sign the APK

Create a file `AetherAprs.Android/appsettings.local.json` (gitignored):

```json
{
  "AndroidSigningKeyStore": "path/to/release.keystore",
  "AndroidSigningKeyAlias": "aetheraprs",
  "AndroidSigningKeyPass": "your-key-password",
  "AndroidSigningStorePass": "your-keystore-password"
}
```

Or use MSBuild properties:

```bash
dotnet build AetherAprs.Android/AetherAprs.Android.csproj -c Release \
  /p:AndroidSigningKeyStore=release.keystore \
  /p:AndroidSigningKeyAlias=aetheraprs \
  /p:AndroidSigningKeyPass=your-key-password \
  /p:AndroidSigningStorePass=your-keystore-password
```

**Output location:**
```
AetherAprs.Android/bin/Release/net10.0-android/com.aetheraprs-Signed.apk
```

### Installing on Device

#### Via ADB (Android Debug Bridge)

```bash
# Install debug APK
adb install AetherAprs.Android/bin/Debug/net10.0-android/com.aetheraprs-Signed.apk

# Install release APK (overwrites debug)
adb install -r AetherAprs.Android/bin/Release/net10.0-android/com.aetheraprs-Signed.apk
```

#### Via File Transfer

1. Copy APK to your Android device
2. Enable "Install from Unknown Sources" in Android settings
3. Open the APK file with a file manager
4. Tap "Install"

### Running on Emulator

```bash
# List available emulators
dotnet build AetherAprs.Android/AetherAprs.Android.csproj -t:QueryAndroidAvds

# Run on emulator (requires emulator running)
dotnet build AetherAprs.Android/AetherAprs.Android.csproj -c Debug -t:Run
```

## Building for Desktop (Experimental)

⚠️ **Desktop builds are not officially supported.** They may not work correctly.

### Build Desktop Project

```bash
dotnet build AetherAprs/AetherAprs.csproj -c Release
```

### Run Desktop App

```bash
dotnet run --project AetherAprs/AetherAprs.csproj
```

**Known Issues:**
- Bluetooth TNCs not supported on desktop
- Location services use stub implementations
- Some Android-specific features missing
- Not tested or maintained

## Running Tests

AetherAprs uses xUnit v3 for testing.

### Run All Tests

```bash
dotnet test AetherAprs.Tests/AetherAprs.Tests.csproj
```

### Run Tests with Detailed Output

```bash
dotnet test AetherAprs.Tests/AetherAprs.Tests.csproj -v detailed
```

### Run Specific Test Class

```bash
dotnet test AetherAprs.Tests/AetherAprs.Tests.csproj --filter "FullyQualifiedName~KissSerializerTests"
```

### Test Coverage

```bash
dotnet test AetherAprs.Tests/AetherAprs.Tests.csproj /p:CollectCoverage=true
```

## Development Setup

### Recommended IDE Setup

#### Visual Studio 2022 (Windows)

1. Install workloads:
   - .NET desktop development
   - Mobile development with .NET
2. Open `AetherAprs.slnx`
3. Set `AetherAprs.Android` as startup project

#### JetBrains Rider

1. Install Rider with Android support
2. Open `AetherAprs.slnx`
3. Configure Android SDK path in Settings → Android

#### Visual Studio Code

1. Install extensions:
   - C# Dev Kit
   - .NET MAUI (for Android debugging)
2. Open project folder
3. Use terminal commands for building

### Project Structure

```
AetherAprs/
├── AetherAprs/                    # Core library (net10.0)
│   ├── Configuration/             # Settings and configuration
│   ├── Database/                  # EF Core models and context
│   ├── Localization/              # Strings.resx files
│   ├── Modems/                    # KISS TNC, APRS-IS implementations
│   ├── Models/                    # Domain models
│   ├── Services/                  # Business logic services
│   ├── ViewModels/                # MVVM ViewModels
│   └── Views/                     # Avalonia AXAML views
├── AetherAprs.Android/            # Android app (net10.0-android)
│   ├── MainActivity.cs            # Android entry point
│   ├── Services/                  # Android-specific services
│   └── Resources/                 # Android resources
├── AetherAprs.Tests/              # Test project (net10.0)
│   └── (mirrors AetherAprs structure)
├── docs/                          # Documentation
│   └── wiki/                      # Wiki markdown files
├── .github/workflows/             # GitHub Actions
├── Directory.Packages.props       # Centralized package versions
├── AetherAprs.slnx               # Solution file
└── AGENTS.md                      # Developer guidelines
```

### Key Files

- **`App.axaml.cs`**: Application entry point, DI setup
- **`ServiceProviderFactory.cs`**: Dependency injection configuration
- **`ViewLocator.cs`**: ViewModel-to-View mapping
- **`AppDbContext.cs`**: Database context
- **`appsettings.json`**: Application configuration

### Code Style

AetherAprs follows strict coding conventions (see `AGENTS.md`):

- **Fields**: `_camelCase` with underscore prefix
- **Properties/Methods**: `PascalCase`
- **Parameters/Locals**: `camelCase`
- **Explicit usings**: `ImplicitUsings` is disabled
- **Localization**: All user-facing text in `Strings.resx`
- **SPDX headers**: Required on all files

### Hot Reload

For rapid development, use hot reload:

```bash
# Desktop (if supported)
dotnet watch run --project AetherAprs/AetherAprs.csproj

# Android (requires device/emulator)
dotnet watch run --project AetherAprs.Android/AetherAprs.Android.csproj
```

Changes to AXAML and some C# code will reload without full rebuild.

## Troubleshooting

### Build Errors

#### "No .NET 10 SDK found"

**Solution**: Install .NET 10 SDK from [dotnet.microsoft.com](https://dotnet.microsoft.com/download)

#### "Android workload not installed"

**Solution**: 
```bash
dotnet workload install android
```

#### "SPDX header missing"

**Solution**: All files need SPDX license headers. Add:

```csharp
// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
```

#### "Package version must be centrally managed"

**Solution**: Add package version to `Directory.Packages.props`, not individual `.csproj` files.

### Runtime Errors

#### "Database error on startup"

**Solution**: Delete app data and restart:
```bash
# Android
adb shell pm clear com.aetheraprs
```

#### "Location permission denied"

**Solution**: Grant location permission in Android settings:
Settings → Apps → AetherAprs → Permissions → Location → Allow

#### "Bluetooth not working"

**Solution**: 
1. Grant Bluetooth permissions
2. Ensure Bluetooth is enabled on device
3. Pair device in Android Bluetooth settings first

### Test Failures

#### "AppDbContext tests failing"

**Known issue**: Some tests fail due to EF Core mocking limitations. These are pre-existing and don't affect functionality.

#### "xUnit version mismatch"

**Solution**: Ensure using xUnit v3 (`xunit.v3` package, not `xunit`).

## Next Steps

- **[Developer Guide](Developer-Guide.md)**: Architecture and contribution guidelines
- **[Android Release Setup](Android-Release-Setup.md)**: Configure GitHub Actions for automated releases
- **Contributing**: See `AGENTS.md` for detailed guidelines

---

**Need help?** Open an issue on [GitHub Issues](https://github.com/RuiOliveira/AetherAprs/issues).
