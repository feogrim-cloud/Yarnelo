# -*- mode: python ; coding: utf-8 -*-
"""PyInstaller recipe. Only the four Qt modules used by Yarnelo are included."""
from pathlib import Path
from PySide6.QtCore import QLibraryInfo

root = Path(SPECPATH).resolve().parents[1]
qt_modules = (
    "3DAnimation", "3DCore", "3DExtras", "3DInput", "3DLogic", "3DRender",
    "Bluetooth", "Charts", "Concurrent", "DBus", "DataVisualization", "Designer",
    "Graphs", "Help", "HttpServer", "Location", "Multimedia", "MultimediaWidgets",
    "NetworkAuth", "Nfc", "OpenGL", "OpenGLWidgets", "Pdf", "PdfWidgets",
    "Positioning", "PrintSupport", "Qml", "Quick", "Quick3D", "QuickControls2",
    "QuickTest", "QuickWidgets", "RemoteObjects", "Scxml", "Sensors", "SerialBus",
    "SerialPort", "SpatialAudio", "Sql", "StateMachine", "Svg", "SvgWidgets",
    "Test", "TextToSpeech", "UiTools", "WebChannel", "WebEngineCore",
    "WebEngineQuick", "WebEngineWidgets", "WebSockets", "WebView", "Xml",
)
app = Analysis(
    [str(root / "linux" / "yarnelo.py")],
    pathex=[str(root / "linux")],
    binaries=[],
    datas=[
        (str(root / "assets" / "Yarnelo-Icon-256.png"), "assets"),
        (str(root / "assets" / "Yarnelo-Icon.svg"), "assets"),
        # Keep standard file/message dialog buttons in the selected app language.
        (str(Path(QLibraryInfo.path(QLibraryInfo.LibraryPath.TranslationsPath)) / "qtbase_ru.qm"), "PySide6/Qt/translations"),
    ],
    hiddenimports=["PySide6.QtCore", "PySide6.QtGui", "PySide6.QtWidgets", "PySide6.QtNetwork"],
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=["PySide6.Qt" + module for module in qt_modules]
             + ["tkinter", "matplotlib", "numpy", "pandas", "IPython"],
    noarchive=False,
    optimize=1,
)

# QtGui's hook otherwise collects unused Wayland, EGLFS and VNC backends.
# This first Linux release supports X11, including XWayland sessions. Keep the
# offscreen platform for automated rendering checks and file/icon image codecs.
allowed_plugins = {
    "platforms/libqxcb.so", "platforms/libqoffscreen.so",
    "iconengines/libqsvgicon.so", "imageformats/libqsvg.so",
    "imageformats/libqgif.so", "imageformats/libqico.so",
    "imageformats/libqjpeg.so", "imageformats/libqwebp.so",
}


def wanted(entry):
    name = entry[0].replace("\\", "/")
    marker = "PySide6/Qt/plugins/"
    return marker not in name or name.split(marker, 1)[1] in allowed_plugins


app.binaries = [entry for entry in app.binaries if wanted(entry)]
app.datas = [entry for entry in app.datas if wanted(entry)]
# Keep libraries supplied by Python/Qt wheels, while leaving the host's core
# /lib and /usr/lib libraries to the documented distro package dependencies.
app.exclude_system_libraries(list_of_exceptions=[
    "*libpython*", "*libQt6*", "*libpyside6*", "*libshiboken6*",
    # Ubuntu's libbz2.so.1.0 SONAME differs from Fedora's libbz2.so.1.
    # CPython's _bz2 extension must travel with the library it was linked to.
    "*libbz2*",
])
pyz = PYZ(app.pure)
exe = EXE(
    pyz, app.scripts, [], exclude_binaries=True, name="yarnelo", debug=False,
    bootloader_ignore_signals=False, strip=False, upx=False, console=False,
    disable_windowed_traceback=False,
)
bundle = COLLECT(exe, app.binaries, app.datas, strip=False, upx=False, name="yarnelo")
