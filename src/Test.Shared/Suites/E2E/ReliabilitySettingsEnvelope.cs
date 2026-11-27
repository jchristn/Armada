namespace Test.Shared.Suites.E2E
{
    using Armada.Core.Settings;

    /// <summary>
    /// The 1.5.0 reliability settings read back from GET and PUT /api/v1/settings.
    /// </summary>
    public sealed class ReliabilitySettingsEnvelope
    {
        /// <summary>Timeout scaling reference rate.</summary>
        public int? TimeoutScaleReferenceTokensPerSecond { get; set; }

        /// <summary>Compact variant threshold.</summary>
        public int? PromptVariantCompactMaxContextTokens { get; set; }

        /// <summary>Lean variant threshold.</summary>
        public int? PromptVariantLeanMaxContextTokens { get; set; }

        /// <summary>Merge queue requeue limit.</summary>
        public int? MergeQueueRequeueLimit { get; set; }

        /// <summary>Merge queue batch size.</summary>
        public int? MergeQueueBatchSize { get; set; }

        /// <summary>Merge queue lease seconds.</summary>
        public int? MergeQueueLeaseSeconds { get; set; }

        /// <summary>Unclassified resume limit.</summary>
        public int? UnclassifiedResumeLimit { get; set; }

        /// <summary>Judge rework rounds.</summary>
        public int? JudgeReworkRounds { get; set; }

        /// <summary>Maximum escalations.</summary>
        public int? MaxEscalations { get; set; }

        /// <summary>Escalate on an open endpoint breaker.</summary>
        public bool? EscalateOnOpenEndpointBreaker { get; set; }

        /// <summary>Routing from outcome data.</summary>
        public RoutingDataSettings? RoutingData { get; set; }
    }
}
