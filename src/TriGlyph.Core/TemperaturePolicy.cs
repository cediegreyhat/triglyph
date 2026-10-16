using System;

namespace TriGlyph.Core
{
    public enum TemperatureUnit
    {
        Celsius,
        Fahrenheit
    }

    public enum SmoothingMode
    {
        /// <summary>Show the sensor value verbatim. The honest default.</summary>
        None,
        ExponentialMovingAverage,
        MedianOf3
    }

    public static class TemperatureUnits
    {
        public static double CelsiusToFahrenheit(double celsius) => celsius * 9.0 / 5.0 + 32.0;

        public static double FahrenheitToCelsius(double fahrenheit) => (fahrenheit - 32.0) * 5.0 / 9.0;

        /// <summary>
        /// Render a Celsius reading for display. A non-finite reading renders as
        /// "--" rather than a fabricated number.
        /// </summary>
        public static string Format(double celsius, TemperatureUnit unit)
        {
            if (double.IsNaN(celsius) || double.IsInfinity(celsius))
            {
                return unit == TemperatureUnit.Celsius ? "--\u00B0C" : "--\u00B0F";
            }

            double shown = unit == TemperatureUnit.Celsius
                ? celsius
                : CelsiusToFahrenheit(celsius);

            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0}\u00B0{1}",
                shown, unit == TemperatureUnit.Celsius ? "C" : "F");
        }
    }

    /// <summary>
    /// Decides how a raw sensor reading becomes what the user sees: unit, smoothing,
    /// and how long a reading stays trustworthy.
    ///
    /// Smoothing is off by default on purpose - a temperature display that lags is
    /// worse than one that jitters, and hiding a real spike behind a median is a
    /// silent lie. It is offered because some panels flicker visibly at 1 Hz.
    /// </summary>
    public sealed class TemperaturePolicy
    {
        private readonly double[] _recent = new double[3];
        private int _recentCount;
        private int _recentNext;
        private double _ema;
        private bool _emaSeeded;

        public TemperatureUnit Unit { get; }

        public SmoothingMode Smoothing { get; }

        /// <summary>Weight of the newest sample when using exponential smoothing.</summary>
        public double EmaAlpha { get; }

        /// <summary>How old a reading may be before the UI should call it stale.</summary>
        public TimeSpan StaleAfter { get; }

        public static TemperaturePolicy Default { get; } =
            new TemperaturePolicy(TemperatureUnit.Celsius, SmoothingMode.None, 0.3, TimeSpan.FromSeconds(10));

        public TemperaturePolicy(
            TemperatureUnit unit,
            SmoothingMode smoothing,
            double emaAlpha,
            TimeSpan staleAfter)
        {
            if (emaAlpha <= 0 || emaAlpha > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(emaAlpha), emaAlpha, "alpha must be in (0, 1]");
            }

            Unit = unit;
            Smoothing = smoothing;
            EmaAlpha = emaAlpha;
            StaleAfter = staleAfter;
        }

        /// <summary>Feed a raw Celsius reading and get the value to display.</summary>
        public double Push(double celsius)
        {
            if (double.IsNaN(celsius) || double.IsInfinity(celsius))
            {
                throw new ArgumentOutOfRangeException(nameof(celsius), celsius, "reading must be finite");
            }

            switch (Smoothing)
            {
                case SmoothingMode.ExponentialMovingAverage:
                    if (!_emaSeeded)
                    {
                        _ema = celsius;
                        _emaSeeded = true;
                    }
                    else
                    {
                        _ema = EmaAlpha * celsius + (1 - EmaAlpha) * _ema;
                    }

                    return _ema;

                case SmoothingMode.MedianOf3:
                    _recent[_recentNext] = celsius;
                    _recentNext = (_recentNext + 1) % 3;
                    if (_recentCount < 3)
                    {
                        _recentCount++;
                    }

                    double[] window = new double[_recentCount];
                    Array.Copy(_recent, window, _recentCount);
                    Array.Sort(window);

                    if (window.Length < 3)
                    {
                        // Not enough samples for a median yet. Average what we have so the
                        // displayed value does not jump as the window fills.
                        double sum = 0;
                        foreach (double v in window)
                        {
                            sum += v;
                        }

                        return sum / window.Length;
                    }

                    return window[window.Length / 2];

                default:
                    return celsius;
            }
        }

        public void Reset()
        {
            Array.Clear(_recent, 0, _recent.Length);
            _recentCount = 0;
            _recentNext = 0;
            _ema = 0;
            _emaSeeded = false;
        }

        /// <summary>True when a reading taken at <paramref name="takenAt"/> is too old to trust.</summary>
        public bool IsStale(DateTimeOffset takenAt, DateTimeOffset now)
        {
            if (takenAt > now)
            {
                return false;
            }

            return now - takenAt > StaleAfter;
        }

        public double ToDisplayUnit(double celsius) =>
            Unit == TemperatureUnit.Celsius ? celsius : TemperatureUnits.CelsiusToFahrenheit(celsius);

        public string Format(double celsius) => TemperatureUnits.Format(celsius, Unit);
    }
}
