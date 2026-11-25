# Yarnelo 1.9.3 — Delete shortcuts / Удаление ярлыков

## English

Windows icon menus now offer two separate actions:

- **Delete shortcut** removes the dock entry and Yarnelo's owned imported `.lnk` / `.url` copy without returning anything to the desktop. Programs, ordinary files, folders and existing desktop originals stay in place.
- **Restore to desktop** returns a moved shortcut and removes it from the dock. This is the shorter name of the existing restoration command; existing files are never overwritten.

Use **Delete shortcut** to clear a stale dock icon after uninstalling a game or app. The automatic desktop-shortcut option monitors new shortcut files. Application uninstallations are not tracked automatically. Deleting a dock shortcut does not uninstall its target.

Existing shortcut profiles, order, settings, themes and startup choices are preserved. Linux already removes dock references without moving original launchers; its removal behavior is unchanged. All 14 package variants use version 1.9.3.

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

On Windows, run the matching `Setup.exe`, or extract the entire matching ZIP into a permanent writable folder and run `Yarnelo.exe`. Use x64 for 64-bit Intel/AMD, x86 for 32-bit Windows 10, and ARM64 for Windows 11 on ARM.

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

New Windows Setup installations default to **Program Files** and request administrator approval; current-user installation is also available. An update reuses the existing scope/folder, closes that copy gracefully, and preserves native `%LOCALAPPDATA%\PolkaDock` or an existing portable marker and `Data`. Keep the original folder selected. A separate portable profile is not imported automatically. Update a portable ZIP in its original folder and preserve `Data` and `portable.mode`.

Uninstall restores this installation's owned moved `.lnk` / `.url` shortcuts without overwriting desktop files and stops before deleting program files if restoration fails. The profile and library remain; after successful restoration, only this installation's startup registration is removed. Windows EXE files are currently unsigned; compare downloads against **SHA256SUMS**. Right-click the dock → **Language / Язык → English / Русский** to change the interface language.

**[Installation and update](https://github.com/feogrim-cloud/Yarnelo/blob/main/docs/INSTALL.md)** · [Windows](https://github.com/feogrim-cloud/Yarnelo/blob/main/docs/WINDOWS.md) · [Linux](https://github.com/feogrim-cloud/Yarnelo/blob/main/docs/LINUX.md)

## Русский

В меню значка Windows теперь две отдельные команды:

- **«Удалить ярлык»** убирает запись панели и собственную импортированную копию `.lnk` / `.url` Yarnelo без возврата на рабочий стол. Программы, обычные файлы, папки и оставшиеся на рабочем столе оригиналы не затрагиваются.
- **«Вернуть на рабочий стол»** возвращает перемещённый ярлык и убирает его из панели. Это короткое название прежней команды возврата; существующие файлы не перезаписываются.

Используйте **«Удалить ярлык»** для очистки панели после удаления игры или программы. Автоматическое добавление отслеживает новые файлы ярлыков, а не удаление приложений; в этом выпуске добавлена ручная очистка. Удаление ярлыка из панели не удаляет программу, на которую он ссылается.

Существующие профили ярлыков, порядок, настройки, темы и выбор автозапуска сохраняются. Linux уже убирает ссылки из панели без перемещения исходных ярлыков; его поведение удаления не изменено. Все 14 вариантов пакетов имеют версию 1.9.3.

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

В Windows запустите подходящий `Setup.exe` либо распакуйте весь ZIP нужной архитектуры в постоянную доступную для записи папку и откройте `Yarnelo.exe`. Выбирайте x64 для 64-битных Intel/AMD, x86 для 32-битной Windows 10 и ARM64 для Windows 11 на ARM.

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

Новая установка Windows через Setup по умолчанию использует **Program Files** и запрашивает права администратора; доступна установка только для текущего пользователя. Обновление использует прежний вариант и папку, мягко закрывает эту копию и сохраняет обычный профиль `%LOCALAPPDATA%\PolkaDock` либо существующий переносной маркер и `Data`. Оставьте прежнюю папку выбранной. Отдельный переносной профиль автоматически не импортируется. ZIP обновляйте в прежней папке, сохраняя `Data` и `portable.mode`.

Удаление возвращает принадлежащие установке перемещённые ярлыки `.lnk` / `.url`, не перезаписывает файлы рабочего стола и останавливается до удаления программы при ошибке восстановления. Прежний профиль и библиотека остаются; после успешного восстановления удаляется только автозапуск этой установки. EXE пока не подписаны сертификатом разработчика; сверяйте загрузки с **SHA256SUMS**. Язык интерфейса: правая кнопка по панели → **Язык / Language → Русский / English**.

**[Установка и обновление](https://github.com/feogrim-cloud/Yarnelo/blob/main/docs/INSTALL.ru.md)** · [Windows](https://github.com/feogrim-cloud/Yarnelo/blob/main/docs/WINDOWS.ru.md) · [Linux](https://github.com/feogrim-cloud/Yarnelo/blob/main/docs/LINUX.ru.md)

---

`SHA256SUMS` contains hashes for all **14 packages** / содержит контрольные суммы всех **14 пакетов**. Automated checks do not imply manual testing on every physical device, OS or desktop environment / Автоматические проверки не означают ручного тестирования каждого физического устройства, ОС и окружения рабочего стола.

Screenshots show example shortcuts only; the pictured apps/games are not bundled with Yarnelo / На скриншотах показаны примеры ярлыков; изображённые приложения и игры не входят в комплект Yarnelo.
