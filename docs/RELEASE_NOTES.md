# Yarnelo 1.8.1 — Modern Dusk controls / Современные элементы Dusk

## English

This release updates **Dusk's settings and menu controls to match the common style of the other themes**. Windows has thin accent sliders with round thumbs and rounded checkboxes; Linux uses the same slider, button and menu styling as the other four themes. Dusk retains its dock colors, motion, default opacity and saved backgrounds. All five themes, shortcut profiles, compact labels, startup and automatic addition of new desktop shortcuts are preserved; all 14 Intel/AMD and native ARM64 packages remain available.

### Themes

Choose **Settings → Theme**. Windows applies and saves changes immediately; Linux applies them when you choose **OK**, while **Cancel** leaves the saved settings unchanged.

| Theme | Appearance | First-use background opacity / corner rounding |
| --- | --- | --- |
| **Dusk** | The original translucent dark dock | Your existing settings and the original defaults |
| **Frost** | Light ice tones | 76% / 22 |
| **Graphite** | Matte charcoal | 96% / 12 |
| **Ember** | Warm bronze | 85% / 16 |
| **Aurora** | Indigo with a colored edge accent | 88% / 24 |

Each theme remembers its own opacity and corner rounding. Returning to Dusk brings back the background you had before trying another theme. Switching themes preserves icon size, magnification, rows, position, label visibility, startup and automatic desktop-shortcut settings. Menus and settings use the selected palette, including readable dark text on Frost's light surfaces.

Hover, click and drag motion follows each theme's feel. Aurora highlights its edge during interaction; the dock has no idle flare. The themes use native colors, translucency, borders and shadows. Local concept previews are design references; app screenshots show the actual native interface.

### Compact labels

Compact one-line captions from 1.7.3 remain available through **Settings → Show icon labels** on Windows or **Show labels** on Linux. Long captions are shortened to fit; hovering an icon shows its name. Themes preserve the compact icon spacing and your saved label preference.

### Linux startup

- Right-click the dock → **Settings → Start when signing in → OK**. The option is off by default. Clearing it and choosing **OK** disables startup; **Cancel** leaves the registration unchanged.
- Yarnelo uses `yarnelo.desktop` in your XDG autostart folder: `$XDG_CONFIG_HOME/autostart`, or `~/.config/autostart` by default. No administrator rights are needed, and system configuration and other applications' entries are preserved.
- The entry starts this copy of Yarnelo. AppImage uses its permanent outer file, not a temporary mount. Keep AppImage, TAR.GZ and source copies in a permanent location before enabling startup. Before moving a copy, turn the option off and choose **OK** from the original location; then move it, enable the option from the new location and choose **OK**.
- XDG-compliant desktops such as GNOME, KDE Plasma and Xfce generally support this mechanism. Timing depends on the desktop; Linux has no fixed 10-second delay. Automated registration and interface checks do not establish actual sign-in behavior on every desktop environment.

