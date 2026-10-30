using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LibreHardwareMonitor.Hardware;
using TriGlyph.Core;

namespace TriGlyph.App.Sources
{
    /// <summary>
    /// Reads CPU temperature directly through LibreHardwareMonitorLib, so the user
    /// does not need to enable any WMI/remote-server option in LibreHardwareMonitor.
    /// </summary>
    internal sealed class LibreHardwareMonitorNativeSource : ITemperatureSource
    {
        private Computer _computer;
        private bool _opened;

        public string Name { get; } = "LibreHardwareMonitor";

        public bool TryRead(out double celsius, out string error)
        {
            celsius = 0;
            error = null;
            var readings = new List<(string Label, double Value)>();

            try
            {
                if (!_opened)
                {
                    _computer = new Computer { IsCpuEnabled = true };
                    _computer.Open();
                    _opened = true;
                }

                foreach (IHardware hardware in _computer.Hardware)
                {
                    Collect(hardware, readings);
                }
            }
            catch (Exception ex)
            {
                celsius = 0;
                error = "LibreHardwareMonitor native read failed: " + ex.Message;
                return false;
            }

            if (readings.Count == 0)
            {
                error = "LibreHardwareMonitor reported no usable CPU temperature sensor";
                return false;
            }

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
            return true;
        }

        private static void Collect(IHardware hardware, List<(string Label, double Value)> readings)
        {
            hardware.Update();

            foreach (ISensor sensor in hardware.Sensors)
            {
                if (sensor.SensorType != SensorType.Temperature || !sensor.Value.HasValue)
                {
                    continue;
                }

                double value = sensor.Value.Value;
                if (double.IsNaN(value) || value < 0 || value > 150)
                {
                    continue;
                }

                readings.Add((sensor.Name ?? "", value));
            }

            foreach (IHardware sub in hardware.SubHardware)
            {
                Collect(sub, readings);
            }
        }
    }
}
