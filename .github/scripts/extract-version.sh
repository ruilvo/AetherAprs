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

# Validate the tag format up front (M5): major.minor.patch with an optional
# pre-release suffix (-<type>.<number>).
if ! [[ $TAG =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[a-zA-Z]+\.[0-9]+)?$ ]]; then
  echo "Error: Invalid tag format '$TAG'. Expected vMAJOR.MINOR.PATCH[-<type>.<number>]." >&2
  exit 1
fi

# Reject leading zeros: v01.2.3 and v1.2.3 are different tags but would resolve to
# the same version code, so the second release for the pair would never install.
if [[ $TAG =~ (^|\.)0[0-9] ]]; then
  echo "Error: Invalid tag '$TAG'. Version components must not have leading zeros." >&2
  exit 1
fi

# Extract version components
VERSION_MAJOR=$(echo "$TAG" | cut -d. -f1)
VERSION_MINOR=$(echo "$TAG" | cut -d. -f2)
VERSION_PATCH=$(echo "$TAG" | cut -d. -f3 | cut -d- -f1)

# The version code packs four decimal fields into one integer, so a component that
# overflows its field would borrow from the next one and collide with another tag:
# 1.100.0 and 2.0.0 would both be 2000099. Bound every field before packing.
MAX_MAJOR=2099
MAX_COMPONENT=99

if [ "$VERSION_MAJOR" -gt "$MAX_MAJOR" ]; then
  echo "Error: Major version must be 0-$MAX_MAJOR, got $VERSION_MAJOR." >&2
  exit 1
fi
if [ "$VERSION_MINOR" -gt "$MAX_COMPONENT" ]; then
  echo "Error: Minor version must be 0-$MAX_COMPONENT, got $VERSION_MINOR." >&2
  exit 1
fi
if [ "$VERSION_PATCH" -gt "$MAX_COMPONENT" ]; then
  echo "Error: Patch version must be 0-$MAX_COMPONENT, got $VERSION_PATCH." >&2
  exit 1
fi

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

# Android refuses version codes above 2100000000; fail here rather than at install time.
if [ "$VERSION_CODE" -gt 2100000000 ]; then
  echo "Error: Version code $VERSION_CODE exceeds the Android maximum of 2100000000." >&2
  exit 1
fi

# Output for GitHub Actions
echo "tag=$TAG"
echo "version_code=$VERSION_CODE"
echo "version_name=$TAG"
echo ""
echo "Building version $TAG (code: $VERSION_CODE, prerelease: $PRERELEASE_NUM)" >&2
