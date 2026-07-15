from __future__ import annotations

import argparse
import json
from pathlib import Path

from .manifest import load_manifest
from .replay import replay_bundle
from .runner import run_experiment


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="vsmc-bench")
    subparsers = parser.add_subparsers(dest="command", required=True)
    validate = subparsers.add_parser("validate", help="Validate an experiment manifest")
    validate.add_argument("manifest")
    run = subparsers.add_parser("run", help="Run an experiment manifest")
    run.add_argument("manifest")
    run.add_argument("--output", default="runs")
    replay = subparsers.add_parser("replay", help="Replay and compare an experiment bundle")
    replay.add_argument("bundle")
    replay.add_argument("--output", default="runs/replays")
    return parser


def main() -> None:
    args = build_parser().parse_args()
    if args.command == "replay":
        print(json.dumps(replay_bundle(Path(args.bundle), Path(args.output)), indent=2))
        return
    manifest = load_manifest(args.manifest)
    if args.command == "validate":
        print(
            json.dumps(
                {
                    "valid": True,
                    "experiment_id": manifest.experiment_id,
                    "sha256": manifest.sha256(),
                },
                indent=2,
            )
        )
        return
    bundle = run_experiment(manifest, Path(args.output))
    print(bundle)


if __name__ == "__main__":
    main()
