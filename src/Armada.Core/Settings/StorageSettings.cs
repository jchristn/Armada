namespace Armada.Core.Settings
{
    using System;

    /// <summary>Bounded background storage measurement settings. Changes apply to subsequent collections.</summary>
    public class StorageSettings
    {
        /// <summary>Seconds between samples; default 300, range 30..86400.</summary>
        public int IntervalSeconds { get => _IntervalSeconds; set => _IntervalSeconds = Math.Clamp(value, 30, 86400); }
        private int _IntervalSeconds = 300;

        /// <summary>Per-stage collection budget in seconds; default 30, range 1..300.</summary>
        public int TimeoutSeconds { get => _TimeoutSeconds; set => _TimeoutSeconds = Math.Clamp(value, 1, 300); }
        private int _TimeoutSeconds = 30;

        /// <summary>Maximum examined filesystem entries per host; default 250000, range 100..2000000.</summary>
        public int MaxEntries { get => _MaxEntries; set => _MaxEntries = Math.Clamp(value, 100, 2000000); }
        private int _MaxEntries = 250000;

        /// <summary>Largest files retained per location; default 20, range 1..100.</summary>
        public int LargestFileLimit { get => _LargestFileLimit; set => _LargestFileLimit = Math.Clamp(value, 1, 100); }
        private int _LargestFileLimit = 20;

        /// <summary>Process-local samples; default 288, range 2..1000 (CollectionLimits.MaxSummaryBuckets; larger values are normalized to 1000).</summary>
        public int HistoryCapacity { get => _HistoryCapacity; set => _HistoryCapacity = Math.Clamp(value, 2, Armada.Core.Services.CollectionLimits.MaxSummaryBuckets); }
        private int _HistoryCapacity = 288;

        /// <summary>Maximum reported locations per host; default 1000, range 10..5000.</summary>
        public int MaxLocations { get => _MaxLocations; set => _MaxLocations = Math.Clamp(value, 10, 5000); }
        private int _MaxLocations = 1000;

    }
}
