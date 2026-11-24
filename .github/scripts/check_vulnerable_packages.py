#!/usr/bin/env python3
# This file is part of AetherAprs
# SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
# SPDX-License-Identifier: GPL-3.0-or-later
"""Fail the build when NuGet reports a known vulnerability.

``dotnet list package --vulnerable`` exits with status 0 even when it finds
vulnerable packages, so the CI step must inspect the machine-readable report
instead. The report format is versioned and deliberately sparse: vulnerable
package entries gain a ``vulnerabilities`` array, and healthy entries (or a
fully clean report) do not.
"""

from __future__ import annotations

import argparse
import json
import shutil
import subprocess
import sys
from pathlib import Path
from typing import Any


def find_vulnerable_packages(node: Any) -> list[dict[str, Any]]:
    """Walk the report and collect every object with a non-empty vulnerability list."""
    found: list[dict[str, Any]] = []

    if isinstance(node, dict):
        vulnerabilities = node.get("vulnerabilities")
        if isinstance(vulnerabilities, list) and vulnerabilities:
            found.append(node)
        for value in node.values():
            found.extend(find_vulnerable_packages(value))
    elif isinstance(node, list):
        for value in node:
            found.extend(find_vulnerable_packages(value))

    return found


def describe(package: dict[str, Any]) -> str:
    """Render one vulnerable package entry as a single actionable line."""
    identifier = package.get("id") or package.get("packageId") or "<unknown package>"
    version = package.get("resolvedVersion") or package.get("version") or "<unknown version>"
    severities = sorted(
        str(vulnerability.get("severity", "unknown"))
        for vulnerability in package.get("vulnerabilities", [])
        if isinstance(vulnerability, dict)
    )
    return f"{identifier} {version}: {', '.join(severities) or 'unknown severity'}"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "solution",
        nargs="?",
        default="AetherAprs.slnx",
        help="Solution or project to inspect (default: AetherAprs.slnx).",
    )
    parser.add_argument(
        "--report",
        type=Path,
        help="Read an existing JSON report instead of invoking dotnet (for tests).",
    )
    args = parser.parse_args()

    if args.report is not None:
        report_text = args.report.read_text(encoding="utf-8")
    else:
        dotnet = shutil.which("dotnet")
        if dotnet is None:
            print("error: dotnet was not found on PATH", file=sys.stderr)
            return 2

        command = [
            dotnet,
            "list",
            args.solution,
            "package",
            "--vulnerable",
            "--include-transitive",
            "--format",
            "json",
        ]
        result = subprocess.run(command, capture_output=True, text=True, encoding="utf-8")
        if result.returncode != 0:
            print(result.stdout, file=sys.stderr)
            print(result.stderr, file=sys.stderr)
            print("error: dotnet list package failed; refusing to report a clean scan", file=sys.stderr)
            return result.returncode

        report_text = result.stdout

    try:
        report = json.loads(report_text)
    except json.JSONDecodeError as exception:
        print(f"error: could not parse the vulnerability report: {exception}", file=sys.stderr)
        return 2

    vulnerable = find_vulnerable_packages(report)
    if vulnerable:
        print(f"Found {len(vulnerable)} vulnerable package entr{'y' if len(vulnerable) == 1 else 'ies'}:")
        for package in vulnerable:
            print(f"  {describe(package)}")
        return 1

    print("No vulnerable packages found.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
