using System;
using Microsoft.Win32;

namespace TriGlyph.App.Services
{
    /// <summary>
    /// Run-at-login via HKCU\...\Run. Per-user, so no elevation is ever needed - which
    /// is the whole point of avoiding a service or a driver.
    /// </summary>
    internal static class AutostartService
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "TriGlyph";

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    return key?.GetValue(ValueName) != null;
                }
            }
            catch (System.Security.SecurityException)
            {
                return false;
            }
        }

        public static bool TrySet(bool enabled, out string error)
        {
            error = null;

            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key == null)
                    {
                        error = "could not open the per-user Run key";
                        return false;
                    }

                    if (enabled)
                    {
                        string exe = Environment.ProcessPath;
                        if (string.IsNullOrEmpty(exe))
                        {
                            error = "could not determine the executable path";
                            return false;
                        }

                        key.SetValue(ValueName, "\"" + exe + "\"");
                    }
                    else
                    {
                        key.DeleteValue(ValueName, false);
                    }

                    return true;
                }
            }
            catch (System.Security.SecurityException ex)
            {
                error = "access denied writing the Run key: " + ex.Message;
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                error = "access denied writing the Run key: " + ex.Message;
                return false;
            }
        }
    }
}
