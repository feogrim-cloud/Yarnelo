"""Isolated XDG registration tests; no test uses the real user config."""

import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import time
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from yarnelo_autostart import (
    AutostartManager, OWNER_KEY, decode_exec, encode_exec, resolve_startup_command,
)
from yarnelo_model import ModelError


class AutostartTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.home = self.root / "home"
        self.config = self.home / ".config"
        self.system = self.root / "system"
        self.command = [sys.executable, str((self.root / "Yarnelo space % Ю.py").absolute()), "--startup"]
        Path(self.command[1]).write_text("# source fixture\n", encoding="utf-8")
        self.manager = self.manager_for()

    def manager_for(self, **kwargs):
        args = dict(config_home=self.config, environ={}, home=self.home, config_dirs=[self.system])
        args.update(kwargs)
        return AutostartManager(self.command, **args)

    def entry(self, *, system=False, content=None):
        path = (self.system if system else self.config) / "autostart" / "yarnelo.desktop"
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(self.manager._content(True) if content is None else content.encode("utf-8"))
        return path

    def assert_key(self, key, call):
        with self.assertRaises(ModelError) as raised:
            call()
        self.assertEqual(key, raised.exception.key)

    def test_reading_missing_registration_has_no_side_effects(self):
        self.assertEqual("missing", self.manager.status)
        self.assertFalse(self.manager.enabled)
        self.assertFalse(self.config.exists())

    def test_enable_readback_and_disable_remove_only_registration(self):
        keep = self.config / "autostart" / "other.desktop"
        keep.parent.mkdir(parents=True)
        keep.write_text("do not change", encoding="utf-8")
        self.manager.set_enabled(True)
        self.assertTrue(self.manager.enabled)
        self.assertEqual("enabled", self.manager.status)
        raw = self.manager.path.read_text(encoding="utf-8")
        self.assertIn(f"{OWNER_KEY}=true", raw)
        self.assertIn("Hidden=false", raw)
        self.manager.set_enabled(False)
        self.assertFalse(self.manager.path.exists())
        self.assertFalse(self.manager.enabled)
        self.assertEqual("do not change", keep.read_text(encoding="utf-8"))
        self.assertEqual([], list(keep.parent.glob(".yarnelo-*.tmp")))

    def test_repeated_disable_on_missing_does_not_create_directories(self):
        self.manager.set_enabled(False)
        self.manager.set_enabled(False)
        self.assertFalse(self.config.exists())

    def test_relative_xdg_config_home_is_ignored(self):
        manager = self.manager_for(config_home=None, environ={"XDG_CONFIG_HOME": "relative"})
        self.assertEqual(self.home / ".config" / "autostart" / "yarnelo.desktop", manager.path)

    def test_absolute_xdg_config_home_is_used(self):
        other = self.root / "custom config"
        manager = self.manager_for(config_home=None, environ={"XDG_CONFIG_HOME": str(other)})
        self.assertEqual(other / "autostart" / "yarnelo.desktop", manager.path)

    def test_relative_system_xdg_directories_are_ignored(self):
        manager = self.manager_for(config_dirs=None, environ={"XDG_CONFIG_DIRS": "relative:also-relative"})
        self.assertEqual((), manager.config_dirs)

    def test_external_hidden_state_is_read_on_each_access(self):
        path = self.entry()
        self.assertTrue(self.manager.enabled)
        path.write_text(path.read_text(encoding="utf-8").replace("Hidden=false", "Hidden=true"), encoding="utf-8")
        self.assertFalse(self.manager.enabled)
        self.assertEqual("disabled", self.manager.status)
        self.manager.set_enabled(True)
        self.assertTrue(self.manager.enabled)

    def test_gnome_external_disable_is_respected(self):
        path = self.entry()
        with path.open("a", encoding="utf-8") as stream:
            stream.write("X-GNOME-Autostart-enabled=false\n")
        self.assertFalse(self.manager.enabled)
        self.manager.set_enabled(True)
        self.assertTrue(self.manager.enabled)

    def test_only_show_in_and_not_show_in_are_respected(self):
        cases = [
            ("OnlyShowIn=GNOME;KDE;", "GNOME:Classic", True),
            ("OnlyShowIn=GNOME;", "KDE", False),
            ("OnlyShowIn=GNOME;", "", False),
            ("NotShowIn=GNOME;", "GNOME:Classic", False),
            ("NotShowIn=GNOME;", "KDE", True),
            ("OnlyShowIn=GNOME;\nNotShowIn=KDE;", "GNOME", False),
        ]
        for extra, desktop, expected in cases:
            with self.subTest(extra=extra, desktop=desktop):
                self.entry(content=self.manager._content(True).decode() + extra + "\n")
                self.assertEqual(expected, self.manager_for(environ={"XDG_CURRENT_DESKTOP": desktop}).enabled)

    def test_try_exec_missing_and_path_lookup(self):
        original = self.manager._content(True).decode()
        for value, path_env, expected in (("missing-yarnelo-xyz", "", False),
                                          (Path(sys.executable).name, str(Path(sys.executable).parent), True)):
            with self.subTest(value=value):
                content = "\n".join("TryExec=" + value if line.startswith("TryExec=") else line for line in original.splitlines())
                self.entry(content=content + "\n")
                self.assertEqual(expected, self.manager_for(environ={"PATH": path_env}).enabled)

    def test_foreign_entry_never_overwritten_or_removed(self):
        for content in ("[Desktop Entry]\nType=Application\nName=Other\nExec=other\n",
                        self.manager._content(True).decode().replace(f"{OWNER_KEY}=true", f"{OWNER_KEY}=false"),
                        self.manager._content(True).decode().replace("--startup", "--other")):
            with self.subTest(content=content):
                path = self.entry(content=content)
                previous = path.read_bytes()
                self.assertEqual("foreign", self.manager.status)
                self.assertFalse(self.manager.enabled)
                for enabled in (False, True):
                    self.assert_key("startup_entry_foreign", lambda: self.manager.set_enabled(enabled))
                    self.assertEqual(previous, path.read_bytes())

    def test_malformed_file_is_preserved(self):
        path = self.entry(content="not a desktop entry\n")
        for action in (lambda: self.manager.enabled, lambda: self.manager.set_enabled(True), lambda: self.manager.set_enabled(False)):
            self.assert_key("startup_entry_invalid", action)
        self.assertEqual("not a desktop entry\n", path.read_text(encoding="utf-8"))

    def test_duplicate_desktop_key_and_oversized_entry_are_rejected(self):
        for content in (self.manager._content(True).decode() + "Hidden=true\n", "#" * 65537):
            with self.subTest(length=len(content)):
                self.entry(content=content)
                self.assert_key("startup_entry_invalid", lambda: self.manager.set_enabled(True))

    def test_system_owned_entry_can_be_disabled_with_user_override(self):
        system = self.entry(system=True)
        original = system.read_bytes()
        self.assertTrue(self.manager.enabled)
        self.assertFalse(self.config.exists())
        self.manager.set_enabled(False)
        self.assertFalse(self.manager.enabled)
        self.assertIn("Hidden=true", self.manager.path.read_text(encoding="utf-8"))
        self.assertEqual(original, system.read_bytes())
        self.manager.set_enabled(True)
        self.assertTrue(self.manager.enabled)
        self.assertEqual(original, system.read_bytes())

    def test_user_hidden_takes_precedence_over_system(self):
        self.entry(system=True)
        self.entry(content=self.manager._content(False).decode())
        self.assertFalse(self.manager.enabled)

    def test_first_system_directory_takes_precedence(self):
        other = self.root / "system-2"
        self.entry(system=True, content=self.manager._content(False).decode())
        manager = self.manager_for(config_dirs=[self.system, other])
        target = other / "autostart" / "yarnelo.desktop"
        target.parent.mkdir(parents=True)
        target.write_bytes(manager._content(True))
        self.assertFalse(manager.enabled)

    def test_foreign_system_file_is_preserved(self):
        path = self.entry(system=True, content="[Desktop Entry]\nType=Application\nName=Other\nExec=other\n")
        before = path.read_bytes()
        for enabled in (True, False):
            self.assert_key("startup_entry_foreign", lambda: self.manager.set_enabled(enabled))
        self.assertEqual(before, path.read_bytes())
        self.assertFalse(self.config.exists())

    def test_disabling_user_entry_does_not_expose_foreign_system_file(self):
        self.entry(system=True, content="[Desktop Entry]\nType=Application\nName=Other\nExec=other\n")
        self.entry()
        self.manager.set_enabled(False)
        self.assertFalse(self.manager.enabled)
        self.assertIn("Hidden=true", self.manager.path.read_text(encoding="utf-8"))

    def test_missing_binary_prevents_enable_but_allows_disable(self):
        self.entry()
        with patch("yarnelo_autostart._executable", return_value=False):
            self.assert_key("startup_command_missing", lambda: self.manager.set_enabled(True))
            self.manager.set_enabled(False)
        self.assertFalse(self.manager.path.exists())

    def test_atomic_replace_failure_keeps_existing_entry_and_removes_temporary(self):
        path = self.entry(content=self.manager._content(False).decode())
        before = path.read_bytes()
        with patch("yarnelo_autostart.os.replace", side_effect=PermissionError("fixture failure")):
            self.assert_key("startup_entry_unsafe", lambda: self.manager.set_enabled(True))
        self.assertEqual(before, path.read_bytes())
        self.assertEqual([], list(path.parent.glob(".yarnelo-*.tmp")))

    def test_concurrent_entry_replacement_is_preserved(self):
        self.entry()
        foreign = "[Desktop Entry]\nName=Changed\nExec=changed\n"
        unchanged = self.manager._unchanged
        def replace_before_check(previous):
            self.manager.path.write_text(foreign, encoding="utf-8")
            unchanged(previous)
        with patch.object(self.manager, "_unchanged", side_effect=replace_before_check):
            self.assert_key("startup_entry_unsafe", lambda: self.manager.set_enabled(True))
        self.assertEqual(foreign, self.manager.path.read_text(encoding="utf-8"))

    def test_readback_failure_is_reported(self):
        with patch.object(self.manager, "_write", return_value=None):
            self.assert_key("startup_verify_failed", lambda: self.manager.set_enabled(True))

    def test_hard_link_registration_is_never_edited(self):
        path = self.entry()
        other = self.root / "keep.desktop"
        try:
            os.link(path, other)
        except OSError:
            self.skipTest("hard links unavailable")
        before = other.read_bytes()
        self.assert_key("startup_entry_unsafe", lambda: self.manager.set_enabled(False))
        self.assertEqual(before, other.read_bytes())

    def test_symlink_registration_and_directory_are_never_followed(self):
        target = self.root / "keep.desktop"
        target.write_bytes(self.manager._content(True))
        self.manager.path.parent.mkdir(parents=True)
        try:
            self.manager.path.symlink_to(target)
        except OSError:
            self.skipTest("symlinks unavailable to this user")
        before = target.read_bytes()
        self.assert_key("startup_entry_unsafe", lambda: self.manager.set_enabled(True))
        self.assertEqual(before, target.read_bytes())
        self.manager.path.unlink()
        self.manager.path.parent.rmdir()
        other = self.root / "other"
        other.mkdir()
        self.manager.path.parent.symlink_to(other, target_is_directory=True)
        self.assert_key("startup_entry_unsafe", lambda: self.manager.set_enabled(True))
        self.assertEqual([], list(other.iterdir()))

    def test_changed_install_path_is_foreign_and_not_migrated(self):
        self.entry()
        command = [sys.executable, str(self.root / "moved.py"), "--startup"]
        manager = AutostartManager(command, self.config, environ={}, config_dirs=[])
        self.assertEqual("foreign", manager.status)
        self.assert_key("startup_entry_foreign", lambda: manager.set_enabled(True))

    def test_exec_round_trip_literal_characters(self):
        args = [sys.executable, "space Юникод 100%", 'quote" dollar$ backtick` backslash\\', "';&|#()<>*?~", " trailing "]
        self.assertEqual(args, decode_exec(encode_exec(args)))
        encoded = encode_exec([sys.executable, "\\$`\"%"])
        self.assertIn("\\\\\\\\", encoded)  # Four file backslashes encode one literal backslash.
        self.assertIn("%%", encoded)

    def test_exec_rejects_shell_expansion_and_field_codes(self):
        for value in ('"python" "%f"', '"python" "$(touch x)"', 'python;touch', '"python" "`touch x`"', '"unterminated', '"a"tail'):
            with self.subTest(value=value):
                self.assert_key("startup_entry_invalid", lambda: decode_exec(value))

    def test_command_rejects_relative_and_injected_control_characters(self):
        for command in ([], "python", ["relative"], [sys.executable, "bad\nline"], [str(self.root / "a=b")]):
            with self.subTest(command=command):
                self.assert_key("startup_command_invalid", lambda: AutostartManager(command, self.config))

    def test_source_resolver_uses_absolute_python_script_and_startup_flag(self):
        self.assertEqual(self.command, resolve_startup_command(executable=sys.executable, script=self.command[1], frozen=False, environ={}))

    def test_source_resolver_missing_script_is_rejected(self):
        self.assert_key("startup_command_missing", lambda: resolve_startup_command(executable=sys.executable, script=self.root / "missing.py", frozen=False, environ={}))

    def test_frozen_archive_deb_rpm_keep_stable_external_binary(self):
        self.assertEqual([sys.executable, "--startup"], resolve_startup_command(executable=sys.executable, frozen=True, environ={}))

    def test_appimage_uses_outer_file_and_preserves_extract_and_run_mode(self):
        app_dir = self.root / ".mount_YarneloFixture"
        inner = app_dir / "usr" / "lib" / "yarnelo" / "yarnelo"
        inner.parent.mkdir(parents=True)
        inner.write_bytes(b"fixture inner executable")
        inner.chmod(0o700)
        outer = self.root / "Yarnelo 100% Ю.AppImage"
        outer.write_bytes(b"fixture outer executable")
        outer.chmod(0o700)
        for mode in (None, "1", "0", ""):
            with self.subTest(mode=mode):
                env = {"APPIMAGE": str(outer), "APPDIR": str(app_dir)}
                if mode is not None:
                    env["APPIMAGE_EXTRACT_AND_RUN"] = mode
                expected = [str(outer), *(["--appimage-extract-and-run"] if mode is not None else []), "--startup"]
                self.assertEqual(expected, resolve_startup_command(executable=inner, frozen=True, environ=env))
                manager = AutostartManager(expected, self.config, environ={}, config_dirs=[])
                manager.set_enabled(True)
                self.assertTrue(manager.enabled)
                self.assertEqual(expected, decode_exec(next(line[5:] for line in manager.path.read_text(encoding="utf-8").splitlines() if line.startswith("Exec="))))

    def test_appimage_cli_only_extract_and_run_keeps_runtime_flag(self):
        app_dir = self.root / "appimage_extracted_fcdcab5a421c40bc8aeb422c5e59b08e"
        inner = app_dir / "usr" / "lib" / "yarnelo" / "yarnelo"
        inner.parent.mkdir(parents=True)
        inner.write_bytes(b"fixture executable")
        inner.chmod(0o700)
        outer = self.root / "Yarnelo permanent % Ю.AppImage"
        outer.write_bytes(b"fixture outer AppImage")
        outer.chmod(0o700)
        # Runtime removes the CLI flag and sets APPDIR, but does not add the
        # environment switch when it was absent from the original invocation.
        env = {"APPIMAGE": str(outer), "APPDIR": str(app_dir)}
        self.assertEqual([str(outer), "--appimage-extract-and-run", "--startup"],
                         resolve_startup_command(executable=inner, frozen=True, environ=env))

    def test_appimage_cannot_register_an_inner_path_as_outer_image(self):
        self.assert_key("startup_command_unstable", lambda: resolve_startup_command(
            executable=sys.executable, frozen=True,
            environ={"APPIMAGE": sys.executable, "APPDIR": str(Path(sys.executable).parent)}))

    def test_appimage_entry_is_owned_across_both_runtime_modes(self):
        normal = AutostartManager([sys.executable, "--startup"], self.config, environ={}, config_dirs=[])
        extracted = AutostartManager([sys.executable, "--appimage-extract-and-run", "--startup"], self.config, environ={}, config_dirs=[])
        for first, second in ((normal, extracted), (extracted, normal)):
            with self.subTest(command=first.command):
                first.set_enabled(True)
                self.assertTrue(second.enabled)
                second.set_enabled(False)
                self.assertFalse(first.enabled)
                second.set_enabled(True)
                self.assertTrue(first.enabled)
                first.set_enabled(False)

    def test_source_resolver_preserves_virtualenv_python_symlink(self):
        python = self.root / "venv" / "bin" / "python"
        python.parent.mkdir(parents=True)
        try:
            python.symlink_to(sys.executable)
        except OSError:
            self.skipTest("symlinks unavailable to this user")
        command = resolve_startup_command(executable=python, script=self.command[1], frozen=False, environ={})
        self.assertEqual(str(python), command[0])

    def test_permission_failure_during_status_uses_localizable_error(self):
        with patch("yarnelo_autostart._safe_directories", side_effect=PermissionError("fixture")):
            self.assert_key("startup_entry_unsafe", lambda: self.manager.enabled)

    def test_appimage_rejects_unrelated_appdir_missing_outer_and_transient_paths(self):
        for env in ({"APPIMAGE": sys.executable, "APPDIR": str(self.root)},
                    {"APPDIR": str(Path(sys.executable).parent)},
                    {"APPIMAGE": "relative", "APPDIR": str(Path(sys.executable).parent)}):
            with self.subTest(env=env):
                self.assert_key("startup_command_unstable", lambda: resolve_startup_command(executable=sys.executable, frozen=True, environ=env))
        with patch("yarnelo_autostart._executable", return_value=True):
            for part in (".mount_Yarneloxyz", "appimage_extracted_xyz"):
                self.assert_key("startup_command_unstable", lambda: resolve_startup_command(executable=self.root / part / "yarnelo", frozen=True, environ={}))

    @unittest.skipUnless(sys.platform.startswith("linux"), "native Desktop Entry launch requires Linux")
    def test_native_gio_launch_preserves_literal_exec_arguments(self):
        if not shutil.which("gio") or not shutil.which("desktop-file-validate"):
            self.skipTest("gio and desktop-file-validate are not installed")
        # Check with the desktop implementation, not just our own parser.
        executable = self.root / 'Yarnelo Ю % $ ` " \\ launcher'
        output = self.root / "launched.json"
        executable.write_text("#!/usr/bin/env python3\nimport json,sys\nfrom pathlib import Path\nPath(sys.argv[1]).write_text(json.dumps(sys.argv[2:]), encoding='utf-8')\n", encoding="utf-8")
        executable.chmod(0o700)
        arguments = ["Unicode Ю with spaces", '100% $ ` " \\ literal', "';&|#()<>*?~", "--startup"]
        manager = AutostartManager([str(executable), str(output), *arguments], self.config, environ={}, config_dirs=[])
        manager.set_enabled(True)
        validated = subprocess.run(["desktop-file-validate", str(manager.path)], text=True, capture_output=True)
        self.assertEqual(0, validated.returncode, validated.stdout + validated.stderr)
        launched = subprocess.run(["gio", "launch", str(manager.path)], text=True, capture_output=True)
        self.assertEqual(0, launched.returncode, launched.stdout + launched.stderr)
        deadline = time.monotonic() + 10
        while not output.exists() and time.monotonic() < deadline:
            time.sleep(0.05)
        self.assertTrue(output.exists(), "gio did not invoke the fixture launcher")
        self.assertEqual(arguments, json.loads(output.read_text(encoding="utf-8")))


if __name__ == "__main__":
    unittest.main()
