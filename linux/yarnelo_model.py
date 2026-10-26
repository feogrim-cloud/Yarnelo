"""Portable data model. No Qt imports, shell parsing, or desktop side effects."""

from __future__ import annotations

import configparser
import json
import math
import os
import tempfile
import time
import uuid
from dataclasses import asdict, dataclass, field
from pathlib import Path
from typing import Any

from yarnelo_i18n import LANGUAGES, text
from yarnelo_themes import THEMES, THEME_IDS, default_appearance

VERSION = "1.9.1"
MAX_ITEMS = 30
PLACEMENTS = ("free", "bottom", "top", "left", "right")


class ModelError(ValueError):
    """Keep a stable error key so UI messages follow the current language."""

    def __init__(self, key: str, **values):
        self.key, self.values = key, values
        super().__init__(key)

    def localized(self, language: str = "auto") -> str:
        return text(self.key, language, **self.values)

    def __str__(self) -> str:
        return self.localized()


@dataclass
class Item:
    id: str
    kind: str
    path: str = ""

    @classmethod
    def create(cls, kind: str = "path", path: str = "") -> "Item":
        if kind not in {"path", "computer", "trash"}:
            raise ModelError("invalid_kind")
        if kind == "path":
            candidate = Path(path).expanduser().absolute()
            if not candidate.exists():
                raise ModelError("path_missing", path=candidate)
            # Preserve the entered symlink. Only a reference is stored; the file stays put.
            path = str(candidate)
        return cls(uuid.uuid4().hex, kind, path if kind == "path" else "")


@dataclass
class Settings:
    rows: int = 1
    opacity: float = 0.64
    icon_size: int = 48
    zoom: float = 1.35
    labels: bool = False
    launch_at_login: bool = False
    auto_add_desktop_shortcuts: bool = False
    corner_radius: float = 23.0
    theme: str = "dusk"
    theme_appearances: dict[str, dict[str, float]] = field(default_factory=dict)
    placement: str = "free"
    language: str = "auto"
    screen: str = ""
    x: int | None = None
    y: int | None = None
    items: list[Item] = field(default_factory=list)

    @classmethod
    def from_dict(cls, data: Any) -> "Settings":
        if not isinstance(data, dict):
            raise ModelError("settings_object")
        if data.get("schema", 1) != 1:
            raise ModelError("settings_schema")
        result = cls()
        selected_theme = data.get("theme", "dusk")
        result.theme = selected_theme if isinstance(selected_theme, str) and selected_theme in THEMES else "dusk"
        for name, low, high, numeric_type in (
            ("rows", 1, 4, int),
            ("opacity", 0.12, 1.0, float),
            ("icon_size", 24, 96, int),
            ("zoom", 1.0, 1.8, float),
            ("corner_radius", 0.0, 100.0, float),
        ):
            value = data.get(name, getattr(result, name))
            if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value):
                raise ModelError("invalid_value", name=name)
            setattr(result, name, numeric_type(min(high, max(low, value))))
        # These fields were added after schema 1. A bad optional theme must not
        # discard an otherwise valid legacy collection of shortcuts.
        raw_appearances = data.get("theme_appearances", {})
        if isinstance(raw_appearances, dict):
            for name in THEME_IDS:
                appearance = raw_appearances.get(name)
                if not isinstance(appearance, dict):
                    continue
                values = {}
                for key, low, high in (("opacity", .12, 1.0), ("corner_radius", 0.0, 100.0)):
                    value = appearance.get(key)
                    if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value):
                        break
                    values[key] = float(min(high, max(low, value)))
                if len(values) == 2:
                    result.theme_appearances[name] = values
        for name in ("labels", "launch_at_login", "auto_add_desktop_shortcuts"):
            if not isinstance(data.get(name, False), bool):
                raise ModelError("invalid_value", name=name)
            setattr(result, name, data.get(name, False))
        placement = data.get("placement", "free")
        if not isinstance(placement, str) or placement not in PLACEMENTS:
            raise ModelError("invalid_value", name="placement")
        result.placement = placement
        language = data.get("language", "auto")
        if language not in LANGUAGES:
            raise ModelError("invalid_value", name="language")
        result.language = language
        result.screen = str(data.get("screen", ""))[:256]
        for name in ("x", "y"):
            value = data.get(name)
            if value is not None and (isinstance(value, bool) or not isinstance(value, int)):
                raise ModelError("invalid_coordinate", name=name)
            setattr(result, name, value)
        raw_items = data.get("items", [])
        if not isinstance(raw_items, list) or len(raw_items) > MAX_ITEMS:
            raise ModelError("item_limit")
        ids: set[str] = set()
        paths: set[tuple[str, str]] = set()
        for raw in raw_items:
            if not isinstance(raw, dict):
                raise ModelError("invalid_item_record")
            ident, kind, path = raw.get("id"), raw.get("kind"), raw.get("path", "")
            if not isinstance(ident, str) or not ident or ident in ids:
                raise ModelError("invalid_item_id")
            if kind not in {"path", "computer", "trash"} or not isinstance(path, str):
                raise ModelError("invalid_item")
            if kind == "path" and (not path or not Path(path).is_absolute()):
                raise ModelError("absolute_path_required")
            if kind != "path":
                path = ""
            key = (kind, path)
            if key in paths:
                raise ModelError("duplicate_item")
            ids.add(ident)
            paths.add(key)
            # Missing files remain removable in the dock instead of losing the entry.
            result.items.append(Item(ident, kind, path))
        return result

    def to_dict(self) -> dict[str, Any]:
        return {"schema": 1, **asdict(self)}

    def appearance_for(self, theme: str) -> dict[str, float]:
        if theme == self.theme:
            return {"opacity": self.opacity, "corner_radius": self.corner_radius}
        return dict(self.theme_appearances.get(theme, default_appearance(theme)))

    def select_theme(self, theme: str) -> None:
        if theme not in THEMES or theme == self.theme:
            return
        target = self.appearance_for(theme)
        self.theme_appearances[self.theme] = self.appearance_for(self.theme)
        self.theme = theme
        self.opacity, self.corner_radius = target["opacity"], target["corner_radius"]
        self.theme_appearances[theme] = target

    def add(self, item: Item) -> bool:
        if any((i.kind, i.path) == (item.kind, item.path) for i in self.items):
            return False
        if len(self.items) >= MAX_ITEMS:
            raise ModelError("dock_full")
        self.items.append(item)
        return True


