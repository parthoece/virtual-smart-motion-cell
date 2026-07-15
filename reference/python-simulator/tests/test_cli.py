from __future__ import annotations

from smart_motion_cell.cli import main


def test_validate_config_command(project_root, capsys) -> None:
    result = main(["validate-config", "--config", str(project_root / "configs" / "demo-cell.json")])
    assert result == 0
    assert "configuration valid" in capsys.readouterr().out


def test_simulate_command_writes_report(project_root, tmp_path) -> None:
    database = tmp_path / "cli.sqlite"
    report = tmp_path / "report"
    result = main(
        [
            "simulate",
            "--config",
            str(project_root / "configs" / "demo-cell.json"),
            "--cycles",
            "1",
            "--database",
            str(database),
            "--report-dir",
            str(report),
        ]
    )
    assert result == 0
    assert (report / "index.html").exists()


def test_validate_config_uses_bundled_default(capsys) -> None:
    result = main(["validate-config"])
    assert result == 0
    assert "bundled demo-cell.json" in capsys.readouterr().out
