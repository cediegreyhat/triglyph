using System;
using System.Linq;
using TriGlyph.Core;
using Xunit;

namespace TriGlyph.Tests
{
    /// <summary>
    /// Wire format under test is the one verified against real KF620 DGT hardware by
    /// the independent tzmrit-dgt-linux project: HID feature report 7, 64 bytes total,
    /// payload [7, 0xFF, 0xFF, hundreds, tens, ones] zero-padded, whole degrees only.
    /// These expected values are transcribed from that reference, not recomputed.
    /// </summary>
    public class TemperatureFrameTests
    {
        [Fact]
        public void Encodes_45C_as_the_verified_wire_format()
        {
            var frame = TemperatureFrame.FromCelsius(45);

            Assert.Equal(64, frame.Bytes.Length);
            Assert.Equal(7, frame.ReportId);
            Assert.Equal(new byte[] { 7, 0xFF, 0xFF, 0, 4, 5 }, frame.Bytes.Take(6).ToArray());
            Assert.True(frame.Bytes.Skip(6).All(b => b == 0), "padding must be zero");
        }

        [Theory]
        [InlineData(0, 0, 0, 0)]
        [InlineData(7, 0, 0, 7)]
        [InlineData(45, 0, 4, 5)]
        [InlineData(99, 0, 9, 9)]
        [InlineData(100, 1, 0, 0)]
        [InlineData(150, 1, 5, 0)]
        public void Encodes_each_decimal_digit_independently(int celsius, int hundreds, int tens, int ones)
        {
            var frame = TemperatureFrame.FromCelsius(celsius);

            Assert.Equal((byte)hundreds, frame.Bytes[3]);
            Assert.Equal((byte)tens, frame.Bytes[4]);
            Assert.Equal((byte)ones, frame.Bytes[5]);
        }

        [Fact]
        public void Truncates_fractional_degrees_toward_zero()
        {
            // 45.9 must render as 45 - matches the reference implementation's int() cast.
            var frame = TemperatureFrame.FromCelsius(45.9);

            Assert.Equal(45, frame.Celsius);
            Assert.Equal((byte)4, frame.Bytes[4]);
            Assert.Equal((byte)5, frame.Bytes[5]);
        }

        [Theory]
        [InlineData(-0.5)]
        [InlineData(-1)]
        [InlineData(151)]
        [InlineData(999)]
        public void Rejects_out_of_range_temperatures(double celsius)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => TemperatureFrame.FromCelsius(celsius));
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void Rejects_non_finite_temperatures(double celsius)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => TemperatureFrame.FromCelsius(celsius));
        }

        [Fact]
        public void Fahrenheit_is_converted_to_celsius_on_the_wire()
        {
            // 113 F == 45 C exactly.
            var frame = TemperatureFrame.FromFahrenheit(113);

            Assert.Equal(45, frame.Celsius);
            Assert.Equal(new byte[] { 7, 0xFF, 0xFF, 0, 4, 5 }, frame.Bytes.Take(6).ToArray());
        }

        [Fact]
        public void TryFromCelsius_reports_failure_instead_of_throwing()
        {
            Assert.False(TemperatureFrame.TryFromCelsius(999, out var bad));
            Assert.Null(bad);

            Assert.True(TemperatureFrame.TryFromCelsius(45, out var good));
            Assert.Equal(45, good.Celsius);
        }

        [Fact]
        public void Frames_are_value_comparable_by_their_bytes()
        {
            Assert.Equal(
                TemperatureFrame.FromCelsius(45).Bytes,
                TemperatureFrame.FromCelsius(45.4).Bytes);
        }
    }
}
