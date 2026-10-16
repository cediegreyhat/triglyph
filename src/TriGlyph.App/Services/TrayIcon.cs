using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using TriGlyph.Core;

namespace TriGlyph.App.Services
{
    /// <summary>
    /// Tray presence. The icon is drawn at runtime and recoloured by temperature, so
    /// the tray itself becomes a glanceable readout - and we avoid shipping an .ico,
    /// which cannot be embedded into the apphost when building off-Windows.
    /// </summary>
    internal sealed class TrayIcon : IDisposable
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr handle);

        private readonly App _app;
        private readonly MainWindow _window;
        private readonly TelemetryHost _host;

        private NotifyIcon _notifyIcon;
        private IntPtr _iconHandle = IntPtr.Zero;

        public TrayIcon(App app, MainWindow window, TelemetryHost host)
        {
            _app = app;
            _window = window;
            _host = host;
        }

        public void Install()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Open control panel", null, (s, e) => ShowWindow());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (s, e) => _app.ExitApplication());

            _notifyIcon = new NotifyIcon
            {
                ContextMenuStrip = menu,
                Visible = true,
                Text = "TriGlyph"
            };

            _notifyIcon.DoubleClick += (s, e) => ShowWindow();

            Update(null, true);
            _host.Ticked += _ => Update(_host.State.Display, _host.State.IsStale);
        }

        public void Update(string display, bool stale)
        {
            if (_notifyIcon == null)
            {
                return;
            }

            _notifyIcon.Text = Trim($"TriGlyph {display}\u00B0 {(stale ? "stale" : "live")}", 63);

            Color colour;
            if (stale || display == "--")
            {
                colour = Color.FromArgb(255, 120, 130, 140);
            }
            else if (int.TryParse(display, out int celsius))
            {
                colour = ColourFor(celsius);
            }
            else
            {
                colour = Color.FromArgb(255, 120, 130, 140);
            }

            SetIcon(colour);
        }

        private static Color ColourFor(int celsius)
        {
            if (celsius < 50) return Color.FromArgb(255, 52, 211, 153);
            if (celsius < 70) return Color.FromArgb(255, 251, 191, 36);
            if (celsius < 85) return Color.FromArgb(255, 251, 146, 60);
            return Color.FromArgb(255, 248, 113, 113);
        }

        private static string Trim(string value, int max)
        {
            return value.Length <= max ? value : value.Substring(0, max);
        }

        private void SetIcon(Color colour)
        {
            using (var bitmap = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);

                    using (var glow = new SolidBrush(Color.FromArgb(70, colour)))
                    {
                        g.FillEllipse(glow, 1, 1, 30, 30);
                    }

                    using (var core = new SolidBrush(colour))
                    {
                        g.FillEllipse(core, 8, 8, 16, 16);
                    }

                    using (var ring = new Pen(Color.FromArgb(220, 255, 255, 255), 1.5f))
                    {
                        g.DrawEllipse(ring, 8, 8, 16, 16);
                    }
                }

                IntPtr handle = bitmap.GetHicon();
                try
                {
                    var icon = Icon.FromHandle(handle);
                    _notifyIcon.Icon = icon;

                    if (_iconHandle != IntPtr.Zero)
                    {
                        DestroyIcon(_iconHandle);
                    }

                    _iconHandle = handle;
                }
                catch (ArgumentException)
                {
                    DestroyIcon(handle);
                }
            }
        }

        private void ShowWindow()
        {
            _window.Show();
            _window.WindowState = System.Windows.WindowState.Normal;
            _window.Activate();
        }

        public void Uninstall()
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }

            if (_iconHandle != IntPtr.Zero)
            {
                DestroyIcon(_iconHandle);
                _iconHandle = IntPtr.Zero;
            }
        }

        public void Dispose() => Uninstall();
    }
}
