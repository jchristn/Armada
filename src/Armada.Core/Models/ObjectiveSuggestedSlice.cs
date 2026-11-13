namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// A slice suggested by objective refinement as a starting point for planning: a title and, optionally, the
    /// earlier slice it builds on. Refinement never dispatches it.
    /// </summary>
    public class ObjectiveSuggestedSlice
    {
        /// <summary>Slice title.</summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>1-based index of an earlier suggested slice this one builds on, or null.</summary>
        public int? Parent { get; set; }
    }
}
