#!/usr/bin/env python3
"""Fails (exit code 1) when the extension, the host and the installer do not carry the same version.

Single source of truth: host/Directory.Build.props (<Version>). It must equal
  - extension/manifest.json  "version"
  - host/installer/setup.iss  #define MyAppVersion "..."
and no project file under host/ may set its own version (that would silently override the shared one).
Run from the repository root:  python3 tools/check_versions.py   (standard library only)
"""
import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

VERSION_FORMAT = re.compile(r"^\d+\.\d+\.\d+$")
VERSION_TAGS = ("Version", "AssemblyVersion", "FileVersion", "InformationalVersion", "VersionPrefix")


def read_props(root):
    node = ET.parse(root / "host" / "Directory.Build.props").getroot().find(".//Version")
    return node.text.strip() if node is not None and node.text else None


def read_manifest(root):
    with open(root / "extension" / "manifest.json", encoding="utf-8-sig") as handle:
        return json.load(handle).get("version")


def read_installer(root):
    text = (root / "host" / "installer" / "setup.iss").read_text(encoding="utf-8-sig", errors="replace")
    match = re.search(r'^\s*#define\s+MyAppVersion\s+"([^"]+)"', text, re.MULTILINE)
    return match.group(1) if match else None


def find_project_overrides(root):
    """Project files that set their own version (their <PackageReference Version=...> attributes are not tags and do not count)."""
    found = []
    for project in sorted((root / "host").rglob("*.csproj")):
        if any(part in ("obj", "bin") for part in project.parts):
            continue
        text = project.read_text(encoding="utf-8-sig", errors="replace")
        for tag in VERSION_TAGS:
            if re.search(r"<%s>" % tag, text):
                found.append("%s sets <%s>" % (project.relative_to(root).as_posix(), tag))
    return found


def check(root):
    """Returns (versions, problems): versions maps a source name to its version text (or None), problems is a list of messages."""
    root = Path(root)
    readers = (
        ("host/Directory.Build.props", read_props),
        ("extension/manifest.json", read_manifest),
        ("host/installer/setup.iss", read_installer),
    )
    versions, problems = {}, []
    for name, reader in readers:
        try:
            versions[name] = reader(root)
        except Exception as error:  # a missing file or broken JSON/XML must be a clear failure, not a stack trace
            versions[name] = None
            problems.append("%s: could not be read (%s: %s)" % (name, type(error).__name__, error))
            continue
        if versions[name] is None:
            problems.append("%s: no version found" % name)
        elif not VERSION_FORMAT.match(versions[name]):
            problems.append("%s: '%s' is not in the form MAJOR.MINOR.PATCH" % (name, versions[name]))

    present = {value for value in versions.values() if value}
    if len(present) > 1:
        problems.append("the versions differ: " + ", ".join("%s=%s" % (name, value) for name, value in versions.items()))

    problems.extend(find_project_overrides(root))
    return versions, problems


def main(argv):
    root = Path(argv[1]) if len(argv) > 1 else Path(".")
    versions, problems = check(root)
    for name, value in versions.items():
        print("%-28s %s" % (name, value))
    if problems:
        print()
        for problem in problems:
            print("ERROR: " + problem)
        return 1
    print("\nAll versions match.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
