using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using TriGlyph.Core;

namespace TriGlyph.App.Interop
{
    /// <summary>
    /// Walks the HID device interfaces Windows exposes and turns each one into a
    /// HidDeviceCandidate. The matcher in Core then decides which is the panel.
    /// </summary>
    internal static class HidEnumerator
    {
        public static List<HidDeviceCandidate> Enumerate()
        {
            var found = new List<HidDeviceCandidate>();

            Guid hidGuid;
            HidNative.HidD_GetHidGuid(out hidGuid);

            IntPtr set = HidNative.SetupDiGetClassDevs(
                ref hidGuid, IntPtr.Zero, IntPtr.Zero,
                HidNative.DIGCF_PRESENT | HidNative.DIGCF_DEVICEINTERFACE);

            if (set == HidNative.INVALID_HANDLE_VALUE)
            {
                return found;
            }

            try
            {
                var interfaceData = new HidNative.SP_DEVICE_INTERFACE_DATA();
                interfaceData.cbSize = Marshal.SizeOf(typeof(HidNative.SP_DEVICE_INTERFACE_DATA));

                for (uint index = 0; HidNative.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, index, ref interfaceData); index++)
                {
                    int required;
                    HidNative.SetupDiGetDeviceInterfaceDetail(set, ref interfaceData, IntPtr.Zero, 0, out required, IntPtr.Zero);
                    if (required <= 0)
                    {
                        continue;
                    }

                    IntPtr detail = Marshal.AllocHGlobal(required);
                    try
                    {
                        // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA is 8 on x64, 6 on x86.
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);

                        if (!HidNative.SetupDiGetDeviceInterfaceDetail(set, ref interfaceData, detail, required, out required, IntPtr.Zero))
                        {
                            continue;
                        }

                        string path = Marshal.PtrToStringUni(new IntPtr(detail.ToInt64() + 4));
                        if (string.IsNullOrEmpty(path))
                        {
                            continue;
                        }

                        HidDeviceCandidate candidate = Describe(path);
                        if (candidate != null)
                        {
                            found.Add(candidate);
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(detail);
                    }
                }
            }
            finally
            {
                HidNative.SetupDiDestroyDeviceInfoList(set);
            }

            return found;
        }

        private static HidDeviceCandidate Describe(string path)
        {
            // Query-only open: we need the caps, not write access.
            IntPtr handle = HidNative.CreateFile(path, 0,
                HidNative.FILE_SHARE_READ | HidNative.FILE_SHARE_WRITE,
                IntPtr.Zero, HidNative.OPEN_EXISTING, 0, IntPtr.Zero);

            if (handle == HidNative.INVALID_HANDLE_VALUE)
            {
                return null;
            }

            try
            {
                var candidate = new HidDeviceCandidate
                {
                    Path = path,
                    InterfaceNumber = ParseInterfaceNumber(path)
                };

                var attributes = new HidNative.HIDD_ATTRIBUTES();
                attributes.Size = Marshal.SizeOf(typeof(HidNative.HIDD_ATTRIBUTES));
                if (HidNative.HidD_GetAttributes(handle, ref attributes))
                {
                    candidate.VendorId = attributes.VendorID;
                    candidate.ProductId = attributes.ProductID;
                }

                var text = new StringBuilder(256);
                if (HidNative.HidD_GetProductString(handle, text, 256))
                {
                    candidate.Product = text.ToString();
                }

                text = new StringBuilder(256);
                if (HidNative.HidD_GetManufacturerString(handle, text, 256))
                {
                    candidate.Manufacturer = text.ToString();
                }

                IntPtr preparsed;
                if (HidNative.HidD_GetPreparsedData(handle, out preparsed))
                {
                    try
                    {
                        var caps = new HidNative.HIDP_CAPS();
                        if (HidNative.HidP_GetCaps(preparsed, ref caps) >= 0)
                        {
                            candidate.UsagePage = caps.UsagePage;
                            candidate.Usage = caps.Usage;
                            candidate.FeatureReportLength = caps.FeatureReportByteLength;
                        }
                    }
                    finally
                    {
                        HidNative.HidD_FreePreparsedData(preparsed);
                    }
                }

                return candidate;
            }
            finally
            {
                HidNative.CloseHandle(handle);
            }
        }

        /// <summary>Interface numbers appear in the device path as "&amp;mi_01".</summary>
        private static int ParseInterfaceNumber(string path)
        {
            int at = path.IndexOf("&mi_", StringComparison.OrdinalIgnoreCase);
            if (at < 0 || at + 5 > path.Length)
            {
                return 0;
            }

            string hex = path.Substring(at + 4, 2);
            return int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int value)
                ? value
                : 0;
        }
    }
}
