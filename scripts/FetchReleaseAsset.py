"""Download an official AppImage build tool and verify GitHub's asset digest."""
import hashlib
import json
import os
from pathlib import Path
import re
import sys
import urllib.request

TOOLS = {
    "appimagetool": ("AppImage/appimagetool", "appimagetool-x86_64.AppImage"),
    "runtime": ("AppImage/type2-runtime", "runtime-x86_64"),
}


def main():
    name, destination = sys.argv[1:]
    repository, asset_name = TOOLS[name]
    headers = {"User-Agent": "Yarnelo-build", "Accept": "application/vnd.github+json"}
    if os.environ.get("GITHUB_TOKEN"):
        headers["Authorization"] = "Bearer " + os.environ["GITHUB_TOKEN"]
    url = f"https://api.github.com/repos/{repository}/releases/tags/continuous"
    with urllib.request.urlopen(urllib.request.Request(url, headers=headers), timeout=60) as reply:
        release = json.load(reply)
    asset = next(item for item in release["assets"] if item["name"] == asset_name)
    digest = asset.get("digest") or ""
    if not re.fullmatch(r"sha256:[0-9a-f]{64}", digest):
        raise RuntimeError("Official release has no SHA256 digest; refusing an unverified tool.")
    download = asset["browser_download_url"]
    if not download.startswith(f"https://github.com/{repository}/releases/download/"):
        raise RuntimeError("Unexpected tool download origin.")
    target = Path(destination)
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
        temporary.replace(target)
        target.chmod(0o755)
    finally:
        temporary.unlink(missing_ok=True)
    print(f"Verified {repository}/{asset_name}: {digest}")


if __name__ == "__main__":
    main()
