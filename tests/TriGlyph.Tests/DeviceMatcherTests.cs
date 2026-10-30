using System.Linq;
using TriGlyph.Core;
using Xunit;

namespace TriGlyph.Tests
{
    public class DeviceMatcherTests
    {
        private static HidDeviceCandidate Cand(
            string path, int vid, int pid, int iface, int page, int usage, int featLen)
        {
            return new HidDeviceCandidate
            {
                Path = path,
                VendorId = vid,
                ProductId = pid,
                InterfaceNumber = iface,
                UsagePage = page,
                Usage = usage,
                FeatureReportLength = featLen,
                Product = "USB Gaming Keyboard",
                Manufacturer = "SEMICO"
            };
        }

        [Fact]
        public void Picks_the_known_panel_exactly()
        {
            var candidates = new[]
            {
                Cand(@"\\?\hid#vid_1a2c&pid_4184&mi_00", 0x1A2C, 0x4184, 0, 0x0001, 0x06, 0),
                Cand(@"\\?\hid#vid_1a2c&pid_4184&mi_01", 0x1A2C, 0x4184, 1, 0xFF00, 0x01, 64)
            };

            var match = DeviceMatcher.Match(candidates);

            Assert.NotNull(match.Candidate);
            Assert.Equal(MatchConfidence.Exact, match.Confidence);
            Assert.Equal(1, match.Candidate.InterfaceNumber);
        }

        [Fact]
        public void Falls_back_to_vendor_when_the_product_id_differs()
        {
            var candidates = new[]
            {
                Cand(@"\\?\hid#vid_1a2c&pid_9999&mi_01", 0x1A2C, 0x9999, 1, 0xFF00, 0x01, 64)
            };

            var match = DeviceMatcher.Match(candidates);

            Assert.Equal(MatchConfidence.FamilyVendor, match.Confidence);
            Assert.Equal(0x9999, match.Candidate.ProductId);
        }

        [Fact]
        public void Falls_back_to_structure_when_the_vendor_id_differs()
        {
            // A rebadged unit on a different VID still has the same shape.
            var candidates = new[]
            {
                Cand(@"\\?\hid#vid_1234&pid_5678&mi_01", 0x1234, 0x5678, 1, 0xFF00, 0x01, 64)
            };

            var match = DeviceMatcher.Match(candidates);

            Assert.Equal(MatchConfidence.Structural, match.Confidence);
        }

        [Fact]
        public void Exact_beats_structural()
        {
            var candidates = new[]
            {
                Cand(@"\\?\hid#vid_1234&pid_5678&mi_01", 0x1234, 0x5678, 1, 0xFF00, 0x01, 64),
                Cand(@"\\?\hid#vid_1a2c&pid_4184&mi_01", 0x1A2C, 0x4184, 1, 0xFF00, 0x01, 64)
            };

            Assert.Equal(MatchConfidence.Exact, DeviceMatcher.Match(candidates).Confidence);
        }

        [Fact]
        public void Ignores_interfaces_without_a_64_byte_feature_report()
        {
            var candidates = new[]
            {
                Cand(@"\\?\hid#vid_1a2c&pid_4184&mi_01", 0x1A2C, 0x4184, 1, 0xFF00, 0x01, 32)
            };

            var match = DeviceMatcher.Match(candidates);

            Assert.Null(match.Candidate);
            Assert.Equal(MatchConfidence.None, match.Confidence);
        }

        [Fact]
        public void Ignores_a_keyboard_interface_that_happens_to_have_64_bytes()
        {
            // Same shape but a standard page: must not be mistaken for the panel.
            var candidates = new[]
            {
                Cand(@"\\?\hid#vid_1a2c&pid_4184&mi_00", 0x1A2C, 0x4184, 0, 0x0001, 0x06, 64)
            };

            Assert.Null(DeviceMatcher.Match(candidates).Candidate);
        }

        [Fact]
        public void Prefers_interface_one_when_two_tie()
        {
            var candidates = new[]
            {
                Cand(@"\\?\hid#a&mi_02", 0x1A2C, 0x4184, 2, 0xFF00, 0x01, 64),
                Cand(@"\\?\hid#b&mi_01", 0x1A2C, 0x4184, 1, 0xFF00, 0x01, 64)
            };

            Assert.Equal(1, DeviceMatcher.Match(candidates).Candidate.InterfaceNumber);
        }

        [Fact]
        public void Is_deterministic_when_everything_ties()
        {
            var candidates = new[]
            {
                Cand(@"\\?\hid#zzz", 0x1A2C, 0x4184, 1, 0xFF00, 0x01, 64),
                Cand(@"\\?\hid#aaa", 0x1A2C, 0x4184, 1, 0xFF00, 0x01, 64)
            };

            Assert.Equal(@"\\?\hid#aaa", DeviceMatcher.Match(candidates).Candidate.Path);
            Assert.Equal(@"\\?\hid#aaa", DeviceMatcher.Match(candidates.Reverse()).Candidate.Path);
        }

        [Fact]
        public void No_candidates_yields_no_match_with_a_reason()
        {
            var match = DeviceMatcher.Match(new HidDeviceCandidate[0]);

            Assert.Null(match.Candidate);
            Assert.Equal(MatchConfidence.None, match.Confidence);
            Assert.False(string.IsNullOrWhiteSpace(match.Reason));
        }

        [Fact]
        public void Match_explains_itself()
        {
            var candidates = new[]
            {
                Cand(@"\\?\hid#vid_1a2c&pid_4184&mi_01", 0x1A2C, 0x4184, 1, 0xFF00, 0x01, 64)
            };

            string reason = DeviceMatcher.Match(candidates).Reason;
            Assert.Contains("1A2C", reason.ToUpperInvariant());
            Assert.Contains("4184", reason);
        }
    }
}
