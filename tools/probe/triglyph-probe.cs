// TriGlyph.cs - replacement for the Jungle Leopard / TZMRIT "JL DGT" vendor tool.
//
// Drives the 3-digit temperature panel on KF420 DGT / KF620 DGT CPU coolers.
// The panel is a plain USB HID peripheral: its own firmware renders the digits,
// the host just pushes the number via HID *feature* report 7 (64 bytes total).
//
//   payload = [ 7, 0xFF, 0xFF, hundreds, tens, ones, 0 * 58 ]
//
// No kernel driver, no admin, no vendor DLLs. Targets .NET Framework 4.5 so it
// runs on stock Windows with nothing installed.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

static class Hid
{
    public const uint DIGCF_PRESENT = 0x02;
    public const uint DIGCF_DEVICEINTERFACE = 0x10;
    public const uint GENERIC_READ = 0x80000000;
    public const uint GENERIC_WRITE = 0x40000000;
    public const uint FILE_SHARE_READ = 0x01;
    public const uint FILE_SHARE_WRITE = 0x02;
    public const uint OPEN_EXISTING = 3;
    public const uint FILE_FLAG_OVERLAPPED = 0x40000000;

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDD_ATTRIBUTES
    {
        public int Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    // HIDP_CAPS from hidpi.h. Reserved[17] must stay inline or the field
    // offsets after it (NumberLinkCollectionNodes etc.) are wrong.
    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_CAPS
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [DllImport("hid.dll")]
    public static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll")]
    public static extern bool HidD_GetAttributes(IntPtr h, ref HIDD_ATTRIBUTES a);
    [DllImport("hid.dll")]
    public static extern bool HidD_GetPreparsedData(IntPtr h, out IntPtr pp);
    [DllImport("hid.dll")]
    public static extern bool HidD_FreePreparsedData(IntPtr pp);
    [DllImport("hid.dll")]
    public static extern int HidP_GetCaps(IntPtr pp, ref HIDP_CAPS caps);
    [DllImport("hid.dll", CharSet = CharSet.Unicode)]
    public static extern bool HidD_GetProductString(IntPtr h, StringBuilder buf, int len);
    [DllImport("hid.dll", CharSet = CharSet.Unicode)]
    public static extern bool HidD_GetManufacturerString(IntPtr h, StringBuilder buf, int len);
    [DllImport("hid.dll")]
    public static extern bool HidD_SetFeature(IntPtr h, byte[] buf, int len);
    [DllImport("hid.dll")]
    public static extern bool HidD_GetFeature(IntPtr h, byte[] buf, int len);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr enumerator, IntPtr hwnd, uint flags);
    [DllImport("setupapi.dll")]
    public static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid g, uint index, ref SP_DEVICE_INTERFACE_DATA data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
    public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int detailSize, out int required, IntPtr devInfo);
    [DllImport("setupapi.dll")]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr templ);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr h);
}

sealed class HidDevice
{
    public string Path;
    public ushort Vid;
    public ushort Pid;
    public ushort UsagePage;
    public ushort Usage;
    public ushort FeatureReportLength;
    public string Product = "";
    public string Manufacturer = "";

    public bool LooksLikeCoolerPanel
    {
        // Vendor-defined usage page + a 64-byte feature report is the signature
        // shared by every SKU in this OEM family. VID 1a2c (China Resource
        // Semico) is the factory profile but we do not require it, so rebadged
        // units with a different VID still get picked up.
        get { return UsagePage == 0xFF00 && FeatureReportLength == 64; }
    }

    public override string ToString()
    {
        return string.Format(CultureInfo.InvariantCulture,
            "VID_{0:X4}&PID_{1:X4}  usagePage=0x{2:X4} usage=0x{3:X4}  featLen={4}  {5}",
            Vid, Pid, UsagePage, Usage, FeatureReportLength, Product);
    }

    public IntPtr Open()
    {
        return Hid.CreateFile(Path,
            Hid.GENERIC_READ | Hid.GENERIC_WRITE,
            Hid.FILE_SHARE_READ | Hid.FILE_SHARE_WRITE,
            IntPtr.Zero, Hid.OPEN_EXISTING, 0, IntPtr.Zero);
    }
}

static class Program
{
    const byte ReportId = 7;
    const ushort TargetVid = 0x1A2C;
    const ushort TargetPid = 0x4184;

