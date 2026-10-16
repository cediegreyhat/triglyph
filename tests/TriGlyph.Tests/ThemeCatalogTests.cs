using System.IO;
using TriGlyph.Core;
using Xunit;

namespace TriGlyph.Tests
{
    public class ThemeCatalogTests
    {
        [Fact]
        public void Ships_four_built_in_presets()
        {
            var names = ThemeCatalog.BuiltInNames();
            Assert.Contains("glass", names);
            Assert.Contains("softlight", names);
            Assert.Contains("industrial", names);
            Assert.Contains("editorial", names);
            Assert.Equal(4, names.Count);
        }

        [Fact]
        public void Every_built_in_preset_has_valid_colours()
        {
            foreach (string name in ThemeCatalog.BuiltInNames())
            {
                var t = ThemeCatalog.BuiltIn(name);
                Assert.NotNull(t);
                Assert.False(string.IsNullOrWhiteSpace(t.Background));
                Assert.False(string.IsNullOrWhiteSpace(t.Text));
                Assert.False(string.IsNullOrWhiteSpace(t.Accent));
                Assert.NotEqual("", AppSettings.NormalizeHexColor(t.Background));
                Assert.NotEqual("", AppSettings.NormalizeHexColor(t.Text));
                Assert.NotEqual("", AppSettings.NormalizeHexColor(t.Accent));
            }
        }

        [Fact]
        public void Unknown_theme_name_falls_back_to_glass()
        {
            Assert.Equal("glass", ThemeCatalog.BuiltIn("nope-does-not-exist").Name);
        }

        [Fact]
        public void Parses_a_theme_from_json()
        {
            string json = @"{
                ""name"": ""custom"",
                ""isDark"": true,
                ""background"": ""#010203"",
                ""surface"": ""#040506"",
                ""text"": ""#F0F0F0"",
                ""accent"": ""#00FF88""
            }";

            var t = ThemeDefinition.FromJson(json);

            Assert.Equal("custom", t.Name);
            Assert.True(t.IsDark);
            Assert.Equal("#010203", t.Background);
            Assert.Equal("#00FF88", t.Accent);
        }

        [Fact]
        public void A_theme_with_an_invalid_colour_is_repaired_not_rejected()
        {
            string json = @"{ ""name"": ""broken"", ""background"": ""banana"", ""text"": ""#FFFFFF"", ""accent"": ""#00FF88"" }";
            var t = ThemeDefinition.FromJson(json);
            Assert.Equal("#050505", t.Background);   // glass background substituted
        }

        [Fact]
        public void Discovers_theme_files_in_a_directory()
        {
            string dir = Path.Combine(Path.GetTempPath(), "kf420-themes-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "mine.json"),
                    @"{ ""name"": ""mine"", ""background"": ""#111111"", ""text"": ""#EEEEEE"", ""accent"": ""#FF0000"" }");
                File.WriteAllText(Path.Combine(dir, "notes.txt"), "not a theme");

                var catalog = ThemeCatalog.LoadFrom(dir);

                Assert.True(catalog.ContainsKey("mine"));
                Assert.False(catalog.ContainsKey("notes"));
                Assert.True(catalog.ContainsKey("glass"));   // built-ins always present
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void A_missing_theme_directory_still_yields_the_built_ins()
        {
            var catalog = ThemeCatalog.LoadFrom("/nonexistent/themes/here");
            Assert.Equal(4, catalog.Count);
            Assert.True(catalog.ContainsKey("glass"));
        }

        [Fact]
        public void A_user_theme_overrides_a_built_in_of_the_same_name()
        {
            string dir = Path.Combine(Path.GetTempPath(), "kf420-themes-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "glass.json"),
                    @"{ ""name"": ""glass"", ""background"": ""#0A0A0A"", ""text"": ""#FFFFFF"", ""accent"": ""#123456"" }");

                var catalog = ThemeCatalog.LoadFrom(dir);

                Assert.Equal("#123456", catalog["glass"].Accent);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
