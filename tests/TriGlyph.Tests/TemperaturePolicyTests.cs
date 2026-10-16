using System;
using TriGlyph.Core;
using Xunit;

namespace TriGlyph.Tests
{
    public class TemperaturePolicyTests
    {
        [Fact]
        public void Default_policy_passes_readings_through_untouched()
        {
            var p = TemperaturePolicy.Default;

            Assert.Equal(45, p.Push(45));
            Assert.Equal(52, p.Push(52));
            Assert.Equal(TemperatureUnit.Celsius, p.Unit);
            Assert.Equal(SmoothingMode.None, p.Smoothing);
        }

        [Fact]
        public void Exponential_smoothing_seeds_on_the_first_reading()
        {
            var p = new TemperaturePolicy(TemperatureUnit.Celsius, SmoothingMode.ExponentialMovingAverage, 0.5, TimeSpan.FromSeconds(10));

            Assert.Equal(40, p.Push(40));
            Assert.Equal(45, p.Push(50));   // 0.5*50 + 0.5*40
            Assert.Equal(50, p.Push(55));   // 0.5*55 + 0.5*45
        }

        [Fact]
        public void Median_of_three_rejects_a_single_spike()
        {
            var p = new TemperaturePolicy(TemperatureUnit.Celsius, SmoothingMode.MedianOf3, 0.5, TimeSpan.FromSeconds(10));

            Assert.Equal(40, p.Push(40));
            Assert.Equal(45, p.Push(50));   // mean while the window warms up
            Assert.Equal(45, p.Push(45));   // median of 40/50/45
            Assert.Equal(50, p.Push(99));   // spike rejected: median of 50/45/99
        }

        [Fact]
        public void Reset_clears_smoothing_state()
        {
            var p = new TemperaturePolicy(TemperatureUnit.Celsius, SmoothingMode.ExponentialMovingAverage, 0.5, TimeSpan.FromSeconds(10));

            p.Push(40);
            p.Push(40);
            p.Reset();

            Assert.Equal(80, p.Push(80));   // reseeds, not blended with the old history
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public void Refuses_to_smooth_a_non_finite_reading(double bad)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => TemperaturePolicy.Default.Push(bad));
        }

        [Fact]
        public void Detects_a_stale_reading()
        {
            var p = TemperaturePolicy.Default;
            var now = DateTimeOffset.UnixEpoch.AddHours(1);

            Assert.False(p.IsStale(now.AddSeconds(-2), now));
            Assert.True(p.IsStale(now.AddSeconds(-30), now));
        }

        [Fact]
        public void A_future_timestamp_is_not_stale()
        {
            var p = TemperaturePolicy.Default;
            var now = DateTimeOffset.UnixEpoch.AddHours(1);

            Assert.False(p.IsStale(now.AddSeconds(5), now));
        }

        [Fact]
        public void Converts_to_fahrenheit_for_display_only()
        {
            Assert.Equal(113.0, TemperatureUnits.CelsiusToFahrenheit(45.0), 6);
            Assert.Equal(45.0, TemperatureUnits.FahrenheitToCelsius(113.0), 6);
        }

        [Fact]
        public void Formats_in_the_selected_unit()
        {
            Assert.Equal("45\u00B0C", TemperatureUnits.Format(45, TemperatureUnit.Celsius));
            Assert.Equal("113\u00B0F", TemperatureUnits.Format(45, TemperatureUnit.Fahrenheit));
        }

        [Fact]
        public void Formatting_never_invents_a_number_for_a_bad_reading()
        {
            Assert.Equal("--\u00B0C", TemperatureUnits.Format(double.NaN, TemperatureUnit.Celsius));
        }
    }
}
