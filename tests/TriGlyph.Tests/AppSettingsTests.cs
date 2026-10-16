using System.Collections.Generic;
using System.IO;
using TriGlyph.Core;
using Xunit;

namespace TriGlyph.Tests
{
    public class AppSettingsTests
    {
        [Fact]
        public void Defaults_are_sane()
        {
            var s = new AppSettings();

            Assert.Equal(1, s.SchemaVersion);
            Assert.Equal(TemperatureUnit.Celsius, s.Unit);
            Assert.Equal(1.0, s.RefreshSeconds);
            Assert.Equal(SmoothingMode.None, s.Smoothing);
            Assert.False(s.UnsafeWritesArmed);
            Assert.False(s.BlankOnIdle);
            Assert.True(s.MinimizeToTray);
            Assert.Equal(5, s.SourcePriority.Count);
            Assert.Equal(TemperatureSourceNames.LibreHardwareMonitor, s.SourcePriority[0]);
        }

        [Fact]
        public void Writes_every_tick_by_default_to_match_verified_reference_behaviour()
        {
            var s = new AppSettings();
            Assert.False(s.WriteOnlyOnChange);
        }

        [Fact]
        public void Write_only_on_change_round_trips()
        {
            var restored = SettingsStore.FromJson(SettingsStore.ToJson(new AppSettings { WriteOnlyOnChange = true }));
            Assert.True(restored.WriteOnlyOnChange);
        }

        [Fact]
        public void Round_trips_through_json()
        {
            var original = new AppSettings
            {
                Theme = "industrial",
                AccentColor = "#FF00AA",
                Unit = TemperatureUnit.Fahrenheit,
                RefreshSeconds = 2.5,
                Smoothing = SmoothingMode.MedianOf3,
                BlankOnIdle = true,
                RunAtStartup = true,
                PinnedDevicePath = @"\\?\hid#abc"
            };

            var restored = SettingsStore.FromJson(SettingsStore.ToJson(original));

            Assert.Equal("industrial", restored.Theme);
            Assert.Equal("#FF00AA", restored.AccentColor);
            Assert.Equal(TemperatureUnit.Fahrenheit, restored.Unit);
            Assert.Equal(2.5, restored.RefreshSeconds);
            Assert.Equal(SmoothingMode.MedianOf3, restored.Smoothing);
            Assert.True(restored.BlankOnIdle);
            Assert.True(restored.RunAtStartup);
            Assert.Equal(@"\\?\hid#abc", restored.PinnedDevicePath);
        }

        [Fact]
        public void Enums_serialise_as_readable_strings_so_the_file_stays_hand_editable()
        {
            string json = SettingsStore.ToJson(new AppSettings { Unit = TemperatureUnit.Fahrenheit });
            Assert.Contains("\"Fahrenheit\"", json);
        }

        [Fact]
        public void Unknown_fields_are_tolerated_for_forward_compatibility()
        {
            string json = "{ \"Unit\": \"Celsius\", \"SomethingFromTheFuture\": 42 }";
            var s = SettingsStore.FromJson(json);
            Assert.Equal(TemperatureUnit.Celsius, s.Unit);
        }

        [Fact]
        public void Corrupt_json_falls_back_to_defaults_instead_of_throwing()
        {
            var s = SettingsStore.FromJson("{ this is not json ");
            Assert.Equal(TemperatureUnit.Celsius, s.Unit);
            Assert.Equal(1.0, s.RefreshSeconds);
        }

        [Theory]
        [InlineData(0.01, 0.25)]
        [InlineData(0.0, 0.25)]
        [InlineData(-5, 0.25)]
        [InlineData(999, 10.0)]
        public void Refresh_interval_is_clamped(double given, double expected)
        {
            var s = new AppSettings { RefreshSeconds = given };
            s.Normalize();
            Assert.Equal(expected, s.RefreshSeconds);
        }

        [Theory]
        [InlineData("", "")]
        [InlineData("not-a-colour", "")]
        [InlineData("#12345", "")]
        [InlineData("#GGGGGG", "")]
        [InlineData("#22D3EE", "#22D3EE")]
        [InlineData("#22d3ee", "#22D3EE")]
        public void Accent_colour_is_validated_and_uppercased(string given, string expected)
        {
            var s = new AppSettings { AccentColor = given };
            s.Normalize();
            Assert.Equal(expected, s.AccentColor);
        }

        [Fact]
        public void Missing_source_priority_falls_back_to_the_default_order()
        {
            var s = new AppSettings { SourcePriority = null };
            s.Normalize();
            Assert.Equal(5, s.SourcePriority.Count);
        }

        [Fact]
        public void Source_priority_is_deduplicated_preserving_order()
        {
            var s = new AppSettings
            {
                SourcePriority = new List<string> { "AcpiThermalZone", "Manual", "AcpiThermalZone" }
            };
            s.Normalize();
            Assert.Equal(new[] { "AcpiThermalZone", "Manual" }, s.SourcePriority);
        }

        [Fact]
        public void Saving_and_loading_a_file_round_trips()
        {
            string path = Path.Combine(Path.GetTempPath(), "kf420-settings-test.json");
            try
            {
                SettingsStore.Save(path, new AppSettings { Theme = "editorial" });
                var loaded = SettingsStore.Load(path);
                Assert.Equal("editorial", loaded.Theme);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void Loading_a_missing_file_yields_defaults()
        {
            var s = SettingsStore.Load("/nonexistent/definitely/not/here.json");
            Assert.Equal(TemperatureUnit.Celsius, s.Unit);
        }

        [Fact]
        public void TryLoad_reports_the_error_without_throwing()
        {
            string path = Path.Combine(Path.GetTempPath(), "kf420-bad-" + System.Guid.NewGuid() + ".json");
            File.WriteAllText(path, "{ broken");
            try
            {
                Assert.False(SettingsStore.TryLoad(path, out var s, out string error));
                Assert.NotNull(s);
                Assert.False(string.IsNullOrWhiteSpace(error));
            }
            finally { File.Delete(path); }
        }
    }
}
