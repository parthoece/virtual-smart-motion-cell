from pathlib import Path

from fastapi.testclient import TestClient

from vsmc_research.api import app


ROOT = Path(__file__).resolve().parents[2]


def test_visual_studio_lists_templates_and_validates() -> None:
    client = TestClient(app)
    response = client.get("/api/templates")
    assert response.status_code == 200
    templates = response.json()
    assert len(templates) >= 4
    validation = client.post("/api/validate", json=templates[0]["manifest"])
    assert validation.status_code == 200
    index = client.get("/")
    assert index.status_code == 200
    assert "Visual Experiment Studio" in index.text


def test_visual_studio_runs_short_experiment(tmp_path: Path, monkeypatch) -> None:
    import time
    import vsmc_research.api as api_module

    monkeypatch.setattr(api_module, "RUN_ROOT", tmp_path)
    client = TestClient(app)
    manifest = client.get("/api/templates").json()[0]["manifest"]
    manifest["episodes"] = 1
    manifest["environment"]["duration_s"] = 3
    manifest["scenarios"] = []
    started = client.post("/api/experiments", json=manifest)
    assert started.status_code == 200
    job_id = started.json()["job_id"]
    for _ in range(80):
        job = client.get(f"/api/experiments/{job_id}").json()
        if job["status"] in {"completed", "failed"}:
            break
        time.sleep(0.05)
    assert job["status"] == "completed", job
    assert job["metrics"]["telemetry_rows"] > 0


def test_studio_assets_are_packaged() -> None:
    import vsmc_research.api as api_module

    assert (api_module.STUDIO_ROOT / "index.html").is_file()
    assert (api_module.STUDIO_ROOT / "app.js").is_file()
    assert len(list(api_module.MANIFEST_ROOT.glob("*.yaml"))) >= 4


def test_packaged_studio_matches_repository_assets() -> None:
    package_root = ROOT / "research/vsmc_research/studio"
    source_root = ROOT / "research/studio"
    for name in ["index.html", "styles.css", "app.js"]:
        assert (package_root / name).read_text() == (source_root / name).read_text()
