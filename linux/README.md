# Yarnelo for Linux — preview

[English app overview](../README.md) · [Описание на русском](../README.ru.md) · [English installation](../docs/LINUX.md) · [Установка на русском](../docs/LINUX.ru.md)

A native **Python 3.11 / PySide6 Essentials / Qt 6** dock, with no embedded browser or web server. It supports up to 30 shortcuts, one to four rows, adjustable background opacity, icon size and magnification, animated hover/click/rearrangement, and English/Russian menus and settings.

## Ready-made packages

Use the [Linux installation guide](../docs/LINUX.md) for distribution packages and portable launchers. The TAR.GZ must be extracted completely: `yarnelo` needs its adjacent `_internal` folder. Ready-made packages do not need a separately installed Python runtime.

## Run from source

Use Python 3.11 or newer with a PySide6-Essentials wheel available for your architecture. From the repository root:

```bash
python3 -m venv .venv
. .venv/bin/activate
python -m pip install -r linux/requirements.txt
python linux/yarnelo.py
```

System requirements include a graphical desktop, Qt/X11 or Wayland libraries, `xdg-open`, `gio` for `.desktop` launchers (usually part of GLib), and a file manager supporting `trash:///` for Trash, often through GVfs. Windows binaries do not run as Linux apps.

## Use and data

- Drop a `.desktop` file, ordinary file or folder from the file manager, or use **+**. Computer opens the filesystem root; Trash uses the desktop file manager.
- Drop operations **only add references**. Yarnelo never removes or moves original files. To clear the desktop, move originals yourself to a permanent folder, then add them from that location.
- Drag an icon inside the dock to rearrange it. Releasing outside cancels the move. Right-click an icon to open it, reveal its folder or remove its reference.
- Drag the left handle or unused background to move the dock. The menu can select a monitor or return it to the bottom center. Wayland window managers may restrict positioning.
- Right-click → **Language / Язык → English / Русский** switches the interface without restarting and saves the choice. The initial default is Russian for a Russian locale, English otherwise.
- Settings control rows, background opacity (icons keep their contrast), size, magnification and labels. The size shrinks automatically if 30 icons in a single row would not fit the monitor.
- Settings are at `$XDG_CONFIG_HOME/yarnelo/settings.json` or `~/.config/yarnelo/settings.json`. Writes are atomic; corrupt original data is preserved beside a fresh profile. Windows profiles are not automatically imported.
- One instance runs per user. Launching again shows the existing dock without taking focus. Exit through the dock or notification-area menu, where supported.

## Preview limitations

**X11 is recommended.** On X11, Yarnelo requests placement below ordinary windows and does not reserve screen space. Transparency requires compositing. The Linux preview has no “always on top” mode or keyboard/mouse hooks.

**Wayland is best effort through XWayland.** Ready-made packages include the X11 `xcb` platform plugin and need XWayland. Positioning, stacking above the desktop and notification-area support depend on the desktop/compositor. Use `QT_QPA_PLATFORM=xcb ./yarnelo` if needed. Native Qt/Wayland is available only when running from source with an installed platform plugin; the dock behaves as a separate window. The Linux preview does not reproduce Windows overlap/fullscreen-game detection or guarantee behavior in every game mode. Exit Yarnelo if a compositor puts it above a game.

Icons come from the desktop theme and each launcher's `Icon=` field. Sparse icon themes may show generic icons. `gio launch` handles launchers; Yarnelo does not interpret `Exec=` through a shell, but a launcher still runs its author's program. Add launchers you trust. Windows `.lnk` and `.url` files are not supported as Linux application launchers.

## Checks

```bash
python -m unittest discover -s linux/tests -v
python linux/yarnelo.py --version
QT_QPA_PLATFORM=offscreen python linux/yarnelo.py --smoke-test
# Optional image, without reading or modifying the user profile:
QT_QPA_PLATFORM=offscreen python linux/yarnelo.py --smoke-test --smoke-output /tmp/yarnelo-smoke.png
```

The smoke test creates a Qt application and renders the dock in memory. It does not verify a particular window manager, real app launches, games or system Trash.

## Build packages

Supported native binary build baselines are **Ubuntu 22.04 x86_64 / glibc 2.35** and **Ubuntu 24.04 ARM64 / glibc 2.39**. Official PySide6 Essentials 6.12.0 and Shiboken ARM64 wheels need the newer glibc baseline: Debian 12 / Ubuntu 22.04 ARM64 cannot run these ready-made packages. On ARM64, use Ubuntu 24.04+, Debian 13+ or another graphical distribution with glibc 2.39+ and the required libraries. Raspberry Pi hardware alone does not establish compatibility; the OS must be 64-bit and meet the same requirements.

The GitHub Actions workflow installs the pinned Python 3.11.17, PySide6 Essentials 6.12.0, PyInstaller 6.22.3 and fpm versions and fetches verified official AppImage tools for each architecture. Refer to [the workflow](../.github/workflows/build.yml) for its system dependencies and exact build environment. Package builds are native; cross-compiling Python/Qt bundles is not supported by this script.

With that environment prepared:

```bash
APPIMAGETOOL=/absolute/path/appimagetool-x86_64.AppImage \
APPIMAGE_RUNTIME=/absolute/path/runtime-x86_64 \
bash scripts/Package-Linux.sh --output dist --version "$(cat VERSION)" --smoke-test
```

On an **ARM64 Ubuntu 24.04 build host**, use the native ARM64 tools with the same script:

```bash
APPIMAGETOOL=/absolute/path/appimagetool-aarch64.AppImage \
APPIMAGE_RUNTIME=/absolute/path/runtime-aarch64 \
bash scripts/Package-Linux.sh --output dist --version "$(cat VERSION)" --smoke-test
```

The script detects `uname -m`. ARM64 outputs are `Yarnelo-Linux-aarch64.AppImage`, `Yarnelo-Linux-aarch64.tar.gz`, `Yarnelo-Linux-aarch64.rpm` and `Yarnelo-Linux-arm64.deb`; the Debian architecture name is `arm64`, while the other formats use `aarch64`. Existing x86_64 output names are unchanged. Both architectures retain the X11/XWayland preview limitations above.

The script uses **PyInstaller**, **appimagetool** and **fpm** to create TAR.GZ, AppImage, DEB and RPM packages. External tools fetched by `scripts/FetchReleaseAsset.py` are checked against the published SHA-256 digests. Builds include third-party notices, licenses and corresponding-source links. See the root [technology stack](../README.md#technology-stack) and [rights](../LICENSE).
