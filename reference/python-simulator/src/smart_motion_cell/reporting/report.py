from __future__ import annotations

import csv
import json
import sqlite3
from html import escape
from pathlib import Path

from smart_motion_cell.manufacturing.oee import calculate_oee


def generate_report(
    database: str | Path, output_dir: str | Path, run_id: str | None = None
) -> Path:
    database_path = Path(database)
    output_path = Path(output_dir)
    output_path.mkdir(parents=True, exist_ok=True)
    with sqlite3.connect(database_path) as connection:
        connection.row_factory = sqlite3.Row
        selected_run = run_id or _latest_run_id(connection)
        if selected_run is None:
            raise ValueError("database contains no runs")
        run = dict(
            connection.execute("SELECT * FROM runs WHERE run_id = ?", (selected_run,)).fetchone()
        )
        cycles = [
            dict(r)
            for r in connection.execute(
                "SELECT * FROM cycles WHERE run_id = ? ORDER BY cycle_number", (selected_run,)
            )
        ]
        events = [
            dict(r)
            for r in connection.execute(
                "SELECT * FROM events WHERE run_id = ? ORDER BY id", (selected_run,)
            )
        ]
        samples = [
            dict(r)
            for r in connection.execute(
                "SELECT * FROM samples WHERE run_id = ? ORDER BY sim_time, axis", (selected_run,)
            )
        ]

    summary = _summary(run, cycles, samples)
    (output_path / "summary.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
    _write_csv(output_path / "cycles.csv", cycles)
    _write_csv(output_path / "events.csv", events)
    svg = _tracking_svg(samples)
    (output_path / "tracking.svg").write_text(svg, encoding="utf-8")
    html = _html_report(run, summary, cycles, events, svg)
    report_path = output_path / "index.html"
    report_path.write_text(html, encoding="utf-8")
    return report_path


def _latest_run_id(connection: sqlite3.Connection) -> str | None:
    row = connection.execute("SELECT run_id FROM runs ORDER BY rowid DESC LIMIT 1").fetchone()
    return None if row is None else str(row[0])


def _summary(
    run: dict[str, object], cycles: list[dict[str, object]], samples: list[dict[str, object]]
) -> dict[str, object]:
    errors = [abs(float(row["following_error"])) for row in samples]
    total = len(cycles)
    good = sum(1 for row in cycles if row["result"] == "good")
    if cycles:
        start = min(float(row["start_time"]) for row in cycles)
        end = max(float(row["end_time"]) for row in cycles)
        run_time = max(end - start, 1e-9)
        avg_cycle = sum(float(row["end_time"]) - float(row["start_time"]) for row in cycles) / total
        config = json.loads(str(run["config_json"]))
        ideal_cycle = float(config["recipe"]["ideal_cycle_seconds"])
        oee = calculate_oee(run_time, run_time, ideal_cycle, total, good)
        oee_data = {
            "availability": oee.availability,
            "performance": oee.performance,
            "quality": oee.quality,
            "oee": oee.oee,
        }
    else:
        avg_cycle = 0.0
        oee_data = {"availability": 0.0, "performance": 0.0, "quality": 0.0, "oee": 0.0}
    return {
        "run_id": run["run_id"],
        "scenario": run["scenario"],
        "status": run["status"],
        "cycles": total,
        "good_cycles": good,
        "average_cycle_seconds": avg_cycle,
        "max_abs_following_error": max(errors, default=0.0),
        "oee": oee_data,
    }


def _write_csv(path: Path, rows: list[dict[str, object]]) -> None:
    if not rows:
        path.write_text("", encoding="utf-8")
        return
    with path.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)


