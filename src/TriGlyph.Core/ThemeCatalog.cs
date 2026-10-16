using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TriGlyph.Core
{
    /// <summary>
    /// A colour/typography preset. Every field is a "#RRGGBB" string so themes are
    /// trivially hand-editable and can be dropped into the themes/ folder.
    /// </summary>
    public sealed class ThemeDefinition
    {
        public string Name { get; set; } = "glass";
        public bool IsDark { get; set; } = true;

        public string Background { get; set; } = "#050505";
        public string Surface { get; set; } = "#101418";
        public string SurfaceAlt { get; set; } = "#161B22";
        public string Border { get; set; } = "#232A33";

        public string Text { get; set; } = "#F2F5F8";
        public string TextMuted { get; set; } = "#8B97A6";

        public string Accent { get; set; } = "#22D3EE";
        public string AccentAlt { get; set; } = "#34D399";

        public string Good { get; set; } = "#34D399";
        public string Warn { get; set; } = "#FBBF24";
        public string Bad { get; set; } = "#F87171";

        public string FontFamily { get; set; } = "Segoe UI";

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>Repair any invalid colour by substituting the glass preset's value.</summary>
        public void Normalize()
        {
            ThemeDefinition fallback = ThemeCatalog.BuiltIn("glass");

            if (string.IsNullOrWhiteSpace(Name)) Name = "glass";
            Background = Repair(Background, fallback.Background);
            Surface = Repair(Surface, fallback.Surface);
            SurfaceAlt = Repair(SurfaceAlt, fallback.SurfaceAlt);
            Border = Repair(Border, fallback.Border);
            Text = Repair(Text, fallback.Text);
            TextMuted = Repair(TextMuted, fallback.TextMuted);
            Accent = Repair(Accent, fallback.Accent);
            AccentAlt = Repair(AccentAlt, fallback.AccentAlt);
            Good = Repair(Good, fallback.Good);
            Warn = Repair(Warn, fallback.Warn);
            Bad = Repair(Bad, fallback.Bad);

            if (string.IsNullOrWhiteSpace(FontFamily)) FontFamily = fallback.FontFamily;
        }

        private static string Repair(string value, string fallback)
        {
            string normalized = AppSettings.NormalizeHexColor(value);
            return normalized.Length == 0 ? fallback : normalized;
        }

        public static ThemeDefinition FromJson(string json)
        {
            ThemeDefinition theme = null;
            try
            {
                theme = JsonSerializer.Deserialize<ThemeDefinition>(json, Options);
            }
            catch (JsonException)
            {
                theme = null;
            }

            if (theme == null)
            {
                theme = new ThemeDefinition();
            }

            theme.Normalize();
            return theme;
        }

        public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        });
    }

    public static class ThemeCatalog
    {
        public const string DefaultThemeName = "glass";

        public static IReadOnlyList<string> BuiltInNames() => new[] { "glass", "softlight", "industrial", "editorial" };

        /// <summary>Returns the named built-in, or the default preset when the name is unknown.</summary>
        public static ThemeDefinition BuiltIn(string name)
        {
            string key = (name ?? "").Trim().ToLowerInvariant();
            switch (key)
            {
                case "softlight": return SoftLight();
                case "industrial": return Industrial();
                case "editorial": return Editorial();
                default: return Glass();
            }
        }

        /// <summary>
        /// Built-ins plus every *.json in <paramref name="directory"/>. User files win on
        /// name collision, so shipping a tweaked glass.json overrides the built-in.
        /// A missing or unreadable directory is not an error.
        /// </summary>
        public static Dictionary<string, ThemeDefinition> LoadFrom(string directory)
        {
            var catalog = new Dictionary<string, ThemeDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in BuiltInNames())
            {
                catalog[name] = BuiltIn(name);
            }

            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return catalog;
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly);
            }
            catch (IOException)
            {
                return catalog;
            }
            catch (UnauthorizedAccessException)
            {
                return catalog;
            }

            foreach (string file in files)
            {
                ThemeDefinition theme;
                try
                {
                    theme = ThemeDefinition.FromJson(File.ReadAllText(file));
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(theme.Name))
                {
                    catalog[theme.Name] = theme;
                }
            }

            return catalog;
        }

        private static ThemeDefinition Glass() => new ThemeDefinition
        {
            Name = "glass",
            IsDark = true,
            Background = "#050505",
            Surface = "#101418",
            SurfaceAlt = "#161B22",
            Border = "#232A33",
            Text = "#F2F5F8",
            TextMuted = "#8B97A6",
            Accent = "#22D3EE",
            AccentAlt = "#34D399",
            FontFamily = "Segoe UI"
        };

        private static ThemeDefinition SoftLight() => new ThemeDefinition
        {
            Name = "softlight",
            IsDark = false,
            Background = "#F7F8FA",
            Surface = "#FFFFFF",
            SurfaceAlt = "#EEF1F5",
            Border = "#DCE2EA",
            Text = "#10151C",
            TextMuted = "#5C6773",
            Accent = "#2563EB",
            AccentAlt = "#059669",
            FontFamily = "Segoe UI"
        };

        private static ThemeDefinition Industrial() => new ThemeDefinition
        {
            Name = "industrial",
            IsDark = true,
            Background = "#0B0D0F",
            Surface = "#14181B",
            SurfaceAlt = "#1B2024",
            Border = "#2A3136",
            Text = "#D7DEE3",
            TextMuted = "#78868F",
            Accent = "#FF8A3D",
            AccentAlt = "#4ADE80",
            FontFamily = "Consolas"
        };

        private static ThemeDefinition Editorial() => new ThemeDefinition
        {
            Name = "editorial",
            IsDark = false,
            Background = "#FDFBF7",
            Surface = "#FFFFFF",
            SurfaceAlt = "#F4EFE6",
            Border = "#E3DACA",
            Text = "#241E17",
            TextMuted = "#6F6455",
            Accent = "#8C5A2B",
            AccentAlt = "#3F6212",
            FontFamily = "Georgia"
        };
    }
}
