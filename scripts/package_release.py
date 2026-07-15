#!/usr/bin/env python3
"""Create deterministic-ish platform archives from dotnet publish outputs."""
from __future__ import annotations

import argparse
import hashlib
import tarfile
import zipfile
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("--rid", required=True)
parser.add_argument("--extension", choices=("zip", "tar.gz"), required=True)
args = parser.parse_args()

artifacts = Path("artifacts")
out = artifacts / "release"
out.mkdir(parents=True, exist_ok=True)


def write_archive(source: Path, name: str) -> Path:
    if not source.is_dir():
        raise SystemExit(f"Missing publish directory: {source}")
    if args.extension == "zip":
        target = out / f"{name}.zip"
        with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED) as archive:
            for item in sorted(source.rglob("*")):
                if item.is_file():
                    archive.write(item, item.relative_to(source))
        return target
    target = out / f"{name}.tar.gz"
    with tarfile.open(target, "w:gz") as archive:
        archive.add(source, arcname=name)
    return target


created: list[Path] = []
for kind in ("runtime", "hmi", "mes-simulator"):
    created.append(write_archive(
        artifacts / f"{kind}-{args.rid}",
        f"virtual-smart-motion-cell-{kind}-{args.rid}",
    ))

checksums = [f"{hashlib.sha256(path.read_bytes()).hexdigest()}  {path.name}" for path in created]
(out / f"checksums-{args.rid}.txt").write_text("\n".join(checksums) + "\n", encoding="utf-8")
