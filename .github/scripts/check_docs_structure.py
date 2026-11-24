#!/usr/bin/env python3
# This file is part of AetherAprs
# SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
# SPDX-License-Identifier: GPL-3.0-or-later
"""Keep CODEBASE_STRUCTURE.md aligned with the tree.

The document is the map contributors and coding agents use to decide where a
new file belongs. It drifts silently when a folder is added or renamed. This
check fails when:

* a project directory or a core top-level folder is absent from the document;
* a ``Services/`` sub-folder is absent from the service-folder list;
* the document lists a folder that does not exist.
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

IGNORED_FOLDERS = {"bin", "obj"}
PATH_TOKEN = re.compile(r"`([A-Za-z0-9_./-]+/)`")
CODE_BLOCK_PATH = re.compile(r"(?m)^([A-Za-z0-9_.-]+/)\s{2,}")
BARE_TOKEN = re.compile(r"`([A-Za-z0-9_]+)`")


def section(text: str, heading: str) -> str:
    """Return the text between ``heading`` and the next level-2 heading."""
    start = text.find(heading)
    if start == -1:
        return ""
    start += len(heading)
    end = text.find("\n## ", start)
    return text[start:] if end == -1 else text[start:end]


def documented_paths(document: str) -> set[str]:
    """Return every folder-shaped path written in backticks or a layout block."""
    backticked = {token.rstrip("/") for token in PATH_TOKEN.findall(document)}
    block = {token.rstrip("/") for token in CODE_BLOCK_PATH.findall(document)}
    return backticked | block


def documented_services(document: str) -> set[str]:
    """Return the service folders named in the prose list."""
    prose = section(document, "## Core folders")
    marker = "Service folders are"
    start = prose.find(marker)
    if start == -1:
        return set()
    start += len(marker)
    end = prose.find("\n\n", start)
    return set(BARE_TOKEN.findall(prose[start:] if end == -1 else prose[start:end]))


def listed_core_folders(document: str) -> set[str]:
    """Return the folders written in the ``Core folders`` table."""
    prose = section(document, "## Core folders")
    table = prose.split("## ", 1)[0]
    return set(PATH_TOKEN.findall(table))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path.cwd(), help="Repository root.")
    parser.add_argument(
        "--doc",
        type=Path,
        help="Document to check (default: <root>/CODEBASE_STRUCTURE.md).",
    )
    args = parser.parse_args()

    root = args.root.resolve()
    doc_path = args.doc or root / "CODEBASE_STRUCTURE.md"
    if not doc_path.is_file():
        print(f"error: {doc_path} does not exist", file=sys.stderr)
        return 2

    document = doc_path.read_text(encoding="utf-8")
    paths = documented_paths(document)
    services = documented_services(document)
    core_table = listed_core_folders(document)
    problems: list[str] = []

    core = root / "AetherAprs"
    if not core.is_dir():
        print(f"error: core project folder {core} does not exist", file=sys.stderr)
        return 2

    def is_documented(folder: str) -> bool:
        return any(path == folder or path.startswith(folder + "/") for path in paths)

    for entry in sorted(core.iterdir()):
        if entry.is_dir() and entry.name not in IGNORED_FOLDERS and not is_documented(entry.name):
            problems.append(
                f"core folder `{entry.name}/` is missing from CODEBASE_STRUCTURE.md"
            )

    services_root = core / "Services"
    if services_root.is_dir():
        for entry in sorted(services_root.iterdir()):
            if entry.is_dir() and entry.name not in IGNORED_FOLDERS:
                if entry.name not in services and f"Services/{entry.name}" not in paths:
                    problems.append(
                        f"service folder `Services/{entry.name}/` is missing from the "
                        "service folder list"
                    )

    for relative in sorted(core_table):
        if not (core / relative).is_dir():
            problems.append(
                f"CODEBASE_STRUCTURE.md lists `{relative}`, but AetherAprs/{relative} "
                "does not exist"
            )

    for project in sorted(root.glob("*/*.csproj")):
        folder = project.parent.name
        if not is_documented(folder):
            problems.append(
                f"project folder `{folder}/` is missing from CODEBASE_STRUCTURE.md"
            )

    if problems:
        print("CODEBASE_STRUCTURE.md is out of date:")
        for problem in problems:
            print(f"  - {problem}")
        return 1

    print("CODEBASE_STRUCTURE.md matches the tree.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
