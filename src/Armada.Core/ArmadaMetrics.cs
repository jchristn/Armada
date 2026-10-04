namespace Armada.Core
{
    using System.Diagnostics.Metrics;

    /// <summary>
    /// Armada's telemetry instruments. Emitting a measurement rides the .NET base class library
    /// (<see cref="Meter"/>) and costs nothing until a host subscribes, so Armada.Core takes no
    /// dependency on any telemetry framework. A host (see the server's telemetry wiring) observes
    /// <see cref="MeterName"/> and exports to Prometheus/Grafana/Loki. Instruments focus on the
    /// reliability signals that matter operationally: mission failures, dock churn, stalls,
    /// recoveries, review timeouts, handoff repair, and merge-queue processing. Units are left unset
    /// so the OpenTelemetry Prometheus exporter yields clean "armada_*_total" series names.
    /// </summary>
    public static class ArmadaMetrics
    {
        /// <summary>
        /// The meter name a telemetry host subscribes to.
        /// </summary>
        public const string MeterName = "Armada";

        private static readonly Meter _Meter = new Meter(MeterName);

        /// <summary>Missions that reached Failed.</summary>
        public static readonly Counter<long> MissionsFailed =
            _Meter.CreateCounter<long>("armada.missions.failed", null, "Missions failed");

        /// <summary>Docks provisioned.</summary>
        public static readonly Counter<long> DocksProvisioned =
            _Meter.CreateCounter<long>("armada.docks.provisioned", null, "Docks provisioned");

        /// <summary>Docks reclaimed.</summary>
        public static readonly Counter<long> DocksReclaimed =
            _Meter.CreateCounter<long>("armada.docks.reclaimed", null, "Docks reclaimed");

        /// <summary>Captain stalls detected.</summary>
        public static readonly Counter<long> CaptainStalls =
            _Meter.CreateCounter<long>("armada.captains.stalls", null, "Captain stalls detected");

        /// <summary>Captain recovery attempts.</summary>
        public static readonly Counter<long> CaptainRecoveries =
            _Meter.CreateCounter<long>("armada.captains.recoveries", null, "Captain recovery attempts");

        /// <summary>Missions force-failed for exceeding max runtime.</summary>
        public static readonly Counter<long> MissionRuntimeExceeded =
            _Meter.CreateCounter<long>("armada.missions.runtime_exceeded", null, "Missions force-failed for exceeding max runtime");

        /// <summary>Reviews released after going overdue.</summary>
        public static readonly Counter<long> ReviewsOverdue =
            _Meter.CreateCounter<long>("armada.reviews.overdue", null, "Reviews released after going overdue");

        /// <summary>Dangling pipeline handoffs re-driven.</summary>
        public static readonly Counter<long> HandoffsRedriven =
            _Meter.CreateCounter<long>("armada.handoffs.redriven", null, "Dangling pipeline handoffs re-driven");

        /// <summary>Merge-queue entries processed to a terminal state.</summary>
        public static readonly Counter<long> MergeEntriesProcessed =
            _Meter.CreateCounter<long>("armada.mergequeue.processed", null, "Merge-queue entries processed");

        /// <summary>
        /// Fleet action run targets that reached a terminal state, exported as
        /// armada_fleet_action_targets_total. Tags: kind (Command or Mission) and outcome (the terminal target
        /// status: Succeeded, Failed, TimedOut, Skipped, Cancelled). No vessel, run, or command values are tagged.
        /// </summary>
        public static readonly Counter<long> FleetActionTargets =
            _Meter.CreateCounter<long>("armada.fleet_action.targets", null, "Fleet action targets finished");

        /// <summary>
        /// Wall-clock duration of fleet action targets that executed, exported as
        /// armada_fleet_action_target_duration_seconds. Tag: kind (Command or Mission).
        /// </summary>
        public static readonly Histogram<double> FleetActionTargetDuration =
            _Meter.CreateHistogram<double>("armada.fleet_action.target_duration", "s", "Fleet action target duration");
    }
}
