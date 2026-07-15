from __future__ import annotations

import math
import random
import time
from dataclasses import dataclass
from typing import Any

from .faults import ScenarioState, scenario_state
from .models import AxisState, MachineState, Order, ProductRecipe, Scenario


@dataclass
class StepOutput:
    telemetry: dict[str, Any]
    production_events: list[dict[str, Any]]
    logs: list[dict[str, Any]]
    network_publish_due: bool
    scenario_states: list[ScenarioState]


class DynamicGantryEnvironment:
    def __init__(self, manifest: dict[str, Any], seed: int, episode_id: str):
        self.manifest = manifest
        self.seed = seed
        self.episode_id = episode_id
        self.rng = random.Random(seed)
        self.dt = float(manifest["environment"].get("dt_ms", 20.0)) / 1000.0
        self.duration_s = float(manifest["environment"].get("duration_s", 60.0))
        self.time_s = 0.0
        self.step_index = 0
        self.state = MachineState.IDLE
        self.state_elapsed_s = 0.0
        self.axes = {"x": AxisState("x"), "y": AxisState("y")}
        self.input_queue: list[dict[str, Any]] = []
        self.output_count = 0
        self.rework_count = 0
        self.current_part: dict[str, Any] | None = None
        self.current_cycle_id: str | None = None
        self.cycle_count = 0
        self.good_count = 0
        self.rejected_count = 0
        self.failed_pick_count = 0
        self.last_product_id: str | None = None
        self.active_order: Order | None = None
        self.orders = self._build_orders(manifest.get("production", {}))
        self.recipes = self._build_recipes(manifest.get("production", {}))
        self.next_arrival_s = 0.0
        self.arrival_index = 0
        network_config = manifest.get("network", {})
        self.network_period_s = (
            float(
                network_config.get("cycle_period_ms", network_config.get("publish_period_ms", 100))
            )
            / 1000
        )
        self.next_network_publish_s = 0.0
        self.next_maintenance_s = float(
            manifest.get("production", {}).get("maintenance_interval_s", 1e12)
        )
        self.maintenance_duration_s = float(
            manifest.get("production", {}).get("maintenance_duration_s", 3.0)
        )
        self.maintenance_remaining_s = 0.0
        self.scenarios: list[Scenario] = []
        self._events: list[dict[str, Any]] = []
        self._logs: list[dict[str, Any]] = []

    def _build_recipes(self, production: dict[str, Any]) -> dict[str, ProductRecipe]:
        raw = production.get("products") or [
            {"id": "P1", "payload_kg": 1.0, "inspect_s": 0.35},
            {"id": "P2", "payload_kg": 1.8, "inspect_s": 0.55},
        ]
        recipes: dict[str, ProductRecipe] = {}
        for index, item in enumerate(raw):
            product_id = str(item["id"])
            recipes[product_id] = ProductRecipe(
                product_id=product_id,
                payload_kg=float(item.get("payload_kg", 1.0)),
                pick=(0.75, 0.25 + 0.05 * index),
                inspect=(0.15, 0.85),
                place=(-0.65, 0.55 + 0.05 * index),
                inspect_s=float(item.get("inspect_s", 0.4)),
                changeover_s=float(item.get("changeover_s", 0.8)),
                quality_threshold=float(item.get("quality_threshold", 0.94)),
            )
        return recipes

    def _build_orders(self, production: dict[str, Any]) -> list[Order]:
        raw_orders = production.get("orders") or [
            {"id": "ORDER-P1", "product_id": "P1", "quantity": 20, "priority": 2},
            {"id": "ORDER-P2", "product_id": "P2", "quantity": 15, "priority": 1},
        ]
        orders = [
            Order(
                order_id=str(item["id"]),
                product_id=str(item["product_id"]),
                quantity=int(item["quantity"]),
                priority=int(item.get("priority", 1)),
                due_time_s=float(item.get("due_time_s", 1e9)),
            )
            for item in raw_orders
        ]
        return sorted(orders, key=lambda order: (-order.priority, order.due_time_s))

    def set_scenarios(self, scenarios: list[Scenario]) -> None:
        self.scenarios = scenarios

    def _scenario_states(self) -> list[ScenarioState]:
        return [scenario_state(s, self.time_s, self.duration_s) for s in self.scenarios]

    def _machine_effects(self, states: list[ScenarioState]) -> dict[str, float | bool]:
        effects: dict[str, float | bool] = {
            "x_friction": 0.12,
            "y_friction": 0.12,
            "x_sensor_bias": 0.0,
            "y_sensor_bias": 0.0,
            "drive_authority": 1.0,
            "failed_pick": False,
        }
        for state in states:
            if not state.active or state.scenario.domain.value != "machine":
                continue
            scenario = state.scenario
            intensity = state.intensity
            if scenario.fault_type == "increased_friction":
                key = "x_friction" if scenario.component.startswith("x") else "y_friction"
                effects[key] = float(effects[key]) + intensity
            elif scenario.fault_type == "encoder_drift":
                key = "x_sensor_bias" if scenario.component.startswith("x") else "y_sensor_bias"
                effects[key] = intensity * max(self.time_s - scenario.activation_time_s, 0.0) / 20.0
            elif scenario.fault_type == "drive_derating":
                effects["drive_authority"] = max(0.2, 1.0 - intensity)
            elif scenario.fault_type == "failed_pick" and self.state == MachineState.PICK:
                effects["failed_pick"] = self.rng.random() < min(0.95, intensity)
        return effects

    def _add_event(self, event_type: str, **attributes: Any) -> None:
        self._events.append(
            {
                "event_type": event_type,
                "simulation_time_ns": int(self.time_s * 1e9),
                "machine_state": self.state.value,
                "cycle_id": self.current_cycle_id,
                "part_id": None if self.current_part is None else self.current_part["part_id"],
                **attributes,
            }
        )

    def _log(self, severity: str, event_code: str, message: str, **attributes: Any) -> None:
        self._logs.append(
            {
                "simulation_time_ns": int(self.time_s * 1e9),
                "severity": severity,
                "event_code": event_code,
                "message": message,
                "machine_state": self.state.value,
                "cycle_id": self.current_cycle_id,
                **attributes,
            }
        )

    def _select_order(self) -> None:
        if self.active_order and self.active_order.produced < self.active_order.quantity:
            return
        self.active_order = next((o for o in self.orders if o.produced < o.quantity), None)
        if self.active_order:
            self._add_event("order.activated", order_id=self.active_order.order_id)

    def _generate_arrivals(self) -> None:
        production = self.manifest.get("production", {})
        mean = float(production.get("mean_arrival_interval_s", 0.8))
        capacity = int(production.get("input_buffer_capacity", 12))
        while self.time_s >= self.next_arrival_s:
            self._select_order()
            if self.active_order is None:
                return
            if len(self.input_queue) < capacity:
                self.arrival_index += 1
                part = {
                    "part_id": f"{self.episode_id}-PART-{self.arrival_index:05d}",
                    "product_id": self.active_order.product_id,
                    "order_id": self.active_order.order_id,
                    "arrival_time_s": self.time_s,
                }
                self.input_queue.append(part)
                self._add_event("part.arrived", **part, input_queue=len(self.input_queue))
            interval = max(0.15, self.rng.expovariate(1.0 / mean))
            self.next_arrival_s += interval

    def _set_state(self, state: MachineState) -> None:
        if state != self.state:
            previous = self.state
            self.state = state
            self.state_elapsed_s = 0.0
            self._add_event("machine.state_changed", previous=previous.value, current=state.value)

    def _target(self) -> tuple[float, float]:
        if self.current_part is None:
            return 0.0, 0.0
        recipe = self.recipes[self.current_part["product_id"]]
        if self.state == MachineState.MOVE_TO_PICK:
            return recipe.pick
        if self.state == MachineState.MOVE_TO_INSPECT:
            return recipe.inspect
        if self.state == MachineState.MOVE_TO_PLACE:
            return recipe.place
        return self.axes["x"].command_position, self.axes["y"].command_position

    def _update_axis(
        self, axis: AxisState, target: float, payload_kg: float, friction: float, authority: float
    ) -> None:
        axis.command_position = target
        measured = axis.actual_position + axis.sensor_bias
        error = target - measured
        axis.integral = max(-2.0, min(2.0, axis.integral + error * self.dt))
        derivative = (error - axis.previous_error) / self.dt
        effort = 12.0 * error + 1.2 * axis.integral + 0.6 * derivative
        effort = max(-8.0, min(8.0, effort)) * authority
        damping = 1.6 + friction * 2.0
        mass = 1.0 + payload_kg * 0.35
        acceleration = (
            effort - damping * axis.velocity - friction * math.tanh(axis.velocity * 20)
        ) / mass
        axis.velocity += acceleration * self.dt
        axis.actual_position += axis.velocity * self.dt
        axis.control_effort = effort
        axis.previous_error = error

    def _at_target(self, tolerance: float = 0.025) -> bool:
        return all(
            abs(axis.command_position - (axis.actual_position + axis.sensor_bias)) < tolerance
            and abs(axis.velocity) < 0.08
            for axis in self.axes.values()
        )

    def _advance_sequence(self, effects: dict[str, float | bool]) -> None:
        if self.maintenance_remaining_s > 0:
            self.maintenance_remaining_s -= self.dt
            self._set_state(MachineState.MAINTENANCE)
            if self.maintenance_remaining_s <= 0:
                self._add_event("maintenance.completed")
                self.next_maintenance_s += float(
                    self.manifest.get("production", {}).get("maintenance_interval_s", 1e12)
                )
                self._set_state(MachineState.IDLE)
            return

        if self.time_s >= self.next_maintenance_s and self.state == MachineState.IDLE:
            self.maintenance_remaining_s = self.maintenance_duration_s
            self._add_event("maintenance.started")
            self._set_state(MachineState.MAINTENANCE)
            return

        if self.state == MachineState.IDLE:
            if not self.input_queue:
                return
            self.current_part = self.input_queue.pop(0)
            self.current_cycle_id = f"{self.episode_id}-CYCLE-{self.cycle_count + 1:05d}"
            if self.last_product_id != self.current_part["product_id"]:
                self._set_state(MachineState.CHANGEOVER)
            else:
                self._set_state(MachineState.MOVE_TO_PICK)
            self._add_event("cycle.started", order_id=self.current_part["order_id"])
            return

        recipe = self.recipes[self.current_part["product_id"]] if self.current_part else None
        if recipe is None:
            return

        if self.state == MachineState.CHANGEOVER and self.state_elapsed_s >= recipe.changeover_s:
            self.last_product_id = recipe.product_id
            self._add_event("recipe.changed", product_id=recipe.product_id)
            self._set_state(MachineState.MOVE_TO_PICK)
        elif self.state == MachineState.MOVE_TO_PICK and self._at_target():
            self._set_state(MachineState.PICK)
        elif self.state == MachineState.PICK and self.state_elapsed_s >= 0.25:
            if bool(effects["failed_pick"]):
                self.failed_pick_count += 1
                self.rework_count += 1
                self._log("warning", "PICK_FAILED", "Pick verification did not confirm a part")
                self._complete_cycle(good=False, reason="failed_pick")
            else:
                self._add_event("part.picked")
                self._set_state(MachineState.MOVE_TO_INSPECT)
        elif self.state == MachineState.MOVE_TO_INSPECT and self._at_target():
            self._set_state(MachineState.INSPECT)
        elif self.state == MachineState.INSPECT and self.state_elapsed_s >= recipe.inspect_s:
            position_error = sum(
                abs(a.command_position - (a.actual_position + a.sensor_bias))
                for a in self.axes.values()
            )
            score = max(0.0, 1.0 - position_error * 3.0 - self.rng.random() * 0.02)
            self.current_part["quality_score"] = score
            self._add_event("inspection.completed", quality_score=score)
            self._set_state(MachineState.MOVE_TO_PLACE)
        elif self.state == MachineState.MOVE_TO_PLACE and self._at_target():
            self._set_state(MachineState.PLACE)
        elif self.state == MachineState.PLACE and self.state_elapsed_s >= 0.25:
            score = float(self.current_part.get("quality_score", 0.0))
            self._complete_cycle(good=score >= recipe.quality_threshold, reason="quality")

    def _complete_cycle(self, good: bool, reason: str) -> None:
        if self.current_part is None:
            return
        self.cycle_count += 1
        if good:
            self.good_count += 1
            self.output_count += 1
        else:
            self.rejected_count += 1
            self.rework_count += 1
        if self.active_order:
            self.active_order.produced += 1
        self._add_event(
            "cycle.completed",
            good=good,
            reason=reason,
            output_count=self.output_count,
            rework_count=self.rework_count,
        )
        self.current_part = None
        self.current_cycle_id = None
        self._set_state(MachineState.IDLE)

    def step(self) -> StepOutput:
        started = time.perf_counter_ns()
        self._events = []
        self._logs = []
        self._generate_arrivals()
        scenario_states = self._scenario_states()
        effects = self._machine_effects(scenario_states)

        self.axes["x"].sensor_bias = float(effects["x_sensor_bias"])
        self.axes["y"].sensor_bias = float(effects["y_sensor_bias"])
        target_x, target_y = self._target()
        payload = 0.0
        if self.current_part:
            payload = self.recipes[self.current_part["product_id"]].payload_kg
        self._update_axis(
            self.axes["x"],
            target_x,
            payload,
            float(effects["x_friction"]),
            float(effects["drive_authority"]),
        )
        self._update_axis(
            self.axes["y"],
            target_y,
            payload,
            float(effects["y_friction"]),
            float(effects["drive_authority"]),
        )
        self._advance_sequence(effects)

        max_following_error = max(
            abs(axis.command_position - (axis.actual_position + axis.sensor_bias))
            for axis in self.axes.values()
        )
        if max_following_error > 0.38:
            self._log(
                "error",
                "FOLLOWING_ERROR",
                "Axis following error exceeded the operational threshold",
            )

        telemetry = {
            "simulation_time_ns": int(self.time_s * 1e9),
            "step_index": self.step_index,
            "machine_state": self.state.value,
            "production_step": self.state.value,
            "cycle_id": self.current_cycle_id,
            "part_id": None if self.current_part is None else self.current_part["part_id"],
            "product_id": None if self.current_part is None else self.current_part["product_id"],
            "order_id": None if self.current_part is None else self.current_part["order_id"],
            "input_queue": len(self.input_queue),
            "output_count": self.output_count,
            "rework_count": self.rework_count,
            "cycle_count": self.cycle_count,
            "good_count": self.good_count,
            "rejected_count": self.rejected_count,
            "payload_kg": payload,
            "x_command_position": self.axes["x"].command_position,
            "x_actual_position": self.axes["x"].actual_position,
            "x_measured_position": self.axes["x"].actual_position + self.axes["x"].sensor_bias,
            "x_velocity": self.axes["x"].velocity,
            "x_control_effort": self.axes["x"].control_effort,
            "x_following_error": self.axes["x"].command_position
            - (self.axes["x"].actual_position + self.axes["x"].sensor_bias),
            "y_command_position": self.axes["y"].command_position,
            "y_actual_position": self.axes["y"].actual_position,
            "y_measured_position": self.axes["y"].actual_position + self.axes["y"].sensor_bias,
            "y_velocity": self.axes["y"].velocity,
            "y_control_effort": self.axes["y"].control_effort,
            "y_following_error": self.axes["y"].command_position
            - (self.axes["y"].actual_position + self.axes["y"].sensor_bias),
            "max_following_error": max_following_error,
            "step_wall_duration_ns": time.perf_counter_ns() - started,
        }
        network_due = self.time_s >= self.next_network_publish_s
        if network_due:
            self.next_network_publish_s += self.network_period_s
        self.time_s += self.dt
        self.state_elapsed_s += self.dt
        self.step_index += 1
        return StepOutput(
            telemetry, list(self._events), list(self._logs), network_due, scenario_states
        )
