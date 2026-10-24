"""Current-user XDG autostart, independent of Qt and without launch-time writes.

The Exec value is a Desktop Entry command, never a shell command.  We only
change registrations bearing our marker and referring to this exact launch
command; another program's yarnelo.desktop is left untouched.
"""

from __future__ import annotations

import configparser
from dataclasses import dataclass
import os
from pathlib import Path
import shutil
import stat
import sys
import tempfile
from typing import Mapping, Sequence

from yarnelo_model import ModelError

ENTRY_NAME = "yarnelo.desktop"
OWNER_KEY = "X-Yarnelo-Autostart"
_MAX_ENTRY_BYTES = 65536
_RESERVED = set(" \t\n\r\"'\\><~|&;$*?#()`")


def _invalid(key: str, cause: Exception | None = None):
    error = ModelError(key)
    if cause is not None:
        raise error from cause
    raise error


def _absolute(value: str | os.PathLike, key="startup_command_invalid") -> Path:
    try:
        path = Path(value)
        if not path.is_absolute() or any(ord(c) < 32 for c in str(path)):
            _invalid(key)
        return path
    except (TypeError, ValueError, OSError) as exc:
        _invalid(key, exc)


def _command(command: Sequence[str]) -> list[str]:
    if isinstance(command, (str, bytes)) or not command:
        _invalid("startup_command_invalid")
    result = list(command)
    if any(not isinstance(arg, str) or not arg or any(ord(c) < 32 for c in arg) for arg in result):
        _invalid("startup_command_invalid")
    try:
        for arg in result:
            arg.encode("utf-8")
    except UnicodeError as exc:
        _invalid("startup_command_invalid", exc)
    _absolute(result[0])
    # The Desktop Entry specification forbids '=' in the executable name.
    if "=" in result[0]:
        _invalid("startup_command_invalid")
    return result


def _executable(path: Path, environ: Mapping[str, str]) -> bool:
    return path.is_file() and os.access(path, os.X_OK)


def _env_launcher() -> str:
    # Do not resolve env through a user-controlled PATH. Linux distributions
    # supply this core utility in one of these stable absolute locations.
    for name in ("/usr/bin/env", "/bin/env"):
        path = Path(name)
        if path.is_absolute() and _executable(path, {}):
            return str(path)
    _invalid("startup_command_missing")


def desktop_launch_command(command: Sequence[str]) -> list[str]:
    """Keep a literal percent out of argv[0] for GLib's pre-expansion check.

    GLib checks the executable's existence before expanding Desktop Entry %%
    escapes. A valid filename containing '%' would consequently be checked as
    '%%' and rejected. Passing that exact filename to env as an argument avoids
    this ordering bug; env directly executes it without a shell or re-parsing.
    TryExec remains the original executable's literal absolute path.
    """
    command = _command(command)
    return [_env_launcher(), "--", *command] if "%" in command[0] else command


