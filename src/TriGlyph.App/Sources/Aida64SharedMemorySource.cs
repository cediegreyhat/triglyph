using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using TriGlyph.Core;

namespace TriGlyph.App.Sources
{
    /// <summary>
    /// Reads CPU temperature from AIDA64's external-application export.
    /// No driver and no elevation: AIDA64 does the hardware access and publishes
    /// its already-measured values through a named shared-memory block (or the
    /// HKCU registry mirror). The block exists only while AIDA64 is running.
    /// </summary>
    internal sealed class Aida64SharedMemorySource : ITemperatureSource
    {
        private const string SharedMemoryName = "AIDA64_SensorValues";
        private const string RegistryKeyPath = @"Software\FinalWire\AIDA64\SensorValues";

        private static readonly Regex EntryRegex = new Regex(
            @"<(?<kind>sys|temp|fan|duty|volt|curr|pwr)>" +
            @"<id>(?<id>[^<]*)</id>" +
            @"<label>(?<label>[^<]*)</label>" +
            @"<value>(?<value>[^<]*)</value>" +
            @"</\k<kind>>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public string Name { get; } = "AIDA64";

        public string Caveat { get; private set; }

        public bool TryRead(out double celsius, out string error)
        {
            Caveat = null;
            celsius = 0;
            error = null;

            var readings = new List<(string Id, string Label, double Value)>();

            string payload = ReadSharedMemory();
            if (payload != null)
            {
                foreach (Match match in EntryRegex.Matches(payload))
                {
                    if (!string.Equals(match.Groups["kind"].Value, "temp", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    AddReading(readings, match.Groups["id"].Value, match.Groups["label"].Value, match.Groups["value"].Value);
                }
            }

            if (readings.Count == 0)
            {
                readings = ReadRegistry();
            }

            if (readings.Count == 0)
            {
                error = "AIDA64 is not publishing sensor values (enable External Applications, or start AIDA64)";
                return false;
            }

            // Prefer an explicitly CPU-labelled temperature.
            var cpuReadings = readings
                .Where(r => r.Id.StartsWith("TCPU", StringComparison.OrdinalIgnoreCase) ||
                            r.Label.IndexOf("cpu", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            if (cpuReadings.Count > 0)
            {
                celsius = cpuReadings.OrderByDescending(r => r.Value).First().Value;
                return true;
            }

            celsius = readings.OrderByDescending(r => r.Value).First().Value;
            Caveat = "no CPU-labelled sensor found; using the hottest AIDA64 temperature";
            return true;
        }

        private static void AddReading(List<(string Id, string Label, double Value)> readings,
            string id, string label, string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            double value;
            try
            {
                value = Convert.ToDouble(raw.Trim().Replace(",", "."), CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
                return;
            }
            catch (OverflowException)
            {
                return;
            }

            if (double.IsNaN(value) || value < 0 || value > 150)
            {
                return;
            }

            readings.Add((id ?? "", label ?? "", value));
        }

        private static List<(string Id, string Label, double Value)> ReadRegistry()
        {
            var readings = new List<(string Id, string Label, double Value)>();
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath))
                {
                    if (key == null)
                    {
                        return readings;
                    }

                    var names = key.GetValueNames();
                    var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string name in names)
                    {
                        values[name] = Convert.ToString(key.GetValue(name)) ?? "";
                    }

                    foreach (string name in names)
                    {
                        if (!name.StartsWith("Value.", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        string id = name.Substring("Value.".Length);
                        if (string.IsNullOrWhiteSpace(id) || !id.StartsWith("T", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        string raw = values[name];
                        string label = values.TryGetValue("Label." + id, out string found) && !string.IsNullOrWhiteSpace(found)
                            ? found
                            : id;

                        AddReading(readings, id, label, raw);
                    }
                }
            }
            catch (Exception)
            {
                // Registry mirror unavailable; treat as no data.
            }

            return readings;
        }

        private static string ReadSharedMemory()
        {
            IntPtr handle = OpenFileMapping(FILE_MAP_READ, false, SharedMemoryName);
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            IntPtr address = IntPtr.Zero;
            try
            {
                address = MapViewOfFile(handle, FILE_MAP_READ, 0, 0, UIntPtr.Zero);
                if (address == IntPtr.Zero)
                {
                    return null;
                }

                MEMORY_BASIC_INFORMATION info;
                UIntPtr queried = VirtualQuery(address, out info, new UIntPtr((uint)Marshal.SizeOf(typeof(MEMORY_BASIC_INFORMATION))));
                if (queried == UIntPtr.Zero)
                {
                    return null;
                }

                long size = (long)info.RegionSize;
                if (size <= 0 || size > 1024 * 1024)
                {
                    size = 65536;
                }

                byte[] buffer = new byte[size];
                Marshal.Copy(address, buffer, 0, (int)size);

                int terminator = Array.IndexOf(buffer, (byte)0);
                if (terminator >= 0)
                {
                    size = terminator;
                }

                return Encoding.UTF8.GetString(buffer, 0, (int)size);
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                if (address != IntPtr.Zero)
                {
                    UnmapViewOfFile(address);
                }
                CloseHandle(handle);
            }
        }

        private const uint FILE_MAP_READ = 0x0004;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr OpenFileMapping(uint dwDesiredAccess, bool bInheritHandle, string lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr MapViewOfFile(IntPtr hFileMappingObject, uint dwDesiredAccess,
            uint dwFileOffsetHigh, uint dwFileOffsetLow, UIntPtr dwNumberOfBytesToMap);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool UnmapViewOfFile(IntPtr lpBaseAddress);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern UIntPtr VirtualQuery(IntPtr lpAddress, out MEMORY_BASIC_INFORMATION lpBuffer, UIntPtr dwLength);

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORY_BASIC_INFORMATION
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public uint AllocationProtect;
            public UIntPtr RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }
    }
}
