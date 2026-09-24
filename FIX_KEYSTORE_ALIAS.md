<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC-BY-SA-4.0
-->
# FIX REQUIRED: Keystore Alias Mismatch

## Problem

Your GitHub Actions workflow is failing because the `ANDROID_KEY_ALIAS` secret does not match the alias in your keystore.

**From the build log:**
```
Alias name: aetheraprs
✗ Alias '***' NOT found in keystore
```

Your keystore contains the alias `aetheraprs`, but your GitHub secret `ANDROID_KEY_ALIAS` has a different value.

## Solution

Update your GitHub secret to match the keystore:

1. Go to: **GitHub Repository → Settings → Secrets and variables → Actions**

2. Find the secret named `ANDROID_KEY_ALIAS`

3. Click **Update** (or delete and recreate it)

4. Set the value to: `aetheraprs` (exactly as shown, case-sensitive)

5. Save the secret

6. Re-run the failed workflow or create a new tag

## Verification

After updating the secret, the workflow will show:
```
✓ Alias 'aetheraprs' found in keystore
```

And the build should proceed successfully.

## Prevention

When creating a keystore, remember the alias you use:
```bash
keytool -genkeypair -v -keystore release.keystore \
  -alias aetheraprs \  # ← This is what goes in ANDROID_KEY_ALIAS
  -keyalg RSA -keysize 2048 -validity 10000 -storetype PKCS12
```

The workflow now includes automatic verification that will catch this error early and show you the correct alias to use.
