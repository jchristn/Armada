namespace Armada.Core.Models
{
    using Armada.Core.Services;
    using Armada.Core.Enums;

    /// <summary>Explicit availability and configuration-source metadata for usage selection controls.</summary>
    public sealed class UsageSelectionCapabilities
    {
        /// <summary>Available strategy names; none bypass safety or capacity gates.</summary>
        [CollectionBound(3)]
        public CaptainSelectionStrategyEnum[] Strategies { get; set; } = new[] { CaptainSelectionStrategyEnum.FirstAvailable, CaptainSelectionStrategyEnum.MostRemainingAllowance, CaptainSelectionStrategyEnum.MostUsedAllowance };
        /// <summary>Observe integration is available.</summary>
        public bool ObserveAvailable { get; set; } = true;
        /// <summary>True only after atomic budget enforcement is integrated.</summary>
        public bool EnforcementAvailable { get; set; }
        /// <summary>File-backed group updates affect future admissions without restart.</summary>
        public bool AppliedLive { get; set; } = true;
        /// <summary>Environment-supplied policy wins and cannot be overwritten through APIs.</summary>
        public bool EnvironmentOverride { get; set; }
        /// <summary>Effective source: SettingsFile or Environment.</summary>
        public string Source { get; set; } = "SettingsFile";
    }
}
