namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// A note Armada attached to a mission after it was created (RELIABILITY_SIMPLICITY_PLAN.md RS-3.4). Notes keep
    /// the mission description immutable: they are stored apart from it and rendered after it at launch.
    /// </summary>
    public class MissionNote
    {
        /// <summary>Kind of note; a mission holds at most one of each kind.</summary>
        public MissionNoteKindEnum Kind { get; set; } = MissionNoteKindEnum.PipelineHandoff;

        /// <summary>Markdown text rendered into the mission instructions.</summary>
        public string Body { get; set; } = "";

        /// <summary>When the note was last set.</summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    }
}
