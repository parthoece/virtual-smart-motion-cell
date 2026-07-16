#!/usr/bin/env python3
"""Initialize and start one production cycle in the Docker Compose stack."""
from __future__ import annotations

import json
import os
import time
import urllib.error
import urllib.request
from typing import Any, Callable

API = os.environ.get("VSMC_API_URL", "http://machine-runtime:8080").rstrip("/")
TIMEOUT_S = float(os.environ.get("VSMC_BOOTSTRAP_TIMEOUT_S", "120"))


def request(method: str, path: str, payload: dict[str, Any] | None = None) -> tuple[int, Any]:
    body = None if payload is None else json.dumps(payload).encode("utf-8")
    req = urllib.request.Request(f"{API}{path}", data=body, method=method)
    req.add_header("X-Correlation-ID", "compose-bootstrap")
    if body is not None:
        req.add_header("Content-Type", "application/json")
    try:
        with urllib.request.urlopen(req, timeout=5) as response:
            raw = response.read()
            return response.status, json.loads(raw) if raw else None
    except urllib.error.HTTPError as exc:
        raw = exc.read()
        value = json.loads(raw) if raw else {"error": str(exc)}
        return exc.code, value


def wait_until(action: Callable[[], Any], predicate: Callable[[Any], bool], label: str) -> Any:
    deadline = time.monotonic() + TIMEOUT_S
    last: Any = None
    while time.monotonic() < deadline:
        try:
            last = action()
            if predicate(last):
                return last
        except Exception as exc:  # startup diagnostics
            last = repr(exc)
        time.sleep(0.25)
    raise RuntimeError(f"Timed out waiting for {label}; last value={last!r}")


def state() -> dict[str, Any]:
    status, value = request("GET", "/api/v1/state")
    if status != 200 or not isinstance(value, dict):
        raise RuntimeError(f"State endpoint returned {status}: {value}")
    return value


def command(command_type: str, **fields: Any) -> dict[str, Any]:
    payload = {
        "type": command_type,
        "requestedBy": "compose-bootstrap",
        "correlationId": "compose-bootstrap",
        **fields,
    }
    status, value = request("POST", "/api/v1/commands", payload)
    if status != 202 or not isinstance(value, dict) or value.get("status") != "Accepted":
        raise RuntimeError(f"Command {command_type!r} failed: HTTP {status}: {value}")
    return value


def main() -> int:
    wait_until(
        lambda: request("GET", "/health/ready"),
        lambda result: result[0] == 200,
        "machine runtime readiness",
    )

    current = state()
    execution = current.get("executionState")
    if execution == "RecoveryRequired":
        command("recover-reset")
        current = wait_until(state, lambda value: value.get("executionState") == "Stopped", "recovery reset")
        execution = current.get("executionState")
    elif execution == "Faulted":
        command("clear-fault", fault="all")
        command("acknowledge-alarms")
        command("reset")
        current = wait_until(
            state,
            lambda value: value.get("executionState") in {"Stopped", "Ready"},
            "fault reset",
        )
        execution = current.get("executionState")
    elif execution in {"Running", "Starting", "Paused", "Homing"}:
        command("abort")
        current = wait_until(state, lambda value: value.get("executionState") == "Stopped", "abort")
        execution = current.get("executionState")

    if execution == "Stopped":
        command("initialize")
        current = wait_until(state, lambda value: value.get("executionState") == "Stopped", "initialization")

    axes_homed = bool(current.get("xAxis", {}).get("homed")) and bool(current.get("yAxis", {}).get("homed"))
    if not axes_homed or current.get("executionState") != "Ready":
        command("home")
        current = wait_until(
            state,
            lambda value: value.get("executionState") == "Ready"
            and value.get("xAxis", {}).get("homed")
            and value.get("yAxis", {}).get("homed"),
            "homing",
        )

    active_order = current.get("production", {}).get("activeOrder")
    if not active_order:
        order_id = f"COMPOSE-DEMO-{int(time.time())}"
        command(
            "load-order",
            orderId=order_id,
            quantity=3,
            recipeId="standard-widget",
            recipeRevision=1,
        )
        current = state()

    if current.get("mode") != "Automatic":
        command("set-mode", mode="Automatic")
        current = state()

    initial_cycles = int(current.get("production", {}).get("cycleCount", 0))
    command("start")
    final = wait_until(
        state,
        lambda value: int(value.get("production", {}).get("cycleCount", 0)) > initial_cycles,
        "one completed production cycle",
    )
    print(
        json.dumps(
            {
                "status": "machine-demo-running",
                "executionState": final.get("executionState"),
                "cycleCount": final.get("production", {}).get("cycleCount"),
                "activeOrder": final.get("production", {}).get("activeOrder"),
            },
            indent=2,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