def config_path() -> Path:
    base = Path(os.environ.get("XDG_CONFIG_HOME") or Path.home() / ".config")
    # XDG directories must be absolute; ignore malformed relative overrides.
    if not base.is_absolute():
        base = Path.home() / ".config"
    return base / "yarnelo" / "settings.json"


def load_settings(path: Path) -> Settings:
    try:
        if not path.exists():
            return Settings()
        if path.stat().st_size > 1024 * 1024:
            raise ModelError("settings_too_large")
        return Settings.from_dict(json.loads(path.read_text(encoding="utf-8")))
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise ModelError("settings_read_failed", error=error) from error


def save_settings(path: Path, settings: Settings) -> None:
    # Validate before writing and atomically replace a fully flushed temporary file.
    payload = Settings.from_dict(settings.to_dict()).to_dict()
    path.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
    temporary: str | None = None
    try:
        with tempfile.NamedTemporaryFile("w", encoding="utf-8", dir=path.parent, prefix=".settings-", suffix=".tmp", delete=False) as stream:
            temporary = stream.name
            json.dump(payload, stream, ensure_ascii=False, indent=2, allow_nan=False)
            stream.write("\n")
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
        temporary = None
    finally:
        if temporary:
            try:
                os.unlink(temporary)
            except FileNotFoundError:
                pass


def preserve_invalid_settings(path: Path) -> Path:
    """Keep unreadable user data before the first explicit edit writes defaults."""
    backup = path.with_name(f"settings.invalid-{time.time_ns()}.json")
    backup.write_bytes(path.read_bytes())
    return backup


