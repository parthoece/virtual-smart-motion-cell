#!/usr/bin/env python3
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("--root", default=".")
parser.add_argument("--output", default="artifacts/sbom.spdx.json")
args = parser.parse_args()

root = Path(args.root).resolve()
out = Path(args.output)
if not out.is_absolute():
    out = (root / out).resolve()

excluded_directories = {
    ".git",
    "bin",
    "obj",
    "artifacts",
    "runtime-data",
    "node_modules",
    "__pycache__",
    ".pytest_cache",
    "TestResults",
}
files: list[dict[str, object]] = []
for path in sorted(root.rglob("*")):
    if not path.is_file() or path.resolve() == out:
        continue
    if any(part in excluded_directories or part.endswith(".egg-info") for part in path.parts):
        continue
    data = path.read_bytes()
    relative = path.relative_to(root).as_posix()
    files.append(
        {
            "SPDXID": "SPDXRef-File-" + hashlib.sha1(relative.encode()).hexdigest(),
            "fileName": "./" + relative,
            "checksums": [
                {"algorithm": "SHA256", "checksumValue": hashlib.sha256(data).hexdigest()}
            ],
        }
    )

document_fingerprint = hashlib.sha256(
    "\n".join(item["fileName"] for item in files).encode()
).hexdigest()
document = {
    "spdxVersion": "SPDX-2.3",
    "dataLicense": "CC0-1.0",
    "SPDXID": "SPDXRef-DOCUMENT",
    "name": "virtual-smart-motion-cell-source",
    "documentNamespace": (
        "https://example.org/spdx/virtual-smart-motion-cell/" + document_fingerprint
    ),
    "creationInfo": {
        "created": "2026-07-14T00:00:00Z",
        "creators": ["Tool: scripts/generate_sbom.py"],
    },
    "files": files,
}
out.parent.mkdir(parents=True, exist_ok=True)
out.write_text(json.dumps(document, indent=2) + "\n", encoding="utf-8")
print(out)
