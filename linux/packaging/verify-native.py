"""Reject a mixed or incorrectly labelled Linux ELF bundle before publication."""
import argparse
from pathlib import Path
import struct


ELF_MACHINES = {"x86_64": 62, "aarch64": 183}


def verify(path: Path, architecture: str) -> int:
    files = path.rglob("*") if path.is_dir() else (path,)
    count = 0
    for candidate in files:
        if not candidate.is_file():
            continue
        with candidate.open("rb") as stream:
            header = stream.read(20)
        if header[:4] != b"\x7fELF":
            continue
        if (len(header) < 20 or header[4:6] != b"\x02\x01"
                or struct.unpack_from("<H", header, 18)[0] != ELF_MACHINES[architecture]):
            raise ValueError(f"Expected a native 64-bit {architecture} ELF: {candidate}")
        count += 1
    if not count:
        raise ValueError(f"No ELF executables or libraries found: {path}")
    return count


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("architecture", choices=ELF_MACHINES)
    parser.add_argument("path", type=Path)
    args = parser.parse_args()
    count = verify(args.path, args.architecture)
    print(f"Verified {count} native {args.architecture} ELF files: {args.path}")


if __name__ == "__main__":
    main()