def resolve_startup_command(*, executable=None, script=None, frozen=None, environ=None) -> list[str]:
    """Resolve a persistent launcher, retaining the outer AppImage when mounted.

    Keep a virtualenv's Python symlink rather than resolving it to base Python.
    Dependency injection makes this usable without touching the caller's XDG
    directories and allows package tests to exercise every launch format.
    """
    env = os.environ if environ is None else environ
    exe = _absolute(sys.executable if executable is None else executable)
    is_frozen = bool(getattr(sys, "frozen", False)) if frozen is None else frozen
    if not _executable(exe, env):
        _invalid("startup_command_missing")
    if not is_frozen:
        source = _absolute(Path(__file__).with_name("yarnelo.py") if script is None else script)
        if not source.is_file():
            _invalid("startup_command_missing")
        return _command([str(exe), str(source), "--startup"])

    image, app_dir = env.get("APPIMAGE"), env.get("APPDIR")
    if image or app_dir:
        # An unrelated APPIMAGE inherited from a parent application must never
        # become Yarnelo's login launcher.
        if not image or not app_dir:
            _invalid("startup_command_unstable")
        outer = _absolute(image, "startup_command_unstable")
        directory = _absolute(app_dir, "startup_command_unstable")
        try:
            physical_directory = directory.resolve(strict=True)
            exe.resolve(strict=True).relative_to(physical_directory)
        except (OSError, ValueError) as exc:
            _invalid("startup_command_unstable", exc)
        if not _executable(outer, env):
            _invalid("startup_command_missing")
        try:
            outer.resolve(strict=True).relative_to(physical_directory)
        except ValueError:
            pass
        else:
            _invalid("startup_command_unstable")
        if any(part.startswith((".mount_", "appimage_extracted_")) for part in outer.parts):
            _invalid("startup_command_unstable")
        args = [str(outer)]
        # type2-runtime also accepts a CLI flag, removes it from application
        # argv, and leaves APPIMAGE_EXTRACT_AND_RUN unset in that case. Its
        # extraction root still has this deterministic prefix. The physical
        # executable's containment and the external image were verified above.
        if "APPIMAGE_EXTRACT_AND_RUN" in env or physical_directory.name.startswith("appimage_extracted_"):
            args.append("--appimage-extract-and-run")
        return _command([*args, "--startup"])

    if any(part.startswith((".mount_", "appimage_extracted_")) for part in exe.parts):
        _invalid("startup_command_unstable")
    return _command([str(exe), "--startup"])


def _escape_string(value: str) -> str:
    # Escape spaces too, so a literal trailing space in TryExec is not trimmed
    # as Desktop Entry whitespace.  The general string layer is decoded first.
    return value.replace("\\", "\\\\").replace(" ", "\\s").replace("\n", "\\n").replace("\t", "\\t").replace("\r", "\\r")


def _unescape_string(value: str) -> str:
    result, i = [], 0
    escaped = {"s": " ", "n": "\n", "t": "\t", "r": "\r", "\\": "\\"}
    while i < len(value):
        char = value[i]
        if char == "\\":
            i += 1
            if i == len(value) or value[i] not in escaped:
                _invalid("startup_entry_invalid")
            char = escaped[value[i]]
        result.append(char)
        i += 1
    return "".join(result)


def encode_exec(command: Sequence[str]) -> str:
    """Encode both Desktop Entry string escaping and Exec argument quoting."""
    tokens = []
    for arg in _command(command):
        quoted = "".join(("\\" + char) if char in '\\"$`' else char for char in arg)
        tokens.append('"' + quoted.replace("%", "%%") + '"')
    return _escape_string(" ".join(tokens))


def decode_exec(value: str) -> list[str]:
    """Read the conservative literal-only Exec grammar used for ownership.

    Field codes other than %% are deliberately rejected: a registration using
    them is not our exact command and must not be overwritten.
    """
    value = _unescape_string(value)
    result, i = [], 0
    while i < len(value):
        while i < len(value) and value[i] in " \t":
            i += 1
        if i == len(value):
            break
        quoted = value[i] == '"'
        if quoted:
            i += 1
        token, closed = [], not quoted
        while i < len(value):
            char = value[i]
            if quoted and char == '"':
                i += 1
                closed = True
                break
            if not quoted and char in " \t":
                break
            if quoted and char == "\\":
                i += 1
                if i == len(value) or value[i] not in '\\"$`':
                    _invalid("startup_entry_invalid")
                char = value[i]
            elif (not quoted and char in _RESERVED) or (quoted and char in "$`"):
                _invalid("startup_entry_invalid")
            if char == "%":
                i += 1
                if i == len(value) or value[i] != "%":
                    _invalid("startup_entry_invalid")
            token.append(char)
            i += 1
        if not closed or not token or (i < len(value) and value[i] not in " \t"):
            _invalid("startup_entry_invalid")
        result.append("".join(token))
    if not result:
        _invalid("startup_entry_invalid")
    return result


def _identity(command: list[str]) -> list[str]:
    # AppImage's runtime consumes this flag before Yarnelo receives --startup.
    # A later manual launch with FUSE must still be able to disable an entry
    # originally created without FUSE (and vice versa).  No other differences
    # in program path or arguments are considered the same installation.
    if len(command) == 3 and command[1:] == ["--appimage-extract-and-run", "--startup"]:
        return [command[0], "--startup"]
    return command


