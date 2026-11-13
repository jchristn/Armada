namespace Armada.Server
{
    using System.Collections.Generic;
    using Armada.Core.Settings;

    /// <summary>
    /// The members of a <c>Concurrency</c> settings update that the caller actually sent. Omitted members keep their
    /// current values. Values are clamped to their bounds, never rejected (section 4.7).
    /// </summary>
    public sealed class ConcurrencyPatch
    {
        #region Public-Members

        /// <summary>Default simultaneous voyages on a new vessel, when sent.</summary>
        public int? DefaultVesselMaxParallelVoyages { get; set; }

        /// <summary>Default simultaneous missions per voyage on a new vessel, when sent.</summary>
        public int? DefaultVesselMaxParallelMissionsPerVoyage { get; set; }

        /// <summary>Default CLI captain slots, when sent.</summary>
        public int? DefaultCliCaptainMaxConcurrency { get; set; }

        /// <summary>Default endpoint and Mux captain slots, when sent.</summary>
        public int? DefaultEndpointCaptainMaxConcurrency { get; set; }

        /// <summary>Server ceiling on captain slots, when sent.</summary>
        public int? MaxCaptainConcurrency { get; set; }

        /// <summary>Maximum hierarchy depth, when sent.</summary>
        public int? MaxHierarchyDepth { get; set; }

        /// <summary>Minutes before a waiting mission is reported as starving, when sent.</summary>
        public int? StarvationWarningMinutes { get; set; }

        /// <summary>Admission lock timeout in seconds, when sent.</summary>
        public int? AdmissionLockTimeoutSeconds { get; set; }

        /// <summary>Days closed wait intervals are kept, when sent.</summary>
        public int? WaitIntervalRetentionDays { get; set; }

        #endregion

        #region Public-Methods

        /// <summary>Apply every sent member to <paramref name="current"/> in place, then normalize cross-field bounds.</summary>
        /// <param name="current">The live settings group; services read it on every admission.</param>
        /// <param name="changed">True when any effective value changed.</param>
        /// <returns>Clamping adjustments, for the caller to report.</returns>
        /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="current"/> is null.</exception>
        public IReadOnlyList<string> ApplyTo(ConcurrencySettings current, out bool changed)
        {
            if (current == null) throw new System.ArgumentNullException(nameof(current));
            string before = Fingerprint(current);
            // The ceiling goes first so the captain defaults clamp against the new value.
            if (MaxCaptainConcurrency.HasValue) current.MaxCaptainConcurrency = MaxCaptainConcurrency.Value;
            if (DefaultVesselMaxParallelVoyages.HasValue) current.DefaultVesselMaxParallelVoyages = DefaultVesselMaxParallelVoyages.Value;
            if (DefaultVesselMaxParallelMissionsPerVoyage.HasValue) current.DefaultVesselMaxParallelMissionsPerVoyage = DefaultVesselMaxParallelMissionsPerVoyage.Value;
            if (DefaultCliCaptainMaxConcurrency.HasValue) current.DefaultCliCaptainMaxConcurrency = DefaultCliCaptainMaxConcurrency.Value;
            if (DefaultEndpointCaptainMaxConcurrency.HasValue) current.DefaultEndpointCaptainMaxConcurrency = DefaultEndpointCaptainMaxConcurrency.Value;
            if (MaxHierarchyDepth.HasValue) current.MaxHierarchyDepth = MaxHierarchyDepth.Value;
            if (StarvationWarningMinutes.HasValue) current.StarvationWarningMinutes = StarvationWarningMinutes.Value;
            if (AdmissionLockTimeoutSeconds.HasValue) current.AdmissionLockTimeoutSeconds = AdmissionLockTimeoutSeconds.Value;
            if (WaitIntervalRetentionDays.HasValue) current.WaitIntervalRetentionDays = WaitIntervalRetentionDays.Value;
            IReadOnlyList<string> adjustments = current.Normalize();
            changed = before != Fingerprint(current);
            return adjustments;
        }

        #endregion

        #region Private-Methods

        private static string Fingerprint(ConcurrencySettings s) => string.Join(",",
            s.DefaultVesselMaxParallelVoyages, s.DefaultVesselMaxParallelMissionsPerVoyage, s.DefaultCliCaptainMaxConcurrency,
            s.DefaultEndpointCaptainMaxConcurrency, s.MaxCaptainConcurrency, s.MaxHierarchyDepth, s.StarvationWarningMinutes,
            s.AdmissionLockTimeoutSeconds, s.WaitIntervalRetentionDays);

        #endregion
    }
}
