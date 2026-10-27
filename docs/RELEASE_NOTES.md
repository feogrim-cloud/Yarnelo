# Yarnelo 1.9.2 — Reliability and security fixes / Исправления устойчивости и безопасности

## English

This release fixes **all six audit findings**, improves transfer of installer-created Windows desktop shortcuts, and includes the Windows taskbar-tooltip fix from 1.9.1. Valid existing profiles keep their shortcuts, settings, five themes, smooth transitions and startup choice.

### Fixed

1. **Windows icon memory:** removed shortcuts no longer remain in an ever-growing cache. Pending paths are bounded, one extraction runs at a time, and stale results cannot repopulate removed items.
2. **Windows shared-shortcut safety:** the elevated helper no longer saves user-profile settings. The ordinary dock journals and reconciles changes with normal permissions, removing the elevated profile-write path exposed by directory substitution. The audit did not establish remote code execution or a UAC bypass.
3. **Windows over-capacity profiles:** damaged or edited profiles containing more than 30 items no longer create controls outside the supported layout.
4. **Windows duplicate item IDs:** invalid duplicate-ID profiles are detected instead of allowing removal of a different item; item lookup prefers the exact selected object.
5. **Linux invalid settings:** wrong field types, excessively large numbers and malformed JSON follow the settings-error path instead of escaping as uncaught startup exceptions.
6. **Linux launcher output:** unused stdout goes to the null device and stderr is drained into a bounded diagnostic tail, preventing long-lived launchers from retaining unlimited output.
7. **Installer-created Windows shortcuts:** personal transfers retry temporary locks and matching recreated copies; changed copies stay on the desktop with a notice. With transfer enabled, new shared imports added automatically or manually queue approval only for those newly added shortcuts. The request waits for a visible dock and active desktop/Yarnelo, outside fullscreen apps, settings, menus and dragging. Canceled or failed requests require **+ → Move shared shortcuts into the dock** to retry; older pending shared transfers are not automatically enrolled.

Invalid Windows profiles are preserved for recovery, including managed shortcut files. Yarnelo tries a valid settings backup; if none is usable, it opens defaults with a notice. It does not silently truncate excess entries or renumber ownership IDs. Normal profiles retain their IDs, paths, order, background and other preferences.

### Preserved behavior

Windows 11 taskbar hover labels no longer hide the dock. Normal overlap, fullscreen hiding and multi-monitor behavior remain intact. All five themes, smooth transitions, compact labels, rows, placement and automatic desktop-shortcut preferences remain available.

Startup stays a separate setting: Windows uses `Yarnelo.Logon.<SID>` with a 10-second sign-in delay and ordinary permissions; Linux uses its owned XDG `yarnelo.desktop` entry. Keep AppImage/TAR.GZ/portable files in their permanent location before enabling startup. Installed copies should be opened through their normal shortcuts or sign-in task; automation can use the independent launcher.

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

New Windows Setup installations default to **Program Files** and request administrator approval; current-user installation is also available. An update reuses the existing scope/folder, closes that copy gracefully, and preserves native `%LOCALAPPDATA%\PolkaDock` or an existing portable marker and `Data`. Keep the original folder selected. A separate portable profile is not imported automatically. Update a portable ZIP in its original folder and preserve `Data` and `portable.mode`.

Uninstall restores this installation's owned moved `.lnk` / `.url` shortcuts without overwriting desktop files and stops before deleting program files if restoration fails. The profile and library remain; after successful restoration, only this installation's startup registration is removed. Windows EXE files are currently unsigned; compare downloads against **SHA256SUMS**. Right-click the dock → **Language / Язык → English / Русский** to change the interface language.

