"""Observe only newly created desktop launchers; never alter their source files."""

from __future__ import annotations

from dataclasses import dataclass
import os
from pathlib import Path
import stat
import time


@dataclass
class _Pending:
    since: float
    signature: tuple | None = None
    stable: int = 0


class DesktopShortcutTracker:
    """Explicit polling keeps file IO and retries independent of the GUI toolkit.

    Existing names form a baseline at construction. A new readable, nonempty
    regular .desktop file must have the same identity/size/mtime for two polls.
    The caller acknowledges successful imports or duplicates. Failed attempts
    are offered again for at most 30 seconds without any source writes.
    """

    def __init__(self, desktop: Path, *, clock=time.monotonic):
        self.directory = Path(desktop).absolute()
        self.clock = clock
        self._closed = False
        self._seen: set[str] = set()
        self._pending: dict[str, _Pending] = {}
        self._baseline_ok = False
        self.reset()

    @staticmethod
    def _redirected(info):
        return stat.S_ISLNK(info.st_mode) or bool(getattr(info, "st_file_attributes", 0) & 0x400)

    def _names(self):
        try:
            info = self.directory.lstat()
            if self._redirected(info) or not stat.S_ISDIR(info.st_mode):
                return None
            return {path.name: path for path in self.directory.iterdir() if path.suffix.lower() == ".desktop"}
        except FileNotFoundError:
            return {}
        except OSError:
            return None

    @staticmethod
    def _signature(path):
        try:
            before = path.lstat()
            if (DesktopShortcutTracker._redirected(before) or not stat.S_ISREG(before.st_mode)
                    or before.st_nlink != 1 or before.st_size == 0):
                return None
            descriptor = os.open(path, os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0) | getattr(os, "O_BINARY", 0))
            with os.fdopen(descriptor, "rb") as stream:
                opened = os.fstat(stream.fileno())
                if (not stat.S_ISREG(opened.st_mode) or opened.st_nlink != 1
                        or (before.st_dev, before.st_ino, before.st_size, before.st_mtime_ns)
                        != (opened.st_dev, opened.st_ino, opened.st_size, opened.st_mtime_ns)
                        or not stream.read(1)):
                    return None
                after = os.fstat(stream.fileno())
                if (opened.st_size, opened.st_mtime_ns) != (after.st_size, after.st_mtime_ns):
                    return None
                return (after.st_dev, after.st_ino, after.st_size, after.st_mtime_ns)
        except OSError:
            return None

    def reset(self):
        if self._closed:
            return
        names = self._names()
        self._baseline_ok = names is not None
        self._seen = set(names or {})
        self._pending.clear()

    def scan(self) -> list[Path]:
        if self._closed:
            return []
        names = self._names()
        if names is None:
            return []
        if not self._baseline_ok:
            self._seen = set(names)
            self._baseline_ok = True
            return []
        self._seen.intersection_update(names)
        for name in tuple(self._pending):
            if name not in names:
                del self._pending[name]
        now = self.clock()
        ready = []
        for name, path in sorted(names.items()):
            if name in self._seen:
                continue
            pending = self._pending.setdefault(name, _Pending(now))
            if now - pending.since >= 30:
                self.acknowledge(path)
                continue
            signature = self._signature(path)
            if signature is None:
                pending.signature, pending.stable = None, 0
                continue
            pending.stable = pending.stable + 1 if signature == pending.signature else 1
            pending.signature = signature
            if pending.stable >= 2:
                ready.append(path)
        return ready

    def poll(self) -> list[Path]:
        return self.scan()

    def acknowledge(self, path):
        path = Path(path).absolute()
        if path.parent == self.directory:
            self._seen.add(path.name)
            self._pending.pop(path.name, None)

    def close(self):
        self._closed = True
        self._pending.clear()
        self._seen.clear()
