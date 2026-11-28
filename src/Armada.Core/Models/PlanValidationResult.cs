namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>The outcome of <see cref="Services.MissionPlanValidator.Validate"/>.</summary>
    public sealed class PlanValidationResult
    {
        /// <summary>Broken rules that reject the plan.</summary>
        public List<PlanFinding> Errors { get; } = new List<PlanFinding>();

        /// <summary>Findings that do not reject the plan.</summary>
        public List<PlanFinding> Warnings { get; } = new List<PlanFinding>();

        /// <summary>True when there are no errors.</summary>
        public bool IsValid => Errors.Count == 0;

        /// <summary>Resolved 1-based parent per 1-based position (0 for a root), valid only when <see cref="IsValid"/>.</summary>
        internal int[] Parents { get; set; } = new int[0];

        /// <summary>Depth per 1-based position (0 for a root).</summary>
        internal int[] Depths { get; set; } = new int[0];

        /// <summary>The 1-based parent of a mission, or null for a root.</summary>
        /// <param name="position">1-based position.</param>
        /// <returns>Parent position or null.</returns>
        public int? ParentOf(int position) => position >= 1 && position < Parents.Length && Parents[position] > 0 ? Parents[position] : null;

        /// <summary>Hierarchy depth of a mission (0 for a root).</summary>
        /// <param name="position">1-based position.</param>
        /// <returns>Depth.</returns>
        public int DepthOf(int position) => position >= 1 && position < Depths.Length ? Depths[position] : 0;

        /// <summary>The errors as one sentence list.</summary>
        /// <returns>Errors joined with "; ".</returns>
        public string Describe() => string.Join("; ", Errors.Select(e => e.Message));
    }
}
