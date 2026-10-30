using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using TriGlyph.App.Services;
using TriGlyph.Core;

namespace TriGlyph.App.Views
{
    public partial class SettingsView : UserControl
    {
        private TelemetryHost _host;
        private App _app;
        private bool _loading = true;

        public SettingsView()
        {
            InitializeComponent();
        }

        public void Initialize(TelemetryHost host, App app)
        {
            _host = host;
            _app = app;
            DataContext = host.State;

            _loading = true;

            ThemeBox.Items.Clear();
            UnitBox.Items.Clear();
            SmoothingBox.Items.Clear();

            var catalog = ThemeCatalog.LoadFrom(app.ThemesDirectory);
            foreach (string name in catalog.Keys)
            {
                ThemeBox.Items.Add(name);
            }

            ThemeBox.SelectedItem = app.Settings.Theme;
            AccentBox.Text = app.Settings.AccentColor;

            UnitBox.Items.Add("Celsius");
            UnitBox.Items.Add("Fahrenheit");
            UnitBox.SelectedIndex = app.Settings.Unit == TemperatureUnit.Celsius ? 0 : 1;

            SmoothingBox.Items.Add("None (honest)");
            SmoothingBox.Items.Add("Exponential moving average");
            SmoothingBox.Items.Add("Median of 3");
            SmoothingBox.SelectedIndex = (int)app.Settings.Smoothing;

            RefreshSlider.Value = app.Settings.RefreshSeconds;
            RefreshLabel.Text = app.Settings.RefreshSeconds.ToString("0.##", CultureInfo.InvariantCulture) + " s";

            WriteOnChangeCheck.IsChecked = app.Settings.WriteOnlyOnChange;
            AutostartCheck.IsChecked = AutostartService.IsEnabled();
            StartMinimizedCheck.IsChecked = app.Settings.StartMinimized;
            TrayCheck.IsChecked = app.Settings.MinimizeToTray;

            _loading = false;

            RebuildSourceList();
            UpdatePinnedText();
        }

        private void RebuildSourceList()
        {
            SourceList.Items.Clear();
            foreach (string name in _app.Settings.SourcePriority)
            {
                SourceList.Items.Add(name);
            }
        }

        private void UpdatePinnedText()
        {
            PinnedText.Text = string.IsNullOrEmpty(_app.Settings.PinnedDevicePath)
                ? "Auto-detecting the panel."
                : "Pinned to: " + _app.Settings.PinnedDevicePath;
        }

        private void Theme_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || ThemeBox.SelectedItem == null)
            {
                return;
            }

            _app.Settings.Theme = (string)ThemeBox.SelectedItem;
            _app.ApplyTheme(_app.Settings.Theme);
            _app.SaveSettings();
            SettingsStatus.Text = "theme applied";
        }

        private void Accent_Changed(object sender, TextChangedEventArgs e)
        {
            if (_loading)
            {
                return;
            }

            _app.Settings.AccentColor = AccentBox.Text;
            _app.Settings.Normalize();
            _app.ApplyTheme(_app.Settings.Theme);
        }

        private void Unit_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading)
            {
                return;
            }

            _app.Settings.Unit = UnitBox.SelectedIndex == 0 ? TemperatureUnit.Celsius : TemperatureUnit.Fahrenheit;
            _host.Reconfigure();
        }

        private void Smoothing_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading)
            {
                return;
            }

            _app.Settings.Smoothing = (SmoothingMode)SmoothingBox.SelectedIndex;
            _host.Reconfigure();
        }

        private void Refresh_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading)
            {
                return;
            }

            RefreshLabel.Text = e.NewValue.ToString("0.##", CultureInfo.InvariantCulture) + " s";
            _app.Settings.RefreshSeconds = e.NewValue;
            _host.Reconfigure();
        }

        private void WriteOnChange_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading)
            {
                return;
            }

            _app.Settings.WriteOnlyOnChange = WriteOnChangeCheck.IsChecked == true;
            _host.Reconfigure();
        }

        private void Autostart_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading)
            {
                return;
            }

            bool wanted = AutostartCheck.IsChecked == true;
            if (!AutostartService.TrySet(wanted, out string error))
            {
                SettingsStatus.Text = "autostart failed: " + error;
                return;
            }

            _app.Settings.RunAtStartup = wanted;
            _app.SaveSettings();
            SettingsStatus.Text = wanted ? "autostart enabled" : "autostart disabled";
        }

        private void StartMinimized_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading)
            {
                return;
            }

            _app.Settings.StartMinimized = StartMinimizedCheck.IsChecked == true;
            _app.SaveSettings();
        }

        private void Tray_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading)
            {
                return;
            }

            _app.Settings.MinimizeToTray = TrayCheck.IsChecked == true;
            _app.SaveSettings();
        }

        private void Rescan_Click(object sender, RoutedEventArgs e)
        {
            _host.RefreshDevice();
            _host.Reconfigure();
            SettingsStatus.Text = "rescan complete";
        }

        private void Pin_Click(object sender, RoutedEventArgs e)
        {
            if (CandidateList.SelectedIndex < 0 || CandidateList.SelectedIndex >= _host.Candidates.Count)
            {
                SettingsStatus.Text = "select a device first";
                return;
            }

            _app.Settings.PinnedDevicePath = _host.Candidates[CandidateList.SelectedIndex].Path;
            _app.SaveSettings();
            _host.RefreshDevice();
            UpdatePinnedText();
            SettingsStatus.Text = "pinned";
        }

        private void Unpin_Click(object sender, RoutedEventArgs e)
        {
            _app.Settings.PinnedDevicePath = "";
            _app.SaveSettings();
            _host.RefreshDevice();
            UpdatePinnedText();
            SettingsStatus.Text = "pin cleared";
        }

        private void Up_Click(object sender, RoutedEventArgs e) => Move(-1);

        private void Down_Click(object sender, RoutedEventArgs e) => Move(1);

        private void Move(int delta)
        {
            int index = SourceList.SelectedIndex;
            List<string> order = _app.Settings.SourcePriority;

            int target = index + delta;
            if (index < 0 || target < 0 || target >= order.Count)
            {
                return;
            }

            string item = order[index];
            order.RemoveAt(index);
            order.Insert(target, item);

            RebuildSourceList();
            SourceList.SelectedIndex = target;
            _host.Reconfigure();
        }

        private void Manual_Click(object sender, RoutedEventArgs e)
        {
            if (!double.TryParse(ManualBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                SettingsStatus.Text = "manual value must be a number";
                return;
            }

            _host.SetManual(value);
            SettingsStatus.Text = "manual value set to " + value.ToString("0.#", CultureInfo.InvariantCulture);
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            _app.SaveSettings();
            SettingsStatus.Text = "saved to " + _app.SettingsPath;
        }

        private void Reload_Click(object sender, RoutedEventArgs e)
        {
            _app.ReloadSettings();
            Initialize(_host, _app);
            SettingsStatus.Text = "reloaded";
        }
    }
}
