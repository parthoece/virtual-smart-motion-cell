#!/usr/bin/env python3
"""Drive the public API for a target cycle count and optional wall-clock soak duration."""
from __future__ import annotations

import argparse
import json
import time
import urllib.request
from pathlib import Path


def request(url: str, data: dict | None = None):
    body = None if data is None else json.dumps(data).encode()
    req = urllib.request.Request(
        url,
        data=body,
        headers={"content-type": "application/json", "X-Correlation-ID": "http-soak"},
    )
    with urllib.request.urlopen(req, timeout=5) as response:
        payload = response.read()
        return json.loads(payload) if payload else None


def command(base: str, payload: dict) -> None:
    result = request(base + "/api/v1/commands", {"requestedBy": "http-soak", **payload})
    if result and result.get("status") != "Accepted":
        raise RuntimeError(f"Command rejected: {result}")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--url", default="http://localhost:8080")
    parser.add_argument("--cycles", type=int, default=100)
    parser.add_argument("--duration-minutes", type=float, default=0)
    parser.add_argument("--output", default="artifacts/soak-report.json")
    args = parser.parse_args()

    command(args.url, {"type": "initialize"})
    time.sleep(0.2)
    command(args.url, {"type": "home"})
    deadline = time.monotonic() + 60
    while time.monotonic() < deadline:
        state = request(args.url + "/api/v1/state")
        if state["executionState"] == "Ready":
            break
        time.sleep(0.1)
    else:
        raise TimeoutError("Machine did not reach Ready after homing")

    command(args.url, {
        "type": "load-order",
        "orderId": "HTTP-SOAK-001",
        "quantity": max(1, args.cycles),
        "recipeId": "standard-widget",
        "recipeRevision": 1,
    })
    command(args.url, {"type": "set-mode", "mode": "Automatic"})
    command(args.url, {"type": "start"})

    started = time.monotonic()
    target_duration = args.duration_minutes * 60
    last = None
    samples: list[dict] = []
    while True:
        last = request(args.url + "/api/v1/state")
        elapsed = time.monotonic() - started
        if len(samples) < 2_000:
            samples.append({
                "elapsedSeconds": elapsed,
                "cycles": last["production"]["cycleCount"],
                "loopMs": last["runtime"]["lastLoopDurationMilliseconds"],
                "workingState": last["executionState"],
            })
        cycles_done = last["production"]["cycleCount"] >= args.cycles
        duration_done = target_duration <= 0 or elapsed >= target_duration
        if cycles_done and duration_done:
            break
        if last["executionState"] == "Faulted":
            raise RuntimeError(f"Soak campaign faulted: {last['activeAlarms']}")
        time.sleep(0.25)

    report = {
        "targetCycles": args.cycles,
        "durationMinutes": args.duration_minutes,
        "elapsedSeconds": time.monotonic() - started,
        "samples": samples,
        "finalState": last,
    }
    path = Path(args.output)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(report, indent=2))
    print(path)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
