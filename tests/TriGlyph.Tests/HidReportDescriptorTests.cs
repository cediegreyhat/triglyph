using System.Linq;
using TriGlyph.Core;
using Xunit;

namespace TriGlyph.Tests
{
    /// <summary>
    /// Descriptor bytes follow USB HID 1.11 section 6.2.2 item encoding.
    /// Expected values are hand-decoded from the spec, not recomputed by the parser.
    /// </summary>
    public class HidReportDescriptorTests
    {
        // Shaped like the cooler panel: vendor-defined page, one 63-byte feature
        // report under report id 7 => 64 bytes on the wire including the id byte.
        private static readonly byte[] PanelLike =
        {
            0x06, 0x00, 0xFF,   // UsagePage(0xFF00 vendor defined)
            0x09, 0x01,         // Usage(0x01)
            0xA1, 0x01,         // Collection(Application)
            0x85, 0x07,         //   ReportID(7)
            0x06, 0x00, 0xFF,   //   UsagePage(0xFF00)
            0x09, 0x01,         //   Usage(0x01)
            0x75, 0x08,         //   ReportSize(8)
            0x95, 0x3F,         //   ReportCount(63)
            0xB1, 0x02,         //   Feature(Data,Var,Abs)
            0xC0                // EndCollection
        };

        // Classic boot mouse: no ReportID item, 3 button bits + 5 pad bits + 2x8 axes.
        private static readonly byte[] BootMouse =
        {
            0x05, 0x01, 0x09, 0x02, 0xA1, 0x01,
            0x09, 0x01, 0xA1, 0x00,
            0x05, 0x09, 0x19, 0x01, 0x29, 0x03,
            0x15, 0x00, 0x25, 0x01,
            0x95, 0x03, 0x75, 0x01, 0x81, 0x02,
            0x95, 0x01, 0x75, 0x05, 0x81, 0x01,
            0x05, 0x01, 0x09, 0x30, 0x09, 0x31,
            0x15, 0x81, 0x25, 0x7F,
            0x75, 0x08, 0x95, 0x02, 0x81, 0x06,
            0xC0, 0xC0
        };

        [Fact]
        public void Parses_the_panel_like_descriptor()
        {
            var d = HidReportDescriptor.Parse(PanelLike);

            Assert.True(d.UsesReportIds);
            var f = Assert.Single(d.ReportsOfKind(HidReportKind.Feature));
            Assert.Equal(7, f.ReportId);
            Assert.Equal(63 * 8, f.BitLength);
            Assert.Equal(63, f.DataByteLength);
            Assert.Equal(64, f.TransferByteLength);
        }

        [Fact]
        public void Captures_usage_page_and_first_usage()
        {
            var f = HidReportDescriptor.Parse(PanelLike).ReportsOfKind(HidReportKind.Feature).Single();
            Assert.Equal(0xFF00, f.UsagePage);
            Assert.Equal(0x01, f.FirstUsage);
        }

        [Fact]
        public void Boot_mouse_has_no_report_id_so_transfer_equals_data_length()
        {
            var d = HidReportDescriptor.Parse(BootMouse);

            Assert.False(d.UsesReportIds);
            var input = Assert.Single(d.ReportsOfKind(HidReportKind.Input));
            Assert.Equal(0, input.ReportId);
            Assert.Equal(24, input.BitLength);   // 3 + 5 + 16
            Assert.Equal(3, input.DataByteLength);
            Assert.Equal(3, input.TransferByteLength);
        }

        [Fact]
        public void Accumulates_multiple_items_into_one_report_id()
        {
            byte[] desc =
            {
                0x85, 0x03,        // ReportID(3)
                0x75, 0x08, 0x95, 0x04, 0xB1, 0x02,   // 4 bytes feature
                0x75, 0x08, 0x95, 0x02, 0xB1, 0x02,   // + 2 bytes feature
                0x75, 0x08, 0x95, 0x01, 0x81, 0x02    // 1 byte input
            };

            var d = HidReportDescriptor.Parse(desc);

            var f = Assert.Single(d.ReportsOfKind(HidReportKind.Feature));
            Assert.Equal(3, f.ReportId);
            Assert.Equal(6, f.DataByteLength);
            Assert.Equal(7, f.TransferByteLength);

            var i = Assert.Single(d.ReportsOfKind(HidReportKind.Input));
            Assert.Equal(1, i.DataByteLength);
        }

