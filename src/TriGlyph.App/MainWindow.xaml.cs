using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TriGlyph.App.Services;
using TriGlyph.App.Views;

namespace TriGlyph.App
{
    public partial class MainWindow : Window
    {
        private readonly App _app;
        private readonly TelemetryHost _host;
        private readonly DashboardView _dashboard = new DashboardView();
        private readonly AdvancedView _advanced = new AdvancedView();
        private readonly SettingsView _settings = new SettingsView();

        private bool _exiting;

        public MainWindow(App app, TelemetryHost host)
        {
            _app = app;
            _host = host;

            InitializeComponent();

            DataContext = host.State;

            _dashboard.Initialize(host, app);
            _advanced.Initialize(host, app);
            _settings.Initialize(host, app);

            ShowPage("dashboard");
            Closing += OnClosing;
        }

        /// <summary>Called by App before Shutdown so closing is not cancelled by the tray.</summary>
        public void PrepareExit() => _exiting = true;

        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            // Fires during XAML load, before ContentHost exists.
            if (ContentHost == null)
            {
                return;
            }

            if (sender is RadioButton button && button.Tag is string tag)
            {
                ShowPage(tag);
            }
        }

        private void ShowPage(string tag)
        {
            switch (tag)
            {
                case "advanced":
                    ContentHost.Content = _advanced;
                    break;
                case "settings":
                    ContentHost.Content = _settings;
                    break;
                default:
                    ContentHost.Content = _dashboard;
                    break;
            }

            if (NavStatus != null)
            {
                NavStatus.Text = _host.State.Status;
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
                return;
            }

            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // DragMove throws if the button was released before it started.
            }
        }

        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            if (_app.Settings.MinimizeToTray)
            {
                Hide();
            }
            else
            {
                _app.ExitApplication();
            }
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            if (_exiting)
            {
                return;
            }

            if (_app.Settings.MinimizeToTray)
            {
                e.Cancel = true;
                Hide();
            }
            else
            {
                _app.ExitApplication();
            }
        }
    }
}
