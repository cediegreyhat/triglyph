using System;

namespace TriGlyph.Core
{
    public sealed class ConsoleDecision
    {
        public bool Allowed { get; }
        public string Reason { get; }

        public ConsoleDecision(bool allowed, string reason)
        {
            Allowed = allowed;
            Reason = reason;
        }
    }

    /// <summary>
    /// Gatekeeper for the raw feature-report console.
    ///
    /// The panel is a black box: its 205-byte descriptor is undocumented, and the
    /// tzmrit project records that a bad USB state left the device unconfigured
    /// ("can't set config #1, error -32") until reboot. So arbitrary writes to
    /// unknown report ids are blocked unless the user explicitly arms them, and
    /// writes to input reports are refused outright - those are read-only by
    /// definition and a "write" is a sign the report was misidentified.
    /// </summary>
    public sealed class ReportConsolePolicy
    {
        public const int KnownTemperatureReportId = 7;

        public bool UnsafeWritesArmed { get; }

        public ReportConsolePolicy(bool unsafeWritesArmed)
        {
            UnsafeWritesArmed = unsafeWritesArmed;
        }

        public ConsoleDecision EvaluateWrite(HidReportInfo report, byte[] payload)
        {
            if (report == null)
            {
                return new ConsoleDecision(false, "no report selected");
            }

            if (payload == null)
            {
                return new ConsoleDecision(false, "no payload");
            }

            if (payload.Length != report.TransferByteLength)
            {
                return new ConsoleDecision(false,
                    $"payload must be exactly {report.TransferByteLength} byte(s) for report {report.ReportId}, got {payload.Length}");
            }

            if (report.Kind == HidReportKind.Input)
            {
                return new ConsoleDecision(false,
                    $"report {report.ReportId} is an input report and is read-only; a write means the report was misidentified");
            }

            if (report.Kind == HidReportKind.Feature && report.ReportId == KnownTemperatureReportId)
            {
                return new ConsoleDecision(true,
                    $"report {KnownTemperatureReportId} is the known temperature report");
            }

            if (!UnsafeWritesArmed)
            {
                return new ConsoleDecision(false,
                    $"report {report.ReportId} is not the known temperature report; " +
                    "arm unsafe writes in Settings to send it, and be aware a bad USB state can " +
                    "leave the panel unconfigured until reboot");
            }

            return new ConsoleDecision(true,
                $"unsafe writes are armed: sending {report.Kind} report {report.ReportId}");
        }

        public ConsoleDecision EvaluateRead(HidReportInfo report, byte[] buffer)
        {
            if (report == null)
            {
                return new ConsoleDecision(false, "no report selected");
            }

            if (buffer == null)
            {
                return new ConsoleDecision(false, "no buffer");
            }

            if (buffer.Length != report.TransferByteLength)
            {
                return new ConsoleDecision(false,
                    $"buffer must be exactly {report.TransferByteLength} byte(s) for report {report.ReportId}, got {buffer.Length}");
            }

            return new ConsoleDecision(true, $"reading {report.Kind} report {report.ReportId}");
        }
    }
}
