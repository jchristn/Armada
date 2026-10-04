namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Whitelisted sort columns for vessel health enumeration.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum VesselHealthSortEnum
    {
        /// <summary>
        /// Sort by vessel name.
        /// </summary>
        VesselName,

        /// <summary>
        /// Sort by fleet name.
        /// </summary>
        FleetName,

        /// <summary>
        /// Sort by overall status rank (Fail, Warn, Pass, then Unknown and NotApplicable).
        /// </summary>
        OverallStatus,

        /// <summary>
        /// Sort by commits ahead plus commits behind the default branch.
        /// </summary>
        Divergence,

        /// <summary>
        /// Sort by commits ahead of the default branch.
        /// </summary>
        AheadOfDefault,

        /// <summary>
        /// Sort by commits behind the default branch.
        /// </summary>
        BehindDefault,

        /// <summary>
        /// Sort by whether the working tree is dirty.
        /// </summary>
        IsDirty,

        /// <summary>
        /// Sort by local branch count.
        /// </summary>
        BranchCount,

        /// <summary>
        /// Sort by stale branch count.
        /// </summary>
        StaleBranchCount,

        /// <summary>
        /// Sort by outdated dependency count.
        /// </summary>
        OutdatedCount,

        /// <summary>
        /// Sort by count of dependencies with major drift.
        /// </summary>
        OutdatedMajorCount,

        /// <summary>
        /// Sort by vulnerable dependency count.
        /// </summary>
        VulnerableCount,

        /// <summary>
        /// Sort by dependency status rank.
        /// </summary>
        DependencyStatus,

        /// <summary>
        /// Sort by test infrastructure status rank.
        /// </summary>
        TestInfraStatus,

        /// <summary>
        /// Sort by continuous integration status rank.
        /// </summary>
        CiStatus,

        /// <summary>
        /// Sort by last commit timestamp.
        /// </summary>
        LastCommitUtc,

        /// <summary>
        /// Sort by last evaluation timestamp.
        /// </summary>
        EvaluatedUtc
    }
}
