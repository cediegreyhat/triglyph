using System;
using System.Globalization;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using TriGlyph.Core;

namespace TriGlyph.App.Sources
{
    /// <summary>
    /// Reads HWiNFO's published sensor shared memory.
    ///
    /// HWiNFO does not document its struct layout, so this reader refuses to guess:
    /// it validates the header signature and requires the element size to match the
    /// layout compiled in below. If a future HWiNFO changes that size, this source
    /// reports unavailable rather than decoding garbage into a plausible-looking
    /// temperature. Reading the wrong bytes and displaying them would be worse than
    /// showing nothing.
    ///
    /// It also deliberately ignores the reading-type enum and keys off the unit string
    /// plus the label, so an enum renumbering cannot silently shift what we read.
    /// </summary>
    internal sealed class HwInfoSharedMemorySource : ITemperatureSource
    {
        private const string MappingName = "Global\\HWiNFO_SENS_SM2";
        private const uint Signature = 0x53695748; // "HWiS"

        // HWiNFO_SENSORS_READING_ELEMENT, natural alignment, verified against the
        // declared element size before use.
        private const int ReadingElementSize = 320;
        private const int OffsetLabelUser = 140;   // char szLabelUser[128]
        private const int OffsetUnit = 268;        // char szUnit[16]
        private const int OffsetValue = 288;       // double Value, 8-aligned after szUnit

        private const int HeaderReadingElementSizeOffset = 32;
        private const int HeaderReadingCountOffset = 40;
        private const int HeaderReadingSectionOffset = 36;

        public string Name => TemperatureSourceNames.HwInfoSharedMemory;

        public bool TryRead(out double celsius, out string error)
        {
            celsius = 0;
            error = null;

            try
            {
                using (MemoryMappedFile file = MemoryMappedFile.OpenExisting(
                    MappingName, MemoryMappedFileRights.Read))
                using (MemoryMappedViewAccessor view = file.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read))
                {
                    long length = view.Capacity;
                    if (length < 64)
                    {
                        error = "HWiNFO shared memory is present but too small to be valid";
                        return false;
                    }

                    var header = new byte[64];
                    view.ReadArray(0, header, 0, header.Length);

                    uint signature = BitConverter.ToUInt32(header, 0);
                    if (signature != Signature)
                    {
                        error = "HWiNFO shared memory signature mismatch; not a HWiNFO v2 block";
                        return false;
                    }

                    int elementSize = BitConverter.ToInt32(header, HeaderReadingElementSizeOffset);
                    int readingCount = BitConverter.ToInt32(header, HeaderReadingCountOffset);
                    int readingOffset = BitConverter.ToInt32(header, HeaderReadingSectionOffset);

                    if (elementSize != ReadingElementSize)
                    {
                        error = $"HWiNFO reading element is {elementSize} bytes, this build understands " +
                                $"{ReadingElementSize}; refusing to guess at the layout";
                        return false;
                    }

                    if (readingCount <= 0 || readingOffset <= 0 || readingOffset + (long)readingCount * elementSize > length)
                    {
                        error = "HWiNFO shared memory declares an implausible reading section";
                        return false;
                    }

                    var element = new byte[elementSize];
                    double best = double.MinValue;

                    for (int i = 0; i < readingCount; i++)
                    {
                        view.ReadArray(readingOffset + (long)i * elementSize, element, 0, elementSize);

                        string unit = ReadAnsi(element, OffsetUnit, 16);
                        if (unit.IndexOf('C') < 0 && unit.IndexOf('c') < 0)
                        {
                            continue;
                        }

                        string label = ReadAnsi(element, OffsetLabelUser, 128);
                        if (label.Length == 0)
                        {
                            continue;
                        }

                        string lower = label.ToLowerInvariant();
                        if (lower.IndexOf("cpu", StringComparison.Ordinal) < 0)
                        {
                            continue;
                        }

                        double value = BitConverter.ToDouble(element, OffsetValue);
                        if (double.IsNaN(value) || value < 0 || value > 150)
                        {
                            continue;
                        }

                        if (value > best)
                        {
                            best = value;
                        }
                    }

                    if (best == double.MinValue)
                    {
                        error = "HWiNFO is running but reported no CPU temperature reading";
                        return false;
                    }

                    celsius = best;
                    return true;
                }
            }
            catch (FileNotFoundException)
            {
                error = "HWiNFO is not running (no shared memory section)";
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                error = "cannot open HWiNFO shared memory: " + ex.Message;
                return false;
            }
            catch (IOException ex)
            {
                error = "HWiNFO shared memory read failed: " + ex.Message;
                return false;
            }
        }

        private static string ReadAnsi(byte[] buffer, int offset, int max)
        {
            int end = offset;
            int limit = Math.Min(offset + max, buffer.Length);
            while (end < limit && buffer[end] != 0)
            {
                end++;
            }

            return Encoding.ASCII.GetString(buffer, offset, end - offset).Trim();
        }
    }
}
