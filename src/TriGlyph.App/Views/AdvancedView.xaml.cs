using System;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using TriGlyph.App.Services;
using TriGlyph.Core;

namespace TriGlyph.App.Views
{
    public partial class AdvancedView : UserControl
    {
        private TelemetryHost _host;
        private App _app;

        public AdvancedView()
        {
            InitializeComponent();
        }

        public void Initialize(TelemetryHost host, App app)
        {
            _host = host;
            _app = app;
            DataContext = host.State;

            ArmCheck.IsChecked = app.Settings.UnsafeWritesArmed;
            host.State.PropertyChanged += (s, e) => Refresh();
            Loaded += (s, e) => Refresh();
        }

        private void Refresh()
        {
            if (_host == null)
            {
                return;
            }

            HidReportDescriptor descriptor = _host.Descriptor;
            DescriptorSummary.Text = descriptor == null
                ? "not read yet - open this page with a panel connected"
                : descriptor.Describe();

            RawHexText.Text = _host.RawDescriptor == null
                ? ""
                : BitConverter.ToString(_host.RawDescriptor).Replace("-", " ");
        }

        private void Reread_Click(object sender, RoutedEventArgs e)
        {
            _host.ReadDescriptor();
            Refresh();
        }

        private void Arm_Changed(object sender, RoutedEventArgs e)
        {
            if (_app == null)
            {
                return;
            }

            _app.Settings.UnsafeWritesArmed = ArmCheck.IsChecked == true;
            _app.SaveSettings();
            _host.State.AppendLog(_app.Settings.UnsafeWritesArmed
                ? "unsafe writes ARMED"
                : "unsafe writes disarmed");
        }

        private void Fill_Click(object sender, RoutedEventArgs e)
        {
            // Pad the known temperature frame out to the full 64-byte report.
            var bytes = new StringBuilder("07 FF FF 00 2D 00");
            for (int i = 6; i < 64; i++)
            {
                bytes.Append(" 00");
            }

            PayloadBox.Text = bytes.ToString();
        }

        private bool TryParsePayload(int reportId, out byte[] payload, out string error)
        {
            payload = null;
            error = null;

            string[] parts = PayloadBox.Text
                .Split(new[] { ' ', ',', '-', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0)
            {
                error = "payload is empty";
                return false;
            }

            var bytes = new byte[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!byte.TryParse(parts[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[i]))
                {
                    error = $"'{parts[i]}' is not a hex byte";
                    return false;
                }
            }

            if (bytes[0] != reportId)
            {
                error = $"payload starts with report id {bytes[0]} but the report id box says {reportId}";
                return false;
            }

            payload = bytes;
            return true;
        }

        private void Send_Click(object sender, RoutedEventArgs e)
        {
            int reportId = 7;
            if (!int.TryParse(ReportIdBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out reportId)
                || reportId < 0 || reportId > 255)
            {
                ConsoleResult.Text = "report id must be 0-255";
                return;
            }

            if (!TryParsePayload(reportId, out byte[] payload, out string parseError))
            {
                ConsoleResult.Text = parseError;
                return;
            }

            HidReportInfo info = _host.Descriptor?.Find(reportId, HidReportKind.Feature)
                                 ?? new HidReportInfo
                                 {
                                     ReportId = reportId,
                                     Kind = HidReportKind.Feature,
                                     BitLength = (payload.Length - 1) * 8,
                                     TransferByteLength = payload.Length
                                 };

            ConsoleDecision decision = _host.ConsolePolicy.EvaluateWrite(info, payload);
            if (!decision.Allowed)
            {
                ConsoleResult.Text = "blocked: " + decision.Reason;
                _host.State.AppendLog("console: blocked write to report " + reportId);
                return;
            }

            bool ok = _host.TrySendRaw(payload, out string error);
            ConsoleResult.Text = ok ? $"sent {payload.Length} bytes to report {reportId}" : "failed: " + error;
            _host.State.AppendLog(ConsoleResult.Text);
        }

        private void Read_Click(object sender, RoutedEventArgs e)
        {
            int reportId = 7;
            if (!int.TryParse(ReportIdBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out reportId))
            {
                ConsoleResult.Text = "report id must be a number";
                return;
            }

            int length = _host.Descriptor?.Find(reportId, HidReportKind.Feature)?.TransferByteLength ?? 64;
            var buffer = new byte[length];
            buffer[0] = (byte)reportId;

            bool ok = _host.TryReadRaw(buffer, out string error);
            ConsoleResult.Text = ok
                ? "read: " + BitConverter.ToString(buffer).Replace("-", " ")
                : "failed: " + error;
            _host.State.AppendLog("console: read report " + reportId);
        }
    }
}
