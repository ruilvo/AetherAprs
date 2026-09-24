<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC-BY-SA-4.0
-->
# Troubleshooting APK Build Failures

## Common Issues and Solutions

### Java Exit Code 2 Error

**Error message:**
```
error MSB6006: "java" exited with code 2.
```

This error typically occurs during APK signing. Common causes:

#### 1. Incorrect Keystore Password or Alias

**Solution:** Verify your GitHub Secrets are correct:
- `ANDROID_KEYSTORE_PASSWORD` - Must match the password used when creating the keystore
- `ANDROID_KEY_PASSWORD` - Must match the key password (can be same as keystore password)
- `ANDROID_KEY_ALIAS` - Must exactly match the alias used (default: `aetheraprs`)

**Test locally:**
```bash
keytool -list -v -keystore release.keystore -alias aetheraprs
```

If the password is wrong, you'll get an error. If the alias is wrong, it won't be listed.

#### 2. Corrupted Keystore Base64 Encoding

**Solution:** Re-encode the keystore:

Linux/macOS:
```bash
base64 -w 0 release.keystore > keystore.base64.txt
```

Windows PowerShell:
```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes("release.keystore")) | Out-File -FilePath keystore.base64.txt -Encoding ascii -NoNewline
```

Important: Ensure there are NO line breaks in the base64 string. Update the `ANDROID_KEYSTORE_BASE64` secret with the entire content of `keystore.base64.txt`.

#### 3. Keystore Algorithm Incompatibility

**Solution:** Ensure keystore was created with RSA:
```bash
keytool -genkey -v -keystore release.keystore -alias aetheraprs -keyalg RSA -keysize 2048 -validity 10000
```

If you used a different algorithm, regenerate the keystore.

#### 4. Java Version Issues

The GitHub Actions runner uses Java 11+ by default. If your keystore was created with a newer Java version with different defaults, there might be compatibility issues.

**Solution:** Regenerate keystore with explicit parameters:
```bash
keytool -genkey -v -keystore release.keystore \
  -alias aetheraprs \
  -keyalg RSA \
  -keysize 2048 \
  -validity 10000 \
  -storetype JKS
```

### Node.js Deprecation Warnings

**Warning message:**
```
Node.js 20 is deprecated. The following actions target Node.js 20...
```

**Status:** Fixed in workflow by updating to latest action versions:
- `actions/checkout@v4.2.2`
- `actions/setup-dotnet@v4.1.0`
- `softprops/action-gh-release@v2`

### Compiler Warnings in Release Build

**Warning messages:**
```
warning CS0414: The field '..._appSettingsDevelopmentFileName' is assigned but its value is never used
```

**Status:** Fixed by wrapping declarations in `#if DEBUG` conditional compilation blocks.

## Debugging APK Build Issues

### Enable Verbose Logging

The workflow already includes `--verbosity normal`. To debug locally:

```bash
dotnet publish AetherAprs.Android/AetherAprs.Android.csproj \
  -c Release \
  -f net10.0-android \
  -p:ApplicationVersion=10001 \
  -p:ApplicationDisplayVersion=0.1.0-alfa.1 \
  -p:AndroidKeyStore=true \
  -p:AndroidSigningKeyStore=/path/to/release.keystore \
  -p:AndroidSigningKeyAlias=aetheraprs \
  -p:AndroidSigningKeyPass=yourpassword \
  -p:AndroidSigningStorePass=yourpassword \
  --verbosity diagnostic
```

### Verify Keystore Information

```bash
# List keystore contents
keytool -list -v -keystore release.keystore

# Check certificate
keytool -list -v -keystore release.keystore -alias aetheraprs

# Export certificate (for verification)
keytool -exportcert -keystore release.keystore -alias aetheraprs -file cert.der
```

### Test Signing Locally

Build an unsigned APK first:
```bash
dotnet publish AetherAprs.Android/AetherAprs.Android.csproj \
  -c Release \
  -f net10.0-android \
  -p:ApplicationVersion=10001 \
  -p:ApplicationDisplayVersion=0.1.0-alfa.1
```

Then manually sign it:
```bash
jarsigner -verbose -sigalg SHA256withRSA -digestalg SHA-256 \
  -keystore release.keystore \
  path/to/your.apk aetheraprs
```

If manual signing works but the workflow fails, the issue is likely with how secrets are being passed.

## Getting Help

If you continue to have issues:

1. Check the full build log in GitHub Actions
2. Look for the exact error message before "java exited with code 2"
3. Verify all four secrets are set correctly in GitHub
4. Test signing locally with the same keystore
5. Create a new keystore and update all secrets

## Prevention

- **Back up your keystore!** Store it in multiple secure locations
- Document the passwords in a secure password manager
- Test the release workflow with a test tag before creating official releases
- Keep the keystore and passwords secure - they can't be recovered if lost
