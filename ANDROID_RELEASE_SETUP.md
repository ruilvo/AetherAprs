<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC-BY-SA-4.0
-->
# Android APK Release Setup Guide

This guide explains the manual steps required to enable automatic APK builds and releases on GitHub.

## Overview

The GitHub Actions workflow (`.github/workflows/release-apk.yml`) automatically:
- Triggers on any git tag starting with `v` (e.g., `v1.0.0`, `v1.2.3-beta.1`)
- Extracts version information from the tag
- Builds a signed APK with the correct version
- Creates a GitHub release with the APK attached

## Required Manual Setup

### 1. Generate Android Keystore

You need to create a keystore for signing your APK. Run this command locally:

```bash
keytool -genkey -v -keystore release.keystore -alias aetheraprs -keyalg RSA -keysize 2048 -validity 10000
```

You'll be prompted for:
- **Keystore password**: Choose a strong password (you'll need this later)
- **Key password**: Choose a strong password (can be the same or different)
- **Your name, organization, etc.**: Fill in as appropriate

**Important**: Keep `release.keystore` safe and NEVER commit it to git!

### 2. Convert Keystore to Base64

GitHub Actions needs the keystore as a base64-encoded string:

#### On Linux/macOS:
```bash
base64 -w 0 release.keystore > keystore.base64.txt
```

#### On Windows (PowerShell):
```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes("release.keystore")) | Out-File -FilePath keystore.base64.txt -Encoding ascii -NoNewline
```

This creates a file `keystore.base64.txt` with the encoded keystore.

### 3. Add GitHub Secrets

Go to your GitHub repository: **Settings → Secrets and variables → Actions → New repository secret**

Add these four secrets:

| Secret Name | Value | Description |
|-------------|-------|-------------|
| `ANDROID_KEYSTORE_BASE64` | Content of `keystore.base64.txt` | Base64-encoded keystore file |
| `ANDROID_KEY_ALIAS` | `aetheraprs` (or your chosen alias) | Keystore alias used during generation |
| `ANDROID_KEY_PASSWORD` | Your key password | Password for the key (from step 1) |
| `ANDROID_KEYSTORE_PASSWORD` | Your keystore password | Password for the keystore (from step 1) |

**Security Notes**:
- Never share these secrets or commit them to git
- If the keystore is compromised, you'll need to generate a new one and update all releases
- Store a backup of `release.keystore` in a secure location (password manager, encrypted backup, etc.)

### 4. Test the Workflow

Create and push a test tag:

```bash
git tag v0.1.0
git push origin v0.1.0
```

This will trigger the workflow. Check **Actions** tab in GitHub to monitor progress.

## Version Numbering Scheme

The workflow automatically converts git tags to Android version codes with smart pre-release type mapping:

| Git Tag | Display Version | Version Code | Pre-release Range | Notes |
|---------|-----------------|--------------|-------------------|-------|
| `v0.1.0-alfa.1` | 0.1.0-alfa.1 | 10001 | alfa: 1-33 | Pre-release |
| `v0.1.0-alfa.2` | 0.1.0-alfa.2 | 10002 | alfa: 1-33 | Pre-release |
| `v0.1.0-beta.1` | 0.1.0-beta.1 | 10034 | beta: 34-66 | Pre-release |
| `v0.1.0-beta.2` | 0.1.0-beta.2 | 10035 | beta: 34-66 | Pre-release |
| `v0.1.0-rc.1` | 0.1.0-rc.1 | 10067 | rc: 67-98 | Pre-release |
| `v0.1.0-rc.2` | 0.1.0-rc.2 | 10068 | rc: 67-98 | Pre-release |
| `v0.1.0` | 0.1.0 | 10099 | final: 99 | **Release** |
| `v1.0.0` | 1.0.0 | 1000099 | final: 99 | **Release** |
| `v1.2.3` | 1.2.3 | 1020399 | final: 99 | **Release** |

**Version Code Formula**: `MAJOR * 1000000 + MINOR * 10000 + PATCH * 100 + PRERELEASE_NUM`

**Pre-release Type Mapping**:
- `alfa`/`alpha`: 1-33 (e.g., `alfa.1` → 1, `alfa.5` → 5)
- `beta`: 34-66 (e.g., `beta.1` → 34, `beta.5` → 38)
- `rc`: 67-98 (e.g., `rc.1` → 67, `rc.5` → 71)
- **Final releases**: 99 (no suffix)
- Unknown types: Use raw number 1-98 (for custom pre-release types)

**Natural Update Path**:
```
v0.1.0-alfa.1 (10001)
  ↓ update
v0.1.0-alfa.2 (10002)
  ↓ update
v0.1.0-beta.1 (10034)
  ↓ update
v0.1.0-rc.1 (10067)
  ↓ update
v0.1.0 (10099) ← Final release, highest version code
```

**Important**: 
- Version codes must always increase for Google Play Store updates
- Each component can be 0-99 (MAJOR, MINOR, PATCH)
- Pre-release numbers: alfa (1-33), beta (1-33), rc (1-32)
- Final releases automatically get 99, making them higher than all pre-releases
- Maximum version code is 2147483647 (allows MAJOR up to 2147)
- Pre-release tags (containing `-`) are marked as pre-release on GitHub
- This scheme ensures final releases always update over their pre-releases

## Creating Releases

### For Regular Releases

```bash
git tag v1.0.0
git push origin v1.0.0
```

### For Pre-releases (Beta, Alpha, RC)

```bash
# Alfa releases
git tag v0.1.0-alfa.1
git push origin v0.1.0-alfa.1

git tag v0.1.0-alfa.2
git push origin v0.1.0-alfa.2

# Beta releases
git tag v1.0.0-beta.1
git push origin v1.0.0-beta.1
```

Pre-releases are automatically marked as such on GitHub. Each incremental pre-release number will have a higher version code, allowing Android to recognize it as an update.

## Troubleshooting

### Workflow fails with "No .NET SDK found"

The workflow uses .NET 10. If it's not available on GitHub Actions yet, you may need to update the `dotnet-version` in the workflow or use a self-hosted runner.

### Keystore authentication fails

Double-check that:
- The base64 encoding was done correctly (no line breaks)
- All four secrets are set correctly in GitHub
- The alias matches exactly (case-sensitive)
- Passwords are correct

### APK not found after build

Check the build logs in GitHub Actions. The APK path may have changed. Adjust the `find` command in the "Find APK" step if needed.

### Version code conflicts

If you delete and recreate a tag, use a higher version number. Android won't install an APK with the same or lower version code than what's already installed.

## Cleanup

After setting up the secrets, you can safely delete these local files:
- `keystore.base64.txt`

**Do NOT delete your backup of `release.keystore`** - you'll need it if you ever need to update the GitHub secrets or sign APKs manually.

## Next Steps

Once setup is complete:
1. Create your first release tag
2. Monitor the Actions tab to ensure the workflow succeeds
3. Download the APK from the Releases page to verify it works
4. Consider setting up Google Play Store publishing in the future (requires additional configuration)
