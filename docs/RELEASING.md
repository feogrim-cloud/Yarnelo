# Publishing Yarnelo / Публикация Yarnelo

## English

Update `VERSION`, Windows assembly versions, the Linux version and the English/Russian release notes together. Run the regression checks against isolated profiles; never run installer integration tests on a personal computer.

The Windows and Linux workflow publishes only after every platform job succeeds. It accepts a `v*` tag, a manual `release_tag` input, or an explicit main-branch commit whose subject starts with **`Release Yarnelo v`**, for example `Release Yarnelo v1.9.2`. The last option publishes the version in `VERSION` and creates its tag at the checked commit, without requiring browser access. Ordinary commits and pull requests do not publish.

The release contains all 14 Windows/Linux packages plus `SHA256SUMS`. Check the uploaded assets and hashes before installing an update. Back up the actual Windows user profile and startup definition in the ordinary user context, update the existing installation, then verify every managed shortcut ID/path/file and the new process's `pid=N ready` record. Keep Program Files binaries separate from the native AppData profile and retain existing portable profiles.

## Русский

Обновляйте `VERSION`, версии сборки Windows/Linux и примечания к выпуску на обоих языках вместе. Проверяйте регрессии на изолированных профилях; интеграционные тесты установщика нельзя запускать на личном компьютере.

Публикация выполняется только после успешного завершения всех платформенных заданий. Доступны тег `v*`, ручной параметр `release_tag` и явный коммит в main с началом заголовка **`Release Yarnelo v`**, например `Release Yarnelo v1.9.2`. Последний вариант публикует версию из `VERSION` и создаёт тег на проверенном коммите без доступа к браузеру. Обычные коммиты и pull request ничего не публикуют.

Релиз содержит все 14 пакетов Windows/Linux и `SHA256SUMS`. Перед установкой сверяйте файлы и контрольные суммы. Сохраните настоящий пользовательский профиль Windows и определение автозапуска в обычном контексте пользователя, обновите существующую установку и проверьте каждый ID, путь и файл управляемого ярлыка, а также запись `pid=N ready` нового процесса. Бинарные файлы Program Files должны оставаться отдельно от профиля AppData; прежние переносные профили сохраняются.
