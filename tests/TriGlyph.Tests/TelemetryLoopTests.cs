using System;
using System.Collections.Generic;
using TriGlyph.Core;
using Xunit;

namespace TriGlyph.Tests
{
    public class TelemetryLoopTests
    {
        private sealed class FakeSource : ITemperatureSource
        {
            private readonly Queue<double> _readings;
            public string Name { get; }
            public int ReadCount { get; private set; }

            public FakeSource(string name, params double[] readings)
            {
                Name = name;
                _readings = new Queue<double>(readings);
            }

            public bool TryRead(out double celsius, out string error)
            {
                ReadCount++;
                if (_readings.Count == 0)
                {
                    celsius = 0;
                    error = "sensor not available";
                    return false;
                }

                celsius = _readings.Dequeue();
                error = null;
                return true;
            }
        }

        private sealed class FailingSource : ITemperatureSource
        {
            public string Name => "Broken";
            public bool TryRead(out double celsius, out string error)
            {
                celsius = 0;
                error = "no driver";
                return false;
            }
        }

        private sealed class RecordingWriter : IPanelWriter
        {
            public readonly List<TemperatureFrame> Written = new List<TemperatureFrame>();
            public bool Succeed = true;
            public bool TryWrite(TemperatureFrame frame, out string error)
            {
                if (!Succeed) { error = "device busy"; return false; }
                Written.Add(frame);
                error = null;
                return true;
            }
        }

        private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch.AddHours(1);

        [Fact]
        public void Reads_smooths_encodes_and_writes()
        {
            var src = new FakeSource("A", 45);
            var writer = new RecordingWriter();
            var loop = new TelemetryLoop(new[] { (ITemperatureSource)src }, writer, TemperaturePolicy.Default);

            var r = loop.Tick(T0);

            Assert.True(r.Wrote);
            Assert.Equal(45, r.Celsius);
            Assert.Equal("A", r.SourceName);
            Assert.Single(writer.Written);
            Assert.Equal(new byte[] { 7, 0xFF, 0xFF, 0, 4, 5 }, writer.Written[0].Bytes[..6]);
        }

        [Fact]
        public void Falls_over_to_the_next_source_in_priority_order()
        {
            var writer = new RecordingWriter();
            var loop = new TelemetryLoop(
                new ITemperatureSource[] { new FailingSource(), new FakeSource("B", 60) },
                writer, TemperaturePolicy.Default);

            var r = loop.Tick(T0);

            Assert.True(r.Wrote);
            Assert.Equal("B", r.SourceName);
            Assert.Equal(60, r.Celsius);
        }

        [Fact]
        public void Reports_honestly_when_no_source_can_produce_a_reading()
        {
            var writer = new RecordingWriter();
            var loop = new TelemetryLoop(new ITemperatureSource[] { new FailingSource() }, writer, TemperaturePolicy.Default);

            var r = loop.Tick(T0);

            Assert.False(r.Wrote);
            Assert.Empty(writer.Written);
            Assert.Null(r.Celsius);
            Assert.Contains("no temperature source", r.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("no driver", r.Message);
        }

        [Fact]
        public void Never_writes_a_reading_outside_the_panels_range()
        {
            var writer = new RecordingWriter();
            var loop = new TelemetryLoop(new ITemperatureSource[] { new FakeSource("A", 900) }, writer, TemperaturePolicy.Default);

            var r = loop.Tick(T0);

            Assert.False(r.Wrote);
            Assert.Empty(writer.Written);
            Assert.Contains("range", r.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Smoothing_is_applied_before_encoding()
        {
            var policy = new TemperaturePolicy(TemperatureUnit.Celsius, SmoothingMode.ExponentialMovingAverage, 0.5, TimeSpan.FromSeconds(10));
            var writer = new RecordingWriter();
            var loop = new TelemetryLoop(new ITemperatureSource[] { new FakeSource("A", 40, 50) }, writer, policy);

            loop.Tick(T0);
            var second = loop.Tick(T0.AddSeconds(1));

            Assert.Equal(40, writer.Written[0].Celsius);
            Assert.Equal(45, second.Celsius);   // 0.5*50 + 0.5*40
        }

        [Fact]
        public void Write_only_on_change_suppresses_repeated_identical_frames()
        {
            var writer = new RecordingWriter();
            var loop = new TelemetryLoop(
                new ITemperatureSource[] { new FakeSource("A", 45, 45, 45) },
                writer, TemperaturePolicy.Default, writeOnlyOnChange: true);

            loop.Tick(T0);
            var second = loop.Tick(T0.AddSeconds(1));
            var third = loop.Tick(T0.AddSeconds(2));

            Assert.Single(writer.Written);
            Assert.False(second.Wrote);
            Assert.False(third.Wrote);
            Assert.Equal(45, third.Celsius);   // still reports what the panel is showing
        }

        [Fact]
        public void By_default_every_tick_is_written_so_the_panel_can_never_lapse()
        {
            var writer = new RecordingWriter();
            var loop = new TelemetryLoop(
                new ITemperatureSource[] { new FakeSource("A", 45, 45) },
                writer, TemperaturePolicy.Default);

            loop.Tick(T0);
            loop.Tick(T0.AddSeconds(1));

            Assert.Equal(2, writer.Written.Count);
        }

        [Fact]
        public void A_write_failure_is_reported_and_not_recorded_as_displayed()
        {
            var writer = new RecordingWriter { Succeed = false };
            var loop = new TelemetryLoop(new ITemperatureSource[] { new FakeSource("A", 45) }, writer, TemperaturePolicy.Default);

            var r = loop.Tick(T0);

            Assert.False(r.Wrote);
            Assert.Contains("device busy", r.Message);
            Assert.True(r.IsStale);   // nothing has ever been displayed successfully
        }

        [Fact]
        public void Becomes_stale_once_the_last_good_reading_ages_out()
        {
            var writer = new RecordingWriter();
            var loop = new TelemetryLoop(new ITemperatureSource[] { new FakeSource("A", 45) }, writer, TemperaturePolicy.Default);

            var first = loop.Tick(T0);
            Assert.False(first.IsStale);

            var later = loop.Tick(T0.AddSeconds(30));
            Assert.True(later.IsStale);
        }

        [Fact]
        public void Reset_forgets_displayed_state()
        {
            var writer = new RecordingWriter();
            var loop = new TelemetryLoop(
                new ITemperatureSource[] { new FakeSource("A", 45, 45) },
                writer, TemperaturePolicy.Default, writeOnlyOnChange: true);

            loop.Tick(T0);
            loop.Reset();
            var after = loop.Tick(T0.AddSeconds(1));

            Assert.True(after.Wrote);   // the duplicate-suppression memory was cleared
            Assert.Equal(2, writer.Written.Count);
        }

        [Fact]
        public void An_empty_source_list_is_handled_without_throwing()
        {
            var writer = new RecordingWriter();
            var loop = new TelemetryLoop(new ITemperatureSource[0], writer, TemperaturePolicy.Default);

            var r = loop.Tick(T0);

            Assert.False(r.Wrote);
            Assert.False(string.IsNullOrWhiteSpace(r.Message));
        }
    }
}
