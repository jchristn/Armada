namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Stable code identifying a vessel health criterion.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum VesselHealthCriterionEnum
    {
        /// <summary>
        /// Commits ahead of and behind the default branch and the upstream.
        /// </summary>
        GitDivergence,

        /// <summary>
        /// Whether the working tree has modified or untracked files.
        /// </summary>
        WorkingTree,

        /// <summary>
        /// Branch count, stale branches, and leftover armada branches.
        /// </summary>
        Branches,

        /// <summary>
        /// Age of the most recent commit (informational).
        /// </summary>
        CommitRecency,

        /// <summary>
        /// Outdated package dependencies.
        /// </summary>
        Dependencies,

        /// <summary>
        /// Known-vulnerable package dependencies.
        /// </summary>
        Vulnerabilities,

        /// <summary>
        /// Presence of test projects or configuration and the latest check run outcome.
        /// </summary>
        TestInfrastructure,

        /// <summary>
        /// Presence of continuous integration configuration.
        /// </summary>
        ContinuousIntegration,

        /// <summary>
        /// Armada vessel readiness errors and warnings.
        /// </summary>
        ArmadaReadiness,

        /// <summary>
        /// Recent failed or landing-failed missions on the vessel.
        /// </summary>
        MissionOutcomes,

        /// <summary>
        /// The worst-of rollup across the scored criteria.
        /// </summary>
        Overall
    }
}
