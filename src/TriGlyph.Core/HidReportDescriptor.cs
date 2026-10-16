using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TriGlyph.Core
{
    public enum HidReportKind
    {
        Input,
        Output,
        Feature
    }

    /// <summary>One report (a report id + kind) declared by a HID descriptor.</summary>
    public sealed class HidReportInfo
    {
        public int ReportId { get; set; }
        public HidReportKind Kind { get; set; }

        /// <summary>Total payload bits across every item contributing to this report.</summary>
        public int BitLength { get; set; }

        /// <summary>Payload bytes, rounded up to a byte boundary.</summary>
        public int DataByteLength => (BitLength + 7) / 8;

        /// <summary>
        /// Bytes actually transferred, i.e. including the leading report id byte when
        /// the descriptor uses report ids. This is what HidP_GetCaps reports as
        /// FeatureReportByteLength.
        /// </summary>
        public int TransferByteLength { get; set; }

        public int UsagePage { get; set; }
        public int FirstUsage { get; set; }

        public override string ToString()
        {
            return $"report {ReportId} {Kind} {DataByteLength}B data / {TransferByteLength}B wire " +
                   $"(page 0x{UsagePage:X4} usage 0x{FirstUsage:X4})";
        }
    }

    /// <summary>
    /// Parser for a raw USB HID report descriptor.
    ///
    /// The cooler panel ships a 205-byte descriptor that the vendor has never
    /// documented, so the only honest way to enumerate what the device can do is to
    /// read that descriptor off the device and decode it here. This type is pure:
    /// bytes in, report inventory out - no Windows, no device, fully testable.
    /// </summary>
    public sealed class HidReportDescriptor
    {
        private const byte LongItemPrefix = 0xFE;

        private readonly List<HidReportInfo> _reports;

        /// <summary>True when the descriptor declares report ids (so every transfer leads with one).</summary>
        public bool UsesReportIds { get; }

        public IReadOnlyList<HidReportInfo> Reports => _reports;

        private HidReportDescriptor(List<HidReportInfo> reports, bool usesReportIds)
        {
            _reports = reports;
            UsesReportIds = usesReportIds;
        }

        public IEnumerable<HidReportInfo> ReportsOfKind(HidReportKind kind)
        {
            return _reports.Where(r => r.Kind == kind);
        }

        public HidReportInfo Find(int reportId, HidReportKind kind)
        {
            return _reports.FirstOrDefault(r => r.ReportId == reportId && r.Kind == kind);
        }

        public string Describe()
        {
            if (_reports.Count == 0)
            {
                return "descriptor declares no reports";
            }

            var sb = new StringBuilder();
            sb.Append(UsesReportIds ? "uses report ids" : "no report ids");
            sb.Append(", ").Append(_reports.Count).Append(" report(s):");
            foreach (HidReportInfo r in _reports)
            {
                sb.Append(Environment.NewLine).Append("  ").Append(r);
            }

            return sb.ToString();
        }

        public static HidReportDescriptor Parse(byte[] descriptor)
        {
            if (!TryParse(descriptor, out HidReportDescriptor result, out string error))
            {
                throw new FormatException(error);
            }

            return result;
        }

        public static bool TryParse(byte[] descriptor, out HidReportDescriptor result, out string error)
        {
            result = null;
            error = null;

            if (descriptor == null)
            {
                error = "descriptor is null";
                return false;
            }

            var globals = new GlobalState();
            var stack = new Stack<GlobalState>();
            var locals = new LocalState();
            var accumulated = new Dictionary<ReportKey, Accumulator>();
            bool sawReportId = false;

            int i = 0;
            while (i < descriptor.Length)
            {
                byte prefix = descriptor[i++];

                if (prefix == LongItemPrefix)
                {
                    if (i + 2 > descriptor.Length)
                    {
                        error = $"truncated long item header at offset {i - 1}";
                        return false;
                    }

                    int dataSize = descriptor[i];
                    i += 2; // bDataSize then bLongItemTag

                    if (i + dataSize > descriptor.Length)
                    {
                        error = $"truncated long item body at offset {i}";
                        return false;
                    }

                    i += dataSize;
                    continue;
                }

                int sizeCode = prefix & 0x03;
                int size = sizeCode == 3 ? 4 : sizeCode;
                int type = (prefix >> 2) & 0x03;
                int tag = (prefix >> 4) & 0x0F;

                if (i + size > descriptor.Length)
                {
                    error = $"truncated item at offset {i - 1} (needs {size} data byte(s))";
                    return false;
                }

                uint value = 0;
                for (int b = 0; b < size; b++)
                {
                    value |= (uint)descriptor[i + b] << (8 * b);
                }

                i += size;

                switch (type)
                {
                    case 0: // Main
                        if (tag == 0x8 || tag == 0x9 || tag == 0xB)
                        {
                            HidReportKind kind = tag == 0x8 ? HidReportKind.Input
                                : tag == 0x9 ? HidReportKind.Output
                                : HidReportKind.Feature;

                            AddReport(accumulated, globals, locals, kind);
                        }

                        // Local items are cleared by every main item, including
                        // Collection and EndCollection.
                        locals.Clear();
                        break;

                    case 1: // Global
                        switch (tag)
                        {
                            case 0x0: globals.UsagePage = (int)value; break;
                            case 0x7: globals.ReportSize = (int)value; break;
                            case 0x8:
                                globals.ReportId = (int)value;
                                sawReportId = true;
                                break;
                            case 0x9: globals.ReportCount = (int)value; break;
                            case 0xA: stack.Push(globals.Clone()); break;
                            case 0xB:
                                if (stack.Count > 0)
                                {
                                    globals = stack.Pop();
                                }

                                break;
                        }

                        break;

                    case 2: // Local
                        switch (tag)
                        {
                            case 0x0: locals.Usages.Add((int)value); break;
                            case 0x1: locals.UsageMinimum = (int)value; break;
                            case 0x2: locals.UsageMaximum = (int)value; break;
                        }

                        break;
                }
            }

            var reports = accumulated
                .Select(kv => new HidReportInfo
                {
                    ReportId = kv.Key.ReportId,
                    Kind = kv.Key.Kind,
                    BitLength = kv.Value.Bits,
                    UsagePage = kv.Value.UsagePage,
                    FirstUsage = kv.Value.FirstUsage,
                    TransferByteLength = ((kv.Value.Bits + 7) / 8) + (sawReportId ? 1 : 0)
                })
                .OrderBy(r => r.ReportId)
                .ThenBy(r => r.Kind)
                .ToList();

            result = new HidReportDescriptor(reports, sawReportId);
            return true;
        }

        private static void AddReport(
            Dictionary<ReportKey, Accumulator> accumulated,
            GlobalState globals,
            LocalState locals,
            HidReportKind kind)
        {
            int bits = globals.ReportSize * globals.ReportCount;
            var key = new ReportKey(globals.ReportId, kind);

            if (!accumulated.TryGetValue(key, out Accumulator acc))
            {
                acc = new Accumulator
                {
                    UsagePage = globals.UsagePage,
                    FirstUsage = locals.Usages.Count > 0
                        ? locals.Usages[0]
                        : locals.UsageMinimum
                };
                accumulated[key] = acc;
            }

            acc.Bits += bits;
        }

        private readonly struct ReportKey : IEquatable<ReportKey>
        {
            public readonly int ReportId;
            public readonly HidReportKind Kind;

            public ReportKey(int reportId, HidReportKind kind)
            {
                ReportId = reportId;
                Kind = kind;
            }

            public bool Equals(ReportKey other) => ReportId == other.ReportId && Kind == other.Kind;

            public override bool Equals(object obj) => obj is ReportKey other && Equals(other);

            public override int GetHashCode() => (ReportId * 397) ^ (int)Kind;
        }

        private sealed class Accumulator
        {
            public int Bits;
            public int UsagePage;
            public int FirstUsage;
        }

        private sealed class GlobalState
        {
            public int UsagePage;
            public int ReportSize;
            public int ReportCount;
            public int ReportId;

            public GlobalState Clone() => new GlobalState
            {
                UsagePage = UsagePage,
                ReportSize = ReportSize,
                ReportCount = ReportCount,
                ReportId = ReportId
            };
        }

        private sealed class LocalState
        {
            public readonly List<int> Usages = new List<int>();
            public int UsageMinimum;
            public int UsageMaximum;

            public void Clear()
            {
                Usages.Clear();
                UsageMinimum = 0;
                UsageMaximum = 0;
            }
        }
    }
}
