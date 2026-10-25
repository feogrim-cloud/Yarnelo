"""Five native, Qt-independent appearance and motion presets.

Dusk keeps the pre-theme Linux colors and motion. Materials are painted colors
and gradients; no backdrop blur or platform-specific window hooks are required.
"""

from dataclasses import dataclass


@dataclass(frozen=True)
class Theme:
    surface: str
    bottom: str
    sheet: str
    menu: str
    control: str
    selection: str
    edge: str
    foreground: str
    muted: str
    accent: str
    accent_text: str
    opacity: float
    corner_radius: float
    hover_ms: int
    press_ms: int
    move_ms: int
    lift: float
    press_depth: float
    hover_curve: str = "OutCubic"


THEMES = {
    "dusk": Theme("#2A3146", "#11192B", "#1C2334", "#1C2334", "#303B52", "#46547B", "#566078", "#F2F3FC", "#B8C6E2", "#6FAAFF", "#FFFFFF", .64, 23, 190, 150, 210, 2, .13),
    "frost": Theme("#EEF6FD", "#EEF6FD", "#EFF6FC", "#F0F7FD", "#DCE6EE", "#D9E8F4", "#A8C2D5", "#203340", "#5B6D7C", "#357CBA", "#FFFFFF", .76, 22, 240, 170, 250, 3.5, .11, "OutQuart"),
    "graphite": Theme("#191C20", "#191C20", "#1D2025", "#24282E", "#292D33", "#3E514A", "#68727B", "#E6E8EC", "#939CA9", "#AAD0BD", "#17221C", .96, 12, 130, 100, 150, 1.5, .08),
    "ember": Theme("#37241F", "#37241F", "#352621", "#3B2C25", "#4A352C", "#624A37", "#B18B68", "#F0E1D0", "#B8A394", "#EAB981", "#38261D", .85, 16, 210, 180, 235, 3, .12),
    "aurora": Theme("#17182F", "#17182F", "#1C1D31", "#242440", "#34334D", "#484263", "#8F86BE", "#ECEAFC", "#A2A1BF", "#C2B0FF", "#292039", .88, 24, 200, 140, 200, 4, .10),
}
THEME_IDS = tuple(THEMES)


def default_appearance(theme: str) -> dict[str, float]:
    value = THEMES[theme]
    return {"opacity": value.opacity, "corner_radius": value.corner_radius}


LEGACY_STYLESHEET = """
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
"""


def stylesheet(theme: str) -> str:
    value = THEMES[theme]
    if theme == "dusk":
        return LEGACY_STYLESHEET + '\nQLabel[tone="muted"] { color: #b8c6e2; }'
    return f"""
        QMenu, QDialog {{ background: {value.sheet}; color: {value.foreground}; }}
        QMenu {{ background: {value.menu}; border: 1px solid {value.edge}; border-radius: 9px; padding: 6px; }}
        QMenu::item {{ padding: 7px 22px; border-radius: 5px; }}
        QMenu::item:selected {{ background: {value.selection}; }}
        QMenu::item:disabled {{ color: {value.muted}; }}
        QMenu::separator {{ height: 1px; background: {value.edge}; margin: 5px 9px; }}
        QLabel, QCheckBox {{ color: {value.foreground}; }}
        QLabel[tone="muted"] {{ color: {value.muted}; }}
        QSpinBox, QDoubleSpinBox {{ color: {value.foreground}; background: {value.control}; padding: 4px; selection-background-color: {value.accent}; selection-color: {value.accent_text}; }}
        QComboBox {{ color: {value.foreground}; background: {value.control}; border: 1px solid {value.edge}; border-radius: 4px; padding: 4px; selection-background-color: {value.accent}; selection-color: {value.accent_text}; }}
        QComboBox QAbstractItemView {{ color: {value.foreground}; background: {value.menu}; selection-background-color: {value.selection}; selection-color: {value.foreground}; border: 1px solid {value.edge}; }}
        QPushButton {{ color: {value.foreground}; background: {value.control}; border: 1px solid {value.edge}; border-radius: 5px; padding: 6px 14px; }}
        QPushButton:hover {{ background: {value.selection}; }}
        QPushButton:pressed {{ background: {value.accent}; color: {value.accent_text}; }}
        QPushButton#primaryButton {{ background: {value.accent}; color: {value.accent_text}; border-color: {value.accent}; }}
        QPushButton:focus, QComboBox:focus, QSpinBox:focus, QDoubleSpinBox:focus {{ border-color: {value.accent}; }}
        QSlider::groove:horizontal {{ height: 4px; background: {value.control}; border: 1px solid {value.edge}; border-radius: 2px; }}
        QSlider::sub-page:horizontal {{ background: {value.accent}; border-radius: 2px; }}
        QSlider::handle:horizontal {{ background: {value.accent}; border: 1px solid {value.accent}; width: 10px; margin: -4px 0; border-radius: 5px; }}
        QToolTip {{ background: {value.menu}; color: {value.foreground}; border: 1px solid {value.edge}; padding: 5px; }}
    """
