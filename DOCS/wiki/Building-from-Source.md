<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC-BY-SA-4.0
-->

# Building from source

## Requirements

- .NET 10 SDK
- Git
- `pre-commit` (for the license check and local git hook)
- .NET Android workload and Android SDK for Android builds
- Java tooling supplied by the Android workload

Install the Android workload if needed:

```bash
dotnet workload install android
```

## Clone and validate

```bash
git clone https://github.com/RuiOliveira/AetherAprs.git
cd AetherAprs
pre-commit install
dotnet build AetherAprs.slnx
dotnet test AetherAprs.Tests/AetherAprs.Tests.csproj
```

The test project uses xUnit v3 and NSubstitute.

## Build Android

```bash
dotnet build AetherAprs.Android/AetherAprs.Android.csproj -c Debug
dotnet build AetherAprs.Android/AetherAprs.Android.csproj -c Release
```

The APK is written under
`AetherAprs.Android/bin/<Configuration>/net10.0-android/`.

For an emulator or device, Visual Studio can deploy the Debug build. For manual APK
installation use a self-contained Release build or the SDK install target:

```bash
dotnet build AetherAprs.Android/AetherAprs.Android.csproj -t:Install
```

Fast Deployment Debug APKs need the SDK deployment step because their managed assemblies
are pushed separately. A raw `adb install` of such an APK is incomplete.

## Signing

Keep release keystores and passwords outside the repository. Supply signing properties
to MSBuild or configure them in a local, ignored settings file:

```bash
dotnet build AetherAprs.Android/AetherAprs.Android.csproj -c Release \
  /p:AndroidSigningKeyStore=release.keystore \
  /p:AndroidSigningKeyAlias=aetheraprs \
  /p:AndroidSigningKeyPass=<key-password> \
  /p:AndroidSigningStorePass=<store-password>
```

Never commit a keystore or password.

## Repository conventions

Read [AGENTS.md](../../AGENTS.md) before changing code. It defines architecture boundaries,
DI registration, localization, naming, test placement, and SPDX requirements.

The [Developer Guide](Developer-Guide.md) explains the receive, query, and transmit flows.
