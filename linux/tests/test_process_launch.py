"""Real QtCore launcher checks, without a dock, display or user profile."""

import ast
from pathlib import Path
import os
import shutil
import sys
import tempfile
import time
from types import SimpleNamespace
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from yarnelo import external_environment


class ProcessLaunchTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        try:
            from PySide6.QtCore import QCoreApplication, QEvent, QObject, QProcess, QProcessEnvironment
        except ImportError as error:
            raise unittest.SkipTest("QtCore is required for native launcher checks") from error
        cls.qt = SimpleNamespace(Core=QCoreApplication, Event=QEvent, Object=QObject,
                                 Process=QProcess, Environment=QProcessEnvironment)
        cls.app = QCoreApplication.instance() or QCoreApplication([])
        # Exercise the production nested method without constructing QApplication
        # or running main(), which would inspect the real Linux desktop/profile.
        tree = ast.parse((Path(__file__).resolve().parents[1] / "yarnelo.py").read_text(encoding="utf-8-sig"))
        launch = next(node for node in ast.walk(tree)
                      if isinstance(node, ast.FunctionDef) and node.name == "launch_command")
        module = ast.fix_missing_locations(ast.Module(body=[launch], type_ignores=[]))
        cls.namespace = dict(QProcess=QProcess, QProcessEnvironment=QProcessEnvironment,
                             os=os, shutil=shutil, sys=sys, Path=Path,
                             external_environment=external_environment)
        exec(compile(module, "<production launch_command>", "exec"), cls.namespace)

        class Fixture(QObject):
            launch_command = cls.namespace["launch_command"]

            def __init__(self):
                super().__init__()
                self.processes = set()

            def t(self, key, **values):
                return key + (": " + str(values["error"]) if "error" in values else "")

        cls.Fixture = Fixture

    def setUp(self):
        self.directory = tempfile.TemporaryDirectory(prefix="yarnelo-launch-")
        self.root = Path(self.directory.name)
        self.fixture = self.Fixture()
        self.messages = []
        self.namespace["shutil"] = shutil
        self.namespace["QMessageBox"] = SimpleNamespace(warning=lambda _owner, _title, message: self.messages.append(message))

    def tearDown(self):
        # Terminate only this test's own synthetic children if an assertion fails.
        for process in tuple(self.fixture.processes):
            process.kill()
            process.waitForFinished(5000)
        self.app.processEvents()
        self.fixture.deleteLater()
        self.qt.Core.sendPostedEvents(None, self.qt.Event.Type.DeferredDelete)
        self.directory.cleanup()

    def until(self, predicate, timeout=15):
        end = time.monotonic() + timeout
        while time.monotonic() < end:
            self.app.processEvents()
            if predicate():
                return
            time.sleep(.005)
        self.fail("Synthetic launcher did not reach the expected state")

    def start_output_child(self, *, exit_code=0):
        marker = self.root / "output-flushed"
        child = self.root / "output-child.py"
        child.write_text(
            "import pathlib,sys,time\n"
            "sys.stdout.buffer.write(b'o' * (16 * 1024 * 1024)); sys.stdout.flush()\n"
            "sys.stderr.buffer.write(b'e' * (4 * 1024 * 1024)); sys.stderr.flush()\n"
            "sys.stderr.buffer.write('\\nПоследняя ошибка_日本語_END\\n'.encode('utf-8')); sys.stderr.flush()\n"
            "pathlib.Path(sys.argv[1]).write_text('ready', encoding='ascii')\n"
            "time.sleep(3)\n"
            "raise SystemExit(int(sys.argv[2]))\n", encoding="utf-8")
        self.fixture.launch_command(sys.executable, [str(child), str(marker), str(exit_code)])
        self.assertEqual(1, len(self.fixture.processes))
        process = next(iter(self.fixture.processes))
        self.until(marker.exists)
        self.app.processEvents()
        self.assertEqual(self.qt.Process.ProcessState.Running, process.state())
        self.assertEqual(b"", bytes(process.readAllStandardOutput()), "Unused stdout must bypass Qt's memory buffer")
        self.assertLessEqual(process.bytesAvailable(), 8192, "Stderr must be drained while the child is still running")
        self.until(lambda: not self.fixture.processes)

    def test_chatty_successful_launcher_has_bounded_buffers_and_cleans_up(self):
        self.start_output_child()
        self.assertEqual([], self.messages)

    def test_failed_launcher_keeps_only_a_bounded_unicode_diagnostic_tail(self):
        self.start_output_child(exit_code=3)
        self.assertEqual(1, len(self.messages))
        prefix, detail = self.messages[0].split(": ", 1)
        self.assertEqual("open_failed", prefix)
        self.assertLessEqual(len(detail), 1200)
        self.assertTrue(detail.endswith("Последняя ошибка_日本語_END"))

    def test_failed_to_start_reports_error_and_releases_the_process(self):
        # Pass discovery so the real QProcess FailedToStart path is exercised.
        self.namespace["shutil"] = SimpleNamespace(which=lambda program: program)
        self.fixture.launch_command(str(self.root / "does-not-exist"), [])
        self.until(lambda: not self.fixture.processes)
        self.assertEqual(1, len(self.messages))
        self.assertTrue(self.messages[0].startswith("launch_failed: "))


if __name__ == "__main__":
    unittest.main()
