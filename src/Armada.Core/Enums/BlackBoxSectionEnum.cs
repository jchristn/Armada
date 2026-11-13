namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A section of a BlackBoxRecord, readable on its own.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BlackBoxSectionEnum
    {
        /// <summary>
        /// Identity.
        /// </summary>
        [EnumMember(Value = "Subject")]
        Subject,

        /// <summary>
        /// Status before and after, timeline.
        /// </summary>
        [EnumMember(Value = "Status")]
        Status,

        /// <summary>
        /// Where it failed.
        /// </summary>
        [EnumMember(Value = "Site")]
        Site,

        /// <summary>
        /// How it failed.
        /// </summary>
        [EnumMember(Value = "Mechanism")]
        Mechanism,

        /// <summary>
        /// Kind, raw reason, suspected cause.
        /// </summary>
        [EnumMember(Value = "Classification")]
        Classification,

        /// <summary>
        /// Dock, worktree, branch and target state.
        /// </summary>
        [EnumMember(Value = "Workspace")]
        Workspace,

        /// <summary>
        /// Conflicted files, directories and conflicting missions.
        /// </summary>
        [EnumMember(Value = "Conflict")]
        Conflict,

        /// <summary>
        /// Scoped and out-of-scope files.
        /// </summary>
        [EnumMember(Value = "Scope")]
        Scope,

        /// <summary>
        /// Effective configuration with sources.
        /// </summary>
        [EnumMember(Value = "Configuration")]
        Configuration,

        /// <summary>
        /// Prior attempts and rescue chain.
        /// </summary>
        [EnumMember(Value = "Attempts")]
        Attempts,

        /// <summary>
        /// Evidence object index.
        /// </summary>
        [EnumMember(Value = "Evidence")]
        Evidence,

        /// <summary>
        /// Advisor findings, recommendations and recovery paths.
        /// </summary>
        [EnumMember(Value = "Analysis")]
        Analysis,

        /// <summary>
        /// Fingerprint and similar entries.
        /// </summary>
        [EnumMember(Value = "Related")]
        Related,

        /// <summary>
        /// Voyage roll-up (voyage entries only).
        /// </summary>
        [EnumMember(Value = "Voyage")]
        Voyage,

        /// <summary>
        /// Redaction report.
        /// </summary>
        [EnumMember(Value = "Redaction")]
        Redaction,

        /// <summary>
        /// Truncation report.
        /// </summary>
        [EnumMember(Value = "Truncation")]
        Truncation,

        /// <summary>
        /// The resilience analysis (analysis.resilience): self-healing, detection gap, code pointers and proposed Armada changes.
        /// </summary>
        [EnumMember(Value = "Resilience")]
        Resilience,

        /// <summary>
        /// The handoff and landing trail (handoff): ordered steps, automatic recoveries, dock transitions and target movement.
        /// </summary>
        [EnumMember(Value = "Handoff")]
        Handoff,

        /// <summary>
        /// A value this build does not recognize (written by a newer Armada); tolerant readers map unknown values here.
        /// </summary>
        [EnumMember(Value = "Unknown")]
        Unknown
    }
}
