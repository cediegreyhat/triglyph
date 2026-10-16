using System;
using System.Runtime.InteropServices;
using System.Text;
using TriGlyph.Core;

namespace TriGlyph.App.Interop
{
    /// <summary>
    /// An open handle to one HID interface: feature reports in and out, plus the raw
    /// report descriptor that the vendor never documented.
    /// </summary>
    internal sealed class HidFeatureChannel : IDisposable
    {
        private IntPtr _handle = HidNative.INVALID_HANDLE_VALUE;

        public string Path { get; }
        public int FeatureReportLength { get; }

        private HidFeatureChannel(string path, IntPtr handle, int featureReportLength)
        {
            Path = path;
            _handle = handle;
            FeatureReportLength = featureReportLength;
        }

        public static bool TryOpen(string path, out HidFeatureChannel channel, out string error)
        {
            channel = null;
            error = null;

            IntPtr handle = HidNative.CreateFile(path,
                HidNative.GENERIC_READ | HidNative.GENERIC_WRITE,
                HidNative.FILE_SHARE_READ | HidNative.FILE_SHARE_WRITE,
                IntPtr.Zero, HidNative.OPEN_EXISTING, 0, IntPtr.Zero);

            if (handle == HidNative.INVALID_HANDLE_VALUE)
            {
                error = "CreateFile failed (win32 " + Marshal.GetLastWin32Error() + ")";
                return false;
            }

            int featureLength = 0;
            IntPtr preparsed;
            if (HidNative.HidD_GetPreparsedData(handle, out preparsed))
            {
                try
                {
                    var caps = new HidNative.HIDP_CAPS();
                    if (HidNative.HidP_GetCaps(preparsed, ref caps) >= 0)
                    {
                        featureLength = caps.FeatureReportByteLength;
                    }
                }
                finally
                {
                    HidNative.HidD_FreePreparsedData(preparsed);
                }
            }

            channel = new HidFeatureChannel(path, handle, featureLength);
            return true;
        }

        public bool TrySetFeature(byte[] payload, out string error)
        {
            error = null;

            if (_handle == HidNative.INVALID_HANDLE_VALUE)
            {
                error = "device is not open";
                return false;
            }

            if (!HidNative.HidD_SetFeature(_handle, payload, payload.Length))
            {
                error = "HidD_SetFeature failed (win32 " + Marshal.GetLastWin32Error() + ")";
                return false;
            }

            return true;
        }

        public bool TryGetFeature(byte[] buffer, out string error)
        {
            error = null;

            if (_handle == HidNative.INVALID_HANDLE_VALUE)
            {
                error = "device is not open";
                return false;
            }

            if (!HidNative.HidD_GetFeature(_handle, buffer, buffer.Length))
            {
                error = "HidD_GetFeature failed (win32 " + Marshal.GetLastWin32Error() + ")";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Fetch the raw HID report descriptor. This is the only way to see what the
        /// panel can actually do, since its 205-byte descriptor was never published.
        /// </summary>
        public bool TryReadReportDescriptor(out byte[] descriptor, out string error)
        {
            descriptor = null;
            error = null;

            if (_handle == HidNative.INVALID_HANDLE_VALUE)
            {
                error = "device is not open";
                return false;
            }

            const int MaxDescriptor = 4096;
            IntPtr buffer = Marshal.AllocHGlobal(MaxDescriptor);
            try
            {
                int returned;
                bool ok = HidNative.DeviceIoControl(
                    _handle,
                    HidNative.IOCTL_HID_GET_REPORT_DESCRIPTOR,
                    IntPtr.Zero, 0,
                    buffer, MaxDescriptor,
                    out returned,
                    IntPtr.Zero);

                if (!ok || returned <= 0)
                {
                    error = "IOCTL_HID_GET_REPORT_DESCRIPTOR failed (win32 " + Marshal.GetLastWin32Error() + ")";
                    return false;
                }

                descriptor = new byte[returned];
                Marshal.Copy(buffer, descriptor, 0, returned);
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        public void Dispose()
        {
            if (_handle != HidNative.INVALID_HANDLE_VALUE)
            {
                HidNative.CloseHandle(_handle);
                _handle = HidNative.INVALID_HANDLE_VALUE;
            }
        }
    }
}
