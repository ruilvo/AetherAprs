#!/usr/bin/env python3
# This file is part of AetherAprs
# SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
# SPDX-License-Identifier: GPL-3.0-or-later
"""Generate and verify a CycloneDX SBOM for the Android application.

The document is built from ``dotnet list package --include-transitive --format
json``, so it lists the package versions the build actually resolved instead of a
hand-maintained list that drifts from the project files. ``verify`` re-reads the
generated document and proves that it describes the exact APK about to be
published: same version name, same version code, same SHA-256.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import shutil
import subprocess
import sys
import uuid
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

APPLICATION_NAME = "AetherAprs"
CYCLONEDX_SPEC_VERSION = "1.5"
SERIAL_NAMESPACE = uuid.UUID("6b9f7f8e-9f38-4f0a-9a4e-0d1b7c2a5f31")
VERSION_CODE_PROPERTY = "aether-aprs:version-code"


def read_report(project: str) -> dict[str, Any]:
    """Return the resolved dependency report for ``project``."""
    dotnet = shutil.which("dotnet")
    if dotnet is None:
        raise SystemExit("error: dotnet was not found on PATH")

    command = [
        dotnet,
        "list",
        project,
        "package",
        "--include-transitive",
        "--format",
        "json",
    ]
    result = subprocess.run(command, capture_output=True, text=True, encoding="utf-8")
    if result.returncode != 0:
        print(result.stdout, file=sys.stderr)
        print(result.stderr, file=sys.stderr)
        raise SystemExit("error: dotnet list package failed; refusing to describe an unresolved build")

    try:
        return json.loads(result.stdout)
    except json.JSONDecodeError as exception:
        raise SystemExit(f"error: could not parse the dependency report: {exception}") from exception


def collect_packages(report: dict[str, Any]) -> list[dict[str, str]]:
    """Deduplicate the per-project, per-framework package entries."""
    packages: dict[tuple[str, str], dict[str, str]] = {}

    for project in report.get("projects", []):
        for framework in project.get("frameworks", []):
            for group in ("topLevelPackages", "transitivePackages"):
                for package in framework.get(group) or []:
                    identifier = package.get("id")
                    version = package.get("resolvedVersion")
                    if not identifier or not version:
                        continue
                    packages[(identifier, version)] = {
                        "type": "library",
                        "name": identifier,
                        "version": version,
                        "purl": f"pkg:nuget/{identifier}@{version}",
                        "bom-ref": f"pkg:nuget/{identifier}@{version}",
                    }

    return [packages[key] for key in sorted(packages)]


def sha256_of(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def build_sbom(
    report: dict[str, Any],
    version_name: str,
    version_code: int,
    apk_hash: str | None,
) -> dict[str, Any]:
    """Describe the application and everything the build resolved for it."""
    components = collect_packages(report)
    if not components:
        raise SystemExit("error: the dependency report listed no packages")

    application: dict[str, Any] = {
        "type": "application",
        "bom-ref": f"pkg:generic/{APPLICATION_NAME}@{version_name}",
        "name": APPLICATION_NAME,
        "version": version_name,
        "purl": f"pkg:generic/{APPLICATION_NAME}@{version_name}",
        "properties": [{"name": VERSION_CODE_PROPERTY, "value": str(version_code)}],
    }
    if apk_hash is not None:
        application["hashes"] = [{"alg": "SHA-256", "content": apk_hash}]

    return {
        "bomFormat": "CycloneDX",
        "specVersion": CYCLONEDX_SPEC_VERSION,
        "serialNumber": f"urn:uuid:{uuid.uuid5(SERIAL_NAMESPACE, f'{version_name}/{version_code}')}",
        "version": 1,
        "metadata": {
            "timestamp": datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z"),
            "tools": {
                "components": [
                    {
                        "type": "application",
                        "name": "generate_sbom.py",
                        "version": "1",
                    }
                ]
            },
            "component": application,
        },
        "components": components,
    }


def verify_sbom(
    sbom: dict[str, Any],
    version_name: str,
    version_code: int,
    apk_hash: str | None,
) -> list[str]:
    """Return every way ``sbom`` fails to describe this release."""
    problems: list[str] = []

    component = (sbom.get("metadata") or {}).get("component") or {}
    if component.get("name") != APPLICATION_NAME:
        problems.append(f"the SBOM describes '{component.get('name')}', not {APPLICATION_NAME}")
    if component.get("version") != version_name:
        problems.append(f"the SBOM version is '{component.get('version')}', not '{version_name}'")

    properties = {entry.get("name"): entry.get("value") for entry in component.get("properties") or []}
    if properties.get(VERSION_CODE_PROPERTY) != str(version_code):
        problems.append(
            f"the SBOM version code is '{properties.get(VERSION_CODE_PROPERTY)}', not '{version_code}'"
        )

    if apk_hash is not None:
        hashes = {
            entry.get("alg"): str(entry.get("content", "")).lower() for entry in component.get("hashes") or []
        }
        if hashes.get("SHA-256") != apk_hash.lower():
            problems.append("the SBOM checksum does not match the APK")

    components = sbom.get("components") or []
    if not components:
        problems.append("the SBOM lists no dependency components")
    for entry in components:
        if not entry.get("name") or not entry.get("version") or not str(entry.get("purl", "")).startswith("pkg:"):
            problems.append(f"the SBOM component '{entry.get('name')}' is incomplete")
            break

    return problems


def read_json(path: Path) -> dict[str, Any]:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exception:
        raise SystemExit(f"error: could not read {path}: {exception}") from exception


def write_sbom(document: dict[str, Any], output: Path) -> None:
    output.write_text(json.dumps(document, indent=2, ensure_ascii=True) + "\n", encoding="utf-8")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    commands = parser.add_subparsers(dest="command", required=True)

    generate = commands.add_parser("generate", help="write a CycloneDX SBOM for the resolved build")
    generate.add_argument("--project", default="AetherAprs.Android/AetherAprs.Android.csproj")
    generate.add_argument("--report", type=Path, help="read an existing JSON report instead of invoking dotnet")
    generate.add_argument("--output", type=Path, required=True)
    generate.add_argument("--version-name", required=True)
    generate.add_argument("--version-code", type=int, required=True)
    generate.add_argument("--apk", type=Path, help="APK whose SHA-256 the SBOM should record")

    verify = commands.add_parser("verify", help="prove an SBOM describes this release artifact")
    verify.add_argument("--sbom", type=Path, required=True)
    verify.add_argument("--version-name", required=True)
    verify.add_argument("--version-code", type=int, required=True)
    verify.add_argument("--apk", type=Path)

    args = parser.parse_args(argv)

    if args.command == "generate":
        if args.report is not None:
            report = read_json(args.report)
        else:
            report = read_report(args.project)
        apk_hash = sha256_of(args.apk) if args.apk is not None else None
        document = build_sbom(report, args.version_name, args.version_code, apk_hash)
        write_sbom(document, args.output)
        print(f"Wrote {args.output} with {len(document['components'])} components.")
        return 0

    apk_hash = sha256_of(args.apk) if args.apk is not None else None
    problems = verify_sbom(read_json(args.sbom), args.version_name, args.version_code, apk_hash)
    if problems:
        for problem in problems:
            print(f"error: {problem}", file=sys.stderr)
        return 1

    print(f"{args.sbom} describes {APPLICATION_NAME} {args.version_name} ({args.version_code}).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
