namespace Armada.Gate
{
    using System;

    /// <summary>
    /// The outcome counts of one reference pipeline's battery run.
    /// </summary>
    public sealed class GatePipelineResult
    {
        /// <summary>Pipeline name.</summary>
        public string Pipeline { get; set; } = String.Empty;

        /// <summary>Battery run id.</summary>
        public string? RunId { get; set; }

        /// <summary>Final run status.</summary>
        public string Status { get; set; } = String.Empty;

        /// <summary>Tasks graded Passed.</summary>
        public int Passed { get; set; }

        /// <summary>Tasks graded Failed.</summary>
        public int Failed { get; set; }

        /// <summary>Tasks that could not run (toolchain missing), excluded from G1.</summary>
        public int CouldNotRun { get; set; }

        /// <summary>Tasks waiting for a rubric grade, excluded from G1.</summary>
        public int PendingEvaluation { get; set; }
    }
}
