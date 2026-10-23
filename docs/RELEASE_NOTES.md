Yarnelo 1.4.0 добавляет установщики Windows и первую нативную версию для Linux.

## Windows

- **Windows 10/11 x64:** `Yarnelo-Windows-x64-Setup.exe`.
- **Windows 10 32 бита:** `Yarnelo-Windows-x86-Setup.exe`.
- Для запуска без установки выберите соответствующий `Portable.zip`, распакуйте все файлы и откройте `Yarnelo.exe`.

Установщик работает без прав администратора, создаёт ярлык в меню «Пуск» и сохраняет папку Data при обновлении и удалении. Если вы уже используете переносную версию, обновляйте её в прежней папке: новый установщик не переносит старые ярлыки автоматически. Требуется .NET Framework 4.8/4.8.1. EXE пока не подписаны сертификатом разработчика.

## Linux x86_64 — предварительная версия

- **Ubuntu 22.04+, Debian 12+, Mint 21+:** `Yarnelo-Linux-amd64.deb`.
- **Fedora:** `Yarnelo-Linux-x86_64.rpm`.
- **Другие дистрибутивы с glibc 2.35+:** AppImage или TAR.GZ.

Python и Qt включены; установка среды разработки не нужна. Пример для AppImage:

```bash
chmod +x Yarnelo-Linux-x86_64.AppImage
./Yarnelo-Linux-x86_64.AppImage
```

Порт поддерживает до 30 ярлыков, 1–4 ряда, прозрачность, плавное наведение и перестановку, ссылки `.desktop`, файлы, папки, Компьютер и Корзину. Исходные `.desktop` не перемещаются. Рекомендуется X11; в Wayland используется XWayland, размещение панели и поведение с полноэкранными окнами зависят от композитора. ARM64 и Alpine/musl не входят в этот выпуск.

**[Пошаговая установка и обновление](https://github.com/feogrim-cloud/Yarnelo/blob/main/docs/INSTALL.md)** · [Windows](https://github.com/feogrim-cloud/Yarnelo/blob/main/docs/WINDOWS.ru.md) · [Linux](https://github.com/feogrim-cloud/Yarnelo/blob/main/docs/LINUX.md)

Контрольные суммы всех восьми пакетов — в `SHA256SUMS`. Сборка и проверки выполняются на Windows и Linux; это не означает ручное тестирование на каждой версии Windows или в каждом оконном окружении Linux.
