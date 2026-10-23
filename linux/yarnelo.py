#!/usr/bin/env python3
"""Yarnelo: a native Qt desktop launcher for Linux. See linux/README.md."""

from __future__ import annotations

import argparse
import os
from pathlib import Path
import shutil
import sys

from yarnelo_model import (
    VERSION, Item, ModelError, Settings, config_path, desktop_metadata,
    load_settings, move_item, preserve_invalid_settings, save_settings, slot_indices,
)


def arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Yarnelo — панель ярлыков для Linux")
    parser.add_argument("--version", action="version", version=f"Yarnelo {VERSION}")
    parser.add_argument("--smoke-test", action="store_true", help="render offscreen without reading or modifying user settings")
    parser.add_argument("--smoke-output", type=Path, help="optional PNG output for --smoke-test")
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
    if args.smoke_output and not args.smoke_test:
        raise SystemExit("--smoke-output requires --smoke-test")
    if args.smoke_test:
        os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")

    from PySide6.QtCore import (
        QEasingCurve, QFileInfo, QLocale, QLockFile, QPoint, QPointF, QProcess, QProcessEnvironment, Property,
        QRect, QRectF, QSize, Qt, QTimer, QPropertyAnimation,
    )
    from PySide6.QtGui import (
        QColor, QFont, QFontDatabase, QIcon, QImage, QLinearGradient, QPainter, QPen, QPixmap,
    )
    from PySide6.QtNetwork import QLocalServer, QLocalSocket
    from PySide6.QtWidgets import (
        QApplication, QCheckBox, QDialog, QDialogButtonBox, QFileDialog,
        QFileIconProvider, QFormLayout, QLabel, QMenu, QMessageBox,
        QSlider, QSpinBox, QDoubleSpinBox, QSystemTrayIcon, QVBoxLayout, QWidget,
    )

    app = QApplication(sys.argv[:1])
    app.setApplicationName("Yarnelo")
    app.setApplicationVersion(VERSION)
    app.setOrganizationName("Yarnelo")
    app.setQuitOnLastWindowClosed(False)
    app.setStyle("Fusion")
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
        QSpinBox, QDoubleSpinBox { color: #f2f3fc; background: #303b52; padding: 4px; }
        QLabel, QCheckBox { color: #f2f3fc; }
        QPushButton { padding: 6px 14px; }
        QToolTip { background: #222b40; color: white; border: 1px solid #6b7891; padding: 5px; }
    """)

    class Tile(QWidget):
        def __init__(self, dock: "Dock", item: Item | None):
            super().__init__(dock)
            self.dock, self.item = dock, item
            self.progress = 0.0
            self.pressed_progress = 0.0
            self.press_origin: QPoint | None = None
            self.dragging = False
            self.pixmap = QPixmap()
            self.label = "Добавить ярлыки"
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
            if item:
                if item.kind == "trash":
                    self.label = "Корзина"
                    icon = QIcon.fromTheme("user-trash", self.dock.icon_provider.icon(QFileIconProvider.IconType.Trashcan))
                elif item.kind == "computer":
                    self.label = "Компьютер"
                    icon = QIcon.fromTheme("computer", self.dock.icon_provider.icon(QFileIconProvider.IconType.Computer))
                else:
                    path = Path(item.path)
                    self.label = path.name or item.path
                    if path.suffix.lower() == ".desktop" and path.is_file():
                        try:
                            self.label, name = desktop_metadata(path, QLocale.system().name())
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

        def paintEvent(self, event):
            painter = QPainter(self)
            painter.setRenderHint(QPainter.RenderHint.Antialiasing)
            painter.setRenderHint(QPainter.RenderHint.SmoothPixmapTransform)
            base = self.dock.effective_icon_size
            scale = (1 + (self.dock.settings.zoom - 1) * self.progress) * (1 - 0.13 * self.pressed_progress)
            size = base * scale
            label_height = 17 if self.dock.settings.labels and self.width() >= 42 else 0
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
                text = painter.fontMetrics().elidedText(self.label, Qt.TextElideMode.ElideRight, self.width() - 5)
                painter.drawText(QRect(2, self.height() - 18, self.width() - 4, 16), Qt.AlignmentFlag.AlignCenter, text)

        def mousePressEvent(self, event):
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
            self.setWindowTitle("Yarnelo — настройки")
            self.setMinimumWidth(350)
            layout = QVBoxLayout(self)
            form = QFormLayout()
            self.rows = QSpinBox()
            self.rows.setRange(1, 4)
            self.rows.setValue(dock.settings.rows)
            self.opacity = QSlider(Qt.Orientation.Horizontal)
            self.opacity.setRange(12, 100)
            self.opacity.setValue(round(dock.settings.opacity * 100))
            self.size = QSpinBox()
            self.size.setRange(24, 96)
            self.size.setSuffix(" px")
            self.size.setValue(dock.settings.icon_size)
            self.zoom = QDoubleSpinBox()
            self.zoom.setRange(1, 1.8)
            self.zoom.setSingleStep(0.05)
            self.zoom.setDecimals(2)
            self.zoom.setValue(dock.settings.zoom)
            self.labels = QCheckBox("Показывать подписи")
            self.labels.setChecked(dock.settings.labels)
            form.addRow("Количество рядов", self.rows)
            form.addRow("Непрозрачность фона", self.opacity)
            form.addRow("Размер значков", self.size)
            form.addRow("Увеличение при наведении", self.zoom)
            form.addRow(self.labels)
            layout.addLayout(form)
            note = QLabel("На X11 панель располагается под обычными окнами.\nНа Wayland положение зависит от вашего окружения.")
            note.setWordWrap(True)
            layout.addWidget(note)
            buttons = QDialogButtonBox(QDialogButtonBox.StandardButton.Ok | QDialogButtonBox.StandardButton.Cancel)
            buttons.accepted.connect(self.accept)
            buttons.rejected.connect(self.reject)
            layout.addWidget(buttons)

    class Dock(QWidget):
        def __init__(self, settings: Settings, store: Path | None, invalid: bool = False):
            super().__init__()
            self.settings, self.store, self.invalid_settings = settings, store, invalid
            self.icon_provider = QFileIconProvider()
            self.tiles: dict[str, Tile] = {}
            self.positions: dict[str, QPoint] = {}
            self.processes: set[QProcess] = set()
            self.effective_icon_size = float(settings.icon_size)
            self.drag_order: list[Item] | None = None
            self.panel_drag_offset: QPoint | None = None
            self.layout_pending = False
            self.screen_hooked = None
            self.setWindowTitle("Yarnelo — панель ярлыков")
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
                menu = QMenu()
                menu.addAction("Показать панель", self.show_passive)
                menu.addAction("Настройки…", self.open_settings)
                menu.addSeparator()
                menu.addAction("Выход", app.quit)
                self.tray.setContextMenu(menu)
                self.tray.activated.connect(lambda reason: self.show_passive() if reason == QSystemTrayIcon.ActivationReason.Trigger else None)
                self.tray.show()
            app.screenAdded.connect(lambda screen: self.relayout())
            app.screenRemoved.connect(lambda screen: self.relayout())

        def current_screen(self):
            return next((screen for screen in app.screens() if screen.name() == self.settings.screen), app.primaryScreen())

        def rebuild(self):
            present = {item.id for item in self.settings.items}
            for ident in set(self.tiles) - present:
                self.tiles.pop(ident).deleteLater()
            for item in self.settings.items:
                if item.id not in self.tiles:
                    self.tiles[item.id] = Tile(self, item)
                self.tiles[item.id].show()
            self.add_tile.show()
            self.relayout()

        def relayout(self, animate=False):
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
            slots = slot_indices(len(self.settings.items), self.settings.rows)
            columns = max(column for _, column in slots) + 1
            rows = max(row for row, _ in slots) + 1
            cell_width = min(round(self.settings.icon_size * self.settings.zoom + 14), max(16, (bounds.width() - 58) // columns))
            self.effective_icon_size = min(self.settings.icon_size, max(8, (cell_width - 10) / self.settings.zoom))
            cell_height = round(self.effective_icon_size * self.settings.zoom + 14) + (18 if self.settings.labels else 0)
            width, height = 38 + columns * cell_width, 20 + rows * cell_height
            self.setFixedSize(width, height)
            target_x = self.settings.x if self.settings.x is not None else bounds.center().x() - width // 2
            target_y = self.settings.y if self.settings.y is not None else bounds.bottom() - height - 20
            self.move(min(max(target_x, bounds.left()), max(bounds.left(), bounds.right() - width + 1)), min(max(target_y, bounds.top()), max(bounds.top(), bounds.bottom() - height + 1)))
            row_counts = {row: sum(1 for r, _ in slots if r == row) for row in range(rows)}
            ordered = [self.tiles[item.id] for item in self.settings.items] + [self.add_tile]
            for tile, (row, column) in zip(ordered, slots):
                row_offset = (columns - row_counts[row]) * cell_width // 2
                point = QPoint(26 + row_offset + column * cell_width, 10 + row * cell_height)
                key = tile.item.id if tile.item else "+"
                self.positions[key] = point
                tile.setFixedSize(cell_width, cell_height)
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
            painter.drawRoundedRect(rect, 23, 23)
            painter.setPen(Qt.PenStyle.NoPen)
            painter.setBrush(QColor(221, 229, 255, 95))
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

        def persist(self):
            if self.store is None:
                return
            try:
                if self.invalid_settings and self.store.exists():
                    preserve_invalid_settings(self.store)
                    self.invalid_settings = False
                save_settings(self.store, self.settings)
            except (OSError, ModelError) as error:
                QMessageBox.warning(self, "Yarnelo", f"Не удалось сохранить настройки.\n{error}")

        def add_paths(self, paths):
            errors = []
            for path in paths:
                try:
                    candidate = Path(path)
                    if candidate.suffix.lower() == ".desktop":
                        desktop_metadata(candidate, QLocale.system().name())
                    self.settings.add(Item.create("path", str(candidate)))
                except (OSError, ModelError) as error:
                    errors.append(str(error))
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
                QMessageBox.information(self, "Yarnelo", str(error))

        def choose_files(self):
            paths, _ = QFileDialog.getOpenFileNames(self, "Добавить ярлыки и файлы", str(Path.home()), "Все файлы (*)")
            if paths:
                self.add_paths(paths)

        def choose_folder(self):
            path = QFileDialog.getExistingDirectory(self, "Добавить папку", str(Path.home()))
            if path:
                self.add_paths([path])

        def launch_command(self, program, arguments):
            if not shutil.which(program):
                QMessageBox.warning(self, "Yarnelo", f"Не найдена системная команда {program}. См. инструкцию по установке зависимостей Linux.")
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
                    QMessageBox.warning(self, "Yarnelo", f"Не удалось открыть ярлык.\n{detail or 'Системная команда завершилась с ошибкой.'}")
                self.processes.discard(process)
                process.deleteLater()

            def failed(error):
                if error == QProcess.ProcessError.FailedToStart:
                    QMessageBox.warning(self, "Yarnelo", f"Не удалось запустить {program}: {process.errorString()}")
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
                    QMessageBox.information(self, "Yarnelo", f"Файл или папка больше не найдены:\n{path}\nЯрлык можно убрать через меню правой кнопкой.")
                    return
                if path.suffix.lower() == ".desktop":
                    try:
                        desktop_metadata(path, QLocale.system().name())
                    except ModelError as error:
                        QMessageBox.warning(self, "Yarnelo", str(error))
                        return
                    self.launch_command("gio", ["launch", str(path)])
                else:
                    self.launch_command("xdg-open", [str(path)])

        def remove_item(self, ident):
            self.settings.items = [item for item in self.settings.items if item.id != ident]
            self.rebuild()
            self.persist()

        def show_menu(self, point, item=None):
            menu = QMenu(self)
            if item:
                menu.addAction("Открыть", lambda: self.launch(item))
                if item.kind == "path":
                    menu.addAction("Открыть папку ярлыка", lambda: self.launch_command("xdg-open", [str(Path(item.path).parent)]))
                menu.addAction("Убрать с панели", lambda: self.remove_item(item.id))
                menu.addSeparator()
            add = menu.addMenu("Добавить")
            add.addAction("Ярлыки и файлы…", self.choose_files)
            add.addAction("Папку…", self.choose_folder)
            add.addSeparator()
            add.addAction("Компьютер", lambda: self.add_special("computer"))
            add.addAction("Корзину", lambda: self.add_special("trash"))
            menu.addAction("Настройки…", self.open_settings)
            menu.addAction("Вниз по центру", self.reset_position)
            screens_menu = menu.addMenu("Монитор")
            for index, screen in enumerate(app.screens(), 1):
                action = screens_menu.addAction(f"{index}. {screen.name()}")
                action.setCheckable(True)
                action.setChecked(screen is self.current_screen())
                action.triggered.connect(lambda checked=False, name=screen.name(): self.change_screen(name))
            menu.addSeparator()
            menu.addAction(f"Yarnelo {VERSION} · Выход", app.quit)
            menu.exec(point)
            menu.deleteLater()

        def open_settings(self):
            dialog = SettingsDialog(self)
            if dialog.exec() == QDialog.DialogCode.Accepted:
                self.settings.rows = dialog.rows.value()
                self.settings.opacity = dialog.opacity.value() / 100
                self.settings.icon_size = dialog.size.value()
                self.settings.zoom = dialog.zoom.value()
                self.settings.labels = dialog.labels.isChecked()
                self.relayout(True)
                self.persist()
            dialog.deleteLater()

        def reset_position(self):
            self.settings.x = self.settings.y = None
            self.relayout()
            self.persist()

        def change_screen(self, name):
            self.settings.screen = name
            self.reset_position()

        def show_passive(self):
            self.show()
            if app.platformName().lower() == "xcb":
                self.lower()

        def mousePressEvent(self, event):
            if event.button() == Qt.MouseButton.LeftButton:
                self.panel_drag_offset = event.globalPosition().toPoint() - self.pos()
                self.setCursor(Qt.CursorShape.ClosedHandCursor)
                event.accept()
            else:
                super().mousePressEvent(event)

        def mouseMoveEvent(self, event):
            if self.panel_drag_offset is not None and event.buttons() & Qt.MouseButton.LeftButton:
                self.move(event.globalPosition().toPoint() - self.panel_drag_offset)
                event.accept()
            else:
                self.setCursor(Qt.CursorShape.SizeAllCursor if event.position().x() < 26 else Qt.CursorShape.ArrowCursor)
                super().mouseMoveEvent(event)

        def mouseReleaseEvent(self, event):
            if self.panel_drag_offset is not None:
                self.panel_drag_offset = None
                screen = app.screenAt(self.frameGeometry().center()) or self.current_screen()
                self.settings.screen = screen.name()
                self.settings.x, self.settings.y = self.x(), self.y()
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

    if args.smoke_test:
        settings = Settings(rows=2, labels=True, items=[Item("smoke-computer", "computer"), Item("smoke-trash", "trash"), Item("smoke-home", "path", str(Path.home()))])
        dock = Dock(settings, None)
        dock.show_passive()
        app.processEvents()
        # Exercise real Qt animation properties and the drag/neighbor path.
        first = dock.tiles[settings.items[0].id]
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
        existing = QLocalSocket()
        existing.connectToServer(server_name)
        if existing.waitForConnected(1500):
            existing.write(b"show")
            existing.waitForBytesWritten(500)
            existing.disconnectFromServer()
        else:
            QMessageBox.information(None, "Yarnelo", "Другой экземпляр Yarnelo уже запускается или работает.")
        return 0
    existing = QLocalSocket()
    existing.connectToServer(server_name)
    if existing.waitForConnected(500):
        existing.write(b"show")
        existing.waitForBytesWritten(500)
        existing.disconnectFromServer()
        return 0
    server = QLocalServer(app)
    server.setSocketOptions(QLocalServer.SocketOption.UserAccessOption)
    # Atomic QLockFile ownership prevents a second starter removing a live socket.
    # Remove only this app's stale Unix socket after a failed connection.
    QLocalServer.removeServer(server_name)
    if not server.listen(server_name):
        QMessageBox.critical(None, "Yarnelo", f"Не удалось запустить единственный экземпляр:\n{server.errorString()}")
        return 1
    store = config_path()
    load_error = None
    try:
        settings = load_settings(store)
    except ModelError as error:
        settings = Settings()
        load_error = str(error)
    dock = Dock(settings, store, bool(load_error))

    def incoming():
        while server.hasPendingConnections():
            socket = server.nextPendingConnection()
            socket.disconnectFromServer()
            socket.deleteLater()
        dock.show_passive()

    server.newConnection.connect(incoming)
    app.aboutToQuit.connect(server.close)
    app.aboutToQuit.connect(instance_lock.unlock)
    dock.show_passive()
    if load_error:
        QTimer.singleShot(0, lambda: QMessageBox.warning(dock, "Yarnelo", f"{load_error}\nПанель запущена с начальными настройками. При сохранении исходный файл будет сохранён рядом как settings.invalid-….json."))
    return app.exec()


if __name__ == "__main__":
    raise SystemExit(main())
