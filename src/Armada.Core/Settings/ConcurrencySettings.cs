namespace Armada.Core.Settings
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// Bounds and defaults for v1.3 concurrency. This foundation persists configuration; the new per-captain,
    /// per-voyage and hierarchy admission behavior is enabled by the subsequent implementation milestones.
    /// </summary>
    public sealed class ConcurrencySettings
    {
        private readonly List<string> _Adjustments = new List<string>();
        private int _Voyages = Constants.DefaultVesselMaxParallelVoyages;
        private int _Missions = Constants.DefaultVesselMaxParallelMissionsPerVoyage;
        private int _Cli = Constants.DefaultCliCaptainMaxConcurrency;
        private int _Endpoint = Constants.DefaultEndpointCaptainMaxConcurrency;
        private int _Maximum = Constants.CaptainConcurrencyCeiling;
        private int _Depth = Constants.DefaultMaxHierarchyDepth;
        private int _Starvation = Constants.DefaultStarvationWarningMinutes;
        private int _Timeout = Constants.DefaultAdmissionLockTimeoutSeconds;
        private int _Retention = Constants.DefaultWaitIntervalRetentionDays;

        /// <summary>Default simultaneous voyages on a new vessel, 0..1000. Zero means unlimited.</summary>
        public int DefaultVesselMaxParallelVoyages { get => _Voyages; set => _Voyages = Record(nameof(DefaultVesselMaxParallelVoyages), value, ConcurrencyClamp.VesselLimit(value)); }
        /// <summary>Default simultaneous missions per voyage, 0..1000. Zero means unlimited.</summary>
        public int DefaultVesselMaxParallelMissionsPerVoyage { get => _Missions; set => _Missions = Record(nameof(DefaultVesselMaxParallelMissionsPerVoyage), value, ConcurrencyClamp.VesselLimit(value)); }
        /// <summary>Default CLI captain slots, 1..MaxCaptainConcurrency after normalization.</summary>
        public int DefaultCliCaptainMaxConcurrency { get => _Cli; set => _Cli = Record(nameof(DefaultCliCaptainMaxConcurrency), value, ConcurrencyClamp.CaptainLimit(value)); }
        /// <summary>Default endpoint/Mux captain slots, 1..MaxCaptainConcurrency after normalization.</summary>
        public int DefaultEndpointCaptainMaxConcurrency { get => _Endpoint; set => _Endpoint = Record(nameof(DefaultEndpointCaptainMaxConcurrency), value, ConcurrencyClamp.CaptainLimit(value)); }
        /// <summary>Server ceiling on captain slots, 1..256.</summary>
        public int MaxCaptainConcurrency { get => _Maximum; set => _Maximum = Record(nameof(MaxCaptainConcurrency), value, ConcurrencyClamp.CaptainLimit(value)); }
        /// <summary>Maximum hierarchy depth, 1..16. Defaults to four.</summary>
        public int MaxHierarchyDepth { get => _Depth; set => _Depth = Record(nameof(MaxHierarchyDepth), value, ConcurrencyClamp.Finite(value, 1, Constants.HierarchyDepthCeiling)); }
        /// <summary>Wait before reporting starvation, 1..10080 minutes.</summary>
        public int StarvationWarningMinutes { get => _Starvation; set => _Starvation = Record(nameof(StarvationWarningMinutes), value, ConcurrencyClamp.Finite(value, 1, Constants.StarvationWarningMinutesCeiling)); }
        /// <summary>Admission lock timeout, 1..600 seconds.</summary>
        public int AdmissionLockTimeoutSeconds { get => _Timeout; set => _Timeout = Record(nameof(AdmissionLockTimeoutSeconds), value, ConcurrencyClamp.Finite(value, 1, Constants.AdmissionLockTimeoutSecondsCeiling)); }
        /// <summary>Wait-interval retention, 1..3650 days.</summary>
        public int WaitIntervalRetentionDays { get => _Retention; set => _Retention = Record(nameof(WaitIntervalRetentionDays), value, ConcurrencyClamp.Finite(value, 1, Constants.WaitIntervalRetentionDaysCeiling)); }

        /// <summary>
        /// Apply cross-field bounds after all JSON properties have been read, regardless of property order.
        /// Returns and drains adjustment warnings so file loading or API callers can log them at the appropriate level.
        /// </summary>
        public IReadOnlyList<string> Normalize()
        {
            _Cli = Record(nameof(DefaultCliCaptainMaxConcurrency), _Cli, ConcurrencyClamp.CaptainLimit(_Cli, _Maximum));
            _Endpoint = Record(nameof(DefaultEndpointCaptainMaxConcurrency), _Endpoint, ConcurrencyClamp.CaptainLimit(_Endpoint, _Maximum));
            string[] warnings = _Adjustments.ToArray();
            _Adjustments.Clear();
            return warnings;
        }

        private int Record(string name, int given, int effective)
        {
            if (given != effective) _Adjustments.Add("Concurrency." + name + " clamped from " + given + " to " + effective + ".");
            return effective;
        }
    }
}