def _redirected(info: os.stat_result) -> bool:
    return stat.S_ISLNK(info.st_mode) or bool(getattr(info, "st_file_attributes", 0) & 0x400)


def _safe_directories(path: Path):
    """Reject links/reparse points in the bounded path we are about to edit."""
    for parent in reversed((path, *path.parents)):
        try:
            info = parent.lstat()
        except FileNotFoundError:
            continue
        if _redirected(info) or not stat.S_ISDIR(info.st_mode):
            _invalid("startup_entry_unsafe")


@dataclass(frozen=True)
class _Entry:
    path: Path
    values: dict[str, str]
    signature: tuple


def _signature(info):
    return (info.st_dev, info.st_ino, info.st_size, info.st_mtime_ns)


def _read_entry(path: Path, *, user=False) -> _Entry | None:
    try:
        info = path.lstat()
    except FileNotFoundError:
        return None
    if _redirected(info) or not stat.S_ISREG(info.st_mode) or info.st_nlink != 1:
        _invalid("startup_entry_unsafe")
    if user and hasattr(os, "getuid") and info.st_uid != os.getuid():
        _invalid("startup_entry_unsafe")
    if info.st_size > _MAX_ENTRY_BYTES:
        _invalid("startup_entry_invalid")
    try:
        flags = os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0) | getattr(os, "O_BINARY", 0)
        descriptor = os.open(path, flags)
        with os.fdopen(descriptor, "rb") as stream:
            opened = os.fstat(stream.fileno())
            if (_signature(opened) != _signature(info) or not stat.S_ISREG(opened.st_mode)
                    or opened.st_nlink != 1 or (user and hasattr(os, "getuid") and opened.st_uid != os.getuid())):
                _invalid("startup_entry_unsafe")
            data = stream.read(_MAX_ENTRY_BYTES + 1)
        if len(data) > _MAX_ENTRY_BYTES:
            _invalid("startup_entry_invalid")
        parser = configparser.ConfigParser(interpolation=None, strict=True, empty_lines_in_values=False)
        parser.optionxform = str
        parser.read_string(data.decode("utf-8-sig"))
        if "Desktop Entry" not in parser:
            _invalid("startup_entry_invalid")
        values = dict(parser["Desktop Entry"])
        return _Entry(path, values, _signature(opened))
    except (OSError, UnicodeError, configparser.Error) as exc:
        _invalid("startup_entry_invalid", exc)


