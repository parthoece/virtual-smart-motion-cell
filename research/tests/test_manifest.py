from pathlib import Path

from vsmc_research.manifest import load_manifest


ROOT = Path(__file__).resolve().parents[2]


def test_all_manifests_validate() -> None:
    paths = sorted((ROOT / "benchmarks/manifests").glob("*.yaml"))
    assert paths
    for path in paths:
        manifest = load_manifest(path)
        assert manifest.experiment_id
        assert manifest.sha256()


def test_packaged_templates_match_repository_manifests() -> None:
    package_templates = ROOT / "research/vsmc_research/templates"
    repository_templates = ROOT / "benchmarks/manifests"
    for repository in sorted(repository_templates.glob("*.yaml")):
        packaged = package_templates / repository.name
        assert packaged.read_text() == repository.read_text()
