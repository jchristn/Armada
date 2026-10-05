namespace Armada.Core.Models
{
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
