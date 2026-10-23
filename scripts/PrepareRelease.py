"""Validate the complete platform build set and write release checksums."""
import hashlib
from pathlib import Path
import re
import shutil
import sys

FILES = [
    "Yarnelo-Windows-x64-Portable.zip", "Yarnelo-Windows-x64-Setup.exe",
    "Yarnelo-Windows-x86-Portable.zip", "Yarnelo-Windows-x86-Setup.exe",
    "Yarnelo-Linux-x86_64.tar.gz", "Yarnelo-Linux-x86_64.AppImage",
    "Yarnelo-Linux-amd64.deb", "Yarnelo-Linux-x86_64.rpm",
]


def main():
    tag, incoming, output = sys.argv[1:]
    version = Path("VERSION").read_text(encoding="utf-8").strip()
    if not re.fullmatch(r"v\d+\.\d+\.\d+", tag) or tag != "v" + version:
        raise ValueError(f"Release tag must be v{version}.")
    target = Path(output)
    target.mkdir(parents=True, exist_ok=False)
    lines = []
    for name in FILES:
        candidates = list(Path(incoming).rglob(name))
        if len(candidates) != 1 or candidates[0].is_symlink():
            raise ValueError(f"Expected exactly one regular asset: {name}")
        source = candidates[0]
        if source.stat().st_size < 1024:
            raise ValueError(f"Empty or incomplete asset: {name}")
        shutil.copyfile(source, target / name)
        checksum = hashlib.sha256(source.read_bytes()).hexdigest()
        lines.append(f"{checksum}  {name}")
    (target / "SHA256SUMS").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"Validated {len(lines)} release assets for {tag}.")


if __name__ == "__main__":
    main()
