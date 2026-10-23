# Установка Yarnelo

[English](INSTALL.md) · **Русский**

Готовые файлы находятся в разделе **Assets** на [странице релизов](https://github.com/feogrim-cloud/Yarnelo/releases). Кнопки GitHub **Source code** скачивают исходники; для обычной установки выберите файл из таблицы.

## Какой файл скачать

| Ваша система | Рекомендуемый файл | Другой вариант |
| --- | --- | --- |
| Windows 10 / 11, Intel или AMD, 64 бита | `Yarnelo-Windows-x64-Setup.exe` | `Yarnelo-Windows-x64-Portable.zip` |
| Windows 10, 32 бита | `Yarnelo-Windows-x86-Setup.exe` | `Yarnelo-Windows-x86-Portable.zip` |
| Windows 11, ARM64 (например Snapdragon) | `Yarnelo-Windows-arm64-Setup.exe` | `Yarnelo-Windows-arm64-Portable.zip` |
| Ubuntu 22.04+, Debian 12+, Linux Mint 21+, x86_64 | `Yarnelo-Linux-amd64.deb` | AppImage или TAR.GZ |
| Fedora, x86_64 | `Yarnelo-Linux-x86_64.rpm` | AppImage или TAR.GZ |
| openSUSE с glibc 2.35+, x86_64 | `Yarnelo-Linux-x86_64.rpm` | AppImage или TAR.GZ; отдельная проверка openSUSE пока не выполнена |
| Arch Linux и другие Linux с glibc 2.35+, x86_64 | `Yarnelo-Linux-x86_64.AppImage` | `Yarnelo-Linux-x86_64.tar.gz` |
| Ubuntu 24.04+, Debian 13+, ARM64 | `Yarnelo-Linux-arm64.deb` | `Yarnelo-Linux-aarch64.AppImage` или TAR.GZ |
| Fedora с glibc 2.39+, ARM64 | `Yarnelo-Linux-aarch64.rpm` | `Yarnelo-Linux-aarch64.AppImage` или TAR.GZ |
| Другие Linux с glibc 2.39+, ARM64 | `Yarnelo-Linux-aarch64.AppImage` | `Yarnelo-Linux-aarch64.tar.gz` |

В Windows откройте **Параметры → Система → О системе → Тип системы**. Для 64-битных Intel/AMD выбирайте **x64**, для ARM-процессоров вроде Snapdragon — **ARM64**, для 32-битной Windows 10 — **x86**. Нативная ARM64-сборка требует **Windows 11 и .NET Framework 4.8.1**; Windows 10 ARM64 этим пакетом не поддерживается. Для Windows x64/x86 нужен .NET Framework 4.8 или 4.8.1. Установщик проверяет нужный компонент.

В Linux проверьте архитектуру и системные библиотеки:

```bash
uname -m
getconf GNU_LIBC_VERSION
```

`amd64` / `x86_64` — 64-битная Intel/AMD, нужен **glibc 2.35+**. `arm64` / `aarch64` — 64-битная ARM, нужен **glibc 2.39+**. ARM-компьютер, включая Raspberry Pi с 64-битной графической ОС, должен соответствовать этим требованиям: одно название устройства или формат AppImage не гарантируют совместимость. Debian 12 и Ubuntu 22.04 подходят под базовые требования x86_64, но **не ARM64**. Python и Qt уже включены.

Windows 7 / 8.1, 32-битный Linux, Alpine/musl и Ubuntu 20.04 не входят в заявленную совместимость. Linux-версия пока имеет статус **preview**: рекомендуется X11; в Wayland-сеансе используется XWayland. Особенности наложения окон описаны в [руководстве Linux](LINUX.ru.md#совместимость-и-окружение).

## Windows

1. Запустите `Setup.exe` нужной разрядности и пройдите мастер. Права администратора не нужны.
2. Откройте Yarnelo из меню «Пуск».
3. Перетащите ярлыки на панель. Настройки открываются правой кнопкой по панели.
4. Для выбора языка откройте **Язык / Language → Русский / English** в меню панели. Выбор сохраняется и применяется без перезапуска.

Для переносной версии распакуйте **весь** ZIP в постоянную доступную для записи папку и запустите `Yarnelo.exe`.

Обновление, автозапуск, хранение ярлыков и удаление: [подробная инструкция Windows](WINDOWS.ru.md).

## Linux

В терминале перейдите в папку со скачанным файлом. Выполните подходящий вариант.

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

Для **Linux ARM64** с glibc 2.39+ используйте соответствующие команды:

```bash
# Ubuntu 24.04+ / Debian 13+ ARM64:
sudo apt install ./Yarnelo-Linux-arm64.deb
# Fedora ARM64:
sudo dnf install ./Yarnelo-Linux-aarch64.rpm
# Или переносной AppImage:
chmod +x ./Yarnelo-Linux-aarch64.AppImage
./Yarnelo-Linux-aarch64.AppImage
```

Выберите один способ установки; не устанавливайте пакеты Intel/AMD и ARM64 одновременно.

openSUSE:

```bash
sudo zypper install ./Yarnelo-Linux-x86_64.rpm
yarnelo
```

AppImage без системной установки:

```bash
chmod +x ./Yarnelo-Linux-x86_64.AppImage
./Yarnelo-Linux-x86_64.AppImage
```

Если AppImage сообщает об отсутствии FUSE:

```bash
APPIMAGE_EXTRACT_AND_RUN=1 ./Yarnelo-Linux-x86_64.AppImage
```

Для ARM64 в этой же команде подставьте `Yarnelo-Linux-aarch64.AppImage`.

Дистрибутивные зависимости, TAR.GZ, значок в меню приложений, обновление и удаление: [подробная инструкция Linux](LINUX.ru.md).

В Linux язык также выбирается через **Язык / Language → Русский / English**. При первом запуске обе версии выбирают русский для русской локали системы и английский для остальных.

## Проверка скачанных файлов

Скачайте `SHA256SUMS` из того же релиза в папку с пакетами. На Linux:

```bash
sha256sum --check SHA256SUMS --ignore-missing
```

Команда проверит скачанные файлы, перечисленные в списке; для них должно появиться `OK`. Проверяйте именно файлы одного релиза.

На Windows в PowerShell:

```powershell
Get-FileHash .\Yarnelo-Windows-x64-Setup.exe -Algorithm SHA256
```

Сравните результат со строкой нужного файла в `SHA256SUMS` или с соседним файлом `.sha256`. Для x86, arm64 либо ZIP подставьте его имя. Windows-файлы пока не подписаны сертификатом разработчика.

## Лицензия

Все права на собственный код и графику Yarnelo сохранены за автором. Сторонние библиотеки распространяются по своим лицензиям: см. [LICENSE](../LICENSE), `THIRD-PARTY-NOTICES.txt`, каталог `licenses` и `corresponding-sources.txt` внутри Linux-сборки.
