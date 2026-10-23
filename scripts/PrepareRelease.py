"""Validate the complete platform build set and write release checksums."""
import hashlib
from pathlib import Path
import re
import shutil
import sys
import struct
import tarfile
import zipfile

FILES = [
    "Yarnelo-Windows-x64-Portable.zip", "Yarnelo-Windows-x64-Setup.exe",
    "Yarnelo-Windows-x86-Portable.zip", "Yarnelo-Windows-x86-Setup.exe",
    "Yarnelo-Windows-arm64-Portable.zip", "Yarnelo-Windows-arm64-Setup.exe",
    "Yarnelo-Linux-x86_64.tar.gz", "Yarnelo-Linux-x86_64.AppImage",
    "Yarnelo-Linux-amd64.deb", "Yarnelo-Linux-x86_64.rpm",
    "Yarnelo-Linux-aarch64.tar.gz", "Yarnelo-Linux-aarch64.AppImage",
    "Yarnelo-Linux-arm64.deb", "Yarnelo-Linux-aarch64.rpm",
]


def pe_machine(data):
    if len(data) < 64 or data[:2] != b"MZ":
        raise ValueError("Expected a Windows PE executable.")
    offset = struct.unpack_from("<I", data, 0x3C)[0]
    if offset + 26 > len(data) or data[offset:offset + 4] != b"PE\0\0":
        raise ValueError("Invalid Windows PE header.")
    return struct.unpack_from("<H", data, offset + 4)[0]


def elf_machine(data):
    if len(data) < 20 or data[:6] != b"\x7fELF\x02\x01":
        raise ValueError("Expected a little-endian 64-bit Linux ELF executable.")
    return struct.unpack_from("<H", data, 18)[0]


def validate_architecture(source):
    """Read actual payloads so an x64 file cannot be published under an ARM label."""
    name = source.name
    if name.startswith("Yarnelo-Windows-") and name.endswith("-Portable.zip"):
        arch = name.removeprefix("Yarnelo-Windows-").removesuffix("-Portable.zip")
        expected = {"x64": 0x8664, "x86": 0x014C, "arm64": 0xAA64}[arch]
        with zipfile.ZipFile(source) as archive:
            if archive.namelist().count("Yarnelo.exe") != 1:
                raise ValueError(f"Expected exactly one flat Yarnelo.exe in {name}.")
            if pe_machine(archive.read("Yarnelo.exe")) != expected:
                raise ValueError(f"Wrong executable architecture in {name}.")
            if arch == "arm64" and b"Version=v4.8.1" not in archive.read("Yarnelo.exe.config"):
                raise ValueError("Windows ARM64 must require native .NET Framework 4.8.1.")
    elif name.startswith("Yarnelo-Linux-") and name.endswith((".AppImage", ".tar.gz")):
        arch = "aarch64" if "-aarch64." in name else "x86_64"
        expected = {"x86_64": 62, "aarch64": 183}[arch]
        if name.endswith(".tar.gz"):
            with tarfile.open(source, "r:gz") as archive:
                entries = [item for item in archive.getmembers() if item.name == "Yarnelo/yarnelo"]
                if len(entries) != 1 or not entries[0].isfile():
                    raise ValueError(f"Expected one regular launcher in {name}.")
                with archive.extractfile(entries[0]) as stream:
                    header = stream.read(20)
        else:
            with source.open("rb") as stream:
                header = stream.read(20)
        if elf_machine(header) != expected:
            raise ValueError(f"Wrong executable architecture in {name}.")


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
        validate_architecture(source)
        shutil.copyfile(source, target / name)
        checksum = hashlib.sha256(source.read_bytes()).hexdigest()
        lines.append(f"{checksum}  {name}")
    (target / "SHA256SUMS").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"Validated {len(lines)} release assets for {tag}.")


if __name__ == "__main__":
    main()
