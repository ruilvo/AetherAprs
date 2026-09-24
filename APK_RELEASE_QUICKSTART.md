<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC-BY-SA-4.0
-->
# Quick Start: APK Release Setup

## What Was Done Automatically

✅ Created `.github/workflows/release-apk.yml` - GitHub Actions workflow that:
- Triggers on tags starting with `v` (e.g., `v1.0.0`, `v0.1.0-alfa.1`)
- Extracts version from tag and sets Android version code
- Includes pre-release numbers in version code (so `alfa.1` < `alfa.2` < final release)
- Builds signed APK with correct versioning
- Creates GitHub release with APK attached
- Marks pre-releases (tags with `-`) appropriately

✅ Created `ANDROID_RELEASE_SETUP.md` - Detailed setup guide

## What You Must Do Manually

### 1. Generate Keystore (One-time setup)

```bash
keytool -genkey -v -keystore release.keystore -alias aetheraprs -keyalg RSA -keysize 2048 -validity 10000
```

Choose strong passwords for both keystore and key. **Keep `release.keystore` safe!**

### 2. Convert to Base64

**Linux/macOS:**
```bash
base64 -w 0 release.keystore > keystore.base64.txt
```

**Windows PowerShell:**
```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes("release.keystore")) | Out-File -FilePath keystore.base64.txt -Encoding ascii -NoNewline
```

### 3. Add GitHub Secrets

Go to: **GitHub Repository → Settings → Secrets and variables → Actions**

Add these 4 secrets:

| Secret Name | Value | Notes |
|-------------|-------|-------|
| `ANDROID_KEYSTORE_BASE64` | Content of `keystore.base64.txt` | The entire base64 string |
| `ANDROID_KEY_ALIAS` | `aetheraprs` | **Must exactly match the alias from step 1** |
| `ANDROID_KEY_PASSWORD` | Your key password | Password entered when creating keystore |
| `ANDROID_KEYSTORE_PASSWORD` | Your keystore password | Password entered when creating keystore |

**Critical:** The `ANDROID_KEY_ALIAS` value must exactly match the `-alias` parameter you used when creating the keystore. If you used a different alias, put that value here instead of `aetheraprs`.

### 4. Create a Release

```bash
# Pre-release alfa versions
git tag v0.1.0-alfa.1
git push origin v0.1.0-alfa.1

git tag v0.1.0-alfa.2
git push origin v0.1.0-alfa.2

# Or a regular release
git tag v1.0.0
git push origin v1.0.0
```

The workflow will automatically:
- Build the APK
- Sign it with your keystore
- Create a GitHub release
- Attach the APK to the release

## Version Numbering

- `v0.1.0-alfa.1` → Version code: 10001 (alfa range: 1-33)
- `v0.1.0-alfa.2` → Version code: 10002 (updates over alfa.1)
- `v0.1.0-beta.1` → Version code: 10034 (beta range: 34-66, updates over alfa.x)
- `v0.1.0-rc.1` → Version code: 10067 (rc range: 67-98, updates over beta.x)
- `v0.1.0` → Version code: 10099 (final release: 99, updates over all pre-releases!)
- `v1.2.3` → Version code: 1020399 (final release)

**Formula**: `MAJOR * 1000000 + MINOR * 10000 + PATCH * 100 + PRERELEASE_NUM`

**Pre-release Mapping**:
- `alfa`/`alpha`: 1-33 (e.g., `alfa.5` → 5)
- `beta`: 34-66 (e.g., `beta.5` → 38)
- `rc`: 67-98 (e.g., `rc.5` → 71)
- **Final releases**: 99 (no suffix)

**Natural progression**: `alfa.1` → `alfa.2` → `beta.1` → `rc.1` → `v0.1.0` (final)

Final releases automatically get version code 99, ensuring they always update over pre-releases.

## Important Notes

- **Backup your keystore!** You can't publish updates without it.
- **Never commit keystore to git!** (It's in `.gitignore`)
- Version codes must always increase for updates
- Pre-release tags (with `-`) are marked as pre-release on GitHub
- Pre-release types are mapped to ranges: alfa (1-33), beta (34-66), rc (67-98), final (99)
- Each alfa/beta can have up to 33 versions, rc up to 32 versions
- Final releases automatically get 99, ensuring they update over all pre-releases

See `ANDROID_RELEASE_SETUP.md` for detailed instructions and troubleshooting.
