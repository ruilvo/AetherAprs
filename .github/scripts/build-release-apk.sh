#!/bin/bash
# This file is part of AetherAprs
# SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
# SPDX-License-Identifier: GPL-3.0-or-later

# Publishes the release APK with the version derived from a git tag, so neither CI nor
# a local release build can fall back to the placeholder version in the project file.
# Signing material is read from the environment (see .github/workflows/release-apk.yml);
# when it is absent the package is signed with the development key.
#
# Usage: build-release-apk.sh            # version from `git describe --tags --abbrev=0`
#        TAG=v1.2.3 build-release-apk.sh # version from an explicit tag
#        build-release-apk.sh --dry-run  # print the publish command instead of running it

set -euo pipefail

DRY_RUN=false
if [ "${1:-}" = "--dry-run" ]; then
  DRY_RUN=true
fi

REPO_ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
cd "$REPO_ROOT"

TAG=${TAG:-$(git describe --tags --abbrev=0)}

VERSION_OUTPUT=$("$REPO_ROOT/.github/scripts/extract-version.sh" "$TAG")
VERSION_CODE=$(printf '%s\n' "$VERSION_OUTPUT" | sed -n 's/^version_code=//p')
VERSION_NAME=$(printf '%s\n' "$VERSION_OUTPUT" | sed -n 's/^version_name=//p')

if [ -z "$VERSION_CODE" ] || [ -z "$VERSION_NAME" ]; then
  echo "Error: Could not derive a version from tag '$TAG'." >&2
  exit 1
fi

EXTRA_ARGS=()
if [ -n "${ANDROID_BUILD_TOOLS_VERSION:-}" ]; then
  EXTRA_ARGS+=(-p:AndroidSdkBuildToolsVersion="$ANDROID_BUILD_TOOLS_VERSION")
fi
if [ -n "${AndroidSigningKeyStore:-}" ]; then
  EXTRA_ARGS+=(-p:AndroidKeyStore=true)
fi

echo "Building $VERSION_NAME (version code $VERSION_CODE)" >&2

PUBLISH=(
  dotnet publish AetherAprs.Android/AetherAprs.Android.csproj
  -c Release
  -f net11.0-android
  -r android-arm64
  "-p:ApplicationVersion=$VERSION_CODE"
  "-p:ApplicationDisplayVersion=$VERSION_NAME"
  -p:AndroidPackageFormat=apk
)
PUBLISH+=(${EXTRA_ARGS[@]+"${EXTRA_ARGS[@]}"})

if [ "$DRY_RUN" = true ]; then
  printf '%s ' "${PUBLISH[@]}"
  echo
  exit 0
fi

"${PUBLISH[@]}"
