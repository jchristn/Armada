namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Models;

    /// <summary>One row of a voyage's mission tree: a logical mission at its depth, with its later pipeline stages.</summary>
    public class MissionTreeRow
    {
        /// <summary>Depth (0 for a top-level logical mission).</summary>
        public int Depth { get; }

        /// <summary>The logical mission's root.</summary>
        public MissionSummary Mission { get; }

        /// <summary>Its later pipeline stages, in order.</summary>
        public List<MissionSummary> Stages { get; }

        /// <summary>Instantiate.</summary>
        /// <param name="depth">Depth.</param>
        /// <param name="mission">Root mission.</param>
        /// <param name="stages">Later stages.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="mission"/> is null.</exception>
        public MissionTreeRow(int depth, MissionSummary mission, List<MissionSummary>? stages)
        {
            Depth = Math.Max(0, depth);
            Mission = mission ?? throw new ArgumentNullException(nameof(mission));
            Stages = stages ?? new List<MissionSummary>();
        }
    }
}
