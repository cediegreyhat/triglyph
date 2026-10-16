using System;
using System.Windows.Media;
using TriGlyph.Core;

namespace TriGlyph.App.Theming
{
    /// <summary>
    /// Pushes a ThemeDefinition into application resources. Views bind with
    /// DynamicResource so a theme change repaints without rebuilding the tree.
    /// </summary>
    public static class ThemeApplier
    {
        public static void Apply(ThemeDefinition theme, string accentOverride)
        {
            var resources = System.Windows.Application.Current.Resources;

            string accent = AppSettings.NormalizeHexColor(accentOverride);
            if (accent.Length == 0)
            {
                accent = theme.Accent;
            }

            Set(resources, "Bg", theme.Background);
            Set(resources, "Surface", theme.Surface);
            Set(resources, "SurfaceAlt", theme.SurfaceAlt);
            Set(resources, "BorderBrush", theme.Border);
            Set(resources, "Fg", theme.Text);
            Set(resources, "FgMuted", theme.TextMuted);
            Set(resources, "Accent", accent);
            Set(resources, "AccentAlt", theme.AccentAlt);
            Set(resources, "Good", theme.Good);
            Set(resources, "Warn", theme.Warn);
            Set(resources, "Bad", theme.Bad);
            Set(resources, "AccentSoft", accent, 0x33);
            Set(resources, "AccentGlow", accent, 0x66);
            resources["AppFontFamily"] = new FontFamily(theme.FontFamily);
        }

        private static void Set(System.Windows.ResourceDictionary resources, string key, string hex, byte? alpha = null)
        {
            Color color;
            try
            {
                color = (Color)ColorConverter.ConvertFromString(hex);
            }
            catch (FormatException)
            {
                color = Colors.Gray;
            }

            if (alpha.HasValue)
            {
                color = Color.FromArgb(alpha.Value, color.R, color.G, color.B);
            }

            var brush = new SolidColorBrush(color);
            brush.Freeze();
            resources[key] = brush;
        }
    }
}
