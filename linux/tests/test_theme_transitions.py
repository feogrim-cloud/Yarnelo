from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from yarnelo_themes import (
    COLOR_FIELDS, THEMES, THEME_IDS, appearance, blend_appearance, blend_color, foreground_progress, stylesheet,
)


class ThemeTransitionTests(unittest.TestCase):
    def test_color_midpoints_interpolate_channels_and_alpha_without_losing_endpoints(self):
        self.assertEqual("#808080", blend_color("#000000", "#FFFFFF", .5))
        self.assertEqual("#80800080", blend_color("#00000000", "#FFFF00FF", .5))
        self.assertEqual("#112233", blend_color("#112233", "#445566", -1))
        self.assertEqual("#445566", blend_color("#112233", "#445566", 2))

    def test_all_theme_endpoints_keep_exact_styles_and_dock_colors(self):
        for source in THEME_IDS:
            for target in THEME_IDS:
                first, second = appearance(source), appearance(target)
                self.assertIs(first, blend_appearance(first, second, 0))
                self.assertIs(second, blend_appearance(first, second, 1))
                self.assertEqual(stylesheet(target), stylesheet(second.palette))
                middle = blend_appearance(first, second, .5)
                for name in COLOR_FIELDS:
                    self.assertEqual(blend_color(getattr(first.palette, name), getattr(second.palette, name), .5), getattr(middle.palette, name))
        dusk = appearance("dusk", .64, 23)
        self.assertEqual(("#2A3146", "#11192B", "#51DEE8FF", "#E0E6FA", "#F2F4FF", "#5FDDE5FF"),
                         (dusk.palette.surface, dusk.palette.bottom, dusk.edge, dusk.glyph, dusk.caption, dusk.handle))

    def test_opacity_and_custom_rounding_blend_independently_of_theme_defaults(self):
        first, second = appearance("dusk", .27, 0), appearance("frost", .93, 40)
        middle = blend_appearance(first, second, .5)
        self.assertAlmostEqual(.6, middle.palette.opacity)
        self.assertEqual(20, middle.radius(250))
        self.assertEqual((.27, 0), (first.palette.opacity, first.palette.corner_radius))
        self.assertEqual((.93, 40), (second.palette.opacity, second.palette.corner_radius))

    def test_capsule_sentinel_has_no_last_frame_jump_on_tall_multirow_docks(self):
        for maximum in (30, 150, 280):
            for first_radius, second_radius in ((0, 100), (23, 100), (100, 0), (100, 67)):
                first, second = appearance("dusk", corner_radius=first_radius), appearance("frost", corner_radius=second_radius)
                first_actual, second_actual = first.radius(maximum), second.radius(maximum)
                for amount in (0, .25, .5, .75, .99, .999, 1):
                    middle = blend_appearance(first, second, amount)
                    # When a fixed radius itself exceeds the available size,
                    # paint clamps it; large multirow docks retain linearity.
                    if maximum >= max(first_radius if first_radius < 100 else 0, second_radius if second_radius < 100 else 0):
                        self.assertAlmostEqual(first_actual + (second_actual - first_actual) * amount, middle.radius(maximum))
                penultimate = blend_appearance(first, second, .999999).radius(maximum)
                self.assertLess(abs(second_actual - penultimate), .001)

    def test_retarget_starts_from_the_currently_visible_frame(self):
        initial, old_target, new_target = appearance("dusk"), appearance("frost"), appearance("ember")
        visible = blend_appearance(initial, old_target, .37)
        self.assertIs(visible, blend_appearance(visible, new_target, 0))
        self.assertNotEqual(blend_appearance(initial, new_target, .5), blend_appearance(visible, new_target, .5))
        self.assertIs(new_target, blend_appearance(visible, new_target, 1))

    def test_aurora_hover_edge_fades_with_the_palette_and_remains_idle_for_other_themes(self):
        dusk, aurora = appearance("dusk"), appearance("aurora")
        self.assertEqual(dusk.edge, dusk.hovered_edge)
        self.assertNotEqual(aurora.edge, aurora.hovered_edge)
        middle = blend_appearance(aurora, dusk, .5)
        self.assertEqual(blend_color(aurora.hovered_edge, dusk.hovered_edge, .5), middle.hovered_edge)

    def test_light_dark_foreground_changes_in_a_short_continuous_window(self):
        first, second = appearance("dusk"), appearance("frost")
        for amount, expected in ((0, 0), (.2, 0), (.35, 0), (.5, .5), (.65, 1), (.8, 1), (1, 1)):
            self.assertEqual(expected, foreground_progress(first.palette, second.palette, amount))
        early, late = blend_appearance(first, second, .3), blend_appearance(first, second, .7)
        self.assertEqual(first.palette.foreground, early.palette.foreground)
        self.assertEqual(second.palette.foreground, late.palette.foreground)
        self.assertNotEqual(first.palette.surface, early.palette.surface)
        self.assertNotEqual(second.palette.surface, late.palette.surface)
        for amount in (.2, .5, .8):
            self.assertEqual(amount, foreground_progress(THEMES["dusk"], THEMES["graphite"], amount))


if __name__ == "__main__":
    unittest.main()
