# Changelog / Изменения

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
