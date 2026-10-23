#!/usr/bin/env bash
# Build on Ubuntu 22.04 x86_64 (glibc 2.35); cross-compilation is unsupported.
# Dependencies are installed by CI, never fetched or installed by this script.
set -Eeuo pipefail
export LC_ALL=C.UTF-8
export LANG=C.UTF-8

die() { printf 'Package-Linux: %s\n' "$*" >&2; exit 1; }
need() { command -v "$1" >/dev/null 2>&1 || die "Missing command: $1"; }
repo=$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
output="$repo/dist"
version=1.4.0
smoke_test=false

while (($#)); do
    case "$1" in
        --output) (($# >= 2)) || die '--output needs a directory'; output=$2; shift 2 ;;
        --version) (($# >= 2)) || die '--version needs a version'; version=$2; shift 2 ;;
        --smoke-test) smoke_test=true; shift ;;
        -h|--help)
            printf '%s\n' 'Usage: bash scripts/Package-Linux.sh [--output DIRECTORY] [--version 1.4.0] [--smoke-test]' \
              'Requires Python + PyInstaller + linux/requirements.txt, fpm, dpkg-deb, rpmbuild,' \
              'desktop-file-validate, APPIMAGETOOL and APPIMAGE_RUNTIME (verified local files).'
            exit 0 ;;
        *) die "Unknown argument: $1" ;;
    esac
done

[[ $(uname -s) == Linux ]] || die 'Build this package on Linux, not Windows/macOS.'
[[ $(uname -m) == x86_64 ]] || die 'Only x86_64 builds are supported in this release.'
[[ $version =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || die 'Use a numeric major.minor.patch version.'
for tool in python3 fpm dpkg-deb rpmbuild rpm tar gzip install sha256sum desktop-file-validate getconf realpath; do need "$tool"; done
[[ $(getconf GNU_LIBC_VERSION) == 'glibc 2.35' ]] || die 'Use an Ubuntu 22.04/glibc 2.35 build host to preserve the published compatibility baseline.'
python3 -c 'import PyInstaller, PySide6, shiboken6' || die 'Install PyInstaller and linux/requirements.txt first.'
[[ -n ${APPIMAGETOOL:-} && -f $APPIMAGETOOL && -x $APPIMAGETOOL ]] || die 'APPIMAGETOOL must name a verified executable appimagetool file.'
[[ -n ${APPIMAGE_RUNTIME:-} && -f $APPIMAGE_RUNTIME ]] || die 'APPIMAGE_RUNTIME must name a verified x86_64 type2-runtime file.'
appimagetool=$(realpath -- "$APPIMAGETOOL")
appimage_runtime=$(realpath -- "$APPIMAGE_RUNTIME")
desktop-file-validate "$repo/linux/packaging/yarnelo.desktop"
[[ $(python3 "$repo/linux/yarnelo.py" --version) == *"$version"* ]] || die 'Requested version differs from linux/yarnelo.py.'

mkdir -p -- "$output" "$repo/build"
output=$(CDPATH= cd -- "$output" && pwd)
build=$(mktemp -d "$repo/build/linux-package.XXXXXXXX")
# mktemp always creates a child of this repository's build directory.
trap 'case "$build" in "$repo"/build/linux-package.*) rm -rf -- "$build" ;; esac' EXIT

python3 -m PyInstaller --noconfirm --clean \
    --distpath "$build/frozen" --workpath "$build/pyinstaller" \
    "$repo/linux/packaging/yarnelo.spec"
app="$build/frozen/yarnelo"
[[ -x $app/yarnelo ]] || die 'PyInstaller did not produce yarnelo.'
[[ -f $app/_internal/assets/Yarnelo-Icon-256.png ]] || die 'The bundled application icon is missing.'

install -m 644 "$repo/LICENSE" "$app/LICENSE"
install -m 644 "$repo/linux/packaging/THIRD-PARTY-NOTICES.txt" "$app/THIRD-PARTY-NOTICES.txt"
mkdir -p "$app/docs"
for document in "$repo/docs/INSTALL.md" "$repo/linux/README.md"; do
    [[ ! -f $document ]] || install -m 644 "$document" "$app/docs/$(basename -- "$document")"
done

# Preserve licenses provided in all three Python for Qt wheel distributions.
python3 - "$app" <<'PY'
import importlib.metadata as metadata
from pathlib import Path
import shutil
import sys
import sysconfig

destination = Path(sys.argv[1])
from PySide6.QtCore import qVersion

python_version = sys.version.split()[0]
qt_version = qVersion()
versions = [f"Python {python_version}", f"Qt {qt_version}"]
sources = [
    f"CPython {python_version}: https://github.com/python/cpython/tree/v{python_version}",
    f"Qt {qt_version}: https://code.qt.io/qt/qtbase.git/tree/?h=v{qt_version}",
    f"Qt SVG {qt_version}: https://code.qt.io/qt/qtsvg.git/tree/?h=v{qt_version}",
    f"Qt Image Formats {qt_version}: https://code.qt.io/qt/qtimageformats.git/tree/?h=v{qt_version}",
]
for name in ("PyInstaller", "PySide6", "PySide6-Essentials", "shiboken6"):
    try:
        distribution = metadata.distribution(name)
    except metadata.PackageNotFoundError:
        if name == "PySide6":
            continue  # The much smaller Essentials wheel supplies this module.
        raise
    versions.append(f"{distribution.metadata['Name']} {distribution.version}")
    if name == "PySide6-Essentials":
        sources.append(f"PySide/Shiboken {distribution.version}: https://code.qt.io/pyside/pyside-setup.git/tree/?h=v{distribution.version}")
    for item in distribution.files or ():
        if not any(part.lower().startswith(("license", "copying", "copyright")) for part in item.parts):
            continue
        source = Path(distribution.locate_file(item))
        if source.is_file():
            target = destination / "licenses" / name / Path(*item.parts)
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, target)
for base in (Path(sys.base_prefix), Path(sysconfig.get_path("stdlib"))):
    for name in ("LICENSE", "LICENSE.txt"):
        source = base / name
        if source.is_file():
            target = destination / "licenses" / "Python" / name
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, target)
(destination / "build-versions.txt").write_text("\n".join(versions) + "\n", encoding="utf-8")
(destination / "corresponding-sources.txt").write_text("\n".join(sources) + "\n", encoding="utf-8")
PY

if $smoke_test; then
    QT_QPA_PLATFORM=offscreen "$app/yarnelo" --smoke-test
fi

portable="$build/portable/Yarnelo"
mkdir -p "$portable"
cp -a "$app/." "$portable/"
install -m 644 "$repo/linux/packaging/yarnelo.desktop" "$portable/yarnelo.desktop"
tar -C "$build/portable" -czf "$output/Yarnelo-Linux-x86_64.tar.gz" Yarnelo

stage="$build/system"
mkdir -p "$stage/opt/yarnelo" "$stage/usr/bin" "$stage/usr/share/applications" \
    "$stage/usr/share/icons/hicolor/256x256/apps" "$stage/usr/share/icons/hicolor/scalable/apps"
cp -a "$app/." "$stage/opt/yarnelo/"
ln -s /opt/yarnelo/yarnelo "$stage/usr/bin/yarnelo"
install -m 644 "$repo/linux/packaging/yarnelo.desktop" "$stage/usr/share/applications/yarnelo.desktop"
install -m 644 "$repo/assets/Yarnelo-Icon-256.png" "$stage/usr/share/icons/hicolor/256x256/apps/yarnelo.png"
install -m 644 "$repo/assets/Yarnelo-Icon.svg" "$stage/usr/share/icons/hicolor/scalable/apps/yarnelo.svg"

common=(-s dir -n yarnelo -v "$version" --iteration 1 --license 'All rights reserved' \
    --vendor feogrim-cloud --maintainer "${YARNELO_MAINTAINER:-feogrim-cloud}" \
    --url https://github.com/feogrim-cloud/Yarnelo \
    --description 'Yarnelo desktop shortcut dock (X11 / XWayland)' \
    --category Utility -C "$stage" --force)

# Debian package names are stable across the documented Ubuntu/Debian releases.
# Qt itself and Python are bundled; no Python/Qt package installation is needed.
deb_dependencies=(
    'libc6 (>= 2.35)' libgcc-s1 libstdc++6 libx11-6 libxcb1 libxcb-cursor0
    libxcb-icccm4 libxcb-image0 libxcb-keysyms1 libxcb-render-util0 libxcb-render0
    libxcb-shape0 libxcb-randr0 libxcb-xfixes0 libxcb-shm0 libxcb-sync1 libxcb-xinerama0
    libxcb-xkb1 libxkbcommon0 libxkbcommon-x11-0 libxext6 libxrender1 libxi6
    libfontconfig1 libfreetype6 'libglib2.0-0 | libglib2.0-0t64' libdbus-1-3
    libgl1 libegl1 libice6 libsm6 xdg-utils libglib2.0-bin
)
deb_args=()
for dependency in "${deb_dependencies[@]}"; do deb_args+=(--depends "$dependency"); done
fpm "${common[@]}" -t deb -a amd64 "${deb_args[@]}" --deb-recommends gvfs-backends \
    -p "$output/Yarnelo-Linux-amd64.deb" .

# Automatic ELF SONAME requirements/providers avoid Fedora-only dependency names.
# Shared libraries shipped inside this package satisfy their own SONAME requires.
fpm "${common[@]}" -t rpm -a x86_64 --rpm-os linux --rpm-autoreqprov \
    --rpm-digest sha256 --rpm-compression gzip --depends 'glibc >= 2.35' \
    --depends /usr/bin/xdg-open --depends /usr/bin/gio \
    -p "$output/Yarnelo-Linux-x86_64.rpm" .

appdir="$build/Yarnelo.AppDir"
mkdir -p "$appdir/usr/lib/yarnelo" "$appdir/usr/share/applications" \
    "$appdir/usr/share/icons/hicolor/256x256/apps"
cp -a "$app/." "$appdir/usr/lib/yarnelo/"
install -m 755 "$repo/linux/packaging/AppRun" "$appdir/AppRun"
install -m 644 "$repo/linux/packaging/yarnelo.desktop" "$appdir/yarnelo.desktop"
install -m 644 "$repo/linux/packaging/yarnelo.desktop" "$appdir/usr/share/applications/yarnelo.desktop"
install -m 644 "$repo/assets/Yarnelo-Icon-256.png" "$appdir/yarnelo.png"
install -m 644 "$repo/assets/Yarnelo-Icon-256.png" "$appdir/usr/share/icons/hicolor/256x256/apps/yarnelo.png"
ln -s yarnelo.png "$appdir/.DirIcon"
APPIMAGE_EXTRACT_AND_RUN=1 ARCH=x86_64 VERSION="$version" \
    "$appimagetool" --no-appstream --runtime-file "$appimage_runtime" \
    "$appdir" "$output/Yarnelo-Linux-x86_64.AppImage"
chmod 755 "$output/Yarnelo-Linux-x86_64.AppImage"

dpkg-deb --info "$output/Yarnelo-Linux-amd64.deb"
rpm -qpi "$output/Yarnelo-Linux-x86_64.rpm"
if $smoke_test; then
    APPIMAGE_EXTRACT_AND_RUN=1 QT_QPA_PLATFORM=offscreen \
        "$output/Yarnelo-Linux-x86_64.AppImage" --smoke-test
fi
printf 'Built Linux x86_64 packages in %s\n' "$output"
sha256sum "$output/Yarnelo-Linux-x86_64.tar.gz" "$output/Yarnelo-Linux-x86_64.AppImage" \
    "$output/Yarnelo-Linux-amd64.deb" "$output/Yarnelo-Linux-x86_64.rpm"