        [Fact]
        public void Distinguishes_all_three_report_kinds()
        {
            byte[] desc =
            {
                0x85, 0x02,
                0x75, 0x08, 0x95, 0x01, 0x81, 0x02,   // input
                0x75, 0x08, 0x95, 0x02, 0x91, 0x02,   // output
                0x75, 0x08, 0x95, 0x03, 0xB1, 0x02    // feature
            };

            var d = HidReportDescriptor.Parse(desc);

            Assert.Equal(1, d.ReportsOfKind(HidReportKind.Input).Single().DataByteLength);
            Assert.Equal(2, d.ReportsOfKind(HidReportKind.Output).Single().DataByteLength);
            Assert.Equal(3, d.ReportsOfKind(HidReportKind.Feature).Single().DataByteLength);
        }

        [Fact]
        public void Enumerates_every_report_id()
        {
            byte[] desc =
            {
                0x85, 0x01, 0x75, 0x08, 0x95, 0x01, 0xB1, 0x02,
                0x85, 0x07, 0x75, 0x08, 0x95, 0x3F, 0xB1, 0x02,
                0x85, 0x09, 0x75, 0x08, 0x95, 0x02, 0xB1, 0x02
            };

            var ids = HidReportDescriptor.Parse(desc).Reports
                .Where(r => r.Kind == HidReportKind.Feature)
                .Select(r => r.ReportId)
                .OrderBy(x => x)
                .ToArray();

            Assert.Equal(new[] { 1, 7, 9 }, ids);
        }

        [Fact]
        public void Find_locates_a_specific_report()
        {
            var d = HidReportDescriptor.Parse(PanelLike);
            Assert.NotNull(d.Find(7, HidReportKind.Feature));
            Assert.Null(d.Find(9, HidReportKind.Feature));
            Assert.Null(d.Find(7, HidReportKind.Output));
        }

        [Fact]
        public void Push_and_pop_restore_global_state()
        {
            byte[] desc =
            {
                0x06, 0x00, 0xFF,   // UsagePage(0xFF00)
                0xA4,               // Push
                0x06, 0x01, 0x00,   // UsagePage(0x0001)
                0x85, 0x05, 0x75, 0x08, 0x95, 0x01, 0xB1, 0x02,  // feature on page 0x0001
                0xB4,               // Pop
                0x85, 0x06, 0x75, 0x08, 0x95, 0x01, 0xB1, 0x02   // feature back on 0xFF00
            };

            var d = HidReportDescriptor.Parse(desc);

            Assert.Equal(0x0001, d.Find(5, HidReportKind.Feature).UsagePage);
            Assert.Equal(0xFF00, d.Find(6, HidReportKind.Feature).UsagePage);
        }

        [Fact]
        public void Skips_long_items()
        {
            byte[] desc =
            {
                0xFE, 0x02, 0x01, 0xAA, 0xBB,   // long item, 2 data bytes
                0x85, 0x07, 0x75, 0x08, 0x95, 0x3F, 0xB1, 0x02
            };

            var f = Assert.Single(HidReportDescriptor.Parse(desc).ReportsOfKind(HidReportKind.Feature));
            Assert.Equal(7, f.ReportId);
            Assert.Equal(64, f.TransferByteLength);
        }

        [Fact]
        public void Rejects_a_truncated_item_without_throwing()
        {
            byte[] truncated = { 0x06, 0x00 };   // UsagePage claims 2 data bytes, only 1 present

            Assert.False(HidReportDescriptor.TryParse(truncated, out var result, out string error));
            Assert.Null(result);
            Assert.False(string.IsNullOrWhiteSpace(error));
        }

        [Fact]
        public void Rejects_a_truncated_long_item()
        {
            byte[] truncated = { 0xFE, 0x05, 0x01, 0xAA };
            Assert.False(HidReportDescriptor.TryParse(truncated, out _, out _));
        }

        [Fact]
        public void Empty_descriptor_parses_to_no_reports()
        {
            var d = HidReportDescriptor.Parse(new byte[0]);
            Assert.Empty(d.Reports);
            Assert.False(d.UsesReportIds);
        }

        [Fact]
        public void Describe_renders_a_readable_summary()
        {
            string text = HidReportDescriptor.Parse(PanelLike).Describe();
            Assert.Contains("Feature", text);
            Assert.Contains("64", text);
        }
    }
}
