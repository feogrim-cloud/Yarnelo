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

VERSION = "1.4.0"
MAX_ITEMS = 30


class ModelError(ValueError):
    pass


@dataclass
class Item:
    id: str
    kind: str
    path: str = ""

    @classmethod
    def create(cls, kind: str = "path", path: str = "") -> "Item":
        if kind not in {"path", "computer", "trash"}:
            raise ModelError("Неизвестный тип ярлыка.")
        if kind == "path":
            candidate = Path(path).expanduser().absolute()
            if not candidate.exists():
                raise ModelError(f"Файл или папка не найдены: {candidate}")
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
    screen: str = ""
    x: int | None = None
    y: int | None = None
    items: list[Item] = field(default_factory=list)

    @classmethod
    def from_dict(cls, data: Any) -> "Settings":
        if not isinstance(data, dict):
            raise ModelError("Настройки должны быть объектом JSON.")
        if data.get("schema", 1) != 1:
            raise ModelError("Формат настроек этой версии не поддерживается.")
        result = cls()
        for name, low, high, numeric_type in (
            ("rows", 1, 4, int),
            ("opacity", 0.12, 1.0, float),
            ("icon_size", 24, 96, int),
            ("zoom", 1.0, 1.8, float),
        ):
            value = data.get(name, getattr(result, name))
            if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value):
                raise ModelError(f"Некорректное значение: {name}")
            setattr(result, name, numeric_type(min(high, max(low, value))))
        if not isinstance(data.get("labels", False), bool):
            raise ModelError("Некорректное значение: labels")
        result.labels = data.get("labels", False)
        result.screen = str(data.get("screen", ""))[:256]
        for name in ("x", "y"):
            value = data.get(name)
            if value is not None and (isinstance(value, bool) or not isinstance(value, int)):
                raise ModelError(f"Некорректная координата: {name}")
            setattr(result, name, value)
        raw_items = data.get("items", [])
        if not isinstance(raw_items, list) or len(raw_items) > MAX_ITEMS:
            raise ModelError("В панели может быть не больше 30 ярлыков.")
        ids: set[str] = set()
        paths: set[tuple[str, str]] = set()
        for raw in raw_items:
            if not isinstance(raw, dict):
                raise ModelError("Некорректная запись ярлыка.")
            ident, kind, path = raw.get("id"), raw.get("kind"), raw.get("path", "")
            if not isinstance(ident, str) or not ident or ident in ids:
                raise ModelError("Некорректный или повторяющийся идентификатор ярлыка.")
            if kind not in {"path", "computer", "trash"} or not isinstance(path, str):
                raise ModelError("Некорректный ярлык.")
            if kind == "path" and (not path or not Path(path).is_absolute()):
                raise ModelError("Для ярлыка нужен абсолютный путь.")
            if kind != "path":
                path = ""
            key = (kind, path)
            if key in paths:
                raise ModelError("В настройках есть повторяющийся ярлык.")
            ids.add(ident)
            paths.add(key)
            # Missing files remain removable in the dock instead of losing the entry.
            result.items.append(Item(ident, kind, path))
        return result

    def to_dict(self) -> dict[str, Any]:
        return {"schema": 1, **asdict(self)}

    def add(self, item: Item) -> bool:
        if any((i.kind, i.path) == (item.kind, item.path) for i in self.items):
            return False
        if len(self.items) >= MAX_ITEMS:
            raise ModelError("В панели уже 30 ярлыков. Удалите один, чтобы добавить новый.")
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
            raise ModelError("Файл настроек слишком большой.")
        return Settings.from_dict(json.loads(path.read_text(encoding="utf-8")))
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise ModelError(f"Не удалось прочитать настройки: {error}") from error


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
            raise ModelError("Файл .desktop слишком большой.")
        parser.read_string(path.read_text(encoding="utf-8-sig"))
        entry = parser["Desktop Entry"]
        if entry.get("Type") != "Application":
            raise ModelError("Поддерживаются .desktop типа Application.")
        if not entry.get("Exec") and entry.get("DBusActivatable", "").lower() != "true":
            raise ModelError("В .desktop не указано приложение для запуска.")
        language = locale_name.split(".")[0].split("@")[0]
        name = entry.get(f"Name[{language}]") or entry.get(f"Name[{language.split('_')[0]}]") or entry.get("Name") or path.stem
        return name.replace("\\s", " ").replace("\\n", " ").replace("\\t", " ").replace("\\\\", "\\"), entry.get("Icon", "")
    except (KeyError, configparser.Error, OSError, UnicodeError) as error:
        raise ModelError(f"Не удалось прочитать .desktop: {error}") from error


def slot_indices(item_count: int, rows: int) -> list[tuple[int, int]]:
    """Balanced row-major slots, including the persistent add button."""
    if not 0 <= item_count <= MAX_ITEMS or not 1 <= rows <= 4:
        raise ModelError("Некорректный размер панели.")
    total = item_count + 1
    used_rows = min(rows, total)
    base, extra = divmod(total, used_rows)
    return [(row, column) for row in range(used_rows) for column in range(base + (1 if row < extra else 0))]


def move_item(items: list[Item], item_id: str, destination: int) -> None:
    source = next((index for index, item in enumerate(items) if item.id == item_id), None)
    if source is None or not 0 <= destination < len(items):
        raise ModelError("Некорректное перемещение ярлыка.")
    items.insert(destination, items.pop(source))
