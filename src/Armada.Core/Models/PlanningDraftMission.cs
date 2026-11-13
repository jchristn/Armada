namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>One mission of a multi-mission planning dispatch draft.</summary>
    public class PlanningDraftMission
    {
        /// <summary>Optional key naming the mission so a later one can name it as <see cref="ParentKey"/>.</summary>
        public string? Key { get; set; }

        /// <summary>Optional key of an earlier mission in the draft that this one builds on.</summary>
        public string? ParentKey { get; set; }

        /// <summary>Mission title.</summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>Mission-ready description.</summary>
        public string Description { get; set; } = String.Empty;

        /// <summary>Files or directories the mission may change.</summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<string> OwnedPaths { get; set; } = new List<string>();
    }
}
