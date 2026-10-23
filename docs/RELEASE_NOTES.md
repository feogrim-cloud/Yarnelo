# Yarnelo 1.7.0

## English

This release adds **background corner rounding, four edge positions and automatic wrapping** on Windows and Linux. Existing shortcut profiles are preserved; all 14 Intel/AMD and native ARM64 packages remain available.

### New dock settings

- Set **Corner rounding** from **0** (square corners) to **100** (capsule), with intermediate values for softer corners.
- Settings now use **two columns without vertical scrolling** and fit the available screen space.
- Choose **Top, Bottom, Left or Right** centered placement. Side docks are vertical with upright icons. Drag the handle for free positioning.
- Choose one to four preferred rows. If the dock would exceed the screen, it automatically adds rows, or columns at a side edge, before reducing icon size. It returns to the preferred layout when there is room.

Right-click the dock → **Settings** to use these controls. Changes are saved immediately and are available in English and Russian.

![Yarnelo 1.7 settings](https://raw.githubusercontent.com/feogrim-cloud/Yarnelo/v1.7.0/assets/screenshots/yarnelo-settings-en.png)

### Downloads

| Your system | Installer / package | Portable option |
| --- | --- | --- |
| Windows 10/11 x64, Intel/AMD | `Yarnelo-Windows-x64-Setup.exe` | `Yarnelo-Windows-x64-Portable.zip` |
| Windows 10, 32-bit | `Yarnelo-Windows-x86-Setup.exe` | `Yarnelo-Windows-x86-Portable.zip` |
| **Windows 11 ARM64**, for example Snapdragon | **`Yarnelo-Windows-arm64-Setup.exe`** | **`Yarnelo-Windows-arm64-Portable.zip`** |
| Linux x86_64, glibc 2.35+ | `Yarnelo-Linux-amd64.deb` / `Yarnelo-Linux-x86_64.rpm` | `Yarnelo-Linux-x86_64.AppImage` / `Yarnelo-Linux-x86_64.tar.gz` |
| **Linux ARM64**, glibc 2.39+ | **`Yarnelo-Linux-arm64.deb` / `Yarnelo-Linux-aarch64.rpm`** | **`Yarnelo-Linux-aarch64.AppImage` / `Yarnelo-Linux-aarch64.tar.gz`** |

**Choose the correct architecture:** Windows → Settings → System → About → System type. Intel/AMD x64 uses x64; an ARM processor such as Snapdragon uses ARM64. On Linux, run `uname -m` and `getconf GNU_LIBC_VERSION`: `x86_64` / `amd64` and `aarch64` / `arm64` are different architectures.

- **Windows ARM64 requires Windows 11 and .NET Framework 4.8.1.** Windows 10 ARM64 is not supported by the native ARM64 package. Windows x64/x86 continues to use .NET Framework 4.8/4.8.1.
- **Linux ARM64 requires glibc 2.39+:** Ubuntu 24.04+, Debian 13+ or another matching graphical distribution. Debian 12 and Ubuntu 22.04 are supported by the x86_64 packages, **not** the ARM64 packages. All Linux packages include Python and Qt.
- **Linux remains a preview; X11 is recommended.** Ready-made packages need XWayland in Wayland sessions. Window placement and fullscreen behavior depend on the compositor; original `.desktop` files are not moved. ARM64 does not remove these limitations. 32-bit Linux and Alpine/musl are outside this release's compatibility scope.

### Quick installation

On Windows 11 ARM64, run `Yarnelo-Windows-arm64-Setup.exe`, or extract the entire ARM64 ZIP into a permanent writable folder and run `Yarnelo.exe`.

On a compatible Linux ARM64 desktop, choose **one** of the following:

```bash
# Ubuntu 24.04+ / Debian 13+ ARM64:
sudo apt install ./Yarnelo-Linux-arm64.deb
# Fedora ARM64:
sudo dnf install ./Yarnelo-Linux-aarch64.rpm
# Or AppImage:
chmod +x ./Yarnelo-Linux-aarch64.AppImage
./Yarnelo-Linux-aarch64.AppImage
```

If FUSE is unavailable, use `APPIMAGE_EXTRACT_AND_RUN=1 ./Yarnelo-Linux-aarch64.AppImage`. AppImage still needs the matching CPU architecture and glibc version.

Windows installers run per user and retain `Data` on update/removal. Update a portable copy in its original folder and preserve `Data`; a new installer does not import a separate portable profile. Windows EXE files are currently unsigned. Right-click the dock → **Language / Язык → English / Русский** to change the interface language.

**[Installation and update](https://github.com/feogrim-cloud/Yarnelo/blob/main/docs/INSTALL.md)** · [Windows](https://github.com/feogrim-cloud/Yarnelo/blob/main/docs/WINDOWS.md) · [Linux](https://github.com/feogrim-cloud/Yarnelo/blob/main/docs/LINUX.md)

## Русский

Добавлены **скругление фона, четыре положения у края экрана и автоматический перенос ярлыков** в Windows и Linux. Прежние профили сохранены; доступны все 14 пакетов Intel/AMD и нативных ARM64.

### Новые настройки панели

- **Скругление углов** от **0** (прямые углы) до **100** (капсула), с промежуточными значениями.
- Настройки размещены **в два столбца без вертикальной прокрутки**; окно подстраивается под доступное место на экране.
- Выберите расположение по центру **сверху, снизу, слева или справа**. Боковые панели вертикальные, значки остаются обычной ориентации. Перетаскивайте ручку для свободного размещения.
- Выберите от одного до четырёх предпочтительных рядов. При выходе за пределы экрана панель сначала добавит ряды, либо столбцы у бокового края, и только затем уменьшит значки. Когда место появится, вернётся выбранная раскладка.

Откройте правой кнопкой меню панели → **Настройки**. Изменения сохраняются сразу; все новые пункты доступны на русском и английском.

### Что скачать

| Ваша система | Установщик / пакет | Без установки |
| --- | --- | --- |
| Windows 10/11 x64, Intel/AMD | `Yarnelo-Windows-x64-Setup.exe` | `Yarnelo-Windows-x64-Portable.zip` |
| Windows 10, 32 бита | `Yarnelo-Windows-x86-Setup.exe` | `Yarnelo-Windows-x86-Portable.zip` |
| **Windows 11 ARM64**, например Snapdragon | **`Yarnelo-Windows-arm64-Setup.exe`** | **`Yarnelo-Windows-arm64-Portable.zip`** |
| Linux x86_64, glibc 2.35+ | `Yarnelo-Linux-amd64.deb` / `Yarnelo-Linux-x86_64.rpm` | `Yarnelo-Linux-x86_64.AppImage` / `Yarnelo-Linux-x86_64.tar.gz` |
| **Linux ARM64**, glibc 2.39+ | **`Yarnelo-Linux-arm64.deb` / `Yarnelo-Linux-aarch64.rpm`** | **`Yarnelo-Linux-aarch64.AppImage` / `Yarnelo-Linux-aarch64.tar.gz`** |

**Как определить архитектуру:** в Windows — Параметры → Система → О системе → Тип системы. Для Intel/AMD x64 выбирайте x64, для ARM-процессоров вроде Snapdragon — ARM64. В Linux выполните `uname -m` и `getconf GNU_LIBC_VERSION`: `x86_64` / `amd64` и `aarch64` / `arm64` — разные архитектуры.

- **Windows ARM64 требует Windows 11 и .NET Framework 4.8.1.** Windows 10 ARM64 не поддерживается нативным ARM64-пакетом. Для Windows x64/x86 по-прежнему нужен .NET Framework 4.8/4.8.1.
- **Linux ARM64 требует glibc 2.39+:** Ubuntu 24.04+, Debian 13+ либо другую подходящую графическую ОС. Debian 12 и Ubuntu 22.04 подходят для x86_64, **но не ARM64**. Python и Qt включены во все Linux-пакеты.
- **Linux остаётся preview; рекомендуется X11.** В Wayland-сеансе готовым сборкам нужен XWayland. Размещение панели и поведение с полноэкранными приложениями зависят от композитора; исходные `.desktop` не перемещаются. ARM64 не отменяет эти ограничения. 32-битный Linux и Alpine/musl не входят в совместимость выпуска.

### Краткая установка

В Windows 11 ARM64 запустите `Yarnelo-Windows-arm64-Setup.exe` либо распакуйте весь ARM64 ZIP в постоянную доступную для записи папку и откройте `Yarnelo.exe`.

На совместимом графическом Linux ARM64 выберите **один** способ:

```bash
# Ubuntu 24.04+ / Debian 13+ ARM64:
sudo apt install ./Yarnelo-Linux-arm64.deb
# Fedora ARM64:
sudo dnf install ./Yarnelo-Linux-aarch64.rpm
# Или AppImage:
chmod +x ./Yarnelo-Linux-aarch64.AppImage
./Yarnelo-Linux-aarch64.AppImage
```

Без FUSE используйте `APPIMAGE_EXTRACT_AND_RUN=1 ./Yarnelo-Linux-aarch64.AppImage`. AppImage всё равно требует подходящую архитектуру процессора и версию glibc.

Установщики Windows работают без прав администратора и сохраняют `Data` при обновлении и удалении. Переносную версию обновляйте в прежней папке, сохраняя `Data`; новый установщик не импортирует отдельный профиль автоматически. EXE пока не подписаны сертификатом разработчика. Язык интерфейса: правая кнопка по панели → **Язык / Language → Русский / English**.

**[Установка и обновление](https://github.com/feogrim-cloud/Yarnelo/blob/main/docs/INSTALL.ru.md)** · [Windows](https://github.com/feogrim-cloud/Yarnelo/blob/main/docs/WINDOWS.ru.md) · [Linux](https://github.com/feogrim-cloud/Yarnelo/blob/main/docs/LINUX.ru.md)

---

`SHA256SUMS` contains hashes for all **14 packages** / содержит контрольные суммы всех **14 пакетов**. Automated checks do not imply manual testing on every physical device, OS or desktop environment / Автоматические проверки не означают ручного тестирования каждого физического устройства, ОС и окружения рабочего стола.

Screenshots show example shortcuts only; the pictured apps/games are not bundled with Yarnelo / На скриншотах показаны примеры ярлыков; изображённые приложения и игры не входят в комплект Yarnelo.
