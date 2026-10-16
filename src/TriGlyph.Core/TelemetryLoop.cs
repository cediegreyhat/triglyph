using System;
using System.Collections.Generic;

namespace TriGlyph.Core
{
    /// <summary>A place CPU temperature can be read from.</summary>
    public interface ITemperatureSource
    {
        string Name { get; }

        /// <summary>Read Celsius. Returns false with a reason rather than inventing a value.</summary>
        bool TryRead(out double celsius, out string error);
    }

    /// <summary>The panel, from the loop's point of view.</summary>
    public interface IPanelWriter
    {
        bool TryWrite(TemperatureFrame frame, out string error);
    }

    public sealed class TelemetryTickResult
    {
        public bool Wrote { get; }
        public double? Celsius { get; }
        public string SourceName { get; }
        public string Message { get; }

        /// <summary>True when nothing trustworthy is currently on the panel.</summary>
        public bool IsStale { get; }

        public TelemetryTickResult(bool wrote, double? celsius, string sourceName, string message, bool isStale)
        {
            Wrote = wrote;
            Celsius = celsius;
            SourceName = sourceName;
            Message = message;
            IsStale = isStale;
        }
    }

    /// <summary>
    /// One iteration of the read -> smooth -> encode -> write pipeline.
    ///
    /// Deliberately lives here rather than in the WPF layer: it is the part with real
    /// behaviour (failover, range rejection, staleness) and it can only be tested if it
    /// does not depend on Windows. The app supplies real adapters; tests supply fakes.
    /// </summary>
    public sealed class TelemetryLoop
    {
        private readonly IReadOnlyList<ITemperatureSource> _sources;
        private readonly IPanelWriter _writer;
        private readonly TemperaturePolicy _policy;
        private readonly bool _writeOnlyOnChange;

        private bool _hasDisplayed;
        private int _displayedCelsius;
        private DateTimeOffset _lastGoodReadingAt;

        public TelemetryLoop(
            IReadOnlyList<ITemperatureSource> sources,
            IPanelWriter writer,
            TemperaturePolicy policy,
            bool writeOnlyOnChange = false)
        {
            _sources = sources ?? (IReadOnlyList<ITemperatureSource>)new ITemperatureSource[0];
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
            _writeOnlyOnChange = writeOnlyOnChange;
        }

        public TelemetryTickResult Tick(DateTimeOffset now)
        {
            double celsius = 0;
            string chosenSource = null;
            string lastError = "no sources configured";

            foreach (ITemperatureSource source in _sources)
            {
                if (source == null)
                {
                    continue;
                }

                if (source.TryRead(out celsius, out string error))
                {
                    chosenSource = source.Name;
                    break;
                }

                lastError = $"{source.Name}: {error}";
                celsius = 0;
            }

            if (chosenSource == null)
            {
                return new TelemetryTickResult(
                    false, null, null,
                    "no temperature source available (" + lastError + ")",
                    IsStaleNow(now));
            }

            double smoothed = _policy.Push(celsius);

            if (!TemperatureFrame.TryFromCelsius(smoothed, out TemperatureFrame frame))
            {
                return new TelemetryTickResult(
                    false, null, chosenSource,
                    $"reading {smoothed:0.#}C from {chosenSource} is outside the panel's " +
                    $"{TemperatureFrame.MinCelsius}-{TemperatureFrame.MaxCelsius}C range; not written",
                    IsStaleNow(now));
            }

            if (_writeOnlyOnChange && _hasDisplayed && _displayedCelsius == frame.Celsius)
            {
                return new TelemetryTickResult(
                    false, frame.Celsius, chosenSource,
                    $"panel already shows {frame.Celsius}C; unchanged",
                    IsStaleNow(now));
            }

            if (!_writer.TryWrite(frame, out string writeError))
            {
                return new TelemetryTickResult(
                    false, null, chosenSource,
                    "panel write failed: " + writeError,
                    IsStaleNow(now));
            }

            _hasDisplayed = true;
            _displayedCelsius = frame.Celsius;
            _lastGoodReadingAt = now;

            return new TelemetryTickResult(
                true, frame.Celsius, chosenSource,
                $"{frame.Celsius}C written from {chosenSource}",
                false);
        }

        public void Reset()
        {
            _hasDisplayed = false;
            _displayedCelsius = 0;
            _lastGoodReadingAt = default;
            _policy.Reset();
        }

        private bool IsStaleNow(DateTimeOffset now)
        {
            if (!_hasDisplayed)
            {
                return true;
            }

            return _policy.IsStale(_lastGoodReadingAt, now);
        }
    }
}
