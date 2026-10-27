# Changelog / Изменения

## 1.9.2 — 2026-10-27

### English

- Fixed six audit findings: Windows icon-cache retention, strict profile-save directory substitution, over-capacity and duplicate-ID Windows profiles, Linux malformed-settings exceptions, and unbounded launcher output buffering.
- Fixed installer-created and recreated Windows shortcuts remaining on the desktop: personal transfers retry temporary locks, matching recreated copies are moved again, and changed copies are retained with a notice. New shared imports queue a scoped approval request until safe desktop/dock interaction; canceled or failed requests require manual retry, and older pending shared items are not automatically enrolled.
- Windows keeps only current icon paths and one extraction in flight; late results cannot repopulate removed items. Elevated shared-shortcut helpers no longer save user-profile settings; the ordinary dock journals and reconciles changes with normal permissions.
- Invalid Windows profiles with more than 30 items or duplicate IDs are preserved for recovery. A valid backup is used when available; otherwise defaults and a notice are shown. Managed shortcut files are retained. Linux invalid field types, huge numbers and malformed JSON follow its settings-error path; launcher diagnostics are bounded.
- Included the 1.9.1 Windows taskbar-tooltip fix and the independent automation launcher. Normal shortcut profiles, settings, themes and startup choices are preserved.
- Windows Setup now defaults to Program Files with administrator approval, offers current-user installation, and reuses the previous scope/folder. Existing native or portable profiles are preserved. Uninstall restores owned moved shortcuts without overwriting desktop files and stops if restoration fails.

### Русский

- Исправлены шесть замечаний аудита: удержание иконок в кэше Windows, подмена папки при строгом сохранении профиля, профили Windows с лишними элементами и повторяющимися ID, необработанные ошибки настроек Linux и неограниченное накопление вывода запускателей.
- Исправлено сохранение созданных и заново созданных установщиком ярлыков на рабочем столе: личные ярлыки повторно проверяются после временной блокировки, совпадающие копии переносятся снова, изменённые остаются с уведомлением. Для новых общих ярлыков ставится ограниченный ими запрос прав до подходящего взаимодействия с рабочим столом или панелью; отменённые и неудачные запросы повторяются только вручную, прежние ожидающие общие ярлыки автоматически не включаются.
- Windows хранит только иконки текущих путей и выполняет одну загрузку за раз; запоздалые результаты не возвращают удалённые элементы. Повышенные помощники для общих ярлыков больше не сохраняют настройки пользовательского профиля; обычная панель ведёт журнал и согласует изменения без повышения прав.
- Некорректные профили Windows с более чем 30 элементами или повторяющимися ID сохраняются для восстановления. При наличии используется корректная резервная копия; иначе показываются настройки по умолчанию и уведомление. Файлы управляемых ярлыков сохраняются. Ошибочные типы, чрезмерные числа и повреждённый JSON Linux обрабатываются через механизм ошибок настроек; объём диагностики запускателей ограничен.
- Включены исправление подсказок панели задач Windows из 1.9.1 и независимый запуск из автоматизации. Обычные профили ярлыков, настройки, темы и выбор автозапуска сохраняются.
- Установщик Windows по умолчанию использует Program Files с запросом прав администратора, позволяет установку для текущего пользователя и повторно использует прежний вариант и папку. Обычные и переносные профили сохраняются. Удаление возвращает принадлежащие установке перемещённые ярлыки без перезаписи файлов рабочего стола и останавливается при ошибке восстановления.

## 1.9.1 — 2026-10-26

### English

- Fixed Windows 11 taskbar hover labels hiding the dock. Shell XAML popups are recognized separately from normal application windows.
- Preserved hiding for fullscreen applications and ordinary windows covering the dock, including the existing multi-monitor behavior. Shortcut profiles, settings and startup preferences are unchanged.
- Linux behavior is unchanged in this patch.

### Русский

