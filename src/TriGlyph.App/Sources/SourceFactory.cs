using System;
using System.Collections.Generic;
using TriGlyph.Core;

namespace TriGlyph.App.Sources
{
    /// <summary>
    /// Builds the temperature sources in the order the user configured. Names are
    /// matched case-insensitively so a hand-edited settings file still works.
    /// </summary>
    internal static class SourceFactory
    {
        public static List<ITemperatureSource> Build(AppSettings settings, ManualTemperatureSource manual)
        {
            var byName = new Dictionary<string, ITemperatureSource>(StringComparer.OrdinalIgnoreCase)
            {
                [TemperatureSourceNames.LibreHardwareMonitor] = new WmiTemperatureSource(
                    TemperatureSourceNames.LibreHardwareMonitor, "root\\LibreHardwareMonitor", WmiFlavour.HardwareMonitor),
                [TemperatureSourceNames.OpenHardwareMonitor] = new WmiTemperatureSource(
                    TemperatureSourceNames.OpenHardwareMonitor, "root\\OpenHardwareMonitor", WmiFlavour.HardwareMonitor),
                [TemperatureSourceNames.HwInfoSharedMemory] = new HwInfoSharedMemorySource(),
                [TemperatureSourceNames.AcpiThermalZone] = new WmiTemperatureSource(
                    TemperatureSourceNames.AcpiThermalZone, "root\\WMI", WmiFlavour.AcpiThermalZone),
                [TemperatureSourceNames.WindowsTemperatureProbe] = new WmiTemperatureSource(
                    TemperatureSourceNames.WindowsTemperatureProbe, "root\\CIMV2", WmiFlavour.WindowsTemperatureProbe),
                [TemperatureSourceNames.Manual] = manual
            };

            var ordered = new List<ITemperatureSource>();
            IReadOnlyList<string> wanted = settings?.SourcePriority ?? (IReadOnlyList<string>)new List<string>();

            foreach (string name in wanted)
            {
                if (name != null && byName.TryGetValue(name, out ITemperatureSource source) && !ordered.Contains(source))
                {
                    ordered.Add(source);
                }
            }

            if (!ordered.Contains(manual))
            {
                ordered.Add(manual);
            }

            return ordered;
        }

        public static IReadOnlyList<string> KnownNames => TemperatureSourceNames.All;
    }
}
