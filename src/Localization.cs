using System;
using System.Collections.Generic;
using System.Globalization;

namespace Polka {
    // Stable identifiers keep wording independent from persisted preferences.
    internal static class L10n {
        static readonly Dictionary<string,string[]> strings=new Dictionary<string,string[]>(StringComparer.Ordinal);
        static string language=Normalize(null);
        internal static string Language { get { return language; } }
        internal static IEnumerable<string> Keys { get { return strings.Keys; } }
        internal static string Normalize(string value) {
            if(String.Equals(value,"en",StringComparison.OrdinalIgnoreCase)) return "en";
            if(String.Equals(value,"ru",StringComparison.OrdinalIgnoreCase)) return "ru";
            return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName=="ru" ? "ru" : "en";
        }
        internal static void SetLanguage(string value) { language=Normalize(value); }
        internal static string T(string key,params object[] arguments) {
            string[] pair;
            if(!strings.TryGetValue(key,out pair)) throw new ArgumentException("Unknown translation key: "+key,"key");
            string value=pair[language=="ru"?0:1];
            return arguments.Length==0 ? value : String.Format(CultureInfo.GetCultureInfo(language),value,arguments);
        }
        internal static string DisplayName(DockItem item) {
            if(item==null) return String.Empty;
            if(!item.UserNamed) {
                bool computer=String.Equals(item.Path,"shell:MyComputerFolder",StringComparison.OrdinalIgnoreCase);
                bool trash=String.Equals(item.Path,"shell:RecycleBinFolder",StringComparison.OrdinalIgnoreCase);
                // Old profiles have no UserNamed flag. Preserve their custom titles.
                if(computer && (String.IsNullOrEmpty(item.Name) || item.Name=="Этот компьютер" || item.Name=="This PC")) return T("Computer");
                if(trash && (String.IsNullOrEmpty(item.Name) || item.Name=="Корзина" || item.Name=="Recycle Bin")) return T("Trash");
            }
            return item.Name ?? T("Shortcut");
        }
        static void Add(string key,string ru,string en) { strings.Add(key,new[]{ru,en}); }
        static L10n() {
            Add("DockTitle","Yarnelo — панель ярлыков","Yarnelo — shortcut dock");
            Add("Welcome","Перетащите ярлыки сюда · до 30 значков · настройки по кнопке +","Drop shortcuts here · up to 30 icons · settings under +");
            Add("MoveDock","Переместить панель · правая кнопка: настройки","Move the dock · right-click for settings");
            Add("DropShortcuts","Перетащите ярлыки","Drop shortcuts here");
            Add("AddShortcuts","Добавить ярлыки","Add shortcuts");
            Add("OpenFailed","Не удалось открыть: ","Could not open: ");
            Add("AddAndSettings","Добавить ярлыки и открыть настройки","Add shortcuts and open settings");
            Add("AlreadyAdded","Этот ярлык уже в панели","This shortcut is already in the dock");
            Add("AddedCount","Добавлено: {0} · ярлыков {1} / 30","Added: {0} · shortcuts {1} / 30");
            Add("AddFiles","Добавить ярлыки или файлы…","Add shortcuts or files…");
            Add("AddFolder","Добавить папку…","Add folder…");
            Add("Computer","Этот компьютер","This PC");
            Add("Trash","Корзина","Recycle Bin");
            Add("MoveSharedCount","Перенести общие ярлыки в панель ({0})…","Move shared shortcuts into dock ({0})…");
            Add("SettingsMenu","⚙  Настройки","⚙  Settings");
            Add("CenterMenu","Вниз по центру","Center at bottom");
            Add("Exit","Выход","Exit");
            Add("Open","Открыть","Open");
            Add("Reveal","Показать в папке","Show in folder");
            Add("Rename","Переименовать…","Rename…");
            Add("RemoveRestore","Убрать и вернуть ярлык на рабочий стол","Remove and restore shortcut to desktop");
            Add("Remove","Убрать из панели","Remove from dock");
            Add("Removed","Ярлык убран из панели","Shortcut removed from dock");
            Add("SharedMoved","Общие ярлыки перенесены с рабочего стола в панель","Shared shortcuts moved from desktop to dock");
            Add("SaveDockFailed","Не удалось сохранить панель: ","Could not save the dock: ");
            Add("Incomplete","Действие завершено не полностью. Сохранённые копии остались в панели.","The action was not completed. Saved copies remain in the dock.");
            Add("Cancelled","Действие отменено. Сохранённые копии остались в панели.","Action cancelled. Saved copies remain in the dock.");
            Add("ShortcutName","Название ярлыка","Shortcut name");
            Add("Save","Сохранить","Save");
            Add("AddToYarnelo","Добавить в Yarnelo","Add to Yarnelo");
            Add("FileFilter","Ярлыки и программы|*.lnk;*.url;*.exe|Все файлы|*.*","Shortcuts and applications|*.lnk;*.url;*.exe|All files|*.*");
            Add("FolderPrompt","Выберите папку для панели. Сама папка останется на месте.","Choose a folder for the dock. The folder will stay in its current location.");
            Add("SaveSettingsFailed","Не удалось сохранить настройки: ","Could not save settings: ");
            Add("ShowDock","Показать панель","Show dock");
            Add("Settings","Настройки","Settings");
            Add("ActionFinishFailed","Не удалось завершить действие.\n","Could not complete the action.\n");
            Add("ActionFailed","Не удалось выполнить действие.\n","Could not perform the action.\n");
            Add("StartFailed","Yarnelo не смогла запуститься.\n","Yarnelo could not start.\n");
            Add("SettingsTitle","Настройки — Yarnelo","Settings — Yarnelo");
            Add("CloseSettings","Закрыть настройки","Close settings");
            Add("Tagline","Ваши ярлыки. В одном месте.","Your shortcuts. In one place.");
            Add("Opacity","Непрозрачность фона","Background opacity");
            Add("IconSize","Размер значков","Icon size");
            Add("HoverZoom","Увеличение при наведении","Hover magnification");
            Add("ShowLabels","Подписи под значками","Show icon labels");
            Add("AlwaysOnTop","Показывать поверх обычных окон","Show above regular windows");
            Add("VisibilityHelp","Если выключено, панель скрывается, когда другое окно перекрывает её место, и возвращается сама, когда место освобождается. Окно над панелью или на другом мониторе её не скрывает. Полноэкранные приложения на мониторе панели всегда скрывают её.","When disabled, the dock hides while another window covers its position and returns when the space is clear. A window above the dock or on another monitor does not hide it. Fullscreen applications on the dock's monitor always hide it.");
            Add("Startup","Запускать при входе в Windows","Start when signing in to Windows");
            Add("MoveDesktop","Переносить ярлыки с рабочего стола в панель","Move desktop shortcuts into the dock");
            Add("MoveHelp","Ярлыки .lnk и .url возвращаются на рабочий стол при удалении из панели. Для общих ярлыков нажмите + → Перенести общие ярлыки и подтвердите запрос Windows. Программы, файлы и папки остаются на месте.","Removing a .lnk or .url shortcut restores it to the desktop. For shared shortcuts, choose + → Move shared shortcuts and confirm the Windows prompt. Applications, files and folders stay in place.");
            Add("CenterBottom","Расположить внизу по центру","Place at bottom center");
            Add("UsageHelp","Перетащите ⠿ для перемещения панели.\nМеняйте порядок значков перетаскиванием.\nПравая кнопка на значке — дополнительные действия.","Drag ⠿ to move the dock.\nDrag icons to change their order.\nRight-click an icon for more actions.");
            Add("AutoSaveHelp","До 30 ярлыков · изменения сохраняются автоматически","Up to 30 shortcuts · changes save automatically");
            Add("Rows","Количество рядов","Number of rows");
            Add("OneRow","1 ряд","1 row");
            Add("RowCount","{0} ряда","{0} rows");
            Add("RowsAutomation","Количество рядов: ","Number of rows: ");
            Add("Language","Язык","Language");
            Add("StartupOpenFailed","Не удалось открыть параметры автозапуска.","Could not open startup settings.");
            Add("ShortcutMissing","Этот ярлык больше не найден.","This shortcut could not be found.");
            Add("RecoveredSaveFailed","Настройки восстановлены в памяти, но пока не удалось сохранить их: ","Settings were recovered in memory but could not be saved yet: ");
            Add("DockFull","На панели уже 30 значков. Удалите один из них, чтобы добавить новый.","The dock already contains 30 icons. Remove one to add a new shortcut.");
            Add("TargetMissing","Не удалось найти файл или папку для этого ярлыка.","The file or folder for this shortcut could not be found.");
            Add("SharedCopyNotice","Это общий ярлык для всех пользователей. Его копия добавлена на панель; чтобы убрать оригинал с общего рабочего стола, выполните отдельное действие с правами администратора.","This shortcut is shared by all users. A copy was added to the dock; removing the original from the shared desktop requires a separate action with administrator rights.");
            Add("ImportCopyFailed","Ярлык добавлен, но остался на рабочем столе: не удалось сохранить его копию. ","The shortcut was added but remains on the desktop because its copy could not be saved. ");
            Add("ImportChanged","Ярлык изменился во время добавления; его новый вариант сохранён на рабочем столе.","The shortcut changed during import; its new version has been kept on the desktop.");
            Add("SourceStillExists","Исходный ярлык всё ещё существует.","The original shortcut still exists.");
            Add("ImportRemoveFailed","Ярлык добавлен на панель как копия; убрать оригинал с рабочего стола не удалось. ","A copy was added to the dock; the original could not be removed from the desktop. ");
            Add("FinalSavePending","Изменение добавлено, но его последний этап пока не удалось сохранить. Оно будет проверено при следующем запуске. ","The change was applied but its final step could not be saved. It will be checked on the next launch. ");
            Add("SharedIdFailed","Не удалось проверить идентификатор общего ярлыка: ","Could not verify the shared shortcut identifier: ");
            Add("OriginalUnchangedSuffix",". Оригинал не изменён.",". The original was not changed.");
            Add("CopiesDiffer","Существующая копия отличается от исходного ярлыка.","The existing copy differs from the original shortcut.");
            Add("SharedCopyPreparePrefix","Не удалось подготовить копию общего ярлыка «","Could not prepare a copy of shared shortcut “");
            Add("SharedCopyPrepareSuffix","». Оригинал не изменён. ","”. The original was not changed. ");
            Add("SharedPrepared","Общие ярлыки подготовлены на панели. Для удаления оригиналов с общего рабочего стола выполните отдельное действие с правами администратора.","Shared shortcuts are ready in the dock. Removing originals from the shared desktop requires a separate action with administrator rights.");
            Add("SharedPathsFailed","Не удалось проверить пути общего ярлыка.","Could not verify the shared shortcut paths.");
            Add("SharedFoldersFailed","Не удалось проверить папки хранения общего ярлыка.","Could not verify the shared shortcut storage folders.");
            Add("SharedCopyVerifyFailed","Не удалось проверить сохранённую копию общего ярлыка; оригинал не изменён.","Could not verify the saved shared shortcut copy; the original was not changed.");
            Add("SharedCopySafeFailed","Не удалось безопасно проверить копию общего ярлыка; оригинал не изменён. ","Could not safely verify the shared shortcut copy; the original was not changed. ");
            Add("SharedSourceSafeFailed","Не удалось безопасно проверить исходный общий ярлык; оригинал не изменён. ","Could not safely verify the original shared shortcut; the original was not changed. ");
            Add("SharedChanged","Общий ярлык изменился после добавления на панель. Оригинал оставлен на рабочем столе; добавьте его заново, чтобы перенести новый вариант.","The shared shortcut changed after being added to the dock. Its original remains on the desktop; add it again to move the new version.");
            Add("RestoreRecordFailed","Не удалось сохранить сведения для возврата общего ярлыка. Оригинал не изменён. ","Could not save shared shortcut restoration details. The original was not changed. ");
            Add("SharedSourceStillExists","Исходный общий ярлык всё ещё существует.","The original shared shortcut still exists.");
            Add("SharedNeedsAdmin","Общий ярлык остался на рабочем столе. Для его переноса нужны права администратора. ","The shared shortcut remains on the desktop. Moving it requires administrator rights. ");
            Add("CheckNextLaunch","Последний этап будет проверен при следующем запуске.","The final step will be checked on the next launch.");
            Add("SharedMovedPending","Общий ярлык перенесён. Завершение сохранения будет проверено при следующем запуске. ","The shared shortcut was moved. The final save will be checked on the next launch. ");
            Add("SharedRestoreVerifyFailed","Не удалось проверить сохранённый общий ярлык для возврата.","Could not verify the saved shared shortcut for restoration.");
            Add("SharedRestoreAdmin","Для возврата общего ярлыка нужны права администратора. Сохранённая копия оставлена на панели. ","Restoring the shared shortcut requires administrator rights. Its saved copy remains in the dock. ");
            Add("SharedRestoredSavePending","Общий ярлык возвращён, но сохранить изменение пока не удалось; копия на панели сохранена.","The shared shortcut was restored but the change could not be saved; its copy has been kept in the dock.");
            Add("SharedRestoredPrefix","Общий ярлык возвращён на рабочий стол: ","Shared shortcut restored to desktop: ");
            Add("SharedRestoreRequired","Для возврата общего ярлыка нужны права администратора. Ярлык оставлен на панели до возврата на рабочий стол.","Restoring the shared shortcut requires administrator rights. It will remain in the dock until restored to the desktop.");
            Add("OriginalLocationFailed","Не удалось проверить исходное место ярлыка. Его копия сохранена в папке панели.","Could not verify the shortcut's original location. Its copy has been kept in the dock folder.");
            Add("RestoreFailed","Ярлык пока оставлен на панели: не удалось вернуть его на рабочий стол. ","The shortcut remains in the dock because it could not be restored to the desktop. ");
            Add("CopyMissingRestore","Копия ярлыка в папке панели не найдена; восстановить её на рабочий стол невозможно.","The shortcut copy is missing from the dock folder and cannot be restored to the desktop.");
            Add("RestoredRemovePending","Ярлык уже возвращён на рабочий стол, но удалить его из настроек панели пока не удалось.","The shortcut was restored to the desktop but could not yet be removed from the dock settings.");
            Add("DockChangeSaveFailed","Не удалось сохранить изменение панели. ","Could not save the dock change. ");
            Add("BackupCleanupPending","Значок удалён с панели, но его запасную копию пока не удалось очистить.","The icon was removed from the dock but its backup copy could not yet be cleaned up.");
            Add("RestoredPrefix","Ярлык возвращён на рабочий стол: ","Shortcut restored to desktop: ");
            Add("SettingsRecovered","Настройки восстановлены из резервной копии. Предыдущий файл сохранён для проверки.","Settings were restored from a backup. The previous file was kept for inspection.");
            Add("SettingsReset","Файл настроек не удалось прочитать. Созданы начальные настройки; прежние файлы сохранены для проверки.","The settings file could not be read. Default settings were created; the previous files were kept for inspection.");
            Add("SettingsTooLarge","Слишком большой файл настроек.","The settings file is too large.");
            Add("SettingsEmpty","Пустые настройки.","The settings are empty.");
            Add("SecureLoadFailed","Безопасная загрузка существующих настроек прервана; файл не изменён. ","Secure loading of existing settings stopped; the file was not changed. ");
            Add("PreserveSettingsFailed","Не удалось создать отдельную копию прежних настроек: ","Could not create a separate copy of the previous settings: ");
            Add("PendingSharedUnsafe","Незавершённый общий ярлык не изменён: его пути не прошли безопасную проверку. ","An unfinished shared shortcut was not changed because its paths failed safety checks. ");
            Add("ImportCheckedPrefix","Проверено незавершённое добавление ярлыка: ","Checked an unfinished shortcut import: ");
            Add("ShortcutLower","ярлык","shortcut");
            Add("Shortcut","Ярлык","Shortcut");
            Add("RestoredNamePrefix"," (восстановлено "," (restored ");
            Add("RestoreNameFailed","Не удалось выбрать свободное имя для восстановленного ярлыка.","Could not find an unused name for the restored shortcut.");
            Add("SharedRestoreNameFailed","Не удалось выбрать свободное имя для восстановленного общего ярлыка.","Could not find an unused name for the restored shared shortcut.");
            Add("PathMissing","Путь не указан.","No path was provided.");
            Add("VerifyPathMissing","Путь для безопасной проверки не указан.","No path was provided for safety checks.");
            Add("ReparseUnsupported","Ссылки и точки перенаправления не поддерживаются в безопасной операции: ","Links and reparse points are not supported in this safe operation: ");
            Add("SafeFileTooLarge","Файл слишком велик для безопасной операции.","The file is too large for this safe operation.");
            Add("ReadChanged","Файл изменился во время чтения.","The file changed while it was being read.");
            Add("SharedMoveChanged","Общий ярлык изменился во время переноса.","The shared shortcut changed while it was being moved.");
            Add("VerifiedDeleteFailed","Не удалось удалить проверенный общий ярлык.","Could not delete the verified shared shortcut.");
            Add("VerifyOpenFailed","Не удалось открыть проверяемый файл: ","Could not open the file for verification: ");
            Add("VerifyPropertiesFailed","Не удалось проверить свойства открытого файла.","Could not verify the opened file's properties.");
            Add("SafeFileUnsupported","Ссылки, каталоги и файлы с несколькими жёсткими ссылками не поддерживаются в безопасной операции.","Links, directories and files with multiple hard links are not supported in this safe operation.");
            Add("VerifyFinalPathFailed","Не удалось проверить конечный путь открытого файла.","Could not verify the opened file's final path.");
            Add("VerifyFullFinalPathFailed","Не удалось проверить полный конечный путь файла.","Could not verify the file's complete final path.");
            Add("FinalPathMismatch","Конечный путь открытого файла отличается от ожидаемого.","The opened file's final path differs from the expected path.");
        }
    }
}
