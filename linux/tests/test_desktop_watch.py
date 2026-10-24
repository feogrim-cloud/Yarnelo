import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from yarnelo_desktop_watch import DesktopShortcutTracker


class DesktopWatchTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.desktop = Path(self.temporary.name) / "Desktop"
        self.desktop.mkdir()
        self.now = 0.0
        self.tracker = DesktopShortcutTracker(self.desktop, clock=lambda: self.now)

    def create(self, name="New.desktop", content="[Desktop Entry]\nType=Application\nName=New\nExec=true\n"):
        path = self.desktop / name
        path.write_text(content, encoding="utf-8")
        return path

    def test_existing_launchers_and_changes_to_them_are_never_imported(self):
        path = self.create()
        tracker = DesktopShortcutTracker(self.desktop)
        self.assertEqual([], tracker.scan())
        path.write_text("changed old launcher", encoding="utf-8")
        self.assertEqual([], tracker.scan())
        self.assertEqual([], tracker.scan())

    def test_new_file_needs_two_stable_polls_and_success_is_acknowledged(self):
        path = self.create()
        before = path.read_bytes()
        self.assertEqual([], self.tracker.scan())
        self.assertEqual([path], self.tracker.scan())
        self.assertEqual([path], self.tracker.scan())
        self.tracker.acknowledge(path)
        self.assertEqual([], self.tracker.scan())
        self.assertEqual(before, path.read_bytes())

    def test_partial_file_must_be_nonempty_and_stable(self):
        path = self.create(content="")
        self.assertEqual([], self.tracker.scan())
        self.assertEqual([], self.tracker.scan())
        path.write_text("first part", encoding="utf-8")
        self.assertEqual([], self.tracker.scan())
        path.write_text("a larger second part", encoding="utf-8")
        self.assertEqual([], self.tracker.scan())
        self.assertEqual([path], self.tracker.scan())

    def test_unacknowledged_failures_expire_after_30_seconds(self):
        path = self.create()
        self.tracker.scan()
        self.now = 1
        self.assertEqual([path], self.tracker.scan())
        self.now = 29.9
        self.assertEqual([path], self.tracker.scan())
        self.now = 30
        self.assertEqual([], self.tracker.scan())
        self.now = 100
        self.assertEqual([], self.tracker.scan())
        self.assertTrue(path.exists())

    def test_deleted_then_recreated_name_is_new(self):
        path = self.create()
        self.tracker.scan()
        self.tracker.scan()
        self.tracker.acknowledge(path)
        path.unlink()
        self.assertEqual([], self.tracker.scan())
        self.create()
        self.assertEqual([], self.tracker.scan())
        self.assertEqual([path], self.tracker.scan())

    def test_only_desktop_files_in_the_top_level_are_observed(self):
        self.create("not-a-shortcut.txt")
        directory = self.desktop / "directory.desktop"
        directory.mkdir()
        nested = self.desktop / "subdirectory"
        nested.mkdir()
        (nested / "nested.desktop").write_text("nested", encoding="utf-8")
        self.assertEqual([], self.tracker.scan())
        self.assertEqual([], self.tracker.scan())

    def test_reset_forms_new_baseline_without_importing_old_files(self):
        self.create()
        self.tracker.reset()
        self.assertEqual([], self.tracker.scan())
        self.assertEqual([], self.tracker.scan())
        path = self.create("Later.desktop")
        self.tracker.scan()
        self.assertEqual([path], self.tracker.scan())

    def test_close_stops_tracking(self):
        self.create()
        self.tracker.scan()
        self.tracker.close()
        self.assertEqual([], self.tracker.scan())
        self.tracker.reset()
        self.assertEqual([], self.tracker.scan())

    def test_missing_directory_can_appear_later(self):
        directory = self.desktop / "future"
        tracker = DesktopShortcutTracker(directory)
        self.assertFalse(directory.exists())
        self.assertEqual([], tracker.scan())
        directory.mkdir()
        path = directory / "new.desktop"
        path.write_text("new", encoding="utf-8")
        self.assertEqual([], tracker.scan())
        self.assertEqual([path], tracker.scan())

    def test_unreadable_initial_directory_gets_baseline_on_first_success(self):
        with patch.object(DesktopShortcutTracker, "_names", return_value=None):
            tracker = DesktopShortcutTracker(self.desktop)
        self.create()
        self.assertEqual([], tracker.scan())
        self.assertEqual([], tracker.scan())

    def test_unreadable_file_is_retried_without_modifying_it(self):
        path = self.create()
        with patch("yarnelo_desktop_watch.os.open", side_effect=PermissionError("fixture")):
            self.assertEqual([], self.tracker.scan())
        self.assertEqual([], self.tracker.scan())
        self.assertEqual([path], self.tracker.scan())
        self.assertTrue(path.exists())

    def test_symlink_launcher_and_directory_are_ignored(self):
        source = self.desktop.parent / "outside.desktop"
        source.write_text("outside", encoding="utf-8")
        try:
            (self.desktop / "link.desktop").symlink_to(source)
        except OSError:
            self.skipTest("symlinks unavailable")
        self.assertEqual([], self.tracker.scan())
        self.assertEqual([], self.tracker.scan())
        linked_directory = self.desktop.parent / "linked"
        linked_directory.symlink_to(self.desktop, target_is_directory=True)
        tracker = DesktopShortcutTracker(linked_directory)
        self.create()
        self.assertEqual([], tracker.scan())
        self.assertEqual([], tracker.scan())
        self.assertEqual("outside", source.read_text(encoding="utf-8"))

    def test_hardlinked_launcher_is_ignored(self):
        path = self.create()
        try:
            os.link(path, self.desktop.parent / "same.desktop")
        except OSError:
            self.skipTest("hard links unavailable")
        self.assertEqual([], self.tracker.scan())
        self.assertEqual([], self.tracker.scan())


if __name__ == "__main__":
    unittest.main()
