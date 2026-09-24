#!/bin/bash
# This file is part of AetherAprs
# SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
# SPDX-License-Identifier: GPL-3.0-or-later

# Extract version from git tag and calculate Android version code
# Usage: extract-version.sh v1.2.3-beta.5

set -e

TAG="$1"

if [ -z "$TAG" ]; then
  echo "Error: No tag provided"
  exit 1
fi

# Remove 'v' prefix if present
TAG="${TAG#v}"

# Extract version components
VERSION_MAJOR=$(echo "$TAG" | cut -d. -f1)
VERSION_MINOR=$(echo "$TAG" | cut -d. -f2)
VERSION_PATCH=$(echo "$TAG" | cut -d. -f3 | cut -d- -f1)

# Extract pre-release type and number if present
PRERELEASE_NUM=99  # Default to 99 for final releases

if [[ $TAG =~ -([a-zA-Z]+)\.([0-9]+) ]]; then
  PRERELEASE_TYPE="${BASH_REMATCH[1]}"
  PRERELEASE_SEQ="${BASH_REMATCH[2]}"
  
  # Map pre-release types to number ranges
  # alfa/alpha: 1-33, beta: 34-66, rc: 67-98, final: 99
  case "$PRERELEASE_TYPE" in
    alfa|alpha)
      if [ "$PRERELEASE_SEQ" -le 33 ]; then
        PRERELEASE_NUM=$PRERELEASE_SEQ
      else
        echo "Error: alfa version number must be 1-33, got $PRERELEASE_SEQ" >&2
        exit 1
      fi
      ;;
    beta)
      if [ "$PRERELEASE_SEQ" -le 33 ]; then
        PRERELEASE_NUM=$((33 + PRERELEASE_SEQ))
      else
        echo "Error: beta version number must be 1-33, got $PRERELEASE_SEQ" >&2
        exit 1
      fi
      ;;
    rc)
      if [ "$PRERELEASE_SEQ" -le 32 ]; then
        PRERELEASE_NUM=$((66 + PRERELEASE_SEQ))
      else
        echo "Error: rc version number must be 1-32, got $PRERELEASE_SEQ" >&2
        exit 1
      fi
      ;;
    *)
      echo "Warning: Unknown pre-release type '$PRERELEASE_TYPE', treating as custom pre-release" >&2
      if [ "$PRERELEASE_SEQ" -le 98 ]; then
        PRERELEASE_NUM=$PRERELEASE_SEQ
      else
        echo "Error: custom pre-release number must be 1-98, got $PRERELEASE_SEQ" >&2
        exit 1
      fi
      ;;
  esac
fi

# Version code: MAJOR * 1000000 + MINOR * 10000 + PATCH * 100 + PRERELEASE
VERSION_CODE=$((VERSION_MAJOR * 1000000 + VERSION_MINOR * 10000 + VERSION_PATCH * 100 + PRERELEASE_NUM))

# Output for GitHub Actions
echo "tag=$TAG"
echo "version_code=$VERSION_CODE"
echo "version_name=$TAG"
echo ""
echo "Building version $TAG (code: $VERSION_CODE, prerelease: $PRERELEASE_NUM)" >&2
