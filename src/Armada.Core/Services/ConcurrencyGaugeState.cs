namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Metrics;
    using Armada.Core.Enums;

    /// <summary>
    /// Cached values behind Armada's observable concurrency gauges. Gauge callbacks read only this state and never
    /// query the database; the invariant monitor and the wait tracker publish to it.
    /// </summary>
    internal static class ConcurrencyGaugeState
    {
        private static volatile ConcurrencyGaugeSnapshot? _Snapshot;
        private static volatile Func<Dictionary<MissionWaitGateEnum, int>>? _Waiting;

        /// <summary>Publish the latest capacity snapshot.</summary>
        internal static void Publish(ConcurrencyGaugeSnapshot snapshot) => _Snapshot = snapshot;

        /// <summary>Register the source of waiting-mission counts.</summary>
        internal static void PublishWaiting(Func<Dictionary<MissionWaitGateEnum, int>> source) => _Waiting = source;

        internal static IEnumerable<Measurement<int>> Waiting()
        {
            Func<Dictionary<MissionWaitGateEnum, int>>? source = _Waiting;
            if (source == null) yield break;
            foreach (KeyValuePair<MissionWaitGateEnum, int> pair in source())
                yield return new Measurement<int>(pair.Value, new KeyValuePair<string, object?>("gate", pair.Key.ToString()));
        }

        internal static IEnumerable<Measurement<int>> ByRuntime(Func<ConcurrencyGaugeSnapshot, Dictionary<string, int>> select)
        {
            ConcurrencyGaugeSnapshot? snapshot = _Snapshot;
            if (snapshot == null) yield break;
            foreach (KeyValuePair<string, int> pair in select(snapshot))
                yield return new Measurement<int>(pair.Value, new KeyValuePair<string, object?>("runtime", pair.Key));
        }

        internal static IEnumerable<Measurement<int>> Value(Func<ConcurrencyGaugeSnapshot, int> select)
        {
            ConcurrencyGaugeSnapshot? snapshot = _Snapshot;
            if (snapshot == null) yield break;
            yield return new Measurement<int>(select(snapshot));
        }
    }
}
