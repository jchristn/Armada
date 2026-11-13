namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Where in a mission's or voyage's life a black box failure happened. Derived from the step (BlackBoxTaxonomy.PhaseOf); see docs/BLACKBOX.md for the taxonomy.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BlackBoxPhaseEnum
    {
        /// <summary>
        /// The phase could not be determined (for example a sweeper capture with no step).
        /// </summary>
        [EnumMember(Value = "Unknown")]
        Unknown,

        /// <summary>
        /// Assigning the mission to a captain.
        /// </summary>
        [EnumMember(Value = "Dispatch")]
        Dispatch,

        /// <summary>
        /// Creating the dock, worktree and branch.
        /// </summary>
        [EnumMember(Value = "Provision")]
        Provision,

        /// <summary>
        /// The captain's process run.
        /// </summary>
        [EnumMember(Value = "Execution")]
        Execution,

        /// <summary>
        /// Completion gates after the captain finished: no-op, scope, boundary, definition of done, stage checks.
        /// </summary>
        [EnumMember(Value = "Completion")]
        Completion,

        /// <summary>
        /// Judge or human review.
        /// </summary>
        [EnumMember(Value = "Review")]
        Review,

        /// <summary>
        /// Handing work between pipeline stages.
        /// </summary>
        [EnumMember(Value = "Handoff")]
        Handoff,

        /// <summary>
        /// Landing the mission's work on the target branch.
        /// </summary>
        [EnumMember(Value = "Landing")]
        Landing,

        /// <summary>
        /// Processing the mission's merge-queue entry.
        /// </summary>
        [EnumMember(Value = "MergeQueue")]
        MergeQueue,

        /// <summary>
        /// Recovery, rescue and redispatch handling.
        /// </summary>
        [EnumMember(Value = "Recovery")]
        Recovery,

        /// <summary>
        /// Voyage settlement, halt or cancellation.
        /// </summary>
        [EnumMember(Value = "Voyage")]
        Voyage,

        /// <summary>
        /// Changes made outside the mission flow (vessel deleted, operator transition).
        /// </summary>
        [EnumMember(Value = "External")]
        External
    }
}