- Исправлено исчезновение панели при появлении подписей над значками панели задач Windows 11. Всплывающие XAML-окна оболочки распознаются отдельно от обычных окон приложений.
- Сохранено скрытие для полноэкранных приложений и обычных окон, перекрывающих панель, включая прежнее поведение на нескольких мониторах. Профили ярлыков, настройки и выбор автозапуска не изменены.
- Поведение Linux в этом исправлении не изменено.

## Developer tooling / Инструменты разработчика — 2026-10-26

- Added an independent Windows launcher for automation: a temporary current-user task starts Yarnelo outside the calling assistant's process lifetime, then its definition is removed. Startup preferences and installed profiles are preserved.
- Added an isolated regression check that closes a disposable caller's kill-on-close job and verifies the launched process survives. Application binaries and release version remain 1.9.0.
- Добавлен независимый запуск Windows из автоматизации: временная задача текущего пользователя запускает Yarnelo отдельно от времени жизни ассистента, затем её определение удаляется. Выбор автозапуска и установленные профили сохраняются.
- Добавлена изолированная проверка: закрытие собственной тестовой группы завершает обычного потомка, а независимо запущенный процесс продолжает работать. Исполняемые файлы приложения и версия релиза остаются 1.9.0.

## 1.9.0 — 2026-10-25

### English

- Added smooth theme transitions on Windows and Linux: palette colors, background opacity and corner rounding change gradually between the five themes.
- Rapid theme choices continue from the currently displayed colors toward the latest choice, keeping transitions continuous. Idle appearance remains still.
- Preserved per-theme backgrounds, icon size, magnification, rows, position, compact labels, startup and automatic desktop-shortcut settings. Windows still saves theme choices immediately; Linux still applies them with **OK**, while **Cancel** leaves saved settings unchanged.

### Русский

- В Windows и Linux добавлено плавное переключение пяти тем: цвета палитры, непрозрачность фона и скругление постепенно переходят к новым значениям.
- При быстром выборе другой темы переход продолжается от текущих видимых цветов к последнему выбору. В покое оформление остаётся неподвижным.
- Сохранены отдельный фон каждой темы, размер значков, увеличение, ряды, положение, компактные подписи, автозапуск и автоматическое добавление ярлыков. Windows по-прежнему сохраняет выбор темы сразу; в Linux изменения применяются по **OK**, а **«Отмена»** не меняет сохранённые настройки.

## 1.8.1 — 2026-10-25

### English

- Updated Dusk's settings and menu controls to use the common style of the other themes. Windows has thin accent sliders with round thumbs and rounded checkboxes; Linux uses the same slider, button and menu styling as the other four themes.
- Preserved Dusk's dock colors, motion, default opacity and saved backgrounds. All five themes, shortcut profiles, startup and automatic desktop-shortcut settings remain unchanged.

### Русский

- Элементы настроек и меню Dusk приведены к общему стилю остальных тем. В Windows используются тонкие акцентные ползунки с круглыми бегунками и скруглённые галочки; в Linux — то же оформление ползунков, кнопок и меню, что у остальных четырёх тем.
- Цвета панели Dusk, анимации, непрозрачность по умолчанию и сохранённый фон оставлены прежними. Все пять тем, профили ярлыков, автозапуск и автоматическое добавление ярлыков сохраняются без изменений.

## 1.8.0 — 2026-10-25

### English

- Added five themes on Windows and Linux: **Dusk**, **Frost**, **Graphite**, **Ember** and **Aurora**, covering the dock, menus and settings. Dusk preserves the original appearance, defaults and existing user background settings.
- Each theme remembers its own background opacity and corner rounding. First-use presets for Frost, Graphite, Ember and Aurora are respectively **76% / 22**, **96% / 12**, **85% / 16** and **88% / 24**. Returning to Dusk restores its saved background.
- Themes tune hover, click and drag motion while retaining your chosen icon size, magnification, rows, placement and compact one-line labels. Aurora's edge highlight appears during interaction; idle panels remain calm.
- Windows applies theme changes immediately and saves them automatically. Linux applies them with **OK**; **Cancel** leaves the saved settings unchanged. Startup, automatic desktop-shortcut addition, profiles and all 14 package variants are preserved.

### Русский

