using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TriGlyph.Core
{
    /// <summary>Canonical source names, used in the settings file and the UI.</summary>
    public static class TemperatureSourceNames
    {
        public const string LibreHardwareMonitor = "LibreHardwareMonitor";
        public const string OpenHardwareMonitor = "OpenHardwareMonitor";
        public const string HwInfoSharedMemory = "HwInfoSharedMemory";
        public const string AcpiThermalZone = "AcpiThermalZone";
        public const string Manual = "Manual";

        public static readonly IReadOnlyList<string> All = new[]
        {
            LibreHardwareMonitor,
            OpenHardwareMonitor,
            HwInfoSharedMemory,
            AcpiThermalZone,
            Manual
        };
    }

    /// <summary>
    /// User configuration. Serialised as human-editable JSON with string enums, so the
    /// file can be hand-tweaked and unknown keys from a newer version do not break it.
    /// </summary>
    public sealed class AppSettings
    {
        public const double MinRefreshSeconds = 0.25;
        public const double MaxRefreshSeconds = 10.0;
        public const int MinBlankAfterSeconds = 5;
        public const int MaxBlankAfterSeconds = 86400;

        public int SchemaVersion { get; set; } = 1;
        public string Theme { get; set; } = "glass";

        /// <summary>Empty means "use the theme's own accent".</summary>
        public string AccentColor { get; set; } = "";

        public TemperatureUnit Unit { get; set; } = TemperatureUnit.Celsius;
        public double RefreshSeconds { get; set; } = 1.0;
        public SmoothingMode Smoothing { get; set; } = SmoothingMode.None;
        public double EmaAlpha { get; set; } = 0.3;

        public bool BlankOnIdle { get; set; }
        public int BlankAfterSeconds { get; set; } = 300;

        public bool StartMinimized { get; set; }
        public bool RunAtStartup { get; set; }
        public bool MinimizeToTray { get; set; } = true;

        /// <summary>When set, the app uses this interface and skips auto-detection.</summary>
        public string PinnedDevicePath { get; set; } = "";

        /// <summary>Arms writes to report ids other than the known temperature report.</summary>
        public bool UnsafeWritesArmed { get; set; }

        /// <summary>
        /// When true the panel is only written when the displayed value changes.
        /// Off by default: the verified reference implementation writes every tick, and
        /// we do not know whether this panel blanks itself if reports stop arriving.
        /// </summary>
        public bool WriteOnlyOnChange { get; set; }

        public List<string> SourcePriority { get; set; } = DefaultSourcePriority();

        public static List<string> DefaultSourcePriority() => new List<string>(TemperatureSourceNames.All);

        /// <summary>Clamp and repair anything a hand-edited file could have got wrong.</summary>
        public void Normalize()
        {
            RefreshSeconds = Clamp(RefreshSeconds, MinRefreshSeconds, MaxRefreshSeconds);
            EmaAlpha = Clamp(EmaAlpha, 0.01, 1.0);
            BlankAfterSeconds = (int)Clamp(BlankAfterSeconds, MinBlankAfterSeconds, MaxBlankAfterSeconds);

            if (string.IsNullOrWhiteSpace(Theme))
            {
                Theme = "glass";
            }

            AccentColor = NormalizeHexColor(AccentColor);

            if (SourcePriority == null || SourcePriority.Count == 0)
            {
                SourcePriority = DefaultSourcePriority();
            }
            else
            {
                SourcePriority = SourcePriority
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (SourcePriority.Count == 0)
                {
                    SourcePriority = DefaultSourcePriority();
                }
            }

            PinnedDevicePath = PinnedDevicePath ?? "";
        }

        private static double Clamp(double value, double min, double max)
        {
            if (double.IsNaN(value)) return min;
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        /// <summary>Returns "#RRGGBB" upper-case, or "" when the input is not a valid colour.</summary>
        public static string NormalizeHexColor(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";

            string v = value.Trim();
            if (v.Length != 7 && v.Length != 9) return "";
            if (v[0] != '#') return "";

            for (int i = 1; i < v.Length; i++)
            {
                char c = v[i];
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex) return "";
            }

            return v.ToUpperInvariant();
        }
    }

    public static class SettingsStore
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            Converters = { new JsonStringEnumConverter() }
        };

        public static string ToJson(AppSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            settings.Normalize();
            return JsonSerializer.Serialize(settings, Options);
        }

        public static AppSettings FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new AppSettings();
            }

            AppSettings parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<AppSettings>(json, Options);
            }
            catch (JsonException)
            {
                return new AppSettings();
            }

            if (parsed == null)
            {
                return new AppSettings();
            }

            parsed.Normalize();
            return parsed;
        }

        public static bool TryLoad(string path, out AppSettings settings, out string error)
        {
            settings = new AppSettings();
            error = null;

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                error = "settings file not found";
                return false;
            }

            try
            {
                AppSettings parsed = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options);
                settings = parsed ?? new AppSettings();
                settings.Normalize();
                return true;
            }
            catch (JsonException ex)
            {
                error = "settings file is not valid JSON: " + ex.Message;
                settings = new AppSettings();
                return false;
            }
            catch (IOException ex)
            {
                error = ex.Message;
                settings = new AppSettings();
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                error = ex.Message;
                settings = new AppSettings();
                return false;
            }
        }

        public static AppSettings Load(string path)
        {
            TryLoad(path, out AppSettings settings, out _);
            return settings;
        }

        public static void Save(string path, AppSettings settings)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path required", nameof(path));

            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(path, ToJson(settings));
        }
    }
}
