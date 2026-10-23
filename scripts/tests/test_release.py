"""Architecture mistakes must fail before a public release is created."""
import importlib.util
import io
from pathlib import Path
import struct
import tarfile
import tempfile
import unittest
import zipfile

spec = importlib.util.spec_from_file_location("prepare_release", Path(__file__).parents[1] / "PrepareRelease.py")
release = importlib.util.module_from_spec(spec)
spec.loader.exec_module(release)


def pe(machine):
    data = bytearray(256)
    data[:2] = b"MZ"
    struct.pack_into("<I", data, 0x3C, 128)
    data[128:132] = b"PE\0\0"
    struct.pack_into("<H", data, 132, machine)
    return data


def elf(machine):
    data = bytearray(64)
    data[:6] = b"\x7fELF\x02\x01"
    struct.pack_into("<H", data, 18, machine)
    return data


class ReleaseArchitectureTests(unittest.TestCase):
    def test_arm_windows_rejects_x64_binary_even_with_correct_filename(self):
        with tempfile.TemporaryDirectory(prefix="yarnelo-release-") as temp:
            path = Path(temp) / "Yarnelo-Windows-arm64-Portable.zip"
            with zipfile.ZipFile(path, "w") as archive:
                archive.writestr("Yarnelo.exe", pe(0x8664))
                archive.writestr("Yarnelo.exe.config", "Version=v4.8.1")
            with self.assertRaisesRegex(ValueError, "architecture"):
                release.validate_architecture(path)

    def test_arm_windows_requires_native_runtime_and_accepts_arm_payload(self):
        with tempfile.TemporaryDirectory(prefix="yarnelo-release-") as temp:
            path = Path(temp) / "Yarnelo-Windows-arm64-Portable.zip"
            for runtime, valid in (("v4.8", False), ("v4.8.1", True)):
                with zipfile.ZipFile(path, "w") as archive:
                    archive.writestr("Yarnelo.exe", pe(0xAA64))
                    archive.writestr("Yarnelo.exe.config", f"Version={runtime}")
                if valid:
                    release.validate_architecture(path)
                else:
                    with self.assertRaisesRegex(ValueError, "4.8.1"):
                        release.validate_architecture(path)

    def test_appimage_rejects_wrong_architecture(self):
        with tempfile.TemporaryDirectory(prefix="yarnelo-release-") as temp:
            path = Path(temp) / "Yarnelo-Linux-aarch64.AppImage"
            path.write_bytes(elf(62))
            with self.assertRaisesRegex(ValueError, "architecture"):
                release.validate_architecture(path)
            path.write_bytes(elf(183))
            release.validate_architecture(path)

    def test_tar_checks_contained_launcher_not_just_archive_name(self):
        with tempfile.TemporaryDirectory(prefix="yarnelo-release-") as temp:
            path = Path(temp) / "Yarnelo-Linux-aarch64.tar.gz"
            for machine in (62, 183):
                with tarfile.open(path, "w:gz") as archive:
                    content = elf(machine)
                    entry = tarfile.TarInfo("Yarnelo/yarnelo")
                    entry.size = len(content)
                    archive.addfile(entry, io.BytesIO(content))
                if machine == 183:
                    release.validate_architecture(path)
                else:
                    with self.assertRaisesRegex(ValueError, "architecture"):
                        release.validate_architecture(path)

    def test_malformed_executables_are_rejected(self):
        with self.assertRaises(ValueError):
            release.pe_machine(b"not a PE executable")
        with self.assertRaises(ValueError):
            release.elf_machine(b"not an ELF executable")


if __name__ == "__main__":
    unittest.main()
