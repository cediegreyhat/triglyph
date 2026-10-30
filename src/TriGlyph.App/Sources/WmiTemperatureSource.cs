using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Management;
using TriGlyph.Core;

namespace TriGlyph.App.Sources
{
    internal enum WmiFlavour
    {
        /// <summary>LibreHardwareMonitor or OpenHardwareMonitor WMI provider.</summary>
        HardwareMonitor,

        /// <summary>root\WMI MSAcpi_ThermalZoneTemperature. Often absent on desktops.</summary>
        AcpiThermalZone,
        /// <summary>root\CIMV2 Win32_TemperatureProbe. Present on some boards/OEM systems.</summary>
        WindowsTemperatureProbe
    }

    /// <summary>
    /// Reads CPU temperature over WMI. No kernel driver and no elevation: if the user
    /// already runs LibreHardwareMonitor or OpenHardwareMonitor we get its accurate
    /// package reading, otherwise we fall back to the ACPI thermal zone, which is often
    /// absent and usually measures the board rather than the core - the UI says so.
    /// </summary>
    internal sealed class WmiTemperatureSource : ITemperatureSource
    {
        private readonly WmiFlavour _flavour;
        private readonly string _wmiNamespace;

        public string Name { get; }

        /// <summary>Set when the source had to settle for a less accurate reading.</summary>
        public string Caveat { get; private set; }

        public WmiTemperatureSource(string name, string wmiNamespace, WmiFlavour flavour)
        {
            Name = name;
            _wmiNamespace = wmiNamespace;
            _flavour = flavour;
        }

        public bool TryRead(out double celsius, out string error)
        {
            Caveat = null;

            try
            {
                return _flavour == WmiFlavour.AcpiThermalZone
                    ? TryReadAcpi(out celsius, out error)
                    : _flavour == WmiFlavour.WindowsTemperatureProbe
                        ? TryReadWindowsProbe(out celsius, out error)
                        : TryReadHardwareMonitor(out celsius, out error);
            }
            catch (ManagementException ex)
            {
                celsius = 0;
                error = "WMI unavailable: " + ex.Message;
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                celsius = 0;
                error = "access denied: " + ex.Message;
                return false;
            }
        }

        private bool TryReadHardwareMonitor(out double celsius, out string error)
        {
            celsius = 0;
            error = null;

            var readings = new List<(string Label, double Value)>();

            using (var searcher = new ManagementObjectSearcher(
                _wmiNamespace,
                "SELECT Name, Value FROM Sensor WHERE SensorType='Temperature'"))
            {
                foreach (ManagementBaseObject sensor in searcher.Get())
                {
                    using (sensor)
                    {
                        object raw = sensor["Value"];
                        if (raw == null)
                        {
                            continue;
                        }

                        double value;
                        try
                        {
                            value = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                        }
                        catch (FormatException)
                        {
                            continue;
                        }
                        catch (InvalidCastException)
                        {
                            continue;
                        }

                        if (double.IsNaN(value) || value < 0 || value > 150)
                        {
                            continue;
                        }

                        readings.Add((Convert.ToString(sensor["Name"]) ?? "", value));
                    }
                }
            }

            if (readings.Count == 0)
            {
                error = _wmiNamespace + " exposed no usable temperature sensor";
                return false;
            }

            // Prefer the package/Tctl/Tdie reading, which is what the cooler is cooling.
            string[] preferred = { "package", "tctl", "tdie", "cpu die", "core max", "ccd" };
            foreach (string want in preferred)
            {
                var hit = readings
                    .Where(r => r.Label.ToLowerInvariant().Contains(want))
                    .OrderByDescending(r => r.Value)
                    .FirstOrDefault();

                if (hit.Label != null)
                {
                    celsius = hit.Value;
                    return true;
                }
            }

            var cpuOnly = readings
                .Where(r => r.Label.ToLowerInvariant().Contains("cpu"))
                .OrderByDescending(r => r.Value)
                .FirstOrDefault();

            if (cpuOnly.Label != null)
            {
                celsius = cpuOnly.Value;
                return true;
            }

            celsius = readings.OrderByDescending(r => r.Value).First().Value;
            Caveat = "no CPU-labelled sensor found; using the hottest temperature reported";
            return true;
        }

        private bool TryReadWindowsProbe(out double celsius, out string error)
    {
        celsius = 0;
        error = null;

        double best = double.MinValue;

        try
        {
            using (var searcher = new ManagementObjectSearcher(
                "root\\CIMV2",
                "SELECT CurrentReading FROM Win32_TemperatureProbe"))
            {
                foreach (ManagementBaseObject probe in searcher.Get())
                {
                    using (probe)
                    {
                        object raw = probe["CurrentReading"];
                        if (raw == null)
                        {
                            continue;
                        }

                        double value;
                        try
                        {
                            value = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                        }
                        catch (FormatException)
                        {
                            continue;
                        }
                        catch (InvalidCastException)
                        {
                            continue;
                        }

                        // Win32_TemperatureProbe reports tenths of a degree on most
                        // implementations; normalise anything implausibly large.
                        if (value > 1000)
                        {
                            value /= 10.0;
                        }

                        if (value >= 0 && value <= 150 && value > best)
                        {
                            best = value;
                        }
                    }
                }
            }
        }
        catch (ManagementException ex)
        {
            celsius = 0;
            error = "WMI unavailable: " + ex.Message;
            return false;
        }

        if (best == double.MinValue)
        {
            error = "Win32_TemperatureProbe returned nothing usable " +
                    "(common; many boards do not populate it)";
            return false;
        }

        celsius = best;
        Caveat = "Win32_TemperatureProbe usually measures the board, not the CPU core";
        return true;
    }

    private bool TryReadAcpi(out double celsius, out string error)
        {
            celsius = 0;
            error = null;

            double best = double.MinValue;

            using (var searcher = new ManagementObjectSearcher(
                "root\\WMI",
                "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature"))
            {
                foreach (ManagementBaseObject zone in searcher.Get())
                {
                    using (zone)
                    {
                        object raw = zone["CurrentTemperature"];
                        if (raw == null)
                        {
                            continue;
                        }

                        // Reported in tenths of a degree Kelvin.
                        double deciKelvin = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                        double c = deciKelvin / 10.0 - 273.15;

                        if (c >= 0 && c <= 150 && c > best)
                        {
                            best = c;
                        }
                    }
                }
            }

            if (best == double.MinValue)
            {
                error = "MSAcpi_ThermalZoneTemperature returned nothing usable " +
                        "(common on desktop boards; run LibreHardwareMonitor instead)";
                return false;
            }

            celsius = best;
            Caveat = "ACPI thermal zone usually measures the motherboard, not the CPU core";
            return true;
        }
    }
}
