"""Five native, Qt-independent appearance and motion presets.

Dusk keeps the pre-theme dock colors and motion while all five themes share
the same controls. Materials are painted colors and gradients; no backdrop blur
or platform-specific window hooks are required.
"""

from dataclasses import dataclass, replace


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

COLOR_FIELDS = ("surface", "bottom", "sheet", "menu", "control", "selection", "edge", "foreground", "muted", "accent", "accent_text")


def blend_color(first: str, second: str, amount: float) -> str:
    """Interpolate RGB or Qt's #AARRGGBB colors without an image crossfade."""
    if amount <= 0:
        return first
    if amount >= 1:
        return second
    if len(first) == 7 and len(second) == 7:
        start, end = first[1:], second[1:]
    else:
        start = ("FF" + first[1:]) if len(first) == 7 else first[1:]
        end = ("FF" + second[1:]) if len(second) == 7 else second[1:]
    return "#" + "".join(f"{round(int(start[index:index + 2], 16) + (int(end[index:index + 2], 16) - int(start[index:index + 2], 16)) * amount):02X}" for index in range(0, len(start), 2))


def _alpha(color: str, alpha: int) -> str:
    return f"#{alpha:02X}{color[1:]}"


def foreground_progress(first: Theme, second: Theme, amount: float) -> float:
    def brightness(color):
        return sum(int(color[index:index + 2], 16) * weight for index, weight in zip((1, 3, 5), (.2126, .7152, .0722))) / 255
    if (brightness(first.surface) > .5) == (brightness(second.surface) > .5):
        return amount
    # Light/dark flips keep foreground changes in a shorter continuous window
    # to reduce the time text and its surface spend at similar middle grays.
    # This is the same .35→.65 window, written to retain an exact midpoint.
    progress = min(1.0, max(0.0, (amount * 20 - 7) / 6))
    return progress * progress * (3 - 2 * progress)


@dataclass(frozen=True)
class ThemeAppearance:
    palette: Theme
    edge: str
    hovered_edge: str
    glyph: str
    caption: str
    handle: str
    placeholder: str
    placeholder_edge: str
    radius_pixels: float
    radius_fraction: float

    def radius(self, maximum: float) -> float:
        # The full-rounding setting is a relative radius, rather than a 100px
        # sentinel. Blending both components avoids a jump at the last frame
        # and keeps a capsule correct if the dock changes size during a blend.
        return min(maximum, self.radius_pixels + self.radius_fraction * maximum)


def appearance(theme: str, opacity: float | None = None, corner_radius: float | None = None) -> ThemeAppearance:
    value = THEMES[theme]
    opacity = value.opacity if opacity is None else opacity
    radius = value.corner_radius if corner_radius is None else corner_radius
    value = replace(value, opacity=opacity, corner_radius=radius)
    alpha = round(255 * opacity)
    dusk = theme == "dusk"
    edge = _alpha("#DEE8FF" if dusk else value.edge, max(35 if dusk else 75, alpha // 2))
    hovered_edge = edge
    if theme == "aurora":
        edge, hovered_edge = _alpha(value.accent, 60), _alpha(value.accent, 155)
    return ThemeAppearance(
        value, edge, hovered_edge, "#E0E6FA" if dusk else value.foreground,
        "#F2F4FF" if dusk else value.foreground,
        _alpha("#DDE5FF" if dusk else value.foreground, 95 if dusk else 110),
        _alpha("#8FA3F0" if dusk else value.accent, 22 if dusk else 26),
        _alpha("#B4C6FA" if dusk else value.accent, 110 if dusk else 145),
        0.0 if radius >= 100 else radius, 1.0 if radius >= 100 else 0.0,
    )


def blend_appearance(first: ThemeAppearance, second: ThemeAppearance, amount: float) -> ThemeAppearance:
    if amount <= 0:
        return first
    if amount >= 1:
        return second
    def number(start, end):
        return start + (end - start) * amount
    text_amount = foreground_progress(first.palette, second.palette, amount)
    palette = replace(second.palette, **{name: blend_color(getattr(first.palette, name), getattr(second.palette, name), text_amount if name in ("foreground", "muted") else amount) for name in COLOR_FIELDS},
                      opacity=number(first.palette.opacity, second.palette.opacity),
                      corner_radius=number(first.palette.corner_radius, second.palette.corner_radius))
    colors = {name: blend_color(getattr(first, name), getattr(second, name), text_amount if name in ("glyph", "caption", "handle") else amount)
              for name in ("edge", "hovered_edge", "glyph", "caption", "handle", "placeholder", "placeholder_edge")}
    return ThemeAppearance(palette, **colors, radius_pixels=number(first.radius_pixels, second.radius_pixels),
                           radius_fraction=number(first.radius_fraction, second.radius_fraction))


def default_appearance(theme: str) -> dict[str, float]:
    value = THEMES[theme]
    return {"opacity": value.opacity, "corner_radius": value.corner_radius}


def stylesheet(theme: str | Theme) -> str:
    value = THEMES[theme] if isinstance(theme, str) else theme
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