def desktop_metadata(path: Path, locale_name: str = "") -> tuple[str, str]:
    parser = configparser.ConfigParser(interpolation=None, strict=False)
    parser.optionxform = str
    try:
        if path.stat().st_size > 1024 * 1024:
            raise ModelError("desktop_too_large")
        parser.read_string(path.read_text(encoding="utf-8-sig"))
        entry = parser["Desktop Entry"]
        if entry.get("Type") != "Application":
            raise ModelError("desktop_type")
        if not entry.get("Exec") and entry.get("DBusActivatable", "").lower() != "true":
            raise ModelError("desktop_no_exec")
        language = locale_name.split(".")[0].split("@")[0]
        name = entry.get(f"Name[{language}]") or entry.get(f"Name[{language.split('_')[0]}]") or entry.get("Name") or path.stem
        return name.replace("\\s", " ").replace("\\n", " ").replace("\\t", " ").replace("\\\\", "\\"), entry.get("Icon", "")
    except (KeyError, configparser.Error, OSError, UnicodeError) as error:
        raise ModelError("desktop_read_failed", error=error) from error


def slot_indices(item_count: int, rows: int) -> list[tuple[int, int]]:
    """Balanced row-major slots, including the persistent add button."""
    if not 0 <= item_count <= MAX_ITEMS or not 1 <= rows <= MAX_ITEMS + 1:
        raise ModelError("invalid_layout")
    total = item_count + 1
    used_rows = min(rows, total)
    base, extra = divmod(total, used_rows)
    return [(row, column) for row in range(used_rows) for column in range(base + (1 if row < extra else 0))]


@dataclass(frozen=True)
class DockLayout:
    width: int
    height: int
    icon_size: float
    rows: int
    vertical: bool
    cell_width: int
    cell_height: int
    positions: tuple[tuple[int, int], ...]


def dock_layout(settings: Settings, available_width: int, available_height: int) -> DockLayout:
    """Wrap before reducing icons; transpose side docks without rotating content.

    The saved row count is a preference, not a geometry cache. This lets a dock
    return to its requested number of rows on a larger monitor.
    """
    vertical = settings.placement in ("left", "right")
    max_width, max_height = max(1, available_width - 20), max(1, available_height - 20)
    first_rows = min(settings.rows, len(settings.items) + 1)

    def candidate(rows, size):
        slots = slot_indices(len(settings.items), rows)
        columns = max(column for _, column in slots) + 1
        used_rows = max(row for row, _ in slots) + 1
        cell_width = math.ceil(size * settings.zoom + 14)
        cell_height = cell_width + (18 if settings.labels else 0)
        width, height = ((20 + used_rows * cell_width, 38 + columns * cell_height) if vertical
                         else (38 + columns * cell_width, 20 + used_rows * cell_height))
        counts = {row: sum(r == row for r, _ in slots) for row in range(used_rows)}
        points = []
        for row, column in slots:
            offset = (columns - counts[row]) * (cell_height if vertical else cell_width) // 2
            points.append((10 + row * cell_width, 26 + offset + column * cell_height) if vertical
                          else (26 + offset + column * cell_width, 10 + row * cell_height))
        return DockLayout(width, height, size, used_rows, vertical, cell_width, cell_height, tuple(points))

    choices = range(first_rows, len(settings.items) + 2)
    for rows in choices:
        result = candidate(rows, settings.icon_size)
        if result.width <= max_width and result.height <= max_height:
            return result
    # Extremely small work areas can require a smaller icon after every
    # arrangement has been tried. Pick the one with the largest visible icons.
    best = None
    for rows in choices:
        low, high = 1.0, float(settings.icon_size)
        for _ in range(20):
            middle = (low + high) / 2
            result = candidate(rows, middle)
            if result.width <= max_width and result.height <= max_height:
                low = middle
            else:
                high = middle
        result = candidate(rows, low)
        if best is None or result.icon_size > best.icon_size + 0.01:
            best = result
    return best


def move_item(items: list[Item], item_id: str, destination: int) -> None:
    source = next((index for index, item in enumerate(items) if item.id == item_id), None)
    if source is None or not 0 <= destination < len(items):
        raise ModelError("invalid_move")
    items.insert(destination, items.pop(source))
