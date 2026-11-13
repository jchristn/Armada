namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>What an ownership check found in a mission's changed files (warning only in v1.3.0).</summary>
    public class OwnershipFinding
    {
        /// <summary>Changed files outside the mission's owned paths.</summary>
        [Armada.Core.Services.CollectionBound(Armada.Core.Services.CollectionLimits.MaxLinkedIds, Reason = "One per changed file of one diff")]
        public List<string> OutsideOwned { get; set; } = new List<string>();

        /// <summary>Changed files owned by a concurrent mission, as "file (mission title)".</summary>
        [Armada.Core.Services.CollectionBound(Armada.Core.Services.CollectionLimits.MaxLinkedIds, Reason = "One per changed file of one diff")]
        public List<string> InConcurrentPaths { get; set; } = new List<string>();

        /// <summary>True when nothing was found.</summary>
        public bool Clean => OutsideOwned.Count == 0 && InConcurrentPaths.Count == 0;

        /// <summary>One line describing the finding, bounded to the first ten files of each kind.</summary>
        public string Describe()
        {
            List<string> parts = new List<string>();
            if (OutsideOwned.Count > 0)
                parts.Add("changed outside its owned paths: " + string.Join(", ", OutsideOwned.Take(10)) + (OutsideOwned.Count > 10 ? " and " + (OutsideOwned.Count - 10) + " more" : ""));
            if (InConcurrentPaths.Count > 0)
                parts.Add("changed paths owned by missions running alongside it: " + string.Join(", ", InConcurrentPaths.Take(10)) + (InConcurrentPaths.Count > 10 ? " and " + (InConcurrentPaths.Count - 10) + " more" : ""));
            return "Ownership: " + string.Join("; ", parts) + ".";
        }
    }
}
