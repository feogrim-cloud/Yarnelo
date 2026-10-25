# Yarnelo for Linux — preview

[English app overview](../README.md) · [Описание на русском](../README.ru.md) · [English installation](../docs/LINUX.md) · [Установка на русском](../docs/LINUX.ru.md)

A native **Python 3.12 / PySide6 Essentials / Qt 6** dock, with no embedded browser or web server. It supports up to 30 shortcuts, one to four preferred rows with automatic overflow rows, four screen-edge positions, square-to-rounded backgrounds, adjustable opacity, icon size and magnification, animated hover/click/rearrangement, optional startup at sign-in, and English/Russian menus and settings.

## Ready-made packages

Use the [Linux installation guide](../docs/LINUX.md) for distribution packages and portable launchers. The TAR.GZ must be extracted completely: `yarnelo` needs its adjacent `_internal` folder. Ready-made packages do not need a separately installed Python runtime.

## Run from source

Use Python 3.12 or newer with a PySide6-Essentials wheel available for your architecture. Python 3.11 and earlier are rejected before loading Qt, because the upstream ARM64 Qt binding is incompatible with their reference-count implementation. Ready-made packages bundle a compatible runtime and do not change the system Python. From the repository root:

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
- **Settings → Position** or the right-click **Position** submenu places the dock at the top, bottom, left or right center. Side docks are vertical and keep icons upright. A selected edge remains centered when the screen or dock size changes. Drag the handle or unused background to switch to free positioning; the handle is at the top for a side dock and on the left for a horizontal dock. The menu can also select a monitor. Wayland window managers may restrict positioning.
- Right-click → **Language / Язык → English / Русский** switches the interface without restarting and saves the choice. The initial default is Russian for a Russian locale, English otherwise.
- Settings use two columns without a vertical scrollbar. They control preferred rows, background opacity (icons keep their contrast), corner rounding, size, magnification and labels. Corner rounding **0** gives square corners; **100** makes the background fully rounded.
- **Settings → Theme → OK** selects Dusk, Frost, Graphite, Ember or Aurora for the dock, menus and settings. **Cancel** leaves saved settings unchanged. Dusk preserves the original dock colors, motion and your background when upgrading; each theme remembers its own opacity and corner rounding. All five share modern settings controls. Initial Frost/Graphite/Ember/Aurora presets are respectively **76% / 22**, **96% / 12**, **85% / 16**, **88% / 24**. Icon size, magnification, rows, position and behavior preferences are preserved.
- Each theme tunes hover, click and drag motion. Aurora's edge highlight is interaction-only; the dock has no idle flare. Palettes use native Qt colors, translucency, borders and shadows, including contrasting text on Frost's light controls. [Theme details](../docs/LINUX.md#themes).
- **Show labels** enables compact one-line captions. Long captions are shortened to fit; hovering an icon shows its name. Existing label preferences are retained.
- **Settings → Start when signing in → OK** enables current-user XDG autostart; the default is off. **Cancel** does not change the entry. Keep portable/source copies in a permanent location before enabling it. [Startup and removal details](../docs/LINUX.md#startup-at-sign-in).
- **Automatically add new desktop shortcuts** is also off by default. When enabled with **OK**, it adds references only to new `.desktop` launchers appearing while Yarnelo is running. Existing icons, ordinary files and folders are ignored; originals are unchanged. Duplicates are skipped, and launchers seen at the 30-icon limit stay on the desktop without a deferred import queue.
- If the dock would extend beyond the available screen area, Yarnelo adds rows automatically before reducing the chosen icon size. For side docks it adds columns. Your preferred one-to-four-row setting is kept, so a larger screen restores it. On very small screens, icons shrink only if no arrangement fits at the requested size.
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

The smoke test creates a Qt application and renders the dock in memory. It also checks all four positions with 30 shortcuts, legacy settings, background rounding, and unclipped English/Russian settings at 1280×720, 640×480 and 640×360 logical work areas. With `--smoke-output`, settings previews are saved beside the requested dock PNG. Startup tests use isolated XDG folders to check registration, command quoting, persistent AppImage/source launch paths, disabling and ownership; they do not alter the developer's own startup entries. These checks do not verify a particular window manager, actual sign-in, real app launches, games or system Trash.

Новые настройки: **скругление углов** от 0 (прямые) до 100 (полное), положение **сверху / снизу / слева / справа по центру** и свободное перемещение за ручку. По бокам панель становится вертикальной, а значки остаются прямыми. Если ярлыки не помещаются, панель сначала добавляет ряды или боковые столбцы; выбранные 1–4 ряда сохраняются как предпочтение. Окно настроек состоит из двух столбцов без вертикальной прокрутки. Ограничения X11/XWayland, описанные выше, продолжают действовать.

## Build packages

Supported native binary build baselines are **Ubuntu 22.04 x86_64 / glibc 2.35** and **Ubuntu 24.04 ARM64 / glibc 2.39**. Official PySide6 Essentials 6.12.0 and Shiboken ARM64 wheels need the newer glibc baseline: Debian 12 / Ubuntu 22.04 ARM64 cannot run these ready-made packages. On ARM64, use Ubuntu 24.04+, Debian 13+ or another graphical distribution with glibc 2.39+ and the required libraries. Raspberry Pi hardware alone does not establish compatibility; the OS must be 64-bit and meet the same requirements.

The GitHub Actions workflow installs the pinned Python 3.12.15, PySide6 Essentials 6.12.0, PyInstaller 6.22.3 and fpm versions and fetches verified official AppImage tools for each architecture. Python 3.12+ provides the runtime ABI required by the Qt wheels ([Qt development notes, PYSIDE-3424](https://wiki.qt.io/Qt_for_Python_Development_Notes#22._Oktober_2026)). The smoke test checks wrapped calls for reference-count loss and verifies normal Qt object destruction before exiting. Refer to [the workflow](../.github/workflows/build.yml) for its system dependencies and exact build environment. Package builds are native; cross-compiling Python/Qt bundles is not supported by this script.

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