**[Installation and update](https://github.com/feogrim-cloud/Yarnelo/blob/main/docs/INSTALL.md)** · [Windows](https://github.com/feogrim-cloud/Yarnelo/blob/main/docs/WINDOWS.md) · [Linux](https://github.com/feogrim-cloud/Yarnelo/blob/main/docs/LINUX.md)

## Русский

Выпуск исправляет **все шесть замечаний аудита**, улучшает перенос созданных установщиками ярлыков Windows и включает исправление подсказок панели задач Windows из 1.9.1. В корректных существующих профилях сохраняются ярлыки, настройки, пять тем, плавные переходы и выбор автозапуска.

### Исправлено

1. **Память иконок Windows:** удалённые ярлыки больше не остаются в растущем кэше. Ожидающие пути ограничены, загрузка выполняется по одной, а запоздалые результаты не возвращают удалённые элементы.
2. **Безопасность общих ярлыков Windows:** повышенный помощник больше не сохраняет настройки пользовательского профиля. Обычная панель ведёт журнал и согласует изменения без повышения прав, исключая выявленный путь записи в подменённую папку с повышенными правами. Аудит не доказал удалённое выполнение кода или обход UAC.
3. **Переполненные профили Windows:** повреждённый или отредактированный профиль с более чем 30 элементами больше не создаёт значки за пределами поддерживаемой раскладки.
4. **Повторяющиеся ID Windows:** некорректные профили с одинаковыми ID распознаются, чтобы удаление одного элемента не затрагивало другой; поиск сначала учитывает точный выбранный объект.
5. **Некорректные настройки Linux:** ошибочные типы полей, чрезмерные числа и повреждённый JSON обрабатываются механизмом ошибок настроек вместо необработанного исключения при запуске.
6. **Вывод запускателей Linux:** ненужный stdout направляется в нулевое устройство, stderr читается по мере поступления с сохранением ограниченного хвоста диагностики. Длительно работающий запускатель больше не накапливает неограниченный вывод в памяти Yarnelo.
7. **Ярлыки установщиков Windows:** перенос личных ярлыков повторяется после временной блокировки и для заново созданных совпадающих копий; изменённые файлы остаются на рабочем столе с уведомлением. При включённом переносе запрос прав ставится в очередь только для новых общих ярлыков, добавленных автоматически или вручную. Он ждёт видимой панели и активного рабочего стола/Yarnelo, вне полноэкранных приложений, настроек, меню и перетаскивания. Отменённые или неудачные запросы повторяются через **+ → Перенести общие ярлыки в панель**; прежние ожидающие общие ярлыки автоматически не включаются.

Некорректные профили Windows сохраняются для восстановления вместе с файлами управляемых ярлыков. Yarnelo пробует корректную резервную копию настроек; если её нет, открывает настройки по умолчанию с уведомлением. Лишние записи не обрезаются безвозвратно, ID принадлежности не перенумеровываются. В обычном профиле сохраняются ID, пути, порядок, фон и остальные предпочтения.

### Сохранённое поведение

Подписи при наведении на значки панели задач Windows 11 больше не скрывают панель. Обработка обычных перекрывающих окон, полноэкранных приложений и нескольких мониторов сохранена. По-прежнему доступны пять тем, плавные переходы, компактные подписи, ряды, положения панели и автоматическое добавление новых ярлыков.

Автозапуск включается отдельно: Windows использует `Yarnelo.Logon.<SID>` с задержкой входа 10 секунд и обычными правами; Linux — принадлежащую Yarnelo запись XDG `yarnelo.desktop`. AppImage/TAR.GZ/переносные файлы нужно заранее поместить в постоянную папку. Установленную копию открывайте её обычным ярлыком или задачей входа; для автоматизации доступен независимый запускатель.

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

Новая установка Windows через Setup по умолчанию использует **Program Files** и запрашивает права администратора; доступна установка только для текущего пользователя. Обновление использует прежний вариант и папку, мягко закрывает эту копию и сохраняет обычный профиль `%LOCALAPPDATA%\PolkaDock` либо существующий переносной маркер и `Data`. Оставьте прежнюю папку выбранной. Отдельный переносной профиль автоматически не импортируется. ZIP обновляйте в прежней папке, сохраняя `Data` и `portable.mode`.

Удаление возвращает принадлежащие установке перемещённые ярлыки `.lnk` / `.url`, не перезаписывает файлы рабочего стола и останавливается до удаления программы при ошибке восстановления. Прежний профиль и библиотека остаются; после успешного восстановления удаляется только автозапуск этой установки. EXE пока не подписаны сертификатом разработчика; сверяйте загрузки с **SHA256SUMS**. Язык интерфейса: правая кнопка по панели → **Язык / Language → Русский / English**.

**[Установка и обновление](https://github.com/feogrim-cloud/Yarnelo/blob/main/docs/INSTALL.ru.md)** · [Windows](https://github.com/feogrim-cloud/Yarnelo/blob/main/docs/WINDOWS.ru.md) · [Linux](https://github.com/feogrim-cloud/Yarnelo/blob/main/docs/LINUX.ru.md)

---

`SHA256SUMS` contains hashes for all **14 packages** / содержит контрольные суммы всех **14 пакетов**. Automated checks do not imply manual testing on every physical device, OS or desktop environment / Автоматические проверки не означают ручного тестирования каждого физического устройства, ОС и окружения рабочего стола.

Screenshots show example shortcuts only; the pictured apps/games are not bundled with Yarnelo / На скриншотах показаны примеры ярлыков; изображённые приложения и игры не входят в комплект Yarnelo.
