# Yarnelo for Linux: installation and use

**English** · [Русский](LINUX.ru.md)

Yarnelo for Linux is a **Qt 6 preview** with up to 30 shortcuts, one to four rows, adjustable background opacity and icon size, smooth hover magnification and animated rearrangement. Ready-made packages include Python and Qt.

Download packages from [feogrim-cloud/Yarnelo releases](https://github.com/feogrim-cloud/Yarnelo/releases). Also download `SHA256SUMS` and run `sha256sum --check SHA256SUMS --ignore-missing` in the same folder.

## Compatibility and desktop environment

Packages target **x86_64 with glibc 2.35 or newer**: Ubuntu 22.04+, Debian 12+, Mint 21+ and sufficiently recent Fedora/openSUSE/Arch. This is a library compatibility baseline, not a claim of testing every distribution release. Check your architecture and glibc with:

```bash
uname -m
getconf GNU_LIBC_VERSION
```

Expect `x86_64` and `glibc 2.35` or newer. These binaries do not support ARM64, 32-bit Linux, Alpine/musl or Ubuntu 20.04. A graphical desktop is required.

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

Using `apt install ./file.deb` lets the package manager install dependencies. Yarnelo appears in the application menu. Program files are in `/opt/yarnelo`, and the launcher is `/usr/bin/yarnelo`.

## Fedora: RPM

```bash
sudo dnf install ./Yarnelo-Linux-x86_64.rpm
yarnelo
```

The RPM declares system-library SONAME requirements. It installs the app in `/opt/yarnelo`, the `yarnelo` command and an application-menu entry.

## openSUSE: RPM

On a sufficiently recent release with glibc 2.35+:

```bash
sudo zypper install ./Yarnelo-Linux-x86_64.rpm
yarnelo
```

This is the same RPM. Installation on openSUSE has not been separately checked; do not bypass dependency checks if the package manager cannot resolve them. AppImage or TAR.GZ with the required system libraries is another option.

## AppImage: a single executable file

```bash
chmod +x ./Yarnelo-Linux-x86_64.AppImage
./Yarnelo-Linux-x86_64.AppImage
```

Administrator rights are not needed. Put the file in a permanent folder, such as `~/Applications`, before creating a launcher. AppImage does not add an application-menu entry or enable startup automatically.

If FUSE is unavailable, use built-in extraction and launch:

```bash
APPIMAGE_EXTRACT_AND_RUN=1 ./Yarnelo-Linux-x86_64.AppImage
```

This extracts temporary files and removes them after exit. TAR.GZ below also works without FUSE.

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

The Linux preview has no startup-at-sign-in switch. Add the command through your desktop's startup settings, or put an adjusted `.desktop` file in `$XDG_CONFIG_HOME/autostart` (usually `~/.config/autostart`).

## Shortcuts, language and settings

Drop a `.desktop` file, ordinary file or folder on the dock. **+** and the context menu also add Computer and Trash. Computer opens the filesystem root. Drag icons to rearrange them. Use **Settings…** for row count, background opacity, icon size, magnification and labels.

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

Updates and removal preserve your user profile. Manually added launchers and startup entries are removed separately. Your own files, folders and `.desktop` originals are unaffected.

## Development and licenses

Source setup and interface checks are in [linux/README.md](../linux/README.md). The packaging script `scripts/Package-Linux.sh` runs on Ubuntu 22.04 x86_64 and packages the same standalone GUI as TAR.GZ, AppImage, DEB and RPM.

All rights to Yarnelo's own code and artwork are reserved. Python, PySide6/Shiboken, Qt and other components retain their own licenses. Packages contain `licenses/`, `THIRD-PARTY-NOTICES.txt`, `build-versions.txt` and `corresponding-sources.txt`. Qt libraries are dynamically loaded and may be replaced with compatible modified versions under their licenses. See [LICENSE](../LICENSE).