class AutostartManager:
    """Read current registration; write only after an explicit settings change."""

    def __init__(self, command: Sequence[str], config_home: Path | None = None, *, environ=None,
                 home=None, config_dirs=None):
        self.command = _command(command)
        self.environ = dict(os.environ if environ is None else environ)
        user_home = Path.home() if home is None else _absolute(home)
        if config_home is None:
            configured = self.environ.get("XDG_CONFIG_HOME", "")
            config_home = Path(configured) if configured and Path(configured).is_absolute() else user_home / ".config"
        self.config_home = _absolute(config_home, "startup_entry_unsafe")
        self.path = self.config_home / "autostart" / ENTRY_NAME
        if config_dirs is None:
            configured = self.environ.get("XDG_CONFIG_DIRS", "/etc/xdg") or "/etc/xdg"
            config_dirs = [Path(value) for value in configured.split(":") if value and Path(value).is_absolute()]
        self.config_dirs = tuple(_absolute(path, "startup_entry_unsafe") for path in config_dirs)

    def _own(self, entry: _Entry) -> bool:
        if entry.values.get(OWNER_KEY) != "true":
            return False
        try:
            registered = decode_exec(entry.values.get("Exec", ""))
            if _identity(registered) == _identity(self.command):
                return True
            if "%" in self.command[0]:
                launcher = _env_launcher()
                return registered[:2] == [launcher, "--"] and _identity(registered[2:]) == _identity(self.command)
            return False
        except ModelError:
            return False

    def _user_entry(self):
        _safe_directories(self.path.parent)
        return _read_entry(self.path, user=True)

    def _system_entry(self):
        for directory in self.config_dirs:
            entry = _read_entry(directory / "autostart" / ENTRY_NAME)
            if entry is not None:
                return entry
        return None

    def _active(self, entry: _Entry) -> bool:
        values = entry.values
        if values.get("Hidden", "false").lower() == "true" or values.get("X-GNOME-Autostart-enabled", "true").lower() == "false":
            return False
        if values.get("Type") != "Application":
            return False
        desktops = set(self.environ.get("XDG_CURRENT_DESKTOP", "").split(":")) - {""}
        only, excluded = values.get("OnlyShowIn"), values.get("NotShowIn")
        if only is not None and excluded is not None:
            return False
        if only is not None and not desktops.intersection(only.split(";")):
            return False
        if excluded is not None and desktops.intersection(excluded.split(";")):
            return False
        try_exec = _unescape_string(values.get("TryExec", ""))
        if try_exec:
            candidate = Path(try_exec)
            if candidate.is_absolute():
                return _executable(candidate, self.environ)
            return shutil.which(try_exec, path=self.environ.get("PATH", "")) is not None
        return True

    @property
    def status(self) -> str:
        try:
            entry = self._user_entry() or self._system_entry()
            if entry is None:
                return "missing"
            if not self._own(entry):
                return "foreign"
            return "enabled" if self._active(entry) else "disabled"
        except OSError as exc:
            _invalid("startup_entry_unsafe", exc)

    @property
    def enabled(self) -> bool:
        return self.status == "enabled"

    def _content(self, enabled: bool) -> bytes:
        return ("[Desktop Entry]\nType=Application\nName=Yarnelo\n"
                "Comment=Your shortcuts. In one place.\n"
                f"Exec={encode_exec(desktop_launch_command(self.command))}\n"
                f"TryExec={_escape_string(self.command[0])}\n"
                "Terminal=false\nStartupNotify=false\n"
                f"Hidden={'false' if enabled else 'true'}\n"
                f"{OWNER_KEY}=true\n").encode("utf-8")

    def _unchanged(self, previous: _Entry | None):
        current = self._user_entry()
        if (previous is None) != (current is None):
            _invalid("startup_entry_unsafe")
        if current is not None and (current.signature != previous.signature or not self._own(current)):
            _invalid("startup_entry_unsafe")

    def _write(self, data: bytes, previous: _Entry | None):
        _safe_directories(self.path.parent)
        self.path.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
        _safe_directories(self.path.parent)
        descriptor, temporary = tempfile.mkstemp(prefix=".yarnelo-", suffix=".tmp", dir=self.path.parent)
        try:
            with os.fdopen(descriptor, "wb") as stream:
                stream.write(data)
                stream.flush()
                os.fsync(stream.fileno())
            self._unchanged(previous)
            os.replace(temporary, self.path)
        finally:
            try:
                os.unlink(temporary)
            except FileNotFoundError:
                pass

    def set_enabled(self, enabled: bool):
        if not isinstance(enabled, bool):
            _invalid("startup_command_invalid")
        try:
            previous = self._user_entry()
            system = self._system_entry()
            effective = previous or system
            if effective is not None and not self._own(effective):
                _invalid("startup_entry_foreign")
            if enabled:
                if not _executable(Path(self.command[0]), self.environ):
                    _invalid("startup_command_missing")
                self._write(self._content(True), previous)
            elif system is not None:
                # The higher-priority Hidden entry also safely masks a foreign
                # system file that removal of our own user entry would expose.
                self._write(self._content(False), previous)
            elif previous is not None:
                self._unchanged(previous)
                self.path.unlink()
            current = self._user_entry()
            if enabled:
                if current is None or not self._own(current) or not self._active(current):
                    _invalid("startup_verify_failed")
            elif current is not None and (not self._own(current) or self._active(current)):
                _invalid("startup_verify_failed")
            elif self.enabled:
                _invalid("startup_verify_failed")
        except OSError as exc:
            _invalid("startup_entry_unsafe", exc)