    static IntPtr INVALID_HANDLE = new IntPtr(-1);

    static List<HidDevice> Enumerate()
    {
        var found = new List<HidDevice>();
        Guid hidGuid;
        Hid.HidD_GetHidGuid(out hidGuid);

        IntPtr set = Hid.SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero,
            Hid.DIGCF_PRESENT | Hid.DIGCF_DEVICEINTERFACE);
        if (set == INVALID_HANDLE) return found;

        try
        {
            var iface = new Hid.SP_DEVICE_INTERFACE_DATA();
            iface.cbSize = Marshal.SizeOf(typeof(Hid.SP_DEVICE_INTERFACE_DATA));

            for (uint i = 0; Hid.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, i, ref iface); i++)
            {
                int required;
                Hid.SetupDiGetDeviceInterfaceDetail(set, ref iface, IntPtr.Zero, 0, out required, IntPtr.Zero);
                if (required <= 0) continue;

                IntPtr detail = Marshal.AllocHGlobal(required);
                try
                {
                    // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA: 8 on x64, 6 on x86.
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!Hid.SetupDiGetDeviceInterfaceDetail(set, ref iface, detail, required, out required, IntPtr.Zero))
                        continue;

                    string path = Marshal.PtrToStringUni(new IntPtr(detail.ToInt64() + 4));
                    if (string.IsNullOrEmpty(path)) continue;

                    IntPtr h = Hid.CreateFile(path, 0,
                        Hid.FILE_SHARE_READ | Hid.FILE_SHARE_WRITE,
                        IntPtr.Zero, Hid.OPEN_EXISTING, 0, IntPtr.Zero);
                    if (h == INVALID_HANDLE) continue;

                    try
                    {
                        var dev = new HidDevice();
                        dev.Path = path;

                        var attr = new Hid.HIDD_ATTRIBUTES();
                        attr.Size = Marshal.SizeOf(typeof(Hid.HIDD_ATTRIBUTES));
                        if (Hid.HidD_GetAttributes(h, ref attr))
                        {
                            dev.Vid = attr.VendorID;
                            dev.Pid = attr.ProductID;
                        }

                        var sb = new StringBuilder(256);
                        if (Hid.HidD_GetProductString(h, sb, 256)) dev.Product = sb.ToString();
                        sb = new StringBuilder(256);
                        if (Hid.HidD_GetManufacturerString(h, sb, 256)) dev.Manufacturer = sb.ToString();

                        IntPtr pp;
                        if (Hid.HidD_GetPreparsedData(h, out pp))
                        {
                            try
                            {
                                var caps = new Hid.HIDP_CAPS();
                                if (Hid.HidP_GetCaps(pp, ref caps) >= 0)
                                {
                                    dev.UsagePage = caps.UsagePage;
                                    dev.Usage = caps.Usage;
                                    dev.FeatureReportLength = caps.FeatureReportByteLength;
                                }
                            }
                            finally { Hid.HidD_FreePreparsedData(pp); }
                        }

                        found.Add(dev);
                    }
                    finally { Hid.CloseHandle(h); }
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { Hid.SetupDiDestroyDeviceInfoList(set); }

        return found;
    }

    static HidDevice Pick(List<HidDevice> all)
    {
        // 1. exact known identity
        foreach (var d in all)
            if (d.Vid == TargetVid && d.Pid == TargetPid && d.FeatureReportLength == 64) return d;

        // 2. any VID 1a2c interface with the right shape
        foreach (var d in all)
            if (d.Vid == TargetVid && d.LooksLikeCoolerPanel) return d;

        // 3. structural match only - catches rebadged units
        foreach (var d in all)
            if (d.LooksLikeCoolerPanel) return d;

        return null;
    }

    static bool SetTemp(HidDevice dev, int celsius)
    {
        if (celsius < 0) celsius = 0;
        if (celsius > 150) celsius = 150;

        byte[] buf = new byte[64];
        buf[0] = ReportId;
        buf[1] = 0xFF;
        buf[2] = 0xFF;
        buf[3] = (byte)(celsius / 100);
        buf[4] = (byte)((celsius / 10) % 10);
        buf[5] = (byte)(celsius % 10);

        IntPtr h = dev.Open();
        if (h == INVALID_HANDLE)
        {
            Console.Error.WriteLine("open failed: " + dev.Path);
            return false;
        }
        try { return Hid.HidD_SetFeature(h, buf, buf.Length); }
        finally { Hid.CloseHandle(h); }
    }