- В Windows и Linux добавлены пять тем для панели, меню и настроек: **Dusk («Сумерки», базовая)**, **Frost («Матовое стекло»)**, **Graphite («Матовый графит»)**, **Ember («Тёмная бронза»)** и **Aurora («Ночной индиго»)**. Dusk сохраняет прежний вид, значения по умолчанию и уже выбранный пользователем фон.
- У каждой темы запоминаются собственные непрозрачность и скругление фона. При первом выборе Frost, Graphite, Ember и Aurora используются соответственно **76% / 22**, **96% / 12**, **85% / 16** и **88% / 24**. Возврат к Dusk восстанавливает её сохранённый фон.
- Темы задают характер анимаций наведения, нажатия и перетаскивания; выбранные размер значков, увеличение, ряды, положение и компактные однострочные подписи сохраняются. Подсветка края Aurora появляется при взаимодействии; в покое панель остаётся спокойной.
- В Windows тема применяется сразу с автоматическим сохранением. В Linux изменения применяются по **OK**; **«Отмена»** оставляет сохранённые настройки без изменений. Автозапуск, автоматическое добавление новых ярлыков, профили и все 14 вариантов пакетов сохранены.

## 1.7.3 — 2026-10-24

### English

- Restored the compact one-line icon captions and the original **Show icon labels** / **Show labels** checkbox on Windows and Linux. Removed the two-mode name selector and wider two-line caption layout introduced in 1.7.2.
- Restored the original name-hover behavior and compact spacing. Existing label preferences and shortcut profiles are preserved.
- Linux startup at sign-in and optional automatic addition of new desktop shortcuts from 1.7.2 remain available; their behavior is unchanged.

### Русский

- В Windows и Linux возвращены компактные однострочные подписи и прежняя галочка **«Подписи под значками»** / **«Показывать подписи»**. Убраны переключатель двух режимов названий и расширенная двухстрочная раскладка из 1.7.2.
- Возвращены прежнее поведение подсказок и компактные отступы. Выбор показа подписей и профили ярлыков сохраняются.
- Автозапуск Linux и необязательное автоматическое добавление новых ярлыков из 1.7.2 сохранены без изменения поведения.

## 1.7.2 — 2026-10-24

### English

- Added **Settings → Shortcut names** with **On hover** and **Always (two lines)**. Both modes keep the full-name hover bubble; two-line captions reserve wider cells and wrap into extra rows when needed. Existing label preferences are retained.
- Added an opt-in **Automatically add new desktop shortcuts** checkbox, off by default. Only new Windows `.lnk` / `.url` or Linux `.desktop` shortcuts appearing while Yarnelo is running are considered; enabling it does not import existing icons. Duplicates and the 30-icon limit are respected. Windows follows the desktop-transfer setting without automatic shared-desktop administrator prompts; Linux leaves originals untouched.
- Added **Start when signing in** to Linux settings, off by default. Changes apply with **OK**; **Cancel** leaves startup registration untouched.
- Linux uses the current user's XDG autostart folder. The launcher keeps the installed executable, source interpreter/script or permanent AppImage path, including paths with spaces.
- Turning startup off removes Yarnelo's own registration and, where necessary, disables its system entry for the current user. Other applications' entries and system configuration are preserved; administrator access is not required.
- Added startup registration and settings checks. Windows startup behavior from 1.7.1, existing profiles and all 14 package formats/architectures are retained. Linux sign-in timing depends on the desktop environment.

### Русский

