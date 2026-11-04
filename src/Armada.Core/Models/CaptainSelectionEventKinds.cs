namespace Armada.Core.Models
{
    /// <summary>Stable persisted execution event kinds. Values are stored and returned verbatim; never rename them.</summary>
    public static class CaptainSelectionEventKinds
    {
        /// <summary>The admitted attempt's process launched.</summary>
        public const string ExecutionStarted = "ExecutionStarted";

        /// <summary>The runtime reported the model it actually used.</summary>
        public const string ActualModelReported = "ActualModelReported";

        /// <summary>An administrator recovery authorization was applied to the admission.</summary>
        public const string RecoveryAuthorizationApplied = "RecoveryAuthorizationApplied";

        /// <summary>Durable evidence proved the attempt never launched, so its claims were released.</summary>
        public const string ProvenUnstarted = "ProvenUnstarted";

        /// <summary>A classified provider quota or rate-limit cooldown was recorded for the attempt's account.</summary>
        public const string ProviderCooldownObserved = "ProviderCooldownObserved";
    }
}
