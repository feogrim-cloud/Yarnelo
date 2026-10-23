# Yarnelo 1.5.1

## English

This patch also fixes graceful closing by Windows Setup and Uninstall after selecting the English interface.

Yarnelo now has an **English and Russian interface** on Windows and Linux. Right-click the dock → **Language / Язык → English / Русский**. The choice is saved and applies without restarting; the first launch follows the system locale (Russian for a Russian locale, English otherwise).

- English project overview with a complete Russian version and links between them.
- Screenshots showing a single row, multiple rows and the language menu, using example app/game shortcuts.
- Technology stack documented for both native implementations and the release tools.
- English and Russian installation guides, with the same update and profile-preservation instructions.

### Downloads

- **Windows 10/11 x64:** `Yarnelo-Windows-x64-Setup.exe` or `Yarnelo-Windows-x64-Portable.zip`.
- **Windows 10 32-bit:** `Yarnelo-Windows-x86-Setup.exe` or `Yarnelo-Windows-x86-Portable.zip`.
- **Linux x86_64:** DEB for Ubuntu 22.04+ / Debian 12+ / Mint 21+; RPM for Fedora; AppImage or TAR.GZ for systems with glibc 2.35+.

Windows requires .NET Framework 4.8/4.8.1. Installers run per user and retain `Data` on update/removal. Update an existing portable copy in its original folder; a new installer does not import a separate portable profile. Windows EXE files are currently unsigned.

**Linux remains a preview. X11 is recommended.** Ready-made packages use XWayland in Wayland sessions; placement and fullscreen behavior depend on the compositor. Original `.desktop` files are not moved. Python and Qt are included. ARM64, 32-bit Linux and Alpine/musl are outside this release's compatibility scope.

**[Installation and update](https://github.com/feogrim-cloud/Yarnelo/blob/main/docs/INSTALL.md)** · [Windows](https://github.com/feogrim-cloud/Yarnelo/blob/main/docs/WINDOWS.md) · [Linux](https://github.com/feogrim-cloud/Yarnelo/blob/main/docs/LINUX.md)

## Русский

Исправлено корректное закрытие панели установщиком и удалением Windows после выбора английского интерфейса.

Yarnelo получил **русский и английский интерфейс** в Windows и Linux. Правая кнопка по панели → **Язык / Language → Русский / English**. Выбор сохраняется и применяется без перезапуска; при первом запуске используется русский для русской локали системы, английский — для остальных.

- Описание проекта на английском и полная русская версия с переключением между ними.
- Скриншоты одного ряда, нескольких рядов и меню языка с примерами ярлыков приложений и игр.
- Описание стека обеих нативных версий и инструментов выпуска.
- Инструкции установки на русском и английском с сохранением рекомендаций по обновлению и профилю.

### Что скачать

- **Windows 10/11 x64:** `Yarnelo-Windows-x64-Setup.exe` или `Yarnelo-Windows-x64-Portable.zip`.
- **Windows 10, 32 бита:** `Yarnelo-Windows-x86-Setup.exe` или `Yarnelo-Windows-x86-Portable.zip`.
- **Linux x86_64:** DEB для Ubuntu 22.04+ / Debian 12+ / Mint 21+; RPM для Fedora; AppImage или TAR.GZ для систем с glibc 2.35+.

Для Windows нужен .NET Framework 4.8/4.8.1. Установщик работает без прав администратора и сохраняет `Data` при обновлении и удалении. Переносную версию обновляйте в прежней папке: новый установщик не импортирует отдельный профиль автоматически. EXE пока не подписаны сертификатом разработчика.

**Linux остаётся предварительной версией. Рекомендуется X11.** Готовые сборки используют XWayland в Wayland-сеансах; размещение и поведение с полноэкранными приложениями зависят от композитора. Исходные `.desktop` не перемещаются. Python и Qt включены. ARM64, 32-битный Linux и Alpine/musl не входят в совместимость этого выпуска.

**[Установка и обновление](https://github.com/feogrim-cloud/Yarnelo/blob/main/docs/INSTALL.ru.md)** · [Windows](https://github.com/feogrim-cloud/Yarnelo/blob/main/docs/WINDOWS.ru.md) · [Linux](https://github.com/feogrim-cloud/Yarnelo/blob/main/docs/LINUX.ru.md)

---

`SHA256SUMS` contains hashes for all eight packages / содержит контрольные суммы всех восьми пакетов. Automated checks do not imply manual testing on every OS or desktop environment / Автоматические проверки не означают ручного тестирования каждой ОС и окружения рабочего стола.

Screenshots show example shortcuts only; the pictured apps/games are not bundled with Yarnelo / На скриншотах показаны примеры ярлыков; изображённые приложения и игры не входят в комплект Yarnelo.
