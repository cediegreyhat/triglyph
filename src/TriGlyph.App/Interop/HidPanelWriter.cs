using System;
using TriGlyph.Core;

namespace TriGlyph.App.Interop
{
    /// <summary>
    /// Opens the panel on demand, writes one frame, and closes. Deliberately not held
    /// open between ticks: the vendor tool, HWiNFO and the OS keyboard driver may all
    /// want the same device, and holding a handle across a 1 Hz loop invites conflicts.
    /// </summary>
    internal sealed class HidPanelWriter : IPanelWriter
    {
        private readonly string _path;

        public HidPanelWriter(string path)
        {
            _path = path;
        }

        public string Path => _path;

        public bool TryWrite(TemperatureFrame frame, out string error)
        {
            error = null;

            if (string.IsNullOrEmpty(_path))
            {
                error = "no panel selected";
                return false;
            }

            if (!HidFeatureChannel.TryOpen(_path, out HidFeatureChannel channel, out error))
            {
                return false;
            }

            using (channel)
            {
                return channel.TrySetFeature(frame.Bytes, out error);
            }
        }
    }
}
