namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;
    using System.Threading;

    /// <summary>Best-effort usage telemetry on the existing Armada pipeline. No identity or error text labels.</summary>
    public static class UsageTelemetry
    {
        private static readonly Meter _Meter = new Meter(ArmadaMetrics.MeterName);
        private static readonly ActivitySource _Source = new ActivitySource(ArmadaMetrics.MeterName);
        private static readonly Counter<long> _Operations = _Meter.CreateCounter<long>("armada.usage.operations");
        private static readonly Histogram<double> _Duration = _Meter.CreateHistogram<double>("armada.usage.duration", "s");
        private static readonly Histogram<double> _Age = _Meter.CreateHistogram<double>("armada.usage.observation_age_at_capture", "s");
        private static readonly UpDownCounter<long> _Active = _Meter.CreateUpDownCounter<long>("armada.usage.active");
        private static readonly Counter<long> _Deleted = _Meter.CreateCounter<long>("armada.usage.retention.deleted");

        private static long _LastObservedTicks;
        private static readonly ObservableGauge<double> _AgeGauge = _Meter.CreateObservableGauge<double>("armada.usage.observation_age", () =>
        {
            long ticks = Interlocked.Read(ref _LastObservedTicks);
            return ticks == 0 ? Array.Empty<Measurement<double>>() : new[] { new Measurement<double>(Math.Max(0, (DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc)).TotalSeconds)) };
        }, "s");

        /// <summary>Continue only valid W3C trace context received from the Admiral.</summary>
        public static Activity? Receive(string? traceParent)
        {
            try
            {
                return ActivityContext.TryParse(traceParent, null, out ActivityContext context)
                    ? _Source.StartActivity("usage.host.receive", ActivityKind.Internal, context) : null;
            }
            catch (Exception) { return null; }
        }

        /// <summary>Begin a bounded operation. Only fixed operation names and runtime enum names may be supplied.</summary>
        public static UsageTelemetryOperation Start(string operation, string runtime = "all") => new UsageTelemetryOperation(operation, runtime);

        /// <summary>Start an activity on the source already subscribed by the server.</summary>
        internal static Activity? StartActivity(string operation)
        {
            try { return _Source.StartActivity("usage." + operation); }
            catch (Exception) { return null; }
        }

        /// <summary>Record skipped or joined scheduling decisions using a fixed outcome label.</summary>
        public static void Schedule(string outcome)
        {
            try { _Operations.Add(1, new KeyValuePair<string, object?>("operation", "schedule"), new KeyValuePair<string, object?>("outcome", outcome)); }
            catch (Exception) { }
        }

        /// <summary>Record raw or rollup retention deletions.</summary>
        public static void Deleted(string kind, int count)
        {
            try { _Deleted.Add(count, new KeyValuePair<string, object?>("kind", kind)); }
            catch (Exception) { }
        }

        /// <summary>Record observation age without any account, tenant, captain or host labels.</summary>
        public static void ObservationAge(DateTime observedUtc)
        {
            try { Interlocked.Exchange(ref _LastObservedTicks, observedUtc.Ticks); _Age.Record(Math.Max(0, (DateTime.UtcNow - observedUtc).TotalSeconds)); }
            catch (Exception) { }
        }

        internal static void Active(string operation, long delta)
        {
            try { _Active.Add(delta, new KeyValuePair<string, object?>("operation", operation)); }
            catch (Exception) { }
        }

        internal static void Finish(string operation, string runtime, string outcome, double seconds)
        {
            try
            {
                TagList tags = new TagList { { "operation", operation }, { "runtime", runtime }, { "outcome", outcome } };
                _Operations.Add(1, tags); _Duration.Record(seconds, tags);
            }
            catch (Exception) { }
        }
    }
}
