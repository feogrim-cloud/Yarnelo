# Install Yarnelo

**English** · [Русский](INSTALL.ru.md)

Download ready-made files from **Assets** on the [Releases page](https://github.com/feogrim-cloud/Yarnelo/releases). GitHub's **Source code** links download the source; choose a package below to install the app.

## Choose a download

| Your system | Recommended file | Alternative |
| --- | --- | --- |
| Windows 10 / 11, Intel or AMD, 64-bit | `Yarnelo-Windows-x64-Setup.exe` | `Yarnelo-Windows-x64-Portable.zip` |
| Windows 10, 32-bit | `Yarnelo-Windows-x86-Setup.exe` | `Yarnelo-Windows-x86-Portable.zip` |
| Windows 11, ARM64 (for example Snapdragon) | `Yarnelo-Windows-arm64-Setup.exe` | `Yarnelo-Windows-arm64-Portable.zip` |
| Ubuntu 22.04+, Debian 12+, Linux Mint 21+, x86_64 | `Yarnelo-Linux-amd64.deb` | AppImage or TAR.GZ |
| Fedora, x86_64 | `Yarnelo-Linux-x86_64.rpm` | AppImage or TAR.GZ |
| openSUSE with glibc 2.35+, x86_64 | `Yarnelo-Linux-x86_64.rpm` | AppImage or TAR.GZ; openSUSE installation has not been separately checked |
| Arch and other Linux distributions with glibc 2.35+, x86_64 | `Yarnelo-Linux-x86_64.AppImage` | `Yarnelo-Linux-x86_64.tar.gz` |
| Ubuntu 24.04+, Debian 13+, ARM64 | `Yarnelo-Linux-arm64.deb` | `Yarnelo-Linux-aarch64.AppImage` or TAR.GZ |
| Fedora with glibc 2.39+, ARM64 | `Yarnelo-Linux-aarch64.rpm` | `Yarnelo-Linux-aarch64.AppImage` or TAR.GZ |
| Other Linux distributions with glibc 2.39+, ARM64 | `Yarnelo-Linux-aarch64.AppImage` | `Yarnelo-Linux-aarch64.tar.gz` |

On Windows, open **Settings → System → About → System type**. Choose **x64** for Intel/AMD x64 processors, **ARM64** for ARM processors such as Snapdragon, or **x86** only for 32-bit Windows 10. Native ARM64 requires **Windows 11 and .NET Framework 4.8.1**; Windows 10 ARM64 is not supported by this package. Windows x64/x86 requires .NET Framework 4.8 or 4.8.1. The installer checks the required runtime.

On Linux, check both architecture and libraries:

```bash
uname -m
getconf GNU_LIBC_VERSION
```

`amd64` / `x86_64` means Intel/AMD 64-bit and requires **glibc 2.35+**. `arm64` / `aarch64` means ARM 64-bit and requires **glibc 2.39+**. A 64-bit ARM desktop, including a Raspberry Pi with a matching 64-bit graphical OS, must meet these requirements; neither the hardware name nor the AppImage format guarantees compatibility. Debian 12 and Ubuntu 22.04 satisfy the x86_64 baseline but **not the ARM64 baseline**. Python and Qt are included.

Windows 7 / 8.1, 32-bit Linux, Alpine/musl and Ubuntu 20.04 are outside this release's compatibility scope. Linux is a **preview**: X11 is recommended; Wayland sessions require XWayland for ready-made packages. See [Linux compatibility](LINUX.md#compatibility-and-desktop-environment).

## Windows

1. Run the appropriate `Setup.exe` and follow the wizard. Administrator rights are not required.
2. Open Yarnelo from the Start menu.
3. Drop shortcuts on the dock. Right-click the dock for settings.
4. Choose **Language → English / Русский** to change the interface language.

For the portable version, extract the **entire** ZIP to a permanent writable folder and run `Yarnelo.exe`.

Updating, startup at sign-in, shortcut storage and removal: [Windows guide](WINDOWS.md).

## Linux

In a terminal, open the folder containing the downloaded package and use the appropriate command.

Ubuntu / Debian / Mint:

```bash
sudo apt install ./Yarnelo-Linux-amd64.deb
yarnelo
```

Fedora:

```bash
sudo dnf install ./Yarnelo-Linux-x86_64.rpm
yarnelo
```

For **Linux ARM64** with glibc 2.39+, use the matching commands instead:

```bash
# Ubuntu 24.04+ / Debian 13+ ARM64:
sudo apt install ./Yarnelo-Linux-arm64.deb
# Fedora ARM64:
sudo dnf install ./Yarnelo-Linux-aarch64.rpm
# Or a portable AppImage:
chmod +x ./Yarnelo-Linux-aarch64.AppImage
./Yarnelo-Linux-aarch64.AppImage
```

Choose one installation method; do not install the Intel/AMD and ARM64 packages together.

openSUSE:

```bash
sudo zypper install ./Yarnelo-Linux-x86_64.rpm
yarnelo
```

AppImage, without system installation:

```bash
chmod +x ./Yarnelo-Linux-x86_64.AppImage
./Yarnelo-Linux-x86_64.AppImage
```

If AppImage reports that FUSE is unavailable:

```bash
APPIMAGE_EXTRACT_AND_RUN=1 ./Yarnelo-Linux-x86_64.AppImage
```

Use `Yarnelo-Linux-aarch64.AppImage` in the same workaround on ARM64.

Right-click the dock → **Language / Язык** to choose **English / Русский**. The selection is saved. The first launch chooses Russian for a Russian system locale and English otherwise.

Dependencies, TAR.GZ, application-menu entries, updates and removal: [Linux guide](LINUX.md).

## Verify downloaded files

Download `SHA256SUMS` from the same release into the folder containing the packages. On Linux:

```bash
sha256sum --check SHA256SUMS --ignore-missing
```

Downloaded files listed in the manifest should report `OK`. Use files and checksums from the same release.

On Windows, in PowerShell:

```powershell
Get-FileHash .\Yarnelo-Windows-x64-Setup.exe -Algorithm SHA256
```

Compare the result with the matching filename in `SHA256SUMS` or its adjacent `.sha256` file. Substitute the x86, arm64 or ZIP filename when appropriate. Windows files are currently unsigned.

## Rights

All rights to Yarnelo's own code and artwork are reserved. Third-party libraries retain their own licenses: see [LICENSE](../LICENSE), `THIRD-PARTY-NOTICES.txt`, the `licenses` directory and `corresponding-sources.txt` inside Linux packages.
