namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Describes a mission's title and description for voyage dispatch.
    /// Carries a mission title and description as a dedicated model.
    /// </summary>
    public class MissionDescription
    {
        #region Public-Members

        /// <summary>
        /// Mission title.
        /// </summary>
        public string Title { get; set; } = "";

        /// <summary>
        /// Mission description.
        /// </summary>
        public string Description { get; set; } = "";

        /// <summary>
        /// Optional required capability tier for dispatch routing (Economy/Standard/Premium). Null routes
        /// to any idle captain. When a preferred captain is set via <see cref="RequestedCaptainId"/>, this is
        /// the fallback tier used when that captain is busy.
        /// </summary>
        public Armada.Core.Enums.CaptainTierEnum? Tier { get; set; } = null;

        /// <summary>
        /// Optional preferred (dictated) captain identifier for this mission, referenced by captain id
        /// (cpt_ prefix). When set and idle, dispatch assigns it; when busy, dispatch falls back by
        /// <see cref="Tier"/>. Null means normal persona/tier routing.
        /// </summary>
        public string? RequestedCaptainId { get; set; } = null;

        /// <summary>
        /// Optional persona for this mission, set by in-process callers (a Mission fleet action's <c>Persona</c>). When
        /// every mission of a dispatch carries a persona and no pipeline is named, the vessel and fleet default pipelines
        /// are skipped and each mission runs as a single stage with its persona; with a single-stage pipeline the
        /// persona replaces the stage persona (the stage's review policy still applies); a multi-stage pipeline decides
        /// its own personas and this is ignored. Not part of the wire format.
        /// </summary>
        [JsonIgnore]
        public string? Persona { get; set; } = null;

        /// <summary>
        /// Mission mode, set by in-process callers (a report-only Mission fleet action uses Audit or Research). Every
        /// mission created from this description, including each pipeline stage, gets this mode. Not part of the wire
        /// format.
        /// </summary>
        [JsonIgnore]
        public Armada.Core.Enums.MissionModeEnum Mode { get; set; } = Armada.Core.Enums.MissionModeEnum.Implementation;

        /// <summary>
        /// Request-local key for this mission, so another description in the same dispatch can name it as its parent.
        /// </summary>
        public string? Key { get; set; } = null;

        /// <summary>
        /// Key of an earlier description in the same dispatch whose mission is this one's hierarchy parent (D13). The
        /// child starts when the parent's terminal pipeline stage finishes; an unknown or later key is rejected.
        /// </summary>
        public string? ParentKey { get; set; } = null;

        /// <summary>
        /// Branch strategy for a child: <c>ContinueParent</c> or <c>ForkFromParent</c>. Null applies the default rule (the
        /// first child continues the parent's branch and later children fork from its head).
        /// </summary>
        public Armada.Core.Enums.MissionBranchStrategyEnum? BranchStrategy { get; set; } = null;

        /// <summary>
        /// Files or directories this mission may change (a trailing <c>/</c> is a directory prefix). Stored on every
        /// stage and named in the captain's instructions; missions that can run at the same time should not overlap.
        /// </summary>
        [Armada.Core.Services.CollectionBound(Armada.Core.Services.CollectionLimits.MaxConfigItems)]
        public List<string>? OwnedPaths { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public MissionDescription()
        {
        }

        /// <summary>
        /// Instantiate with title and description.
        /// </summary>
        /// <param name="title">Mission title.</param>
        /// <param name="description">Mission description.</param>
        public MissionDescription(string title, string description)
        {
            Title = title ?? "";
            Description = description ?? "";
        }

        #endregion
    }
}