- Добавлен выбор **Настройки → Названия ярлыков**: **«При наведении»** и **«Всегда (две строки)»**. Полное название во всплывающей подсказке доступно в обоих режимах; для двухстрочных подписей выделяется больше места, при необходимости добавляются ряды. Прежний выбор показа подписей сохраняется.
- Добавлена галочка **«Автоматически добавлять новые ярлыки с рабочего стола»**, по умолчанию выключенная. Учитываются только новые `.lnk` / `.url` Windows или `.desktop` Linux, появившиеся во время работы Yarnelo; включение не импортирует прежние значки. Дубликаты и лимит 30 значков учитываются. Windows следует настройке переноса без автоматического запроса прав для общего рабочего стола; Linux оставляет оригиналы на месте.
- В настройки Linux добавлена галочка **«Запускать при входе в систему»**, по умолчанию выключенная. Изменения применяются по **OK**; **«Отмена»** не меняет регистрацию автозапуска.
- Linux использует папку автозапуска XDG текущего пользователя. Запись сохраняет путь установленной программы, интерпретатор и скрипт исходной версии либо постоянный путь AppImage, в том числе с пробелами.
- Выключение убирает собственную запись Yarnelo и при необходимости отключает её системную запись для текущего пользователя. Записи других приложений и системные настройки сохраняются; права администратора не нужны.
- Добавлены проверки регистрации и настроек. Автозапуск Windows из 1.7.1, прежние профили и все 14 вариантов пакетов и архитектур сохранены. Момент запуска Linux зависит от окружения рабочего стола.

## 1.7.1 — 2026-10-24

### English

- Windows startup now uses a current-user Task Scheduler logon task with a 10-second delay, the exact executable path and its working directory.
- The task runs with the user's ordinary permissions, works on battery power, and has no execution time limit. The startup switch verifies the saved registration and restores its previous state if a change fails.
- Enabled legacy startup entries belonging to this installation migrate after the new task has been verified. Disabled startup choices and other installations are preserved; uninstall removes only its own registration.
- Added startup registration and launch checks. Existing shortcut profiles, dock appearance settings and Linux behavior are unchanged.

### Русский

- Автозапуск Windows переведён на задачу входа текущего пользователя в планировщике с задержкой 10 секунд, точным путём к программе и её рабочей папкой.
- Задача использует обычные права пользователя, работает от батареи и не ограничена по времени выполнения. Переключатель проверяет сохранённую регистрацию и возвращает прежнее состояние при ошибке изменения.
- Включённые прежние записи автозапуска этой установки переносятся после проверки новой задачи. Отключённый автозапуск и другие установки сохраняются; удаление убирает только собственную регистрацию.
- Добавлены проверки регистрации и запуска. Профили ярлыков, настройки внешнего вида панели и поведение Linux не изменены.

## 1.7.0 — 2026-10-23

### English

- Background corner rounding from square corners to a capsule, saved with your other preferences.
- Wider two-column settings without vertical scrolling, fitted to the available screen space.
- Top, bottom, left and right centered placement; vertical side docks keep icons upright. Dragging the handle restores free placement.
- Automatic extra rows or columns when shortcuts would overflow the screen, preserving the requested icon size whenever possible.
- The new controls and guidance are available in English and Russian on Windows and Linux; existing shortcut profiles remain compatible.
- Linux packages bundle Python 3.12.15 to match the official Qt wheel runtime, with checks for normal event-loop shutdown and widget cleanup. Source launches require Python 3.12+.

### Русский

- Настройка скругления фона от прямых углов до капсулы с сохранением выбора.
- Более широкое окно настроек в два столбца без вертикальной прокрутки, подстраивающееся под доступную область экрана.
- Размещение по центру сверху, снизу, слева и справа; вертикальные боковые панели сохраняют ориентацию значков. Перетаскивание ручки возвращает свободное размещение.
- Дополнительные ряды или столбцы при нехватке места на экране с сохранением выбранного размера значков, пока это возможно.
- Новые пункты и подсказки на русском и английском для Windows и Linux; прежние профили ярлыков совместимы.
- Linux-пакеты включают Python 3.12.15 для совместимости с официальными сборками Qt; проверяются обычное завершение цикла событий и очистка виджетов. Запуск из исходников требует Python 3.12+.

## 1.6.0 — 2026-10-23

### English

- Native Windows ARM64 EXE installer and portable ZIP for Windows 11 with .NET Framework 4.8.1; existing Windows 10/11 x64 and Windows 10 x86 packages remain available.
- Native Linux ARM64 AppImage, DEB, RPM and TAR.GZ, built on Ubuntu 24.04 ARM64 with a glibc 2.39 minimum. The x86_64 glibc 2.35 baseline is unchanged.
- English and Russian download matrices and instructions for checking architecture, installing, updating and verifying all 14 release packages.
- Architecture-aware packaging and native ARM64 release checks. Linux remains a preview with the existing X11/XWayland limitations.

