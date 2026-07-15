from __future__ import annotations

import argparse
import json
import os
import threading
import uuid
from pathlib import Path
from typing import Any

import uvicorn
import yaml
from fastapi import FastAPI, HTTPException
from fastapi.responses import FileResponse
from fastapi.staticfiles import StaticFiles

from .manifest import Manifest, validate_manifest_data
from .runner import run_experiment


PACKAGE_ROOT = Path(__file__).resolve().parent
STUDIO_ROOT = PACKAGE_ROOT / "studio"
MANIFEST_ROOT = PACKAGE_ROOT / "templates"
RUN_ROOT = Path(os.environ.get("VSMC_RESEARCH_RUN_ROOT", "runs")).resolve()

app = FastAPI(title="VSMC Visual Experiment Studio", version="0.5.0")
app.mount("/assets", StaticFiles(directory=STUDIO_ROOT), name="studio-assets")
_jobs: dict[str, dict[str, Any]] = {}


@app.get("/")
def index() -> FileResponse:
    return FileResponse(STUDIO_ROOT / "index.html")


@app.get("/api/templates")
def templates() -> list[dict[str, Any]]:
    result = []
    for path in sorted(MANIFEST_ROOT.glob("*.yaml")):
        data = yaml.safe_load(path.read_text())
        result.append({"id": path.stem, "name": data.get("name", path.stem), "manifest": data})
    return result


@app.post("/api/validate")
def validate(payload: dict[str, Any]) -> dict[str, Any]:
    try:
        validate_manifest_data(payload)
    except ValueError as exc:
        raise HTTPException(status_code=422, detail=str(exc)) from exc
    return {"valid": True}


@app.post("/api/experiments")
def start_experiment(payload: dict[str, Any]) -> dict[str, Any]:
    try:
        validate_manifest_data(payload)
    except ValueError as exc:
        raise HTTPException(status_code=422, detail=str(exc)) from exc
    job_id = uuid.uuid4().hex[:12]
    _jobs[job_id] = {"status": "queued", "bundle": None, "error": None}

    def worker() -> None:
        _jobs[job_id]["status"] = "running"
        try:
            pseudo_path = MANIFEST_ROOT / f"studio-{job_id}.yaml"
            manifest = Manifest(pseudo_path, payload)
            bundle = run_experiment(manifest, RUN_ROOT)
            metrics = json.loads((bundle / "metrics/metrics.json").read_text())
            _jobs[job_id].update(status="completed", bundle=str(bundle), metrics=metrics)
        except Exception as exc:  # pragma: no cover - surfaced to visual client
            _jobs[job_id].update(status="failed", error=str(exc))

    threading.Thread(target=worker, daemon=True).start()
    return {"job_id": job_id, "status": "queued"}


@app.get("/api/experiments/{job_id}")
def experiment_status(job_id: str) -> dict[str, Any]:
    if job_id not in _jobs:
        raise HTTPException(status_code=404, detail="Unknown experiment job")
    return _jobs[job_id]


def main() -> None:
    parser = argparse.ArgumentParser(prog="vsmc-studio")
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=8090)
    args = parser.parse_args()
    uvicorn.run("vsmc_research.api:app", host=args.host, port=args.port, reload=False)


if __name__ == "__main__":
    main()
