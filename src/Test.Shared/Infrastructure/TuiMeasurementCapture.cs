namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Metrics;
    using System.Linq;
    using Armada.Tui.Services;

    /// <summary>
    /// Records every measurement on the <c>Armada.Tui</c> meter while alive, using a <see cref="MeterListener"/>.
    /// Measurements from any thread are captured, so assertions should match on distinctive tag values.
    /// </summary>
    public sealed class TuiMeasurementCapture : IDisposable
    {
        #region Private-Members

        private readonly MeterListener _Listener;
        private readonly List<TuiMeasurement> _Measurements = new List<TuiMeasurement>();
        private readonly object _Lock = new object();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Start listening.
        /// </summary>
        public TuiMeasurementCapture()
        {
            _Listener = new MeterListener();
            _Listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == TuiTelemetry.MeterName) listener.EnableMeasurementEvents(instrument);
            };
            _Listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) => Add(instrument, value, tags));
            _Listener.SetMeasurementEventCallback<double>((instrument, value, tags, state) => Add(instrument, value, tags));
            _Listener.Start();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Measurements captured so far on one instrument.
        /// </summary>
        /// <param name="instrument">Instrument name (for example <c>armada.tui.screen.views</c>).</param>
        /// <returns>Measurements.</returns>
        public List<TuiMeasurement> For(string instrument)
        {
            lock (_Lock)
            {
                return _Measurements.Where(m => m.Instrument == instrument).ToList();
            }
        }

        /// <summary>
        /// Stop listening.
        /// </summary>
        public void Dispose()
        {
            _Listener.Dispose();
        }

        #endregion

        #region Private-Methods

        private void Add(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            Dictionary<string, string?> map = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, object?> tag in tags) map[tag.Key] = tag.Value?.ToString();
            lock (_Lock)
            {
                _Measurements.Add(new TuiMeasurement(instrument.Name, value, map));
            }
        }

        #endregion
    }
}
