#!/usr/bin/env python3
"""Yarnelo: a native Qt desktop launcher for Linux. See linux/README.md."""

from __future__ import annotations

import argparse
import html
import os
from pathlib import Path
import shutil
import sys

from yarnelo_i18n import LANGUAGES, error_text, resolve_language, text
from yarnelo_autostart import AutostartManager, resolve_startup_command
from yarnelo_desktop_watch import DesktopShortcutTracker

from yarnelo_model import (
    VERSION, PLACEMENTS, Item, ModelError, Settings, config_path, desktop_metadata, dock_layout,
    load_settings, move_item, preserve_invalid_settings, save_settings,
)


def arguments() -> argparse.Namespace:
    # Read only the optional language flag before formatting help; this never
    # touches the user's profile (including in smoke-test mode).
    preliminary = argparse.ArgumentParser(add_help=False)
    preliminary.add_argument("--language", choices=LANGUAGES, default="auto")
    requested, _ = preliminary.parse_known_args()
    parser = argparse.ArgumentParser(description=text("app_description", requested.language))
    parser.add_argument("--version", action="version", version=f"Yarnelo {VERSION}", help=text("help_version", requested.language))
    parser.add_argument("--language", choices=LANGUAGES, default=None, help=text("help_language", requested.language))
    parser.add_argument("--startup", action="store_true", help=text("help_startup", requested.language))
    parser.add_argument("--smoke-test", action="store_true", help=text("help_smoke", requested.language))
    parser.add_argument("--smoke-output", type=Path, help=text("help_smoke_output", requested.language))
    return parser.parse_args()


def external_environment(environment: dict[str, str], bundle_root: Path) -> dict[str, str]:
    """Do not pass PyInstaller's private Qt/ELF loader paths to real applications."""
    cleaned = dict(environment)
    original_library_path = cleaned.get("LD_LIBRARY_PATH_ORIG")
    if original_library_path:
        cleaned["LD_LIBRARY_PATH"] = original_library_path
    else:
        cleaned.pop("LD_LIBRARY_PATH", None)
    bundle_root = bundle_root.resolve()
    for name in ("QT_PLUGIN_PATH", "QT_QPA_PLATFORM_PLUGIN_PATH", "QML2_IMPORT_PATH"):
        if name not in cleaned:
            continue
        remaining = []
        for value in cleaned[name].split(os.pathsep):
            if value and not Path(value).resolve().is_relative_to(bundle_root):
                remaining.append(value)
        if remaining:
            cleaned[name] = os.pathsep.join(remaining)
        else:
            cleaned.pop(name, None)
    return cleaned


