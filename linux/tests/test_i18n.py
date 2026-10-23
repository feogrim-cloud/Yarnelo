import os
from pathlib import Path
import string
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from yarnelo_i18n import TEXT, error_text, resolve_language, system_language, text
from yarnelo_model import Item, ModelError, Settings, desktop_metadata, load_settings, save_settings


class LanguageTests(unittest.TestCase):
    def test_all_messages_have_both_languages_and_identical_placeholders(self):
        self.assertEqual(set(TEXT["en"]), set(TEXT["ru"]))
        formatter = string.Formatter()
        for key in TEXT["en"]:
            with self.subTest(key=key):
                names = lambda value: {field for _, field, _, _ in formatter.parse(value) if field}
                self.assertEqual(names(TEXT["en"][key]), names(TEXT["ru"][key]))
                self.assertTrue(TEXT["en"][key].strip())
                self.assertTrue(TEXT["ru"][key].strip())

    def test_auto_language_respects_message_locale_and_has_english_fallback(self):
        with patch.dict(os.environ, {"LANG": "ru_RU.UTF-8"}, clear=True):
            self.assertEqual("ru", resolve_language("auto"))
            self.assertEqual("en", resolve_language("en"))
        with patch.dict(os.environ, {"LANG": "ru_RU", "LC_MESSAGES": "en_GB.UTF-8"}, clear=True):
            self.assertEqual("en", system_language())
        with patch.dict(os.environ, {"LANG": "en_US", "LC_MESSAGES": "en_GB", "LC_ALL": "ru-RU"}, clear=True):
            self.assertEqual("ru", system_language())
        with patch.dict(os.environ, {"LANG": "de_DE.UTF-8"}, clear=True):
            self.assertEqual("en", system_language())
        with patch.dict(os.environ, {}, clear=True), patch("yarnelo_i18n.locale.getlocale", return_value=("ru_RU", "UTF-8")):
            self.assertEqual("ru", system_language())

    def test_legacy_settings_use_system_language_without_losing_shortcuts(self):
        legacy = {"schema": 1, "rows": 3, "items": [{"id": "trash", "kind": "trash"}]}
        settings = Settings.from_dict(legacy)
        self.assertEqual("auto", settings.language)
        self.assertEqual(3, settings.rows)
        self.assertEqual("trash", settings.items[0].id)
        for invalid in (None, True, "xx", [], {}):
            with self.subTest(invalid=invalid), self.assertRaises(ModelError):
                Settings.from_dict({**legacy, "language": invalid})

    def test_selected_language_survives_save_load_and_keeps_item_identity(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "settings.json"
            settings = Settings(items=[Item("computer", "computer"), Item("trash", "trash")])
            for language in ("ru", "en", "auto"):
                settings.language = language
                save_settings(path, settings)
                restored = load_settings(path)
                self.assertEqual(language, restored.language)
                self.assertEqual(settings.items, restored.items)
                self.assertEqual(1, restored.to_dict()["schema"])

    def test_same_model_error_can_be_shown_in_the_current_language(self):
        error = ModelError("path_missing", path="/demo/пример.desktop")
        self.assertEqual("File or folder not found: /demo/пример.desktop", error_text(error, "en"))
        self.assertEqual("Файл или папка не найдены: /demo/пример.desktop", error_text(error, "ru"))
        self.assertEqual("disk detail", error_text(OSError("disk detail"), "en"))

    def test_desktop_label_uses_chosen_app_language_instead_of_system_locale(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "demo.desktop"
            path.write_text("[Desktop Entry]\nType=Application\nName=Default\nName[en]=English app\nName[ru]=Русское приложение\nExec=true\n", encoding="utf-8")
            with patch.dict(os.environ, {"LANG": "ru_RU.UTF-8"}, clear=True):
                self.assertEqual("English app", desktop_metadata(path, resolve_language("en"))[0])
                self.assertEqual("Русское приложение", desktop_metadata(path, resolve_language("auto"))[0])
            self.assertEqual("English", text("language_en", "ru"))
            self.assertEqual("Русский", text("language_ru", "en"))


if __name__ == "__main__":
    unittest.main()
