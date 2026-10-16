using System;
using System.Collections.Generic;
using System.Windows.Threading;
using TriGlyph.App.Interop;
using TriGlyph.App.Sources;
using TriGlyph.App.ViewModels;
using TriGlyph.Core;

namespace TriGlyph.App.Services
{
    /// <summary>
    /// Owns the timer, the device choice and the telemetry loop. Ticks happen on the
    /// dispatcher thread so results can be handed straight to the UI.
    /// </summary>
    public sealed class TelemetryHost
    {
        private readonly AppSettings _settings;
        private readonly ManualTemperatureSource _manual = new ManualTemperatureSource();
        private readonly DispatcherTimer _timer = new DispatcherTimer(DispatcherPriority.Background);

        private TelemetryLoop _loop;
        private HidPanelWriter _writer;
        private List<ITemperatureSource> _sources;

        public AppState State { get; } = new AppState();

        public DeviceMatch Match { get; private set; }
        public List<HidDeviceCandidate> Candidates { get; private set; } = new List<HidDeviceCandidate>();
        public HidReportDescriptor Descriptor { get; private set; }
        public byte[] RawDescriptor { get; private set; }

        public event Action<TelemetryTickResult> Ticked;

        public TelemetryHost(AppSettings settings)
        {
            _settings = settings ?? new AppSettings();
            _timer.Tick += OnTick;
        }

        public void Start()
        {
            RefreshDevice();
            Rebuild();

            _timer.Interval = TimeSpan.FromSeconds(_settings.RefreshSeconds);
            _timer.Start();
            OnTick(null, EventArgs.Empty);
        }

        public void Stop() => _timer.Stop();

        /// <summary>Call after settings change: new interval, sources or device.</summary>
        public void Reconfigure()
        {
            _timer.Interval = TimeSpan.FromSeconds(_settings.RefreshSeconds);
            Rebuild();
        }

        private void Rebuild()
        {
            _sources = SourceFactory.Build(_settings, _manual);

            var policy = new TemperaturePolicy(
                _settings.Unit, _settings.Smoothing, _settings.EmaAlpha, TimeSpan.FromSeconds(10));

            _writer = new HidPanelWriter(Match?.Candidate?.Path);
            _loop = new TelemetryLoop(_sources, _writer, policy, _settings.WriteOnlyOnChange);
        }

        public void RefreshDevice()
        {
            Candidates = HidEnumerator.Enumerate();

            Match = !string.IsNullOrEmpty(_settings.PinnedDevicePath)
                ? PinTo(_settings.PinnedDevicePath)
                : DeviceMatcher.Match(Candidates);

            State.Candidates.Clear();
            foreach (HidDeviceCandidate c in Candidates)
            {
                State.Candidates.Add(c.ToString());
            }

            if (Match.Candidate == null)
            {
                State.Device = "not detected";
                State.MatchReason = Match.Reason;
                State.IsConnected = false;
            }
            else
            {
                State.Device = $"VID_{Match.Candidate.VendorId:X4}&PID_{Match.Candidate.ProductId:X4} " +
                               $"iface {Match.Candidate.InterfaceNumber}";
                State.MatchReason = $"{Match.Confidence}: {Match.Reason}";
                State.IsConnected = true;
                ReadDescriptor();
            }
        }

        private DeviceMatch PinTo(string path)
        {
            HidDeviceCandidate pinned = Candidates.Find(c => string.Equals(c.Path, path, StringComparison.OrdinalIgnoreCase));
            return pinned != null
                ? new DeviceMatch(pinned, MatchConfidence.Exact, "pinned in settings")
                : DeviceMatcher.Match(Candidates);
        }

        /// <summary>Pull and decode the panel's undocumented report descriptor.</summary>
        public void ReadDescriptor()
        {
            RawDescriptor = null;
            Descriptor = null;

            string path = Match?.Candidate?.Path;
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            if (!HidFeatureChannel.TryOpen(path, out HidFeatureChannel channel, out string error))
            {
                State.AppendLog("descriptor: could not open device - " + error);
                return;
            }

            using (channel)
            {
                if (!channel.TryReadReportDescriptor(out byte[] raw, out error))
                {
                    State.AppendLog("descriptor: " + error);
                    return;
                }

                RawDescriptor = raw;

                if (HidReportDescriptor.TryParse(raw, out HidReportDescriptor parsed, out error))
                {
                    Descriptor = parsed;
                    State.AppendLog($"descriptor: decoded {raw.Length} bytes - {parsed.Reports.Count} report(s)");
                }
                else
                {
                    State.AppendLog("descriptor: could not decode - " + error);
                }
            }

            State.Reports.Clear();
            if (Descriptor != null)
            {
                foreach (HidReportInfo r in Descriptor.Reports)
                {
                    State.Reports.Add(r.ToString());
                }
            }
        }

        public void SetManual(double celsius) => _manual.Set(celsius);

        public void ClearManual() => _manual.Clear();

        /// <summary>Send one arbitrary feature report from the console.</summary>
        public bool TrySendRaw(byte[] payload, out string error)
        {
            error = null;

            string path = Match?.Candidate?.Path;
            if (string.IsNullOrEmpty(path))
            {
                error = "no panel selected";
                return false;
            }

            if (!HidFeatureChannel.TryOpen(path, out HidFeatureChannel channel, out error))
            {
                return false;
            }

            using (channel)
            {
                return channel.TrySetFeature(payload, out error);
            }
        }

        public bool TryReadRaw(byte[] buffer, out string error)
        {
            string path = Match?.Candidate?.Path;
            if (string.IsNullOrEmpty(path))
            {
                error = "no panel selected";
                return false;
            }

            if (!HidFeatureChannel.TryOpen(path, out HidFeatureChannel channel, out error))
            {
                return false;
            }

            using (channel)
            {
                return channel.TryGetFeature(buffer, out error);
            }
        }

        public ReportConsolePolicy ConsolePolicy =>
            new ReportConsolePolicy(_settings.UnsafeWritesArmed);

        private void OnTick(object sender, EventArgs e)
        {
            if (_loop == null)
            {
                return;
            }

            TelemetryTickResult result = _loop.Tick(DateTimeOffset.Now);

            State.Display = result.Celsius.HasValue
                ? result.Celsius.Value.ToString("0")
                : "--";
            State.UnitSuffix = _settings.Unit == TemperatureUnit.Celsius ? "C" : "F";
            State.Status = result.Message;
            State.Source = result.SourceName ?? "-";
            State.IsStale = result.IsStale;

            Ticked?.Invoke(result);
        }
    }
}
