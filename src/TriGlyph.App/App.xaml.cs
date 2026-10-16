using System;
using System.IO;
using System.Windows;
using TriGlyph.App.Services;
using TriGlyph.App.Sources;
using TriGlyph.App.Theming;
using TriGlyph.Core;

namespace TriGlyph.App
{
    public partial class App : System.Windows.Application
    {
        private TelemetryHost _host;
        private TrayIcon _tray;
        private MainWindow _window;

        public AppSettings Settings { get; private set; }
        public string SettingsPath { get; private set; }
        public ThemeDefinition Theme { get; private set; }
        public string ThemesDirectory { get; private set; }
        public string AppDirectory { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            AppDirectory = AppDomain.CurrentDomain.BaseDirectory;
            ThemesDirectory = Path.Combine(AppDirectory, "themes");
            SettingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TriGlyph", "settings.json");

            Settings = SettingsStore.Load(SettingsPath);
            ApplyTheme(Settings.Theme);

            _host = new TelemetryHost(Settings);
            _window = new MainWindow(this, _host);

            _tray = new TrayIcon(this, _window, _host);
            _tray.Install();

            _host.Start();

            if (!Settings.StartMinimized)
            {
                _window.Show();
            }
        }

        public void ApplyTheme(string themeName)
        {
            var catalog = ThemeCatalog.LoadFrom(ThemesDirectory);
            Theme = catalog.ContainsKey(themeName ?? "") ? catalog[themeName] : catalog[ThemeCatalog.DefaultThemeName];
            ThemeApplier.Apply(Theme, Settings.AccentColor);
        }

        public void SaveSettings()
        {
            SettingsStore.Save(SettingsPath, Settings);
        }

        /// <summary>Re-read settings from disk, discarding in-memory changes.</summary>
        public void ReloadSettings()
        {
            Settings = SettingsStore.Load(SettingsPath);
            ApplyTheme(Settings.Theme);
        }

        public void ExitApplication()
        {
            _host?.Stop();
            _tray?.Uninstall();
            Shutdown();
        }
    }
}