    // CPU package temperature. Tries LibreHardwareMonitor/OpenHardwareMonitor WMI
    // first (best fidelity, but those must be running), then the ACPI thermal
    // zone (often needs admin and reports the board zone, not the core).
    static int? ReadCpuTemp()
    {
        string[] namespaces = { "root\\LibreHardwareMonitor", "root\\OpenHardwareMonitor" };
        foreach (string ns in namespaces)
        {
            try
            {
                var q = new ManagementObjectSearcher(ns,
                    "SELECT Name, Value FROM Sensor WHERE SensorType='Temperature'");
                foreach (ManagementObject mo in q.Get())
                {
                    string name = Convert.ToString(mo["Name"]);
                    object val = mo["Value"];
                    if (val == null) continue;
                    if (name.IndexOf("CPU", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    return (int)Math.Round(Convert.ToDouble(val, CultureInfo.InvariantCulture));
                }
            }
            catch { /* namespace not present - keep going */ }
        }

        try
        {
            var q = new ManagementObjectSearcher("root\\WMI",
                "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
            foreach (ManagementObject mo in q.Get())
            {
                object val = mo["CurrentTemperature"];
                if (val == null) continue;
                double deciKelvin = Convert.ToDouble(val, CultureInfo.InvariantCulture);
                return (int)Math.Round(deciKelvin / 10.0 - 273.15);
            }
        }
        catch { /* ACPI thermal zone unavailable */ }

        return null;
    }

    static int Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "--probe";

        if (mode == "-h" || mode == "--help" || mode == "/?")
        {
            Console.WriteLine("TriGlyph - KF420/KF620 DGT temperature panel driver");
            Console.WriteLine();
            Console.WriteLine("  --probe           list every HID interface and flag candidates");
            Console.WriteLine("  --set <celsius>   push one temperature reading to the panel");
            Console.WriteLine("  --watch [secs]    loop: read CPU temp, push to panel (default 2s)");
            return 0;
        }

        var all = Enumerate();

        if (mode == "--probe")
        {
            Console.WriteLine("HID interfaces found: " + all.Count);
            foreach (var d in all)
                Console.WriteLine("  " + (d.LooksLikeCoolerPanel ? "* " : "  ") + d);

            var pick = Pick(all);
            Console.WriteLine();
            if (pick == null)
            {
                Console.WriteLine("No cooler panel candidate. If the display works with the");
                Console.WriteLine("vendor tool, run this while that tool is open and re-check.");
                return 2;
            }
            Console.WriteLine("Selected: " + pick);
            return 0;
        }

        if (mode == "--set")
        {
            if (args.Length < 2) { Console.Error.WriteLine("usage: --set <celsius>"); return 1; }
            int t = int.Parse(args[1], CultureInfo.InvariantCulture);
            var pick = Pick(all);
            if (pick == null) { Console.Error.WriteLine("no panel found"); return 2; }
            Console.WriteLine("set " + t + "C -> " + (SetTemp(pick, t) ? "ok" : "FAILED"));
            return 0;
        }

        if (mode == "--watch")
        {
            int interval = 2;
            if (args.Length > 1) int.TryParse(args[1], out interval);
            if (interval < 1) interval = 1;

            var pick = Pick(all);
            if (pick == null) { Console.Error.WriteLine("no panel found"); return 2; }
            Console.WriteLine("watching: " + pick);

            int last = -1;
            while (true)
            {
                int? t = ReadCpuTemp();
                if (t == null)
                {
                    Console.Error.WriteLine("no temperature source available " +
                        "(start LibreHardwareMonitor, or run as admin for the ACPI zone)");
                }
                else if (t.Value != last)
                {
                    bool ok = SetTemp(pick, t.Value);
                    Console.WriteLine(DateTime.Now.ToString("HH:mm:ss") + "  " + t.Value + "C  " +
                        (ok ? "ok" : "FAILED"));
                    if (ok) last = t.Value;
                }
                Thread.Sleep(interval * 1000);
            }
        }

        Console.Error.WriteLine("unknown mode: " + mode);
        return 1;
    }
}
