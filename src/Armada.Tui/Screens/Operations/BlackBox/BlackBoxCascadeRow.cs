namespace Armada.Tui.Screens.Operations
{
    using System;
    using Armada.Core.Models;

    /// <summary>
    /// One row of the voyage Cascade tab: a cascade node, its depth in the tree, and the edge it was reached by.
    /// </summary>
    public class BlackBoxCascadeRow
    {
        /// <summary>
        /// Depth (0 for a root).
        /// </summary>
        public int Depth { get; }

        /// <summary>
        /// Node.
        /// </summary>
        public BlackBoxCascadeNode Node { get; }

        /// <summary>
        /// The edge from the parent row, or null for a root.
        /// </summary>
        public BlackBoxCascadeEdge? Via { get; }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="depth">Depth.</param>
        /// <param name="node">Node.</param>
        /// <param name="via">Edge from the parent, or null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="node"/> is null.</exception>
        public BlackBoxCascadeRow(int depth, BlackBoxCascadeNode node, BlackBoxCascadeEdge? via)
        {
            Depth = Math.Max(0, depth);
            Node = node ?? throw new ArgumentNullException(nameof(node));
            Via = via;
        }
    }
}
