#!/usr/bin/env python3
"""Create a Dalamud custom repository index from a packaged AutoFete ZIP."""

import argparse
import json
import sys
import time
from pathlib import Path
from urllib.parse import urlparse
from zipfile import BadZipFile, ZipFile
from xml.etree import ElementTree


PROJECT_ROOT = Path(__file__).resolve().parents[1]


def public_url(value: str) -> str:
    parsed = urlparse(value)
    if parsed.scheme not in ("http", "https") or not parsed.netloc:
        raise argparse.ArgumentTypeError("expected a public HTTP(S) URL")
    if parsed.hostname == "example.com" or "YOUR_NAME" in value:
        raise argparse.ArgumentTypeError("replace the example URL with a real public URL")
    return value


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--zip", type=Path, default=PROJECT_ROOT / "bin/Release/AutoFete/latest.zip")
    parser.add_argument("--source-url", type=public_url, required=True, help="public plugin source/project URL")
    parser.add_argument("--download-url", type=public_url, required=True, help="public URL of this exact ZIP")
    parser.add_argument("--output", type=Path, default=PROJECT_ROOT / "repo.json")
    parser.add_argument("--last-update", type=int, default=None, help="Unix timestamp; defaults to now")
    parser.add_argument("--release-tag", help="verify a v<Version> tag against the project and ZIP")
    args = parser.parse_args()

    try:
        with ZipFile(args.zip) as package:
            names = set(package.namelist())
            required = {"AutoFete.dll", "AutoFete.json"}
            missing = required - names
            if missing:
                raise ValueError(f"package is missing {', '.join(sorted(missing))}")
            manifest = json.loads(package.read("AutoFete.json"))
    except (OSError, BadZipFile, ValueError, json.JSONDecodeError) as error:
        parser.error(f"cannot use {args.zip}: {error}")

    if manifest.get("InternalName") != "AutoFete":
        parser.error("package manifest must have InternalName AutoFete")
    for key in ("Name", "Author", "Description", "Punchline", "AssemblyVersion", "DalamudApiLevel"):
        if not manifest.get(key):
            parser.error(f"package manifest is missing {key}")

    if args.release_tag:
        project_version = ElementTree.parse(PROJECT_ROOT / "AutoFete.csproj").findtext("./PropertyGroup/Version")
        if not project_version or args.release_tag != f"v{project_version}":
            parser.error(f"release tag {args.release_tag} must match project version v{project_version}")
        expected_assembly_version = ".".join((project_version.split(".") + ["0"] * 4)[:4])
        if manifest["AssemblyVersion"] != expected_assembly_version:
            parser.error(
                f"package assembly version {manifest['AssemblyVersion']} does not match {expected_assembly_version}"
            )

    entry = dict(manifest)
    entry.update(
        RepoUrl=args.source_url,
        DownloadLinkInstall=args.download_url,
        DownloadLinkUpdate=args.download_url,
        LastUpdate=str(args.last_update if args.last_update is not None else int(time.time())),
    )
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps([entry], indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"Wrote {args.output} for AutoFete {entry['AssemblyVersion']}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
