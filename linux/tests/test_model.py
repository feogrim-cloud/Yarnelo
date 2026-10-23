import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from yarnelo_model import (
    MAX_ITEMS, Item, ModelError, Settings, config_path, desktop_metadata,
    load_settings, move_item, preserve_invalid_settings, save_settings, slot_indices,
)
from yarnelo import external_environment


class ModelTests(unittest.TestCase):
    def test_balanced_rows_include_add_button_without_lost_slots(self):
        for count in range(MAX_ITEMS + 1):
            for rows in range(1, 5):
                slots = slot_indices(count, rows)
                self.assertEqual(count + 1, len(slots))
                self.assertEqual(len(slots), len(set(slots)))
                used_rows = min(rows, count + 1)
                counts = [sum(r == row for r, _ in slots) for row in range(used_rows)]
                self.assertLessEqual(max(counts) - min(counts), 1)
                self.assertEqual(list(range(used_rows)), sorted(set(row for row, _ in slots)))

    def test_duplicate_and_limit_do_not_modify_items(self):
        settings = Settings()
        self.assertTrue(settings.add(Item.create("trash")))
        self.assertFalse(settings.add(Item.create("trash")))
        for index in range(1, MAX_ITEMS):
            settings.add(Item(str(index), "path", f"/tmp/example-{index}"))
        with self.assertRaises(ModelError):
            settings.add(Item.create("computer"))
        self.assertEqual(MAX_ITEMS, len(settings.items))

    def test_internal_reorder_keeps_item_identity(self):
        items = [Item(str(i), "path", f"/tmp/{i}") for i in range(5)]
        move_item(items, "1", 4)
        self.assertEqual(["0", "2", "3", "4", "1"], [i.id for i in items])
        move_item(items, "1", 0)
        self.assertEqual(["1", "0", "2", "3", "4"], [i.id for i in items])
        with self.assertRaises(ModelError):
            move_item(items, "missing", 0)

    def test_settings_atomic_roundtrip_preserves_unicode_and_missing_paths(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "nested" / "settings.json"
            settings = Settings(rows=4, opacity=0.32, labels=True, items=[Item("тест", "path", str(Path(directory) / "удалённый файл")), Item.create("trash")])
            save_settings(path, settings)
            self.assertEqual(settings.to_dict(), load_settings(path).to_dict())
            settings.rows = 2
            save_settings(path, settings)
            self.assertEqual(2, load_settings(path).rows)
            self.assertEqual([path], list(path.parent.iterdir()))

    def test_invalid_settings_do_not_replace_existing_file(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "settings.json"
            path.write_text("{broken", encoding="utf-8")
            with self.assertRaises(ModelError):
                load_settings(path)
            backup = preserve_invalid_settings(path)
            self.assertEqual(b"{broken", backup.read_bytes())
            bad = Settings(items=[Item("duplicate", "trash"), Item("duplicate", "computer")])
            with self.assertRaises(ModelError):
                save_settings(path, bad)
            self.assertEqual(b"{broken", path.read_bytes())

    def test_failed_atomic_replace_keeps_original_and_removes_temporary_file(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "settings.json"
            save_settings(path, Settings(rows=1))
            original = path.read_bytes()
            with patch("yarnelo_model.os.replace", side_effect=OSError("simulated disk error")):
                with self.assertRaises(OSError):
                    save_settings(path, Settings(rows=4))
            self.assertEqual(original, path.read_bytes())
            self.assertEqual([path], list(Path(directory).iterdir()))

    def test_validation_rejects_nonfinite_bad_types_and_relative_paths(self):
        for raw in ({"schema": 99}, {"opacity": float("nan")}, {"zoom": float("inf")}, {"rows": True}, {"labels": "yes"}, {"items": [{"id": "x", "kind": "path", "path": "relative"}]}):
            with self.subTest(raw=raw), self.assertRaises(ModelError):
                Settings.from_dict(raw)
        self.assertEqual(4, Settings.from_dict({"rows": 100}).rows)
        self.assertEqual(0.12, Settings.from_dict({"opacity": -1}).opacity)

    def test_desktop_metadata_is_localized_and_never_evaluates_exec(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "example.desktop"
            path.write_text("[Desktop Entry]\nType=Application\nName=Example\nName[ru]=Пример\\sимени\nExec=sh -c 'touch /tmp/never-executed-%U'\nIcon=applications-games\n", encoding="utf-8")
            self.assertEqual(("Пример имени", "applications-games"), desktop_metadata(path, "ru_RU.UTF-8"))
            self.assertEqual(("Example", "applications-games"), desktop_metadata(path, "en_US"))
            self.assertTrue(path.exists())

    def test_invalid_desktop_entry_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "bad.desktop"
            for value in ("[Desktop Entry]\nType=Link\nURL=https://example.com", "[Desktop Entry]\nType=Application\nName=No Exec", "not an ini"):
                path.write_text(value, encoding="utf-8")
                with self.subTest(value=value), self.assertRaises(ModelError):
                    desktop_metadata(path)

    def test_adding_path_only_references_original(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "launcher.desktop"
            contents = "[Desktop Entry]\nType=Application\nName=Safe\nExec=true\n"
            path.write_text(contents, encoding="utf-8")
            item = Item.create("path", str(path))
            self.assertEqual(str(path.absolute()), item.path)
            self.assertEqual(contents, path.read_text(encoding="utf-8"))
            self.assertEqual([path], list(Path(directory).iterdir()))

    def test_xdg_config_absolute_override(self):
        with tempfile.TemporaryDirectory() as directory:
            with patch.dict(os.environ, {"XDG_CONFIG_HOME": directory}):
                self.assertEqual(Path(directory) / "yarnelo" / "settings.json", config_path())
        with patch.dict(os.environ, {"XDG_CONFIG_HOME": "relative"}):
            self.assertEqual(Path.home() / ".config" / "yarnelo" / "settings.json", config_path())

    def test_frozen_launch_environment_restores_external_loader_and_keeps_user_paths(self):
        with tempfile.TemporaryDirectory() as directory:
            bundle = Path(directory) / "_internal"
            custom_plugins = str(Path(directory) / "custom-plugins")
            original = {"LD_LIBRARY_PATH": str(bundle), "LD_LIBRARY_PATH_ORIG": "/usr/local/lib", "QT_PLUGIN_PATH": os.pathsep.join((str(bundle / "PySide6" / "Qt" / "plugins"), custom_plugins)), "QT_QPA_PLATFORM_PLUGIN_PATH": str(bundle / "platforms"), "QT_QPA_PLATFORM": "xcb", "PATH": "/usr/bin"}
            cleaned = external_environment(original, bundle)
            self.assertEqual("/usr/local/lib", cleaned["LD_LIBRARY_PATH"])
            self.assertEqual(custom_plugins, cleaned["QT_PLUGIN_PATH"])
            self.assertNotIn("QT_QPA_PLATFORM_PLUGIN_PATH", cleaned)
            self.assertEqual("xcb", cleaned["QT_QPA_PLATFORM"])
            self.assertEqual("/usr/bin", cleaned["PATH"])
            self.assertEqual(str(bundle), original["LD_LIBRARY_PATH"])
            self.assertNotIn("LD_LIBRARY_PATH", external_environment({"LD_LIBRARY_PATH": str(bundle)}, bundle))


if __name__ == "__main__":
    unittest.main()
