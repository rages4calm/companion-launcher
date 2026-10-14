#!/usr/bin/env python3
"""Prove two separate new-game apps build using the same pinned packages, not shared source copies."""
from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import subprocess
import uuid

from new_game import ROOT, VERSION, create


def run(arguments: list[str], directory: Path, environment: dict | None = None) -> str:
    result = subprocess.run(arguments, cwd=directory, env=environment, capture_output=True, text=True, timeout=120)
    if result.returncode:
        raise RuntimeError(f"command failed ({result.returncode}): {arguments}\n{result.stdout}\n{result.stderr}")
    return result.stdout


def verify() -> dict:
    proof = ROOT / "build" / "consumer-proof" / uuid.uuid4().hex
    proof.mkdir(parents=True, exist_ok=False)
    # An unreleased local package can be repacked during development. A fresh proof cache
    # ensures we verify the actual feed bytes, rather than an older global NuGet cache.
    build_environment = dict(os.environ, NUGET_PACKAGES=str(proof / "nuget-cache"))
    projects = []
    for game_id, title in (("fixture-a", 'First "Quoted" Game'), ("fixture-b", "Second Game")):
        output = create(game_id, title, proof / game_id, ROOT / "packages")
        log = run(["dotnet", "build", "Game.Companion.csproj", "-c", "Release"], output, build_environment)
        (output / "build.log").write_text(log, encoding="utf-8")
        lock = json.loads((output / "packages.lock.json").read_text(encoding="utf-8"))
        dependencies = next(iter(lock["dependencies"].values()))
        for package in ("Companion.Launcher.Core", "Companion.Launcher.Windows"):
            if dependencies[package]["resolved"] != VERSION:
                raise RuntimeError("consumer did not resolve the exact shared package")
        assets = json.loads((output / "obj" / "project.assets.json").read_text(encoding="utf-8"))
        if any(item.get("type") == "project" for item in assets["libraries"].values()):
            raise RuntimeError("consumer used source project references instead of shared packages")
        smoke = output / "ui-smoke"
        env = dict(os.environ, COMPANION_DATA_ROOT=str(proof / "data"))
        exe = output / "bin" / "Release" / "net8.0-windows" / f"{game_id}.Companion.exe"
        run([str(exe), "--smoke-test", str(smoke)], output, env)
        result = json.loads((smoke / "smoke-test.json").read_text())
        if result["gameLaunched"] or result["settingsApplied"] or result["gameId"] != game_id or result["pageCount"] != 2:
            raise RuntimeError("generated-app UI smoke contract failed")
        if (proof / "data").exists():
            raise RuntimeError("read-only smoke unexpectedly wrote settings")
        projects.append({"gameId": game_id, "project": str(output), "builtFromPackages": True,
                         "packageVersions": {name: dependencies[name]["resolved"] for name in ("Companion.Launcher.Core", "Companion.Launcher.Windows")},
                         "renderedPages": result["pageCount"], "gameLaunched": False, "settingsApplied": False})
    packages = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in (ROOT / "packages").glob(f"*.{VERSION}.nupkg")}
    receipt = {"passed": True, "launcherVersion": VERSION, "packages": packages, "consumers": projects}
    (proof / "verification.json").write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    return receipt


if __name__ == "__main__":
    print(json.dumps(verify(), indent=2))
