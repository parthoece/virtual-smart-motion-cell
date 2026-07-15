from __future__ import annotations

import json
from dataclasses import dataclass
from importlib.resources import files
from pathlib import Path
from typing import Any


@dataclass(frozen=True)
class AxisConfig:
    name: str
    mass: float
    damping: float
    kp: float
    ki: float
    kd: float
    command_limit: float
    max_velocity: float
    max_acceleration: float
    soft_min: float
    soft_max: float
    position_tolerance: float = 0.01
    velocity_tolerance: float = 0.02
    following_error_limit: float = 0.35

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> AxisConfig:
        return cls(**data)

    def validate(self) -> None:
        positive = {
            "mass": self.mass,
            "damping": self.damping,
            "command_limit": self.command_limit,
            "max_velocity": self.max_velocity,
            "max_acceleration": self.max_acceleration,
            "position_tolerance": self.position_tolerance,
            "velocity_tolerance": self.velocity_tolerance,
            "following_error_limit": self.following_error_limit,
        }
        for name, value in positive.items():
            if value <= 0:
                raise ValueError(f"axis {self.name}: {name} must be positive")
        if self.soft_min >= self.soft_max:
            raise ValueError(f"axis {self.name}: soft_min must be below soft_max")


@dataclass(frozen=True)
class CellRecipe:
    version: str
    home: tuple[float, float]
    pick: tuple[float, float]
    inspect: tuple[float, float]
    place: tuple[float, float]
    dwell_seconds: float
    ideal_cycle_seconds: float

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> CellRecipe:
        return cls(
            version=str(data["version"]),
            home=_point(data["home"]),
            pick=_point(data["pick"]),
            inspect=_point(data["inspect"]),
            place=_point(data["place"]),
            dwell_seconds=float(data["dwell_seconds"]),
            ideal_cycle_seconds=float(data["ideal_cycle_seconds"]),
        )

    def validate(self, axes: tuple[AxisConfig, AxisConfig]) -> None:
        if self.dwell_seconds < 0:
            raise ValueError("dwell_seconds must be non-negative")
        if self.ideal_cycle_seconds <= 0:
            raise ValueError("ideal_cycle_seconds must be positive")
        x_axis, y_axis = axes
        for name, point in {
            "home": self.home,
            "pick": self.pick,
            "inspect": self.inspect,
            "place": self.place,
        }.items():
            x, y = point
            if not x_axis.soft_min <= x <= x_axis.soft_max:
                raise ValueError(f"recipe point {name}.x is outside X soft limits")
            if not y_axis.soft_min <= y <= y_axis.soft_max:
                raise ValueError(f"recipe point {name}.y is outside Y soft limits")


@dataclass(frozen=True)
class SimulationConfig:
    cycle_time_seconds: float
    sample_every_ticks: int
    max_simulation_seconds: float
    axes: tuple[AxisConfig, AxisConfig]
    recipe: CellRecipe

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> SimulationConfig:
        axes = tuple(AxisConfig.from_dict(item) for item in data["axes"])
        if len(axes) != 2:
            raise ValueError("exactly two axes are required")
        config = cls(
            cycle_time_seconds=float(data["cycle_time_seconds"]),
            sample_every_ticks=int(data["sample_every_ticks"]),
            max_simulation_seconds=float(data["max_simulation_seconds"]),
            axes=(axes[0], axes[1]),
            recipe=CellRecipe.from_dict(data["recipe"]),
        )
        config.validate()
        return config

    def validate(self) -> None:
        if self.cycle_time_seconds <= 0:
            raise ValueError("cycle_time_seconds must be positive")
        if self.sample_every_ticks <= 0:
            raise ValueError("sample_every_ticks must be positive")
        if self.max_simulation_seconds <= 0:
            raise ValueError("max_simulation_seconds must be positive")
        for axis in self.axes:
            axis.validate()
        self.recipe.validate(self.axes)


def load_config(path: str | Path) -> SimulationConfig:
    raw = json.loads(Path(path).read_text(encoding="utf-8"))
    return _config_from_raw(raw)


def load_default_config() -> SimulationConfig:
    raw = json.loads(
        files("smart_motion_cell.resources").joinpath("demo-cell.json").read_text(encoding="utf-8")
    )
    return _config_from_raw(raw)


def _config_from_raw(raw: object) -> SimulationConfig:
    if not isinstance(raw, dict):
        raise ValueError("configuration root must be a JSON object")
    return SimulationConfig.from_dict(raw)


def _point(value: Any) -> tuple[float, float]:
    if not isinstance(value, list | tuple) or len(value) != 2:
        raise ValueError("recipe points must contain exactly two numbers")
    return float(value[0]), float(value[1])
