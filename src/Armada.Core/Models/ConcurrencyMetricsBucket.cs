namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>One time bucket of a concurrency metrics report.</summary>
    public class ConcurrencyMetricsBucket
    {
        /// <summary>Bucket start.</summary>
        public DateTime StartUtc { get; set; }

        /// <summary>Missions waiting on any gate at some point in the bucket.</summary>
        public int MissionsWaiting { get; set; }

        /// <summary>Wait seconds by gate within the bucket; gates without waits are omitted.</summary>
        [CollectionBound(16, Reason = "One per MissionWaitGateEnum value")]
        public List<GateWaitTotal> ByGate { get; set; } = new List<GateWaitTotal>();
    }
}
