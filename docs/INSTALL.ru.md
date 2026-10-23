# Установка Yarnelo

[English](INSTALL.md) · **Русский**

Готовые файлы находятся в разделе **Assets** на [странице релизов](https://github.com/feogrim-cloud/Yarnelo/releases). Кнопки GitHub **Source code** скачивают исходники; для обычной установки выберите файл из таблицы.

## Какой файл скачать

| Ваша система | Рекомендуемый файл | Другой вариант |
| --- | --- | --- |
| Windows 10 / 11, Intel или AMD, 64 бита | `Yarnelo-Windows-x64-Setup.exe` | `Yarnelo-Windows-x64-Portable.zip` |
| Windows 10, 32 бита | `Yarnelo-Windows-x86-Setup.exe` | `Yarnelo-Windows-x86-Portable.zip` |
| Ubuntu 22.04+, Debian 12+, Linux Mint 21+, x86_64 | `Yarnelo-Linux-amd64.deb` | AppImage или TAR.GZ |
| Fedora, x86_64 | `Yarnelo-Linux-x86_64.rpm` | AppImage или TAR.GZ |
| openSUSE с glibc 2.35+, x86_64 | `Yarnelo-Linux-x86_64.rpm` | AppImage или TAR.GZ; отдельная проверка openSUSE пока не выполнена |
| Arch Linux и другие Linux с glibc 2.35+, x86_64 | `Yarnelo-Linux-x86_64.AppImage` | `Yarnelo-Linux-x86_64.tar.gz` |

В Linux названия архитектур `amd64` и `x86_64` означают одну и ту же 64-битную архитектуру Intel/AMD. Бинарные сборки содержат Python и Qt: отдельно устанавливать их не нужно. Для Windows нужен .NET Framework 4.8 или 4.8.1; установщик проверит его наличие.

Windows 7 / 8.1, нативная Windows ARM64, Linux ARM64, 32-битный Linux, Alpine/musl и Ubuntu 20.04 не входят в заявленную совместимость. Linux-версия пока имеет статус **preview**: рекомендуется X11; в Wayland-сеансе используется XWayland. Особенности наложения окон описаны в [руководстве Linux](LINUX.ru.md#совместимость-и-окружение).

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

Сравните результат со строкой нужного файла в `SHA256SUMS` или с соседним файлом `.sha256`. Для x86 либо ZIP подставьте его имя. Windows-файлы пока не подписаны сертификатом разработчика.

## Лицензия

Все права на собственный код и графику Yarnelo сохранены за автором. Сторонние библиотеки распространяются по своим лицензиям: см. [LICENSE](../LICENSE), `THIRD-PARTY-NOTICES.txt`, каталог `licenses` и `corresponding-sources.txt` внутри Linux-сборки.