def main() -> int:
    args = arguments()
    if sys.version_info < (3, 12):
        # Reject incompatible interpreters before importing any Qt extension.
        raise SystemExit(text("python_version_required", args.language or "auto"))
    if args.smoke_output and not args.smoke_test:
        raise SystemExit(text("smoke_requires", args.language or "auto"))
    if args.smoke_test:
        os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")

    from PySide6.QtCore import (
        QCoreApplication, QEvent, QEasingCurve, QFileInfo, QLibraryInfo, QLocale, QLockFile, QPoint, QPointF, QProcess, QProcessEnvironment, Property,
        QRect, QRectF, QSize, QStandardPaths, Qt, QTimer, QPropertyAnimation, QTranslator,
    )
    from PySide6.QtGui import (
        QActionGroup, QColor, QFont, QFontDatabase, QHelpEvent, QIcon, QImage, QLinearGradient, QPainter, QPen, QPixmap, QTextLayout, QTextOption,
    )
    from PySide6.QtNetwork import QLocalServer, QLocalSocket
    from shiboken6 import isValid
    from PySide6.QtWidgets import (
        QApplication, QCheckBox, QComboBox, QDialog, QDialogButtonBox, QFileDialog,
        QFileIconProvider, QHBoxLayout, QLabel, QMenu, QMessageBox,
        QSlider, QSpinBox, QDoubleSpinBox, QSystemTrayIcon, QToolTip, QVBoxLayout, QWidget,
    )

    app = QApplication(sys.argv[:1])
    app.setApplicationName("Yarnelo")
    app.setApplicationVersion(VERSION)
    app.setOrganizationName("Yarnelo")
    app.setQuitOnLastWindowClosed(False)
    app.setStyle("Fusion")
    app.setEffectEnabled(Qt.UIEffect.UI_FadeTooltip, True)
    if args.smoke_test:
        # CPython before 3.12 has a mortal None singleton. A broken abi3 Qt
        # wheel can silently decrement it on wrapped void calls, then crash at
        # interpreter shutdown. Check actual calls before the larger UI test.
        probe = QLabel("reference probe")
        probe.setText("warmup")
        app.processEvents()
        before = sys.getrefcount(None)
        for _ in range(100):
            probe.setText("probe")
        text_delta = sys.getrefcount(None) - before
        before = sys.getrefcount(None)
        for _ in range(100):
            app.processEvents()
        events_delta = sys.getrefcount(None) - before
        print(f"Qt reference probe: setText={text_delta:+d}, processEvents={events_delta:+d}; None={sys.getrefcount(None)}", flush=True)
        if text_delta < -10 or events_delta < -10:
            raise RuntimeError("The installed Qt binding loses None references on wrapped calls; use linux/requirements.txt")
        probe.deleteLater()
        QCoreApplication.sendPostedEvents(None, QEvent.Type.DeferredDelete)
    if args.smoke_test and os.name == "nt":
        # The offscreen plugin on Windows has no native font discovery. This is
        # only for local visual QA; Linux uses its normal installed font database.
        system_font = Path(os.environ.get("WINDIR", "C:/Windows")) / "Fonts" / "segoeui.ttf"
        if system_font.is_file() and QFontDatabase.addApplicationFont(str(system_font)) >= 0:
            app.setFont(QFont("Segoe UI", 9))
    asset_root = Path(getattr(sys, "_MEIPASS", Path(__file__).resolve().parent.parent)) / "assets"
    app_icon = QIcon(str(asset_root / "Yarnelo-Icon-256.png"))
    app.setWindowIcon(app_icon)
    app.setStyleSheet("""
        QMenu, QDialog { background: #1c2334; color: #f2f3fc; }
        QMenu { border: 1px solid #566078; border-radius: 9px; padding: 6px; }
        QMenu::item { padding: 7px 22px; border-radius: 5px; }
        QMenu::item:selected { background: #46547b; }
        QMenu::separator { height: 1px; background: #455068; margin: 5px 9px; }
        QSpinBox, QDoubleSpinBox, QComboBox { color: #f2f3fc; background: #303b52; padding: 4px; }
        QComboBox QAbstractItemView { color: #f2f3fc; background: #303b52; selection-background-color: #46547b; }
        QLabel, QCheckBox { color: #f2f3fc; }
        QPushButton { padding: 6px 14px; }
        QToolTip { background: #222b40; color: white; border: 1px solid #6b7891; padding: 5px; }
    """)

    store = None if args.smoke_test else config_path()
    load_error = None
    settings = Settings(language=args.language or "auto")
    if store is not None:
        try:
            settings = load_settings(store)
        except ModelError as error:
            load_error = error
        if args.language:
            settings.language = args.language

    def set_qt_language(language):
        previous = getattr(app, "qt_translator", None)
        if previous:
            app.removeTranslator(previous)
        app.qt_translator = QTranslator(app)
        selected = resolve_language(language)
        if selected == "ru" and app.qt_translator.load("qtbase_ru", QLibraryInfo.path(QLibraryInfo.LibraryPath.TranslationsPath)):
            app.installTranslator(app.qt_translator)
        if previous:
            previous.deleteLater()
        QLocale.setDefault(QLocale(selected))

    set_qt_language(settings.language)

    def wrapped_lines(value, font, width, limit=None):
        """Use Qt's Unicode/word wrapping for captions and full-name bubbles."""
        layout = QTextLayout(value, font)
        option = QTextOption()
        option.setWrapMode(QTextOption.WrapMode.WrapAtWordBoundaryOrAnywhere)
        layout.setTextOption(option)
        lines, starts = [], []
        # QTextLine offsets count UTF-16 units; Python indices count Unicode
        # code points. Keep emoji/non-BMP names intact across wrapped lines.
        encoded = value.encode("utf-16-le", "surrogatepass")
        layout.beginLayout()
        while True:
            line = layout.createLine()
            if not line.isValid():
                break
            line.setLineWidth(max(1, width))
            starts.append(line.textStart())
            lines.append(encoded[line.textStart() * 2:(line.textStart() + line.textLength()) * 2].decode("utf-16-le", "surrogatepass").strip())
            if limit and len(lines) > limit:
                break
        layout.endLayout()
        if limit and len(lines) > limit:
            from PySide6.QtGui import QFontMetrics
            remainder = encoded[starts[limit - 1] * 2:].decode("utf-16-le", "surrogatepass")
            lines[limit - 1] = QFontMetrics(font).elidedText(remainder, Qt.TextElideMode.ElideRight, max(1, width))
            lines = lines[:limit]
        return lines

    class Tile(QWidget):
        def __init__(self, dock: "Dock", item: Item | None):
            super().__init__(dock)
            self.dock, self.item = dock, item
            self.progress = 0.0
            self.pressed_progress = 0.0
            self.press_origin: QPoint | None = None
            self.dragging = False
            self.pixmap = QPixmap()
            self.label = dock.t("add_shortcuts")
            self.caption_cache = (None, [])
            self.setMouseTracking(True)
            self.setCursor(Qt.CursorShape.PointingHandCursor)
            self.hover_animation = QPropertyAnimation(self, b"hover", self)
            self.hover_animation.setDuration(190)
            self.hover_animation.setEasingCurve(QEasingCurve.Type.OutCubic)
            self.press_animation = QPropertyAnimation(self, b"press", self)
            self.press_animation.setDuration(150)
            self.press_animation.setEasingCurve(QEasingCurve.Type.OutCubic)
            self.position_animation = QPropertyAnimation(self, b"pos", self)
            self.position_animation.setDuration(210)
            self.position_animation.setEasingCurve(QEasingCurve.Type.OutCubic)
            self.refresh_icon()

        def get_hover(self):
            return self.progress

        def set_hover(self, value):
            self.progress = value
            self.update()

        hover = Property(float, get_hover, set_hover)

        def get_press(self):
            return self.pressed_progress

        def set_press(self, value):
            self.pressed_progress = value
            self.update()

        press = Property(float, get_press, set_press)

        def animate(self, animation, value):
            animation.stop()
            animation.setStartValue(self.property(animation.propertyName().data().decode()))
            animation.setEndValue(value)
            animation.start()

        def refresh_icon(self):
            icon = QIcon()
            item = self.item
            self.label = self.dock.t("add_shortcuts")
            if item:
                if item.kind == "trash":
                    self.label = self.dock.t("trash")
                    icon = QIcon.fromTheme("user-trash", self.dock.icon_provider.icon(QFileIconProvider.IconType.Trashcan))
                elif item.kind == "computer":
                    self.label = self.dock.t("computer")
                    icon = QIcon.fromTheme("computer", self.dock.icon_provider.icon(QFileIconProvider.IconType.Computer))
                else:
                    path = Path(item.path)
                    self.label = path.name or item.path
                    if path.suffix.lower() == ".desktop" and path.is_file():
                        try:
                            self.label, name = desktop_metadata(path, resolve_language(self.dock.settings.language))
                            if Path(name).is_absolute():
                                icon = QIcon(name)
                            elif name:
                                icon = QIcon.fromTheme(name)
                        except ModelError:
                            pass
                    if icon.isNull():
                        icon = self.dock.icon_provider.icon(QFileInfo(str(path)))
                if icon.isNull():
                    icon = QIcon.fromTheme("application-x-executable", app_icon)
                self.pixmap = icon.pixmap(QSize(192, 192))
                if not self.pixmap.isNull():
                    image = self.pixmap.toImage().convertToFormat(QImage.Format.Format_ARGB32)
                    if image.hasAlphaChannel():
                        left, top, right, bottom = image.width(), image.height(), -1, -1
                        for y in range(image.height()):
                            for x in range(image.width()):
                                if image.pixel(x, y) >> 24 > 16:
                                    left, top = min(left, x), min(top, y)
                                    right, bottom = max(right, x), max(bottom, y)
                        if right >= left and bottom >= top:
                            # Normalize visible bounds so oversized transparent
                            # margins in a launcher icon cannot make it tiny.
                            self.pixmap = self.pixmap.copy(left, top, right - left + 1, bottom - top + 1)
            self.setToolTip(self.label)
            self.update()

        def enterEvent(self, event):
            self.animate(self.hover_animation, 1.0)
            super().enterEvent(event)

        def leaveEvent(self, event):
            self.animate(self.hover_animation, 0.0)
            super().leaveEvent(event)

        def event(self, event):
            if event.type() == QEvent.Type.ToolTip:
                if self.dragging or self.press_origin or self.dock.visible_menus or self.dock.settings_dialog or not self.dock.isVisible():
                    QToolTip.hideText()
                    return True
                bounds = self.dock.current_screen().availableGeometry()
                width = max(40, min(300, bounds.width() - 28))
                lines = wrapped_lines(self.label, QToolTip.font(), width)
                bubble = "<p>" + "<br>".join(html.escape(line) for line in lines) + "</p>"
                # QToolTip is a passive native popup: no focus or activation.
                # Position above the tile where possible; Qt keeps it onscreen.
                from PySide6.QtGui import QFontMetrics
                height = len(lines) * QFontMetrics(QToolTip.font()).lineSpacing() + 16
                point = self.mapToGlobal(QPoint(self.width() // 2 - width // 2, -height - 10))
                point.setX(max(bounds.left() + 4, min(point.x(), bounds.right() - width - 16)))
                point.setY(max(bounds.top() + 4, point.y()))
                QToolTip.showText(point, bubble, self, self.rect())
                return True
            return super().event(event)

        def paintEvent(self, event):
            painter = QPainter(self)
            painter.setRenderHint(QPainter.RenderHint.Antialiasing)
            painter.setRenderHint(QPainter.RenderHint.SmoothPixmapTransform)
            base = self.dock.effective_icon_size
            scale = (1 + (self.dock.settings.zoom - 1) * self.progress) * (1 - 0.13 * self.pressed_progress)
            size = base * scale
            label_height = 34 if self.dock.settings.labels else 0
            center = QPointF(self.width() / 2, (self.height() - label_height) / 2 - 2 * self.progress)
            target = QRectF(center.x() - size / 2, center.y() - size / 2, size, size)
            if self.item:
                if not self.pixmap.isNull():
                    painter.setPen(Qt.PenStyle.NoPen)
                    painter.setBrush(QColor(0, 0, 0, int(28 + 20 * self.progress)))
                    painter.drawEllipse(QRectF(center.x() - size * 0.4, center.y() + size * 0.35, size * 0.8, size * 0.12))
                    aspect = self.pixmap.width() / self.pixmap.height()
                    image_width, image_height = (size, size / aspect) if aspect >= 1 else (size * aspect, size)
                    image_target = QRectF(center.x() - image_width / 2, center.y() - image_height / 2, image_width, image_height)
                    painter.drawPixmap(image_target, self.pixmap, QRectF(self.pixmap.rect()))
            else:
                painter.setPen(QPen(QColor(224, 230, 250, int(175 + 65 * self.progress)), max(1.6, base / 20), Qt.PenStyle.SolidLine, Qt.PenCapStyle.RoundCap))
                arm = max(4.0, base * 0.17 * scale)
                painter.drawLine(QPointF(center.x() - arm, center.y()), QPointF(center.x() + arm, center.y()))
                painter.drawLine(QPointF(center.x(), center.y() - arm), QPointF(center.x(), center.y() + arm))
            if label_height:
                painter.setPen(QColor(242, 244, 255))
                painter.setFont(QFont("", 8))
                cache_key = (self.label, painter.font().toString(), self.width())
                if cache_key != self.caption_cache[0]:
                    self.caption_cache = (cache_key, wrapped_lines(self.label, painter.font(), self.width() - 8, 2))
                lines = self.caption_cache[1]
                line_height = min(16, painter.fontMetrics().lineSpacing())
                for index, caption in enumerate(lines):
                    painter.drawText(QRect(4, self.height() - 33 + index * line_height, self.width() - 8, line_height), Qt.AlignmentFlag.AlignCenter, caption)

        def mousePressEvent(self, event):
            QToolTip.hideText()
            if event.button() == Qt.MouseButton.LeftButton:
                self.press_origin = event.globalPosition().toPoint()
                self.animate(self.press_animation, 1.0)
                event.accept()
            else:
                super().mousePressEvent(event)

        def mouseMoveEvent(self, event):
            if self.item and self.press_origin and event.buttons() & Qt.MouseButton.LeftButton:
                if not self.dragging and (event.globalPosition().toPoint() - self.press_origin).manhattanLength() >= QApplication.startDragDistance():
                    self.dragging = True
                    self.dock.begin_drag(self)
                if self.dragging:
                    self.dock.update_drag(self, event.globalPosition().toPoint())
                event.accept()
            else:
                super().mouseMoveEvent(event)

        def mouseReleaseEvent(self, event):
            if event.button() == Qt.MouseButton.LeftButton and self.press_origin:
                self.press_origin = None
                self.animate(self.press_animation, 0.0)
                if self.dragging:
                    self.dragging = False
                    self.dock.end_drag(self, event.globalPosition().toPoint())
                elif self.rect().contains(event.position().toPoint()):
                    if self.item:
                        self.dock.launch(self.item)
                    else:
                        self.dock.show_menu(self.mapToGlobal(self.rect().center()))
                event.accept()
            else:
                super().mouseReleaseEvent(event)

        def contextMenuEvent(self, event):
            self.dock.show_menu(event.globalPos(), self.item)
            event.accept()

    class SettingsDialog(QDialog):
        def __init__(self, dock):
            super().__init__(dock)
            self.dock = dock
            self.base_font = QFont(self.font())
            layout = QVBoxLayout(self)
            layout.setContentsMargins(24, 22, 24, 20)
            layout.setSpacing(16)
            columns = QHBoxLayout()
            columns.setSpacing(28)
            self.headings, self.field_labels, self.fields = {}, {}, []
            self.columns = []
            for key in ("appearance", "layout"):
                column = QVBoxLayout()
                column.setSpacing(12)
                heading = QLabel()
                heading.setStyleSheet("font-size: 16px; font-weight: 600;")
                self.headings[key] = heading
                column.addWidget(heading)
                self.columns.append(column)
                columns.addLayout(column, 1)
            layout.addLayout(columns)
            self.rows = QSpinBox()
            self.rows.setRange(1, 4)
            self.rows.setValue(dock.settings.rows)
            self.opacity = QSlider(Qt.Orientation.Horizontal)
            self.opacity.setRange(12, 100)
            self.opacity.setValue(round(dock.settings.opacity * 100))
            self.icon_size = QSpinBox()
            self.icon_size.setRange(24, 96)
            self.icon_size.setSuffix(" px")
            self.icon_size.setValue(dock.settings.icon_size)
            self.zoom = QDoubleSpinBox()
            self.zoom.setRange(1, 1.8)
            self.zoom.setSingleStep(0.05)
            self.zoom.setDecimals(2)
            self.zoom.setValue(dock.settings.zoom)
            self.corners = QSlider(Qt.Orientation.Horizontal)
            self.corners.setRange(0, 100)
            self.corners.setValue(round(dock.settings.corner_radius))
            self.placement = QComboBox()
            for value in PLACEMENTS:
                self.placement.addItem("", value)
            self.placement.setCurrentIndex(PLACEMENTS.index(dock.settings.placement))
            self.labels = QComboBox()
            self.labels.addItem("", False)
            self.labels.addItem("", True)
            self.labels.setCurrentIndex(int(dock.settings.labels))
            self.startup = QCheckBox()
            self.startup_error = dock.startup_error
            self.startup_initial = False
            if dock.autostart is not None:
                try:
                    self.startup_initial = dock.autostart.enabled
                except (OSError, ModelError) as error:
                    self.startup_error = error
            self.startup.setChecked(self.startup_initial)
            self.startup.setEnabled(dock.autostart is not None and self.startup_error is None)
            self.auto_add_desktop = QCheckBox()
            self.auto_add_desktop.setChecked(dock.settings.auto_add_desktop_shortcuts)

            def add_field(column, key, widget, value_format=None):
                group = QVBoxLayout()
                group.setSpacing(6)
                self.fields.append(group)
                heading = QHBoxLayout()
                label = QLabel()
                label.setWordWrap(True)
                self.field_labels[key] = label
                heading.addWidget(label, 1)
                if value_format:
                    value = QLabel(value_format(widget.value()))
                    value.setStyleSheet("color: #b8c6e2;")
                    widget.valueChanged.connect(lambda number: value.setText(value_format(number)))
                    heading.addWidget(value)
                group.addLayout(heading)
                group.addWidget(widget)
                self.columns[column].addLayout(group)

            add_field(0, "background_opacity", self.opacity, lambda value: f"{value}%")
            add_field(0, "corner_radius", self.corners, str)
            self.corner_note = QLabel()
            self.corner_note.setWordWrap(True)
            self.corner_note.setStyleSheet("color: #b8c6e2;")
            self.columns[0].addWidget(self.corner_note)
            add_field(0, "icon_size", self.icon_size)
            add_field(0, "hover_zoom", self.zoom)
            add_field(1, "position", self.placement)
            add_field(1, "row_count", self.rows)
            self.rows_note = QLabel()
            self.rows_note.setWordWrap(True)
            self.rows_note.setStyleSheet("color: #b8c6e2;")
            self.columns[1].addWidget(self.rows_note)
            add_field(1, "shortcut_names", self.labels)
            self.columns[1].addWidget(self.startup)
            self.columns[1].addWidget(self.auto_add_desktop)
            for column in self.columns:
                column.addStretch(1)
            self.note = QLabel()
            self.note.setWordWrap(True)
            self.note.setStyleSheet("color: #b8c6e2;")
            layout.addWidget(self.note)
            self.buttons = QDialogButtonBox(QDialogButtonBox.StandardButton.Ok | QDialogButtonBox.StandardButton.Cancel)
            self.buttons.accepted.connect(self.accept)
            self.buttons.rejected.connect(self.reject)
            layout.addWidget(self.buttons)
            self.retranslate()

        def accept(self):
            # Applying before accepting keeps a failed change visible and lets
            # the user retry or cancel without losing the previous settings.
            if self.dock.apply_settings_dialog(self):
                super().accept()

        def fit_available_screen(self):
            screen = self.dock.current_screen()
            if not screen:
                return
            bounds = screen.availableGeometry().adjusted(14, 20, -14, -20)
            # Qt already reports logical coordinates at the current DPI. Keep
            # both columns visible, reducing spacing/font only on small areas.
            small = bounds.width() < 720 or bounds.height() < 500
            compact = bounds.height() < 380
            self.setStyleSheet("QLabel, QCheckBox, QSpinBox, QDoubleSpinBox, QComboBox, QPushButton { font-size: 10px; } QSpinBox, QDoubleSpinBox, QComboBox { padding: 2px; } QPushButton { padding: 3px 10px; }" if compact else "")
            font = QFont(self.base_font)
            if small:
                font.setPointSizeF(7.0 if compact else max(7.0, min(9.0, font.pointSizeF())))
            self.setFont(font)
            self.layout().setContentsMargins(14 if small else 24, 6 if compact else 12 if small else 22,
                                            14 if small else 24, 6 if compact else 12 if small else 20)
            self.layout().setSpacing(3 if compact else 8 if small else 16)
            for column in self.columns:
                column.setSpacing(2 if compact else 7 if small else 12)
            for group in self.fields:
                group.setSpacing(2 if compact else 6)
            for heading in self.headings.values():
                heading.setStyleSheet("font-size: 14px; font-weight: 600;" if compact else "font-size: 16px; font-weight: 600;")
            self.ensurePolished()
            self.layout().activate()
            hint = self.sizeHint()
            width = min(bounds.width(), max(min(760, bounds.width()), hint.width()))
            height = min(bounds.height(), hint.height())
            self.resize(width, height)
            self.move(bounds.center() - self.rect().center())

        def retranslate(self):
            self.setWindowTitle(self.dock.t("settings_title"))
            self.setLocale(QLocale(resolve_language(self.dock.settings.language)))
            self.zoom.setLocale(self.locale())
            self.labels.setItemText(0, self.dock.t("names_hover"))
            self.labels.setItemText(1, self.dock.t("names_always"))
            self.startup.setText(self.dock.t("start_at_login"))
            self.startup.setToolTip(self.dock.describe_error(self.startup_error) if self.startup_error else self.dock.t("startup_help"))
            self.auto_add_desktop.setText(self.dock.t("auto_add_desktop"))
            self.auto_add_desktop.setToolTip(self.dock.t("auto_add_desktop_help"))
            for key, label in self.headings.items():
                label.setText(self.dock.t(key))
            for key, label in self.field_labels.items():
                label.setText(self.dock.t(key))
            for index, value in enumerate(PLACEMENTS):
                self.placement.setItemText(index, self.dock.t(f"position_{value}"))
            self.corner_note.setText(self.dock.t("corner_help"))
            self.rows_note.setText(self.dock.t("rows_help"))
            self.note.setText(self.dock.t("platform_note"))
            self.buttons.button(QDialogButtonBox.StandardButton.Ok).setText(self.dock.t("ok"))
            self.buttons.button(QDialogButtonBox.StandardButton.Cancel).setText(self.dock.t("cancel"))
            self.fit_available_screen()

    class Dock(QWidget):
        def __init__(self, settings: Settings, store: Path | None, invalid: bool = False, autostart=None, startup_error=None, desktop_directory=None):
            super().__init__()
            self.settings, self.store, self.invalid_settings = settings, store, invalid
            self.autostart, self.startup_error = autostart, startup_error
            # Tests supply their own directory and poll manually. A smoke test
            # must never inspect the real Desktop or start a real monitor.
            if desktop_directory is None and not args.smoke_test:
                desktop_directory = QStandardPaths.writableLocation(QStandardPaths.StandardLocation.DesktopLocation)
            self.desktop_directory = Path(desktop_directory) if desktop_directory else None
            self.desktop_tracker = None
            self.desktop_notices = set()
            self.desktop_timer = QTimer(self)
            self.desktop_timer.setInterval(1000)
            self.desktop_timer.timeout.connect(self.poll_desktop_shortcuts)
            self.settings_dialog = None
            self.visible_menus = []
            self.icon_provider = QFileIconProvider()
            self.tiles: dict[str, Tile] = {}
            self.positions: dict[str, QPoint] = {}
            self.processes: set[QProcess] = set()
            self.effective_icon_size = float(settings.icon_size)
            self.drag_order: list[Item] | None = None
            self.panel_drag_offset: QPoint | None = None
            self.panel_drag_moved = False
            self.layout_key = None
            self.shutting_down = False
            self.layout_pending = False
            self.screen_hooked = None
            self.setWindowTitle(self.t("dock_title"))
            self.setObjectName("YarneloDock")
            platform = app.platformName().lower()
            flags = Qt.WindowType.FramelessWindowHint
            if platform == "xcb":
                flags |= Qt.WindowType.Tool | Qt.WindowType.WindowStaysOnBottomHint
            else:
                flags |= Qt.WindowType.Window
            self.setWindowFlags(flags)
            self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground)
            self.setAttribute(Qt.WidgetAttribute.WA_ShowWithoutActivating)
            self.setAcceptDrops(True)
            self.setMouseTracking(True)
            self.add_tile = Tile(self, None)
            self.rebuild()
            self.tray = None
            if not args.smoke_test and QSystemTrayIcon.isSystemTrayAvailable():
                self.tray = QSystemTrayIcon(app_icon, self)
                self.tray.setToolTip(f"Yarnelo {VERSION}")
                menu = QMenu(self)
                self.add_action(menu, "show_dock", self.show_passive)
                self.add_action(menu, "settings", self.open_settings)
                self.add_language_menu(menu)
                menu.addSeparator()
                self.add_action(menu, "quit", app.quit)
                self.tray.setContextMenu(menu)
                self.tray.activated.connect(lambda reason: self.show_passive() if reason == QSystemTrayIcon.ActivationReason.Trigger else None)
                self.tray.show()
            self.apply_language()
            # A bound QObject method is disconnected with its receiver. Avoid
            # application-owned lambdas retaining the dock past Qt shutdown.
            app.screenAdded.connect(self.relayout)
            app.screenRemoved.connect(self.relayout)
            app.aboutToQuit.connect(self.shutdown)
            self.configure_desktop_tracking()

        def shutdown(self):
            QToolTip.hideText()
            self.stop_desktop_tracking()
            if self.shutting_down:
                return
            self.shutting_down = True
            for signal in (app.screenAdded, app.screenRemoved):
                try:
                    signal.disconnect(self.relayout)
                except (RuntimeError, TypeError):
                    pass
            if self.screen_hooked:
                try:
                    self.screen_hooked.availableGeometryChanged.disconnect(self.relayout)
                except (RuntimeError, TypeError):
                    pass
                self.screen_hooked = None
            for tile in [*self.tiles.values(), self.add_tile]:
                tile.hover_animation.stop()
                tile.press_animation.stop()
                tile.position_animation.stop()
            for menu in self.visible_menus:
                menu.close()
            if self.settings_dialog:
                self.settings_dialog.close()
            if self.tray:
                self.tray.hide()
            # Qt destroys children and their signal connections while the
            # QApplication and Python runtime are both still alive.
            self.close()
            self.deleteLater()

        def t(self, key, **values):
            return text(key, self.settings.language, **values)

        def describe_error(self, error):
            return error_text(error, self.settings.language)

        def add_action(self, menu, key, callback):
            action = menu.addAction(self.t(key), callback)
            action.setProperty("translation_key", key)
            return action

        def add_language_menu(self, menu):
            languages = menu.addMenu(self.t("language"))
            languages.menuAction().setProperty("translation_key", "language")
            group = QActionGroup(languages)
            group.setExclusive(True)
            for language in LANGUAGES:
                action = self.add_action(languages, f"language_{language}", lambda checked=False, value=language: self.change_language(value))
                action.setCheckable(True)
                action.setChecked(self.settings.language == language)
                action.setProperty("language_value", language)
                group.addAction(action)

        def retranslate_menu(self, menu):
            for action in menu.actions():
                key = action.property("translation_key")
                if key:
                    action.setText(self.t(key))
                language = action.property("language_value")
                if language:
                    action.setChecked(self.settings.language == language)
                placement = action.property("placement_value")
                if placement:
                    action.setChecked(self.settings.placement == placement)
                if action.menu():
                    self.retranslate_menu(action.menu())

        def apply_language(self):
            selected = resolve_language(self.settings.language)
            set_qt_language(self.settings.language)
            self.setLocale(QLocale(selected))
            self.setWindowTitle(self.t("dock_title"))
            self.add_tile.refresh_icon()
            for tile in self.tiles.values():
                tile.refresh_icon()
            for menu in self.visible_menus:
                self.retranslate_menu(menu)
            if self.tray:
                self.retranslate_menu(self.tray.contextMenu())
            if self.settings_dialog:
                self.settings_dialog.retranslate()

        def change_language(self, language):
            if language not in LANGUAGES or self.settings.language == language:
                return
            previous = self.settings.language
            self.settings.language = language
            if not self.persist():
                self.settings.language = previous
                self.apply_language()
                return
            self.apply_language()

        def current_screen(self):
            return next((screen for screen in app.screens() if screen.name() == self.settings.screen), app.primaryScreen())

        def rebuild(self):
            present = {item.id for item in self.settings.items}
            for ident in set(self.tiles) - present:
                tile = self.tiles.pop(ident)
                tile.hide()
                tile.deleteLater()
            for item in self.settings.items:
                if item.id not in self.tiles:
                    self.tiles[item.id] = Tile(self, item)
                self.tiles[item.id].show()
            self.add_tile.show()
            self.relayout()

        def relayout(self, animate=False):
            if self.shutting_down:
                return
            animate = animate is True
            screen = self.current_screen()
            if not screen:
                return
            if screen is not self.screen_hooked:
                if self.screen_hooked:
                    try:
                        self.screen_hooked.availableGeometryChanged.disconnect(self.relayout)
                    except (RuntimeError, TypeError):
                        pass
                screen.availableGeometryChanged.connect(self.relayout)
                self.screen_hooked = screen
            bounds = screen.availableGeometry()
            key = (len(self.settings.items), self.settings.rows, self.settings.icon_size, self.settings.zoom, self.settings.labels,
                   self.settings.placement in ("left", "right"), bounds.width(), bounds.height())
            if key != self.layout_key:
                self.layout_geometry = dock_layout(self.settings, bounds.width(), bounds.height())
                self.layout_key = key
            geometry = self.layout_geometry
            self.effective_icon_size = geometry.icon_size
            width, height = geometry.width, geometry.height
            self.setFixedSize(width, height)
            center_x, center_y = bounds.x() + (bounds.width() - width) // 2, bounds.y() + (bounds.height() - height) // 2
            placement = self.settings.placement
            if placement == "top":
                target_x, target_y = center_x, bounds.top() + 10
            elif placement == "left":
                target_x, target_y = bounds.left() + 10, center_y
            elif placement == "right":
                target_x, target_y = bounds.right() - width - 9, center_y
            elif placement == "bottom":
                target_x, target_y = center_x, bounds.bottom() - height - 9
            else:
                target_x = self.settings.x if self.settings.x is not None else center_x
                target_y = self.settings.y if self.settings.y is not None else bounds.bottom() - height - 20
            self.move(min(max(target_x, bounds.left()), max(bounds.left(), bounds.right() - width + 1)), min(max(target_y, bounds.top()), max(bounds.top(), bounds.bottom() - height + 1)))
            ordered = [self.tiles[item.id] for item in self.settings.items] + [self.add_tile]
            self.positions.clear()
            for tile, coordinates in zip(ordered, geometry.positions):
                point = QPoint(*coordinates)
                key = tile.item.id if tile.item else "+"
                self.positions[key] = point
                tile.setFixedSize(geometry.cell_width, geometry.cell_height)
                if tile.dragging:
                    continue
                tile.position_animation.stop()
                if animate and tile.isVisible() and tile.pos() != point:
                    tile.position_animation.setStartValue(tile.pos())
                    tile.position_animation.setEndValue(point)
                    tile.position_animation.start()
                else:
                    tile.move(point)
                tile.update()
            self.update()

        def paintEvent(self, event):
            painter = QPainter(self)
            painter.setRenderHint(QPainter.RenderHint.Antialiasing)
            rect = QRectF(self.rect()).adjusted(1, 1, -1, -1)
            background = QLinearGradient(rect.topLeft(), rect.bottomRight())
            alpha = round(255 * self.settings.opacity)
            background.setColorAt(0, QColor(42, 49, 70, alpha))
            background.setColorAt(1, QColor(17, 25, 43, alpha))
            painter.setBrush(background)
            painter.setPen(QPen(QColor(222, 232, 255, max(35, alpha // 2)), 1))
            radius = min(rect.width(), rect.height()) / 2 if self.settings.corner_radius >= 100 else min(self.settings.corner_radius, min(rect.width(), rect.height()) / 2)
            painter.drawRoundedRect(rect, radius, radius)
            painter.setPen(Qt.PenStyle.NoPen)
            painter.setBrush(QColor(221, 229, 255, 95))
            if self.layout_geometry.vertical:
                for x in (-7, 0, 7):
                    for y in (11, 17):
                        painter.drawEllipse(QPointF(self.width() / 2 + x, y), 1.2, 1.2)
            else:
                for y in (-7, 0, 7):
                    for x in (11, 17):
                        painter.drawEllipse(QPointF(x, self.height() / 2 + y), 1.2, 1.2)
            if self.drag_order is not None:
                dragged = next((tile for tile in self.tiles.values() if tile.dragging), None)
                if dragged:
                    placeholder = QRectF(dragged.rect()).translated(self.positions[dragged.item.id]).adjusted(6, 6, -6, -6)
                    painter.setBrush(QColor(143, 163, 240, 22))
                    painter.setPen(QPen(QColor(180, 198, 250, 110), 1, Qt.PenStyle.DashLine))
                    painter.drawRoundedRect(placeholder, 12, 12)

        def begin_drag(self, tile):
            QToolTip.hideText()
            self.drag_order = list(self.settings.items)
            tile.position_animation.stop()
            tile.raise_()
            self.update()

        def update_drag(self, tile, global_position):
            position = self.mapFromGlobal(global_position)
            tile.move(position - tile.rect().center())
            if self.rect().adjusted(-20, -20, 20, 20).contains(position):
                candidates = [(index, self.positions[item.id] + self.tiles[item.id].rect().center()) for index, item in enumerate(self.settings.items)]
                nearest = min(candidates, key=lambda candidate: (candidate[1] - position).manhattanLength())[0]
                old = next(index for index, item in enumerate(self.settings.items) if item.id == tile.item.id)
                if nearest != old:
                    move_item(self.settings.items, tile.item.id, nearest)
                    self.relayout(True)
            self.update()

        def end_drag(self, tile, global_position):
            if not self.rect().adjusted(-20, -20, 20, 20).contains(self.mapFromGlobal(global_position)) and self.drag_order is not None:
                self.settings.items = self.drag_order
            self.drag_order = None
            self.relayout(True)
            self.persist()

        def persist(self, notify=True):
            if self.store is None:
                return True
            try:
                if self.invalid_settings and self.store.exists():
                    preserve_invalid_settings(self.store)
                    self.invalid_settings = False
                save_settings(self.store, self.settings)
                return True
            except (OSError, ModelError) as error:
                if notify:
                    QMessageBox.warning(self, "Yarnelo", self.t("save_failed", error=self.describe_error(error)))
                else:
                    self.desktop_notice("save_failed", error=self.describe_error(error))
                return False

        def desktop_notice(self, key, **values):
            message = self.t(key, **values)
            if key in self.desktop_notices:
                return
            self.desktop_notices.add(key)
            if self.tray:
                self.tray.showMessage("Yarnelo", message, QSystemTrayIcon.MessageIcon.Warning)
            else:
                print(f"Yarnelo: {message}", file=sys.stderr)

        def stop_desktop_tracking(self):
            self.desktop_timer.stop()
            if self.desktop_tracker is not None:
                self.desktop_tracker.close()
                self.desktop_tracker = None

        def configure_desktop_tracking(self):
            self.stop_desktop_tracking()
            if self.settings.auto_add_desktop_shortcuts and self.desktop_directory is not None:
                self.desktop_notices.clear()
                self.desktop_tracker = DesktopShortcutTracker(self.desktop_directory)
                if not args.smoke_test:
                    self.desktop_timer.start()

        def poll_desktop_shortcuts(self):
            if self.desktop_tracker is None or self.shutting_down:
                return
            added = []
            for path in self.desktop_tracker.scan():
                try:
                    # Files still being written stay pending for the tracker's
                    # bounded retry period. Only valid application entries add.
                    desktop_metadata(path, resolve_language(self.settings.language))
                    item = Item.create("path", str(path))
                    if any(existing.kind == "path" and existing.path == item.path for existing in self.settings.items):
                        self.desktop_tracker.acknowledge(path)
                    elif len(self.settings.items) >= 30:
                        self.desktop_tracker.acknowledge(path)
                        self.desktop_notice("auto_add_desktop_full")
                    else:
                        self.settings.add(item)
                        added.append((path, item))
                except (OSError, ModelError):
                    continue
            if added:
                if self.persist(False):
                    for path, _ in added:
                        self.desktop_tracker.acknowledge(path)
                    self.rebuild()
                else:
                    failed_ids = {item.id for _, item in added}
                    self.settings.items[:] = [item for item in self.settings.items if item.id not in failed_ids]

        def add_paths(self, paths):
            errors = []
            for path in paths:
                try:
                    candidate = Path(path)
                    if candidate.suffix.lower() == ".desktop":
                        desktop_metadata(candidate, resolve_language(self.settings.language))
                    self.settings.add(Item.create("path", str(candidate)))
                except (OSError, ModelError) as error:
                    errors.append(self.describe_error(error))
            self.rebuild()
            self.persist()
            if errors:
                QMessageBox.warning(self, "Yarnelo", "\n".join(dict.fromkeys(errors)))

        def add_special(self, kind):
            try:
                self.settings.add(Item.create(kind))
                self.rebuild()
                self.persist()
            except ModelError as error:
                QMessageBox.information(self, "Yarnelo", self.describe_error(error))

        def choose_files(self):
            paths, _ = QFileDialog.getOpenFileNames(self, self.t("add_files_title"), str(Path.home()), self.t("all_files"), options=QFileDialog.Option.DontUseNativeDialog)
            if paths:
                self.add_paths(paths)

        def choose_folder(self):
            path = QFileDialog.getExistingDirectory(self, self.t("add_folder_title"), str(Path.home()), options=QFileDialog.Option.ShowDirsOnly | QFileDialog.Option.DontUseNativeDialog)
            if path:
                self.add_paths([path])

        def launch_command(self, program, arguments):
            if not shutil.which(program):
                QMessageBox.warning(self, "Yarnelo", self.t("command_missing", program=program))
                return
            process = QProcess(self)
            self.processes.add(process)
            process.setProgram(program)
            process.setArguments(arguments)
            process.setProcessChannelMode(QProcess.ProcessChannelMode.SeparateChannels)
            if getattr(sys, "frozen", False):
                environment = QProcessEnvironment()
                for name, value in external_environment(dict(os.environ), Path(sys._MEIPASS)).items():
                    environment.insert(name, value)
                process.setProcessEnvironment(environment)

            def finished(code, status):
                if code != 0:
                    detail = bytes(process.readAllStandardError()).decode("utf-8", errors="replace").strip()[:1200]
                    QMessageBox.warning(self, "Yarnelo", self.t("open_failed", error=detail or self.t("command_failed")))
                self.processes.discard(process)
                process.deleteLater()

            def failed(error):
                if error == QProcess.ProcessError.FailedToStart:
                    QMessageBox.warning(self, "Yarnelo", self.t("launch_failed", program=program, error=process.errorString()))
                    self.processes.discard(process)
                    process.deleteLater()

            process.finished.connect(finished)
            process.errorOccurred.connect(failed)
            process.start()

        def launch(self, item):
            if item.kind == "trash":
                self.launch_command("gio", ["open", "trash:///"]) if shutil.which("gio") else self.launch_command("xdg-open", ["trash:///"])
            elif item.kind == "computer":
                self.launch_command("xdg-open", ["/"])
            else:
                path = Path(item.path)
                if not path.exists():
                    QMessageBox.information(self, "Yarnelo", self.t("target_missing", path=path))
                    return
                if path.suffix.lower() == ".desktop":
                    try:
                        desktop_metadata(path, resolve_language(self.settings.language))
                    except ModelError as error:
                        QMessageBox.warning(self, "Yarnelo", self.describe_error(error))
                        return
                    self.launch_command("gio", ["launch", str(path)])
                else:
                    self.launch_command("xdg-open", [str(path)])

        def remove_item(self, ident):
            self.settings.items = [item for item in self.settings.items if item.id != ident]
            self.rebuild()
            self.persist()

        def build_menu(self, item=None):
            menu = QMenu(self)
            if item:
                self.add_action(menu, "open", lambda: self.launch(item))
                if item.kind == "path":
                    self.add_action(menu, "open_parent", lambda: self.launch_command("xdg-open", [str(Path(item.path).parent)]))
                self.add_action(menu, "remove", lambda: self.remove_item(item.id))
                menu.addSeparator()
            add = menu.addMenu(self.t("add"))
            add.menuAction().setProperty("translation_key", "add")
            self.add_action(add, "files", self.choose_files)
            self.add_action(add, "folder", self.choose_folder)
            add.addSeparator()
            self.add_action(add, "computer", lambda: self.add_special("computer"))
            self.add_action(add, "trash", lambda: self.add_special("trash"))
            self.add_action(menu, "settings", self.open_settings)
            self.add_language_menu(menu)
            positions = menu.addMenu(self.t("position"))
            positions.menuAction().setProperty("translation_key", "position")
            group = QActionGroup(positions)
            group.setExclusive(True)
            for placement in PLACEMENTS:
                action = self.add_action(positions, f"position_{placement}", lambda checked=False, value=placement: self.set_placement(value))
                action.setProperty("placement_value", placement)
                action.setCheckable(True)
                action.setChecked(self.settings.placement == placement)
                group.addAction(action)
            screens_menu = menu.addMenu(self.t("monitor"))
            screens_menu.menuAction().setProperty("translation_key", "monitor")
            for index, screen in enumerate(app.screens(), 1):
                action = screens_menu.addAction(f"{index}. {screen.name()}")
                action.setCheckable(True)
                action.setChecked(screen is self.current_screen())
                action.triggered.connect(lambda checked=False, name=screen.name(): self.change_screen(name))
            menu.addSeparator()
            version = menu.addAction(f"Yarnelo {VERSION}")
            version.setEnabled(False)
            self.add_action(menu, "quit", app.quit)
            return menu

        def show_menu(self, point, item=None):
            QToolTip.hideText()
            menu = self.build_menu(item)
            self.visible_menus.append(menu)
            try:
                menu.exec(point)
            finally:
                self.visible_menus.remove(menu)
                menu.deleteLater()

        def open_settings(self):
            QToolTip.hideText()
            if self.settings_dialog:
                self.settings_dialog.raise_()
                self.settings_dialog.activateWindow()
                return
            dialog = SettingsDialog(self)
            self.settings_dialog = dialog
            dialog.exec()
            self.settings_dialog = None
            if isValid(dialog):
                dialog.deleteLater()

        def settings_error(self, key, error):
            QMessageBox.warning(self, "Yarnelo", self.t(key, error=self.describe_error(error)))

        def apply_settings_dialog(self, dialog):
            previous = Settings.from_dict(self.settings.to_dict())
            previous_enabled = None
            try:
                if dialog.startup.isEnabled():
                    previous_enabled = self.autostart.enabled
                    # A profile preference never repairs or re-enables an
                    # entry disabled externally. Only a changed checkbox does.
                    if dialog.startup.isChecked() != dialog.startup_initial:
                        self.autostart.set_enabled(dialog.startup.isChecked())
                    self.settings.launch_at_login = self.autostart.enabled
                self.settings.rows = dialog.rows.value()
                self.settings.opacity = dialog.opacity.value() / 100
                self.settings.icon_size = dialog.icon_size.value()
                self.settings.zoom = dialog.zoom.value()
                self.settings.labels = dialog.labels.currentData()
                self.settings.auto_add_desktop_shortcuts = dialog.auto_add_desktop.isChecked()
                self.settings.corner_radius = dialog.corners.value()
                self.settings.placement = dialog.placement.currentData()
                if self.persist():
                    dialog.startup_initial = self.settings.launch_at_login
                    self.relayout(True)
                    if previous.auto_add_desktop_shortcuts != self.settings.auto_add_desktop_shortcuts:
                        self.configure_desktop_tracking()
                    return True
            except (OSError, ModelError) as error:
                self.settings_error("startup_failed", error)
            # Preserve the same Settings object: tiles and live menus refer to
            # it, and a failed disk write must not change the running dock.
            self.settings.__dict__.update(previous.__dict__)
            if previous_enabled is not None:
                try:
                    if self.autostart.enabled != previous_enabled:
                        self.autostart.set_enabled(previous_enabled)
                    dialog.startup_initial = self.autostart.enabled
                    dialog.startup.setChecked(dialog.startup_initial)
                except (OSError, ModelError) as error:
                    self.settings_error("startup_restore_failed", error)
            return False

        def reset_position(self):
            self.settings.placement = "bottom"
            self.settings.x = self.settings.y = None
            self.relayout()
            self.persist()

        def set_placement(self, placement):
            self.settings.placement = placement
            if placement == "free":
                self.settings.x, self.settings.y = self.x(), self.y()
            self.relayout()
            self.persist()

        def change_screen(self, name):
            self.settings.screen = name
            self.settings.x = self.settings.y = None
            self.relayout()
            self.persist()

        def show_passive(self):
            self.show()
            if app.platformName().lower() == "xcb":
                self.lower()

        def hideEvent(self, event):
            QToolTip.hideText()
            super().hideEvent(event)

        def mousePressEvent(self, event):
            if event.button() == Qt.MouseButton.LeftButton:
                self.panel_drag_offset = event.globalPosition().toPoint() - self.pos()
                self.panel_drag_moved = False
                self.setCursor(Qt.CursorShape.ClosedHandCursor)
                event.accept()
            else:
                super().mousePressEvent(event)

        def mouseMoveEvent(self, event):
            if self.panel_drag_offset is not None and event.buttons() & Qt.MouseButton.LeftButton:
                target = event.globalPosition().toPoint() - self.panel_drag_offset
                if self.panel_drag_moved or (target - self.pos()).manhattanLength() >= QApplication.startDragDistance():
                    self.panel_drag_moved = True
                    self.move(target)
                event.accept()
            else:
                handle = event.position().y() < 26 if self.layout_geometry.vertical else event.position().x() < 26
                self.setCursor(Qt.CursorShape.SizeAllCursor if handle else Qt.CursorShape.ArrowCursor)
                super().mouseMoveEvent(event)

        def mouseReleaseEvent(self, event):
            if self.panel_drag_offset is not None:
                self.panel_drag_offset = None
                if self.panel_drag_moved:
                    screen = app.screenAt(self.frameGeometry().center()) or self.current_screen()
                    self.settings.screen = screen.name()
                    self.settings.x, self.settings.y = self.x(), self.y()
                    self.settings.placement = "free"
                    self.relayout()
                    self.persist()
                self.setCursor(Qt.CursorShape.ArrowCursor)
                event.accept()
            else:
                super().mouseReleaseEvent(event)

        def contextMenuEvent(self, event):
            self.show_menu(event.globalPos())
            event.accept()

        def dragEnterEvent(self, event):
            if event.mimeData().hasUrls() and any(url.isLocalFile() for url in event.mimeData().urls()):
                event.setDropAction(Qt.DropAction.CopyAction)
                event.accept()
            else:
                event.ignore()

        def dragMoveEvent(self, event):
            self.dragEnterEvent(event)

        def dropEvent(self, event):
            paths = [url.toLocalFile() for url in event.mimeData().urls() if url.isLocalFile()]
            if paths:
                self.add_paths(paths)
                # CopyAction prevents the file manager deleting or moving original launchers.
                event.setDropAction(Qt.DropAction.CopyAction)
                event.accept()
            else:
                event.ignore()

    def receive_instance_requests(server, show_dock):
        while server.hasPendingConnections():
            socket = server.nextPendingConnection()
            pending = bytearray()

            def receive(socket=socket, pending=pending):
                pending.extend(bytes(socket.readAll()))
                if pending == b"show":
                    show_dock()
                elif len(pending) < 4:
                    return
                socket.disconnectFromServer()

            socket.readyRead.connect(receive)
            socket.disconnected.connect(socket.deleteLater)
            if socket.bytesAvailable():
                receive()

    if args.smoke_test:
        import tempfile
        import subprocess
        startup_fixture = tempfile.TemporaryDirectory(prefix="yarnelo-startup-")
        startup_root = Path(startup_fixture.name)
        startup_command = resolve_startup_command()
        if getattr(sys, "frozen", False):
            expected_command = [os.environ.get("APPIMAGE", sys.executable)]
            if os.environ.get("APPIMAGE") and ("APPIMAGE_EXTRACT_AND_RUN" in os.environ
                    or Path(os.environ.get("APPDIR", "")).name.startswith("appimage_extracted_")):
                expected_command.append("--appimage-extract-and-run")
            expected_command.append("--startup")
        else:
            expected_command = [sys.executable, str(Path(__file__).resolve()), "--startup"]
        if startup_command != expected_command:
            raise RuntimeError("The packaged startup command does not refer to the persistent launcher")
        startup_manager = AutostartManager(startup_command, startup_root / "config", environ={}, home=startup_root, config_dirs=[])
        desktop_fixture = startup_root / "Desktop"
        desktop_fixture.mkdir()
        desktop_entry_text = "[Desktop Entry]\nType=Application\nName=Counter-Strike 2\nExec=true\n"
        old_shortcut = desktop_fixture / "old.desktop"
        old_shortcut.write_text(desktop_entry_text, encoding="utf-8")
        if startup_manager.enabled or startup_manager.path.parent.exists() or Settings().launch_at_login:
            raise RuntimeError("A fresh profile enabled startup or created an autostart directory")
        settings = Settings(rows=2, labels=True, language=args.language or "auto", items=[Item("smoke-computer", "computer"), Item("smoke-trash", "trash"), Item("smoke-home", "path", str(Path.home()))])
        dock = Dock(settings, None, autostart=startup_manager, desktop_directory=desktop_fixture)
        dock.show_passive()
        app.processEvents()
        # Exercise the actual local IPC reader. Login startup must never raise
        # an already running dock; a deliberate manual launch still shows it.
        ipc_server = QLocalServer(app)
        if not ipc_server.listen(f"yarnelo-smoke-{startup_root.name}"):
            raise RuntimeError("Could not create the isolated single-instance test server")
        activations = []
        ipc_server.newConnection.connect(lambda: receive_instance_requests(ipc_server, lambda: activations.append(True)))
        for command, expected_shows in ((b"startup", 0), (b"show", 1)):
            client = QLocalSocket()
            client.connectToServer(ipc_server.serverName())
            if not client.waitForConnected(1000):
                raise RuntimeError("Could not connect to the isolated single-instance server")
            client.write(command)
            client.waitForBytesWritten(500)
            for _ in range(20):
                app.processEvents()
                if client.state() == QLocalSocket.LocalSocketState.UnconnectedState:
                    break
                client.waitForDisconnected(10)
            if len(activations) != expected_shows:
                raise RuntimeError("A startup instance activated the dock or a manual launch failed to show it")
            client.disconnectFromServer()
            client.deleteLater()
        ipc_server.close()
        ipc_server.deleteLater()
        # Verify actual UI switches, menu checks and .desktop display names in
        # both directions without ever reading or writing a real profile.
        with tempfile.TemporaryDirectory(prefix="yarnelo-i18n-") as directory:
            entry = Path(directory) / "localized.desktop"
            entry.write_text("[Desktop Entry]\nType=Application\nName=English demo\nName[ru]=Русский пример\nExec=true\n", encoding="utf-8")
            localized_item = Item("smoke-localized", "path", str(entry))
            settings.items.append(localized_item)
            dock.rebuild()
            menu = dock.build_menu()
            dock.visible_menus.append(menu)
            dialog = SettingsDialog(dock)
            dock.settings_dialog = dialog
            dock.store = Path(directory) / "settings.json"
            language_menu = next(action.menu() for action in menu.actions() if action.property("translation_key") == "language")
            for language, computer, title, localized_name in (("en", "Computer", "Yarnelo — Settings", "English demo"), ("ru", "Компьютер", "Yarnelo — настройки", "Русский пример"), ("en", "Computer", "Yarnelo — Settings", "English demo")):
                next(action for action in language_menu.actions() if action.property("language_value") == language).trigger()
                # A click on an already selected language is intentionally a no-op.
                if not dock.store.exists():
                    dock.persist()
                if load_settings(dock.store).language != language:
                    raise RuntimeError("Language menu selection was not persisted")
                if dock.tiles["smoke-computer"].label != computer or dialog.windowTitle() != title or dock.tiles[localized_item.id].label != localized_name:
                    raise RuntimeError("Language change did not refresh the visible interface")
                checked = [action.property("language_value") for action in language_menu.actions() if action.isChecked()]
                if checked != [language]:
                    raise RuntimeError("Language menu did not retain exactly one selection")
                cancel = dialog.buttons.button(QDialogButtonBox.StandardButton.Cancel).text()
                if cancel != text("cancel", language):
                    raise RuntimeError("Dialog buttons did not change language")
                if dialog.startup.text() != text("start_at_login", language) or not dialog.startup.isEnabled():
                    raise RuntimeError("The startup checkbox did not change language")
                if dialog.auto_add_desktop.text() != text("auto_add_desktop", language) or [dialog.labels.itemText(index) for index in range(2)] != [text("names_hover", language), text("names_always", language)]:
                    raise RuntimeError("The shortcut behavior controls did not change language")
                # Standard Qt dialogs share the bundled official Qt translation.
                expected_cancel = "Отмена" if language == "ru" else "Cancel"
                if app.translate("QPlatformTheme", "Cancel") != expected_cancel:
                    raise RuntimeError("The standard Qt translation is missing")
            saved_bytes = dock.store.read_bytes()
            original_persist = dock.persist
            dock.persist = lambda: False
            try:
                next(action for action in language_menu.actions() if action.property("language_value") == "ru").trigger()
            finally:
                dock.persist = original_persist
            checked = [action.property("language_value") for action in language_menu.actions() if action.isChecked()]
            if settings.language != "en" or checked != ["en"] or dock.store.read_bytes() != saved_bytes or dialog.windowTitle() != text("settings_title", "en"):
                raise RuntimeError("A failed language save did not restore the previous language")
            dialog.startup.setChecked(True)
            dialog.auto_add_desktop.setChecked(True)
            dialog.reject()
            if startup_manager.enabled or startup_manager.path.exists() or settings.launch_at_login or settings.auto_add_desktop_shortcuts or dock.desktop_tracker is not None or dock.store.read_bytes() != saved_bytes:
                raise RuntimeError("Cancel changed the startup record or saved profile")
            dock.settings_dialog = None
            dialog.deleteLater()

            def startup_dialog():
                current = SettingsDialog(dock)
                dock.settings_dialog = current
                return current

            def discard_dialog(current):
                dock.settings_dialog = None
                current.deleteLater()

            # Enable via OK, then read both the actual XDG file and its saved
            # mirror. No user directories or real session startup are involved.
            dialog = startup_dialog()
            dialog.startup.setChecked(True)
            dialog.labels.setCurrentIndex(0)
            dialog.accept()
            if dialog.result() != QDialog.DialogCode.Accepted or not startup_manager.enabled or not load_settings(dock.store).launch_at_login or load_settings(dock.store).labels:
                raise RuntimeError("The startup checkbox did not enable and persist the actual registration")
            validator = shutil.which("desktop-file-validate")
            if validator:
                validated = subprocess.run([validator, str(startup_manager.path)], capture_output=True, text=True, timeout=10,
                                           env=external_environment(dict(os.environ), Path(getattr(sys, "_MEIPASS", Path(__file__).resolve().parent))))
                if validated.returncode:
                    raise RuntimeError(f"The generated startup entry failed desktop-file-validate: {validated.stdout}{validated.stderr}")
            discard_dialog(dialog)

            # A new dialog reads external changes even if the old saved mirror
            # is true. An unrelated OK must not silently re-enable startup.
            enabled_content = startup_manager.path.read_text(encoding="utf-8")
            for disabled_text in (enabled_content.replace("Hidden=false", "Hidden=true"),
                                  enabled_content + "X-GNOME-Autostart-enabled=false\n"):
                startup_manager.path.write_text(disabled_text, encoding="utf-8")
                before = startup_manager.path.read_bytes()
                dialog = startup_dialog()
                if dialog.startup.isChecked():
                    raise RuntimeError("The startup checkbox ignored an externally disabled entry")
                dialog.accept()
                if startup_manager.enabled or startup_manager.path.read_bytes() != before or load_settings(dock.store).launch_at_login:
                    raise RuntimeError("Saving unrelated settings re-enabled externally disabled startup")
                discard_dialog(dialog)

            # Explicit enable followed by disable must survive a reopened dialog.
            dialog = startup_dialog()
            dialog.startup.setChecked(True)
            dialog.accept()
            discard_dialog(dialog)
            dialog = startup_dialog()
            if not dialog.startup.isChecked():
                raise RuntimeError("The startup checkbox did not read back the enabled entry")
            dialog.startup.setChecked(False)
            dialog.labels.setCurrentIndex(1)
            dialog.accept()
            if startup_manager.enabled or load_settings(dock.store).launch_at_login or not load_settings(dock.store).labels:
                raise RuntimeError("The startup checkbox did not disable the actual registration")
            discard_dialog(dialog)

            # Simulated startup write failure and profile write failure both
            # keep the dialog open and restore the running settings and entry.
            original_settings_error = dock.settings_error
            errors = []
            dock.settings_error = lambda key, error: errors.append((key, dock.t(key, error=dock.describe_error(error))))
            try:
                for language in ("en", "ru"):
                    dock.change_language(language)
                    saved_bytes = dock.store.read_bytes()
                    before_settings = settings.to_dict()
                    original_set_enabled = startup_manager.set_enabled

                    def fail_startup_write(enabled):
                        raise ModelError("startup_entry_unsafe")

                    startup_manager.set_enabled = fail_startup_write
                    dialog = startup_dialog()
                    dialog.startup.setChecked(True)
                    dialog.rows.setValue(4)
                    try:
                        dialog.accept()
                    finally:
                        startup_manager.set_enabled = original_set_enabled
                    if dialog.result() == QDialog.DialogCode.Accepted or dialog.startup.isChecked() or startup_manager.enabled or settings.to_dict() != before_settings or dock.store.read_bytes() != saved_bytes:
                        raise RuntimeError("A startup write failure did not restore the previous settings")
                    if not errors or errors[-1][1] != text("startup_failed", language, error=text("startup_entry_unsafe", language)):
                        raise RuntimeError("The startup failure was not displayed in the selected language")
                    discard_dialog(dialog)
                    dialog = startup_dialog()
                    dialog.startup.setChecked(True)
                    dialog.rows.setValue(4)
                    dock.persist = lambda: False
                    try:
                        dialog.accept()
                    finally:
                        dock.persist = original_persist
                    if dialog.result() == QDialog.DialogCode.Accepted or dialog.startup.isChecked() or startup_manager.enabled or settings.to_dict() != before_settings or dock.store.read_bytes() != saved_bytes:
                        raise RuntimeError("A profile write failure did not roll back the startup registration")
                    discard_dialog(dialog)
            finally:
                dock.settings_error = original_settings_error

            # Desktop auto-add is opt-in, starts from a snapshot, and references
            # only new valid .desktop entries without moving their originals.
            original_items = list(settings.items)
            dialog = startup_dialog()
            dialog.auto_add_desktop.setChecked(True)
            dialog.accept()
            discard_dialog(dialog)
            if not settings.auto_add_desktop_shortcuts or not load_settings(dock.store).auto_add_desktop_shortcuts or dock.desktop_tracker is None or dock.desktop_timer.isActive():
                raise RuntimeError("The isolated desktop monitor did not enable without a real timer")
            for _ in range(3):
                dock.poll_desktop_shortcuts()
            if len(settings.items) != len(original_items):
                raise RuntimeError("Enabling desktop auto-add imported old shortcuts")
            partial_shortcut = desktop_fixture / "new-game.desktop"
            partial_shortcut.write_text("[Desktop Entry]\nType=Application\nName=Unfinished\n", encoding="utf-8")
            for _ in range(3):
                dock.poll_desktop_shortcuts()
            if len(settings.items) != len(original_items):
                raise RuntimeError("An unfinished desktop shortcut was added")
            partial_shortcut.write_text(desktop_entry_text, encoding="utf-8")
            for _ in range(3):
                dock.poll_desktop_shortcuts()
            if len(settings.items) != len(original_items) + 1 or partial_shortcut.read_text(encoding="utf-8") != desktop_entry_text:
                raise RuntimeError("A completed new shortcut was not added, or its source changed")
            if not any(item.path == str(partial_shortcut) for item in load_settings(dock.store).items):
                raise RuntimeError("The new desktop shortcut was not saved")

            duplicate_shortcut = desktop_fixture / "already-added.desktop"
            duplicate_shortcut.write_text(desktop_entry_text, encoding="utf-8")
            settings.add(Item.create("path", str(duplicate_shortcut)))
            count = len(settings.items)
            for _ in range(3):
                dock.poll_desktop_shortcuts()
            if len(settings.items) != count:
                raise RuntimeError("Desktop auto-add created a duplicate shortcut")
            dock.persist()
            unsaved_shortcut = desktop_fixture / "retry-save.desktop"
            unsaved_shortcut.write_text(desktop_entry_text, encoding="utf-8")
            saved_bytes = dock.store.read_bytes()
            dock.persist = lambda notify=True: False
            try:
                for _ in range(3):
                    dock.poll_desktop_shortcuts()
            finally:
                dock.persist = original_persist
            if len(settings.items) != count or dock.store.read_bytes() != saved_bytes:
                raise RuntimeError("Desktop auto-add did not roll back a failed profile save")
            for _ in range(3):
                dock.poll_desktop_shortcuts()
            if len(settings.items) != count + 1:
                raise RuntimeError("Desktop auto-add did not retry a failed profile save")

            added_items = list(settings.items)
            settings.items += [Item(f"full-{index}", "path", str(startup_root / f"missing-{index}")) for index in range(30 - len(settings.items))]
            import contextlib
            import io
            notices = io.StringIO()
            with contextlib.redirect_stderr(notices):
                for index in range(2):
                    (desktop_fixture / f"full-{index}.desktop").write_text(desktop_entry_text, encoding="utf-8")
                for _ in range(3):
                    dock.poll_desktop_shortcuts()
            if len(settings.items) != 30 or notices.getvalue().count(dock.t("auto_add_desktop_full")) != 1:
                raise RuntimeError("Desktop capacity handling duplicated warnings or exceeded 30 shortcuts")
            settings.items = added_items
            dialog = startup_dialog()
            dialog.auto_add_desktop.setChecked(False)
            dialog.accept()
            discard_dialog(dialog)
            if dock.desktop_tracker is not None or load_settings(dock.store).auto_add_desktop_shortcuts:
                raise RuntimeError("Disabling desktop auto-add left a monitor running")
            while_disabled = desktop_fixture / "while-disabled.desktop"
            while_disabled.write_text(desktop_entry_text, encoding="utf-8")
            for _ in range(3):
                dock.poll_desktop_shortcuts()
            dialog = startup_dialog()
            dialog.auto_add_desktop.setChecked(True)
            dialog.accept()
            discard_dialog(dialog)
            for _ in range(3):
                dock.poll_desktop_shortcuts()
            if any(item.path == str(while_disabled) for item in settings.items):
                raise RuntimeError("Re-enabling desktop auto-add imported an old disabled-period shortcut")
            monitor = dock.desktop_tracker
            dialog = startup_dialog()
            dialog.auto_add_desktop.setChecked(False)
            dialog.reject()
            discard_dialog(dialog)
            if dock.desktop_tracker is not monitor or not settings.auto_add_desktop_shortcuts:
                raise RuntimeError("Cancel changed the active desktop monitor")
            settings.items = original_items
            dock.rebuild()
            dock.visible_menus.remove(menu)
            menu.deleteLater()
            settings.items.remove(localized_item)
            dock.store = None
            dock.change_language(args.language or "auto")
            dock.rebuild()
        # Exercise real Qt animation properties and the drag/neighbor path.
        first = dock.tiles[settings.items[0].id]
        long_name = "🎮 Counter-Strike 2 — 👩‍🚀 Очень длинное название приложения & Friends <Preview>"
        lines = wrapped_lines(long_name, QFont("", 8), 95)
        if len(lines) < 3 or "".join("".join(lines).split()) != "".join(long_name.split()):
            raise RuntimeError("Full shortcut name wrapping lost Unicode characters")
        captions = wrapped_lines(long_name, QFont("", 8), 80, 2)
        if len(captions) != 2 or not captions[-1].endswith("…"):
            raise RuntimeError("A long caption did not wrap and elide only after two lines")
        original_label = first.label
        first.label = long_name
        focused = app.focusWidget()
        app.sendEvent(first, QHelpEvent(QEvent.Type.ToolTip, first.rect().center(), first.mapToGlobal(first.rect().center())))
        app.processEvents()
        tooltip = next((widget for widget in app.topLevelWidgets() if widget.objectName() == "qtooltip_label"), None)
        if tooltip is None or tooltip.width() > 330 or app.focusWidget() is not focused:
            raise RuntimeError("The full-name hover bubble exceeded its width or took focus")
        tooltip_screen = app.screenAt(tooltip.frameGeometry().center()) or app.primaryScreen()
        if not tooltip_screen.availableGeometry().contains(tooltip.frameGeometry()):
            raise RuntimeError("The full-name hover bubble extended beyond the screen")
        QToolTip.hideText()
        first.label = original_label
        first.animate(first.hover_animation, 1.0)
        first.hover_animation.setCurrentTime(95)
        first.hover_animation.stop()
        first.animate(first.press_animation, 1.0)
        first.press_animation.setCurrentTime(75)
        first.press_animation.stop()
        first.set_press(0.0)
        original_ids = [item.id for item in settings.items]
        first.dragging = True
        dock.begin_drag(first)
        destination = dock.positions[settings.items[1].id] + first.rect().center()
        dock.update_drag(first, dock.mapToGlobal(destination))
        first.dragging = False
        dock.end_drag(first, dock.mapToGlobal(destination))
        if [item.id for item in settings.items] == original_ids:
            raise RuntimeError("Drag did not reorder the launcher tiles")
        for tile in dock.tiles.values():
            tile.position_animation.setCurrentTime(tile.position_animation.duration())
        app.processEvents()

        # Exercise real Qt layout on offset monitors and high-DPI-sized work
        # areas. The test profile and its files live only in the temporary dir.
        original_screen = dock.current_screen
        original_items, original_rows = list(settings.items), settings.rows
        original_placement, original_corners = settings.placement, settings.corner_radius

        class ScreenFixture:
            def __init__(self):
                self.bounds = QRect(-1280, 50, 1280, 720)
                self.availableGeometryChanged = original_screen().availableGeometryChanged

            def availableGeometry(self):
                return self.bounds

        fixture = ScreenFixture()
        dock.current_screen = lambda: fixture
        settings.items = [Item(f"layout-{index}", "computer") for index in range(30)]
        settings.rows, settings.labels = 1, True
        dock.rebuild()
        for always in (False, True):
            settings.labels = always
            for width, height in ((1280, 720), (640, 360)):
                fixture.bounds = QRect(-width, 50, width, height)
                for placement in ("top", "bottom", "left", "right"):
                    settings.placement = placement
                    dock.relayout()
                    app.processEvents()
                    if len(dock.positions) != 31 or not fixture.bounds.contains(dock.geometry()):
                        raise RuntimeError("A caption mode lost icons or exceeded the 30-shortcut work area")
                    for tile in [*dock.tiles.values(), dock.add_tile]:
                        if not dock.rect().contains(tile.geometry()):
                            raise RuntimeError("A caption mode clipped an icon on a small work area")
        settings.labels = True
        fixture.bounds = QRect(-1280, 50, 1280, 720)
        for placement in ("top", "bottom", "left", "right"):
            settings.placement = placement
            dock.relayout()
            app.processEvents()
            if not fixture.bounds.contains(dock.geometry()) or dock.effective_icon_size != settings.icon_size:
                raise RuntimeError("Overflow did not wrap into the available work area at the requested icon size")
            if dock.layout_geometry.rows <= 1 or len(dock.positions) != 31:
                raise RuntimeError("Overflow lost tiles or did not add rows/columns")
            for tile in [*dock.tiles.values(), dock.add_tile]:
                if not dock.rect().contains(tile.geometry()):
                    raise RuntimeError("A dock tile was clipped at a screen edge")
            if placement in ("top", "bottom") and abs(dock.geometry().center().x() - fixture.bounds.center().x()) > 1:
                raise RuntimeError("Horizontal placement was not centered")
            if placement in ("left", "right") and abs(dock.geometry().center().y() - fixture.bounds.center().y()) > 1:
                raise RuntimeError("Vertical placement was not centered")
            ordered_ids = [item.id for item in settings.items]
            tile = dock.tiles[settings.items[0].id]
            destination = dock.positions[settings.items[1].id] + tile.rect().center()
            tile.dragging = True
            dock.begin_drag(tile)
            dock.update_drag(tile, dock.mapToGlobal(destination))
            tile.dragging = False
            dock.end_drag(tile, dock.mapToGlobal(destination))
            if [item.id for item in settings.items] == ordered_ids:
                raise RuntimeError("Drag did not reorder an overflow/side dock")
            for tile in dock.tiles.values():
                tile.position_animation.setCurrentTime(tile.position_animation.duration())
            app.processEvents()
        for radius in (0, 23, 100):
            settings.corner_radius = radius
            dock.update()
            app.processEvents()
            preview = QImage(dock.size(), QImage.Format.Format_ARGB32)
            preview.fill(Qt.GlobalColor.transparent)
            dock.render(preview)
            if preview.isNull():
                raise RuntimeError("A background rounding option could not render")
        for width, height in ((1280, 720), (640, 480), (640, 360)):
            fixture.bounds = QRect(-width, 20, width, height)
            for language in ("en", "ru"):
                dock.change_language(language)
                dialog = SettingsDialog(dock)
                dialog.show()
                app.processEvents()
                dialog.fit_available_screen()
                app.processEvents()
                if not fixture.bounds.contains(dialog.frameGeometry()):
                    raise RuntimeError(f"Settings did not fit the {width}x{height} work area ({language})")
                widgets = [dialog.rows, dialog.opacity, dialog.corners, dialog.icon_size, dialog.zoom, dialog.placement, dialog.labels, dialog.startup, dialog.auto_add_desktop, dialog.buttons, dialog.note, dialog.rows_note, dialog.corner_note, *dialog.field_labels.values(), *dialog.headings.values()]
                for widget in widgets:
                    rect = QRect(widget.mapTo(dialog, QPoint()), widget.size())
                    if not dialog.rect().contains(rect):
                        raise RuntimeError(f"A settings control was clipped ({width}x{height}, {language})")
                    if isinstance(widget, QLabel) and widget.wordWrap() and widget.heightForWidth(widget.width()) > widget.height():
                        raise RuntimeError(f"Settings text was clipped ({width}x{height}, {language}, actual={widget.height()}, required={widget.heightForWidth(widget.width())}, dialog={dialog.size()}, hint={dialog.sizeHint()}: {widget.text()})")
                if args.smoke_output and (width == 1280 or (width, height) == (640, 360)):
                    preview = QImage(dialog.size(), QImage.Format.Format_ARGB32)
                    preview.fill(Qt.GlobalColor.transparent)
                    dialog.render(preview)
                    args.smoke_output.parent.mkdir(parents=True, exist_ok=True)
                    suffix = "-compact" if width == 640 else ""
                    preview.save(str(args.smoke_output.with_name(f"{args.smoke_output.stem}-settings{suffix}-{language}.png")), "PNG")
                dialog.close()
                dialog.deleteLater()
        dock.current_screen = original_screen
        settings.items, settings.rows = original_items, original_rows
        settings.placement, settings.corner_radius = original_placement, original_corners
        dock.change_language(args.language or "auto")
        dock.rebuild()
        app.processEvents()
        image = QImage(dock.size(), QImage.Format.Format_ARGB32)
        image.fill(Qt.GlobalColor.transparent)
        dock.render(image)
        if image.isNull() or not image.width() or not image.height():
            raise RuntimeError("Offscreen render returned an empty image")
        if args.smoke_output:
            args.smoke_output.parent.mkdir(parents=True, exist_ok=True)
            if not image.save(str(args.smoke_output), "PNG"):
                raise RuntimeError("Could not write smoke-test image")
        dock.close()
        print(f"Qt smoke before shutdown: None={sys.getrefcount(None)}", flush=True)
        shutdown_widgets = [widget for widget in app.allWidgets() if isinstance(widget, (Dock, Tile, SettingsDialog))]
        # Run the real event-loop exit path, including aboutToQuit and queued
        # DeferredDelete events. Returning from main with live widgets does not
        # exercise this lifecycle and leaves teardown to interpreter finalizers.
        QTimer.singleShot(0, app.quit)
        exit_code = app.exec()
        QCoreApplication.sendPostedEvents(None, QEvent.Type.DeferredDelete)
        app.processEvents()
        if exit_code != 0 or any(isValid(widget) for widget in shutdown_widgets):
            raise RuntimeError("Qt did not destroy the dock and tiles during normal shutdown")
        if dock.desktop_tracker is not None:
            raise RuntimeError("Qt did not stop desktop monitoring during normal shutdown")
        import gc
        gc.collect()
        startup_fixture.cleanup()
        print(f"Qt smoke after shutdown: None={sys.getrefcount(None)}; widgets destroyed", flush=True)
        print(f"Yarnelo {VERSION}: Qt offscreen smoke test passed ({image.width()}x{image.height()})")
        return 0

    # The socket lives in the user's private runtime directory where available.
    runtime = Path(os.environ.get("XDG_RUNTIME_DIR") or Path.home() / ".cache" / "yarnelo")
    if not runtime.is_absolute():
        runtime = Path.home() / ".cache" / "yarnelo"
    runtime.mkdir(parents=True, exist_ok=True, mode=0o700)
    uid = str(os.getuid())
    server_name = str(runtime / f"yarnelo-{uid}.sock")
    instance_lock = QLockFile(str(runtime / f"yarnelo-{uid}.lock"))
    if not instance_lock.tryLock(0):
        if args.startup:
            return 0
        existing = QLocalSocket()
        existing.connectToServer(server_name)
        if existing.waitForConnected(1500):
            existing.write(b"show")
            existing.waitForBytesWritten(500)
            existing.disconnectFromServer()
        else:
            QMessageBox.information(None, "Yarnelo", text("instance_running", settings.language))
        return 0
    existing = QLocalSocket()
    existing.connectToServer(server_name)
    if existing.waitForConnected(500):
        existing.write(b"startup" if args.startup else b"show")
        existing.waitForBytesWritten(500)
        existing.disconnectFromServer()
        return 0
    server = QLocalServer(app)
    server.setSocketOptions(QLocalServer.SocketOption.UserAccessOption)
    # Atomic QLockFile ownership prevents a second starter removing a live socket.
    # Remove only this app's stale Unix socket after a failed connection.
    QLocalServer.removeServer(server_name)
    if not server.listen(server_name):
        QMessageBox.critical(None, "Yarnelo", text("instance_failed", settings.language, error=server.errorString()))
        return 1
    autostart, startup_error = None, None
    try:
        autostart = AutostartManager(resolve_startup_command())
    except (OSError, ModelError) as error:
        startup_error = error
    dock = Dock(settings, store, bool(load_error), autostart, startup_error)

    def incoming():
        receive_instance_requests(server, dock.show_passive)

    server.newConnection.connect(incoming)
    app.aboutToQuit.connect(server.close)
    app.aboutToQuit.connect(instance_lock.unlock)
    dock.show_passive()
    if load_error:
        QTimer.singleShot(0, lambda: QMessageBox.warning(dock, "Yarnelo", dock.t("settings_recovered", error=dock.describe_error(load_error))))
    return app.exec()


if __name__ == "__main__":
    raise SystemExit(main())
