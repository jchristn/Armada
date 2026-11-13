namespace Armada.Core
{
    using System.Diagnostics.Metrics;
    using Armada.Core.Services;

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

        // Wait histograms share one bucket layout from 1 second to 2 hours.
        private static readonly InstrumentAdvice<double> _WaitBuckets = new InstrumentAdvice<double>
        {
            HistogramBucketBoundaries = new double[] { 1, 5, 15, 30, 60, 120, 300, 600, 1800, 3600, 7200 }
        };

        /// <summary>Bounded admission waits that timed out; gate is vessel or capacity.</summary>
        public static readonly Counter<long> AdmissionLockTimeouts =
            _Meter.CreateCounter<long>("armada.admission.lock_timeouts");

        /// <summary>New or changed capacity overruns, tagged only by gate and observed cause.</summary>
        public static readonly Counter<long> ConcurrencyLimitViolations =
            _Meter.CreateCounter<long>("armada.concurrency.limit_violations");

        /// <summary>Pending missions observed ready before/after three consecutive dispatch opportunities.</summary>
        public static readonly Counter<long> AdmissionStuck = _Meter.CreateCounter<long>("armada.admission.stuck");

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

        /// <summary>Vessel health evaluations, one per vessel, labeled by outcome (Succeeded, Failed, Cancelled).</summary>
        public static readonly Counter<long> HealthEvaluations =
            _Meter.CreateCounter<long>("armada.health.evaluations", null, "Vessel health evaluations");

        /// <summary>Duration in seconds of each vessel health criterion evaluation, labeled by criterion.</summary>
        public static readonly Histogram<double> HealthCriterionDuration =
            _Meter.CreateHistogram<double>("armada.health.criterion_duration_seconds", null, "Vessel health criterion duration in seconds");

        /// <summary>
        /// Vessel import items processed by an import, exported as armada_vessel_import_items_total. Tag: outcome
        /// (Created, SkippedExisting, SkippedNotSelected, Failed). No paths or vessel identifiers are tagged.
        /// </summary>
        public static readonly Counter<long> VesselImportItems =
            _Meter.CreateCounter<long>("armada.vessel_import.items", null, "Vessel import items processed");

        /// <summary>
        /// Time from a mission's first capacity-ready observation to its assignment, exported as
        /// armada_missions_slot_wait_seconds. Tags: gate_last (the last capacity gate before assignment, or None) and persona.
        /// </summary>
        public static readonly Histogram<double> SlotWaitSeconds =
            _Meter.CreateHistogram<double>("armada.missions.slot_wait_seconds", "s", "Mission slot wait", null, _WaitBuckets);

        /// <summary>Time a mission spent Pending before assignment, exported as armada_missions_queue_seconds. Tag: persona.</summary>
        public static readonly Histogram<double> QueueSeconds =
            _Meter.CreateHistogram<double>("armada.missions.queue_seconds", "s", "Mission queue time", null, _WaitBuckets);

        /// <summary>One observation per closed wait interval, exported as armada_missions_gate_wait_seconds. Tag: gate.</summary>
        public static readonly Histogram<double> GateWaitSeconds =
            _Meter.CreateHistogram<double>("armada.missions.gate_wait_seconds", "s", "Mission wait per gate", null, _WaitBuckets);

        /// <summary>Time spent acquiring an admission lock, exported as armada_admission_lock_wait_seconds. Tag: lock (vessel or capacity).</summary>
        public static readonly Histogram<double> AdmissionLockWaitSeconds =
            _Meter.CreateHistogram<double>("armada.admission.lock_wait_seconds", "s", "Admission lock wait", null,
                new InstrumentAdvice<double> { HistogramBucketBoundaries = new double[] { 0.001, 0.005, 0.01, 0.05, 0.1, 0.5, 1, 5, 15, 30 } });

        /// <summary>Missions that waited longer than Concurrency.StarvationWarningMinutes, once per wait. Tag: gate.</summary>
        public static readonly Counter<long> MissionsStarving =
            _Meter.CreateCounter<long>("armada.missions.starving", null, "Missions waiting past the starvation threshold");

        /// <summary>Pending missions by their current wait gate.</summary>
        public static readonly ObservableGauge<int> MissionsWaiting =
            _Meter.CreateObservableGauge<int>("armada.missions.waiting", () => ConcurrencyGaugeState.Waiting(), null, "Pending missions by wait gate");

        /// <summary>Assigned or running missions by runtime.</summary>
        public static readonly ObservableGauge<int> MissionsActive =
            _Meter.CreateObservableGauge<int>("armada.missions.active", () => ConcurrencyGaugeState.ByRuntime(s => s.ActiveByRuntime), null, "Active missions by runtime");

        /// <summary>Occupied captain mission slots by runtime.</summary>
        public static readonly ObservableGauge<int> CaptainSlotsInUse =
            _Meter.CreateObservableGauge<int>("armada.captains.slots_in_use", () => ConcurrencyGaugeState.ByRuntime(s => s.SlotsInUseByRuntime), null, "Captain slots in use by runtime");

        /// <summary>Configured captain mission slots by runtime.</summary>
        public static readonly ObservableGauge<int> CaptainSlotsTotal =
            _Meter.CreateObservableGauge<int>("armada.captains.slots_total", () => ConcurrencyGaugeState.ByRuntime(s => s.SlotsTotalByRuntime), null, "Captain slots by runtime");

        /// <summary>Voyage slots held, summed across vessels with a voyage limit.</summary>
        public static readonly ObservableGauge<int> VesselVoyageSlotsInUse =
            _Meter.CreateObservableGauge<int>("armada.vessels.voyage_slots_in_use", () => ConcurrencyGaugeState.Value(s => s.VoyageSlotsInUse), null, "Vessel voyage slots in use");

        /// <summary>Voyage slot limits, summed across vessels with a limit; unlimited vessels are counted separately.</summary>
        public static readonly ObservableGauge<int> VesselVoyageSlotsTotal =
            _Meter.CreateObservableGauge<int>("armada.vessels.voyage_slots_total", () => ConcurrencyGaugeState.Value(s => s.VoyageSlotsTotal), null, "Vessel voyage slots");

        /// <summary>Vessels with no voyage limit.</summary>
        public static readonly ObservableGauge<int> VesselsUnlimited =
            _Meter.CreateObservableGauge<int>("armada.vessels.unlimited", () => ConcurrencyGaugeState.Value(s => s.UnlimitedVessels), null, "Vessels without a voyage limit");
    }
}