### Русский

- Нативные EXE-установщик и переносной ZIP Windows ARM64 для Windows 11 с .NET Framework 4.8.1; сборки Windows 10/11 x64 и Windows 10 x86 сохранены.
- Нативные AppImage, DEB, RPM и TAR.GZ Linux ARM64, собранные на Ubuntu 24.04 ARM64 с минимальным glibc 2.39. Прежняя базовая версия glibc 2.35 для x86_64 не изменилась.
- Таблицы загрузок и инструкции на русском и английском: проверка архитектуры, установка, обновление и проверка всех 14 пакетов релиза.
- Упаковка с учётом архитектуры и нативные проверки ARM64 при выпуске. Linux сохраняет статус preview и прежние ограничения X11/XWayland.

## 1.5.1

- Windows Setup and Uninstall recognize both English and Russian dock windows when requesting a graceful exit.
- Установщик и удаление Windows корректно закрывают панель с английским и русским интерфейсом.

## 1.5.0 — 2026-10-23

### English

- English and Russian interface on Windows and Linux, with a saved language choice in the dock menu and immediate switching.
- The first-launch language follows the system locale: Russian for a Russian locale, English otherwise; existing profiles remain compatible.
- English default README and complete Russian counterpart, with single-row, multi-row and language-menu screenshots.
- Documented Windows and Linux technology stacks, English/Russian installation guides and bilingual release notes.

### Русский

- Русский и английский интерфейс Windows и Linux; выбор в меню панели сохраняется и применяется без перезапуска.
- При первом запуске русская локаль системы выбирает русский язык, остальные — английский; совместимость прежних профилей сохраняется.
- Основной README на английском и полная русская версия со скриншотами одного ряда, нескольких рядов и меню языка.
- Описание стека Windows и Linux, инструкции установки на двух языках и двуязычные заметки к выпуску.

## 1.4.0 — 2026-10-23

- Установщики `.exe` для Windows x64 и x86, ярлык в меню «Пуск», необязательный ярлык рабочего стола и удаление через настройки Windows.
- Переносные архивы для Windows 10/11 соответствующей разрядности; проверка архитектуры готового EXE.
- Первый нативный Linux-порт на Qt: 1–4 ряда, до 30 ярлыков, настройка прозрачности и размера, анимации наведения, нажатия и перестановки, `.desktop`, файлы, папки, Компьютер и Корзина.
- Linux x86_64: AppImage, DEB, RPM и автономный TAR.GZ. Рекомендуется X11, XWayland имеет ограничения композитора.
- Инструкции выбора пакета, установки, обновления, удаления и проверки SHA256.
- Автоматическая сборка обеих платформ и проверки установки DEB/RPM в отдельных контейнерах.

## 1.3.1 — 2026-10-23

Первый публичный снимок Yarnelo.

### Новая иконка

- Авторская монограмма Y из плавных сиреневой и бирюзовой лент на тёмной скруглённой подложке.
- Отдельная упрощённая отрисовка для маленьких размеров.
- ICO с размерами от 16 до 256 пикселей, PNG и SVG в составе проекта.

### Возможности публичной версии

- До 30 ярлыков, раскладка в 1–4 ряда, добавление файлов, папок, «Этот компьютер» и Корзины.
- Настройка прозрачности, размера, подписей и увеличения значков.
- Плавные анимации наведения, нажатия и перетаскивания между позициями и рядами.
- Перенос ярлыков `.lnk` и `.url` с рабочего стола с возможностью восстановления, обработка общих ярлыков через запрос прав Windows.
- Скрытие при перекрытии окнами и в полноэкранных приложениях, автоматическое возвращение без переключения фокуса, поддержка нескольких мониторов.
- Контекстные меню, значок в области уведомлений, необязательный автозапуск и локальное хранение данных.
