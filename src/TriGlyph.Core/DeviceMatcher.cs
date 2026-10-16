using System;
using System.Collections.Generic;
using System.Linq;

namespace TriGlyph.Core
{
    /// <summary>One HID interface as reported by device enumeration.</summary>
    public sealed class HidDeviceCandidate
    {
        public string Path { get; set; }
        public int VendorId { get; set; }
        public int ProductId { get; set; }
        public int InterfaceNumber { get; set; }
        public int UsagePage { get; set; }
        public int Usage { get; set; }
        public int FeatureReportLength { get; set; }
        public string Product { get; set; }
        public string Manufacturer { get; set; }

        public override string ToString()
        {
            return $"VID_{VendorId:X4}&PID_{ProductId:X4} iface={InterfaceNumber} " +
                   $"page=0x{UsagePage:X4} feat={FeatureReportLength} {Product}";
        }
    }

    public enum MatchConfidence
    {
        None,
        Structural,
        FamilyVendor,
        Exact
    }

    public sealed class DeviceMatch
    {
        public HidDeviceCandidate Candidate { get; }
        public MatchConfidence Confidence { get; }
        public string Reason { get; }

        public DeviceMatch(HidDeviceCandidate candidate, MatchConfidence confidence, string reason)
        {
            Candidate = candidate;
            Confidence = confidence;
            Reason = reason;
        }
    }

    /// <summary>
    /// Chooses which enumerated HID interface is the cooler's display panel.
    ///
    /// Identity alone is not enough: the panel enumerates as a two-interface
    /// "USB Gaming Keyboard" and shares its VID/PID with unrelated SKUs, so the
    /// matcher scores on shape as well and always explains the choice. Callers are
    /// expected to surface <see cref="DeviceMatch.Reason"/> to the user rather than
    /// silently writing to whatever was picked.
    /// </summary>
    public static class DeviceMatcher
    {
        public const int PanelVendorId = 0x1A2C;
        public const int PanelProductId = 0x4184;
        public const int VendorDefinedUsagePage = 0xFF00;
        public const int ExpectedFeatureReportLength = 64;
        public const int PreferredInterfaceNumber = 1;

        public static DeviceMatch Match(IEnumerable<HidDeviceCandidate> candidates)
        {
            if (candidates == null)
            {
                return new DeviceMatch(null, MatchConfidence.None, "no HID interfaces were enumerated");
            }

            List<HidDeviceCandidate> all = candidates.ToList();
            if (all.Count == 0)
            {
                return new DeviceMatch(null, MatchConfidence.None, "no HID interfaces were enumerated");
            }

            DeviceMatch best = Best(all, MatchConfidence.Exact,
                c => c.VendorId == PanelVendorId
                     && c.ProductId == PanelProductId
                     && c.FeatureReportLength == ExpectedFeatureReportLength
                     && IsPanelShaped(c),
                $"exact match: VID {PanelVendorId:X4} PID {PanelProductId:X4} with a " +
                $"{ExpectedFeatureReportLength}-byte feature report");

            if (best != null)
            {
                return best;
            }

            best = Best(all, MatchConfidence.FamilyVendor,
                c => c.VendorId == PanelVendorId
                     && c.FeatureReportLength == ExpectedFeatureReportLength
                     && IsPanelShaped(c),
                $"vendor {PanelVendorId:X4} interface with a vendor-defined usage page and a " +
                $"{ExpectedFeatureReportLength}-byte feature report (unknown product id)");

            if (best != null)
            {
                return best;
            }

            best = Best(all, MatchConfidence.Structural,
                c => c.UsagePage == VendorDefinedUsagePage
                     && c.FeatureReportLength == ExpectedFeatureReportLength,
                "structural match: vendor-defined usage page with a " +
                $"{ExpectedFeatureReportLength}-byte feature report on an unknown vendor (rebadged unit?)");

            if (best != null)
            {
                return best;
            }

            return new DeviceMatch(null, MatchConfidence.None,
                $"no interface matched: {all.Count} enumerated, none with a vendor-defined usage page " +
                $"and a {ExpectedFeatureReportLength}-byte feature report");
        }

        /// <summary>
        /// The panel is documented as interface 1 and speaks a vendor-defined usage page.
        /// Requiring one of the two stops a same-VID/PID keyboard interface that merely
        /// happens to carry a 64-byte feature report from being selected and written to.
        /// </summary>
        private static bool IsPanelShaped(HidDeviceCandidate c) =>
            c.UsagePage == VendorDefinedUsagePage || c.InterfaceNumber == PreferredInterfaceNumber;

        private static DeviceMatch Best(
            List<HidDeviceCandidate> all,
            MatchConfidence confidence,
            Func<HidDeviceCandidate, bool> predicate,
            string reason)
        {
            List<HidDeviceCandidate> hits = all.Where(predicate).ToList();
            if (hits.Count == 0)
            {
                return null;
            }

            HidDeviceCandidate chosen = hits
                .OrderByDescending(c => c.InterfaceNumber == PreferredInterfaceNumber)
                .ThenBy(c => c.Path, StringComparer.Ordinal)
                .First();

            return new DeviceMatch(chosen, confidence, reason);
        }
    }
}
