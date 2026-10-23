"""Download an official native AppImage tool and verify GitHub's asset digest."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import struct
import sys
import urllib.request

TOOLS = {
    "appimagetool": ("AppImage/appimagetool", "appimagetool-{architecture}.AppImage"),
    "runtime": ("AppImage/type2-runtime", "runtime-{architecture}"),
}
ELF_MACHINES = {"x86_64": 62, "aarch64": 183}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("tool", choices=TOOLS)
    parser.add_argument("destination", type=Path)
    parser.add_argument("--architecture", choices=ELF_MACHINES,
                        default={"AMD64": "x86_64", "ARM64": "aarch64"}.get(
                            platform.machine(), platform.machine()))
    args = parser.parse_args()
    if args.architecture not in ELF_MACHINES:
        parser.error("This host architecture is unsupported; specify --architecture.")
    repository, asset_pattern = TOOLS[args.tool]
    asset_name = asset_pattern.format(architecture=args.architecture)
    headers = {"User-Agent": "Yarnelo-build", "Accept": "application/vnd.github+json"}
    if os.environ.get("GITHUB_TOKEN"):
        headers["Authorization"] = "Bearer " + os.environ["GITHUB_TOKEN"]
    url = f"https://api.github.com/repos/{repository}/releases/tags/continuous"
    with urllib.request.urlopen(urllib.request.Request(url, headers=headers), timeout=60) as reply:
        release = json.load(reply)
    reference_url = f"https://api.github.com/repos/{repository}/git/ref/tags/continuous"
    with urllib.request.urlopen(urllib.request.Request(reference_url, headers=headers), timeout=60) as reply:
        source_object = json.load(reply)["object"]
    while source_object["type"] == "tag":
        tag_url = f"https://api.github.com/repos/{repository}/git/tags/{source_object['sha']}"
        with urllib.request.urlopen(urllib.request.Request(tag_url, headers=headers), timeout=60) as reply:
            source_object = json.load(reply)["object"]
    if source_object["type"] != "commit":
        raise RuntimeError("Unexpected upstream source reference.")
    asset = next(item for item in release["assets"] if item["name"] == asset_name)
    digest = asset.get("digest") or ""
    if not re.fullmatch(r"sha256:[0-9a-f]{64}", digest):
        raise RuntimeError("Official release has no SHA256 digest; refusing an unverified tool.")
    download = asset["browser_download_url"]
    if not download.startswith(f"https://github.com/{repository}/releases/download/"):
        raise RuntimeError("Unexpected tool download origin.")
    target = args.destination
    target.parent.mkdir(parents=True, exist_ok=True)
    if target.exists():
        raise FileExistsError(target)
    checksum = hashlib.sha256()
    temporary = target.with_suffix(target.suffix + ".part")
    try:
        with urllib.request.urlopen(download, timeout=120) as reply, temporary.open("xb") as output:
            while chunk := reply.read(1024 * 1024):
                checksum.update(chunk)
                output.write(chunk)
        if checksum.hexdigest() != digest.removeprefix("sha256:"):
            raise RuntimeError("Official tool digest mismatch.")
        with temporary.open("rb") as binary:
            header = binary.read(20)
        if (len(header) < 20 or header[:6] != b"\x7fELF\x02\x01"
                or struct.unpack_from("<H", header, 18)[0] != ELF_MACHINES[args.architecture]):
            raise RuntimeError("Official tool has the wrong ELF architecture.")
        temporary.replace(target)
        target.chmod(0o755)
    finally:
        temporary.unlink(missing_ok=True)
    source_record = {
        "repository": repository,
        "asset": asset_name,
        "architecture": args.architecture,
        "digest": digest,
        "source_commit": source_object["sha"],
        "source_archive": f"https://github.com/{repository}/archive/{source_object['sha']}.tar.gz",
        "build_instructions": f"https://github.com/{repository}/tree/{source_object['sha']}",
    }
    target.with_name(target.name + ".source.json").write_text(
        json.dumps(source_record, indent=2) + "\n", encoding="utf-8"
    )
    print(f"Verified {repository}/{asset_name}: {digest}")


if __name__ == "__main__":
    main()
