# Yarnelo for Linux: installation and use

**English** · [Русский](LINUX.ru.md)

Yarnelo for Linux is a **Qt 6 preview** with up to 30 shortcuts, one to four rows, adjustable background opacity and icon size, smooth hover magnification and animated rearrangement. Ready-made packages include Python and Qt.

Download packages from [feogrim-cloud/Yarnelo releases](https://github.com/feogrim-cloud/Yarnelo/releases). Also download `SHA256SUMS` and run `sha256sum --check SHA256SUMS --ignore-missing` in the same folder.

## Compatibility and desktop environment

There are separate native packages for **x86_64** and **ARM64**. Choose both the processor architecture and the required library baseline:

| Architecture shown by `uname -m` | Minimum glibc | Examples of matching systems | Package names |
| --- | --- | --- | --- |
| `x86_64` (Intel/AMD, also called `amd64`) | **2.35** | Ubuntu 22.04+, Debian 12+, Mint 21+ | `amd64.deb`, `x86_64.AppImage`, `x86_64.rpm`, `x86_64.tar.gz` |
| `aarch64` (ARM 64-bit, also called `arm64`) | **2.39** | Ubuntu 24.04+, Debian 13+, sufficiently recent Fedora / other distributions | `arm64.deb`, `aarch64.AppImage`, `aarch64.rpm`, `aarch64.tar.gz` |

These are library compatibility baselines, not a claim of testing every distribution release. Check your architecture and glibc with:

```bash
uname -m
getconf GNU_LIBC_VERSION
```

Match both values to the table. The ARM64 Qt wheels require a newer glibc than x86_64; **Debian 12 and Ubuntu 22.04 are not supported by the ARM64 binaries**. ARM hardware such as a Raspberry Pi needs a matching 64-bit graphical OS and libraries; 32-bit images, older Raspberry Pi OS releases and headless images are not automatically compatible. All ready-made packages require a graphical desktop. There are no 32-bit Linux, Alpine/musl or Ubuntu 20.04 binaries in this release.

**An X11 session with compositing is recommended.** The dock requests placement below ordinary windows and does not reserve screen space. Ready-made packages need XWayland in a Wayland session. If platform selection fails, request X11 explicitly:

```bash
QT_QPA_PLATFORM=xcb yarnelo
# Portable example:
QT_QPA_PLATFORM=xcb ./Yarnelo-Linux-x86_64.AppImage
```

Native Wayland is available only when running from source with the relevant Qt plugins and remains experimental. Window positioning, stacking and notification-area support depend on the desktop environment. The Linux preview does not yet reproduce Windows overlap/fullscreen detection. If your compositor places the dock above a game, exit Yarnelo through its menu.

Opening files requires `xdg-open`; launching `.desktop` files requires `gio`; Trash requires a file manager supporting `trash:///`, commonly through GVfs. DEB/RPM packages request the main dependencies automatically. Trash behavior depends on the file manager.

## Ubuntu, Debian and Linux Mint: DEB

In the folder containing the download:

```bash
sudo apt install ./Yarnelo-Linux-amd64.deb
yarnelo
```

For **ARM64 on Ubuntu 24.04+ / Debian 13+**, install the ARM64 DEB instead:

```bash
sudo apt install ./Yarnelo-Linux-arm64.deb
yarnelo
```

Using `apt install ./file.deb` lets the package manager install dependencies. Yarnelo appears in the application menu. Program files are in `/opt/yarnelo`, and the launcher is `/usr/bin/yarnelo`.

## Fedora: RPM

```bash
sudo dnf install ./Yarnelo-Linux-x86_64.rpm
yarnelo
```

The RPM declares system-library SONAME requirements. It installs the app in `/opt/yarnelo`, the `yarnelo` command and an application-menu entry.

On **Fedora ARM64 with glibc 2.39+**, use:

```bash
sudo dnf install ./Yarnelo-Linux-aarch64.rpm
yarnelo
```

## openSUSE: RPM

On a sufficiently recent release with glibc 2.35+:

```bash
sudo zypper install ./Yarnelo-Linux-x86_64.rpm
yarnelo
```

For ARM64 with glibc 2.39+, substitute `Yarnelo-Linux-aarch64.rpm`. Installation on openSUSE has not been separately checked; do not bypass dependency checks if the package manager cannot resolve them. AppImage or TAR.GZ for the matching architecture with the required system libraries is another option.

## AppImage: a single executable file

```bash
chmod +x ./Yarnelo-Linux-x86_64.AppImage
./Yarnelo-Linux-x86_64.AppImage
```

The **ARM64** equivalent, requiring glibc 2.39+, is:

```bash
chmod +x ./Yarnelo-Linux-aarch64.AppImage
./Yarnelo-Linux-aarch64.AppImage
```

Administrator rights are not needed. Put the file in a permanent folder, such as `~/Applications`, before creating a launcher. AppImage does not add an application-menu entry or enable startup automatically.

If FUSE is unavailable, use built-in extraction and launch:

```bash
APPIMAGE_EXTRACT_AND_RUN=1 ./Yarnelo-Linux-x86_64.AppImage
```

For ARM64, substitute `Yarnelo-Linux-aarch64.AppImage` in the same command. This extracts temporary files and removes them after exit. TAR.GZ below also works without FUSE. AppImage does not remove the architecture or minimum-glibc requirements.

If Qt reports that the `xcb` plugin cannot load, install missing system components on Ubuntu / Debian / Mint:

```bash
sudo apt install libxcb-cursor0 libxkbcommon-x11-0 libxcb-icccm4 \
  libxcb-image0 libxcb-keysyms1 libxcb-render-util0 libxcb-xinerama0 \
  libgl1 libegl1 xdg-utils libglib2.0-bin gvfs-backends
```

Use `QT_DEBUG_PLUGINS=1 ./Yarnelo-Linux-x86_64.AppImage` to identify the missing library. On other distributions, install the corresponding packages using their package manager.

## TAR.GZ: a portable folder, including Arch Linux

```bash
mkdir -p "$HOME/Applications"
tar -xzf ./Yarnelo-Linux-x86_64.tar.gz -C "$HOME/Applications"
"$HOME/Applications/Yarnelo/yarnelo"
```

On **ARM64**, use `Yarnelo-Linux-aarch64.tar.gz` instead. The extracted executable has the same path. Download only one architecture for your machine.

Extract the entire archive. The `yarnelo` executable needs the adjacent `_internal` folder; do not move the executable by itself. System Python is not required. X11/OpenGL/GLib libraries and `xdg-open` / `gio` remain system dependencies.

On Arch, install any missing components with the package manager and follow normal Arch upgrade practices; do not partially upgrade system libraries.

### Application-menu entry for portable packages

DEB/RPM create one automatically. For the TAR.GZ example above:

```bash
mkdir -p "$HOME/.local/share/applications"
cat > "$HOME/.local/share/applications/yarnelo.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=Yarnelo
Exec="$HOME/Applications/Yarnelo/yarnelo"
Icon=$HOME/Applications/Yarnelo/_internal/assets/Yarnelo-Icon-256.png
Terminal=false
Categories=Utility;
StartupNotify=false
EOF
```

Adjust `Exec` and `Icon` if you installed elsewhere. For AppImage, use its full path in `Exec` and save the [Yarnelo icon](https://github.com/feogrim-cloud/Yarnelo/blob/main/assets/Yarnelo-Icon-256.png) to a permanent folder for `Icon`. If you need the FUSE workaround, start the launcher command with `env APPIMAGE_EXTRACT_AND_RUN=1`, followed by the AppImage path.

## Startup at sign-in

Right-click the dock → **Settings… → Start when signing in**, then choose **OK**. The option is off by default. Clearing it and choosing **OK** disables startup; **Cancel** leaves the saved registration unchanged. The checkbox reads the current registration, including a disabled entry, rather than restoring an old preference automatically.

Yarnelo creates `yarnelo.desktop` in `$XDG_CONFIG_HOME/autostart`, or `~/.config/autostart` by default, for your account only. No administrator rights are needed. The entry starts this installed copy: a DEB/RPM executable, the complete TAR.GZ folder, the source interpreter and script, or the original AppImage file. An AppImage entry uses its permanent outer file, never its temporary mount/extraction folder; the extraction-without-FUSE launch mode is preserved when used.

**Choose a permanent location before enabling startup for AppImage, TAR.GZ or source copies.** Before moving the app or its Python environment, turn startup off and choose **OK** from the original location. Move it, then enable startup and choose **OK** from the new location. A temporary download or extraction folder will not work after its files are removed. If you already moved the app, inspect `Exec=` in your user `autostart/yarnelo.desktop`; after confirming that it points to your old Yarnelo copy, remove that stale file and enable startup from the new copy.

Turning the option off removes Yarnelo's own user entry. If its own system entry would otherwise start it, Yarnelo uses a current-user `Hidden=true` override. It does not edit `/etc` or other applications' entries. Remove any duplicate startup command you previously added manually through your desktop settings; on older Yarnelo releases, that manual method remains an option.

The desktop must support XDG autostart, as GNOME, KDE Plasma and Xfce generally do. Startup happens during the next graphical sign-in; timing and any delay are controlled by the desktop environment. Windows' fixed 10-second task delay does not apply on Linux. Automated checks cover entry creation/removal and settings behavior; they do not establish sign-in behavior on every desktop environment.

## Shortcuts, language and settings

Drop a `.desktop` file, ordinary file or folder on the dock. **+** and the context menu also add Computer and Trash. Computer opens the filesystem root. Drag icons to rearrange them. The two-column **Settings…** window provides background opacity, corner rounding (0 square, 100 capsule), icon size, magnification and labels without vertical scrolling.

Enable **Show labels** for compact one-line captions under the icons. Long captions are shortened to fit; hovering an icon shows its name. The saved preference is retained when updating.

**Automatically add new desktop shortcuts** is off by default. Enable it in Settings and choose **OK** to watch for new `.desktop` launchers on your desktop while Yarnelo is running. Existing icons are left alone when enabling or starting the app; ordinary files and folders are ignored. Launchers are allowed to settle before import, duplicates are skipped, and originals remain untouched. At the 30-icon limit, new launchers stay on the desktop and are not queued for later; make room and add them manually. Turning the option off returns to manual adding through drag-and-drop or **+**.

Choose top, bottom, left or right centered placement; side docks run vertically with upright icons. Drag the handle for free positioning. One to four preferred rows become columns at the sides. Extra rows/columns are added automatically when needed before icon size is reduced, without replacing the saved preference. Positioning remains subject to the X11/XWayland compositor limitations described above.

Choose **Language / Язык → English / Русский** in the dock's context menu to switch the interface immediately. The choice is saved. The initial default follows the system locale: Russian for a Russian locale, English otherwise. User file and shortcut names stay as they are.

The Linux version stores references and **does not move originals off the desktop**. To clear your desktop, move originals to a permanent folder first, then add them from that location. Removing an icon only removes Yarnelo's reference.

The profile is `$XDG_CONFIG_HOME/yarnelo/settings.json`, or `~/.config/yarnelo/settings.json` by default. Linux and Windows profiles are separate; Windows `.lnk` / `.url` files are not automatically imported as Linux launchers.

## Updating and removal

Before replacing files, exit Yarnelo through the dock or notification-area menu. Back up the `yarnelo` directory from your configuration folder.

- **DEB:** install the new `.deb` with `sudo apt install ./...`; remove with `sudo apt remove yarnelo`.
- **RPM / Fedora:** update with `sudo dnf upgrade ./Yarnelo-Linux-x86_64.rpm`; remove with `sudo dnf remove yarnelo`.
- **RPM / openSUSE:** install the new RPM with `sudo zypper install ./...`; remove with `sudo zypper remove yarnelo`.
- **AppImage:** replace the file and allow execution again with `chmod +x`; remove by deleting the file.
- **TAR.GZ:** extract the new version to the original permanent location; remove by deleting the program folder.

Before removing Yarnelo, clear **Settings → Start when signing in** and choose **OK** to remove its current-user startup registration. Package removal does not clean configuration inside user home folders. Updates and removal preserve your user profile; manually added menu launchers or duplicate startup commands are removed separately. Your own files, folders and `.desktop` originals are unaffected.

## Development and licenses

Source setup and interface checks are in [linux/README.md](../linux/README.md). The packaging script `scripts/Package-Linux.sh` builds natively on Ubuntu 22.04 x86_64 or Ubuntu 24.04 ARM64 and packages the same standalone GUI as TAR.GZ, AppImage, DEB and RPM. The architecture is detected from the build host; an x86_64 build is not converted to ARM64 by renaming files. Automated checks do not establish behavior on every physical ARM device, desktop environment or game.

All rights to Yarnelo's own code and artwork are reserved. Python, PySide6/Shiboken, Qt and other components retain their own licenses. Packages contain `licenses/`, `THIRD-PARTY-NOTICES.txt`, `build-versions.txt` and `corresponding-sources.txt`. Qt libraries are dynamically loaded and may be replaced with compatible modified versions under their licenses. See [LICENSE](../LICENSE).
