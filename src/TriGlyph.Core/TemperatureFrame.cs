using System;

namespace TriGlyph.Core
{
    /// <summary>
    /// One temperature reading encoded into the panel's HID feature report 7.
    ///
    /// The panel has its own firmware that renders digits; the host only pushes a
    /// number. Wire format, verified against real KF620 DGT hardware:
    ///
    ///   byte 0     report id (7)
    ///   byte 1..2  0xFF, 0xFF
    ///   byte 3..5  hundreds, tens, ones  (whole degrees, truncated)
    ///   byte 6..63 zero padding
    ///
    /// Whole degrees only - the panel has three 7-segment digits and no decimal point.
    /// </summary>
    public sealed class TemperatureFrame
    {
        /// <summary>Feature report id the panel listens on.</summary>
        public const int ReportIdValue = 7;

        /// <summary>Total feature report length including the report id byte.</summary>
        public const int FrameLength = 64;

        public const int MinCelsius = 0;
        public const int MaxCelsius = 150;

        public byte ReportId => ReportIdValue;

        /// <summary>The 64 bytes to hand to HidD_SetFeature.</summary>
        public byte[] Bytes { get; }

        /// <summary>The whole-degree Celsius value actually encoded.</summary>
        public int Celsius { get; }

        private TemperatureFrame(byte[] bytes, int celsius)
        {
            Bytes = bytes;
            Celsius = celsius;
        }

        /// <summary>
        /// Encode a Celsius reading. Throws rather than clamping or inventing a value:
        /// a display showing a made-up number is worse than one showing a stale number.
        /// </summary>
        public static TemperatureFrame FromCelsius(double celsius)
        {
            if (!TryFromCelsius(celsius, out TemperatureFrame frame))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(celsius), celsius,
                    $"temperature must be finite and within {MinCelsius}-{MaxCelsius} C");
            }

            return frame;
        }

        /// <summary>Encode a Fahrenheit reading, converted to Celsius on the wire.</summary>
        public static TemperatureFrame FromFahrenheit(double fahrenheit)
        {
            if (double.IsNaN(fahrenheit) || double.IsInfinity(fahrenheit))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(fahrenheit), fahrenheit, "temperature must be finite");
            }

            return FromCelsius((fahrenheit - 32.0) * 5.0 / 9.0);
        }

        /// <summary>Non-throwing form, for UI code that must tolerate bad sensor readings.</summary>
        public static bool TryFromCelsius(double celsius, out TemperatureFrame frame)
        {
            frame = null;

            if (double.IsNaN(celsius) || double.IsInfinity(celsius))
            {
                return false;
            }

            if (celsius < MinCelsius || celsius > MaxCelsius)
            {
                return false;
            }

            // Truncation toward zero, matching the reference implementation's int() cast.
            int whole = (int)celsius;

            byte[] bytes = new byte[FrameLength];
            bytes[0] = (byte)ReportIdValue;
            bytes[1] = 0xFF;
            bytes[2] = 0xFF;
            bytes[3] = (byte)(whole / 100);
            bytes[4] = (byte)((whole / 10) % 10);
            bytes[5] = (byte)(whole % 10);

            frame = new TemperatureFrame(bytes, whole);
            return true;
        }
    }
}
