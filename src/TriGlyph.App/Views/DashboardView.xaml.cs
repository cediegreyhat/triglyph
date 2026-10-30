using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using TriGlyph.App.Services;
using TriGlyph.Core;

namespace TriGlyph.App.Views
{
    public partial class DashboardView : UserControl
    {
        private TelemetryHost _host;
        private App _app;

        public DashboardView()
        {
            InitializeComponent();
        }

        public void Initialize(TelemetryHost host, App app)
        {
            _host = host;
            _app = app;

            DataContext = host.State;

            host.Ticked += OnTicked;
            host.State.PropertyChanged += (s, e) => Refresh();

            Loaded += (s, e) =>
            {
                ApplyAccentGlow();
                Refresh();
            };
        }

        private void OnTicked(TelemetryTickResult result) => Refresh();

        private void Refresh()
        {
            if (_host == null || !IsLoaded)
            {
                return;
            }

            var state = _host.State;

            Readout.Text = state.Display;
            UnitText.Text = state.UnitSuffix;
            StatusText.Text = state.Status;
            DeviceText.Text = state.Device;
            MatchText.Text = state.MatchReason;
            SourceText.Text = state.Source;

            bool live = !state.IsStale && state.IsConnected;

            LiveText.Text = live ? "LIVE" : (state.IsConnected ? "STALE" : "NO PANEL");
            LiveDot.SetResourceReference(Border.BackgroundProperty, live ? "Good" : "Bad");

            if (int.TryParse(state.Display, out int celsius) && !state.IsStale)
            {
                Readout.SetResourceReference(TextBlock.ForegroundProperty, celsius >= 85 ? "Bad" : celsius >= 70 ? "Warn" : "Fg");
            }
            else
            {
                Readout.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
            }
        }

        private void ApplyAccentGlow()
        {
            if (ReadoutGlow == null)
            {
                return;
            }

            var accent = FindResource("Accent") as SolidColorBrush;
            if (accent != null)
            {
                ReadoutGlow.Color = accent.Color;
            }
        }
    }
}
