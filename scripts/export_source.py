#!/usr/bin/env python3
"""Export versioned, deterministic, explicitly listed source and package deliveries."""
from __future__ import annotations

import hashlib
import json
from pathlib import Path
import zipfile

ROOT = Path(__file__).resolve().parents[1]
ROOT_FILES = [".gitignore", ".gitattributes", "VERSION", "LICENSE", "AGENTS.md", "README.md", "ARCHITECTURE.md", "ADAPTER-GUIDE.md", "PROVENANCE.md", "PLATFORMS.md", "CONTRIBUTING.md", "CHANGELOG.md"]
EXTENSIONS = {".cs", ".csproj", ".json", ".py", ".ps1", ".xml", ".yml"}


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def archive(path: Path, payloads: dict[str, bytes]) -> None:
    with zipfile.ZipFile(path, "w", compression=zipfile.ZIP_STORED) as package:
        for name in sorted(payloads):
            info = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
            info.create_system = 3
            info.external_attr = 0o100644 << 16
            package.writestr(info, payloads[name])


def export() -> dict:
    version = (ROOT / "VERSION").read_text().strip()
    sources = [ROOT / name for name in ROOT_FILES]
    for directory in ("src", "samples", "tests", "scripts", ".github"):
        sources.extend(p for p in (ROOT / directory).rglob("*") if p.is_file() and not p.is_symlink()
                       and not any(part in ("bin", "obj", "__pycache__") for part in p.relative_to(ROOT).parts)
                       and p.suffix in EXTENSIONS)
    payloads = {p.relative_to(ROOT).as_posix(): p.read_bytes().replace(b"\r\n", b"\n") for p in sources}
    hashes = {name: digest(data) for name, data in payloads.items()}
    manifest = {"launcherVersion": version, "license": "MIT", "files": hashes}
    payloads["SOURCE-MANIFEST.json"] = (json.dumps(manifest, indent=2, sort_keys=True) + "\n").encode()
    output = ROOT / "dist"
    output.mkdir(exist_ok=True)
    source_zip = output / f"CompanionLauncher-{version}-source.zip"
    archive(source_zip, payloads)
    packages = {}
    for name in ("Companion.Launcher.Core", "Companion.Launcher.Windows", "Companion.Launcher.Avalonia"):
        package = ROOT / "packages" / f"{name}.{version}.nupkg"
        packages[package.name] = package.read_bytes()
    android_package = ROOT / "packages" / f"Companion.Launcher.Android.{version}.nupkg"
    if android_package.is_file(): packages[android_package.name] = android_package.read_bytes()
    package_zip = output / f"CompanionLauncher-{version}-packages.zip"
    archive(package_zip, packages)
    receipt = {"schemaVersion": 1, "launcherVersion": version, "sourceFileCount": len(hashes),
               "sourceArchive": source_zip.name, "sourceSha256": digest(source_zip.read_bytes()),
               "packageArchive": package_zip.name, "packageSha256": digest(package_zip.read_bytes()),
               "packages": {name: digest(data) for name, data in packages.items()}, "files": hashes,
               "gameContentIncluded": False}
    (output / "export-receipt.json").write_text(json.dumps(receipt, indent=2, sort_keys=True) + "\n", encoding="utf-8", newline="\n")
    return receipt


if __name__ == "__main__":
    result = export()
    print(json.dumps({k: v for k, v in result.items() if k not in ("files", "packages")}, indent=2))
