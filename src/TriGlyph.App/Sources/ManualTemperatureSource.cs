using System.Globalization;
using TriGlyph.Core;

namespace TriGlyph.App.Sources
{
    /// <summary>A value the user typed. Always last in the priority order.</summary>
    internal sealed class ManualTemperatureSource : ITemperatureSource
    {
        private double? _value;

        public string Name => TemperatureSourceNames.Manual;

        public void Set(double celsius) => _value = celsius;

        public void Clear() => _value = null;

        public bool TryRead(out double celsius, out string error)
        {
            if (_value.HasValue)
            {
                celsius = _value.Value;
                error = null;
                return true;
            }

            celsius = 0;
            error = "no manual value has been set";
            return false;
        }

        public string Describe()
        {
            return _value.HasValue
                ? _value.Value.ToString("0.#", CultureInfo.InvariantCulture) + " C"
                : "not set";
        }
    }
}
