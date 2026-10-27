import json
import os
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from yarnelo_model import (
    MAX_ITEMS, PLACEMENTS, Item, ModelError, Settings, config_path, desktop_metadata, dock_layout,
    load_settings, move_item, preserve_invalid_settings, save_settings, slot_indices,
)
from yarnelo import external_environment
from yarnelo_themes import THEMES, THEME_IDS, default_appearance, stylesheet
import yarnelo


class ModelTests(unittest.TestCase):
    def test_incompatible_python_is_rejected_before_loading_qt(self):
        args = SimpleNamespace(language="en", smoke_test=True, smoke_output=None)
        for language, phrase in (("en", "requires Python 3.12"), ("ru", "нужен Python 3.12")):
            args.language = language
            with patch("yarnelo.arguments", return_value=args), patch("yarnelo.sys.version_info", (3, 11, 9)):
                with self.assertRaises(SystemExit) as raised:
                    yarnelo.main()
            self.assertIn(phrase, str(raised.exception))

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

    def test_new_appearance_settings_preserve_legacy_shortcuts_and_saved_choice(self):
        legacy = {"rows": 3, "x": -1200, "y": 80, "items": [{"id": "old-trash", "kind": "trash"}]}
        settings = Settings.from_dict(legacy)
        self.assertEqual((23.0, "free", -1200, 80), (settings.corner_radius, settings.placement, settings.x, settings.y))
        for placement in PLACEMENTS:
            for radius in (0, 18.5, 100):
                settings.placement, settings.corner_radius = placement, radius
                restored = Settings.from_dict(settings.to_dict())
                self.assertEqual((placement, radius, 3, "old-trash"), (restored.placement, restored.corner_radius, restored.rows, restored.items[0].id))
        self.assertEqual(0, Settings.from_dict({"corner_radius": -1}).corner_radius)
        self.assertEqual(100, Settings.from_dict({"corner_radius": 101}).corner_radius)
        for value in (True, float("nan"), float("inf"), "23", None):
            with self.subTest(radius=value), self.assertRaises(ModelError):
                Settings.from_dict({"corner_radius": value})
        for value in (True, None, "unknown", [], {}):
            with self.subTest(placement=value), self.assertRaises(ModelError):
                Settings.from_dict({"placement": value})

    def test_overflow_wraps_before_resizing_and_keeps_the_saved_preference(self):
        settings = Settings(rows=1, icon_size=48, items=[Item(str(i), "path", f"/demo/{i}") for i in range(30)])
        for placement in ("bottom", "top", "left", "right"):
            settings.placement = placement
            geometry = dock_layout(settings, 1280, 720)
            self.assertGreater(geometry.rows, 1)
            self.assertEqual(settings.icon_size, geometry.icon_size)
            self.assertEqual(1, settings.rows)
        settings.placement = "bottom"
        self.assertEqual(1, dock_layout(settings, 3840, 2160).rows)
        settings.placement = "left"
        geometry = dock_layout(settings, 1920, 1080)
        self.assertTrue(geometry.vertical)
        self.assertEqual(geometry.positions[0][0], geometry.positions[1][0])
        self.assertGreater(geometry.positions[1][1], geometry.positions[0][1])

    def test_full_dock_fits_work_areas_without_losing_or_overlapping_tiles(self):
        items = [Item(str(i), "path", f"/demo/{i}") for i in range(30)]
        for width, height in ((320, 240), (640, 360), (640, 480), (1280, 720), (2560, 1440)):
            for placement in PLACEMENTS:
                for preferred_rows in (1, 4):
                    for labels in (False, True):
                        settings = Settings(rows=preferred_rows, icon_size=96, zoom=1.8, labels=labels, placement=placement, items=items)
                        with self.subTest(screen=(width, height), placement=placement, rows=preferred_rows, labels=labels):
                            geometry = dock_layout(settings, width, height)
                            self.assertLessEqual(geometry.width, width - 20)
                            self.assertLessEqual(geometry.height, height - 20)
                            self.assertEqual(31, len(set(geometry.positions)))
                            for x, y in geometry.positions:
                                self.assertGreaterEqual(x, 0)
                                self.assertGreaterEqual(y, 0)
                                self.assertLessEqual(x + geometry.cell_width, geometry.width)
                                self.assertLessEqual(y + geometry.cell_height, geometry.height)
                            for index, (x, y) in enumerate(geometry.positions):
                                for other_x, other_y in geometry.positions[index + 1:]:
                                    self.assertTrue(x + geometry.cell_width <= other_x or other_x + geometry.cell_width <= x or y + geometry.cell_height <= other_y or other_y + geometry.cell_height <= y)

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

    def test_startup_preference_is_optional_boolean_and_survives_profile_roundtrip(self):
        self.assertFalse(Settings().launch_at_login)
        self.assertFalse(Settings.from_dict({"items": [{"id": "legacy-trash", "kind": "trash"}]}).launch_at_login)
        for enabled in (False, True):
            with tempfile.TemporaryDirectory() as directory:
                path = Path(directory) / "settings.json"
                settings = Settings(launch_at_login=enabled)
                save_settings(path, settings)
                self.assertEqual(enabled, load_settings(path).launch_at_login)
        for value in (0, 1, "true", None, [], {}):
            with self.subTest(value=value), self.assertRaises(ModelError):
                Settings.from_dict({"launch_at_login": value})

    def test_desktop_auto_add_defaults_to_manual_and_requires_a_boolean(self):
        self.assertFalse(Settings().auto_add_desktop_shortcuts)
        self.assertFalse(Settings.from_dict({"labels": True}).auto_add_desktop_shortcuts)
        settings = Settings(auto_add_desktop_shortcuts=True)
        self.assertTrue(Settings.from_dict(settings.to_dict()).auto_add_desktop_shortcuts)
        for value in (0, 1, "true", None, [], {}):
            with self.subTest(value=value), self.assertRaises(ModelError):
                Settings.from_dict({"auto_add_desktop_shortcuts": value})

    def test_theme_migration_keeps_the_original_dusk_background_and_shortcuts(self):
        legacy = {"opacity": .37123, "corner_radius": 8.5, "rows": 3,
                  "items": [{"id": "old-computer", "kind": "computer"}, {"id": "old-trash", "kind": "trash"}]}
        settings = Settings.from_dict(legacy)
        self.assertEqual(("dusk", .37123, 8.5, {}), (settings.theme, settings.opacity, settings.corner_radius, settings.theme_appearances))
        self.assertEqual(["old-computer", "old-trash"], [item.id for item in settings.items])
        self.assertEqual(("#2A3146", "#11192B", 190, 150, 210, 2, .13),
                         (THEMES["dusk"].surface, THEMES["dusk"].bottom, THEMES["dusk"].hover_ms,
                          THEMES["dusk"].press_ms, THEMES["dusk"].move_ms, THEMES["dusk"].lift, THEMES["dusk"].press_depth))
        self.assertEqual(("dusk", .37123, 8.5), (Settings.from_dict(settings.to_dict()).theme, settings.opacity, settings.corner_radius))

    def test_explicit_first_theme_choice_uses_the_preset_and_keeps_global_preferences(self):
        settings = Settings(opacity=.413, corner_radius=37, rows=3, icon_size=38, zoom=1.42, labels=True,
                            placement="right", language="ru", x=-1200, y=150,
                            items=[Item("retained-trash", "trash")])
        original_geometry = dock_layout(settings, 1280, 720)
        for name, opacity, radius, surface, accent in (
            ("frost", .76, 22, "#EEF6FD", "#357CBA"),
            ("graphite", .96, 12, "#191C20", "#AAD0BD"),
            ("ember", .85, 16, "#37241F", "#EAB981"),
            ("aurora", .88, 24, "#17182F", "#C2B0FF"),
        ):
            settings.select_theme(name)
            self.assertEqual((name, opacity, radius, surface, accent),
                             (settings.theme, settings.opacity, settings.corner_radius, THEMES[name].surface, THEMES[name].accent))
            self.assertEqual(original_geometry, dock_layout(settings, 1280, 720))
            self.assertEqual((3, 38, 1.42, True, "right", "ru", -1200, 150, "retained-trash"),
                             (settings.rows, settings.icon_size, settings.zoom, settings.labels, settings.placement, settings.language, settings.x, settings.y, settings.items[0].id))
        settings.select_theme("dusk")
        self.assertEqual((.413, 37), (settings.opacity, settings.corner_radius))

    def test_each_theme_remembers_its_own_appearance_after_save_and_reload(self):
        settings = Settings(opacity=.39, corner_radius=0)
        settings.select_theme("frost")
        settings.opacity, settings.corner_radius = .52, 100
        settings.select_theme("graphite")
        settings.opacity, settings.corner_radius = .83, 7
        settings.select_theme("frost")
        self.assertEqual((.52, 100), (settings.opacity, settings.corner_radius))
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "settings.json"
            save_settings(path, settings)
            restored = load_settings(path)
            restored.select_theme("graphite")
            self.assertEqual((.83, 7), (restored.opacity, restored.corner_radius))
            restored.select_theme("dusk")
            self.assertEqual((.39, 0), (restored.opacity, restored.corner_radius))

    def test_invalid_optional_theme_data_does_not_discard_legacy_shortcuts(self):
        for theme in (None, True, 12, "unknown", [], {}):
            restored = Settings.from_dict({"theme": theme, "opacity": .47, "corner_radius": 33,
                                          "items": [{"id": "keep", "kind": "trash"}]})
            self.assertEqual(("dusk", .47, 33, "keep"), (restored.theme, restored.opacity, restored.corner_radius, restored.items[0].id))
        for invalid in (None, [], "bad", True, 42):
            self.assertEqual({}, Settings.from_dict({"theme_appearances": invalid}).theme_appearances)
        for invalid in (None, [], "bad", True, {}, {"opacity": .5}, {"opacity": True, "corner_radius": 20},
                        {"opacity": float("nan"), "corner_radius": 20}, {"opacity": .5, "corner_radius": float("inf")}):
            self.assertEqual({}, Settings.from_dict({"theme_appearances": {"frost": invalid}}).theme_appearances)
        values = {"unknown-" + str(index): {"opacity": .5, "corner_radius": 20} for index in range(20)}
        values.update({name: {"opacity": .5, "corner_radius": 20} for name in THEME_IDS})
        self.assertEqual(set(THEME_IDS), set(Settings.from_dict({"theme_appearances": values}).theme_appearances))
        restored = Settings.from_dict({"theme_appearances": {"frost": {"opacity": -2, "corner_radius": 1000}}})
        self.assertEqual({"opacity": .12, "corner_radius": 100.0}, restored.theme_appearances["frost"])

    def test_theme_foreground_and_secondary_text_remain_readable_on_settings(self):
        def luminance(value):
            components = [int(value[index:index + 2], 16) / 255 for index in (1, 3, 5)]
            linear = [number / 12.92 if number <= .04045 else ((number + .055) / 1.055) ** 2.4 for number in components]
            return sum(number * weight for number, weight in zip(linear, (.2126, .7152, .0722)))
        for name, theme in THEMES.items():
            for color in (theme.foreground, theme.muted):
                lighter, darker = sorted((luminance(color), luminance(theme.sheet)), reverse=True)
                self.assertGreaterEqual((lighter + .05) / (darker + .05), 4.5, name)
            self.assertEqual({"opacity": theme.opacity, "corner_radius": theme.corner_radius}, default_appearance(name))
            self.assertIn(theme.foreground.lower(), stylesheet(name).lower())

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

    def test_malformed_types_and_huge_numbers_use_recoverable_model_errors(self):
        for kind in ([], {}, 1, None):
            with self.subTest(kind=kind), self.assertRaises(ModelError):
                Settings.from_dict({"items": [{"id": "fixture", "kind": kind}]})
            with self.subTest(create_kind=kind), self.assertRaises(ModelError):
                Item.create(kind)
        huge = 10 ** 400
        for name in ("rows", "opacity", "icon_size", "zoom", "corner_radius"):
            for value in (huge, -huge):
                with self.subTest(name=name, sign=value > 0), self.assertRaises(ModelError):
                    Settings.from_dict({name: value})
        for name in ("x", "y"):
            for value in (huge, -huge, 2 ** 31, -(2 ** 31) - 1):
                with self.subTest(coordinate=name, value=value), self.assertRaises(ModelError):
                    Settings.from_dict({name: value})
        valid = Settings.from_dict({"x": -(2 ** 31), "y": 2 ** 31 - 1})
        self.assertEqual((-(2 ** 31), 2 ** 31 - 1), (valid.x, valid.y))

    def test_malformed_files_stay_intact_and_can_be_preserved_before_recovery(self):
        contents = (
            '{"items":[{"id":"fixture","kind":[],"path":""}]}',
            '{"items":[{"id":"fixture","kind":{},"path":""}]}',
            '{"rows":' + '9' * 400 + '}',
            '{"rows":' + '9' * 5000 + '}',
            # Python 3.12's C decoder permits deeper input than Python 3.11.
            # Exceed both limits so this checks recovery, not a version's cutoff.
            '{"unused":' + '[' * 10000 + '0' + ']' * 10000 + '}',
        )
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "settings.json"
            for content in contents:
                original = content.encode("utf-8")
                path.write_bytes(original)
                with self.subTest(size=len(original)), self.assertRaises(ModelError):
                    load_settings(path)
                self.assertEqual(original, path.read_bytes())
                backup = preserve_invalid_settings(path)
                self.assertEqual(original, backup.read_bytes())
                save_settings(path, Settings(items=[Item("valid", "trash")]))
                self.assertEqual("valid", load_settings(path).items[0].id)
                self.assertEqual(original, backup.read_bytes())

    def test_huge_optional_theme_numbers_do_not_discard_existing_shortcuts(self):
        raw = {"items": [{"id": "keep", "kind": "trash"}],
               "theme_appearances": {"frost": {"opacity": 10 ** 400, "corner_radius": 10}}}
        settings = Settings.from_dict(raw)
        self.assertEqual(["keep"], [item.id for item in settings.items])
        self.assertNotIn("frost", settings.theme_appearances)

    def test_settings_limit_is_applied_to_actual_read_bytes(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "settings.json"
            original = b" " * (1024 * 1024 + 1)
            path.write_bytes(original)
            with self.assertRaises(ModelError) as raised:
                load_settings(path)
            self.assertEqual("settings_too_large", raised.exception.key)
            self.assertEqual(original, path.read_bytes())

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
