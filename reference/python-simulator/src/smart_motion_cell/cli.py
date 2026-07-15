from __future__ import annotations

import argparse
import json
from collections.abc import Sequence

from smart_motion_cell.config import load_config, load_default_config
from smart_motion_cell.reporting.report import generate_report
from smart_motion_cell.simulation.runner import run_simulation
from smart_motion_cell.simulation.scenarios import SCENARIOS


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="smc",
        description="Virtual Smart Motion Cell simulation, evidence reporting, and API.",
    )
    subparsers = parser.add_subparsers(dest="command", required=True)

    simulate = subparsers.add_parser("simulate", help="run a deterministic cell simulation")
    simulate.add_argument(
        "--config",
        help="JSON configuration path; omit to use the bundled demo cell",
    )
    simulate.add_argument("--cycles", type=int, default=3)
    simulate.add_argument("--scenario", choices=sorted(SCENARIOS), default="normal")
    simulate.add_argument("--database", default="artifacts/demo.sqlite")
    simulate.add_argument("--report-dir", default="artifacts/report")
    simulate.add_argument("--no-report", action="store_true")

    report = subparsers.add_parser("report", help="generate HTML, SVG, CSV, and JSON evidence")
    report.add_argument("--database", default="artifacts/demo.sqlite")
    report.add_argument("--output", default="artifacts/report")
    report.add_argument("--run-id")

    validate = subparsers.add_parser("validate-config", help="validate a cell configuration")
    validate.add_argument(
        "--config",
        help="JSON configuration path; omit to use the bundled demo cell",
    )

    serve = subparsers.add_parser("serve", help="serve the telemetry API and dashboard")
    serve.add_argument("--database", default="artifacts/demo.sqlite")
    serve.add_argument("--host", default="127.0.0.1")
    serve.add_argument("--port", type=int, default=8000)
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    if args.command == "validate-config":
        config = load_config(args.config) if args.config else load_default_config()
        source = args.config or "bundled demo-cell.json"
        print(f"configuration valid: {source} ({config.recipe.version})")
        return 0
    if args.command == "report":
        path = generate_report(args.database, args.output, args.run_id)
        print(f"report: {path}")
        return 0
    if args.command == "serve":
        try:
            import uvicorn
        except ImportError as exc:
            raise RuntimeError(
                'Install API dependencies with: python -m pip install -e ".[api]"'
            ) from exc
        from smart_motion_cell.api import create_app

        uvicorn.run(create_app(args.database), host=args.host, port=args.port)
        return 0
    if args.command == "simulate":
        config = load_config(args.config) if args.config else load_default_config()
        summary = run_simulation(
            config=config,
            cycles=args.cycles,
            database=args.database,
            scenario_name=args.scenario,
        )
        print(json.dumps(summary.as_dict(), indent=2))
        if not args.no_report:
            path = generate_report(args.database, args.report_dir, summary.run_id)
            print(f"report: {path}")
        return 0 if summary.status == "completed" else 2
    raise AssertionError(f"unhandled command: {args.command}")
