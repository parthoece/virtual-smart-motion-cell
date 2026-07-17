#!/usr/bin/env python3
"""Dependency-free end-to-end smoke test for the API, MES simulator, WebSocket, and OPC UA listener."""
from __future__ import annotations

import base64
import json
import os
import socket
import struct
import time
import urllib.error
import urllib.request
from urllib.parse import urlparse

API = os.getenv("VSMC_SMOKE_API", "http://127.0.0.1:18080")
MES = os.getenv("VSMC_SMOKE_MES", "http://127.0.0.1:18090")
OPC_HOST = os.getenv("VSMC_SMOKE_OPC_HOST", "127.0.0.1")
OPC_PORT = int(os.getenv("VSMC_SMOKE_OPC_PORT", "14840"))

API_ADDRESS = urlparse(API)
API_HOST = API_ADDRESS.hostname or "127.0.0.1"
API_PORT = API_ADDRESS.port or 80


def request(method: str, url: str, payload: object | None = None, timeout: float = 3.0):
    body = None if payload is None else json.dumps(payload).encode()
    req = urllib.request.Request(url, data=body, method=method)
    if body is not None:
        req.add_header("Content-Type", "application/json")
    req.add_header("X-Correlation-ID", "ci-smoke")
    with urllib.request.urlopen(req, timeout=timeout) as response:
        data = response.read()
        return response.status, json.loads(data) if data else None


def wait_http(url: str, expected=(200,), timeout: float = 45.0):
    deadline = time.monotonic() + timeout
    last = None
    while time.monotonic() < deadline:
        try:
            status, value = request("GET", url)
            if status in expected:
                return value
            last = f"HTTP {status}"
        except Exception as exc:  # smoke diagnostic
            last = repr(exc)
        time.sleep(0.25)
    raise RuntimeError(f"Timed out waiting for {url}: {last}")


def command(command_type: str, **fields):
    payload = {"type": command_type, "requestedBy": "ci-smoke", "correlationId": "ci-smoke", **fields}
    status, result = request("POST", f"{API}/api/v1/commands", payload)
    if status != 202 or result.get("status") != "Accepted":
        raise RuntimeError(f"Command {command_type} failed: {status} {result}")
    return result


def wait_state(predicate, label: str, timeout: float = 45.0):
    deadline = time.monotonic() + timeout
    last = None
    while time.monotonic() < deadline:
        try:
            _, state = request("GET", f"{API}/api/v1/state")
            last = state
            if predicate(state):
                return state
        except Exception:
            pass
        time.sleep(0.1)
    raise RuntimeError(f"Timed out waiting for {label}; last state={last}")


def check_tcp(host: str, port: int, timeout: float = 20.0):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        try:
            with socket.create_connection((host, port), timeout=1):
                return
        except OSError:
            time.sleep(0.25)
    raise RuntimeError(f"TCP listener {host}:{port} did not open")


def websocket_snapshot() -> dict:
    key = base64.b64encode(os.urandom(16)).decode()
    with socket.create_connection((API_HOST, API_PORT), timeout=5) as sock:
        sock.settimeout(8)
        request_text = (
            "GET /ws/state HTTP/1.1\r\n"
            f"Host: {API_HOST}:{API_PORT}\r\n"
            "Upgrade: websocket\r\n"
            "Connection: Upgrade\r\n"
            f"Sec-WebSocket-Key: {key}\r\n"
            "Sec-WebSocket-Version: 13\r\n\r\n"
        )
        sock.sendall(request_text.encode("ascii"))
        response = b""
        while b"\r\n\r\n" not in response:
            response += sock.recv(4096)
        if b" 101 " not in response.split(b"\r\n", 1)[0]:
            raise RuntimeError(f"WebSocket handshake failed: {response[:200]!r}")

        header = sock.recv(2)
        if len(header) != 2:
            raise RuntimeError("WebSocket frame header was incomplete")
        first, second = header
        opcode = first & 0x0F
        length = second & 0x7F
        if length == 126:
            length = struct.unpack("!H", sock.recv(2))[0]
        elif length == 127:
            length = struct.unpack("!Q", sock.recv(8))[0]
        if second & 0x80:
            mask = sock.recv(4)
        else:
            mask = None
        payload = bytearray()
        while len(payload) < length:
            payload.extend(sock.recv(length - len(payload)))
        if mask:
            payload = bytearray(value ^ mask[index % 4] for index, value in enumerate(payload))
        if opcode != 1:
            raise RuntimeError(f"Expected a text WebSocket frame, got opcode {opcode}")
        return json.loads(payload.decode())


def main() -> int:
    wait_http(f"{MES}/health/live")
    wait_http(f"{API}/health/live")
    wait_http(f"{API}/health/ready", timeout=60)
    check_tcp(OPC_HOST, OPC_PORT)

    status, _ = request("POST", f"{MES}/api/v1/orders", {
        "orderId": "CI-ORDER-001", "quantity": 1,
        "recipeId": "standard-widget", "recipeRevision": 1,
    })
    if status != 201:
        raise RuntimeError(f"MES order creation returned {status}")
    wait_state(lambda state: state.get("production", {}).get("activeOrder", {}).get("orderId") == "CI-ORDER-001", "MES order assignment")

    command("initialize")
    wait_state(lambda state: state.get("executionState") == "Stopped", "initialization")
    command("home")
    wait_state(lambda state: state.get("executionState") == "Ready" and state["xAxis"]["homed"] and state["yAxis"]["homed"], "homing")
    command("set-mode", mode="Automatic")
    command("start")
    final = wait_state(lambda state: state.get("production", {}).get("cycleCount", 0) >= 1 and state.get("executionState") == "Ready", "completed cycle", 90)

    ws = websocket_snapshot()
    if ws.get("revision", 0) <= 0 or "xAxis" not in ws:
        raise RuntimeError("WebSocket state did not contain a valid machine snapshot")

    results = []
    deadline = time.monotonic() + 30

    while time.monotonic() < deadline:
        try:
            _, value = request("GET", f"{MES}/api/v1/results")
            if isinstance(value, list) and value:
                results = value
                break
        except Exception:
            pass

        time.sleep(0.25)

    if not results:
        raise RuntimeError("MES did not receive the outbox result within 30 seconds")

    with urllib.request.urlopen(f"{API}/metrics", timeout=3) as response:
        metrics = response.read().decode()
    if "vsmc_production_cycles_total" not in metrics:
        raise RuntimeError("Prometheus metrics did not include production cycles")

    print(json.dumps({
        "status": "passed",
        "revision": final["revision"],
        "cycles": final["production"]["cycleCount"],
        "mesResults": len(results),
        "opcUaListener": f"{OPC_HOST}:{OPC_PORT}",
        "webSocketRevision": ws["revision"],
    }, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