def _tracking_svg(samples: list[dict[str, object]]) -> str:
    width, height = 920, 360
    margin_left, margin_right, margin_top, margin_bottom = 60, 25, 35, 50
    plot_w = width - margin_left - margin_right
    plot_h = height - margin_top - margin_bottom
    if not samples:
        return f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}"><text x="20" y="40">No samples</text></svg>'
    times = [float(row["sim_time"]) for row in samples]
    values = [float(row["position"]) for row in samples] + [float(row["target"]) for row in samples]
    t_min, t_max = min(times), max(times)
    v_min, v_max = min(values), max(values)
    if t_max == t_min:
        t_max += 1.0
    if v_max == v_min:
        v_max += 1.0
    pad = max(0.05, (v_max - v_min) * 0.08)
    v_min -= pad
    v_max += pad

    def xy(time_value: float, position: float) -> tuple[float, float]:
        x = margin_left + (time_value - t_min) / (t_max - t_min) * plot_w
        y = margin_top + (v_max - position) / (v_max - v_min) * plot_h
        return x, y

    series: list[tuple[str, str, str]] = []
    for axis, stroke, _dash in [("X", "#2563eb", ""), ("Y", "#f97316", "")]:
        axis_rows = [row for row in samples if row["axis"] == axis]
        pos_points = " ".join(
            f"{x:.1f},{y:.1f}"
            for x, y in (xy(float(r["sim_time"]), float(r["position"])) for r in axis_rows)
        )
        target_points = " ".join(
            f"{x:.1f},{y:.1f}"
            for x, y in (xy(float(r["sim_time"]), float(r["target"])) for r in axis_rows)
        )
        series.append((f"{axis} actual", stroke, pos_points))
        series.append((f"{axis} target", stroke, target_points))

    lines = [
        f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {width} {height}" role="img" aria-label="Axis target and actual position over simulated time">',
        '<rect width="100%" height="100%" fill="#ffffff" rx="12"/>',
        '<text x="60" y="23" font-family="system-ui" font-size="16" font-weight="700" fill="#111827">Axis tracking — target vs actual</text>',
    ]
    for index in range(6):
        y = margin_top + index * plot_h / 5
        value = v_max - index * (v_max - v_min) / 5
        lines.append(
            f'<line x1="{margin_left}" y1="{y:.1f}" x2="{width - margin_right}" y2="{y:.1f}" stroke="#e5e7eb"/>'
        )
        lines.append(
            f'<text x="{margin_left - 8}" y="{y + 4:.1f}" text-anchor="end" font-family="system-ui" font-size="11" fill="#6b7280">{value:.2f}</text>'
        )
    for index in range(7):
        x = margin_left + index * plot_w / 6
        value = t_min + index * (t_max - t_min) / 6
        lines.append(
            f'<text x="{x:.1f}" y="{height - 22}" text-anchor="middle" font-family="system-ui" font-size="11" fill="#6b7280">{value:.1f}</text>'
        )
    lines.append(
        f'<text x="{width / 2:.1f}" y="{height - 5}" text-anchor="middle" font-family="system-ui" font-size="11" fill="#6b7280">Simulated time (s)</text>'
    )
    for idx, (label, stroke, points) in enumerate(series):
        dash = ' stroke-dasharray="7 5"' if "target" in label else ""
        opacity = ' opacity="0.65"' if "target" in label else ""
        lines.append(
            f'<polyline points="{points}" fill="none" stroke="{stroke}" stroke-width="2"{dash}{opacity}/>'
        )
        legend_x = 610 + (idx % 2) * 140
        legend_y = 20 + (idx // 2) * 16
        lines.append(
            f'<line x1="{legend_x}" y1="{legend_y}" x2="{legend_x + 22}" y2="{legend_y}" stroke="{stroke}" stroke-width="2"{dash}/>'
        )
        lines.append(
            f'<text x="{legend_x + 28}" y="{legend_y + 4}" font-family="system-ui" font-size="11" fill="#374151">{label}</text>'
        )
    lines.append("</svg>")
    return "\n".join(lines)


def _html_report(
    run: dict[str, object],
    summary: dict[str, object],
    cycles: list[dict[str, object]],
    events: list[dict[str, object]],
    svg: str,
) -> str:
    oee = summary["oee"]
    assert isinstance(oee, dict)
    cycle_rows = (
        "".join(
            f"<tr><td>{int(r['cycle_number'])}</td><td>{escape(str(r['part_id']))}</td><td>{float(r['end_time']) - float(r['start_time']):.3f}s</td><td>{escape(str(r['result']))}</td></tr>"
            for r in cycles
        )
        or '<tr><td colspan="4">No completed cycles</td></tr>'
    )
    event_rows = (
        "".join(
            f"<tr><td>{float(r['sim_time']):.3f}</td><td>{escape(str(r['severity']))}</td><td>{escape(str(r['event_type']))}</td><td>{escape(str(r['message']))}</td></tr>"
            for r in events[-20:]
        )
        or '<tr><td colspan="4">No events</td></tr>'
    )
    return f"""<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Virtual Smart Motion Cell — run {escape(str(run["run_id"]))}</title>
<style>
:root{{--ink:#111827;--muted:#6b7280;--line:#e5e7eb;--panel:#fff;--bg:#f3f4f6;--accent:#2563eb}}
*{{box-sizing:border-box}} body{{margin:0;background:var(--bg);color:var(--ink);font:15px/1.55 system-ui,sans-serif}}
main{{max-width:1100px;margin:auto;padding:36px 20px 60px}} h1{{margin:0}} .subtitle{{color:var(--muted);margin-top:5px}}
.grid{{display:grid;grid-template-columns:repeat(auto-fit,minmax(155px,1fr));gap:12px;margin:24px 0}}
.card,.panel{{background:var(--panel);border:1px solid var(--line);border-radius:14px;padding:18px;box-shadow:0 4px 16px #1118270a}}
.metric{{font-size:26px;font-weight:750}} .label{{color:var(--muted);font-size:12px;text-transform:uppercase;letter-spacing:.06em}}
.panel{{margin-top:16px;overflow:auto}} table{{width:100%;border-collapse:collapse}} th,td{{text-align:left;padding:9px;border-bottom:1px solid var(--line)}} th{{font-size:12px;text-transform:uppercase;color:var(--muted)}}
.badge{{display:inline-block;padding:3px 9px;border-radius:99px;background:#dbeafe;color:#1d4ed8;font-weight:650}}
</style></head><body><main>
<span class="badge">simulation evidence</span><h1>Virtual Smart Motion Cell</h1>
<p class="subtitle">Run {escape(str(run["run_id"]))} · scenario {escape(str(run["scenario"]))} · status {escape(str(run["status"]))}</p>
<section class="grid">
<div class="card"><div class="label">Completed cycles</div><div class="metric">{summary["cycles"]}</div></div>
<div class="card"><div class="label">Good cycles</div><div class="metric">{summary["good_cycles"]}</div></div>
<div class="card"><div class="label">Average cycle</div><div class="metric">{float(summary["average_cycle_seconds"]):.2f}s</div></div>
<div class="card"><div class="label">Max following error</div><div class="metric">{float(summary["max_abs_following_error"]):.3f}</div></div>
<div class="card"><div class="label">OEE</div><div class="metric">{float(oee["oee"]) * 100:.1f}%</div></div>
</section>
<section class="panel">{svg}</section>
<section class="panel"><h2>Cycle traceability</h2><table><thead><tr><th>Cycle</th><th>Part</th><th>Duration</th><th>Result</th></tr></thead><tbody>{cycle_rows}</tbody></table></section>
<section class="panel"><h2>Recent events</h2><table><thead><tr><th>Time</th><th>Severity</th><th>Type</th><th>Message</th></tr></thead><tbody>{event_rows}</tbody></table></section>
</main></body></html>"""
