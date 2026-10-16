using TriGlyph.Core;
using Xunit;

namespace TriGlyph.Tests
{
    public class ReportConsolePolicyTests
    {
        private static HidReportInfo Feature(int id, int bytes)
        {
            return new HidReportInfo
            {
                ReportId = id, Kind = HidReportKind.Feature,
                BitLength = bytes * 8, TransferByteLength = bytes + 1
            };
        }

        private static HidReportInfo Input(int id, int bytes)
        {
            return new HidReportInfo
            {
                ReportId = id, Kind = HidReportKind.Input,
                BitLength = bytes * 8, TransferByteLength = bytes + 1
            };
        }

        [Fact]
        public void The_known_temperature_report_is_writable_without_arming()
        {
            var policy = new ReportConsolePolicy(unsafeWritesArmed: false);
            var decision = policy.EvaluateWrite(Feature(7, 63), new byte[64]);

            Assert.True(decision.Allowed);
            Assert.False(string.IsNullOrWhiteSpace(decision.Reason));
        }

        [Fact]
        public void An_unknown_report_id_is_blocked_while_disarmed()
        {
            var policy = new ReportConsolePolicy(unsafeWritesArmed: false);
            var decision = policy.EvaluateWrite(Feature(5, 4), new byte[5]);

            Assert.False(decision.Allowed);
            Assert.Contains("arm", decision.Reason, System.StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void An_unknown_report_id_is_allowed_once_armed()
        {
            var policy = new ReportConsolePolicy(unsafeWritesArmed: true);
            var decision = policy.EvaluateWrite(Feature(5, 4), new byte[5]);

            Assert.True(decision.Allowed);
        }

        [Fact]
        public void Writing_to_an_input_report_is_never_allowed_even_when_armed()
        {
            var policy = new ReportConsolePolicy(unsafeWritesArmed: true);
            var decision = policy.EvaluateWrite(Input(3, 4), new byte[5]);

            Assert.False(decision.Allowed);
            Assert.Contains("read-only", decision.Reason, System.StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void A_payload_of_the_wrong_length_is_rejected()
        {
            var policy = new ReportConsolePolicy(unsafeWritesArmed: true);
            var decision = policy.EvaluateWrite(Feature(7, 63), new byte[10]);

            Assert.False(decision.Allowed);
            Assert.Contains("64", decision.Reason);
        }

        [Fact]
        public void Reading_is_always_permitted()
        {
            var policy = new ReportConsolePolicy(unsafeWritesArmed: false);

            Assert.True(policy.EvaluateRead(Feature(7, 63), new byte[64]).Allowed);
            Assert.True(policy.EvaluateRead(Feature(200, 4), new byte[5]).Allowed);
            Assert.True(policy.EvaluateRead(Input(1, 8), new byte[9]).Allowed);
        }

        [Fact]
        public void A_read_of_the_wrong_length_is_rejected()
        {
            var policy = new ReportConsolePolicy(unsafeWritesArmed: false);
            Assert.False(policy.EvaluateRead(Feature(7, 63), new byte[8]).Allowed);
        }

        [Fact]
        public void Null_report_is_rejected_rather_than_throwing()
        {
            var policy = new ReportConsolePolicy(unsafeWritesArmed: true);
            Assert.False(policy.EvaluateWrite(null, new byte[64]).Allowed);
            Assert.False(policy.EvaluateRead(null, new byte[64]).Allowed);
        }

        [Fact]
        public void The_policy_states_which_report_id_is_the_known_one()
        {
            Assert.Equal(7, ReportConsolePolicy.KnownTemperatureReportId);
        }
    }
}