Before removing a Linux portable copy or package, turn the checkbox off and choose **OK**. Package removal preserves user configuration. Remove any duplicate startup commands you previously added manually. [Linux startup instructions](https://github.com/feogrim-cloud/Yarnelo/blob/main/docs/LINUX.md#startup-at-sign-in).

### New desktop shortcuts

**Settings → Automatically add new desktop shortcuts** is off by default, so adding stays manual unless you opt in. It watches only new Windows `.lnk` / `.url` or Linux `.desktop` launchers appearing while Yarnelo is running; switching it on does not import existing desktop icons. Ordinary files and folders are ignored. Files being written are allowed to settle before import, and existing dock items are not duplicated.

The dock still holds at most 30 icons. A shortcut found when it is full stays on the desktop and is not queued for later; add it manually after making space. Windows follows **Move desktop shortcuts into the dock** for personal shortcuts. Shared desktop shortcuts are copied without an automatic administrator prompt; use the explicit menu action for moving them. Linux adds references and leaves originals untouched.

### Windows startup

Windows retains the 1.7.1 current-user Task Scheduler registration: a **10-second logon delay**, the exact executable path and working directory, ordinary account permissions, battery support and no execution time limit. Manage it through **Settings → Start when signing in to Windows**. Enabled legacy registrations belonging to this installation migrate after the new task is verified; disabled choices and other installations are preserved.

The task is named `Yarnelo.Logon.<SID>` in **Task Scheduler Library**. Scheduled tasks do not normally appear in **Task Manager → Startup apps**. [Windows startup instructions](https://github.com/feogrim-cloud/Yarnelo/blob/main/docs/WINDOWS.md#startup-at-sign-in). Corner rounding, two-column settings, edge placement and automatic shortcut wrapping remain available on both platforms.

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

**Элементы настроек и меню Dusk приведены к общему стилю остальных тем**. В Windows используются тонкие акцентные ползунки с круглыми бегунками и скруглённые галочки; в Linux — то же оформление ползунков, кнопок и меню, что у остальных четырёх тем. Цвета панели Dusk, анимации, непрозрачность по умолчанию и сохранённый фон остаются прежними. Все пять тем, профили ярлыков, компактные подписи, автозапуск и автоматическое добавление новых ярлыков сохраняются; доступны все 14 пакетов Intel/AMD и нативных ARM64.

### Темы

Выберите **Настройки → Тема**. В Windows изменения применяются и сохраняются сразу. В Linux нажмите **OK** для применения; **«Отмена»** оставляет сохранённые настройки без изменений.

| Тема | Вид | Непрозрачность / скругление при первом выборе |
| --- | --- | --- |
| **Dusk («Сумерки», базовая)** | Прежняя полупрозрачная тёмная панель | Ваши текущие настройки и прежние значения по умолчанию |
| **Frost («Матовое стекло»)** | Светлые ледяные оттенки | 76% / 22 |
| **Graphite («Матовый графит»)** | Матовый угольный фон | 96% / 12 |
| **Ember («Тёмная бронза»)** | Тёплая бронза | 85% / 16 |
| **Aurora («Ночной индиго»)** | Индиго с цветным акцентом по краю | 88% / 24 |

Каждая тема запоминает собственные непрозрачность и скругление. Возврат к Dusk восстанавливает фон, который был выбран до знакомства с другими темами. Переключение сохраняет размер значков, увеличение, ряды, положение, показ подписей, автозапуск и автоматическое добавление ярлыков. Меню и настройки используют выбранную палитру; на светлых поверхностях Frost остаётся контрастный тёмный текст.

Характер анимаций наведения, нажатия и перетаскивания зависит от темы. Край Aurora подсвечивается при взаимодействии; в покое панель не переливается. Для оформления используются нативные цвета, полупрозрачность, границы и тени. Локальные концепты служат примерами дизайна; скриншоты приложения показывают настоящий нативный интерфейс.

### Компактные подписи

Компактные однострочные подписи из 1.7.3 доступны через **Настройки → Подписи под значками** в Windows или **«Показывать подписи»** в Linux. Длинные подписи сокращаются по ширине; название также видно при наведении на значок. Темы сохраняют компактные отступы и выбранный показ подписей.

### Автозапуск Linux

- Правая кнопка по панели → **Настройки → Запускать при входе в систему → OK**. По умолчанию галочка выключена. Чтобы отключить автозапуск, снимите её и нажмите **OK**; **«Отмена»** оставляет регистрацию без изменений.
- Yarnelo использует `yarnelo.desktop` в вашей папке автозапуска XDG: `$XDG_CONFIG_HOME/autostart` или по умолчанию `~/.config/autostart`. Права администратора не нужны; системные настройки и записи других приложений сохраняются.
- Запись запускает именно эту копию Yarnelo. Для AppImage используется постоянный внешний файл, а не временная папка монтирования. Перед включением разместите AppImage, TAR.GZ или исходную версию в постоянной папке. Перед переносом снимите галочку и нажмите **OK** из прежнего места; затем переместите программу, включите галочку из нового места и нажмите **OK**.
- Механизм обычно поддерживается окружениями с автозапуском XDG, в том числе GNOME, KDE Plasma и Xfce. Момент запуска зависит от окружения; фиксированной задержки 10 секунд в Linux нет. Автоматические проверки регистрации и интерфейса не подтверждают вход в систему во всех окружениях рабочего стола.

Перед удалением переносной версии или пакета Linux снимите галочку и нажмите **OK**. Удаление пакета сохраняет пользовательские настройки. Если ранее вы вручную добавляли отдельные команды автозапуска, удалите дубликаты. [Инструкция автозапуска Linux](https://github.com/feogrim-cloud/Yarnelo/blob/main/docs/LINUX.ru.md#автозапуск-при-входе).

### Новые ярлыки рабочего стола

Галочка **Настройки → Автоматически добавлять новые ярлыки с рабочего стола** по умолчанию выключена: добавление остаётся ручным, пока вы не включите её. Учитываются только новые `.lnk` / `.url` Windows или `.desktop` Linux, появившиеся во время работы Yarnelo; включение не импортирует прежние значки. Обычные файлы и папки игнорируются. Программа дожидается окончания записи ярлыка и не создаёт дубликаты уже добавленных значков.

Лимит остаётся прежним — 30 значков. Если панель заполнена, новый ярлык остаётся на рабочем столе и не откладывается в очередь; после освобождения места добавьте его вручную. Для личных ярлыков Windows действует настройка **«Переносить ярлыки с рабочего стола в панель»**. Общие ярлыки копируются без автоматического запроса прав администратора; для их переноса используйте отдельную команду меню. Linux добавляет ссылки и оставляет оригиналы на месте.

### Автозапуск Windows

В Windows сохранён механизм 1.7.1: задача планировщика текущего пользователя с **задержкой входа 10 секунд**, точным путём к программе и рабочей папкой, обычными правами, работой от батареи и без ограничения времени выполнения. Управление — **Настройки → Запускать при входе в Windows**. Включённые прежние записи этой установки переносятся после проверки новой задачи; отключённый выбор и другие установки сохраняются.

В **Библиотеке планировщика заданий** задача называется `Yarnelo.Logon.<SID>`. Задачи планировщика обычно не отображаются в **Диспетчере задач → Автозагрузка приложений**. [Инструкция автозапуска Windows](https://github.com/feogrim-cloud/Yarnelo/blob/main/docs/WINDOWS.ru.md#автозапуск-при-входе). Скругление фона, настройки в два столбца, положения у краёв и автоматический перенос ярлыков сохранены на обеих платформах.

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
